using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using U2BIO.BT003PROLabelPrinting;
using Fake = HFSample.BT003CtrlWrapper;

internal static class Program
{
    private const string Literal = "^L\nAW,10,10,10,1,0,0,TEST\nE\n";
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static Bt003Printer Connected(string port, int distance = 0, bool confirm = true)
    {
        var printer = new Bt003Printer();
        Check(printer.ConnectUsb(port, distance).IsSuccess, "Connect failed.");
        // 처음 쓰는 포트는 위치 기록이 없으므로 운영자 정렬 확인을 대신 기록한다.
        if (confirm) Check(printer.ConfirmPrintPosition().IsSuccess, "Confirm failed.");
        Check(printer.SetPrintingOptions(4, 12).IsSuccess, "Options failed.");
        return printer;
    }
    private static void Restart()
    {
        // 프로그램 재실행처럼 프로세스 메모리의 위치 기록과 마지막 이동 시각만 지우고 파일은 남긴다.
        ((System.Collections.IDictionary)typeof(Bt003Printer).GetField("Positions", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null)).Clear();
        typeof(Bt003Printer).GetField("lastMotionUtc", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, DateTime.MinValue);
    }
    private static void MinimumInterval()
    {
        // 실기에서 앞 장 동작이 끝나기 전 다음 장을 보내면 프린터가 멈칫하고 기준이 밀렸다. 직전 이동 뒤 간격을 기다린다.
        const string port = "SYNTH-INTERVAL";
        Bt003Printer.MinimumIntervalMs = 300;
        try {
            Fake.Clear(); Restart();
            using (var printer = Connected(port, 15)) {
                Check(printer.Print(Literal).IsSuccess, "First print failed.");
                var watch = System.Diagnostics.Stopwatch.StartNew();
                Check(printer.Print(Literal).IsSuccess && watch.ElapsedMilliseconds >= 250, "Consecutive print did not wait.");
            }
            // 재실행 직후에도 위치 기록 파일의 마지막 저장 시각부터 간격을 센다.
            Restart();
            using (var printer = new Bt003Printer()) {
                Check(printer.ConnectUsb(port, 15).IsSuccess, "Reconnect failed.");
                var watch = System.Diagnostics.Stopwatch.StartNew();
                Check(printer.Print(Literal).IsSuccess && watch.ElapsedMilliseconds >= 250, "Restart print did not wait.");
                // 간격이 이미 지났으면 기다리지 않고, 위치 확인만으로는 대기 시각이 바뀌지 않는다.
                Thread.Sleep(400);
                Check(printer.ConfirmPrintPosition().IsSuccess, "Confirm failed.");
                watch.Restart();
                Check(printer.Print(Literal).IsSuccess && watch.ElapsedMilliseconds < 250, "Print waited after the interval.");
                // FEED도 이송이므로 앞 동작을 기다리고, 다음 인쇄의 대기 기준이 된다.
                watch.Restart();
                Check(printer.FeedAndConfirmPosition().IsSuccess && watch.ElapsedMilliseconds >= 250, "Feed did not wait for the previous motion.");
                watch.Restart();
                Check(printer.Print(Literal).IsSuccess && watch.ElapsedMilliseconds >= 250, "Print did not wait after feed.");
            }
        } finally { Bt003Printer.MinimumIntervalMs = 0; }
    }
    private static void FeedPosition()
    {
        // FEED 키 + ConfirmPrintPosition 수동 절차를 DLL이 대신한다. 배출 상태에서도 복귀 없이 FEED만 보낸다(실기 확인한 수동 절차와 같음).
        const string port = "SYNTH-FEED";
        const string feed = "~S,FEED\r\n";
        string file = Bt003Printer.PositionFile;
        Fake.Clear(); Restart(); File.Delete(file);
        using (var printer = new Bt003Printer()) {
            Check(printer.ConnectUsb(port, 15).IsSuccess, "Connect failed.");
            Fake.Calls.Clear();
            var fed = printer.FeedAndConfirmPosition();
            Check(fed.IsSuccess && fed.NativeCode == 0 && !fed.PrintInvoked && fed.PrintCode == null && fed.Message.Contains("FEED"), "Feed result differs.");
            Check(Fake.Calls.SequenceEqual(new[] { "CheckPrtConnection", feed, "SendData" }) && File.ReadAllLines(file).SequenceEqual(new[] { port, "0" }), "Feed did not record the print position.");
            Fake.Calls.Clear();
            Check(printer.Print(Literal).IsSuccess && !Fake.Calls.Any(call => call.StartsWith("^B")) && Fake.Calls.Contains("^M15\r\n"), "Print after feed returned or skipped presentation.");
            Fake.Calls.Clear();
            Check(printer.FeedAndConfirmPosition().IsSuccess && !Fake.Calls.Any(call => call.StartsWith("^B") || call.StartsWith("^M"))
                && File.ReadAllLines(file).SequenceEqual(new[] { port, "0" }), "Feed from the presented state moved more than FEED.");
            // SDK 실패·예외는 이송 여부를 모르므로 위치를 불명으로 남기고 인쇄를 막는다.
            Fake.FailAt = "SendData"; Fake.FailCode = 101;
            var failed = printer.FeedAndConfirmPosition();
            Check(!failed.IsSuccess && failed.NativeCode == 101 && File.ReadAllLines(file).SequenceEqual(new[] { port, "unknown" }), "Failed feed was trusted.");
            Fake.FailAt = null; Fake.Calls.Clear();
            Check(!printer.Print(Literal).IsSuccess && !Fake.Calls.Contains("StartLabelPrint"), "Print allowed after failed feed.");
            Fake.ThrowAt = "SendData";
            Check(!printer.FeedAndConfirmPosition().IsSuccess && File.ReadAllLines(file).SequenceEqual(new[] { port, "unknown" }), "Feed exception was trusted.");
            Fake.ThrowAt = null;
            Check(printer.FeedAndConfirmPosition().IsSuccess, "Feed did not recover an unknown position.");
            // 기록 파일을 쓸 수 없으면 FEED SDK를 호출하지 않는다.
            string unwritable = Path.Combine(file, "unwritable.txt");
            try {
                Bt003Printer.PositionFile = unwritable; Fake.Calls.Clear();
                Check(!printer.FeedAndConfirmPosition().IsSuccess && !Fake.Calls.Contains(feed), "Unwritable file allowed feed.");
                // FEED 정상 응답 뒤 저장이 실패하면 위치를 불명으로 남겨 인쇄를 막는다.
                Bt003Printer.PositionFile = file;
                Fake.AfterSendData = () => Bt003Printer.PositionFile = unwritable;
                var unsaved = printer.FeedAndConfirmPosition();
                Check(!unsaved.IsSuccess && unsaved.NativeCode == 0 && unsaved.Message.Contains("기록 파일"), "Unsaved feed was reported as confirmed.");
                Fake.AfterSendData = null; Bt003Printer.PositionFile = file; Fake.Calls.Clear();
                Check(!printer.Print(Literal).IsSuccess && !Fake.Calls.Contains("StartLabelPrint"), "Print allowed after unsaved feed.");
            } finally { Fake.AfterSendData = null; Bt003Printer.PositionFile = file; }
            // 연결이 열리지 않았으면 FEED를 보내지 않는다.
            string before = File.ReadAllText(file);
            Fake.PortState = 0; Fake.Calls.Clear();
            Check(!printer.FeedAndConfirmPosition().IsSuccess && !Fake.Calls.Contains(feed) && File.ReadAllText(file) == before, "Feed sent or recorded without an open connection.");
            Fake.PortState = 1;
            // 서식 정리 실패로 연결 복구가 필요하면 FEED를 보내지 않고 기록도 바꾸지 않는다.
            Check(printer.FeedAndConfirmPosition().IsSuccess, "Feed failed after the connection reopened.");
            Fake.CleanupThrows = true;
            Check(!printer.Print(Literal).IsSuccess, "Cleanup failure was not reported.");
            Fake.CleanupThrows = false;
            before = File.ReadAllText(file); Fake.Calls.Clear();
            var faulted = printer.FeedAndConfirmPosition();
            Check(!faulted.IsSuccess && faulted.Message.Contains("복구") && Fake.Calls.Count == 0 && File.ReadAllText(file) == before, "Faulted connection sent FEED.");
        }
        // 연결 소유자가 아니면 SDK를 호출하지 않는다.
        using (var other = new Bt003Printer()) {
            Fake.Calls.Clear();
            Check(!other.FeedAndConfirmPosition().IsSuccess && Fake.Calls.Count == 0, "Non-owner feed reached SDK.");
        }
    }
    private static void PositionFile()
    {
        const string port = "SYNTH-PERSIST";
        string file = Bt003Printer.PositionFile;
        Fake.Clear(); Restart(); File.Delete(file);
        using (var printer = new Bt003Printer()) {
            Check(printer.ConnectUsb(port, 15).IsSuccess, "Connect failed.");
            Fake.Calls.Clear();
            var blocked = printer.Print(Literal);
            Check(!blocked.IsSuccess && blocked.Message.Contains("용지 위치") && !Fake.Calls.Contains("StartLabelPrint"), "Missing file allowed print.");
            Check(printer.ConfirmPrintPosition().IsSuccess && File.ReadAllLines(file).SequenceEqual(new[] { port, "0" }), "Confirm did not create file.");
            Check(printer.Print(Literal).IsSuccess && File.ReadAllLines(file).SequenceEqual(new[] { port, "15" }), "Presentation not saved.");
        }
        // 재실행 뒤 첫 인쇄도 저장된 15mm를 먼저 복귀하고 다시 배출해야 한다.
        for (int run = 0; run < 2; run++) {
            Restart();
            using (var printer = new Bt003Printer()) {
                Check(printer.ConnectUsb(port, 15).IsSuccess, "Reconnect failed.");
                Fake.Calls.Clear();
                Check(printer.Print(Literal).IsSuccess, "Restarted print failed.");
                int back = Fake.Calls.IndexOf("^B15\r\n"), print = Fake.Calls.IndexOf("StartLabelPrint"), eject = Fake.Calls.IndexOf("^M15\r\n");
                Check(back >= 0 && back < print && print < eject && File.ReadAllLines(file).SequenceEqual(new[] { port, "15" }), "Restart did not return then present.");
            }
        }
        // 다른 포트·손상된 기록은 위치 불명으로 시작한다.
        foreach (string content in new[] { "OTHER\r\n15", "garbage", port + "\r\n-1", port + "\r\n1001", port + "\r\nunknown" }) {
            File.WriteAllText(file, content); Restart();
            using (var printer = new Bt003Printer()) {
                Check(printer.ConnectUsb(port, 15).IsSuccess, "Connect failed.");
                Fake.Calls.Clear();
                Check(!printer.Print(Literal).IsSuccess && Fake.Calls.Count(call => call != "CheckPrtConnection") == 0, "Untrusted file allowed movement.");
            }
        }
        // 인쇄 도중 중단되면 다음 실행이 위치 불명으로 시작하도록 기록이 남아야 한다.
        Restart();
        using (var printer = Connected(port, 15)) {
            Fake.ThrowAt = "StartLabelPrint";
            Check(!printer.Print(Literal).IsSuccess && File.ReadAllLines(file).SequenceEqual(new[] { port, "unknown" }), "Interrupted print was not saved as unknown.");
            Fake.ThrowAt = null;
        }
        string unwritable = Path.Combine(file, "unwritable.txt");
        // 인쇄 SDK가 정상 응답한 뒤 파일 저장이 실패하면 메모리는 0으로 갱신하고 파일은 unknown으로 남기며, 배출 SDK는 호출하지 않는다.
        Restart();
        try {
            using (var printer = Connected(port, 15)) {
                Fake.BeforePrint = () => Bt003Printer.PositionFile = unwritable;
                Fake.Calls.Clear();
                var printed = printer.Print(Literal);
                Check(!printed.IsSuccess && printed.PrintInvoked && printed.PrintCode == 0 && printed.Message.Contains("배출")
                    && !Fake.Calls.Contains("^M15\r\n"), "Presentation ran without a saved unknown record.");
                Fake.BeforePrint = null; Bt003Printer.PositionFile = file;
                Check(File.ReadAllLines(file).SequenceEqual(new[] { port, "unknown" }), "Failed follow-up save changed the file.");
                Fake.Calls.Clear();
                Check(printer.Print(Literal).IsSuccess && !Fake.Calls.Any(call => call.StartsWith("^B")) && Fake.Calls.Contains("^M15\r\n"), "Known position was lost after SDK success.");
            }
            // 복귀가 성공한 뒤 인쇄 전 저장이 실패하면 복귀 사실을 남기고 인쇄 SDK는 호출하지 않는다.
            using (var printer = new Bt003Printer()) {
                Check(printer.ConnectUsb(port, 15).IsSuccess, "Connect failed.");
                Fake.AfterSendData = () => Bt003Printer.PositionFile = unwritable;
                Fake.Calls.Clear();
                var returned = printer.Print(Literal);
                Check(!returned.IsSuccess && !returned.PrintInvoked && Fake.Calls.Contains("^B15\r\n") && !Fake.Calls.Contains("StartLabelPrint")
                    && returned.Message.Contains("복귀") && returned.Message.Contains("인쇄 SDK를 호출하지"), "Return evidence was replaced.");
                Fake.AfterSendData = null; Bt003Printer.PositionFile = file;
                Fake.Calls.Clear();
                Check(printer.Print(Literal).IsSuccess && !Fake.Calls.Any(call => call.StartsWith("^B")), "Completed return was repeated.");
            }
        } finally { Fake.BeforePrint = Fake.AfterSendData = null; Bt003Printer.PositionFile = file; }
        // 기록 파일을 쓸 수 없으면 복귀·인쇄 SDK를 호출하지 않는다.
        File.WriteAllText(file, port + "\r\n15"); Restart();
        try {
            using (var printer = new Bt003Printer()) {
                Check(printer.ConnectUsb(port, 15).IsSuccess, "Connect failed.");
                Bt003Printer.PositionFile = Path.Combine(file, "unwritable.txt");
                Fake.Calls.Clear();
                var result = printer.Print(Literal);
                Check(!result.IsSuccess && !result.PrintInvoked && !Fake.Calls.Contains("^B15\r\n") && !Fake.Calls.Contains("StartLabelPrint"), "Unwritable file allowed movement.");
                Check(!printer.ConfirmPrintPosition().IsSuccess, "Unwritable confirm succeeded.");
                // SDK를 호출하지 않았으므로 파일이 복구되면 재확인 없이 저장된 15mm 복귀부터 진행한다.
                Bt003Printer.PositionFile = file; Fake.Calls.Clear();
                Check(printer.Print(Literal).IsSuccess && Fake.Calls.Contains("^B15\r\n"), "Transient file failure required reconfirmation.");
            }
        } finally { Bt003Printer.PositionFile = file; }
    }
    private static void Invalid(Action action, string code = null)
    {
        try { action(); } catch (ArgumentException error) {
            Check(error.InnerException == null && !error.Message.Contains("PRIVATE-MARKER"), "Input leaked.");
            if (code != null) Check(error.Message == code, "Input error code differs.");
            return;
        }
        throw new InvalidOperationException("Invalid input accepted.");
    }
    private static void FormsAndFailures()
    {
        Fake.Clear();
        using (var printer = Connected("SYNTH-FORMS")) {
            string values = "FIRST!!SECOND";
            Fake.BeforePrint = () => values = "MUTATED";
            var result = printer.Print("^L\nAW,10,10,10,1,0,0,^V00\nE\n", values);
            Check(result.IsSuccess && result.PrintInvoked && result.PrintCode == 0, "Print evidence differs.");
            Check(Fake.Variables == "FIRST!!SECOND" && !Fake.LastForm.Replace("\r\n", "").Contains("\n"), "Snapshot/CRLF differs.");
            Check(Fake.Calls.IndexOf("SetPrintFormString") < Fake.Calls.IndexOf("SetPrintFormVariableData") &&
                Fake.Calls.IndexOf("SetPrintFormVariableData") < Fake.Calls.IndexOf("StartLabelPrint"), "Order differs.");
            Fake.BeforePrint = null; Fake.Calls.Clear();
            Check(printer.Print(Literal).IsSuccess && !Fake.Calls.Contains("SetPrintFormVariableData"), "Literal call contract differs.");
            Check(Fake.Variables == "FIRST!!SECOND", "Fake hid variable retention.");
            foreach (string value in new[] { "", "   ", "A\0B", "A\nB", "A\tB", "A😀B" }) {
                Fake.Calls.Clear(); Invalid(() => printer.Print(Literal, value));
                Check(Fake.Calls.Count == 0, "Invalid value reached SDK.");
            }
            Invalid(() => printer.Print("PRIVATE-MARKER\0"));
            Invalid(() => printer.Print(new string('A', 4 * 1024 * 1024 + 1)));
            Fake.FailAt = "SetPrintFormString"; Fake.FailCode = 103; Fake.Calls.Clear();
            result = printer.Print(Literal);
            Check(!result.IsSuccess && !result.PrintInvoked && !Fake.Calls.Contains("StartLabelPrint"), "Registration failure printed.");
        }
    }
    private static void RawVariableData()
    {
        // SDK 치환을 모방하지 않고, 두 공개 경로의 원문 전달과 실패 중단을 확인한다.
        const string form = "^L\nAW,10,10,10,1,0,0,^V00\nAW,10,20,10,1,0,0,^V02\nAW,10,30,10,1,0,0,^V01\nE\n";
        const string data = "홍길동!!1122334455!!20120201!!Blackfish BT-003";
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "raw-variable-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, form, Encoding.GetEncoding(949));
        Fake.Clear();
        try {
            using (var printer = Connected("SYNTH-RAW-VARIABLE")) {
                Fake.Calls.Clear();
                Check(printer.Print(form, data).IsSuccess && Fake.Variables == data, "Manufacturer string changed.");
                Check(Fake.LastForm == form.Replace("\n", "\r\n"), "Variable form changed.");
                Fake.Calls.Clear();
                Check(printer.PrintFile(path, data).IsSuccess && Fake.Variables == data, "File variable string changed.");
                Check(Fake.Calls.IndexOf("SetPrintFormString") < Fake.Calls.IndexOf("SetPrintFormVariableData")
                    && Fake.Calls.IndexOf("SetPrintFormVariableData") < Fake.Calls.IndexOf("StartLabelPrint")
                    && Fake.Calls.Count(call => call == "StartLabelPrint") == 1, "File variable sequence differs.");

                // 공백·단일 !·빈 항목 구분자를 고치지 않는다. 이는 SDK의 빈 항목 지원 검증이 아니다.
                foreach (string raw in new[] { " !FIRST!!SECOND! ", "!!A!!!!B!!" }) {
                    Check(printer.Print(form, raw).IsSuccess && Fake.Variables == raw, "Raw delimiters or spaces changed.");
                }
                string retained = Fake.Variables;
                Fake.Calls.Clear();
                Check(printer.Print(Literal, (string)null).IsSuccess
                    && !Fake.Calls.Contains("SetPrintFormVariableData"), "Explicit null registered variables.");
                Check(Fake.Variables == retained, "Null request pretended to clear retained SDK data.");
                File.WriteAllText(path, Literal, Encoding.GetEncoding(949));
                Fake.Calls.Clear();
                Check(printer.PrintFile(path, (string)null).IsSuccess
                    && !Fake.Calls.Contains("SetPrintFormVariableData"), "File null registered variables.");
                File.WriteAllText(path, form, Encoding.GetEncoding(949));

                foreach (string bad in new[] { "", " ", "A\0B", "A\r\nB", "A😀B", new string('A', 4 * 1024 * 1024 + 1) }) {
                    Fake.Calls.Clear(); Invalid(() => printer.PrintFile(path, bad), "InvalidVariables");
                    Check(Fake.Calls.Count == 0, "Invalid file variable data reached SDK.");
                }
                Fake.FailAt = "SetPrintFormVariableData"; Fake.FailCode = 205; Fake.Calls.Clear();
                var failed = printer.PrintFile(path, data);
                Check(!failed.IsSuccess && failed.NativeCode == 205 && !failed.PrintInvoked && failed.PrintCode == null
                    && failed.Message.Contains("SetPrintFormVariableData") && !failed.Message.Contains("홍길동")
                    && !Fake.Calls.Contains("StartLabelPrint"), "File variable failure printed or leaked input.");
                using (File.Open(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) { }
                // 등록 예외도 인쇄에 도달하지 않고 이전 응답을 NativeCode에 남기지 않는다.
                Fake.FailAt = null; Fake.ThrowAt = "SetPrintFormVariableData"; Fake.Calls.Clear();
                failed = printer.PrintFile(path, data);
                Check(!failed.IsSuccess && failed.NativeCode == null && !failed.PrintInvoked && failed.PrintCode == null
                    && !Fake.Calls.Contains("StartLabelPrint"), "Variable exception reached print or kept stale code.");
                // 변수 등록 실패와 정리 실패가 겹쳐도 파일은 정리 중 잠기고 반환 후 해제된다.
                Fake.ThrowAt = null; Fake.FailAt = "SetPrintFormVariableData"; Fake.CleanupThrows = true;
                Fake.ResetCount = 0;
                bool lockedDuringCleanup = false;
                Fake.BeforeCleanup = () => {
                    try { using (File.Open(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) { } }
                    catch (IOException) { lockedDuringCleanup = true; }
                };
                Fake.Calls.Clear(); failed = printer.PrintFile(path, data);
                // 제품이 정리 예외를 잡아도 잠금 위반은 이 바깥 assertion에서 실패해야 한다.
                Check(lockedDuringCleanup, "Variable failure released file before cleanup.");
                Check(!failed.IsSuccess && failed.NativeCode == 205 && !failed.PrintInvoked && failed.PrintCode == null
                    && failed.Message.Contains("SetPrintFormVariableData") && failed.Message.Contains("ResetForm")
                    && !Fake.Calls.Contains("StartLabelPrint"), "Variable and cleanup evidence lost.");
                using (File.Open(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) { }
                Fake.Calls.Clear();
                Check(!printer.Print(Literal).IsSuccess && !printer.SetPrintingOptions(4, 12).IsSuccess
                    && Fake.Calls.Count == 0, "Variable cleanup fault allowed reuse.");
                Fake.BeforeCleanup = null; Fake.CleanupThrows = false;
            }
        } finally { File.Delete(path); }
    }
    private static void UncertainResponseAndCleanup()
    {
        int index = 0;
        // 정리 예외가 없어도 불명 응답 자체가 false여야 하며, 정리 예외는 원래 설명을 지우지 않는다.
        foreach (bool cleanupThrows in new[] { false, true })
        foreach (string response in new[] { ",101", "garbage,0", ",9999", "" }) {
            string port = "SYNTH-UNKNOWN-" + index++;
            Fake.Clear();
            using (var printer = Connected(port)) {
                Fake.Response = response; Fake.CleanupThrows = cleanupThrows;
                var result = printer.Print(Literal);
                int? expected = response == ",101" ? 101 : response == ",9999" ? 9999 : (int?)null;
                Check(!result.IsSuccess && result.PrintInvoked && result.NativeCode == expected && result.PrintCode == expected
                    && result.Message.Contains("StartLabelPrint") && result.Message.Contains("확정할 수 없")
                    && result.Message.Contains("ResetForm") == cleanupThrows, "Uncertain response or cleanup evidence lost.");
                Fake.Calls.Clear();
                Check(!printer.Print(Literal).IsSuccess && Fake.Calls.Count == 0, "Uncertain response or cleanup fault allowed print.");
            }
            Fake.CleanupThrows = false; Fake.Response = ",0";
            using (var printer = Connected(port, confirm: false)) {
                Fake.Calls.Clear();
                var blocked = printer.Print(Literal);
                Check(!blocked.IsSuccess && blocked.Message.Contains("용지 위치") && !Fake.Calls.Contains("StartLabelPrint"), "New instance lost position uncertainty.");
                Check(printer.ConfirmPrintPosition().IsSuccess && printer.Print(Literal).IsSuccess, "Position confirmation failed.");
            }
        }
        Fake.Clear();
        using (var printer = Connected("SYNTH-CLEANUP")) {
            Fake.CleanupThrows = true;
            var result = printer.Print(Literal);
            Check(!result.IsSuccess && result.PrintInvoked && result.PrintCode == 0 && result.NativeCode == 0
                && result.Message.Contains("StartLabelPrint") && result.Message.Contains("ResetForm"), "Cleanup hid successful print response.");
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
            Check(!result.IsSuccess && !result.PrintInvoked && result.PrintCode == null && result.NativeCode == 7
                && result.Message.Contains("복귀") && result.Message.Contains("SendData"), "Return failure printed or hid movement response.");
            Fake.Calls.Clear();
            var blocked = printer.Print(Literal);
            Check(!blocked.IsSuccess && blocked.Message.Contains("용지 위치") && !Fake.Calls.Contains("SendData"), "Repeated uncertain return.");
        }
        foreach (bool throwing in new[] { false, true }) {
            Fake.Clear();
            using (var printer = Connected("SYNTH-EJECT-" + throwing, 15)) {
                Fake.FailAt = throwing ? null : "SendData"; Fake.FailCode = 7;
                Fake.ThrowAt = throwing ? "SendData" : null;
                var result = printer.Print(Literal);
                Check(!result.IsSuccess
                    && result.PrintInvoked && result.PrintCode == 0 && result.NativeCode == (throwing ? (int?)null : 7)
                    && result.Message.Contains("StartLabelPrint") && result.Message.Contains("배출") && result.Message.Contains("SendData"), "Ejection failure hid print.");
                Fake.Calls.Clear();
                var blocked = printer.Print(Literal);
                Check(!blocked.IsSuccess && blocked.Message.Contains("용지 위치") && !Fake.Calls.Contains("StartLabelPrint"), "Ejection uncertainty lost.");
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
                bool lockedDuringCleanup = false;
                Fake.BeforePrint = locked;
                Fake.BeforeCleanup = () => { locked(); lockedDuringCleanup = true; };
                Fake.CleanupThrows = true;
                Check(printer.PrintFile(file).PrintCode == 0, "File print differs.");
                Check(lockedDuringCleanup, "File lease ended before cleanup.");
                Check(Fake.LastForm == Literal.Replace("\n", "\r\n") && Fake.Calls.Contains("SetPrintFormString")
                    && !Fake.Calls.Contains("SetPrintFormFile") && Fake.Calls.Count(call => call == "StartLabelPrint") == 1, "File snapshot or registration path differs.");
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
            var result = partial.Print(Literal);
            Check(!result.IsSuccess && result.Message.Contains("연결 복구") && !Fake.Calls.Contains("StartLabelPrint"), "Failed connection cleanup allowed printing.");
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
                var print = printer.Print(Literal);
                var options = printer.SetPrintingOptions(4, 12);
                Check(!print.IsSuccess && print.Message.Contains("연결 복구")
                    && !options.IsSuccess && options.Message.Contains("연결 복구") && Fake.Calls.Count == 0,
                    "Unconfirmed disconnect allowed SDK reuse.");
                Fake.KeepOpen = false; Fake.FailAt = null; Fake.ThrowAt = null;
                Check(printer.Disconnect().IsSuccess
                    && printer.ConnectUsb("SYNTH-DISCONNECT-" + failure).IsSuccess
                    && printer.Print(Literal).IsSuccess, "Reconnect did not recover.");
            } finally { Fake.KeepOpen = false; Fake.FailAt = null; Fake.ThrowAt = null; printer.Dispose(); }
        }
        // 같은 요청의 앞선 복귀 응답이 마지막 배출 예외의 코드로 남으면 안 된다.
        Fake.Clear();
        using (var printer = Connected("SYNTH-LAST-MOVEMENT", 15)) {
            Check(printer.Print(Literal).IsSuccess, "First presentation failed.");
            Fake.BeforePrint = () => Fake.ThrowAt = "SendData";
            var result = printer.Print(Literal);
            Check(!result.IsSuccess && result.PrintCode == 0 && result.NativeCode == null
                && result.Message.Contains("배출") && result.Message.Contains("SendData"),
                "Last movement inherited previous response.");
            var blocked = printer.Print(Literal);
            Check(!blocked.IsSuccess && blocked.Message.Contains("용지 위치"), "Uncertain presentation was repeated.");
        }
        Fake.Clear(); Fake.ThrowAt = "CheckPrtConnection";
        var connecting = new Bt003Printer();
        try {
            var result = connecting.ConnectUsb("SYNTH-CONNECTION-EXCEPTION");
            Check(result.Message.Contains("CheckPrtConnection") && result.NativeCode == null && !result.IsSuccess, "Status exception inherited connect response.");
        } finally { Fake.ThrowAt = null; connecting.Dispose(); }
        Fake.Clear();
        using (var printer = Connected("SYNTH-VARIABLE-TOTAL")) {
            Fake.Calls.Clear(); string part = new string('A', 1024 * 1024);
            Invalid(() => printer.Print(Literal, string.Join("!!", new[] { part, part, part, part })));
            Check(Fake.Calls.Count == 0, "Oversized variable total reached SDK.");
        }
    }
    private static void RawVariableByteBoundary()
    {
        Fake.Clear();
        using (var printer = Connected("SYNTH-RAW-BYTES")) {
            // 전체 CP949 바이트 수로 경계를 검사한다. 구분자도 제한 안에 포함된다.
            foreach (string boundary in new[] { new string('A', 4 * 1024 * 1024), new string('가', 2 * 1024 * 1024) }) {
                Fake.Calls.Clear();
                Check(printer.Print(Literal, boundary).IsSuccess && Fake.Variables == boundary, "Exact byte boundary rejected.");
                Fake.Calls.Clear(); Invalid(() => printer.Print(Literal, boundary + "A"));
                Check(Fake.Calls.Count == 0, "One byte above limit reached SDK.");
            }
        }
    }
    private static void MessageAndFailureEvidence()
    {
        // 등록 실패와 정리 실패가 겹쳐도 원래 숫자·설명을 보존하고 미정리 연결의 재사용을 차단한다.
        foreach (string function in new[] { "SetPrintFormString", "SetPrintFormVariableData" }) {
            Fake.Clear();
            using (var printer = Connected("SYNTH-DOUBLE-" + function)) {
                Fake.FailAt = function; Fake.FailCode = 103; Fake.CleanupThrows = true; Fake.Calls.Clear();
                var result = printer.Print(Literal, "VALUE-PRIVATE-MARKER");
                Check(!result.IsSuccess && result.NativeCode == 103 && !result.PrintInvoked && result.PrintCode == null
                    && result.Message.Contains(function) && result.Message.Contains("103") && result.Message.Contains("ResetForm")
                    && !result.Message.Contains("PRIVATE-MARKER") && !Fake.Calls.Contains("StartLabelPrint"), "Registration and cleanup evidence lost.");
                Fake.Calls.Clear();
                Check(!printer.Print(Literal).IsSuccess
                    && !printer.SetPrintingOptions(4, 12).IsSuccess && Fake.Calls.Count == 0, "Double failure reused unclean SDK state.");
            }
        }

        Fake.Clear();
        using (var printer = Connected("SYNTH-PRINT-FAIL-CLEANUP")) {
            Fake.Response = ",103"; Fake.CleanupThrows = true;
            var result = printer.Print(Literal);
            Check(!result.IsSuccess && result.NativeCode == 103 && result.PrintInvoked && result.PrintCode == 103
                && result.Message.Contains("StartLabelPrint") && result.Message.Contains("ResetForm"), "Cleanup replaced known print failure.");
        }

        Fake.Clear();
        using (var printer = Connected("SYNTH-RETURN-EXCEPTION", 15)) {
            Check(printer.Print(Literal).IsSuccess, "Initial presentation failed.");
            Fake.ThrowAt = "SendData"; Fake.Calls.Clear();
            var result = printer.Print(Literal);
            Check(!result.IsSuccess && result.NativeCode == null && !result.PrintInvoked && result.PrintCode == null
                && result.Message.Contains("복귀") && !Fake.Calls.Contains("SetPrintFormString") && !Fake.Calls.Contains("StartLabelPrint"), "Return exception reached registration or kept stale response.");
        }

        // 연결과 해제에 서로 다른 실패 숫자를 주어 추가 정리가 원래 연결 응답을 덮지 않는지 확인한다.
        Fake.Clear(); Fake.ConnectCode = 13; Fake.DisconnectCode = 7;
        var partial = new Bt003Printer();
        try {
            var result = partial.ConnectUsb("SYNTH-CONNECT-DISCONNECT-FAIL");
            Check(!result.IsSuccess && result.NativeCode == 13 && !result.PrintInvoked && result.PrintCode == null
                && result.Message.Contains("DeviceConnect") && result.Message.Contains("13")
                && result.Message.Contains("DeviceDisconnect") && result.Message.Contains("7"), "Connection failure was replaced by cleanup failure.");
            Fake.Calls.Clear();
            using (var other = new Bt003Printer()) Check(!other.ConnectUsb("SYNTH-OTHER-BLOCKED").IsSuccess, "Failed cleanup released owner.");
            Check(!partial.Print(Literal).IsSuccess && !partial.SetPrintingOptions(4, 12).IsSuccess
                && Fake.Calls.Count == 0, "Failed cleanup allowed native reuse.");
        } finally { Fake.ConnectCode = Fake.DisconnectCode = null; partial.Dispose(); }

        // 설명에는 응답 UID나 원문 예외·변수가 들어가지 않아야 한다. SDK 함수와 고정 설명만 전달한다.
        Fake.Clear();
        using (var printer = Connected("SYNTH-MESSAGE-PRIVACY")) {
            Fake.Response = "AB:CD:EF:12,0";
            var result = printer.Print(Literal, "VALUE-PRIVATE-MARKER");
            Check(result.IsSuccess && result.PrintCode == 0
                && !result.Message.Contains("AB:CD:EF:12") && !result.Message.Contains("PRIVATE-MARKER"), "Success message leaked input or UID.");
            Fake.ExceptionText = "PRIVATE-MARKER: synthetic/form.txt"; Fake.ThrowAt = "StartLabelPrint";
            result = printer.Print(Literal);
            Check(!result.IsSuccess && result.PrintInvoked && result.NativeCode == null && result.PrintCode == null
                && result.Message.Contains("StartLabelPrint") && !result.Message.Contains("PRIVATE-MARKER") && !result.Message.Contains("form.txt"), "Exception message lost uncertainty or leaked raw data.");
            Fake.ThrowAt = null; Fake.Calls.Clear();
            Check(!printer.Print(Literal).IsSuccess && Fake.Calls.Count == 0, "Print exception was automatically repeated.");
        }
    }

    private const string TimingSwitch = "U2BIO.BT003PROLabelPrinting.PrintStageTiming";
    private const string TimingPrefix = " [SDK-UTC ";

    private static string WithoutTiming(PrinterResult result, string expected)
    {
        int start = result.Message.IndexOf(TimingPrefix, StringComparison.Ordinal);
        Check(start >= 0 && result.Message.LastIndexOf(TimingPrefix, StringComparison.Ordinal) == start,
            "Timing suffix missing or repeated.");
        string suffix = result.Message.Substring(start);
        var match = System.Text.RegularExpressions.Regex.Match(suffix,
            @"^ \[SDK-UTC B\d{4}=(?<t>\d{8}T\d{6}\.\d{7}Z|-{24})/(?<t>\d{8}T\d{6}\.\d{7}Z|-{24});P=(?<t>\d{8}T\d{6}\.\d{7}Z|-{24})/(?<t>\d{8}T\d{6}\.\d{7}Z|-{24});M\d{4}=(?<t>\d{8}T\d{6}\.\d{7}Z|-{24})/(?<t>\d{8}T\d{6}\.\d{7}Z|-{24})\]$");
        Check(match.Success && suffix.Length <= 200, "Timing suffix is unbounded or contains unexpected payload.");
        string actual = string.Concat(match.Groups["t"].Captures.Cast<System.Text.RegularExpressions.Capture>()
            .Select(c => c.Value[0] == '-' ? "0" : "1"));
        Check(actual == expected, "Timing boundary mismatch: " + actual + " != " + expected);
        foreach (System.Text.RegularExpressions.Capture capture in match.Groups["t"].Captures) {
            if (capture.Value[0] != '-') Check(DateTime.TryParseExact(capture.Value, "yyyyMMdd'T'HHmmss.fffffff'Z'",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal,
                out _), "Invalid UTC timestamp.");
        }
        return result.Message.Substring(0, start);
    }

    private static void PrintStageTiming()
    {
        // 진단은 호출 경계만 검사한다. cold firmware나 물리 이동을 fake로 재현한 결과가 아니다.
        bool defined;
        Check(!AppContext.TryGetSwitch(TimingSwitch, out defined) || !defined, "Timing should default off.");
        string file = Bt003Printer.PositionFile;
        string blockedFile = Path.Combine(file, "unwritable.txt");
        var cases = new[] {
            new { Name = "normal", Pending = 15, Distance = 7, Marks = "111111" },
            new { Name = "zero-new", Pending = 15, Distance = 0, Marks = "111100" },
            new { Name = "zero-old", Pending = 0, Distance = 15, Marks = "001111" },
            new { Name = "b-code", Pending = 15, Distance = 15, Marks = "110000" },
            new { Name = "b-throw", Pending = 15, Distance = 15, Marks = "100000" },
            new { Name = "p-throw", Pending = 15, Distance = 15, Marks = "111000" },
            new { Name = "p-code", Pending = 15, Distance = 15, Marks = "111100" },
            new { Name = "p-unknown", Pending = 15, Distance = 15, Marks = "111100" },
            new { Name = "cleanup", Pending = 15, Distance = 15, Marks = "111111" },
            new { Name = "m-code", Pending = 15, Distance = 15, Marks = "111111" },
            new { Name = "m-throw", Pending = 15, Distance = 15, Marks = "111110" },
            new { Name = "b-save", Pending = 15, Distance = 15, Marks = "000000" },
            new { Name = "p-save", Pending = 15, Distance = 15, Marks = "110000" },
            new { Name = "m-save", Pending = 15, Distance = 15, Marks = "111100" }
        };
        try {
            foreach (var item in cases) {
                Tuple<PrinterResult, string[], string> baseline = null;
                for (int enabled = 0; enabled != 2; ++enabled) {
                    AppContext.SetSwitch(TimingSwitch, enabled != 0);
                    Fake.Clear(); Restart();
                    string port = "SYNTH-TIMING-" + item.Name;
                    File.WriteAllLines(file, new[] { port, item.Pending.ToString() });
                    using (var printer = Connected(port, item.Distance, false)) {
                        Fake.Calls.Clear();
                        switch (item.Name) {
                            case "b-code": Fake.FailAt = "SendData"; Fake.FailCode = 13; break;
                            case "b-throw": Fake.ThrowAt = "SendData"; break;
                            case "p-throw": Fake.ThrowAt = "StartLabelPrint"; break;
                            case "p-code": Fake.Response = ",101"; break;
                            case "p-unknown": Fake.Response = "PRIVATE-RESPONSE"; break;
                            case "cleanup": Fake.CleanupThrows = true; break;
                            case "m-code": Fake.BeforePrint = () => { Fake.FailAt = "SendData"; Fake.FailCode = 7; }; break;
                            case "m-throw": Fake.BeforePrint = () => Fake.ThrowAt = "SendData"; break;
                            case "b-save": Bt003Printer.PositionFile = blockedFile; break;
                            case "p-save": Fake.AfterSendData = () => Bt003Printer.PositionFile = blockedFile; break;
                            case "m-save": Fake.BeforePrint = () => Bt003Printer.PositionFile = blockedFile; break;
                        }
                        Fake.ExceptionText = "PRIVATE-EXCEPTION";
                        var result = printer.Print(Literal, "PRIVATE-VARIABLE");
                        Bt003Printer.PositionFile = file;
                        if (enabled == 0) {
                            Check(!result.Message.Contains(TimingPrefix), "Disabled timing changed the message.");
                            baseline = Tuple.Create(result, Fake.Calls.ToArray(), File.ReadAllText(file));
                        } else {
                            Check(WithoutTiming(result, item.Marks) == baseline.Item1.Message
                                && result.IsSuccess == baseline.Item1.IsSuccess && result.NativeCode == baseline.Item1.NativeCode
                                && result.PrintCode == baseline.Item1.PrintCode && result.PrintInvoked == baseline.Item1.PrintInvoked,
                                "Timing changed the original result: " + item.Name);
                            Check(Fake.Calls.SequenceEqual(baseline.Item2) && File.ReadAllText(file) == baseline.Item3,
                                "Timing changed SDK order or position history: " + item.Name);
                            Check(!result.Message.Contains("PRIVATE-") && result.Message.Length <= baseline.Item1.Message.Length + 200,
                                "Timing leaked payload or exceeded its bound.");
                        }
                    }
                }
            }
            Fake.Clear(); Restart(); AppContext.SetSwitch(TimingSwitch, true);
            using (var printer = Connected("SYNTH-TIMING-SWITCH", 15)) {
                Fake.BeforePrint = () => AppContext.SetSwitch(TimingSwitch, false);
                Check(WithoutTiming(printer.Print(Literal), "001111").Length > 0, "Request did not capture enabled switch.");
                Fake.BeforePrint = () => AppContext.SetSwitch(TimingSwitch, true);
                Check(!printer.Print(Literal).Message.Contains(TimingPrefix), "Switch changed an in-flight disabled request.");
                Fake.BeforePrint = null;
                Check(WithoutTiming(printer.Print(Literal), "111111").Length > 0, "Next request did not see switch.");
                Fake.ThrowAt = "StartLabelPrint";
                Check(!printer.Print(Literal).IsSuccess, "Synthetic print exception expected.");
                Fake.ThrowAt = null; Fake.Calls.Clear();
                var unknown = printer.Print(Literal);
                Check(!unknown.IsSuccess && Fake.Calls.Count == 0 && !unknown.Message.Contains(TimingPrefix),
                    "Unknown position was bypassed or retained the preceding request's timestamps.");
            }
        } finally { Bt003Printer.PositionFile = file; AppContext.SetSwitch(TimingSwitch, false); }
    }

    private const string PostBPauseSwitch = "U2BIO.BT003PROLabelPrinting.DiagnosticPostBPause1000";

    private static bool ConsumeInterrupt()
    {
        try { Thread.Sleep(0); return false; }
        catch (ThreadInterruptedException) { return true; }
    }

    private static void PostBPauseDiagnostic()
    {
        // 실제 모터 완료를 흉내 내지 않는다. B 반환 이후 첫 ResetForm 전의 시간과 중단만 검사한다.
        const string port = "SYNTH-POST-B";
        string file = Bt003Printer.PositionFile;
        try {
            foreach (bool enabled in new[] { false, true }) {
                AppContext.SetSwitch(PostBPauseSwitch, enabled);
                Fake.Clear(); Restart(); File.WriteAllLines(file, new[] { port, "15" });
                using (var printer = Connected(port, 15, false)) {
                    Fake.Calls.Clear();
                    var watch = new System.Diagnostics.Stopwatch();
                    long beforeResetMs = -1;
                    Fake.AfterSendData = () => {
                        if (Fake.Calls[Fake.Calls.Count - 2] == "^B15\r\n") watch.Restart();
                    };
                    Fake.BeforeReset = () => { if (beforeResetMs < 0) beforeResetMs = watch.ElapsedMilliseconds; };
                    // P 뒤의 새 Sleep은 없어야 한다. pending interrupt는 다음 명시적 Sleep에서만 소비한다.
                    Fake.BeforePrint = () => Thread.CurrentThread.Interrupt();
                    var result = printer.Print(Literal);
                    bool unconsumed = ConsumeInterrupt();
                    Check(result.IsSuccess && result.PrintInvoked && result.PrintCode == 0 && unconsumed,
                        "Diagnostic altered print result or added a post-print wait.");
#if BT003_POST_B_PAUSE_DIAGNOSTIC
                    if (enabled) Check(beforeResetMs >= 950, "Post-B diagnostic did not wait before ResetForm.");
#endif
                    Check(Fake.Calls.SequenceEqual(new[] { "CheckPrtConnection", "^B15\r\n", "SendData", "ResetForm",
                        "SetPrintingOption", "SetPrintFormString", "StartLabelPrint", "^M15\r\n", "SendData", "ResetForm" }),
                        "Post-B diagnostic changed SDK commands/order.");
                    Check(File.ReadAllLines(file).SequenceEqual(new[] { port, "15" }), "Post-B diagnostic changed position history.");
                    if (!enabled) Check(beforeResetMs >= 0, "Reset boundary not observed.");
                }
            }
            AppContext.SetSwitch(PostBPauseSwitch, true);
            // fake의 B 반환 시점에 스레드 중단을 예약한다. 제품에 테스트 hook을 추가하지 않는다.
            Fake.Clear(); Restart(); File.WriteAllLines(file, new[] { port, "15" });
            using (var printer = Connected(port, 15, false)) {
                Fake.Calls.Clear(); Fake.AfterSendData = () => Thread.CurrentThread.Interrupt();
                var result = printer.Print(Literal);
                bool unconsumed = ConsumeInterrupt();
#if BT003_POST_B_PAUSE_DIAGNOSTIC
                Check(!result.IsSuccess && !result.PrintInvoked && result.PrintCode == null && result.NativeCode == 0
                    && result.Message.Contains("진단 대기") && !unconsumed,
                    "Diagnostic interruption did not preserve B response and stop before print.");
                Check(Fake.Calls.SequenceEqual(new[] { "CheckPrtConnection", "^B15\r\n", "SendData" })
                    && File.ReadAllLines(file).SequenceEqual(new[] { port, "0" }), "Diagnostic interruption altered B history/cleanup.");
#else
                Check(result.IsSuccess && result.PrintInvoked && unconsumed,
                    "Normal build unexpectedly contains the post-B pause.");
#endif
            }
            // B가 없거나 B가 거부된 경로는 switch가 true여도 새 대기를 수행하지 않는다.
            foreach (string pending in new[] { "0", "unknown", "15" }) {
                Fake.Clear(); Restart(); File.WriteAllLines(file, new[] { port, pending });
                using (var printer = Connected(port, 15, false)) {
                    Fake.Calls.Clear();
                    if (pending == "15") { Fake.FailAt = "SendData"; Fake.FailCode = 13; }
                    Fake.AfterSendData = () => Thread.CurrentThread.Interrupt();
                    var result = printer.Print(Literal);
                    bool unconsumed = ConsumeInterrupt();
                    Check(pending == "0" ? result.IsSuccess && unconsumed : !result.PrintInvoked && !result.IsSuccess,
                        "Post-B pause bypassed pending0/unknown/B failure.");
                    if (pending == "15") Check(unconsumed, "Failed B entered diagnostic pause.");
                    if (pending == "unknown") Check(Fake.Calls.Count == 0, "Unknown position reached SDK.");
                }
            }
#if BT003_POST_B_PAUSE_DIAGNOSTIC
            // opt-in은 첫 SDK 호출 전에 고정된다. B callback에서 off로 바꿔도 이번 요청은 on이다.
            Fake.Clear(); Restart(); File.WriteAllLines(file, new[] { port, "15" });
            using (var printer = Connected(port, 15, false)) {
                var watch = new System.Diagnostics.Stopwatch();
                Fake.AfterSendData = () => { watch.Restart(); AppContext.SetSwitch(PostBPauseSwitch, false); };
                Fake.BeforeReset = () => { if (Fake.ResetCount == 0) Check(watch.ElapsedMilliseconds >= 950, "Diagnostic opt-in not captured."); };
                Check(printer.Print(Literal).IsSuccess, "Captured diagnostic print failed.");
            }
            // PrintFile 잠금과 Gate는 진단 대기 전체를 포함한다. Dispose도 작업 종료 전에는 완료되지 않는다.
            AppContext.SetSwitch(PostBPauseSwitch, true);
            Fake.Clear(); Restart(); File.WriteAllLines(file, new[] { port, "15" });
            string form = Path.Combine(Path.GetDirectoryName(file), "post-b-form.txt");
            File.WriteAllText(form, Literal, Encoding.GetEncoding(949));
            var owned = Connected(port, 15, false);
            using (var bReturned = new ManualResetEvent(false))
            using (var disposed = new ManualResetEvent(false)) {
                Exception workerError = null; PrinterResult printed = null;
                Fake.AfterSendData = () => { if (Fake.Calls[Fake.Calls.Count - 2] == "^B15\r\n") bReturned.Set(); };
                var worker = new Thread(() => { try { printed = owned.PrintFile(form); } catch (Exception e) { workerError = e; } });
                var disposer = new Thread(() => { try { owned.Dispose(); } catch (Exception e) { workerError = e; } finally { disposed.Set(); } });
                worker.Start();
                try {
                    Check(bReturned.WaitOne(10000), "Diagnostic worker did not return from B.");
                    bool locked = false;
                    try { using (File.Open(form, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) { } }
                    catch (IOException) { locked = true; }
                    disposer.Start();
                    Check(locked && !disposed.WaitOne(50), "File lock/Gate released during diagnostic pause.");
                } finally {
                    worker.Join();
                    if (disposer.ThreadState == ThreadState.Unstarted) owned.Dispose(); else disposer.Join();
                    File.Delete(form);
                }
                Check(workerError == null && printed != null && printed.IsSuccess && disposed.WaitOne(0), "Diagnostic lifetime request failed.");
            }
#endif
        } finally { ConsumeInterrupt(); AppContext.SetSwitch(PostBPauseSwitch, false); Bt003Printer.PositionFile = file; }
    }

    private static int Main()
    {
        // 실제 운영 기록을 건드리지 않도록 실행 폴더의 임시 위치 파일을 사용한다.
        string positions = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "position-" + Guid.NewGuid().ToString("N") + ".txt");
        Bt003Printer.PositionFile = positions;
        // 다른 검사는 실제 대기 없이 순서만 확인한다. 간격 검사는 MinimumInterval에서 따로 한다.
        Bt003Printer.MinimumIntervalMs = 0;
        try {
            PrintStageTiming();
            PostBPauseDiagnostic();
            for (int timing = 0; timing != 2; ++timing) {
                AppContext.SetSwitch(TimingSwitch, timing != 0);
                FormsAndFailures(); RawVariableData(); UncertainResponseAndCleanup(); Movement(); FileLifetime(); Ownership(); ReviewRegressions(); RawVariableByteBoundary(); MessageAndFailureEvidence(); PositionFile(); FeedPosition(); MinimumInterval();
            }
            Console.WriteLine("PASS: linked product bool results, SDK sequencing, input snapshots, uncertain response/cleanup, movement, persisted position, feed confirmation, file lifetime and ownership; managed SDK substitute only");
            return 0;
        } catch (Exception error) { Console.Error.WriteLine("FAIL: managed SDK harness (" + error.GetType().Name + "): " + error.Message); return 1; }
        finally { File.Delete(positions); File.Delete(positions + ".tmp"); }
    }
}
