using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using U2BIO.BT003PROLabelPrinting.Native;

namespace U2BIO.BT003PROLabelPrinting
{
    public enum PrinterState { Disconnected, Connecting, Connected, Validating, Printing, Disconnecting, Faulted, Disposed }
    public enum PrinterPortState { Unknown, Closed, Open }
    public enum PrintRowStatus { Succeeded, Failed, Unknown, NotAttempted }
    public enum PrintStopReason { None, ValidationFailed, Busy, NotConnected, ConnectionFailed, SdkFailure, ResponseUnknown, Timeout, Cancelled, NoData, Disposed }
    public enum PrintExecutionStage { None, ManagedValidation, DeviceValidation, ConnectionCheck, FormReset, FormRegistration, PrintInvocation, ResponseValidation, Cleanup }
    public enum PrintCompletionEvidence { None, SdkResponse }

    public sealed class Bt003ConnectionOptions
    {
        public string Transport { get; }
        public string Port => Native.Port;
        public string IpAddress => Native.Ip;
        internal NativeConnectionOptions Native { get; }
        private Bt003ConnectionOptions(string transport, string port, string ip)
        {
            Transport = transport;
            try { Native = NativeConnectionOptions.Create(transport, port, ip); }
            catch (NativeSdkException) { throw new ArgumentException("Invalid connection options; supply an explicit printer target."); }
        }
        public static Bt003ConnectionOptions Usb(string port) => new Bt003ConnectionOptions("usb", port, "");
        public static Bt003ConnectionOptions Serial(string comPort) => new Bt003ConnectionOptions("serial", comPort, "");
        public static Bt003ConnectionOptions Tcp(string ipAddress, int port)
            => new Bt003ConnectionOptions("tcp", port.ToString(CultureInfo.InvariantCulture), ipAddress);
    }

    public sealed class NativeCallResult
    {
        public string Function { get; }
        public int? NativeCode { get; }
        internal NativeCallResult(string function, int? code) { Function = function; NativeCode = code; }
    }

    public sealed class PrinterConnectionStatus
    {
        public PrinterState State { get; }
        public PrinterPortState PortState { get; }
        public string Code { get; }
        public int? NativeCode { get; }
        public bool NativeCallPending { get; }
        public bool ResourcesReleased { get; }
        public string CleanupCode { get; }
        internal PrinterConnectionStatus(PrinterState state, PrinterPortState port, string code, int? native, bool pending, bool released, string cleanup)
        { State = state; PortState = port; Code = code; NativeCode = native; NativeCallPending = pending; ResourcesReleased = released; CleanupCode = cleanup; }
    }

    public sealed class PrinterOperationResult
    {
        public bool IsSuccess => Code == "Ok";
        public string Code { get; }
        public string NativeFunction { get; }
        public int? NativeCode { get; }
        public int? SystemErrorCode { get; }
        public PrinterState State { get; }
        public bool NativeCallPending { get; }
        public bool ResourcesReleased { get; }
        public string CleanupCode { get; }
        public IReadOnlyList<NativeCallResult> Calls { get; }
        public string Message => "Printer operation: " + Code + ".";
        internal PrinterOperationResult(string code, PrinterConnectionStatus status, string function = null, int? native = null, int? system = null, IEnumerable<NativeCallResult> calls = null)
        {
            Code = code; State = status.State; NativeCallPending = status.NativeCallPending; ResourcesReleased = status.ResourcesReleased;
            CleanupCode = status.CleanupCode; NativeFunction = function; NativeCode = native; SystemErrorCode = system;
            Calls = Array.AsReadOnly((calls ?? Enumerable.Empty<NativeCallResult>()).ToArray());
        }
    }

    public sealed class DeviceValidationResult
    {
        public bool IsValid => !CommonIssues.Any(x => x.Severity == ValidationSeverity.Error) && Rows.All(x => x.IsValid);
        public IReadOnlyList<ValidationIssue> CommonIssues { get; }
        public IReadOnlyList<RowValidationResult> Rows { get; }
        public PrinterPortState PortState { get; }
        internal DeviceValidationResult(IEnumerable<ValidationIssue> common, IEnumerable<RowValidationResult> rows, PrinterPortState port)
        { CommonIssues = Array.AsReadOnly(common.ToArray()); Rows = Array.AsReadOnly(rows.ToArray()); PortState = port; }
    }

    public sealed class PrintPreflightResult
    {
        public bool IsReady => Code == "Ok";
        public string Code { get; }
        public BatchValidationResult ManagedValidation { get; }
        public DeviceValidationResult DeviceValidation { get; }
        public PrinterState State { get; }
        public string NativeFunction { get; }
        public int? NativeCode { get; }
        public bool NativeCallPending { get; }
        internal PrintPreflightResult(string code, BatchValidationResult managed, DeviceValidationResult device, PrinterState state,
            string function = null, int? native = null, bool pending = false)
        { Code = code; ManagedValidation = managed; DeviceValidation = device; State = state; NativeFunction = function; NativeCode = native; NativeCallPending = pending; }
    }

    public sealed class RowPrintResult
    {
        public int RowIndex { get; }
        public PrintRowStatus Status { get; }
        public RowValidationResult Validation { get; }
        public RowValidationResult DeviceValidation { get; }
        public PrintStopReason StopReason { get; }
        public string Code { get; }
        public PrintExecutionStage ExecutionStage { get; }
        public bool PrintInvoked { get; }
        public string NativeFunction { get; }
        public int? NativeCode { get; }
        public PrintCompletionEvidence CompletionEvidence { get; }
        public string Message => "Label operation: " + Code + ".";
        internal RowPrintResult(int index, PrintRowStatus status, PrintStopReason reason, string code, RowValidationResult managed = null,
            RowValidationResult device = null, PrintExecutionStage stage = PrintExecutionStage.None, bool invoked = false,
            string function = null, int? native = null, PrintCompletionEvidence evidence = PrintCompletionEvidence.None)
        {
            RowIndex = index; Status = status; StopReason = reason; Code = code; Validation = managed; DeviceValidation = device;
            ExecutionStage = stage; PrintInvoked = invoked; NativeFunction = function; NativeCode = native; CompletionEvidence = evidence;
        }
    }

    public sealed class BatchPrintResult
    {
        public PrintStopReason StopReason { get; }
        public string Code { get; }
        public BatchValidationResult Validation { get; }
        public DeviceValidationResult DeviceValidation { get; }
        public IReadOnlyList<RowPrintResult> Rows { get; }
        public int Succeeded => Rows.Count(x => x.Status == PrintRowStatus.Succeeded);
        public int Failed => Rows.Count(x => x.Status == PrintRowStatus.Failed);
        public int Unknown => Rows.Count(x => x.Status == PrintRowStatus.Unknown);
        public int NotAttemptedCount => Rows.Count(x => x.Status == PrintRowStatus.NotAttempted);
        public string NativeFunction { get; }
        public int? NativeCode { get; }
        public bool NativeCallPending { get; }
        public string CleanupCode { get; }
        public IReadOnlyList<NativeCallResult> Calls { get; }
        internal BatchPrintResult(PrintStopReason reason, string code, IEnumerable<RowPrintResult> rows, BatchValidationResult managed = null,
            DeviceValidationResult device = null, string function = null, int? native = null, bool pending = false,
            string cleanup = "None", IEnumerable<NativeCallResult> calls = null)
        {
            StopReason = reason; Code = code; Rows = Array.AsReadOnly(rows.ToArray()); Validation = managed; DeviceValidation = device;
            NativeFunction = function; NativeCode = native; NativeCallPending = pending; CleanupCode = cleanup;
            Calls = Array.AsReadOnly((calls ?? Enumerable.Empty<NativeCallResult>()).ToArray());
        }
    }
}
