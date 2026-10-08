using System;
using System.Linq;
using System.Reflection;

namespace U2BIO.BT003PROLabelPrinting.Validation
{
    internal static class ConnectionTests
    {
        private const BindingFlags Internal = BindingFlags.NonPublic | BindingFlags.Static;
        private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Explicit connection check failed."); }
        private static object Field(object options, string name) { return options.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(options); }
        internal static void Run(Assembly library)
        {
            Type optionsType = library.GetType("U2BIO.BT003PROLabelPrinting.Native.NativeConnectionOptions", true);
            MethodInfo create = optionsType.GetMethod("Create", Internal);
            Func<string, string, string, object> options = (mode, port, ip) => create.Invoke(null, new object[] { mode, port, ip });
            object usb = options("usb", "USB1", ""), serial = options("serial", "com003", ""), tcp = options("tcp", "09100", "192.0.2.1");
            Check((ushort)Field(usb, "IfType") == 1 && (string)Field(usb, "Port") == "USB1" && (string)Field(usb, "TargetKey") == "USB:USB1");
            Check((ushort)Field(serial, "IfType") == 0 && (string)Field(serial, "Port") == "3" && (string)Field(serial, "TargetKey") == "COM:3");
            Check((string)Field(serial, "TargetKey") == (string)Field(options("serial", "3", ""), "TargetKey"));
            Check((ushort)Field(tcp, "IfType") == 2 && (string)Field(tcp, "Port") == "9100" && (string)Field(tcp, "Ip") == "192.0.2.1");
            Check((string)Field(tcp, "TargetKey") == "TCP:192.0.2.1:9100");
            Check((string)Field(options("tcp", "1", "2001:db8::1"), "TargetKey") == (string)Field(options("tcp", "01", "2001:0db8:0:0:0:0:0:1"), "TargetKey"));
            foreach (string[] bad in new[] {
                new[] { "usb", "", "" }, new[] { "usb", "0 ", "" }, new[] { "usb", "0\0x", "" }, new[] { "usb", "0\nx", "" },
                new[] { "usb", "0", "192.0.2.1" }, new[] { "serial", "COM0", "" }, new[] { "serial", "COM-1", "" },
                new[] { "serial", "COM3x", "" }, new[] { "serial", "3", "192.0.2.1" }, new[] { "tcp", "0", "192.0.2.1" },
                new[] { "tcp", "65536", "192.0.2.1" }, new[] { "tcp", "9100", "example.invalid" }, new[] { "tcp", "9100", "" },
                new[] { "tcp", "9100", "192.0.2.1 " }, new[] { "unknown", "0", "" }, new[] { "usb", "\ud800", "" }
            }) {
                bool rejected = false;
                try { options(bad[0], bad[1], bad[2]); }
                catch (TargetInvocationException error) { rejected = error.InnerException.Message == "InvalidConnectionOptions"; }
                Check(rejected);
            }
            Type sdk = library.GetType("U2BIO.BT003PROLabelPrinting.Native.NativeSdk", true);
            MethodInfo run = sdk.GetMethod("RunConnectionProbe", Internal);
            foreach (object selected in new[] { usb, serial, tcp }) {
                var fixture = new Fixture { Selected = selected };
                string[] names = { "Set", "Connect", "Status", "Disconnect" };
                object[] args = new[] { selected }.Concat(run.GetParameters().Skip(1).Select((p, n) => (object)Delegate.CreateDelegate(p.ParameterType, fixture, fixture.GetType().GetMethod(names[n])))).ToArray();
                object result = run.Invoke(null, args);
                Check((bool)result.GetType().GetProperty("Passed", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(result));
                Check(fixture.Calls == "?SC?D?");
            }
            Console.WriteLine("PASS: explicit connection normalization/validation/target identity and three transport dispatches; vendor code not executed");
        }
        private sealed class Fixture
        {
            internal object Selected;
            internal string Calls = "";
            public int Set(ushort type, string port, string ip) {
                Check(type == (ushort)Field(Selected, "IfType") && port == (string)Field(Selected, "Port") && ip == (string)Field(Selected, "Ip"));
                Calls += "S"; return 0;
            }
            public int Connect() { Calls += "C"; return 0; }
            public int Status() { Calls += "?"; return Calls.Contains("C") && !Calls.Contains("D") ? 1 : 0; }
            public int Disconnect() { Calls += "D"; return 0; }
        }
    }
}
