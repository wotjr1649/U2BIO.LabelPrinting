using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using U2BIO.BT003PROLabelPrinting;

namespace U2BIO.BT003PROLabelPrinting.Validation
{
    internal static class PrinterConnectionTests
    {
        private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Owned connection contract failed."); }
        internal sealed class Fixture
        {
            internal readonly List<string> Calls = new List<string>();
            internal readonly ManualResetEventSlim Entered = new ManualResetEventSlim();
            internal readonly ManualResetEventSlim Release = new ManualResetEventSlim();
            internal string Mode;
            internal int Port, OwnerThread;
            internal ushort Type;
            internal string Name, Ip;
            private void Add(string value)
            {
                int current = Thread.CurrentThread.ManagedThreadId;
                if (OwnerThread == 0) OwnerThread = current;
                Check(OwnerThread == current);
                lock (Calls) Calls.Add(value);
            }
            internal void Load() { Add("Load"); if (Mode == "load-throw") throw new InvalidOperationException("synthetic"); }
            internal int Set(ushort type, string name, string ip) { Add("Set"); Type = type; Name = name; Ip = ip; return Mode == "conflict" ? 1 : Mode == "set-fail" ? 9 : 0; }
            internal int Connect()
            {
                Add("Connect"); Port = 1;
                if (Mode == "pending") { Entered.Set(); Check(Release.Wait(5000)); }
                return Mode == "connect-fail" ? 9 : 0;
            }
            internal int Status() { Add("Status"); return Mode == "conflict" ? Port : Port; }
            internal int Disconnect() { Add("Disconnect"); if (Mode != "stay-open") Port = 0; return 7; }
            internal void Reset() { Add("Reset"); }
            internal int Form(string form) { Add("Form"); return 0; }
            internal void Print(ushort copies, IntPtr pointer) { throw new InvalidOperationException("Connection tests must not print."); }
            internal Bt003Printer Printer(int milliseconds = 1000)
            {
                var assembly = typeof(Bt003ConnectionOptions).Assembly;
                var type = assembly.GetType("U2BIO.BT003PROLabelPrinting.Native.PrinterSdkCalls", true);
                var constructor = type.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
                var methods = new[] { "Load", "Set", "Connect", "Status", "Disconnect", "Reset", "Form", "Print" };
                object calls = constructor.Invoke(constructor.GetParameters().Select((p, i) => (object)Delegate.CreateDelegate(
                    p.ParameterType, this, GetType().GetMethod(methods[i], BindingFlags.NonPublic | BindingFlags.Instance))).ToArray());
                return (Bt003Printer)Activator.CreateInstance(typeof(Bt003Printer), BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new[] { (object)TimeSpan.FromMilliseconds(milliseconds), calls }, null);
            }
        }
        internal static void Run()
        {
            foreach (var target in new[] { Bt003ConnectionOptions.Usb("synthetic-usb"), Bt003ConnectionOptions.Serial("COM3"), Bt003ConnectionOptions.Tcp("192.0.2.1", 9100) }) {
                var f = new Fixture();
                using (var p = f.Printer()) {
                    Check(p.Connect(target).IsSuccess);
                    Check(f.Name == target.Port && f.Ip == target.IpAddress);
                    Check(f.Type == (target.Transport == "usb" ? 1 : target.Transport == "serial" ? 0 : 2));
                    Check(p.GetConnectionStatus().PortState == PrinterPortState.Open);
                    Check(p.Connect(target).IsSuccess);
                    Check(p.Connect(Bt003ConnectionOptions.Usb("different-target")).Code == "AlreadyConnected");
                    using (var other = new Fixture().Printer()) Check(other.Connect(target).Code == "Busy");
                    var closed = p.Disconnect();
                    Check(closed.IsSuccess && closed.ResourcesReleased && closed.Calls.Any(x => x.Function == "DeviceDisconnect" && x.NativeCode == 7));
                    Check(p.GetConnectionStatus().State == PrinterState.Disconnected);
                }
                Check(f.Calls.Count(x => x == "Connect") == 1 && f.Calls.Count(x => x == "Disconnect") == 1);
            }
            foreach (string mode in new[] { "load-throw", "set-fail", "connect-fail" }) {
                var f = new Fixture { Mode = mode };
                using (var p = f.Printer()) {
                    Check(!p.Connect(Bt003ConnectionOptions.Usb("synthetic-failure")).IsSuccess);
                    Check(p.GetConnectionStatus().ResourcesReleased);
                    Check(f.Calls.Count(x => x == "Disconnect") == (mode == "connect-fail" ? 1 : 0));
                }
            }
            var conflict = new Fixture { Mode = "conflict", Port = 1 };
            using (var p = conflict.Printer()) {
                Check(p.Connect(Bt003ConnectionOptions.Usb("synthetic-conflict")).Code == "SdkStateConflict");
                Check(!p.GetConnectionStatus().ResourcesReleased);
                Check(!p.Disconnect().IsSuccess && !conflict.Calls.Contains("Disconnect"));
                conflict.Port = 0;
                Check(p.Disconnect().IsSuccess && p.GetConnectionStatus().ResourcesReleased);
            }
            var open = new Fixture { Mode = "stay-open" };
            using (var p = open.Printer()) {
                Check(p.Connect(Bt003ConnectionOptions.Usb("synthetic-open")).IsSuccess);
                Check(p.Disconnect().Code == "DisconnectUnconfirmed");
                Check(!p.GetConnectionStatus().ResourcesReleased);
                open.Mode = null;
                Check(p.Disconnect().IsSuccess);
            }
            var pending = new Fixture { Mode = "pending" };
            var timed = pending.Printer(50);
            try {
                var result = timed.Connect(Bt003ConnectionOptions.Usb("synthetic-pending"));
                Check(pending.Entered.IsSet && result.Code == "NativeCallTimeout" && result.NativeCallPending);
                Check(!timed.GetConnectionStatus().ResourcesReleased);
                using (var other = new Fixture().Printer()) Check(other.Connect(Bt003ConnectionOptions.Usb("synthetic-other")).Code == "Busy");
                timed.Dispose();
                pending.Release.Set();
                Check(SpinWait.SpinUntil(() => timed.GetConnectionStatus().ResourcesReleased, 3000));
                Check(timed.GetConnectionStatus().State == PrinterState.Disposed && result.NativeCallPending);
                Check(pending.Calls.Count(x => x == "Disconnect") == 1);
            } finally { pending.Release.Set(); timed.Dispose(); }
            Console.WriteLine("PASS: owned connection dispatch, SDK worker, no queue, partial failure, conflict, timeout and late Dispose cleanup; synthetic SDK only");
        }
    }
}
