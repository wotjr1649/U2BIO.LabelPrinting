using System;
using System.Data;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using U2BIO.BT003PROLabelPrinting;

namespace U2BIO.BT003PROLabelPrinting.Validation
{
    internal static class PrinterBatchTests
    {
        private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Batch print contract failed."); }
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
        internal static void Run()
        {
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
            }
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
            foreach (string mode in new[] { "cancel-success", "cancel-fail", "dispose", "last-cancel", "second-timeout", "form-timeout", "preflight-timeout" }) {
                f = Fixture();
                var p = f.Printer(mode.EndsWith("timeout", StringComparison.Ordinal) ? 50 : 1000);
                using (var token = new CancellationTokenSource()) {
                    try {
                        Open(p, f);
                        if (mode == "form-timeout") f.Mode = "form-pending";
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
