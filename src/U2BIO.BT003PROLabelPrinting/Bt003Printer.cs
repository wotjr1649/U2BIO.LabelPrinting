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
    /// <summary>제조사 SDK를 사용하여 USB 연결과 한 장 인쇄를 동기적으로 처리합니다.</summary>
    /// <remarks>
    /// 한 프로세스에서 하나의 객체만 SDK 연결을 소유할 수 있습니다. 같은 프린터를 다른 프로그램과 함께 사용하지 않습니다.
    /// 모든 SDK 호출과 정리는 직렬화되며, 호출이 반환할 때까지 기다립니다. UI의 백그라운드 실행은 호출 프로그램이 맡습니다.
    /// 서식 해석과 변수 치환은 SDK가 담당하며, 이 클래스는 입력 검사와 호출 순서, 결과 기록을 담당합니다.
    /// </remarks>
    public sealed class Bt003Printer : IDisposable
    {
        private const int MaximumBytes = 4 * 1024 * 1024;

        // SDK의 연결과 서식은 전역 상태이므로 다른 객체의 호출이나 Dispose가 작업 중간에 끼어들지 않게 한다.
        private static readonly object Gate = new object();

        // 포트별 명령 이력: null은 위치 불명, 0은 복귀 불필요, 양수는 다음 인쇄 전에 복귀할 거리(mm)다.
        // 객체를 교체하거나 재연결해도 이 기록은 유지되며, 센서로 확인한 실제 용지 위치를 뜻하지 않는다.
        private static readonly Dictionary<string, int?> Positions = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
        private static readonly Encoding Cp949 = Encoding.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

        // owner는 SDK 연결을 사용할 객체다. 연결 해제와 닫힘이 확인되기 전에는 다른 객체에 넘기지 않는다.
        private static Bt003Printer owner;
        private string port;
        private int presentationDistance;
        private ushort speed, darkness;

        // hasOptions는 정상 설정을 보관했는지, faulted는 연결 복구가 필요한지, disposed는 해제를 마쳤는지를 나타낸다.
        private bool hasOptions, faulted, disposed;

        /// <summary>USB 포트를 지정하고 SDK 연결이 열렸는지 확인합니다.</summary>
        /// <param name="port">제조사 SDK에서 사용하는 USB 포트 식별 문자열입니다. 빈 값과 제어문자는 허용하지 않습니다.</param>
        /// <param name="presentationDistanceMm">정상 인쇄 응답 뒤 추가로 배출할 거리입니다. 0..1000 mm이며 기본값 0은 추가 배출 없음입니다.</param>
        /// <returns>포트 지정, 연결 또는 연결 확인 결과입니다. 이미 연결을 소유한 객체가 있으면 SDK를 호출하지 않고 실패를 반환합니다.</returns>
        /// <remarks>
        /// SetPrinterPort → DeviceConnect → CheckPrtConnection 순서로 호출하며, 연결 확인 값 1을 정상으로 처리합니다.
        /// 정상 배출 뒤 같은 포트의 다음 인쇄에서는 기록한 거리만큼 먼저 복귀합니다. 거리는 사용하는 용지에 맞춰 지정합니다.
        /// 연결 도중 실패하면 해제를 시도합니다. 닫힘을 확인하지 못하면 소유권을 유지하며 명시적인 Disconnect가 필요합니다.
        /// </remarks>
        /// <exception cref="ArgumentException">포트 또는 배출 거리 입력이 유효하지 않습니다.</exception>
        /// <exception cref="NotSupportedException">현재 ANSI 코드 페이지가 949가 아닙니다.</exception>
        /// <exception cref="ObjectDisposedException">이미 Dispose를 완료한 객체입니다.</exception>
        public PrinterResult ConnectUsb(string port, int presentationDistanceMm = 0)
        {
            CheckText(port, "InvalidPort");
            if (port.Any(char.IsControl))
            {
                throw InputError("InvalidPort");
            }
            if (presentationDistanceMm < 0 || presentationDistanceMm > 1000)
            {
                throw InputError("InvalidPresentationDistance");
            }

            lock (Gate)
            {
                CheckDisposed();
                if (owner != null)
                {
                    return Failure(owner == this ? "이 객체가 이미 SDK 연결을 소유하고 있습니다." : "다른 객체가 SDK 연결을 사용하고 있습니다.");
                }

                this.port = port;
                presentationDistance = presentationDistanceMm;
                if (!Positions.ContainsKey(port))
                {
                    Positions.Add(port, 0);
                }
                owner = this;

                // 연결 확인 전의 부분 연결과 해제 실패 상태에서는 인쇄를 허용하지 않는다.
                faulted = true;
                var result = Success();
                string function = "SetPrinterPort";
                bool connectionAttempted = false;
                try
                {
                    result.NativeCode = Vendor.SetPrinterPort(1, port, "");
                    if (result.NativeCode != 0)
                    {
                        owner = null;
                        return Fail(result, "USB 포트 지정에 실패했습니다. SetPrinterPort 응답: " + result.NativeCode + ".");
                    }

                    function = "DeviceConnect";
                    result.NativeCode = null;
                    connectionAttempted = true;
                    result.NativeCode = Vendor.DeviceConnect();
                    if (result.NativeCode != 0)
                    {
                        Fail(result, "SDK 연결에 실패했습니다. DeviceConnect 응답: " + result.NativeCode + ".");
                    }
                    else
                    {
                        function = "CheckPrtConnection";
                        result.NativeCode = null;
                        result.NativeCode = Vendor.CheckPrtConnection();
                        if (result.NativeCode == 1)
                        {
                            faulted = false;
                            result.Message = "SDK 연결이 열렸습니다. CheckPrtConnection 응답: 1.";
                            return result;
                        }
                        Fail(result, "SDK 연결이 열린 상태가 아닙니다. CheckPrtConnection 응답: " + result.NativeCode + ".");
                    }
                }
                catch (Exception error) when (Recoverable(error))
                {
                    Fail(result, function + " 호출 중 " + ErrorMessage(error));
                }

                // 연결 중간에 실패해도 포트가 열렸을 수 있으므로 닫힘 확인까지 소유권을 보존한다.
                if (!connectionAttempted)
                {
                    owner = null;
                }
                else
                {
                    var cleanup = DisconnectCore();
                    if (cleanup.Status != PrinterResultStatus.Succeeded)
                    {
                        // 연결 실패의 숫자와 설명은 유지하고, 추가 해제 실패의 설명만 덧붙인다.
                        result.Message += " 연결 정리도 실패했습니다. " + cleanup.Message;
                    }
                }
                return result;
            }
        }

        /// <summary>이 객체가 소유한 SDK 연결의 열림 상태를 조회합니다.</summary>
        /// <returns>NativeCode가 1이면 열린 연결입니다. 소유자가 아니면 Failed와 한글 설명을 반환하며 SDK를 조회하지 않습니다.</returns>
        /// <remarks>포트 연결만 확인합니다. 용지·리본·헤드 준비, 실제 인쇄 완료 또는 faulted 해소를 확인하는 함수가 아닙니다.</remarks>
        /// <exception cref="ObjectDisposedException">이미 Dispose를 완료한 객체입니다.</exception>
        public PrinterResult GetConnectionStatus()
        {
            lock (Gate)
            {
                CheckDisposed();
                if (owner != this)
                {
                    return Failure("이 객체가 소유한 SDK 연결이 없습니다. ConnectUsb로 먼저 연결하십시오.");
                }
                var result = Success();
                try
                {
                    result.NativeCode = Vendor.CheckPrtConnection();
                    if (result.NativeCode != 1)
                    {
                        return Fail(result, "SDK 연결이 열린 상태가 아닙니다. CheckPrtConnection 응답: " + result.NativeCode + ".");
                    }
                    result.Message = "SDK 연결이 열려 있습니다. CheckPrtConnection 응답: 1.";
                    return result;
                }
                catch (Exception error) when (Recoverable(error))
                {
                    return Fail(result, "CheckPrtConnection 호출 중 " + ErrorMessage(error));
                }
            }
        }

        /// <summary>SDK에 인쇄 속도와 농도를 설정하고, 정상 설정을 이후 인쇄에 사용할 수 있도록 보관합니다.</summary>
        /// <param name="speed">0보다 큰 SDK 속도 값입니다. 실제 지원 값은 제조사 SDK와 사용하는 프린터에 따릅니다.</param>
        /// <param name="darkness">1..19 범위의 인쇄 농도입니다.</param>
        /// <returns>SDK 설정 결과입니다. 연결 소유권이 없거나 연결 복구가 필요하면 설정하지 않고 실패를 반환합니다.</returns>
        /// <remarks>성공한 값만 보관하여 인쇄마다 재적용합니다. 서식에 지정된 ^S와 ^H의 우선순위는 SDK 계약을 따릅니다.</remarks>
        /// <exception cref="ArgumentException">속도 또는 농도 입력이 유효하지 않습니다.</exception>
        /// <exception cref="ObjectDisposedException">이미 Dispose를 완료한 객체입니다.</exception>
        public PrinterResult SetPrintingOptions(ushort speed, ushort darkness)
        {
            if (speed == 0 || darkness < 1 || darkness > 19)
            {
                throw InputError("InvalidPrintingOptions");
            }
            lock (Gate)
            {
                CheckDisposed();
                if (owner != this)
                {
                    return Failure("이 객체가 소유한 SDK 연결이 없습니다. ConnectUsb로 먼저 연결하십시오.");
                }
                if (faulted)
                {
                    return Failure("SDK 연결 복구가 필요합니다. Disconnect로 닫힘을 확인한 뒤 다시 연결하십시오.");
                }
                var result = Success();
                try
                {
                    result.NativeCode = Vendor.SetPrintingOption(speed, darkness);
                    if (result.NativeCode != 0)
                    {
                        return Fail(result, "인쇄 옵션 설정에 실패했습니다. SetPrintingOption 응답: " + result.NativeCode + ".");
                    }
                    this.speed = speed;
                    this.darkness = darkness;
                    hasOptions = true;
                    result.Message = "인쇄 옵션의 SDK 설정이 정상입니다. SetPrintingOption 응답: 0.";
                    return result;
                }
                catch (Exception error) when (Recoverable(error))
                {
                    return Fail(result, "SetPrintingOption 호출 중 " + ErrorMessage(error));
                }
            }
        }

        /// <summary>BTPL 문자열과 선택 변수를 SDK에 등록하여 한 장을 인쇄합니다.</summary>
        /// <param name="btpl">빈 값이 아닌 CP949로 표현 가능한 서식입니다. 줄끝과 마지막 줄끝은 CRLF로 정규화합니다.</param>
        /// <param name="values">V00부터 순서대로 제공할 변수 값입니다. 변수 없는 서식이면 생략합니다.</param>
        /// <returns>인쇄·이동·서식 정리를 포함한 결과입니다. 인쇄 응답은 PrintCode에 별도로 보존합니다.</returns>
        /// <remarks>
        /// 변수 서식이면 V00부터 최고 참조 번호까지 모든 값을 요청마다 제공해야 합니다. DLL은 변수 번호와 AW를 해석하지 않습니다.
        /// 서식과 합친 변수는 각각 CP949 4 MiB 이내여야 합니다. SDK 응답이 불명이면 자동으로 다시 인쇄하지 않습니다.
        /// </remarks>
        /// <exception cref="ArgumentException">서식 또는 변수 입력이 유효하지 않습니다.</exception>
        /// <exception cref="NotSupportedException">현재 ANSI 코드 페이지가 949가 아닙니다.</exception>
        /// <exception cref="ObjectDisposedException">이미 Dispose를 완료한 객체입니다.</exception>
        public PrinterResult Print(string btpl, params string[] values)
        {
            string variables = PrepareVariables(values);
            btpl = PrepareForm(btpl, "InvalidForm");
            lock (Gate)
            {
                CheckDisposed();
                return PrintCore(btpl, variables);
            }
        }

        /// <summary>CP949 서식 파일을 읽고 검사한 내용을 SDK에 문자열로 등록하여 한 장을 인쇄합니다.</summary>
        /// <param name="path">읽을 수 있는 서식 파일 경로입니다. 상대경로는 현재 작업 디렉터리 기준으로 절대경로화합니다.</param>
        /// <param name="values">V00부터 최고 참조 번호까지 순서대로 제공할 변수 값입니다. 변수 없는 서식이면 생략합니다.</param>
        /// <returns>Print와 같은 인쇄 결과입니다. 파일 읽기·검증 오류는 SDK 호출 전에 예외로 전달합니다.</returns>
        /// <remarks>
        /// 파일은 비어 있지 않아야 하며 4 MiB 이내여야 합니다. 엄격한 CP949로 읽고 CRLF 정규화 후 크기를 다시 검사합니다.
        /// 파일은 읽기 공유만 허용하여 열고, SDK 인쇄와 서식 정리가 끝날 때까지 유지한 뒤 닫습니다.
        /// SDK에는 경로 대신 읽은 내용을 넘깁니다. AW 해석은 SDK가 담당하며, 실패 후 다른 방식으로 자동 재인쇄하지 않습니다.
        /// </remarks>
        /// <exception cref="ArgumentException">파일 경로·내용·읽기 또는 변수 입력이 유효하지 않습니다.</exception>
        /// <exception cref="NotSupportedException">현재 ANSI 코드 페이지가 949가 아닙니다.</exception>
        /// <exception cref="ObjectDisposedException">이미 Dispose를 완료한 객체입니다.</exception>
        public PrinterResult PrintFile(string path, params string[] values)
        {
            string variables = PrepareVariables(values);
            CheckText(path, "InvalidFile");
            lock (Gate)
            {
                CheckDisposed();
                FileStream file = null;
                string form;
                try
                {
                    path = Path.GetFullPath(path);
                    CheckText(path, "InvalidFile");
                    file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (file.Length == 0 || file.Length > MaximumBytes)
                    {
                        throw InputError("InvalidFile");
                    }

                    // 검사한 스트림을 SDK 반환과 서식 정리가 끝날 때까지 유지하여 쓰기·삭제를 막는다.
                    // reader만 먼저 닫고 파일은 남겨 두기 위해 leaveOpen을 true로 지정한다.
                    using (var reader = new StreamReader(file, Cp949, false, 4096, true))
                    {
                        form = PrepareForm(reader.ReadToEnd(), "InvalidFile");
                    }
                }
                catch (Exception error) when (Recoverable(error))
                {
                    if (file != null)
                    {
                        file.Dispose();
                    }
                    throw InputError("InvalidFile");
                }

                // SDK의 파일 재열기 대신 검사한 내용을 등록한다. AW 해석은 SDK가 담당한다.
                try
                {
                    return PrintCore(form, variables);
                }
                finally
                {
                    // PrintCore의 finally까지 끝난 뒤 닫으므로 정리 실패가 있어도 파일 잠금은 해제된다.
                    file.Dispose();
                }
            }
        }

        /// <summary>호출자가 용지를 수동으로 정렬했음을 확인하고 해당 포트의 위치 기록을 0으로 바꿉니다.</summary>
        /// <returns>위치 기록을 바꿨으면 Succeeded, 연결 소유자가 아니면 Failed와 한글 설명을 반환합니다.</returns>
        /// <remarks>SDK 센서를 조회하거나 용지를 이동하지 않습니다. faulted를 해제하지 않으므로 연결 오류는 별도로 복구해야 합니다.</remarks>
        /// <exception cref="ObjectDisposedException">이미 Dispose를 완료한 객체입니다.</exception>
        public PrinterResult ConfirmPrintPosition()
        {
            lock (Gate)
            {
                CheckDisposed();
                if (owner != this)
                {
                    return Failure("이 객체가 소유한 SDK 연결이 없습니다. ConnectUsb로 먼저 연결하십시오.");
                }
                // 센서 조회가 아닌 호출자의 수동 용지 정렬 확인이다. 연결 정리 오류는 해제하지 않는다.
                Positions[port] = 0;
                return Success("호출자의 수동 용지 정렬 확인을 기록했습니다. SDK 조회나 이동은 수행하지 않았습니다.");
            }
        }

        /// <summary>이 객체의 SDK 연결을 해제하고 포트가 닫혔는지 확인합니다.</summary>
        /// <returns>해제·닫힘 확인 결과입니다. 연결 소유자가 아니면 SDK를 호출하지 않고 정상 결과를 반환합니다.</returns>
        /// <remarks>해제 실패 시 소유권과 faulted를 유지합니다. 원인을 확인한 뒤 Disconnect를 다시 호출할 수 있습니다.</remarks>
        /// <exception cref="ObjectDisposedException">이미 Dispose를 완료한 객체입니다.</exception>
        public PrinterResult Disconnect()
        {
            lock (Gate)
            {
                CheckDisposed();
                return DisconnectCore();
            }
        }

        /// <summary>진행 중인 SDK 작업과 정리가 끝나기를 기다린 뒤 연결을 해제하고 객체 사용을 종료합니다.</summary>
        /// <remarks>
        /// 성공한 Dispose를 반복 호출해도 아무 작업을 하지 않습니다. using 종료에서도 연결 해제를 수행합니다.
        /// 해제를 확인하지 못하면 disposed를 설정하지 않으므로, 오류를 처리한 뒤 Disconnect와 Dispose를 재시도할 수 있습니다.
        /// </remarks>
        /// <exception cref="InvalidOperationException">연결 해제를 확인하지 못했습니다. 메시지는 DisconnectFailed입니다.</exception>
        public void Dispose()
        {
            lock (Gate)
            {
                if (disposed)
                {
                    return;
                }
                // 다른 스레드의 Dispose도 인쇄 및 finally 종료 후에만 SDK 연결을 닫는다.
                if (DisconnectCore().Status != PrinterResultStatus.Succeeded)
                {
                    throw new InvalidOperationException("DisconnectFailed");
                }
                disposed = true;
            }
        }

        private PrinterResult DisconnectCore()
        {
            if (owner != this)
            {
                return Success("이 객체가 소유한 SDK 연결이 없어 해제 호출 없이 완료했습니다.");
            }
            // 해제 중·실패 연결은 닫힘이 확인될 때까지 인쇄와 옵션 변경에 재사용하지 않는다.
            faulted = true;
            var result = Success();
            string function = "DeviceDisconnect";
            try
            {
                result.NativeCode = Vendor.DeviceDisconnect();
                if (result.NativeCode != 0)
                {
                    return Fail(result, "SDK 연결 해제에 실패했습니다. DeviceDisconnect 응답: " + result.NativeCode + ".");
                }

                function = "CheckPrtConnection";
                result.NativeCode = null;
                result.NativeCode = Vendor.CheckPrtConnection();
                if (result.NativeCode != 0)
                {
                    return Fail(result, "SDK 연결의 닫힘을 확인하지 못했습니다. CheckPrtConnection 응답: " + result.NativeCode + ".");
                }
                owner = null;
                faulted = false;
                result.Message = "SDK 연결의 닫힘을 확인했습니다. CheckPrtConnection 응답: 0.";
                return result;
            }
            catch (Exception error) when (Recoverable(error))
            {
                return Fail(result, function + " 호출 중 " + ErrorMessage(error));
            }
        }

        private PrinterResult PrintCore(string form, string variables)
        {
            // Print와 PrintFile이 검사한 문자열을 전달하며, 두 함수 모두 Gate 안에서 이 메서드를 호출한다.
            if (owner != this)
            {
                return Failure("이 객체가 소유한 SDK 연결이 없습니다. ConnectUsb로 먼저 연결하십시오.");
            }
            if (faulted)
            {
                return Failure("SDK 연결 복구가 필요합니다. Disconnect로 닫힘을 확인한 뒤 다시 연결하십시오.");
            }
            if (!Positions[port].HasValue)
            {
                return Failure("용지 위치가 불명입니다. 실제 용지를 정렬한 뒤 ConfirmPrintPosition으로 확인하십시오.");
            }

            var result = Success();
            string function = "CheckPrtConnection";
            // 최초 ResetForm에서 예외가 나도 마지막 정리를 시도하도록 호출 전에 formTouched를 true로 설정한다.
            bool formTouched = false;
            try
            {
                // 1. 연결을 확인하고 이전 정상 배출이 있으면 기록된 거리만큼 먼저 복귀한다.
                result.NativeCode = Vendor.CheckPrtConnection();
                if (result.NativeCode != 1)
                {
                    return Fail(result, "SDK 연결이 열린 상태가 아닙니다. CheckPrtConnection 응답: " + result.NativeCode + ".");
                }
                if (Positions[port].Value > 0 && !Move(result, "^B", Positions[port].Value))
                {
                    return result;
                }

                // 2. 이전 서식을 정리한다. 다음 SDK 호출 전에 NativeCode를 비워 예외에 이전 응답이 남지 않게 한다.
                function = "ResetForm";
                result.NativeCode = null;
                formTouched = true;
                Vendor.ResetForm();

                // 3. 성공한 옵션이 있으면 다시 적용하고 서식 내용을 문자열로 등록한다.
                if (hasOptions)
                {
                    function = "SetPrintingOption";
                    result.NativeCode = null;
                    result.NativeCode = Vendor.SetPrintingOption(speed, darkness);
                    if (result.NativeCode != 0)
                    {
                        return Fail(result, "인쇄 옵션 재적용에 실패했습니다. SetPrintingOption 응답: " + result.NativeCode + ".");
                    }
                }
                function = "SetPrintFormString";
                result.NativeCode = null;
                result.NativeCode = Vendor.SetPrintFormString(form);
                if (result.NativeCode != 0)
                {
                    return Fail(result, "서식 등록에 실패했습니다. SetPrintFormString 응답: " + result.NativeCode + ".");
                }

                // ResetForm이 변수 저장소를 비운다고 가정하지 않으므로 값이 있는 요청은 항상 재등록한다.
                if (variables != null)
                {
                    function = "SetPrintFormVariableData";
                    result.NativeCode = null;
                    result.NativeCode = Vendor.SetPrintFormVariableData(variables);
                    if (result.NativeCode != 0)
                    {
                        return Fail(result, "변수 등록에 실패했습니다. SetPrintFormVariableData 응답: " + result.NativeCode + ".");
                    }
                }

                // 4. 여기부터 예외나 응답 불명은 일부 출력 가능성이 있으므로 다음 자동 인쇄를 막는다.
                function = "StartLabelPrint";
                result.NativeCode = null;
                var response = new StringBuilder(100);
                result.PrintInvoked = true;
                Positions[port] = null;
                // shortcut: 제조사 StringBuilder 출력 계약을 사용한다. SDK 버퍼 결함 방어가 필요하면 별도 재설계한다.
                Vendor.StartLabelPrint(1, response);
                result.PrintCode = ParsePrintCode(response);
                result.NativeCode = result.PrintCode;

                // 5. 정상 인쇄 응답에서만 추가 배출한다. 배출 결과와 관계없이 PrintCode는 인쇄 응답을 보존한다.
                if (result.PrintCode == 0)
                {
                    Positions[port] = 0;
                    result.Message = "인쇄 SDK 응답은 정상입니다. StartLabelPrint 응답 코드: 0.";
                    if (presentationDistance > 0)
                    {
                        Move(result, "^M", presentationDistance);
                    }
                }
                else if (result.PrintCode.HasValue && KnownFailure(result.PrintCode.Value))
                {
                    Fail(result, "인쇄 SDK가 실패 응답을 보냈습니다. StartLabelPrint 응답 코드: " + result.PrintCode + ".");
                }
                else
                {
                    result.Status = PrinterResultStatus.Unknown;
                    result.Message = result.PrintCode.HasValue
                        ? "인쇄 결과를 확정할 수 없습니다. StartLabelPrint 응답 코드: " + result.PrintCode + "."
                        : "StartLabelPrint 응답 형식을 해석할 수 없어 인쇄 결과를 확정할 수 없습니다.";
                }
                return result;
            }
            catch (Exception error) when (Recoverable(error))
            {
                Fail(result, function + " 호출 중 " + ErrorMessage(error));
                if (result.PrintInvoked)
                {
                    result.Status = PrinterResultStatus.Unknown;
                }
                return result;
            }
            finally
            {
                // 6. 등록이나 인쇄 도중 반환해도 정리한다. 파일은 이 정리가 끝날 때까지 PrintFile이 보유한다.
                if (formTouched)
                {
                    try
                    {
                        Vendor.ResetForm();
                    }
                    catch (Exception error) when (Recoverable(error))
                    {
                        faulted = true;
                        result.Message += " 서식 정리도 실패했습니다. ResetForm 호출 중 " + ErrorMessage(error);
                        // 정리 오류로 인쇄 증거와 불명 상태를 덮으면 자동 재인쇄를 유발할 수 있다.
                        if (result.Status == PrinterResultStatus.Succeeded)
                        {
                            result.Status = PrinterResultStatus.Failed;
                        }
                    }
                }
            }
        }

        private bool Move(PrinterResult result, string command, int distance)
        {
            // ^B는 이전 배출 거리 복귀, ^M은 이번 인쇄 뒤 추가 배출이다. 0 응답은 실제 위치 확인이 아니다.
            string movement = command == "^B" ? "인쇄 전 복귀" : "인쇄 후 배출";
            string printMessage = result.PrintInvoked ? result.Message + " " : "";
            result.NativeCode = null;
            // 실패 코드도 일부 이동 후 반환될 수 있으므로 SDK 진입 전에 위치를 불명으로 둔다.
            Positions[port] = null;
            try
            {
                result.NativeCode = Vendor.SendData(command + distance.ToString(CultureInfo.InvariantCulture) + "\r\n");
                if (result.NativeCode != 0)
                {
                    Fail(result, printMessage + movement + "에 실패했습니다. SendData 응답: " + result.NativeCode + ".");
                    return false;
                }
                Positions[port] = command == "^B" ? 0 : distance;
                result.Message = command == "^B"
                    ? "인쇄 전 복귀 명령의 SDK 응답은 정상입니다. SendData 응답: 0."
                    : result.Message + " 인쇄 후 배출 명령의 SDK 응답도 정상입니다. SendData 응답: 0.";
                return true;
            }
            catch (Exception error) when (Recoverable(error))
            {
                // 이동 오류는 이 함수에서 설명하므로 상위 함수가 직전 인쇄 단계로 잘못 기록하지 않는다.
                Fail(result, printMessage + movement + "의 SendData 호출 중 " + ErrorMessage(error));
                if (result.PrintInvoked)
                {
                    result.Status = PrinterResultStatus.Unknown;
                }
                return false;
            }
        }

        private static int? ParsePrintCode(StringBuilder response)
        {
            // UID는 형식 확인에만 사용하고 결과·예외·로그로 복사하지 않는다.
            string text = response.ToString();
            int separator = text.LastIndexOf(',');
            if (separator < 0)
            {
                return null;
            }
            string uid = text.Substring(0, separator);
            if (uid != "" && uid != "Unknown")
            {
                int byteCount = (uid.Length + 1) / 3;
                if ((byteCount != 4 && byteCount != 7 && byteCount != 8 && byteCount != 10) ||
                    !Regex.IsMatch(uid, "\\A[0-9A-F]{2}(?::[0-9A-F]{2})+\\z"))
                {
                    return null;
                }
            }
            string suffix = text.Substring(separator + 1);
            int code;
            return int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out code) &&
                suffix == code.ToString(CultureInfo.InvariantCulture) ? (int?)code : null;
        }

        private static bool KnownFailure(int code)
        {
            // 기존에 분류한 실패 코드만 Failed로 처리한다. 101과 미확인 코드는 Unknown으로 처리한다.
            return (code >= 1 && code <= 11) || code == 20 || code == 21 || code == 22 || code == 50 || code == 60 ||
                code == 102 || code == 103 || code == 104 || code == 105 || code == 205;
        }

        private static string PrepareVariables(string[] values)
        {
            if (values == null)
            {
                throw InputError("InvalidVariables");
            }

            // 호출자의 배열 변경 영향을 피하고, 연결 전에 !! 구분자를 포함한 전체 바이트 수를 확인한다.
            values = (string[])values.Clone();
            long totalBytes = values.Length == 0 ? 0 : ((long)values.Length - 1) * 2;
            foreach (string value in values)
            {
                CheckText(value, "InvalidVariables");
                if (value.Any(char.IsControl) || value.Contains("!!") || value.StartsWith("!", StringComparison.Ordinal) || value.EndsWith("!", StringComparison.Ordinal))
                {
                    throw InputError("InvalidVariables");
                }
                totalBytes += Cp949.GetByteCount(value);
                if (totalBytes > MaximumBytes)
                {
                    throw InputError("InvalidVariables");
                }
            }
            if (values.Length == 0)
            {
                return null;
            }
            string joined = string.Join("!!", values);
            CheckText(joined, "InvalidVariables");
            return joined;
        }

        private static string PrepareForm(string form, string code)
        {
            // CRLF 정규화로 바이트 수가 늘 수 있으므로 전후 크기를 모두 검사한다. BTPL 명령 자체는 해석하지 않는다.
            CheckText(form, code);
            form = form.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");
            if (!form.EndsWith("\r\n", StringComparison.Ordinal))
            {
                form += "\r\n";
            }
            CheckText(form, code);
            return form;
        }

        private static void CheckText(string value, string code)
        {
            // ANSI P/Invoke와 CP949 파일 계약을 맞추고, 변환 과정에서 문자가 바뀌거나 NUL로 잘리는 입력을 거부한다.
            if (Encoding.Default.CodePage != 949)
            {
                throw new NotSupportedException("UnsupportedAnsiCodePage");
            }
            if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumBytes || value.IndexOf('\0') >= 0)
            {
                throw InputError(code);
            }
            try
            {
                byte[] bytes = Cp949.GetBytes(value);
                if (bytes.Length > MaximumBytes || Cp949.GetString(bytes) != value)
                {
                    throw InputError(code);
                }
            }
            catch (EncoderFallbackException)
            {
                throw InputError(code);
            }
            catch (DecoderFallbackException)
            {
                throw InputError(code);
            }
        }

        private void CheckDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException("Bt003Printer");
            }
        }

        private static ArgumentException InputError(string code)
        {
            // 서식·변수·파일 경로와 원래 예외를 노출하지 않고 고정 식별 코드만 전달한다.
            return new ArgumentException(code);
        }

        private static PrinterResult Success(string message = "SDK 주 작업이 정상적으로 끝났습니다.")
        {
            return new PrinterResult { Status = PrinterResultStatus.Succeeded, Message = message };
        }

        private static PrinterResult Failure(string message)
        {
            return new PrinterResult { Status = PrinterResultStatus.Failed, Message = message };
        }

        private static PrinterResult Fail(PrinterResult result, string message)
        {
            // 결과를 실패로 바꿀 때도 이미 기록한 인쇄·이동·정리 응답은 남겨 둔다.
            result.Status = PrinterResultStatus.Failed;
            result.Message = message;
            return result;
        }

        private static bool Recoverable(Exception error)
        {
            // 치명적인 런타임 오류는 일반 SDK 실패로 변환하지 않는다. 기존 예외 정책을 유지한다.
            return !(error is OutOfMemoryException || error is StackOverflowException || error is AccessViolationException || error is System.Threading.ThreadAbortException);
        }

        private static string ErrorMessage(Exception error)
        {
            if (error is DllNotFoundException)
            {
                return "제조사 SDK DLL을 찾을 수 없습니다.";
            }
            if (error is BadImageFormatException)
            {
                return "제조사 SDK DLL의 형식 또는 실행 비트수를 확인해야 합니다.";
            }
            if (error is EntryPointNotFoundException)
            {
                return "제조사 SDK DLL에서 필요한 함수를 찾을 수 없습니다.";
            }
            // 원래 예외 메시지에는 경로나 입력이 포함될 수 있으므로 고정 설명만 사용한다.
            return "SDK 호출 오류가 발생했습니다.";
        }
    }
}
