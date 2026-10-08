using System;
using System.Data;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using U2BIO.BT003PROLabelPrinting;

namespace U2BIO.BT003PROLabelPrinting.Validation
{
    internal static class PrinterBatchTests
    {
        private static void Check(bool condition, [CallerLineNumber] int line = 0)
        {
            if (condition) return;
            Console.WriteLine("FAIL: synthetic batch assertion at " + line);
            throw new InvalidOperationException("Batch print contract failed at " + line + ".");
        }
        private static void Response(IntPtr pointer, int code)
        { var bytes = Encoding.ASCII.GetBytes("," + code + "\0"); Marshal.Copy(bytes, 0, pointer, bytes.Length); }
        private static PrinterConnectionTests.Fixture Fixture() => new PrinterConnectionTests.Fixture { Printed = (copies, pointer) => { Check(copies == 1); Response(pointer, 0); } };
        private static BtplPrintRequest Request(bool invalid = false)
        {
            var table = new DataTable(); table.Columns.Add("Text", typeof(string));
            table.Rows.Add("ROW-1"); table.Rows.Add(invalid ? " " : "ROW-2"); table.Rows.Add("ROW-3");
            var template = PrinterTests.Request().Template;
            template = template.WithText(0, template.TextElements[0].WithPayload("^V00"));
            var bindings = BtplBindings.Parse("{\"variables\":{\"^V00\":{\"column\":\"Text\"}}}");
            return BtplPrintRequest.FromTable(template, bindings, table);
        }
        private static void Open(Bt003Printer p, PrinterConnectionTests.Fixture f)
        {
            Check(p.Connect(Bt003ConnectionOptions.Usb("synthetic-batch")).IsSuccess);
            f.Calls.Clear(); f.FormNumber = f.PrintNumber = f.StatusNumber = f.ResetNumber = 0;
        }
        private static void States(BatchPrintResult result, string expected, PrintStopReason reason)
        {
            string actual = string.Concat(result.Rows.Select(x => x.Status == PrintRowStatus.Succeeded ? "S" : x.Status == PrintRowStatus.Failed ? "F" : x.Status == PrintRowStatus.Unknown ? "U" : "N"));
            Check(actual == expected && result.StopReason == reason);
            Check(result.Succeeded + result.Failed + result.Unknown + result.NotAttemptedCount == result.Rows.Count);
            Check(result.Rows.Select(x => x.RowIndex).SequenceEqual(Enumerable.Range(0, result.Rows.Count)));
            foreach (var row in result.Rows.Where(x => x.Status == PrintRowStatus.NotAttempted)) Check(!row.PrintInvoked && row.NativeFunction == null && row.NativeCode == null);
        }
        private static void PartialValidation()
        {
            var table = new DataTable(); table.Columns.Add("Text", typeof(string));
            for (int i = 0; i < 10000; i++) table.Rows.Add(new string('A', 2048));
            var template = PrinterTests.Request().Template;
            template = template.WithText(0, template.TextElements[0].WithPayload("^V00"));
            var request = BtplPrintRequest.FromTable(template, BtplBindings.Parse("{\"variables\":{\"^V00\":{\"column\":\"Text\"}}}"), table);
            var f = Fixture();
            using (var p = f.Printer()) using (var token = new CancellationTokenSource()) {
                Open(p, f);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var gate = typeof(Bt003Printer).GetField("sync", flags).GetValue(p);
                Task<BatchPrintResult> task; Array partial;
                lock (gate) {
                    task = p.PrintAsync(request, token.Token);
                    var work = typeof(Bt003Printer).GetField("next", flags).GetValue(p);
                    var data = work.GetType().GetField("Printing", flags).GetValue(work);
                    partial = (Array)data.GetType().GetField("PartialManaged", flags).GetValue(data);
                }
                Check(SpinWait.SpinUntil(() => {
                    lock (gate) {
                        if (partial.GetValue(0) == null) return false;
                        Check(partial.GetValue(partial.Length - 1) == null);
                        token.Cancel(); return true;
                    }
                }, 3000));
                Check(task.Wait(3000)); var result = task.Result;
                Check(result.StopReason == PrintStopReason.Cancelled && result.Validation == null && result.DeviceValidation == null);
                Check(result.Rows.Count == request.RowCount && result.NotAttemptedCount == request.RowCount);
                Check(result.Rows[0].Validation != null && result.Rows.Last().Validation == null && f.Calls.Count == 0);
            }
        }
        private static void OtherOwner()
        {
            var f = Fixture(); var other = Fixture(); var request = Request();
            using (var owner = f.Printer()) using (var p = other.Printer()) using (var token = new CancellationTokenSource()) {
                Open(owner, f); token.Cancel();
                Check(p.PrintAsync(request, token.Token).Result.StopReason == PrintStopReason.Busy);
                Check(p.ValidateForPrint(request).Code == "Busy" && p.Disconnect().Code == "Busy");
                Check(other.Calls.Count == 0 && p.GetConnectionStatus().ResourcesReleased);
            }
        }
        private static void PreflightFailurePriority()
        {
            foreach (bool dispose in new[] { false, true }) foreach (int status in new[] { 0, 9 }) {
                var f = Fixture(); var p = f.Printer();
                using (var token = new CancellationTokenSource()) try {
                    Open(p, f); f.Mode = "status-pending";
                    var task = p.PrintAsync(Request(), token.Token); Check(f.Entered.Wait(1000));
                    f.Port = status;
                    if (dispose) p.Dispose(); else token.Cancel();
                    f.Release.Set(); Check(task.Wait(3000)); var result = task.Result;
                    States(result, "NNN", status == 0 ? PrintStopReason.NotConnected : PrintStopReason.SdkFailure);
                    Check(result.Code == (status == 0 ? "NotConnected" : "StatusUnavailable") && result.NativeCode == status);
                    Check(result.DeviceValidation != null && !result.DeviceValidation.IsValid && f.PrintNumber == 0);
                    f.Mode = null; f.Port = 0;
                } finally { f.Release.Set(); f.Mode = null; f.Port = 0; p.Dispose(); }
            }
        }
        private static void LateReturnSnapshot()
        {
            var f = Fixture(); var p = f.Printer(50);
            using (var returned = new ManualResetEventSlim()) try {
                Open(p, f);
                f.Printed = (copies, pointer) => {
                    f.Entered.Set(); Check(f.Release.Wait(5000)); Response(pointer, 0); returned.Set();
                };
                var gate = typeof(Bt003Printer).GetField("sync", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(p);
                var task = p.PrintAsync(PrinterTests.Request()); Check(f.Entered.Wait(1000));
                lock (gate) {
                    Thread.Sleep(80); f.Release.Set(); Check(returned.Wait(1000)); Thread.Sleep(20);
                }
                Check(task.Wait(3000)); var result = task.Result;
                Check(result.StopReason == PrintStopReason.Timeout && result.Unknown == 1 && !result.NativeCallPending);
                Check(SpinWait.SpinUntil(() => p.GetConnectionStatus().ResourcesReleased, 3000));
                Check(result.Unknown == 1 && !result.NativeCallPending && f.PrintNumber == 1);
            } finally { f.Release.Set(); p.Dispose(); }
        }
        internal static void Run()
        {
            OtherOwner(); PreflightFailurePriority(); LateReturnSnapshot();
            var request = Request(); var f = Fixture();
            using (var p = f.Printer()) {
                Open(p, f);
                var invalid = p.Print(Request(true));
                States(invalid, "NNN", PrintStopReason.ValidationFailed);
                Check(invalid.Validation.Rows.Select(x => x.IsValid).SequenceEqual(new[] { true, false, true }) && invalid.DeviceValidation == null && f.Calls.Count == 0);
                using (var token = new CancellationTokenSource()) {
                    token.Cancel(); var cancelled = p.PrintAsync(request, token.Token).GetAwaiter().GetResult();
                    States(cancelled, "NNN", PrintStopReason.Cancelled);
                    Check(cancelled.Validation == null && cancelled.DeviceValidation == null && f.Calls.Count == 0);
                }
                var table = new DataTable();
                var empty = p.Print(BtplPrintRequest.FromTable(request.Template, BtplBindings.Parse("{\"variables\":{}}"), table));
                Check(empty.StopReason == PrintStopReason.NoData && empty.Rows.Count == 0 && !empty.Validation.IsValid && empty.DeviceValidation == null && f.Calls.Count == 0);
            }
            using (var p = new Bt003Printer()) using (var token = new CancellationTokenSource()) {
                token.Cancel(); States(p.PrintAsync(request, token.Token).Result, "NNN", PrintStopReason.Cancelled);
                Check(p.GetConnectionStatus().ResourcesReleased);
            }
            PartialValidation();
            foreach (string mode in new[] { "row-closed", "form-second-fail", "reset-cleanup-fail" }) {
                f = Fixture();
                using (var p = f.Printer()) {
                    Open(p, f); f.Mode = mode;
                    var result = p.Print(request);
                    if (mode == "row-closed") { States(result, "FNN", PrintStopReason.NotConnected); Check(f.PrintNumber == 0); }
                    if (mode == "form-second-fail") { States(result, "SFN", PrintStopReason.SdkFailure); Check(f.PrintNumber == 1 && result.Rows[1].NativeCode == 9 && !result.Rows[1].PrintInvoked); }
                    if (mode == "reset-cleanup-fail") {
                        States(result, "SSS", PrintStopReason.None);
                        Check(result.CleanupCode == "FormResetFailed" && !p.GetConnectionStatus().ResourcesReleased && p.GetConnectionStatus().State == PrinterState.Faulted);
                        f.Mode = null; Check(p.Disconnect().IsSuccess);
                    }
                }
            }
            foreach (string mode in new[] { "cancel-success", "cancel-fail", "cancel-form", "dispose", "last-cancel", "second-timeout", "form-timeout", "preflight-timeout" }) {
                Console.WriteLine("CHECK: synthetic batch " + mode);
                f = Fixture();
                var p = f.Printer(mode.EndsWith("timeout", StringComparison.Ordinal) ? 50 : 1000);
                using (var token = new CancellationTokenSource()) {
                    try {
                        Open(p, f);
                        if (mode == "form-timeout" || mode == "cancel-form") f.Mode = "form-pending";
                        if (mode == "preflight-timeout") f.Mode = "status-pending";
                        f.Printed = (copies, pointer) => {
                            Check(copies == 1);
                            if (mode == "last-cancel" && f.PrintNumber == 3) token.Cancel();
                            if ((mode == "second-timeout" && f.PrintNumber == 2) || ((mode.StartsWith("cancel-", StringComparison.Ordinal) || mode == "dispose") && f.PrintNumber == 1)) {
                                f.Entered.Set(); Check(f.Release.Wait(5000));
                            }
                            Response(pointer, mode == "cancel-fail" ? 9 : 0);
                        };
                        var task = p.PrintAsync(request, token.Token);
                        if (mode != "last-cancel") {
                            Check(f.Entered.Wait(1000));
                            var busy = p.PrintAsync(request, CancellationToken.None).GetAwaiter().GetResult();
                            States(busy, "NNN", PrintStopReason.Busy); Check(!busy.NativeCallPending && busy.Validation == null);
                            if (mode.StartsWith("cancel-", StringComparison.Ordinal)) { token.Cancel(); f.Release.Set(); }
                            if (mode == "dispose") { p.Dispose(); f.Release.Set(); }
                        }
                        Check(task.Wait(3000)); var result = task.Result;
                        if (mode == "cancel-success") States(result, "SNN", PrintStopReason.Cancelled);
                        if (mode == "cancel-fail") States(result, "FNN", PrintStopReason.SdkFailure);
                        if (mode == "cancel-form") { States(result, "NNN", PrintStopReason.Cancelled); Check(f.PrintNumber == 0 && f.FormNumber == 1 && result.Validation.IsValid); }
                        if (mode == "dispose") States(result, "SNN", PrintStopReason.Disposed);
                        if (mode == "last-cancel") States(result, "SSS", PrintStopReason.None);
                        if (mode == "second-timeout") States(result, "SUN", PrintStopReason.Timeout);
                        if (mode == "form-timeout") { States(result, "FNN", PrintStopReason.Timeout); Check(result.Rows.All(x => !x.PrintInvoked)); }
                        if (mode == "preflight-timeout") { States(result, "NNN", PrintStopReason.Timeout); Check(result.DeviceValidation == null); }
                        if (mode.EndsWith("timeout", StringComparison.Ordinal)) {
                            Check(result.NativeCallPending); f.Release.Set();
                            Check(SpinWait.SpinUntil(() => p.GetConnectionStatus().ResourcesReleased, 3000));
                            Check(result.NativeCallPending && result.Rows[1].Status == (mode == "second-timeout" ? PrintRowStatus.Unknown : PrintRowStatus.NotAttempted));
                            Check(f.PrintNumber == (mode == "second-timeout" ? 2 : 0));
                            Check(p.Disconnect().IsSuccess);
                        }
                        if (mode == "dispose") Check(p.GetConnectionStatus().State == PrinterState.Disposed && p.GetConnectionStatus().ResourcesReleased);
                    } finally { f.Release.Set(); p.Dispose(); }
                }
            }
            Console.WriteLine("PASS: C02-C13/C16 batch counts, no-start validation, async admission, cancel/failure precedence, timeout stages, immutable late results and cleanup; synthetic SDK only");
        }
    }
}
