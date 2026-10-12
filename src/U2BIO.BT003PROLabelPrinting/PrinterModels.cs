using System.Net;
using System.Text;
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
    public enum PrintExecutionStage { None, ManagedValidation, DeviceValidation, ConnectionCheck, FormReset, FormRegistration, PrintInvocation, ResponseValidation, Cleanup, PositionReturn, Presentation }
    public enum PrintCompletionEvidence { None, SdkResponse }
    public enum MediaMovementStatus { NotAttempted, CommandAccepted, Failed, Unknown }

    public sealed class Bt003ConnectionOptions
    {
        public string Transport { get; }
        public string Port => Native.Port;
        public string IpAddress => Native.Ip;
        public int PresentationDistanceMm { get; }
        internal NativeConnectionOptions Native { get; }
        private Bt003ConnectionOptions(string transport, string port, string ip)
        {
            Transport = transport;
            try { Native = NativeConnectionOptions.Create(transport, port, ip); }
            catch (NativeSdkException) { throw new ArgumentException("Invalid connection options; supply an explicit printer target."); }
        }
        private Bt003ConnectionOptions(Bt003ConnectionOptions source, int presentationDistance)
        { Transport = source.Transport; Native = source.Native; PresentationDistanceMm = presentationDistance; }
        public static Bt003ConnectionOptions Usb(string port) => new Bt003ConnectionOptions("usb", port, "");
        public static Bt003ConnectionOptions Serial(string comPort) => new Bt003ConnectionOptions("serial", comPort, "");
        public static Bt003ConnectionOptions Tcp(string ipAddress, int port)
            => new Bt003ConnectionOptions("tcp", port.ToString(CultureInfo.InvariantCulture), ipAddress);
        public Bt003ConnectionOptions WithPresentationDistance(int millimeters)
        {
            if (millimeters < 0 || millimeters > 1000) throw new ArgumentOutOfRangeException(nameof(millimeters));
            return new Bt003ConnectionOptions(this, millimeters);
        }
    }

    public sealed class NativeCallResult
    {
        public string Function { get; }
        public int? NativeCode { get; }
        public int? SystemErrorCode { get; }
        internal NativeCallResult(string function, int? code, int? systemError = null) { Function = function; NativeCode = code; SystemErrorCode = systemError; }
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
        public int? PendingReturnDistanceMm { get; }
        internal PrinterConnectionStatus(PrinterState state, PrinterPortState port, string code, int? native, bool pending, bool released, string cleanup, int? returnDistance = 0)
        { State = state; PortState = port; Code = code; NativeCode = native; NativeCallPending = pending; ResourcesReleased = released; CleanupCode = cleanup; PendingReturnDistanceMm = returnDistance; }
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
        public MediaMovementStatus PositionReturnStatus { get; }
        public MediaMovementStatus PresentationStatus { get; }
        public int PresentationDistanceMm { get; }
        public int? PendingReturnDistanceMm { get; }
        internal BatchPrintResult(PrintStopReason reason, string code, IEnumerable<RowPrintResult> rows, BatchValidationResult managed = null,
            DeviceValidationResult device = null, string function = null, int? native = null, bool pending = false,
            string cleanup = "None", IEnumerable<NativeCallResult> calls = null, MediaMovementStatus returnStatus = MediaMovementStatus.NotAttempted,
            MediaMovementStatus presentationStatus = MediaMovementStatus.NotAttempted, int presentationDistance = 0, int? returnDistance = 0)
        {
            StopReason = reason; Code = code; Rows = Array.AsReadOnly(rows.ToArray()); Validation = managed; DeviceValidation = device;
            NativeFunction = function; NativeCode = native; NativeCallPending = pending; CleanupCode = cleanup;
            Calls = Array.AsReadOnly((calls ?? Enumerable.Empty<NativeCallResult>()).ToArray());
            PositionReturnStatus = returnStatus; PresentationStatus = presentationStatus; PresentationDistanceMm = presentationDistance; PendingReturnDistanceMm = returnDistance;
        }
    }
}


namespace U2BIO.BT003PROLabelPrinting.Native
{
    // Diagnostic settings; TASK-05 reuses these normalized SDK arguments and target identity.
    internal sealed class NativeConnectionOptions
    {
        internal readonly ushort IfType;
        internal readonly string Port, Ip, TargetKey;
        private NativeConnectionOptions(ushort type, string port, string ip, string key)
        { IfType = type; Port = port; Ip = ip; TargetKey = key; }
        private static bool Clean(string value)
        { return value != null && value == value.Trim() && Array.TrueForAll(value.ToCharArray(), c => !char.IsControl(c)); }
        internal static NativeConnectionOptions Create(string transport, string port, string ip)
        {
            if (!Clean(port) || port.Length == 0 || !Clean(ip)) throw new NativeSdkException("InvalidConnectionOptions");
            if (transport == "usb" && ip.Length == 0) {
                try { Encoding.GetEncoding(0, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetBytes(port); }
                catch (EncoderFallbackException) { throw new NativeSdkException("InvalidConnectionOptions"); }
                return new NativeConnectionOptions(1, port, "", "USB:" + port);
            }
            int number;
            string digits = transport == "serial" && port.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ? port.Substring(3) : port;
            if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out number) || number < 1)
                throw new NativeSdkException("InvalidConnectionOptions");
            string normalized = number.ToString(CultureInfo.InvariantCulture);
            if (transport == "serial" && ip.Length == 0) return new NativeConnectionOptions(0, normalized, "", "COM:" + normalized);
            IPAddress address;
            if (transport == "tcp" && number <= 65535 && IPAddress.TryParse(ip, out address)) {
                string canonicalIp = address.ToString();
                return new NativeConnectionOptions(2, normalized, canonicalIp, "TCP:" + canonicalIp + ":" + normalized);
            }
            throw new NativeSdkException("InvalidConnectionOptions");
        }
    }
}
