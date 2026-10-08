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
            string second = "^Q40,5,7.5+\r\n^W70\r\n^G0\r\n^S5\r\n^H19\r\n^P1\r\n^C1\r\n^R0\r\n^E15\r\n^L\r\nAW,80,120,10,1,0,0,U2BIO TEST2\r\nE\r\n";
            string third = "^Q40,5,7.5+\r\n^W70\r\n^G0\r\n^E7\r\n^S5\r\n^H19\r\n^P1\r\n^C1\r\n^L\r\nAC,80,120,1,1,0,0,U2BIO TEST2\r\nE\r\n";
            Check(LabelDiagnosticForms.Create("50x25gap3") == first);
            Check(LabelDiagnosticForms.Create("blackmark60x20") == second);
            Check(LabelDiagnosticForms.Create("blackmark60x20ac") == third);
            Check(LabelDiagnosticForms.Create("blackmark60x20direct") == third);
            string presentation = "^M15\r\n";
            Check(LabelDiagnosticForms.Create("present15") == presentation);
            string positionReturn = "^B15\r\n";
            Check(LabelDiagnosticForms.Create("return15") == positionReturn);
            string[] names = { "Set", "Connect", "Status", "Disconnect", "Reset", "Form", "Print" };
            foreach (string selected in new[] { first, second, third })
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
            MethodInfo direct = library.GetType("U2BIO.BT003PROLabelPrinting.Native.NativeSdk", true).GetMethod("RunDirectLabelProbe", Static);
            Check(direct != null);
            string[] directNames = { "Set", "Connect", "Status", "Disconnect", "Send" };
            foreach (string selected in new[] { third, presentation, positionReturn })
            foreach (string mode in new[] { "ok", "pre", "set", "connect", "open", "send" }) {
                var fixture = new Fixture { Mode = mode, ExpectedForm = selected };
                object[] args = new[] { options, selected }.Concat(direct.GetParameters().Skip(2).Select((p, n) => (object)Delegate.CreateDelegate(p.ParameterType, fixture, fixture.GetType().GetMethod(directNames[n])))).ToArray();
                object result = direct.Invoke(null, args);
                string expected = mode == "pre" ? "?" : mode == "set" ? "?S" : mode == "connect" ? "?SCD?" : mode == "open" ? "?SC?D?" : "?SC?TD?";
                Check(fixture.Calls == expected);
                Check(!(bool)result.GetType().GetField("PrintInvoked", Instance).GetValue(result));
                Check(result.GetType().GetField("Response", Instance).GetValue(result) == null);
                Check((int?)result.GetType().GetField("SendCode", Instance).GetValue(result) == (mode == "ok" ? (int?)0 : mode == "send" ? (int?)9 : null));
            }
            foreach (string bad in new[] { null, "", "bad\0data", second, third.Replace("TEST2", "한글") }) {
                var fixture = new Fixture { Mode = "ok", ExpectedForm = bad };
                object[] args = new[] { options, bad }.Concat(direct.GetParameters().Skip(2).Select((p, n) => (object)Delegate.CreateDelegate(p.ParameterType, fixture, fixture.GetType().GetMethod(directNames[n])))).ToArray();
                bool rejected = false;
                try { direct.Invoke(null, args); }
                catch (TargetInvocationException error) { rejected = error.InnerException.Message == "InvalidDiagnosticForm"; }
                Check(rejected && fixture.Calls == "");
            }
            Console.WriteLine("PASS: caller forms, label/direct ordering, short-circuit, cleanup and42byte response; vendor code not executed");
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
            public int Send(string text) { Calls += "T"; Check(text == ExpectedForm); return Mode == "send" ? 9 : 0; }
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
