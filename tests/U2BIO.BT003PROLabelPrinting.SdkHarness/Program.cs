using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using U2BIO.BT003PROLabelPrinting;
using Fake = HFSample.BT003CtrlWrapper;

internal static class Program
{
    private const string Literal = "^L\nAW,10,10,10,1,0,0,TEST\nE\n";
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static Bt003Printer Connected(string port, int distance = 0)
    {
        var printer = new Bt003Printer();
        Check(printer.ConnectUsb(port, distance).IsSuccess, "Connect failed.");
        Check(printer.SetPrintingOptions(4, 12).IsSuccess, "Options failed.");
        return printer;
    }
    private static void Invalid(Action action)
    {
        try { action(); } catch (ArgumentException error) {
            Check(error.InnerException == null && !error.Message.Contains("PRIVATE-MARKER"), "Input leaked."); return;
        }
        throw new InvalidOperationException("Invalid input accepted.");
    }
    private static void FormsAndFailures()
    {
        Fake.Clear();
        using (var printer = Connected("SYNTH-FORMS")) {
            string[] values = { "FIRST", "SECOND" };
            Fake.BeforePrint = () => values[0] = "MUTATED";
            var result = printer.Print("^L\nAW,10,10,10,1,0,0,^V00\nE\n", values);
            Check(result.IsSuccess && result.PrintInvoked && result.PrintCode == 0, "Print evidence differs.");
            Check(Fake.Variables == "FIRST!!SECOND" && !Fake.LastForm.Replace("\r\n", "").Contains("\n"), "Snapshot/CRLF differs.");
            Check(Fake.Calls.IndexOf("SetPrintFormString") < Fake.Calls.IndexOf("SetPrintFormVariableData") &&
                Fake.Calls.IndexOf("SetPrintFormVariableData") < Fake.Calls.IndexOf("StartLabelPrint"), "Order differs.");
            Fake.BeforePrint = null; Fake.Calls.Clear();
            Check(printer.Print(Literal).IsSuccess && !Fake.Calls.Contains("SetPrintFormVariableData"), "Literal call contract differs.");
            Check(Fake.Variables == "FIRST!!SECOND", "Fake hid variable retention.");
            foreach (string value in new[] { "", "A!!B", "A!", "!B", "A\0B" }) {
                Fake.Calls.Clear(); Invalid(() => printer.Print(Literal, value));
                Check(Fake.Calls.Count == 0, "Invalid value reached SDK.");
            }
            Invalid(() => printer.Print("PRIVATE-MARKER\0"));
            Invalid(() => printer.Print(new string('A', 4 * 1024 * 1024 + 1)));
            Fake.FailAt = "SetPrintFormString"; Fake.FailCode = 103; Fake.Calls.Clear();
            result = printer.Print(Literal);
            Check(result.Status == PrinterResultStatus.Failed && !result.PrintInvoked && !Fake.Calls.Contains("StartLabelPrint"), "Registration failure printed.");
        }
    }
    private static void UnknownAndCleanup()
    {
        int index = 0;
        foreach (string response in new[] { ",101", "garbage,0", ",9999" }) {
            string port = "SYNTH-UNKNOWN-" + index++;
            Fake.Clear();
            using (var printer = Connected(port)) {
                Fake.Response = response; Fake.CleanupThrows = true;
                var result = printer.Print(Literal);
                Check(result.Status == PrinterResultStatus.Unknown && result.PrintInvoked && result.CleanupCode != null, "Cleanup replaced Unknown.");
                Check(!printer.Print(Literal).IsSuccess, "Cleanup fault allowed print.");
            }
            Fake.CleanupThrows = false; Fake.Response = ",0";
            using (var printer = Connected(port)) {
                Fake.Calls.Clear();
                Check(printer.Print(Literal).Code == "MediaPositionUnknown" && !Fake.Calls.Contains("StartLabelPrint"), "New instance lost position uncertainty.");
                Check(printer.ConfirmPrintPosition().IsSuccess && printer.Print(Literal).IsSuccess, "Position confirmation failed.");
            }
        }
        Fake.Clear();
        using (var printer = Connected("SYNTH-CLEANUP")) {
            Fake.CleanupThrows = true;
            var result = printer.Print(Literal);
            Check(result.Status == PrinterResultStatus.Failed && result.PrintCode == 0 && result.Function == "StartLabelPrint" && result.NativeCode == 0, "Cleanup hid successful print response.");
        }
    }
    private static void Movement()
    {
        Fake.Clear();
        using (var printer = Connected("SYNTH-MOVE", 15)) {
            Check(printer.Print(Literal).IsSuccess, "Presentation failed.");
            Fake.Calls.Clear(); Check(printer.Print(Literal).IsSuccess, "Repeat failed.");
            Check(Fake.Calls.Contains("^B15\r\n") && Fake.Calls.Contains("^M15\r\n"), "Return/presentation absent.");
            Fake.FailAt = "SendData"; Fake.FailCode = 7;
            var result = printer.Print(Literal);
            Check(!result.PrintInvoked && result.MovementCode == 7, "Return failure printed.");
            Fake.Calls.Clear();
            Check(printer.Print(Literal).Code == "MediaPositionUnknown" && !Fake.Calls.Contains("SendData"), "Repeated uncertain return.");
        }
        foreach (bool throwing in new[] { false, true }) {
            Fake.Clear();
            using (var printer = Connected("SYNTH-EJECT-" + throwing, 15)) {
                Fake.FailAt = throwing ? null : "SendData"; Fake.FailCode = 7;
                Fake.ThrowAt = throwing ? "SendData" : null;
                var result = printer.Print(Literal);
                Check(!result.IsSuccess && result.PrintInvoked && result.PrintCode == 0, "Ejection failure hid print.");
                Fake.Calls.Clear();
                Check(printer.Print(Literal).Code == "MediaPositionUnknown" && !Fake.Calls.Contains("StartLabelPrint"), "Ejection uncertainty lost.");
            }
        }
    }
    private static void FileLifetime()
    {
        Fake.Clear(); string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "synthetic-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(file, Literal.Replace("\n", "\r\n"), Encoding.GetEncoding(949));
        try {
            using (var printer = Connected("SYNTH-FILE")) {
                Action locked = () => {
                    try { using (File.Open(file, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) {} }
                    catch (IOException) { return; }
                    throw new InvalidOperationException("File lease ended early.");
                };
                Fake.BeforePrint = locked; Fake.BeforeCleanup = locked; Fake.CleanupThrows = true;
                Check(printer.PrintFile(file).PrintCode == 0, "File print differs.");
                using (File.Open(file, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) {}
                Invalid(() => printer.PrintFile(file + "-PRIVATE-MARKER"));
            }
        } finally { File.Delete(file); }
    }
    private static void Ownership()
    {
        Fake.Clear(); var owner = Connected("SYNTH-OWNER");
        using (var other = new Bt003Printer()) {
            Fake.Calls.Clear(); Check(!other.GetConnectionStatus().IsSuccess && other.Disconnect().IsSuccess, "Non-owner contract differs.");
            Check(Fake.Calls.Count == 0, "Non-owner touched SDK.");
        }
        using (var entered = new ManualResetEvent(false)) using (var release = new ManualResetEvent(false)) {
            Exception printError = null, disposeError = null;
            Fake.BeforePrint = () => { entered.Set(); Check(release.WaitOne(5000), "Synthetic wait exceeded."); };
            var printing = new Thread(() => { try { owner.Print(Literal); } catch (Exception error) { printError = error; } });
            var disposing = new Thread(() => { try { owner.Dispose(); } catch (Exception error) { disposeError = error; } });
            printing.Start(); Check(entered.WaitOne(5000), "Print did not enter."); disposing.Start();
            Check(!disposing.Join(100), "Dispose passed active SDK call."); release.Set();
            Check(printing.Join(5000) && disposing.Join(5000) && printError == null && disposeError == null, "Cross-thread completion failed.");
        }
        Fake.Clear(); Fake.FailAt = "DeviceConnect"; Fake.FailCode = 13;
        using (var printer = new Bt003Printer()) {
            Check(!printer.ConnectUsb("SYNTH-PARTIAL").IsSuccess && Fake.PortState == 0 && Fake.Calls.Contains("DeviceDisconnect"), "Partial connect not cleaned.");
        }
        Fake.Clear(); Fake.FailAt = "DeviceConnect"; Fake.FailCode = 13; Fake.KeepOpen = true;
        var partial = new Bt003Printer();
        try {
            Check(!partial.ConnectUsb("SYNTH-PARTIAL-UNCLOSED").IsSuccess, "Partial failure accepted.");
            Fake.Calls.Clear();
            Check(partial.Print(Literal).Code == "ConnectionFaulted" && !Fake.Calls.Contains("StartLabelPrint"), "Failed connection cleanup allowed printing.");
        } finally { Fake.KeepOpen = false; partial.Dispose(); }
        Fake.Clear(); var blocked = Connected("SYNTH-DISCONNECT"); Fake.KeepOpen = true;
        Check(!blocked.Disconnect().IsSuccess, "Unconfirmed disconnect succeeded.");
        using (var other = new Bt003Printer()) Check(!other.ConnectUsb("SYNTH-OTHER").IsSuccess, "Unconfirmed owner was released.");
        Fake.KeepOpen = false; Check(blocked.Disconnect().IsSuccess, "Disconnect recovery failed."); blocked.Dispose();
    }
    private static void ReviewRegressions()
    {
        // 해제 실패 뒤 소유권을 보존하는 동안 인쇄와 옵션을 모두 차단해야 한다.
        foreach (string failure in new[] { "open", "return", "throw" }) {
            Fake.Clear(); var printer = Connected("SYNTH-DISCONNECT-" + failure);
            try {
                Fake.KeepOpen = failure == "open";
                Fake.FailAt = failure == "return" ? "DeviceDisconnect" : null; Fake.FailCode = 7;
                Fake.ThrowAt = failure == "throw" ? "DeviceDisconnect" : null;
                Check(!printer.Disconnect().IsSuccess, "Unconfirmed disconnect accepted.");
                Fake.Calls.Clear();
                Check(printer.Print(Literal).Code == "ConnectionFaulted" && printer.SetPrintingOptions(4, 12).Code == "ConnectionFaulted" && Fake.Calls.Count == 0,
                    "Unconfirmed disconnect allowed SDK reuse.");
                Fake.KeepOpen = false; Fake.FailAt = null; Fake.ThrowAt = null;
                Check(printer.Disconnect().IsSuccess && printer.ConnectUsb("SYNTH-DISCONNECT-" + failure).IsSuccess && printer.Print(Literal).IsSuccess, "Reconnect did not recover.");
            } finally { Fake.KeepOpen = false; Fake.FailAt = null; Fake.ThrowAt = null; printer.Dispose(); }
        }
        // 같은 요청의 앞선 복귀 응답이 마지막 배출 예외의 코드로 남으면 안 된다.
        Fake.Clear();
        using (var printer = Connected("SYNTH-LAST-MOVEMENT", 15)) {
            Check(printer.Print(Literal).IsSuccess, "First presentation failed.");
            Fake.BeforePrint = () => Fake.ThrowAt = "SendData";
            var result = printer.Print(Literal);
            Check(result.Status == PrinterResultStatus.Unknown && result.PrintCode == 0 && result.MovementCode == null && result.NativeCode == null,
                "Last movement inherited previous response.");
            Check(printer.Print(Literal).Code == "MediaPositionUnknown", "Uncertain presentation was repeated.");
        }
        Fake.Clear(); Fake.ThrowAt = "CheckPrtConnection";
        var connecting = new Bt003Printer();
        try {
            var result = connecting.ConnectUsb("SYNTH-CONNECTION-EXCEPTION");
            Check(result.Function == "CheckPrtConnection" && result.NativeCode == null && !result.IsSuccess, "Status exception inherited connect response.");
        } finally { Fake.ThrowAt = null; connecting.Dispose(); }
        Fake.Clear();
        using (var printer = Connected("SYNTH-VARIABLE-TOTAL")) {
            Fake.Calls.Clear(); string part = new string('A', 1024 * 1024);
            Invalid(() => printer.Print(Literal, part, part, part, part));
            Check(Fake.Calls.Count == 0, "Oversized variable total reached SDK.");
        }
    }
    private static int Main()
    {
        try {
            FormsAndFailures(); UnknownAndCleanup(); Movement(); FileLifetime(); Ownership(); ReviewRegressions();
            Console.WriteLine("PASS: linked product SDK sequencing, input snapshots, Unknown/cleanup, movement, file lifetime and ownership; managed SDK substitute only");
            return 0;
        } catch (Exception error) { Console.Error.WriteLine("FAIL: managed SDK harness (" + error.GetType().Name + "): " + error.Message); return 1; }
    }
}
