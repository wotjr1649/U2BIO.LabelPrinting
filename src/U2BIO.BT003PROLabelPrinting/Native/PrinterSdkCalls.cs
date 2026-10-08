using System;

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

        internal PrinterSdkCalls(Action load, Func<ushort, string, string, int> setPort, Func<int> connect,
            Func<int> status, Func<int> disconnect, Action reset, Func<string, int> form, Action<ushort, IntPtr> print)
        { Load = load; SetPort = setPort; Connect = connect; Status = status; Disconnect = disconnect; Reset = reset; Form = form; Print = print; }

        internal static PrinterSdkCalls Create() => new PrinterSdkCalls(NativeSdk.LoadForConnection,
            (type, port, ip) => NativeSdk.GetConnectionFunction<NativeApi.SetPrinterPortDelegate>()(type, port, ip),
            () => NativeSdk.GetConnectionFunction<NativeApi.DeviceConnectDelegate>()(),
            () => NativeSdk.GetConnectionFunction<NativeApi.CheckPrtConnectionDelegate>()(),
            () => NativeSdk.GetConnectionFunction<NativeApi.DeviceDisconnectDelegate>()(),
            () => NativeSdk.GetPrintingFunction<NativeApi.ResetFormDelegate>()(),
            form => NativeSdk.GetPrintingFunction<NativeApi.SetPrintFormStringDelegate>()(form),
            (copies, response) => NativeSdk.GetPrintingFunction<NativeApi.StartLabelPrintDelegate>()(copies, response));
    }
}
