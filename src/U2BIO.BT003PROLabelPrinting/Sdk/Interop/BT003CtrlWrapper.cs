using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

namespace HFSample
{
    internal static class BT003CtrlWrapper
    {
        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern void GetUidValue([MarshalAs(UnmanagedType.LPStr, SizeConst = 100)] StringBuilder uid);

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int SetRfidProtocol(ushort iProtocol);

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int SetRfidPort(ushort ifType, string strPortName, string strIpAddr);

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int SetPrinterPort(ushort ifType, string strPortName, string strIpAddr);

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int SetPrintFormFile(string strFormFile);

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int SetPrintFormString(string strFormData);

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int SetPrintFormVariableData(string strVariableData);

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern void ResetForm();

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int SendData(string strData);

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int SetWritingDataType(ushort iType);

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int AddNfcUriRecord(string strUri);

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int AddNfcTextRecord(string strText, string strLocale);

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int AddNfcAARecord(string strPkgName);

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int AddNfcExternalRecord(string strTypeName, string strBinPayload);

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int AddNfcMimeRecord(string strTypeName, string strHexData);

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int AddNfcSmartPosterRecord(string strUri, string strText, string strLocale);

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int AddNfcUserDefinedRecord(byte header, string strTypeName, int iTypeLen, string strId, int iIdLen, string strHexData);

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int AddBlockData(ushort iStartBlockNo, string strHexData);

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int ClearNfcRecord();

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int SetTagBlockData(ushort iStartBlockNo, string strHexData);

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int ClearBlockData();

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int SetUseResponseBuffer(ushort iUseBuffer);

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int IsUsableResponseBuffer();

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int SetRfidCondition(ushort iCardType, ushort iLockMode, ushort iVerify, ushort iRetry, ushort iOperationSafeMode, ushort iProtection);

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int SetLabelSetting(ushort iLabelWidth, ushort iLabelHeight, ushort iGap, ushort iType);

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int SetULEV1_NTAG21x_Security(string strOldPwd, string strNewPwd, string strPACK, ushort blockRange, ushort protectionMode);

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int SetULCSecurity(string strOldPWD, string strNewPWD, ushort blockRange, ushort projectionMode);

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int SetPrintingOption(ushort iSpeed, ushort iDarkness);

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int SetAfiOption(ushort mode, byte afi, ushort bProtect, ushort bLock);

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int SetDsfidOption(ushort mode, byte dsfid, ushort bLock);

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int SetIcodePassword(ushort pwdId, string strOperPassword, string strNewPassword);

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int SetIcodeEasOption(ushort mode, ushort type, ushort bProtect, ushort bLock);

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int SetIcodePasswordWritingOption(ushort pwdOption, ushort lockOption);

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern void StartLabelPrint(ushort iCopies, [MarshalAs(UnmanagedType.LPStr, SizeConst = 100)] StringBuilder strReturn);

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern void StartPrintWithRfid([MarshalAs(UnmanagedType.LPStr, SizeConst = 100)] StringBuilder strReturn);

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CheckPrtConnection();

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int CheckRFConnection();

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int DeviceConnect();

        [DllImport("BlackfishNfcLib.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern int DeviceDisconnect();

        static BT003CtrlWrapper()
        {
        }
    }
}
