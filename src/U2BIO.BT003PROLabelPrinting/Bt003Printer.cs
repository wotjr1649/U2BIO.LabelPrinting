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

        public Bt003Printer() : this(TimeSpan.FromSeconds(30)) { }
        public Bt003Printer(TimeSpan nativeWaitTimeout) : this(nativeWaitTimeout, PrinterSdkCalls.Create()) { }
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
                disposed ? "Disposed" : busy && state != PrinterState.Faulted ? "Busy" : code, nativeCode, ownPending && pending, Released, cleanup);
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
            int value = Call(work, "CheckPrtConnection", sdk.Status, cleaning, receiveResult: receiveResult);
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

        public PrinterOperationResult Connect(Bt003ConnectionOptions options)
        {
            Work work;
            lock (sync) {
                RequireAlive(); if (options == null) throw new ArgumentNullException(nameof(options));
                if (working) return Busy();
                if (state == PrinterState.Connected) {
                    if (target.Native.TargetKey != options.Native.TargetKey) return Result(null, "AlreadyConnected");
                    work = Start(Query, PrinterState.Connected);
                } else {
                    if (state == PrinterState.Faulted) return Result(null, "PrinterFaulted");
                    lock (ProcessSync) {
                        if (processOwner != null) return Busy();
                        processOwner = this; processClaim = true;
                    }
                    target = options; cleanup = "None"; work = Start(Open, PrinterState.Connecting);
                }
            }
            return (PrinterOperationResult)work.Completion.Task.GetAwaiter().GetResult();
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
        public PrinterConnectionStatus GetConnectionStatus()
        {
            Work work;
            lock (sync) {
                if (disposed || working || !loaded || Released || state == PrinterState.Disconnected) return Snapshot(working);
                work = Start(Query, state);
            }
            work.Completion.Task.GetAwaiter().GetResult();
            lock (sync) return Snapshot(working);
        }
        private PrinterOperationResult DisconnectCore(Work work)
        {
            bool closed = Close(work, false);
            return Result(work, closed ? "Ok" : conflict ? "SdkStateConflict" : cleanup,
                work.CleanupFailureFunction ?? "CheckPrtConnection", work.CleanupFailureFunction == null ? nativeCode : null, work.CleanupSystemError);
        }
        public PrinterOperationResult Disconnect()
        {
            Work work;
            lock (sync) {
                RequireAlive(); if (working || OtherOwner) return Busy();
                if (Released) { state = PrinterState.Disconnected; port = PrinterPortState.Closed; code = "Ok"; return Result(null, "Ok"); }
                work = Start(DisconnectCore, PrinterState.Disconnecting);
            }
            return (PrinterOperationResult)work.Completion.Task.GetAwaiter().GetResult();
        }
        public void Dispose()
        {
            Work work = null;
            lock (sync) {
                if (disposed) return;
                disposed = true; state = PrinterState.Disposed;
                if (!working && !Released) work = Start(DisconnectCore, PrinterState.Disposed);
                else if (!working && worker == null) ready.Dispose();
            }
            if (work != null) work.Completion.Task.GetAwaiter().GetResult();
        }

    }
}
