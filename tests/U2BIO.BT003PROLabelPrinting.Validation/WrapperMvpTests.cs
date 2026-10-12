using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using U2BIO.BT003PROLabelPrinting;

namespace U2BIO.BT003PROLabelPrinting.Validation
{
    internal static class WrapperMvpTests
    {
        private const string Form = "^Q40,5,7.5+\r\n^W70\r\n^P1\r\n^C1\r\n^L\r\nAW,80,120,10,1,0,0,^V02\r\nAW,80,160,10,1,0,0,^V00\r\nE\r\n";
        private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("USB Wrapper MVP contract failed."); }
        private static void Reject(Action action)
        {
            bool rejected = false;
            try { action(); } catch (BtplFormatException) { rejected = true; }
            Check(rejected);
        }
        internal static void Run()
        {
            var fixture = new PrinterConnectionTests.Fixture { Printed = (copies, pointer) => {
                Check(copies == 1);
                Marshal.Copy(Encoding.ASCII.GetBytes(",0\0"), 0, pointer, 3);
            } };
            string path = Path.Combine(Path.GetTempPath(), "wrapper-mvp-" + Guid.NewGuid().ToString("N") + ".txt");
            string received = null;
            int fileCalls = 0, variableCode = 0;
            try {
                File.WriteAllText(path, Form, Encoding.GetEncoding(949));
                using (var printer = fixture.Printer()) {
                    var sdk = typeof(Bt003Printer).GetField("sdk", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(printer);
                    var type = sdk.GetType();
                    type.GetField("Options", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(sdk, new Func<ushort, ushort, int>((speed, darkness) => {
                        Check(speed == 4 && darkness == 12); fixture.Calls.Add("Options"); return 0;
                    }));
                    type.GetField("FileForm", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(sdk, new Func<string, int>(file => {
                        Check(file == Path.GetFullPath(path)); fileCalls++;
                        bool locked = false;
                        try { using (File.Open(file, FileMode.Open, FileAccess.Write, FileShare.Read)) { } }
                        catch (IOException) { locked = true; }
                        Check(locked); fixture.Calls.Add("FileForm"); return 0;
                    }));
                    type.GetField("Variables", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(sdk, new Func<string, int>(data => {
                        received = data; fixture.Calls.Add("Variables"); return variableCode;
                    }));
                    Check(printer.ConnectUsb("synthetic-wrapper").IsSuccess && fixture.Type == 1 && fixture.Ip == "");
                    string[] values = { "FIRST", "UNUSED", "THIRD" };
                    Check(printer.Print(Form, values).NotAttemptedCount == 1 && fixture.FormNumber == 0);
                    Check(printer.SetPrintingOptions(4, 12).IsSuccess);
                    fixture.Calls.Clear();
                    Check(printer.Print(Form, values).Succeeded == 1 && fixture.RegisteredForm == Form);
                    Check(received == "FIRST!!UNUSED!!THIRD");
                    Check(string.Join("|", fixture.Calls) == "Status|Status|Reset|Options|Form|Variables|Print|Reset|Status");
                    Check(printer.PrintFile(path, values).Succeeded == 1 && fileCalls == 1);
                    using (File.Open(path, FileMode.Open, FileAccess.Write, FileShare.Read)) { }
                    int prints = fixture.PrintNumber;
                    variableCode = 205;
                    var failed = printer.Print(Form, values);
                    Check(failed.Failed == 1 && failed.NativeCode == 205 && !failed.Rows[0].PrintInvoked && fixture.PrintNumber == prints);
                    variableCode = 0;
                    Check(printer.Print(Form.Replace("^V02", "FIXED").Replace("^V00", "FIXED")).Succeeded == 1);
                    Reject(() => printer.Print(Form, new[] { "FIRST", "THIRD" }));
                    Reject(() => printer.Print(Form, new[] { "FIRST!!SECOND", "UNUSED", "THIRD" }));
                    Reject(() => printer.Print(Form, new[] { "FIRST\r\nE", "UNUSED", "THIRD" }));
                }
            } finally { if (File.Exists(path)) File.Delete(path); }
            Console.WriteLine("PASS: USB Wrapper MVP forms, SDK variables, options, file lease and failure stop; vendor SDK not loaded");
        }
    }
}
