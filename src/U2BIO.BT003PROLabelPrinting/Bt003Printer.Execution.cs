using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using U2BIO.BT003PROLabelPrinting.Native;
using System.Globalization;

namespace U2BIO.BT003PROLabelPrinting
{
    public sealed partial class Bt003Printer : IDisposable
    {
        private static readonly object ProcessSync = new object();
        private static Bt003Printer processOwner;
        private readonly object sync = new object();
        private readonly AutoResetEvent ready = new AutoResetEvent(false);
        private readonly PrinterSdkCalls sdk;
        private readonly int waitMilliseconds;
        private Thread worker;
        private Work next;
        private bool working, disposed, processClaim, loaded, connectionAttempted, conflict, pending, reservationAbandoned;
        private Mutex processMutex, targetMutex;
        private Bt003ConnectionOptions target;
        private ushort? printingSpeed, printingDarkness;
        private PrinterState state = PrinterState.Disconnected;
        private PrinterPortState port = PrinterPortState.Closed;
        private string code = "Ok", cleanup = "None";
        private int? nativeCode;

        private sealed class Work
        {
            internal readonly TaskCompletionSource<object> Completion = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            internal TaskCompletionSource<BatchPrintResult> PrintCompletion;
            internal readonly List<NativeCallResult> Calls = new List<NativeCallResult>();
            internal Func<Work, object> Body;
            internal PrintingData Printing;
            internal bool Terminal, TimedOut, InCall, CleanupAttempted;
            internal long NativeReturnedAt;
            internal int Generation;
            internal string Function;
            internal string CleanupFailureFunction;
            internal int? CleanupSystemError;
        }
        private sealed class Stopped : Exception { }

        internal Bt003Printer(TimeSpan nativeWaitTimeout, PrinterSdkCalls calls)
        {
            if (nativeWaitTimeout.TotalMilliseconds < 1 || nativeWaitTimeout.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(nativeWaitTimeout));
            waitMilliseconds = (int)Math.Ceiling(nativeWaitTimeout.TotalMilliseconds);
            sdk = calls ?? throw new ArgumentNullException(nameof(calls));
        }

        private void RequireAlive() { if (disposed) throw new ObjectDisposedException(nameof(Bt003Printer)); }
        private bool Released => !processClaim && processMutex == null && targetMutex == null && !connectionAttempted && !pending;
        private PrinterConnectionStatus Snapshot(bool busy = false, bool ownPending = true)
            => new PrinterConnectionStatus(disposed ? PrinterState.Disposed : state, busy ? PrinterPortState.Unknown : port,
                disposed ? "Disposed" : busy && state != PrinterState.Faulted ? "Busy" : code, nativeCode, ownPending && pending, Released, cleanup, PendingReturnDistance);
        private PrinterOperationResult Result(Work work, string resultCode, string function = null, int? value = null, int? system = null)
        {
            lock (sync) return new PrinterOperationResult(resultCode, Snapshot(), function, value, system, work?.Calls);
        }
        private PrinterOperationResult Busy()
            => new PrinterOperationResult("Busy", Snapshot(true, false));
        private bool OtherOwner
        {
            get { lock (ProcessSync) return processOwner != null && !ReferenceEquals(processOwner, this); }
        }

        private Work Start(Func<Work, object> body, PrinterState activeState, PrintingData printing = null)
        {
            var work = new Work { Body = body, Printing = printing };
            if (printing != null && !printing.PreflightOnly)
                work.PrintCompletion = new TaskCompletionSource<BatchPrintResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            working = true; state = disposed ? PrinterState.Disposed : activeState; next = work;
            if (worker == null) {
                worker = new Thread(RunWorker) { IsBackground = true, Name = "BT003 SDK" };
                worker.Start();
            }
            ready.Set(); return work;
        }
        private void RunWorker()
        {
            while (true) {
                ready.WaitOne(); Work work;
                lock (sync) { work = next; next = null; }
                object result = null; Exception failure = null;
                try { result = work.Body(work); }
                catch (Exception error) { failure = error; }
                while (true) {
                    lock (sync) {
                        if (!disposed || Released || work.CleanupAttempted) {
                            working = false;
                            if (!work.Terminal) {
                                work.Terminal = true;
                                if (failure == null) {
                                    var operation = result as PrinterOperationResult;
                                    if (disposed && operation != null && operation.IsSuccess) result = Result(work, "Disposed");
                                    var preflight = result as PrintPreflightResult;
                                    if (disposed && preflight != null)
                                        result = new PrintPreflightResult(preflight.IsReady ? "Disposed" : preflight.Code, preflight.ManagedValidation,
                                            preflight.DeviceValidation, PrinterState.Disposed, preflight.NativeFunction, preflight.NativeCode, preflight.NativeCallPending);
                                    Complete(work, result);
                                }
                                else if (work.PrintCompletion != null) work.PrintCompletion.TrySetException(failure);
                                else work.Completion.TrySetException(failure);
                            }
                            if (!processClaim) { worker = null; if (disposed) ready.Dispose(); return; }
                            break;
                        }
                    }
                    Close(work, false);
                }
            }
        }
        private void CheckStop(Work work)
        {
            lock (sync) if (work.TimedOut || disposed || (work.Printing != null && work.Printing.Cancellation.IsCancellationRequested)) throw new Stopped();
        }
        private static void Complete(Work work, object result)
        {
            if (work.PrintCompletion != null) work.PrintCompletion.TrySetResult((BatchPrintResult)result);
            else work.Completion.TrySetResult(result);
        }
        private static void NativeReturned(Work work)
        {
            if (Volatile.Read(ref work.NativeReturnedAt) == 0)
                Volatile.Write(ref work.NativeReturnedAt, Stopwatch.GetTimestamp());
        }
        private T Call<T>(Work work, string function, Func<T> call, bool cleaning = false, Action entered = null, bool receiveResult = false)
        {
            int generation; long started;
            lock (sync) {
                if (!cleaning) CheckStop(work);
                entered?.Invoke();
                work.Function = function; work.InCall = true; work.NativeReturnedAt = 0; generation = ++work.Generation;
                pending = true; started = Stopwatch.GetTimestamp();
            }
            Timer deadlineTimer = null;
            Action<long?> timeout = returnedAt => {
                lock (sync) {
                    if (work.Generation != generation || (!returnedAt.HasValue && (!work.InCall || Volatile.Read(ref work.NativeReturnedAt) != 0))) return;
                    double elapsed = (returnedAt.GetValueOrDefault(Stopwatch.GetTimestamp()) - started) * 1000.0 / Stopwatch.Frequency;
                    if (elapsed < waitMilliseconds) {
                        if (!returnedAt.HasValue) deadlineTimer.Change((int)Math.Ceiling(waitMilliseconds - elapsed), Timeout.Infinite);
                        return;
                    }
                    state = disposed ? PrinterState.Disposed : PrinterState.Faulted;
                    port = PrinterPortState.Unknown; nativeCode = null; code = "NativeCallTimeout"; cleanup = cleaning ? "Timeout" : "Pending";
                    work.TimedOut = true;
                    if (!work.Terminal) {
                        work.Terminal = true;
                        Complete(work, work.Printing == null ? (object)Result(work, "NativeCallTimeout", function) : TimeoutPrinting(work));
                    }
                }
            };
            using (var timer = new Timer(_ => timeout(null), null, Timeout.Infinite, Timeout.Infinite)) {
                deadlineTimer = timer;
                timer.Change(waitMilliseconds, Timeout.Infinite);
                T value;
                try { value = call(); }
                catch (Exception error) when (Recoverable(error)) {
                    NativeReturned(work);
                    lock (sync) work.Calls.Add(new NativeCallResult(function, null, (error as NativeSdkException)?.SystemErrorCode));
                    throw;
                }
                finally {
                    NativeReturned(work);
                    lock (sync) { work.InCall = false; pending = false; timeout(Volatile.Read(ref work.NativeReturnedAt)); }
                }
                lock (sync) work.Calls.Add(new NativeCallResult(function, value is int ? (int?)(int)(object)value : null));
                if (!cleaning) {
                    if (!receiveResult) CheckStop(work);
                    else lock (sync) if (work.TimedOut) throw new Stopped();
                }
                return value;
            }
        }
        private void VoidCall(Work work, string function, Action call, bool cleaning = false, Action entered = null)
            => Call(work, function, () => { call(); return (object)null; }, cleaning, entered);
        private static bool Recoverable(Exception error) => error is NativeSdkException || error is InvalidOperationException ||
            error is ArgumentException || error is System.IO.IOException || error is UnauthorizedAccessException || error is SecurityException ||
            error is DllNotFoundException || error is BadImageFormatException || error is EntryPointNotFoundException;
        private static string ErrorCode(Exception error) => error is NativeSdkException ? ((NativeSdkException)error).Code : "NativeCallFailed";

        private bool TakeMutex(ref Mutex mutex, string name)
        {
            var candidate = new Mutex(false, name); bool acquired;
            try {
                try { acquired = candidate.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; reservationAbandoned = true; }
            } catch { candidate.Dispose(); throw; }
            if (!acquired) { candidate.Dispose(); return false; }
            lock (sync) mutex = candidate;
            return true;
        }
        private bool Reserve()
        {
            if (!TakeMutex(ref processMutex, @"Local\U2BIO.BT003PROLabelPrinting.Process." + Process.GetCurrentProcess().Id)) return false;
            string identity;
            using (var sha = SHA256.Create()) identity = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(target.Native.TargetKey))).Replace("-", "").ToLowerInvariant();
            return TakeMutex(ref targetMutex, @"Local\U2BIO.BT003PROLabelPrinting.Target." + identity);
        }
        private void ReleaseTarget()
        {
            lock (sync) if (targetMutex != null) { targetMutex.ReleaseMutex(); targetMutex.Dispose(); targetMutex = null; }
        }
        private void ReleaseReservations()
        {
            lock (sync) {
                ReleaseTarget();
                if (processMutex != null) { processMutex.ReleaseMutex(); processMutex.Dispose(); processMutex = null; }
                lock (ProcessSync) if (ReferenceEquals(processOwner, this)) processOwner = null;
                processClaim = false;
            }
        }
        private int Status(Work work, bool cleaning = false, bool receiveResult = false)
        {
            int value;
            try { value = Call(work, "CheckPrtConnection", sdk.Status, cleaning, receiveResult: receiveResult); }
            catch (Exception error) when (Recoverable(error)) {
                lock (sync) if (!work.TimedOut) {
                    state = disposed ? PrinterState.Disposed : PrinterState.Faulted;
                    port = PrinterPortState.Unknown; nativeCode = null;
                    code = ErrorCode(error); cleanup = "NativeCallFailed";
                }
                throw;
            }
            lock (sync) { nativeCode = value; port = value == 0 ? PrinterPortState.Closed : value == 1 ? PrinterPortState.Open : PrinterPortState.Unknown; }
            return value;
        }
        private bool Close(Work work, bool keepFaulted)
        {
            work.CleanupAttempted = true;
            try {
                if (connectionAttempted) Call(work, "DeviceDisconnect", sdk.Disconnect, true);
                int value = loaded ? Status(work, true) : 0;
                lock (sync) {
                    if (value != 0) { code = "DisconnectUnconfirmed"; cleanup = code; state = disposed ? PrinterState.Disposed : PrinterState.Faulted; return false; }
                    connectionAttempted = false; conflict = false; port = PrinterPortState.Closed;
                    ReleaseReservations(); cleanup = "None";
                    state = disposed ? PrinterState.Disposed : keepFaulted || work.TimedOut ? PrinterState.Faulted : PrinterState.Disconnected;
                    code = work.TimedOut ? "NativeCallTimeout" : keepFaulted ? work.Printing?.Code ?? code : "Ok";
                    return true;
                }
            } catch (Exception error) when (Recoverable(error)) {
                lock (sync) {
                    work.CleanupFailureFunction = (error as NativeSdkException)?.Function ?? work.Function;
                    work.CleanupSystemError = (error as NativeSdkException)?.SystemErrorCode;
                    state = disposed ? PrinterState.Disposed : PrinterState.Faulted; port = PrinterPortState.Unknown;
                    nativeCode = null; cleanup = "NativeCallFailed"; code = ErrorCode(error);
                }
                return false;
            }
        }



        private PrinterOperationResult Open(Work work)
        {
            string failure = null, failureFunction = null; int? value = null, systemError = null; bool uncertain = false;
            try {
                reservationAbandoned = false;
                try { if (!Reserve()) { ReleaseReservations(); lock (sync) state = PrinterState.Disconnected; return Result(work, "Busy"); } }
                catch (Exception error) when (error is UnauthorizedAccessException || error is SecurityException || error is System.IO.IOException) {
                    ReleaseReservations(); lock (sync) state = PrinterState.Disconnected; return Result(work, "MutexUnavailable");
                }
                VoidCall(work, "LoadForConnection", () => { sdk.Load(); loaded = true; });
                if (reservationAbandoned && (value = Status(work)) != 0) {
                    lock (sync) { conflict = true; state = PrinterState.Faulted; code = "SdkStateConflict"; }
                    ReleaseTarget(); return Result(work, "SdkStateConflict", "CheckPrtConnection", value);
                }
                value = Call(work, "SetPrinterPort", () => sdk.SetPort(target.Native.IfType, target.Port, target.IpAddress));
                if (value == 1) {
                    lock (sync) { conflict = true; state = PrinterState.Faulted; code = "SdkStateConflict"; port = PrinterPortState.Unknown; }
                    ReleaseTarget(); return Result(work, "SdkStateConflict", "SetPrinterPort", value);
                }
                if (value != 0) { ReleaseReservations(); lock (sync) state = PrinterState.Disconnected; return Result(work, "PortConfigurationFailed", "SetPrinterPort", value); }
                value = Call(work, "DeviceConnect", sdk.Connect, entered: () => connectionAttempted = true);
                if (value != 0) { failure = "ConnectFailed"; failureFunction = "DeviceConnect"; }
                else if ((value = Status(work)) != 1) { failure = "StatusUnavailable"; failureFunction = "CheckPrtConnection"; }
                else {
                    lock (sync) { state = PrinterState.Connected; code = "Ok"; }
                    return Result(work, "Ok");
                }
            } catch (Stopped) { failure = disposed ? "Disposed" : "NativeCallTimeout"; failureFunction = work.Function; }
            catch (Exception error) when (Recoverable(error)) {
                failure = ErrorCode(error); failureFunction = (error as NativeSdkException)?.Function ?? work.Function;
                systemError = (error as NativeSdkException)?.SystemErrorCode; value = null; uncertain = true;
                if (!loaded) {
                    ReleaseReservations(); lock (sync) state = disposed ? PrinterState.Disposed : PrinterState.Disconnected;
                    return Result(work, failure, failureFunction, null, systemError);
                }
            }
            if (connectionAttempted) Close(work, work.TimedOut || uncertain);
            else if (loaded && work.Function != "LoadForConnection") {
                if (!Close(work, work.TimedOut || uncertain)) { lock (sync) conflict = true; ReleaseTarget(); }
            } else { ReleaseReservations(); lock (sync) state = disposed ? PrinterState.Disposed : work.TimedOut ? PrinterState.Faulted : PrinterState.Disconnected; }
            lock (sync) if (uncertain) code = failure;
            return Result(work, failure, failureFunction, value, systemError);
        }
        private PrinterOperationResult Query(Work work)
        {
            try {
                int value = Status(work);
                lock (sync) {
                    if (value != 1) { state = PrinterState.Faulted; code = value == 0 ? "NotConnected" : "StatusUnavailable"; }
                    else if (state != PrinterState.Faulted) code = "Ok";
                }
                return Result(work, code, "CheckPrtConnection", value);
            } catch (Stopped) { Close(work, true); return Result(work, disposed ? "Disposed" : "NativeCallTimeout", work.Function); }
            catch (Exception error) when (Recoverable(error)) {
                lock (sync) { state = PrinterState.Faulted; port = PrinterPortState.Unknown; code = "StatusUnavailable"; }
                return Result(work, code, "CheckPrtConnection");
            }
        }
        private PrinterOperationResult DisconnectCore(Work work)
        {
            bool closed = Close(work, false);
            return Result(work, closed ? "Ok" : conflict ? "SdkStateConflict" : cleanup,
                work.CleanupFailureFunction ?? "CheckPrtConnection", work.CleanupFailureFunction == null ? nativeCode : null, work.CleanupSystemError);
        }

    }
}

namespace U2BIO.BT003PROLabelPrinting
{
    public sealed partial class Bt003Printer
    {
        private static readonly HashSet<int> KnownPrintFailures = new HashSet<int>(Enumerable.Range(1, 11).Concat(new[] { 20, 21, 22, 50, 60, 102, 103, 104, 105, 205 }));
        private sealed class PrintingData
        {
            internal readonly BtplPrintRequest Request;
            internal readonly PrintInput Input;
            internal readonly bool PreflightOnly;
            internal readonly RowPrintResult[] Rows;
            internal readonly RowValidationResult[] PartialManaged;
            internal readonly CancellationToken Cancellation;
            internal BatchValidationResult Managed;
            internal DeviceValidationResult Device;
            internal BtplRenderResult[] Rendered;
            internal int CurrentRow = -1;
            internal PrintExecutionStage Stage;
            internal bool Invoked, FormTouched;
            internal MediaMovementStatus PositionReturnStatus, PresentationStatus;
            internal PrintStopReason StopReason;
            internal string Code = "Ok", Function;
            internal int? NativeCode;
            internal PrintingData(BtplPrintRequest request, bool preflightOnly, CancellationToken cancellation = default(CancellationToken), PrintInput input = null)
            {
                Request = request; Input = input; PreflightOnly = preflightOnly; Cancellation = cancellation;
                Rows = new RowPrintResult[request.RowCount]; PartialManaged = new RowValidationResult[request.RowCount];
                Rendered = new BtplRenderResult[request.RowCount];
            }
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
                var managedCommon = data.Input == null ? BtplProcessor.Common(data.Request) : data.Input.Validate(printingSpeed.HasValue);
                for (int i = 0; i < data.Rows.Length; i++) {
                    CheckStop(work);
                    var renderedRow = data.Input == null ? BtplProcessor.RenderCore(data.Request, i, managedCommon) : data.Input.ValidationRow(managedCommon);
                    lock (sync) { data.Rendered[i] = renderedRow; data.PartialManaged[i] = renderedRow.Validation; }
                    CheckStop(work);
                }
                CheckStop(work);
                var managed = new BatchValidationResult(managedCommon, data.PartialManaged);
                lock (sync) data.Managed = managed;
                if (!managed.IsValid) return PreflightResult(work, data.Request.RowCount == 0 ? "NoData" : "ValidationFailed");
                CheckStop(work); data.Stage = PrintExecutionStage.DeviceValidation;
                var common = new List<ValidationIssue>();
                if (target.Transport != "usb") common.Add(new ValidationIssue("DeviceSupportUnverified", null));
                if (!PendingReturnDistance.HasValue) common.Add(new ValidationIssue("MediaPositionUnknown", null));
                if (Encoding.Default.CodePage != 949) common.Add(new ValidationIssue("EncodingNotSupported", null));
                var encoding = Encoding.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                var rows = new List<RowValidationResult>();
                var rendered = data.Rendered;
                for (int i = 0; i < rendered.Length; i++) {
                    CheckStop(work);
                    var issues = new List<ValidationIssue>();
                    foreach (var text in rendered[i].TextElements) {
                        if (text.Command != "AW" || !text.Arguments.Select(x => int.Parse(x, CultureInfo.InvariantCulture)).SequenceEqual(new[] { 10, 1, 0, 0 }))
                            issues.Add(new ValidationIssue("DeviceSupportUnverified", text.Line, i));
                    }
                    try {
                        encoding.GetByteCount(rendered[i].BtplText);
                        if (data.Input?.VariableData != null) encoding.GetByteCount(data.Input.VariableData);
                        if (data.Input?.FilePath != null) encoding.GetByteCount(data.Input.FilePath);
                    }
                    catch (EncoderFallbackException) { issues.Add(new ValidationIssue("EncodingNotSupported", null, i)); }
                    rows.Add(new RowValidationResult(i, issues));
                    CheckStop(work);
                }
                int status = Status(work, receiveResult: true);
                if (status != 1) {
                    common.Add(new ValidationIssue(status == 0 ? "NotConnected" : "StatusUnavailable", null));
                    lock (sync) state = PrinterState.Faulted;
                }
                lock (sync) data.Device = new DeviceValidationResult(common, rows, port);
                return PreflightResult(work, status == 0 ? "NotConnected" : status != 1 ? "StatusUnavailable" : !PendingReturnDistance.HasValue ? "MediaPositionUnknown" : data.Device.IsValid ? "Ok" : "ValidationFailed",
                    "CheckPrtConnection", status);
            } catch (Stopped) { return PreflightResult(work, StopCode(work), work.Function); }
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
        private Task<BatchPrintResult> PrintInputAsync(PrintInput input, CancellationToken cancellation)
        {
            bool handedToWorker = false;
            try {
                var request = BtplPrintRequest.Fixed(input.Template);
                lock (sync) {
                    RequireAlive();
                    if (working || OtherOwner) return Task.FromResult(Immediate(request, PrintStopReason.Busy, "Busy"));
                    if (cancellation.IsCancellationRequested) return Task.FromResult(Immediate(request, PrintStopReason.Cancelled, "Cancelled"));
                    if (state != PrinterState.Connected) return Task.FromResult(Immediate(request, PrintStopReason.NotConnected, state == PrinterState.Faulted ? "PrinterFaulted" : "NotConnected"));
                    var work = Start(PrintWork, PrinterState.Validating, new PrintingData(request, false, cancellation, input));
                    handedToWorker = true;
                    return work.PrintCompletion.Task;
                }
            } finally { if (!handedToWorker) input.Dispose(); }
        }
        private string StopCode(Work work) => work.TimedOut ? "NativeCallTimeout" : disposed ? "Disposed" : "Cancelled";
        private BatchPrintResult Immediate(BtplPrintRequest request, PrintStopReason reason, string resultCode)
            => new BatchPrintResult(reason, resultCode, Enumerable.Range(0, request.RowCount).Select(i => new RowPrintResult(i, PrintRowStatus.NotAttempted, reason, resultCode)),
                presentationDistance: target?.PresentationDistanceMm ?? 0, returnDistance: PendingReturnDistance);
        private static PrintStopReason Reason(string resultCode)
        {
            switch (resultCode) {
                case "Ok": return PrintStopReason.None;
                case "NotConnected": return PrintStopReason.NotConnected;
                case "NoData": return PrintStopReason.NoData;
                case "Disposed": return PrintStopReason.Disposed;
                case "Cancelled": return PrintStopReason.Cancelled;
                case "NativeCallTimeout": case "SdkTimeout": return PrintStopReason.Timeout;
                case "StatusUnavailable": case "FormRegistrationFailed": case "VariableRegistrationFailed": case "PrintingOptionsFailed": case "PrintFailed": case "NativeCallFailed": case "MediaMovementFailed": return PrintStopReason.SdkFailure;
                case "MediaMovementUnknown": case "MediaPositionUnknown": return PrintStopReason.ResponseUnknown;
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
                    data.PartialManaged[data.CurrentRow], data.Device?.Rows[data.CurrentRow], data.Stage, data.Invoked, function, value, evidence);
                if (status != PrintRowStatus.Succeeded) { StopPrinting(work, resultCode, function, value); data.StopReason = reason; }
                if (status != PrintRowStatus.Succeeded && data.Invoked && (target.PresentationDistanceMm > 0 || data.PositionReturnStatus != MediaMovementStatus.NotAttempted)) SetReturnDistance(null);
            }
        }
        private BatchPrintResult Batch(Work work)
        {
            lock (sync) {
                var data = work.Printing;
                return new BatchPrintResult(data.StopReason, data.Code, Enumerable.Range(0, data.Rows.Length).Select(i => data.Rows[i] ??
                    new RowPrintResult(i, PrintRowStatus.NotAttempted, data.StopReason, data.Code, data.PartialManaged[i], data.Device?.Rows[i])),
                    data.Managed, data.Device, data.Function, data.NativeCode, pending, cleanup, work.Calls,
                    data.PositionReturnStatus, data.PresentationStatus, target.PresentationDistanceMm, PendingReturnDistance);
            }
        }
        private object TimeoutPrinting(Work work)
        {
            var data = work.Printing;
            if (data.PreflightOnly) return PreflightResult(work, "NativeCallTimeout", work.Function);
            if (data.CurrentRow < 0 || data.Stage == PrintExecutionStage.PositionReturn || data.Stage == PrintExecutionStage.Presentation) StopPrinting(work, "NativeCallTimeout", work.Function);
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
                    int previous = PendingReturnDistance.Value;
                    if (previous == 0 || MovePaper(work, previous, false)) {
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
                            if (printingSpeed.HasValue) {
                                int option = Call(work, "SetPrintingOption", () => sdk.Options(printingSpeed.Value, printingDarkness.Value), receiveResult: true);
                                if (option != 0) { Row(work, PrintRowStatus.Failed, "PrintingOptionsFailed", "SetPrintingOption", option); break; }
                            }
                            data.Stage = PrintExecutionStage.FormRegistration;
                            string formFunction = data.Input?.FilePath == null ? "SetPrintFormString" : "SetPrintFormFile";
                            int form = Call(work, formFunction, () => data.Input?.FilePath == null ? sdk.Form(data.Rendered[i].BtplText) : sdk.FileForm(data.Input.FilePath), receiveResult: true);
                            if (form != 0) { Row(work, PrintRowStatus.Failed, "FormRegistrationFailed", formFunction, form); break; }
                            if (data.Input?.VariableData != null) {
                                int variables = Call(work, "SetPrintFormVariableData", () => sdk.Variables(data.Input.VariableData), receiveResult: true);
                                if (variables != 0) { Row(work, PrintRowStatus.Failed, "VariableRegistrationFailed", "SetPrintFormVariableData", variables); break; }
                            }
                            data.Stage = PrintExecutionStage.PrintInvocation;
                            var response = Call(work, "StartLabelPrint", () => NativeLabelResponse.Capture(pointer => {
                                lock (sync) { CheckStop(work); data.Invoked = true; }
                                try { sdk.Print(1, pointer); }
                                finally { NativeReturned(work); }
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
                        if (target.PresentationDistanceMm > 0 && data.Rows.All(x => x != null && x.Status == PrintRowStatus.Succeeded)) {
                            CheckStop(work);
                            MovePaper(work, target.PresentationDistanceMm, true);
                        }
                    }
                }
            } catch (Stopped) {
                if (!work.TimedOut) StopPrinting(work, StopCode(work));
            } catch (Exception error) when (Recoverable(error)) {
                if (data.CurrentRow >= 0) Row(work, data.Invoked ? PrintRowStatus.Unknown : PrintRowStatus.Failed,
                    data.Invoked ? error is NativeSdkException && ((NativeSdkException)error).Code == "PrintResponseInvalid" ? "InvalidPrintResponse" : "NativeCallFailed" : "NativeCallFailed", work.Function);
                else StopPrinting(work, "StatusUnavailable", work.Function);
            } finally {
                try { CleanupPrinting(work); }
                finally { data.Input?.Dispose(); }
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
            bool positionLost = !PendingReturnDistance.HasValue && (data.Invoked || data.PositionReturnStatus != MediaMovementStatus.NotAttempted || data.PresentationStatus != MediaMovementStatus.NotAttempted);
            if (work.TimedOut || disposed || positionLost || data.Rows.Any(x => x != null && x.Status == PrintRowStatus.Unknown)) Close(work, work.TimedOut || !disposed);
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

namespace U2BIO.BT003PROLabelPrinting
{
    public sealed partial class Bt003Printer
    {
        // Process memory only: callers must establish the physical baseline after restart/manual motion.
        private static readonly Dictionary<string, int?> PresentedOffsets = new Dictionary<string, int?>();
        private int? PendingReturnDistance
        {
            get {
                lock (ProcessSync) {
                    int? value;
                    return target != null && PresentedOffsets.TryGetValue(target.Native.TargetKey, out value) ? value : 0;
                }
            }
        }
        private void SetReturnDistance(int? value)
        {
            lock (ProcessSync) {
                if (value == 0) PresentedOffsets.Remove(target.Native.TargetKey);
                else PresentedOffsets[target.Native.TargetKey] = value;
            }
        }
        private bool MovePaper(Work work, int distance, bool forward)
        {
            var data = work.Printing;
            lock (sync) data.Stage = forward ? PrintExecutionStage.Presentation : PrintExecutionStage.PositionReturn;
            int result;
            try {
                result = Call(work, "SendData", () => sdk.Move(distance, forward), entered: () => {
                    SetReturnDistance(null);
                    if (forward) data.PresentationStatus = MediaMovementStatus.Unknown;
                    else data.PositionReturnStatus = MediaMovementStatus.Unknown;
                }, receiveResult: true);
            } catch (Stopped) { throw; }
            catch (System.Exception error) when (Recoverable(error)) {
                lock (sync) {
                    state = disposed ? PrinterState.Disposed : PrinterState.Faulted;
                    StopPrinting(work, "MediaMovementUnknown", "SendData");
                }
                return false;
            }
            lock (sync) {
                var status = result == 0 ? MediaMovementStatus.CommandAccepted : MediaMovementStatus.Failed;
                if (forward) data.PresentationStatus = status; else data.PositionReturnStatus = status;
                if (result == 0) { SetReturnDistance(forward ? distance : 0); return true; }
                state = disposed ? PrinterState.Disposed : PrinterState.Faulted;
                StopPrinting(work, "MediaMovementFailed", "SendData", result);
                return false;
            }
        }
    }
}
