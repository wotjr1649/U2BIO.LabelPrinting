using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace U2BIO.BT003PROLabelPrinting.Validation
{
    internal static class LabelPrintTests
    {
        private const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;
        private const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Label diagnostic check failed."); }
        internal static void Run(Assembly library)
        {
            object options = library.GetType("U2BIO.BT003PROLabelPrinting.Native.NativeConnectionOptions", true).GetMethod("Create", Static)
                .Invoke(null, new object[] { "usb", "0", "" });
            MethodInfo run = library.GetType("U2BIO.BT003PROLabelPrinting.Native.NativeSdk", true).GetMethod("RunLabelProbe", Static);
            Check(run != null);
            Check(run.GetParameters().Length == 9 && run.GetParameters()[1].ParameterType == typeof(string));
            string first = "^Q25,3\r\n^W50\r\n^H10\r\n^S2\r\n^P1\r\n^C1\r\n^R0\r\n^E0\r\n^D0\r\n^O0\r\n^L\r\nAC,24,24,1,1,0,0,U2BIO TEST\r\nE\r\n";
            string second = first.Replace("^Q25,3", "^Q40,5,7.5+").Replace("^W50", "^W70\r\n^G0")
                .Replace("AC,24,24,1,1,0,0", "AW,80,120,10,2,0,0");
            Check(LabelDiagnosticForms.Create("50x25gap3") == first);
            Check(LabelDiagnosticForms.Create("blackmark60x20") == second);
            string[] names = { "Set", "Connect", "Status", "Disconnect", "Reset", "Form", "Print" };
            foreach (string selected in new[] { first, second })
            foreach (string mode in new[] { "ok", "pre", "set", "connect", "open", "form", "invalid", "throw" }) {
                var fixture = new Fixture { Mode = mode, ExpectedForm = selected };
                object[] args = new[] { options, selected }.Concat(run.GetParameters().Skip(2).Select((p, n) => (object)Delegate.CreateDelegate(p.ParameterType, fixture, fixture.GetType().GetMethod(names[n])))).ToArray();
                object result = null;
                try { result = run.Invoke(null, args); }
                catch (TargetInvocationException error) { Check(mode == "throw" && error.InnerException is InvalidOperationException); }
                string expected = mode == "pre" ? "?" : mode == "set" ? "?S" : mode == "connect" ? "?SCD?" : mode == "open" ? "?SC?D?" : mode == "form" ? "?SC?RFD?" : "?SC?RFPD?";
                Check(fixture.Calls == expected);
                if (result == null) continue;
                Check((bool)result.GetType().GetField("PrintInvoked", Instance).GetValue(result) == (mode == "ok" || mode == "invalid"));
                object response = result.GetType().GetField("Response", Instance).GetValue(result);
                if (mode == "ok") {
                    Check(response != null && (int)response.GetType().GetField("NativeCode", Instance).GetValue(response) == int.MinValue);
                    Check((int)response.GetType().GetField("Length", Instance).GetValue(response) == 41);
                } else Check(response == null);
                if (mode == "invalid") Check((string)result.GetType().GetField("ResponseError", Instance).GetValue(result) == "PrintResponseInvalid");
            }
            foreach (string bad in new[] { null, "", "bad\0data" }) {
                var fixture = new Fixture { Mode = "ok", ExpectedForm = bad };
                object[] args = new[] { options, bad }.Concat(run.GetParameters().Skip(2).Select((p, n) => (object)Delegate.CreateDelegate(p.ParameterType, fixture, fixture.GetType().GetMethod(names[n])))).ToArray();
                bool rejected = false;
                try { run.Invoke(null, args); }
                catch (TargetInvocationException error) { rejected = error.InnerException.Message == "InvalidDiagnosticForm"; }
                Check(rejected && fixture.Calls == "");
            }
            Console.WriteLine("PASS: two caller-provided diagnostic forms with ordering/short-circuit/cleanup and42byte response; vendor code not executed");
        }
        private sealed class Fixture
        {
            internal string Mode, ExpectedForm, Calls = "";
            public int Set(ushort type, string port, string ip) { Check(type == 1 && port == "0" && ip == ""); Calls += "S"; return Mode == "set" ? 1 : 0; }
            public int Connect() { Calls += "C"; return Mode == "connect" ? 1 : 0; }
            public int Status() { Calls += "?"; return Calls.Contains("D") ? 0 : !Calls.Contains("C") ? (Mode == "pre" ? 1 : 0) : (Mode == "open" ? 0 : 1); }
            public int Disconnect() { Calls += "D"; return 0; }
            public void Reset() { Calls += "R"; }
            public int Form(string text) {
                Calls += "F";
                Check(text == ExpectedForm);
                return Mode == "form" ? 1 : 0;
            }
            public void Print(ushort copies, IntPtr pointer) {
                Calls += "P"; Check(copies == 1);
                var bytes = new byte[42]; Marshal.Copy(pointer, bytes, 0, bytes.Length); Check(bytes.All(b => b == 255));
                if (Mode == "throw") throw new InvalidOperationException("synthetic");
                byte[] output = Encoding.ASCII.GetBytes(Mode == "invalid" ? "unexpected\0" : "00:00:00:00:00:00:00:00:00:00,-2147483648\0");
                Marshal.Copy(output, 0, pointer, output.Length);
            }
        }
    }
}
