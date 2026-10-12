using System;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using U2BIO.BT003PROLabelPrinting;

namespace U2BIO.BT003PROLabelPrinting.Validation
{
    internal static class PrinterPresentationTests
    {
        private static void Check(bool value) { if (!value) throw new InvalidOperationException("Presentation contract failed."); }
        private static object Property(object value, string name) => value.GetType().GetProperty(name).GetValue(value);
        private static Bt003ConnectionOptions Options(string port, int distance)
        {
            var method = typeof(Bt003ConnectionOptions).GetMethod("WithPresentationDistance");
            if (method == null) throw new InvalidOperationException("Presentation API missing.");
            return (Bt003ConnectionOptions)method.Invoke(Bt003ConnectionOptions.Usb(port), new object[] { distance });
        }
        private static void Confirm(Bt003Printer printer)
        {
            var method = typeof(Bt003Printer).GetMethod("ConfirmPrintPosition");
            Check(method != null && ((PrinterOperationResult)method.Invoke(printer, null)).IsSuccess);
        }
        private static int? Pending(Bt003Printer printer) => (int?)Property(printer.GetConnectionStatus(), "PendingReturnDistanceMm");
        private static string Movement(BatchPrintResult result, string property) => Property(result, property).ToString();
        private static void Response(IntPtr pointer, int code = 0)
        {
            var bytes = Encoding.ASCII.GetBytes("," + code + "\0"); Marshal.Copy(bytes, 0, pointer, bytes.Length);
        }
        private static PrinterConnectionTests.Fixture Fixture() => new PrinterConnectionTests.Fixture { Printed = (c, p) => { Check(c == 1); Response(p); } };
        private static void Open(Bt003Printer printer, PrinterConnectionTests.Fixture fixture, string port, int distance)
        {
            Check(printer.Connect(Options(port, distance)).IsSuccess); fixture.Calls.Clear();
        }
        private static BtplPrintRequest Batch()
        {
            var table = new DataTable(); table.Rows.Add(table.NewRow()); table.Rows.Add(table.NewRow());
            return BtplPrintRequest.FromTable(PrinterTests.Request().Template, BtplBindings.Parse("{\"variables\":{}}"), table);
        }
        private static void Sequence()
        {
            var plain = Bt003ConnectionOptions.Usb("presentation-sequence");
            Check((int)Property(plain, "PresentationDistanceMm") == 0);
            foreach (int bad in new[] { -1, 1001, int.MaxValue }) {
                bool rejected = false;
                try { Options("presentation-bounds", bad); } catch (TargetInvocationException error) { rejected = error.InnerException is ArgumentOutOfRangeException; }
                Check(rejected);
            }
            Check((int)Property(Options("presentation-bounds", 1000), "PresentationDistanceMm") == 1000);
            var f = Fixture();
            using (var p = f.Printer()) {
                Open(p, f, "presentation-sequence", 15);
                Check((int)Property(plain, "PresentationDistanceMm") == 0 && Pending(p) == 0);
                f.Calls.Clear(); Check(p.ValidateForPrint(PrinterTests.Request()).IsReady);
                Check(string.Join("|", f.Calls) == "Status"); f.Calls.Clear();
                var first = p.Print(PrinterTests.Request());
                Check(first.Succeeded == 1 && first.Code == "Ok" && Movement(first, "PresentationStatus") == "CommandAccepted");
                Check(Movement(first, "PositionReturnStatus") == "NotAttempted" && Pending(p) == 15);
                Check(string.Join("|", f.Calls) == "Status|Status|Reset|Form|Print|Advance:15|Reset|Status|Status");
                Check(p.Connect(Options("presentation-sequence", 7)).Code == "AlreadyConnected");
                f.Calls.Clear(); var second = p.Print(Batch());
                Check(second.Succeeded == 2 && Movement(second, "PositionReturnStatus") == "CommandAccepted");
                Check(f.Calls.Count(x => x == "Return:15") == 1 && f.Calls.Count(x => x == "Advance:15") == 1);
                Check(f.Calls.IndexOf("Return:15") < f.Calls.IndexOf("Print") && f.Calls.LastIndexOf("Print") < f.Calls.IndexOf("Advance:15"));
                f.Calls.Clear(); Check(p.Disconnect().IsSuccess && !f.Calls.Any(x => x.StartsWith("Return:") || x.StartsWith("Advance:")));
            }
            f = Fixture();
            using (var p = f.Printer()) {
                Open(p, f, "presentation-sequence", 7); var third = p.Print(PrinterTests.Request());
                Check(third.Succeeded == 1 && f.Calls.Contains("Return:15") && f.Calls.Contains("Advance:7") && Pending(p) == 7);
            }
            f = Fixture();
            using (var p = f.Printer()) {
                Open(p, f, "presentation-sequence", 0); var fourth = p.Print(PrinterTests.Request());
                Check(fourth.Succeeded == 1 && f.Calls.Contains("Return:7") && !f.Calls.Any(x => x.StartsWith("Advance:")) && Pending(p) == 0);
            }
        }
        private static void NoMotion()
        {
            foreach (string mode in new[] { "invalid", "empty", "cancel", "print-fail", "print-unknown", "cancel-after-print", "dispose-after-print" }) {
                var f = Fixture(); var p = f.Printer();
                using (var token = new CancellationTokenSource()) try {
                    Open(p, f, "presentation-no-motion-" + mode, 15);
                    var request = PrinterTests.Request();
                    if (mode == "invalid") request = BtplPrintRequest.Fixed(request.Template.WithText(0, request.Template.TextElements[0].WithPayload("^V00")));
                    if (mode == "empty") request = BtplPrintRequest.FromTable(request.Template, BtplBindings.Parse("{\"variables\":{}}"), new DataTable());
                    if (mode == "cancel") token.Cancel();
                    if (mode == "print-fail") f.Printed = (c, ptr) => Response(ptr, 9);
                    if (mode == "print-unknown") f.Printed = (c, ptr) => Response(ptr, 101);
                    if (mode == "cancel-after-print") f.Printed = (c, ptr) => { Response(ptr); token.Cancel(); };
                    if (mode == "dispose-after-print") f.Printed = (c, ptr) => { Response(ptr); p.Dispose(); };
                    var result = p.PrintAsync(request, token.Token).GetAwaiter().GetResult();
                    Check(!f.Calls.Any(x => x.StartsWith("Return:") || x.StartsWith("Advance:")));
                    Check(Movement(result, "PresentationStatus") == "NotAttempted");
                    if (mode.EndsWith("after-print")) Check(result.Succeeded == 1 && result.Code != "Ok");
                } finally { p.Dispose(); }
            }
            var plainFixture = Fixture();
            using (var p = plainFixture.Printer()) {
                Open(p, plainFixture, "presentation-default-zero", 0); Check(p.Print(PrinterTests.Request()).Succeeded == 1);
                Check(!plainFixture.Calls.Any(x => x.StartsWith("Return:") || x.StartsWith("Advance:")));
            }
        }
        private static void Failures()
        {
            foreach (bool forward in new[] { true, false }) foreach (bool throwing in new[] { false, true }) {
                string port = "presentation-fail-" + forward + throwing; var f = Fixture();
                using (var p = f.Printer()) {
                    Open(p, f, port, 15);
                    if (!forward) Check(p.Print(PrinterTests.Request()).Succeeded == 1);
                    f.Calls.Clear();
                    f.Moved = (d, dir) => { if (throwing) throw new InvalidOperationException("synthetic"); return 9; };
                    var result = p.Print(PrinterTests.Request());
                    Check(result.Code == (throwing ? "MediaMovementUnknown" : "MediaMovementFailed"));
                    Check(result.Succeeded == (forward ? 1 : 0) && result.NotAttemptedCount == (forward ? 0 : 1));
                    Check(Movement(result, forward ? "PresentationStatus" : "PositionReturnStatus") == (throwing ? "Unknown" : "Failed"));
                    Check(Pending(p) == null && f.Calls.Count(x => x.StartsWith("Return:") || x.StartsWith("Advance:")) == 1);
                    Check(p.Disconnect().IsSuccess); f.OwnerThread = 0;
                    Check(p.Connect(Options(port, 15)).IsSuccess); f.Calls.Clear();
                    Check(!p.ValidateForPrint(PrinterTests.Request()).IsReady);
                    Check(p.Print(PrinterTests.Request()).NotAttemptedCount == 1 && !f.Calls.Contains("Print"));
                    f.Moved = null; Confirm(p); f.Calls.Clear();
                    Check(p.Print(PrinterTests.Request()).Succeeded == 1 && !f.Calls.Any(x => x.StartsWith("Return:")));
                    Confirm(p);
                }
            }
        }
        private static void PendingCalls()
        {
            foreach (bool forward in new[] { true, false }) foreach (string stop in new[] { "timeout", "cancel", "dispose" }) {
                string port = "presentation-pending-" + forward + stop; var f = Fixture(); var p = f.Printer(stop == "timeout" ? 50 : 1000);
                using (var token = new CancellationTokenSource()) try {
                    Open(p, f, port, 15);
                    if (!forward) Check(p.Print(PrinterTests.Request()).Succeeded == 1);
                    f.Calls.Clear();
                    f.Moved = (d, dir) => { f.Entered.Set(); Check(f.Release.Wait(5000)); return 0; };
                    var task = p.PrintAsync(PrinterTests.Request(), token.Token); Check(f.Entered.Wait(1000));
                    Check(p.Print(PrinterTests.Request()).Code == "Busy");
                    if (stop == "timeout") {
                        Check(task.Wait(2000)); var early = task.Result;
                        Check(early.Code == "NativeCallTimeout" && early.NativeCallPending);
                        Check(early.Succeeded == (forward ? 1 : 0) && Movement(early, forward ? "PresentationStatus" : "PositionReturnStatus") == "Unknown");
                        f.Release.Set(); Check(SpinWait.SpinUntil(() => p.GetConnectionStatus().ResourcesReleased, 3000));
                        Check(Pending(p) == null && Movement(early, forward ? "PresentationStatus" : "PositionReturnStatus") == "Unknown");
                    } else {
                        if (stop == "cancel") token.Cancel(); else p.Dispose();
                        f.Release.Set(); Check(task.Wait(3000)); var result = task.Result;
                        Check(Movement(result, forward ? "PresentationStatus" : "PositionReturnStatus") == "CommandAccepted");
                        Check(result.Succeeded == (forward ? 1 : 0) && Pending(p) == (forward ? (int?)15 : 0));
                        if (!forward) Check(!f.Calls.Contains("Print"));
                    }
                } finally { f.Release.Set(); p.Dispose(); }
            }
        }
        private static void ReturnThenZeroFailure()
        {
            foreach (string mode in new[] { "known-failure", "unknown", "throw" }) {
                string port = "presentation-return-zero-" + mode; var first = Fixture();
                using (var p = first.Printer()) {
                    Open(p, first, port, 15); Check(p.Print(PrinterTests.Request()).Succeeded == 1);
                }
                var second = Fixture();
                second.Printed = (c, ptr) => {
                    if (mode == "throw") throw new InvalidOperationException("synthetic");
                    Response(ptr, mode == "known-failure" ? 9 : 101);
                };
                using (var p = second.Printer()) {
                    Open(p, second, port, 0); var result = p.Print(PrinterTests.Request());
                    Check(Movement(result, "PositionReturnStatus") == "CommandAccepted" && second.Calls.Contains("Return:15"));
                    Check(result.Succeeded == 0 && result.PendingReturnDistanceMm == null && Pending(p) == null);
                }
                var third = Fixture();
                using (var p = third.Printer()) {
                    Open(p, third, port, 0); var blocked = p.Print(PrinterTests.Request());
                    Check(blocked.Code == "MediaPositionUnknown" && blocked.NotAttemptedCount == 1 && !third.Calls.Contains("Print"));
                    Confirm(p); third.Calls.Clear(); Check(p.Print(PrinterTests.Request()).Succeeded == 1);
                    Check(!third.Calls.Any(x => x.StartsWith("Return:") || x.StartsWith("Advance:")));
                }
            }
        }
        internal static void Run()
        {
            // Runtime lookup allows the baseline product to compile and fail for the missing feature.
            Options("presentation-api", 0);
            Sequence(); NoMotion(); Failures(); PendingCalls(); ReturnThenZeroFailure();
            Console.WriteLine("PASS: opt-in presentation, return, batch-last, target lifetime and movement failure/timeout/cancellation contracts; synthetic SDK only");
        }
    }
}
