using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Globalization;
using System.Net;
using System.Text;

namespace U2BIO.BT003PROLabelPrinting.Native
{
    // Header/sample prototypes only. Output pointers have no verified capacity or response contract.
    internal static class NativeApi
    {
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate void GetUidValueDelegate([Out] IntPtr lptUid);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int SetRfidProtocolDelegate([MarshalAs(UnmanagedType.U2)] ushort iProtocol);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int SetRfidPortDelegate([MarshalAs(UnmanagedType.U2)] ushort ifType, [In, MarshalAs(UnmanagedType.LPStr)] string strPortName, [In, MarshalAs(UnmanagedType.LPStr)] string strIpAddr);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int SetPrinterPortDelegate([MarshalAs(UnmanagedType.U2)] ushort ifType, [In, MarshalAs(UnmanagedType.LPStr)] string strPortName, [In, MarshalAs(UnmanagedType.LPStr)] string strIpAddr);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int SetPrintFormFileDelegate([In, MarshalAs(UnmanagedType.LPStr)] string strFormFile);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int SetPrintFormStringDelegate([In, MarshalAs(UnmanagedType.LPStr)] string strFormData);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int SetPrintFormVariableDataDelegate([In, MarshalAs(UnmanagedType.LPStr)] string strVariableData);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate void ResetFormDelegate();

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int SendDataDelegate([In, MarshalAs(UnmanagedType.LPStr)] string strData);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int SetWritingDataTypeDelegate([MarshalAs(UnmanagedType.U2)] ushort iType);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int AddNfcUriRecordDelegate([In, MarshalAs(UnmanagedType.LPStr)] string strUri);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int AddNfcTextRecordDelegate([In, MarshalAs(UnmanagedType.LPStr)] string strText, [In, MarshalAs(UnmanagedType.LPStr)] string strLocale);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int AddNfcAARecordDelegate([In, MarshalAs(UnmanagedType.LPStr)] string strPkgName);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int AddNfcExternalRecordDelegate([In, MarshalAs(UnmanagedType.LPStr)] string strTypeName, [In, MarshalAs(UnmanagedType.LPStr)] string strBinPayload);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int AddNfcMimeRecordDelegate([In, MarshalAs(UnmanagedType.LPStr)] string strTypeName, [In, MarshalAs(UnmanagedType.LPStr)] string strHexData);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int AddNfcSmartPosterRecordDelegate([In, MarshalAs(UnmanagedType.LPStr)] string strUri, [In, MarshalAs(UnmanagedType.LPStr)] string strText, [In, MarshalAs(UnmanagedType.LPStr)] string strLocale);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int AddNfcUserDefinedRecordDelegate([MarshalAs(UnmanagedType.U1)] byte header, [In, MarshalAs(UnmanagedType.LPStr)] string typeName, int typeLen, [In, MarshalAs(UnmanagedType.LPStr)] string id, int idLen, [In, MarshalAs(UnmanagedType.LPStr)] string strHexData);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int AddBlockDataDelegate([MarshalAs(UnmanagedType.U2)] ushort iStartBlockNo, [In, MarshalAs(UnmanagedType.LPStr)] string strHexData);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int ClearNfcRecordDelegate();

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int SetTagBlockDataDelegate([MarshalAs(UnmanagedType.U2)] ushort iStartBlockNo, [In, MarshalAs(UnmanagedType.LPStr)] string strHexData);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int ClearBlockDataDelegate();

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int ClearMFClassicSecurityDelegate();

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int SetUseResponseBufferDelegate([MarshalAs(UnmanagedType.U2)] ushort iUseBuffer);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int IsUsableResponseBufferDelegate();

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int SetRfidConditionDelegate([MarshalAs(UnmanagedType.U2)] ushort iCardType, [MarshalAs(UnmanagedType.U2)] ushort iLockMode, [MarshalAs(UnmanagedType.U2)] ushort iVerify, [MarshalAs(UnmanagedType.U2)] ushort iRetry, [MarshalAs(UnmanagedType.U2)] ushort iOperationSafeMode, [MarshalAs(UnmanagedType.U2)] ushort iProtection);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int SetLabelSettingDelegate([MarshalAs(UnmanagedType.U2)] ushort iLabelWidth, [MarshalAs(UnmanagedType.U2)] ushort iLabelHeight, [MarshalAs(UnmanagedType.U2)] ushort iGap, [MarshalAs(UnmanagedType.U2)] ushort iType);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int SetULEV1_NTAG21x_SecurityDelegate([In, MarshalAs(UnmanagedType.LPStr)] string _strOldPwd, [In, MarshalAs(UnmanagedType.LPStr)] string _strNewPwd, [In, MarshalAs(UnmanagedType.LPStr)] string _strPack, [MarshalAs(UnmanagedType.U2)] ushort _blockRange, [MarshalAs(UnmanagedType.U2)] ushort _protectionMode);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int SetMFClassicSectorSecurityDelegate([MarshalAs(UnmanagedType.U2)] ushort iSectorNum, [In, MarshalAs(UnmanagedType.LPStr)] string authKey, [MarshalAs(UnmanagedType.U2)] ushort authKeyType);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int SetULCSecurityDelegate([In, MarshalAs(UnmanagedType.LPStr)] string _strOldPWD, [In, MarshalAs(UnmanagedType.LPStr)] string _strNewPWD, [MarshalAs(UnmanagedType.U2)] ushort blockRange, [MarshalAs(UnmanagedType.U2)] ushort protectionMode);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int SetPrintingOptionDelegate([MarshalAs(UnmanagedType.U2)] ushort iSpeed, [MarshalAs(UnmanagedType.U2)] ushort iDarkness);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int SetAfiOptionDelegate([MarshalAs(UnmanagedType.U2)] ushort mode, [MarshalAs(UnmanagedType.U1)] byte afi, [MarshalAs(UnmanagedType.U2)] ushort bProtect, [MarshalAs(UnmanagedType.U2)] ushort bLock);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int SetDsfidOptionDelegate([MarshalAs(UnmanagedType.U2)] ushort mode, [MarshalAs(UnmanagedType.U1)] byte dsfid, [MarshalAs(UnmanagedType.U2)] ushort bLock);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int SetIcodePasswordDelegate([MarshalAs(UnmanagedType.U2)] ushort pwdId, [In, MarshalAs(UnmanagedType.LPStr)] string strOperPassword, [In, MarshalAs(UnmanagedType.LPStr)] string strNewPassword);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int SetIcodeEasOptionDelegate([MarshalAs(UnmanagedType.U2)] ushort mode, [MarshalAs(UnmanagedType.U2)] ushort type, [MarshalAs(UnmanagedType.U2)] ushort bProtect, [MarshalAs(UnmanagedType.U2)] ushort bLock);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int SetIcodePasswordWritingOptionDelegate([MarshalAs(UnmanagedType.U2)] ushort pwdOption, [MarshalAs(UnmanagedType.U2)] ushort lockOption);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate void StartPrintWithRfidDelegate([Out] IntPtr _strReturn);

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int CheckPrtConnectionDelegate();

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int CheckRFConnectionDelegate();

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int DeviceConnectDelegate();

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate int DeviceDisconnectDelegate();

        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        internal delegate void StartLabelPrintDelegate([MarshalAs(UnmanagedType.U2)] ushort iCopies, [Out] IntPtr strReturn);

        internal static Type[] Types()
        {
            var result = new List<Type>();
            foreach (Type type in typeof(NativeApi).GetNestedTypes(BindingFlags.NonPublic))
                if (typeof(MulticastDelegate).IsAssignableFrom(type)) result.Add(type);
            result.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return result.ToArray();
        }
        internal static string ExportName(Type type, bool is64)
        {
            if (type == null || type.DeclaringType != typeof(NativeApi) || !typeof(MulticastDelegate).IsAssignableFrom(type) ||
                !type.Name.EndsWith("Delegate", StringComparison.Ordinal)) throw new ArgumentException("Unsupported ABI type.");
            string name = type.Name.Substring(0, type.Name.Length - "Delegate".Length);
            if (is64) return name;
            int bytes = 0;
            foreach (var parameter in type.GetMethod("Invoke").GetParameters()) {
                Type argument = parameter.ParameterType;
                if (argument != typeof(byte) && argument != typeof(ushort) && argument != typeof(int) && argument != typeof(string) && argument != typeof(IntPtr))
                    throw new ArgumentException("Unsupported ABI type.");
                bytes += 4;
            }
            return "_" + name + "@" + bytes.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}

namespace U2BIO.BT003PROLabelPrinting.Native
{
    internal sealed class PrinterSdkCalls
    {
        internal readonly Action Load;
        internal readonly Func<ushort, string, string, int> SetPort;
        internal readonly Func<int> Connect, Status, Disconnect;
        internal readonly Action Reset;
        internal readonly Func<string, int> Form;
        internal readonly Action<ushort, IntPtr> Print;
        internal readonly Func<int, bool, int> Move;
        internal Func<string, int> FileForm = value => { throw new NativeSdkException("SdkFunctionNotAvailable"); };
        internal Func<string, int> Variables = value => { throw new NativeSdkException("SdkFunctionNotAvailable"); };
        internal Func<ushort, ushort, int> Options = (speed, darkness) => { throw new NativeSdkException("SdkFunctionNotAvailable"); };

        internal PrinterSdkCalls(Action load, Func<ushort, string, string, int> setPort, Func<int> connect,
            Func<int> status, Func<int> disconnect, Action reset, Func<string, int> form, Action<ushort, IntPtr> print, Func<int, bool, int> move)
        { Load = load; SetPort = setPort; Connect = connect; Status = status; Disconnect = disconnect; Reset = reset; Form = form; Print = print; Move = move; }

        internal static PrinterSdkCalls Create() => new PrinterSdkCalls(NativeSdk.LoadForConnection,
            BT003CtrlWrapper.SetPrinterPort, BT003CtrlWrapper.DeviceConnect, BT003CtrlWrapper.CheckPrtConnection,
            BT003CtrlWrapper.DeviceDisconnect, BT003CtrlWrapper.ResetForm, BT003CtrlWrapper.SetPrintFormString,
            BT003CtrlWrapper.StartLabelPrint, BT003CtrlWrapper.SendData) {
                FileForm = BT003CtrlWrapper.SetPrintFormFile,
                Variables = BT003CtrlWrapper.SetPrintFormVariableData,
                Options = BT003CtrlWrapper.SetPrintingOption
            };
    }

    // Manufacturer Wrapper API names; retain the existing verified module and response-pointer boundary.
    internal static class BT003CtrlWrapper
    {
        internal static int SetPrinterPort(ushort type, string port, string ip) => NativeSdk.GetConnectionFunction<NativeApi.SetPrinterPortDelegate>()(type, port, ip);
        internal static int DeviceConnect() => NativeSdk.GetConnectionFunction<NativeApi.DeviceConnectDelegate>()();
        internal static int CheckPrtConnection() => NativeSdk.GetConnectionFunction<NativeApi.CheckPrtConnectionDelegate>()();
        internal static int DeviceDisconnect() => NativeSdk.GetConnectionFunction<NativeApi.DeviceDisconnectDelegate>()();
        internal static void ResetForm() => NativeSdk.GetPrintingFunction<NativeApi.ResetFormDelegate>()();
        internal static int SetPrintFormFile(string path) => NativeSdk.GetPrintingFunction<NativeApi.SetPrintFormFileDelegate>()(path);
        internal static int SetPrintFormString(string form) => NativeSdk.GetPrintingFunction<NativeApi.SetPrintFormStringDelegate>()(form);
        internal static int SetPrintFormVariableData(string values) => NativeSdk.GetPrintingFunction<NativeApi.SetPrintFormVariableDataDelegate>()(values);
        internal static int SetPrintingOption(ushort speed, ushort darkness) => NativeSdk.GetPrintingFunction<NativeApi.SetPrintingOptionDelegate>()(speed, darkness);
        internal static void StartLabelPrint(ushort copies, IntPtr response) => NativeSdk.GetPrintingFunction<NativeApi.StartLabelPrintDelegate>()(copies, response);
        internal static int SendData(int millimeters, bool forward) => NativeSdk.MovePaper(millimeters, forward);
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
