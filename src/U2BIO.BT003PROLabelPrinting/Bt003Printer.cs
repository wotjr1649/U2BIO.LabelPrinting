using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Vendor = HFSample.BT003CtrlWrapper;

namespace U2BIO.BT003PROLabelPrinting
{
    /// <summary>한 프로그램의 USB 연결과 한 장 인쇄를 동기적으로 처리합니다.</summary>
    public sealed class Bt003Printer : IDisposable
    {
        private const int MaximumBytes = 4 * 1024 * 1024;
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, int?> Positions = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        private static readonly Encoding Cp949 = Encoding.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        private static Bt003Printer owner;
        private string port;
        private int presentationDistance;
        private ushort speed, darkness;
        private bool hasOptions, faulted, disposed;

        public PrinterResult ConnectUsb(string port, int presentationDistanceMm = 0)
        {
            CheckText(port, "InvalidPort");
            if (port.Any(char.IsControl)) throw InputError("InvalidPort");
            if (presentationDistanceMm < 0 || presentationDistanceMm > 1000) throw InputError("InvalidPresentationDistance");
            lock (Gate)
            {
                CheckDisposed();
                if (owner != null) return Failure(owner == this ? "AlreadyConnected" : "ConnectionBusy");
                this.port = port;
                presentationDistance = presentationDistanceMm;
                if (!Positions.ContainsKey(port)) Positions.Add(port, 0);
                owner = this;
                // 연결 확인 전의 부분 연결과 해제 실패 상태에서는 인쇄를 허용하지 않는다.
                faulted = true;
                var result = Success();
                bool connectionAttempted = false;
                try
                {
                    result.Function = "SetPrinterPort";
                    result.NativeCode = Vendor.SetPrinterPort(1, port, "");
                    if (result.NativeCode != 0) { owner = null; return Fail(result, "PortSelectionFailed"); }
                    result.Function = "DeviceConnect";
                    result.NativeCode = null;
                    connectionAttempted = true;
                    result.NativeCode = Vendor.DeviceConnect();
                    if (result.NativeCode != 0) Fail(result, "ConnectionFailed");
                    else
                    {
                        result.Function = "CheckPrtConnection";
                        result.NativeCode = null;
                        result.NativeCode = Vendor.CheckPrtConnection();
                        if (result.NativeCode == 1) { faulted = false; return result; }
                        Fail(result, "ConnectionNotOpen");
                    }
                }
                catch (Exception error) when (Recoverable(error)) { Fail(result, ErrorCode(error)); }
                // 연결 중간에 실패해도 포트가 열렸을 수 있으므로 닫힘 확인까지 소유권을 보존한다.
                if (!connectionAttempted) owner = null;
                else if (!DisconnectCore().IsSuccess) result.CleanupCode = "DisconnectFailed";
                return result;
            }
        }

        public PrinterResult GetConnectionStatus()
        {
            lock (Gate)
            {
                CheckDisposed();
                if (owner != this) return Failure("NotConnected");
                var result = Success();
                result.Function = "CheckPrtConnection";
                try
                {
                    result.NativeCode = Vendor.CheckPrtConnection();
                    return result.NativeCode == 1 ? result : Fail(result, "ConnectionNotOpen");
                }
                catch (Exception error) when (Recoverable(error)) { return Fail(result, ErrorCode(error)); }
            }
        }

        public PrinterResult SetPrintingOptions(ushort speed, ushort darkness)
        {
            if (speed == 0 || darkness < 1 || darkness > 19) throw InputError("InvalidPrintingOptions");
            lock (Gate)
            {
                CheckDisposed();
                if (owner != this) return Failure("NotConnected");
                if (faulted) return Failure("ConnectionFaulted");
                var result = Success();
                result.Function = "SetPrintingOption";
                try
                {
                    result.NativeCode = Vendor.SetPrintingOption(speed, darkness);
                    if (result.NativeCode != 0) return Fail(result, "PrintingOptionsFailed");
                    this.speed = speed; this.darkness = darkness; hasOptions = true;
                    return result;
                }
                catch (Exception error) when (Recoverable(error)) { return Fail(result, ErrorCode(error)); }
            }
        }

        /// <summary>변수 서식이면 V00부터 최고 참조 번호까지 모든 값을 매번 제공해야 합니다.</summary>
        public PrinterResult Print(string btpl, params string[] values)
        {
            string variables = PrepareVariables(values);
            CheckText(btpl, "InvalidForm");
            btpl = btpl.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");
            if (!btpl.EndsWith("\r\n", StringComparison.Ordinal)) btpl += "\r\n";
            CheckText(btpl, "InvalidForm");
            lock (Gate) { CheckDisposed(); return PrintCore(btpl, null, variables); }
        }

        public PrinterResult PrintFile(string path, params string[] values)
        {
            string variables = PrepareVariables(values);
            CheckText(path, "InvalidFile");
            lock (Gate)
            {
                CheckDisposed();
                FileStream file = null;
                try
                {
                    path = Path.GetFullPath(path);
                    CheckText(path, "InvalidFile");
                    file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (file.Length == 0 || file.Length > MaximumBytes) throw InputError("InvalidFile");
                    // 검사한 스트림을 SDK 반환과 서식 정리가 끝날 때까지 유지하여 쓰기·삭제를 막는다.
                    using (var reader = new StreamReader(file, Cp949, false, 4096, true)) CheckText(reader.ReadToEnd(), "InvalidFile");
                }
                catch (Exception error) when (Recoverable(error))
                {
                    if (file != null) file.Dispose();
                    throw InputError("InvalidFile");
                }
                try { return PrintCore(null, path, variables); }
                finally { file.Dispose(); }
            }
        }

        public PrinterResult ConfirmPrintPosition()
        {
            lock (Gate)
            {
                CheckDisposed();
                if (owner != this) return Failure("NotConnected");
                // 센서 조회가 아닌 호출자의 수동 용지 정렬 확인이다. 연결 정리 오류는 해제하지 않는다.
                Positions[port] = 0;
                return Success();
            }
        }

        public PrinterResult Disconnect()
        {
            lock (Gate) { CheckDisposed(); return DisconnectCore(); }
        }

        public void Dispose()
        {
            lock (Gate)
            {
                if (disposed) return;
                // 다른 스레드의 Dispose도 인쇄 및 finally 종료 후에만 SDK 연결을 닫는다.
                if (!DisconnectCore().IsSuccess) throw new InvalidOperationException("DisconnectFailed");
                disposed = true;
            }
        }

        private PrinterResult DisconnectCore()
        {
            if (owner != this) return Success();
            // 해제 중·실패 연결은 닫힘이 확인될 때까지 인쇄와 옵션 변경에 재사용하지 않는다.
            faulted = true;
            var result = Success();
            try
            {
                result.Function = "DeviceDisconnect";
                result.NativeCode = Vendor.DeviceDisconnect();
                if (result.NativeCode != 0) return Fail(result, "DisconnectFailed");
                result.Function = "CheckPrtConnection";
                result.NativeCode = null;
                result.NativeCode = Vendor.CheckPrtConnection();
                if (result.NativeCode != 0) return Fail(result, "DisconnectNotClosed");
                owner = null; faulted = false;
                return result;
            }
            catch (Exception error) when (Recoverable(error)) { return Fail(result, ErrorCode(error)); }
        }

        private PrinterResult PrintCore(string form, string path, string variables)
        {
            if (owner != this) return Failure("NotConnected");
            if (faulted) return Failure("ConnectionFaulted");
            if (!Positions[port].HasValue) return Failure("MediaPositionUnknown");
            var result = Success();
            bool formTouched = false;
            try
            {
                result.Function = "CheckPrtConnection";
                result.NativeCode = Vendor.CheckPrtConnection();
                if (result.NativeCode != 1) return Fail(result, "ConnectionNotOpen");
                if (Positions[port].Value > 0 && !Move(result, "^B", Positions[port].Value)) return result;
                result.Function = "ResetForm"; result.NativeCode = null; formTouched = true;
                Vendor.ResetForm();
                if (hasOptions)
                {
                    result.Function = "SetPrintingOption";
                    result.NativeCode = null;
                    result.NativeCode = Vendor.SetPrintingOption(speed, darkness);
                    if (result.NativeCode != 0) return Fail(result, "PrintingOptionsFailed");
                }
                result.Function = path == null ? "SetPrintFormString" : "SetPrintFormFile";
                result.NativeCode = null;
                result.NativeCode = path == null ? Vendor.SetPrintFormString(form) : Vendor.SetPrintFormFile(path);
                if (result.NativeCode != 0) return Fail(result, "FormRegistrationFailed");
                // ResetForm이 변수 저장소를 비운다고 가정하지 않으므로 값이 있는 요청은 항상 재등록한다.
                if (variables != null)
                {
                    result.Function = "SetPrintFormVariableData"; result.NativeCode = null;
                    result.NativeCode = Vendor.SetPrintFormVariableData(variables);
                    if (result.NativeCode != 0) return Fail(result, "VariableRegistrationFailed");
                }
                result.Function = "StartLabelPrint"; result.NativeCode = null;
                var response = new StringBuilder(100);
                result.PrintInvoked = true; Positions[port] = null;
                // shortcut: 제조사 StringBuilder 출력 계약을 사용한다. SDK 버퍼 결함 방어가 필요하면 별도 재설계한다.
                Vendor.StartLabelPrint(1, response);
                result.PrintCode = ParsePrintCode(response);
                result.NativeCode = result.PrintCode;
                if (result.PrintCode == 0)
                {
                    Positions[port] = 0;
                    if (presentationDistance > 0) Move(result, "^M", presentationDistance);
                }
                else if (result.PrintCode.HasValue && KnownFailure(result.PrintCode.Value)) Fail(result, "PrintFailed");
                else { result.Status = PrinterResultStatus.Unknown; result.Code = "PrintResponseUnknown"; }
                return result;
            }
            catch (Exception error) when (Recoverable(error))
            {
                Fail(result, ErrorCode(error));
                if (result.PrintInvoked) result.Status = PrinterResultStatus.Unknown;
                return result;
            }
            finally
            {
                if (formTouched)
                {
                    try { Vendor.ResetForm(); }
                    catch (Exception error) when (Recoverable(error))
                    {
                        faulted = true; result.CleanupCode = ErrorCode(error);
                        // 정리 오류로 인쇄 증거와 불명 상태를 덮으면 자동 재인쇄를 유발할 수 있다.
                        if (result.IsSuccess) Fail(result, "CleanupFailed");
                    }
                }
            }
        }

        private bool Move(PrinterResult result, string command, int distance)
        {
            result.Function = "SendData"; result.NativeCode = null;
            result.MovementCode = null;
            // 실패 코드도 일부 이동 후 반환될 수 있으므로 SDK 진입 전에 위치를 불명으로 둔다.
            Positions[port] = null;
            result.MovementCode = Vendor.SendData(command + distance.ToString(CultureInfo.InvariantCulture) + "\r\n");
            result.NativeCode = result.MovementCode;
            if (result.MovementCode != 0) { Fail(result, "MovementFailed"); return false; }
            Positions[port] = command == "^B" ? 0 : distance;
            return true;
        }

        private static int? ParsePrintCode(StringBuilder response)
        {
            // UID는 형식 확인에만 사용하고 결과·예외·로그로 복사하지 않는다.
            string text = response.ToString();
            int separator = text.LastIndexOf(',');
            if (separator < 0) return null;
            string uid = text.Substring(0, separator);
            if (uid != "" && uid != "Unknown")
            {
                int byteCount = (uid.Length + 1) / 3;
                if ((byteCount != 4 && byteCount != 7 && byteCount != 8 && byteCount != 10) ||
                    !Regex.IsMatch(uid, "\\A[0-9A-F]{2}(?::[0-9A-F]{2})+\\z")) return null;
            }
            string suffix = text.Substring(separator + 1);
            int code;
            return int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out code) &&
                suffix == code.ToString(CultureInfo.InvariantCulture) ? (int?)code : null;
        }

        private static bool KnownFailure(int code)
        {
            return (code >= 1 && code <= 11) || code == 20 || code == 21 || code == 22 || code == 50 || code == 60 ||
                code == 102 || code == 103 || code == 104 || code == 105 || code == 205;
        }

        private static string PrepareVariables(string[] values)
        {
            if (values == null) throw InputError("InvalidVariables");
            values = (string[])values.Clone();
            long totalBytes = values.Length == 0 ? 0 : ((long)values.Length - 1) * 2;
            foreach (string value in values)
            {
                CheckText(value, "InvalidVariables");
                if (value.Any(char.IsControl) || value.Contains("!!") || value.StartsWith("!", StringComparison.Ordinal) || value.EndsWith("!", StringComparison.Ordinal))
                    throw InputError("InvalidVariables");
                totalBytes += Cp949.GetByteCount(value);
                if (totalBytes > MaximumBytes) throw InputError("InvalidVariables");
            }
            if (values.Length == 0) return null;
            string joined = string.Join("!!", values);
            CheckText(joined, "InvalidVariables");
            return joined;
        }

        private static void CheckText(string value, string code)
        {
            if (Encoding.Default.CodePage != 949) throw new NotSupportedException("UnsupportedAnsiCodePage");
            if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumBytes || value.IndexOf('\0') >= 0) throw InputError(code);
            try
            {
                byte[] bytes = Cp949.GetBytes(value);
                if (bytes.Length > MaximumBytes || Cp949.GetString(bytes) != value) throw InputError(code);
            }
            catch (EncoderFallbackException) { throw InputError(code); }
            catch (DecoderFallbackException) { throw InputError(code); }
        }

        private void CheckDisposed() { if (disposed) throw new ObjectDisposedException("Bt003Printer"); }
        private static ArgumentException InputError(string code) { return new ArgumentException(code); }
        private static PrinterResult Success() { return new PrinterResult { Status = PrinterResultStatus.Succeeded, Code = "Succeeded" }; }
        private static PrinterResult Failure(string code) { return Fail(Success(), code); }
        private static PrinterResult Fail(PrinterResult result, string code) { result.Status = PrinterResultStatus.Failed; result.Code = code; return result; }
        private static bool Recoverable(Exception error) { return !(error is OutOfMemoryException || error is StackOverflowException || error is AccessViolationException || error is System.Threading.ThreadAbortException); }
        private static string ErrorCode(Exception error)
        {
            if (error is DllNotFoundException) return "SdkDllMissing";
            if (error is BadImageFormatException) return "SdkArchitectureMismatch";
            if (error is EntryPointNotFoundException) return "SdkEntryPointMissing";
            return "SdkCallFailed";
        }
    }
}
