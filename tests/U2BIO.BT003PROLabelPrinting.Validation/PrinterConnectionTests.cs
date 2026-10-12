using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
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
            internal Action<ushort, IntPtr> Printed;
            internal Func<int, bool, int> Moved;
            internal string RegisteredForm;
            internal int FormNumber, PrintNumber, StatusNumber, ResetNumber;
            private void Add(string value)
            {
                int current = Thread.CurrentThread.ManagedThreadId;
                if (OwnerThread == 0) OwnerThread = current;
                Check(OwnerThread == current);
                lock (Calls) Calls.Add(value);
            }
            internal void Load() {
                Add("Load"); if (Mode == "load-throw") throw new InvalidOperationException("synthetic");
                if (Mode == "load-specific") throw SdkError("LoadLibraryExW", 126);
                if (Mode == "load-pending") { Entered.Set(); Check(Release.Wait(5000)); }
            }
            private static Exception SdkError(string function, int system)
            {
                var type = typeof(Bt003ConnectionOptions).Assembly.GetType("U2BIO.BT003PROLabelPrinting.Native.NativeSdkException", true);
                return (Exception)Activator.CreateInstance(type, BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new object[] { "NativeLoadFailed", (int?)system, function }, null);
            }
            internal int Set(ushort type, string name, string ip) { Add("Set"); Type = type; Name = name; Ip = ip; return Mode == "conflict" ? 1 : Mode == "set-fail" ? 9 : 0; }
            internal int Connect()
            {
                Add("Connect"); Port = 1;
                if (Mode == "pending") { Entered.Set(); Check(Release.Wait(5000)); }
                return Mode == "connect-fail" ? 9 : 0;
            }
            internal int Status() {
                Add("Status"); StatusNumber++;
                if (Mode == "first-row-status-throw" && StatusNumber == 3) throw new InvalidOperationException("synthetic status failure");
                if (Mode == "row-closed" && StatusNumber == 2) Port = 0;
                if (Mode == "status-pending") { Entered.Set(); Check(Release.Wait(5000)); }
                return Port;
            }
            internal int Disconnect() {
                Add("Disconnect");
                if (Mode == "disconnect-throw") {
                    throw SdkError("DeviceDisconnect", 5);
                }
                if (Mode != "stay-open") Port = 0; return 7;
            }
            internal void Reset() {
                Add("Reset"); ResetNumber++;
                if (Mode == "reset-cleanup-fail" && ResetNumber == 4) throw new InvalidOperationException("synthetic cleanup failure");
            }
            internal int Form(string form) {
                Add("Form"); FormNumber++; RegisteredForm = form;
                if (Mode == "form-pending") { Entered.Set(); Check(Release.Wait(5000)); }
                return Mode == "form-fail" || Mode == "form-second-fail" && FormNumber == 2 ? 9 : 0;
            }
            internal void Print(ushort copies, IntPtr pointer) {
                Add("Print"); PrintNumber++;
                if (Printed == null) throw new InvalidOperationException("Connection tests must not print.");
                Printed(copies, pointer);
            }
            internal int Move(int distance, bool forward) {
                Add((forward ? "Advance:" : "Return:") + distance);
                return Moved == null ? 0 : Moved(distance, forward);
            }
            internal Bt003Printer Printer(int milliseconds = 1000)
            {
                var assembly = typeof(Bt003ConnectionOptions).Assembly;
                var type = assembly.GetType("U2BIO.BT003PROLabelPrinting.Native.PrinterSdkCalls", true);
                var constructor = type.GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
                var methods = new[] { "Load", "Set", "Connect", "Status", "Disconnect", "Reset", "Form", "Print", "Move" };
                object calls = constructor.Invoke(constructor.GetParameters().Select((p, i) => (object)Delegate.CreateDelegate(
                    p.ParameterType, this, GetType().GetMethod(methods[i], BindingFlags.NonPublic | BindingFlags.Instance))).ToArray());
                return (Bt003Printer)Activator.CreateInstance(typeof(Bt003Printer), BindingFlags.NonPublic | BindingFlags.Instance, null,
                    new[] { (object)TimeSpan.FromMilliseconds(milliseconds), calls }, null);
            }
        }
        private static void FirstRowStatusFailure()
        {
            var f = new Fixture { Mode = "first-row-status-throw" };
            using (var p = f.Printer()) {
                var target = Bt003ConnectionOptions.Usb("synthetic-status-exception");
                Check(p.Connect(target).IsSuccess);
                var failed = p.Print(PrinterTests.Request());
                Check(failed.Failed == 1 && !failed.Rows[0].PrintInvoked && failed.Code == "NativeCallFailed");
                Check(failed.CleanupCode == "NativeCallFailed" && !f.Calls.Contains("Form") && !f.Calls.Contains("Print"));
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                Check((PrinterState)typeof(Bt003Printer).GetField("state", flags).GetValue(p) == PrinterState.Faulted);
                Check((PrinterPortState)typeof(Bt003Printer).GetField("port", flags).GetValue(p) == PrinterPortState.Unknown);
                Check(typeof(Bt003Printer).GetField("nativeCode", flags).GetValue(p) == null);
                var queried = p.GetConnectionStatus();
                Check(queried.State == PrinterState.Faulted && !queried.ResourcesReleased && queried.CleanupCode == "NativeCallFailed");
                int count = f.Calls.Count;
                Check(p.Print(PrinterTests.Request()).Code == "PrinterFaulted" && f.Calls.Count == count);
                using (var other = new Fixture().Printer()) Check(other.Connect(target).Code == "Busy");
                Check(p.Disconnect().IsSuccess && p.GetConnectionStatus().ResourcesReleased);
                f.OwnerThread = 0;
                Check(p.Connect(target).IsSuccess);
                Check(p.Disconnect().IsSuccess);
            }
            Console.WriteLine("PASS: first-row status exception faults connection until explicit disconnect");
        }
        internal static void Run()
        {
            FirstRowStatusFailure();
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
                    var failed = p.Connect(Bt003ConnectionOptions.Usb("synthetic-failure"));
                    Check(!failed.IsSuccess);
                    if (mode == "connect-fail") Check(failed.NativeFunction == "DeviceConnect" && failed.NativeCode == 9);
                    Check(p.GetConnectionStatus().ResourcesReleased);
                    Check(f.Calls.Count(x => x == "Disconnect") == (mode == "connect-fail" ? 1 : 0));
                }
            }
            using (var p = new Fixture { Mode = "load-specific" }.Printer()) {
                var failed = p.Connect(Bt003ConnectionOptions.Usb("synthetic-loader-error"));
                Check(failed.NativeFunction == "LoadLibraryExW" && failed.SystemErrorCode == 126 && failed.ResourcesReleased);
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
            var loading = new Fixture { Mode = "load-pending" };
            using (var p = loading.Printer(50)) {
                try {
                    var result = p.Connect(Bt003ConnectionOptions.Usb("synthetic-late-load"));
                    Check(result.Code == "NativeCallTimeout" && result.NativeCallPending);
                    loading.Release.Set();
                    Check(SpinWait.SpinUntil(() => p.GetConnectionStatus().ResourcesReleased, 3000));
                    Check(!loading.Calls.Contains("Set") && !loading.Calls.Contains("Connect") && !loading.Calls.Contains("Disconnect"));
                    Check(p.GetConnectionStatus().State == PrinterState.Faulted);
                    Check(p.Disconnect().IsSuccess);
                } finally { loading.Release.Set(); }
            }
            var disposing = new Fixture { Mode = "pending" };
            var active = disposing.Printer();
            var connecting = Task.Run(() => active.Connect(Bt003ConnectionOptions.Usb("synthetic-dispose")));
            try {
                Check(disposing.Entered.Wait(1000));
                active.Dispose(); disposing.Release.Set();
                Check(connecting.Wait(3000) && connecting.Result.Code == "Disposed");
                Check(SpinWait.SpinUntil(() => active.GetConnectionStatus().ResourcesReleased, 3000));
                Check(active.GetConnectionStatus().State == PrinterState.Disposed && disposing.Calls.Count(x => x == "Disconnect") == 1);
            } finally { disposing.Release.Set(); active.Dispose(); }
            foreach (int initial in new[] { 1, 0 }) {
                string target = "synthetic-abandoned-" + initial;
                string hash;
                using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes("USB:" + target))).Replace("-", "").ToLowerInvariant();
                using (var abandoned = new Mutex(false, @"Local\U2BIO.BT003PROLabelPrinting.Target." + hash)) {
                    var owner = new Thread(() => abandoned.WaitOne()); owner.Start(); Check(owner.Join(1000));
                    var f = new Fixture { Port = initial };
                    using (var p = f.Printer()) {
                        var result = p.Connect(Bt003ConnectionOptions.Usb(target));
                        if (initial == 1) {
                            Check(result.Code == "SdkStateConflict" && !f.Calls.Contains("Set") && !f.Calls.Contains("Connect") && !f.Calls.Contains("Disconnect"));
                            f.Port = 0; Check(p.Disconnect().IsSuccess);
                        } else Check(result.IsSuccess && f.Calls.IndexOf("Status") < f.Calls.IndexOf("Set"));
                    }
                }
            }
            var failingClose = new Fixture { Mode = "disconnect-throw" };
            using (var p = failingClose.Printer()) {
                Check(p.Connect(Bt003ConnectionOptions.Usb("synthetic-close-error")).IsSuccess);
                var result = p.Disconnect();
                Check(!result.IsSuccess && result.NativeFunction == "DeviceDisconnect" && result.NativeCode == null && result.SystemErrorCode == 5);
                Check(result.Calls.Any(x => x.Function == "DeviceDisconnect" && x.NativeCode == null));
                failingClose.Mode = null; Check(p.Disconnect().IsSuccess);
            }
            for (int attempt = 0; attempt < 200; attempt++) {
                var f = new Fixture(); var p = f.Printer();
                var connect = Task.Run(() => p.Connect(Bt003ConnectionOptions.Usb("synthetic-finalize")));
                Check(SpinWait.SpinUntil(() => p.GetConnectionStatus().NativeCallPending || connect.IsCompleted, 1000));
                p.Dispose(); Check(connect.Wait(2000));
                Check(SpinWait.SpinUntil(() => p.GetConnectionStatus().ResourcesReleased, 1000));
            }
            Console.WriteLine("PASS: owned connection dispatch, SDK worker, no queue, partial failure, conflict, timeout and late Dispose cleanup; synthetic SDK only");
        }
    }
}
