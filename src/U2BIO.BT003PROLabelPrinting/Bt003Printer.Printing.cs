using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using U2BIO.BT003PROLabelPrinting.Native;

namespace U2BIO.BT003PROLabelPrinting
{
    public sealed partial class Bt003Printer
    {
        private static readonly HashSet<int> KnownPrintFailures = new HashSet<int>(Enumerable.Range(1, 11).Concat(new[] { 20, 21, 22, 50, 60, 102, 103, 104, 105, 205 }));
        private sealed class PrintingData
        {
            internal readonly BtplPrintRequest Request;
            internal readonly bool PreflightOnly;
            internal readonly RowPrintResult[] Rows;
            internal BatchValidationResult Managed;
            internal DeviceValidationResult Device;
            internal BtplRenderResult[] Rendered;
            internal int CurrentRow = -1;
            internal PrintExecutionStage Stage;
            internal bool Invoked, FormTouched;
            internal PrintStopReason StopReason;
            internal string Code = "Ok", Function;
            internal int? NativeCode;
            internal PrintingData(BtplPrintRequest request, bool preflightOnly)
            { Request = request; PreflightOnly = preflightOnly; Rows = new RowPrintResult[request.RowCount]; }
        }
        private BtplPrintRequest Prepare(string btpl)
        {
            lock (sync) RequireAlive();
            return BtplPrintRequest.Fixed(BtplTemplate.Parse(btpl));
        }
        private BtplPrintRequest Prepare(BtplTemplate template)
        {
            lock (sync) RequireAlive();
            return BtplPrintRequest.Fixed(template);
        }
        public PrintPreflightResult ValidateForPrint(string btpl) => ValidateForPrint(Prepare(btpl));
        public PrintPreflightResult ValidateForPrint(BtplTemplate template) => ValidateForPrint(Prepare(template));
        public PrintPreflightResult ValidateForPrint(BtplPrintRequest request)
        {
            Work work;
            lock (sync) {
                RequireAlive(); if (request == null) throw new ArgumentNullException(nameof(request));
                if (working) return new PrintPreflightResult("Busy", null, null, state);
                if (state != PrinterState.Connected) return new PrintPreflightResult("NotConnected", null, null, state);
                work = Start(PreflightWork, PrinterState.Validating, new PrintingData(request, true));
            }
            return (PrintPreflightResult)work.Completion.Task.GetAwaiter().GetResult();
        }
        private object PreflightWork(Work work)
        {
            try { return Preflight(work); }
            finally { if (work.TimedOut || disposed) Close(work, work.TimedOut); }
        }
        private PrintPreflightResult Preflight(Work work)
        {
            var data = work.Printing;
            try {
                CheckStop(work); data.Stage = PrintExecutionStage.ManagedValidation;
                var managed = BtplProcessor.Validate(data.Request);
                lock (sync) data.Managed = managed;
                if (!managed.IsValid) return PreflightResult(work, data.Request.RowCount == 0 ? "NoData" : "ValidationFailed");
                CheckStop(work); data.Stage = PrintExecutionStage.DeviceValidation;
                var common = new List<ValidationIssue>();
                if (target.Transport != "usb") common.Add(new ValidationIssue("DeviceSupportUnverified", null));
                if (Encoding.Default.CodePage != 949) common.Add(new ValidationIssue("EncodingNotSupported", null));
                var encoding = Encoding.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                var rows = new List<RowValidationResult>();
                var rendered = new BtplRenderResult[data.Request.RowCount];
                for (int i = 0; i < rendered.Length; i++) {
                    CheckStop(work); rendered[i] = BtplProcessor.Render(data.Request, i);
                    var issues = new List<ValidationIssue>();
                    foreach (var text in rendered[i].TextElements) {
                        if (text.Command != "AW" || !text.Arguments.Select(x => int.Parse(x, CultureInfo.InvariantCulture)).SequenceEqual(new[] { 10, 1, 0, 0 }))
                            issues.Add(new ValidationIssue("DeviceSupportUnverified", text.Line, i));
                    }
                    try { encoding.GetByteCount(rendered[i].BtplText); }
                    catch (EncoderFallbackException) { issues.Add(new ValidationIssue("EncodingNotSupported", null, i)); }
                    rows.Add(new RowValidationResult(i, issues));
                }
                data.Rendered = rendered;
                int status = Status(work);
                if (status != 1) {
                    common.Add(new ValidationIssue(status == 0 ? "NotConnected" : "StatusUnavailable", null));
                    lock (sync) state = PrinterState.Faulted;
                }
                lock (sync) data.Device = new DeviceValidationResult(common, rows, port);
                return PreflightResult(work, status == 0 ? "NotConnected" : status != 1 ? "StatusUnavailable" : data.Device.IsValid ? "Ok" : "ValidationFailed",
                    "CheckPrtConnection", status);
            } catch (Stopped) { return PreflightResult(work, disposed ? "Disposed" : "NativeCallTimeout", work.Function); }
            catch (Exception error) when (Recoverable(error)) {
                lock (sync) { state = disposed ? PrinterState.Disposed : PrinterState.Faulted; port = PrinterPortState.Unknown; nativeCode = null; }
                return PreflightResult(work, "StatusUnavailable", work.Function);
            } finally {
                lock (sync) if (state == PrinterState.Validating) state = disposed ? PrinterState.Disposed : PrinterState.Connected;
            }
        }
        private PrintPreflightResult PreflightResult(Work work, string resultCode, string function = null, int? value = null)
        {
            lock (sync) {
                if (state == PrinterState.Validating) state = disposed ? PrinterState.Disposed : PrinterState.Connected;
                return new PrintPreflightResult(resultCode, work.Printing.Managed, work.Printing.Device,
                    disposed ? PrinterState.Disposed : state, function, value, pending);
            }
        }
        public BatchPrintResult Print(string btpl) => Print(Prepare(btpl));
        public BatchPrintResult Print(BtplTemplate template) => Print(Prepare(template));
        public BatchPrintResult Print(BtplPrintRequest request)
        {
            Work work;
            lock (sync) {
                RequireAlive(); if (request == null) throw new ArgumentNullException(nameof(request));
                if (working) return Immediate(request, PrintStopReason.Busy, "Busy");
                if (state != PrinterState.Connected) return Immediate(request, PrintStopReason.NotConnected, state == PrinterState.Faulted ? "PrinterFaulted" : "NotConnected");
                work = Start(PrintWork, PrinterState.Validating, new PrintingData(request, false));
            }
            return (BatchPrintResult)work.Completion.Task.GetAwaiter().GetResult();
        }
        private static BatchPrintResult Immediate(BtplPrintRequest request, PrintStopReason reason, string resultCode)
            => new BatchPrintResult(reason, resultCode, Enumerable.Range(0, request.RowCount).Select(i => new RowPrintResult(i, PrintRowStatus.NotAttempted, reason, resultCode)));
        private static PrintStopReason Reason(string resultCode)
        {
            switch (resultCode) {
                case "Ok": return PrintStopReason.None;
                case "NotConnected": return PrintStopReason.NotConnected;
                case "NoData": return PrintStopReason.NoData;
                case "Disposed": return PrintStopReason.Disposed;
                case "NativeCallTimeout": case "SdkTimeout": return PrintStopReason.Timeout;
                case "StatusUnavailable": case "FormRegistrationFailed": case "PrintFailed": case "NativeCallFailed": return PrintStopReason.SdkFailure;
                case "InvalidPrintResponse": case "UnrecognizedPrintResponse": return PrintStopReason.ResponseUnknown;
                default: return PrintStopReason.ValidationFailed;
            }
        }
        private void StopPrinting(Work work, string resultCode, string function = null, int? value = null)
        {
            lock (sync) {
                var data = work.Printing; data.Code = resultCode; data.StopReason = Reason(resultCode); data.Function = function; data.NativeCode = value;
            }
        }
        private void Row(Work work, PrintRowStatus status, string resultCode, string function = null, int? value = null, PrintCompletionEvidence evidence = PrintCompletionEvidence.None)
        {
            lock (sync) {
                var data = work.Printing;
                var reason = status == PrintRowStatus.Unknown && resultCode == "NativeCallFailed" ? PrintStopReason.ResponseUnknown : Reason(resultCode);
                data.Rows[data.CurrentRow] = new RowPrintResult(data.CurrentRow, status, reason, resultCode,
                    data.Managed?.Rows[data.CurrentRow], data.Device?.Rows[data.CurrentRow], data.Stage, data.Invoked, function, value, evidence);
                if (status != PrintRowStatus.Succeeded) { StopPrinting(work, resultCode, function, value); data.StopReason = reason; }
            }
        }
        private BatchPrintResult Batch(Work work)
        {
            lock (sync) {
                var data = work.Printing;
                return new BatchPrintResult(data.StopReason, data.Code, Enumerable.Range(0, data.Rows.Length).Select(i => data.Rows[i] ??
                    new RowPrintResult(i, PrintRowStatus.NotAttempted, data.StopReason, data.Code, data.Managed?.Rows[i], data.Device?.Rows[i])),
                    data.Managed, data.Device, data.Function, data.NativeCode, pending, cleanup, work.Calls);
            }
        }
        private object TimeoutPrinting(Work work)
        {
            var data = work.Printing;
            if (data.PreflightOnly) return PreflightResult(work, "NativeCallTimeout", work.Function);
            if (data.CurrentRow < 0) StopPrinting(work, "NativeCallTimeout", work.Function);
            else if (data.Rows[data.CurrentRow] == null) Row(work, data.Invoked ? PrintRowStatus.Unknown : PrintRowStatus.Failed, "NativeCallTimeout", work.Function);
            return Batch(work);
        }
        private object PrintWork(Work work)
        {
            var data = work.Printing;
            try {
                var preflight = Preflight(work);
                if (!preflight.IsReady) StopPrinting(work, preflight.Code, preflight.NativeFunction, preflight.NativeCode);
                else {
                    lock (sync) state = PrinterState.Printing;
                    for (int i = 0; i < data.Rows.Length; i++) {
                        CheckStop(work);
                        lock (sync) { data.CurrentRow = i; data.Invoked = false; data.Stage = PrintExecutionStage.ConnectionCheck; }
                        int status = Status(work, receiveResult: true);
                        if (status != 1) {
                            lock (sync) state = PrinterState.Faulted;
                            Row(work, PrintRowStatus.Failed, status == 0 ? "NotConnected" : "StatusUnavailable", "CheckPrtConnection", status); break;
                        }
                        data.Stage = PrintExecutionStage.FormReset;
                        VoidCall(work, "ResetForm", sdk.Reset, entered: () => data.FormTouched = true);
                        data.Stage = PrintExecutionStage.FormRegistration;
                        int form = Call(work, "SetPrintFormString", () => sdk.Form(data.Rendered[i].BtplText), receiveResult: true);
                        if (form != 0) { Row(work, PrintRowStatus.Failed, "FormRegistrationFailed", "SetPrintFormString", form); break; }
                        data.Stage = PrintExecutionStage.PrintInvocation;
                        var response = Call(work, "StartLabelPrint", () => NativeLabelResponse.Capture(pointer => {
                            lock (sync) { CheckStop(work); data.Invoked = true; }
                            sdk.Print(1, pointer);
                            lock (sync) data.Stage = PrintExecutionStage.ResponseValidation;
                        }), receiveResult: true);
                        data.Stage = PrintExecutionStage.ResponseValidation;
                        if (response.NativeCode == 101) { Row(work, PrintRowStatus.Unknown, "SdkTimeout", "StartLabelPrint", response.NativeCode); break; }
                        if (response.NativeCode != 0 && !KnownPrintFailures.Contains(response.NativeCode)) {
                            Row(work, PrintRowStatus.Unknown, "UnrecognizedPrintResponse", "StartLabelPrint", response.NativeCode); break;
                        }
                        Row(work, response.NativeCode == 0 ? PrintRowStatus.Succeeded : PrintRowStatus.Failed,
                            response.NativeCode == 0 ? "Ok" : "PrintFailed", "StartLabelPrint", response.NativeCode, PrintCompletionEvidence.SdkResponse);
                        if (response.NativeCode != 0) break;
                    }
                }
            } catch (Stopped) {
                if (!work.TimedOut) StopPrinting(work, "Disposed");
            } catch (Exception error) when (Recoverable(error)) {
                if (data.CurrentRow >= 0) Row(work, data.Invoked ? PrintRowStatus.Unknown : PrintRowStatus.Failed,
                    data.Invoked ? error is NativeSdkException && ((NativeSdkException)error).Code == "PrintResponseInvalid" ? "InvalidPrintResponse" : "NativeCallFailed" : "NativeCallFailed", work.Function);
                else StopPrinting(work, "StatusUnavailable", work.Function);
            } finally {
                CleanupPrinting(work);
            }
            return Batch(work);
        }
        private void CleanupPrinting(Work work)
        {
            var data = work.Printing; bool resetFailed = false;
            if (data.FormTouched) {
                try { VoidCall(work, "ResetForm", sdk.Reset, true); }
                catch (Exception error) when (Recoverable(error)) {
                    lock (sync) { cleanup = "FormResetFailed"; code = "FormResetFailed"; state = disposed ? PrinterState.Disposed : PrinterState.Faulted; }
                    resetFailed = true;
                }
            }
            if (work.TimedOut || disposed || data.Rows.Any(x => x != null && x.Status == PrintRowStatus.Unknown)) Close(work, work.TimedOut || !disposed);
            else if (!resetFailed && data.FormTouched) {
                try {
                    int status = Status(work, true);
                    if (work.TimedOut || status == 0) Close(work, work.TimedOut);
                    else lock (sync) {
                        state = status == 1 ? PrinterState.Connected : PrinterState.Faulted;
                        code = status == 1 ? "Ok" : "StatusUnavailable";
                        if (status != 1) cleanup = "NativeCallFailed";
                    }
                } catch (Exception error) when (Recoverable(error)) { lock (sync) { state = PrinterState.Faulted; cleanup = "NativeCallFailed"; } }
            } else if (port == PrinterPortState.Closed && connectionAttempted && data.CurrentRow >= 0) Close(work, false);
            else lock (sync) if (state == PrinterState.Printing) state = PrinterState.Connected;
            if (resetFailed) lock (sync) cleanup = "FormResetFailed";
        }
    }
}
