using System.Collections.Generic;

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
