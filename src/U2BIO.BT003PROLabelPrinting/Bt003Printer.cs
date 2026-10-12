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
        public Bt003Printer() : this(TimeSpan.FromSeconds(30)) { }

        public Bt003Printer(TimeSpan nativeWaitTimeout) : this(nativeWaitTimeout, PrinterSdkCalls.Create()) { }

        public PrinterOperationResult ConnectUsb(string usbPort, int presentationDistanceMm = 0)
            => Connect(Bt003ConnectionOptions.Usb(usbPort).WithPresentationDistance(presentationDistanceMm));

        public PrinterOperationResult SetPrintingOptions(ushort speed, ushort darkness)
        {
            Work work;
            lock (sync) {
                RequireAlive();
                if (speed == 0) throw new ArgumentOutOfRangeException(nameof(speed));
                if (darkness < 1 || darkness > 19) throw new ArgumentOutOfRangeException(nameof(darkness));
                if (working || OtherOwner) return Busy();
                if (state != PrinterState.Connected) return Result(null, "NotConnected");
                work = Start(current => {
                    try {
                        int value = Call(current, "SetPrintingOption", () => sdk.Options(speed, darkness), receiveResult: true);
                        CheckStop(current);
                        if (value == 0) lock (sync) { printingSpeed = speed; printingDarkness = darkness; }
                        return Result(current, value == 0 ? "Ok" : "PrintingOptionsFailed", "SetPrintingOption", value);
                    } catch (Stopped) {
                        Close(current, current.TimedOut);
                        return Result(current, disposed ? "Disposed" : "NativeCallTimeout", "SetPrintingOption");
                    } catch (Exception error) when (Recoverable(error)) {
                        lock (sync) { state = disposed ? PrinterState.Disposed : PrinterState.Faulted; code = ErrorCode(error); }
                        Close(current, true);
                        return Result(current, ErrorCode(error), "SetPrintingOption");
                    }
                }, PrinterState.Connected);
            }
            return (PrinterOperationResult)work.Completion.Task.GetAwaiter().GetResult();
        }

        public PrinterOperationResult Connect(Bt003ConnectionOptions options)
        {
            Work work;
            lock (sync) {
                RequireAlive(); if (options == null) throw new ArgumentNullException(nameof(options));
                if (working) return Busy();
                if (state == PrinterState.Connected) {
                    if (target.Native.TargetKey != options.Native.TargetKey || target.PresentationDistanceMm != options.PresentationDistanceMm) return Result(null, "AlreadyConnected");
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

        public PrintPreflightResult ValidateForPrint(string btpl) => ValidateForPrint(Prepare(btpl));

        public PrintPreflightResult ValidateForPrint(BtplTemplate template) => ValidateForPrint(Prepare(template));

        public PrintPreflightResult ValidateForPrint(BtplPrintRequest request)
        {
            Work work;
            lock (sync) {
                RequireAlive(); if (request == null) throw new ArgumentNullException(nameof(request));
                if (working || OtherOwner) return new PrintPreflightResult("Busy", null, null, state);
                if (state != PrinterState.Connected) return new PrintPreflightResult("NotConnected", null, null, state);
                work = Start(PreflightWork, PrinterState.Validating, new PrintingData(request, true));
            }
            return (PrintPreflightResult)work.Completion.Task.GetAwaiter().GetResult();
        }

        public BatchPrintResult Print(string btpl) => PrintAsync(btpl, new string[0], CancellationToken.None).GetAwaiter().GetResult();

        public BatchPrintResult Print(string btpl, params string[] values)
            => PrintAsync(btpl, values, CancellationToken.None).GetAwaiter().GetResult();

        public BatchPrintResult PrintFile(string filePath, params string[] values)
            => PrintFileAsync(filePath, values, CancellationToken.None).GetAwaiter().GetResult();

        public Task<BatchPrintResult> PrintAsync(string btpl, string[] values, CancellationToken cancellationToken)
        {
            lock (sync) RequireAlive();
            return PrintInputAsync(new PrintInput(btpl, values), cancellationToken);
        }

        public Task<BatchPrintResult> PrintFileAsync(string filePath, string[] values = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            lock (sync) RequireAlive();
            return PrintInputAsync(PrintInput.FromFile(filePath, values), cancellationToken);
        }

        public BatchPrintResult Print(BtplTemplate template) => Print(Prepare(template));

        public BatchPrintResult Print(BtplPrintRequest request) => PrintAsync(request, CancellationToken.None).GetAwaiter().GetResult();

        public Task<BatchPrintResult> PrintAsync(string btpl, CancellationToken cancellationToken = default(CancellationToken))
            => PrintAsync(btpl, new string[0], cancellationToken);

        public Task<BatchPrintResult> PrintAsync(BtplTemplate template, CancellationToken cancellationToken = default(CancellationToken))
            => PrintAsync(Prepare(template), cancellationToken);

        public Task<BatchPrintResult> PrintAsync(BtplPrintRequest request, CancellationToken cancellationToken = default(CancellationToken))
        {
            Work work;
            lock (sync) {
                RequireAlive(); if (request == null) throw new ArgumentNullException(nameof(request));
                if (working || OtherOwner) return Task.FromResult(Immediate(request, PrintStopReason.Busy, "Busy"));
                if (cancellationToken.IsCancellationRequested) return Task.FromResult(Immediate(request, PrintStopReason.Cancelled, "Cancelled"));
                if (state != PrinterState.Connected) return Task.FromResult(Immediate(request, PrintStopReason.NotConnected, state == PrinterState.Faulted ? "PrinterFaulted" : "NotConnected"));
                work = Start(PrintWork, PrinterState.Validating, new PrintingData(request, false, cancellationToken));
            }
            return work.PrintCompletion.Task;
        }

        public PrinterOperationResult ConfirmPrintPosition()
        {
            lock (sync) {
                RequireAlive();
                if (working || OtherOwner) return Busy();
                if (state != PrinterState.Connected) return Result(null, "NotConnected");
                // Explicit caller acknowledgement after physically positioning the stock; no SDK query/move.
                SetReturnDistance(0); code = "Ok";
                return Result(null, "Ok");
            }
        }
    }
}
