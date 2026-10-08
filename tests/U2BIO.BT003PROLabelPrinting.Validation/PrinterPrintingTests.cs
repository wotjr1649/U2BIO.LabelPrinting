using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using U2BIO.BT003PROLabelPrinting;

namespace U2BIO.BT003PROLabelPrinting.Validation
{
    internal static class PrinterPrintingTests
    {
        private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Print wrapper contract failed."); }
        private static void Response(IntPtr pointer, string response)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(response + "\0"); Marshal.Copy(bytes, 0, pointer, bytes.Length);
        }
        private static PrinterConnectionTests.Fixture Fixture(string response = ",0")
            => new PrinterConnectionTests.Fixture { Printed = (copies, pointer) => { Check(copies == 1); Response(pointer, response); } };
        private static void Open(Bt003Printer printer, PrinterConnectionTests.Fixture fixture)
        {
            Check(printer.Connect(Bt003ConnectionOptions.Usb("synthetic-print")).IsSuccess);
            fixture.Calls.Clear();
        }
        internal static void Run()
        {
            var request = PrinterTests.Request();
            var f = Fixture();
            using (var p = f.Printer()) {
                Open(p, f);
                var ready = p.ValidateForPrint(request);
                Check(ready.IsReady && ready.ManagedValidation.IsValid && ready.DeviceValidation.IsValid);
                Check(ready.State == PrinterState.Connected);
                Check(string.Join("|", f.Calls) == "Status");
                f.Calls.Clear();
                var result = p.Print(request.Template.ToBtpl());
                Check(result.Succeeded == 1 && result.StopReason == PrintStopReason.None);
                Check(result.Rows[0].PrintInvoked && result.Rows[0].NativeCode == 0 && result.Rows[0].CompletionEvidence == PrintCompletionEvidence.SdkResponse);
                Check(string.Join("|", f.Calls) == "Status|Status|Reset|Form|Print|Reset|Status");
                Check(f.RegisteredForm.EndsWith("E\r\n", StringComparison.Ordinal) && !f.RegisteredForm.Replace("\r\n", "").Contains("\n"));
                f.Calls.Clear();
                var unsupported = BtplTemplate.Parse(request.Template.ToBtpl().Replace("AW,80,120,10,1,0,0,", "AW,80,120,1,1,0,0,"));
                var blocked = p.Print(unsupported);
                Check(blocked.StopReason == PrintStopReason.ValidationFailed && blocked.NotAttemptedCount == 1);
                Check(blocked.DeviceValidation.Rows[0].Issues.Any(x => x.Code == "DeviceSupportUnverified") && !f.Calls.Contains("Print"));
                Check(p.ValidateForPrint(unsupported).State == PrinterState.Connected);
                f.Calls.Clear();
                var unmappable = p.Print(request.Template.WithText(0, request.Template.TextElements[0].WithPayload("\ud83d\ude00")));
                Check(unmappable.DeviceValidation.Rows[0].Issues.Any(x => x.Code == "EncodingNotSupported") && !f.Calls.Contains("Print"));
                f.Calls.Clear();
                var missing = p.Print(request.Template.WithText(0, request.Template.TextElements[0].WithPayload("^V00")));
                Check(missing.Validation != null && !missing.Validation.IsValid && missing.DeviceValidation == null && f.Calls.Count == 0);
                f.Port = 0;
                var closed = p.ValidateForPrint(request);
                Check(closed.Code == "NotConnected" && closed.ManagedValidation.IsValid && !closed.DeviceValidation.IsValid && closed.NativeCode == 0);
                Check(p.GetConnectionStatus().State == PrinterState.Faulted);
            }
            foreach (string response in new[] { ",9", ",101", "unexpected", ",-2147483648" }) {
                f = Fixture(response);
                using (var p = f.Printer()) {
                    Open(p, f);
                    var result = p.Print(request);
                    Check(result.Rows[0].PrintInvoked && f.Calls.Count(x => x == "Print") == 1);
                    if (response == ",9") Check(result.Failed == 1 && result.Rows[0].NativeCode == 9 && result.Rows[0].CompletionEvidence == PrintCompletionEvidence.SdkResponse);
                    else Check(result.Unknown == 1 && !result.NativeCallPending);
                    if (response == ",101") Check(result.StopReason == PrintStopReason.Timeout && result.Rows[0].NativeCode == 101);
                    if (response == "unexpected") Check(result.StopReason == PrintStopReason.ResponseUnknown);
                    if (response == ",-2147483648") Check(result.StopReason == PrintStopReason.ResponseUnknown && result.Rows[0].NativeCode == int.MinValue);
                }
            }
            f = Fixture(); f.Mode = "form-fail";
            using (var p = f.Printer()) {
                Open(p, f);
                var result = p.Print(request);
                Check(result.Failed == 1 && !result.Rows[0].PrintInvoked && result.Rows[0].NativeCode == 9);
                Check(result.Rows[0].ExecutionStage == PrintExecutionStage.FormRegistration && !f.Calls.Contains("Print"));
            }
            foreach (bool printing in new[] { false, true }) {
                f = Fixture();
                if (!printing) f.Mode = "form-pending";
                else f.Printed = (copies, pointer) => { f.Entered.Set(); Check(f.Release.Wait(5000)); Response(pointer, ",0"); };
                var p = f.Printer(50);
                try {
                    Open(p, f);
                    var active = Task.Run(() => p.Print(request));
                    Check(f.Entered.Wait(1000));
                    var busy = p.Print(request);
                    Check(busy.StopReason == PrintStopReason.Busy && !busy.NativeCallPending && !busy.Rows[0].PrintInvoked);
                    Check(active.Wait(2000)); var result = active.Result;
                    Check(result.StopReason == PrintStopReason.Timeout && result.NativeCallPending);
                    Check(printing ? result.Unknown == 1 && result.Rows[0].PrintInvoked : result.Failed == 1 && !result.Rows[0].PrintInvoked);
                    f.Release.Set();
                    Check(SpinWait.SpinUntil(() => p.GetConnectionStatus().ResourcesReleased, 3000));
                    Check(p.GetConnectionStatus().State == PrinterState.Faulted && result.NativeCallPending);
                    Check(f.Calls.Count(x => x == "Print") == (printing ? 1 : 0));
                    Check(p.Disconnect().IsSuccess);
                } finally { f.Release.Set(); p.Dispose(); }
            }
            f = Fixture();
            using (var p = f.Printer()) {
                Open(p, f);
                var template = request.Template.WithText(0, request.Template.TextElements[0].WithPayload(new string('A', 100)))
                    .AddText(new BtplTextElement("AW", 80, 160, new[] { "10", "1", "0", "0" }, "SECOND LINE"));
                var result = p.Print(template);
                Check(result.Succeeded == 1 && f.Calls.Count(x => x == "Print") == 1);
                Check(f.RegisteredForm.Contains(new string('A', 100)) && f.RegisteredForm.Contains("SECOND LINE"));
            }
            f = Fixture();
            f.Printed = (copies, pointer) => { f.Entered.Set(); Check(f.Release.Wait(5000)); Response(pointer, ",0"); };
            var disposed = f.Printer();
            try {
                Open(disposed, f);
                var printing = Task.Run(() => disposed.Print(request));
                Check(f.Entered.Wait(1000)); disposed.Dispose(); f.Release.Set();
                Check(printing.Wait(3000) && printing.Result.Succeeded == 1 && printing.Result.StopReason == PrintStopReason.None);
                Check(SpinWait.SpinUntil(() => disposed.GetConnectionStatus().ResourcesReleased, 3000));
                Check(disposed.GetConnectionStatus().State == PrinterState.Disposed);
            } finally { f.Release.Set(); disposed.Dispose(); }
            Console.WriteLine("PASS: preflight without output, one wrapper print, strict encoding/AW support, response classification, no retry, late registration/print cleanup; synthetic SDK only");
        }
    }
}
