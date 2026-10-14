using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HFSample
{
    // 제조사 함수만 대체한다. 제품 로직은 실제 제품 두 소스를 Link로 컴파일한다.
    internal static class BT003CtrlWrapper
    {
        internal static readonly List<string> Calls = new List<string>();
        internal static string FailAt, ThrowAt, Response, LastForm, Variables;
        internal static string ExceptionText;
        internal static int? ConnectCode, DisconnectCode;
        internal static int FailCode, PortState, ResetCount;
        internal static bool CleanupThrows, KeepOpen;
        internal static Action BeforePrint, BeforeCleanup, AfterSendData;

        internal static void Clear()
        {
            Calls.Clear(); FailAt = ThrowAt = null; Response = ",0";
            LastForm = Variables = null; FailCode = PortState = ResetCount = 0;
            CleanupThrows = KeepOpen = false; BeforePrint = BeforeCleanup = AfterSendData = null;
            ExceptionText = "SyntheticSdkFailure"; ConnectCode = DisconnectCode = null;
        }
        private static int Call(string function)
        {
            Calls.Add(function);
            if (ThrowAt == function) throw new InvalidOperationException(ExceptionText);
            return FailAt == function ? FailCode : 0;
        }
        public static int SetPrinterPort(ushort type, string port, string ip)
        {
            if (type != 1 || ip != "") throw new InvalidOperationException("USB arguments differ.");
            return Call("SetPrinterPort");
        }
        public static int DeviceConnect()
        {
            int code = Call("DeviceConnect"); PortState = 1; return ConnectCode ?? code;
        }
        public static int CheckPrtConnection() { Call("CheckPrtConnection"); return PortState; }
        public static int DeviceDisconnect()
        {
            int code = Call("DeviceDisconnect");
            code = DisconnectCode ?? code;
            if (code == 0 && !KeepOpen) PortState = 0;
            return code;
        }
        public static int SetPrintingOption(ushort speed, ushort darkness) => Call("SetPrintingOption");
        public static void ResetForm()
        {
            Call("ResetForm"); ResetCount++;
            if (ResetCount > 1) {
                BeforeCleanup?.Invoke();
                if (CleanupThrows) throw new InvalidOperationException("SyntheticCleanupFailure");
            }
            // ResetForm이 변수 저장소를 지우지 않는 상태를 의도적으로 유지한다.
        }
        public static int SetPrintFormString(string form)
        { LastForm = form; return Call("SetPrintFormString"); }
        public static int SetPrintFormFile(string path)
        { LastForm = File.ReadAllText(path, Encoding.GetEncoding(949)); return Call("SetPrintFormFile"); }
        public static int SetPrintFormVariableData(string values)
        { Variables = values; return Call("SetPrintFormVariableData"); }
        public static void StartLabelPrint(ushort copies, StringBuilder output)
        {
            if (copies != 1) throw new InvalidOperationException("Only one label was expected.");
            Call("StartLabelPrint"); BeforePrint?.Invoke(); output.Append(Response);
        }
        public static int SendData(string command)
        { Calls.Add(command); int code = Call("SendData"); AfterSendData?.Invoke(); return code; }
    }
}
