using System;
using System.Globalization;
using System.Text;
using System.Runtime.InteropServices;

namespace U2BIO.BT003PROLabelPrinting.Native
{
    // Metadata only for the fixed SDK pair; never retain the UID or infer physical completion.
    internal sealed class NativeLabelResponse
    {
        internal const int BufferBytes = 42;
        internal readonly int NativeCode, Length;
        internal readonly string PrefixKind;
        private NativeLabelResponse(int code, int length, string kind)
        { NativeCode = code; Length = length; PrefixKind = kind; }

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint allocation, uint protection);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint protection, out uint previous);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool VirtualFree(IntPtr address, UIntPtr size, uint operation);

        // Fixed SDK normal response only. Overflow hits PAGE_NOACCESS; this does not isolate the caller from a native crash.
        internal static NativeLabelResponse Capture(Action<IntPtr> call)
        {
            if (call == null) throw new ArgumentNullException(nameof(call));
            int page = Environment.SystemPageSize;
            IntPtr allocation = VirtualAlloc(IntPtr.Zero, new UIntPtr(checked((uint)page * 2)), 0x3000, 0x04);
            if (allocation == IntPtr.Zero) throw new NativeSdkException("PrintBufferUnavailable", Marshal.GetLastWin32Error());
            var bytes = new byte[BufferBytes];
            IntPtr pointer = IntPtr.Add(allocation, page - BufferBytes);
            try {
                uint previous;
                if (!VirtualProtect(IntPtr.Add(allocation, page), new UIntPtr((uint)page), 0x01, out previous))
                    throw new NativeSdkException("PrintBufferUnavailable", Marshal.GetLastWin32Error());
                for (int i = 0; i < bytes.Length; i++) bytes[i] = 255;
                Marshal.Copy(bytes, 0, pointer, bytes.Length);
                call(pointer);
                Marshal.Copy(pointer, bytes, 0, bytes.Length);
                return Parse(bytes);
            }
            finally {
                Array.Clear(bytes, 0, bytes.Length);
                Marshal.Copy(bytes, 0, pointer, bytes.Length);
                if (!VirtualFree(allocation, UIntPtr.Zero, 0x8000)) throw new NativeSdkException("PrintBufferReleaseFailed", Marshal.GetLastWin32Error());
            }
        }

        internal static NativeLabelResponse Parse(byte[] bytes)
        {
            if (bytes == null || bytes.Length != BufferBytes) throw new NativeSdkException("PrintResponseInvalid");
            int length = Array.IndexOf(bytes, (byte)0);
            if (length < 0) throw new NativeSdkException("PrintResponseInvalid");
            for (int i = 0; i < length; i++)
                if (bytes[i] < 32 || bytes[i] > 126) throw new NativeSdkException("PrintResponseInvalid");
            int comma = Array.IndexOf(bytes, (byte)',', 0, length);
            if (comma < 0) throw new NativeSdkException("PrintResponseInvalid");
            string kind = "empty";
            bool unknown = comma == 7;
            for (int i = 0; unknown && i < 7; i++) unknown = bytes[i] == "Unknown"[i];
            if (unknown) kind = "unknown";
            else if (comma != 0) {
                int count = (comma + 1) / 3;
                if (comma != count * 3 - 1 || (count != 4 && count != 7 && count != 8 && count != 10))
                    throw new NativeSdkException("PrintResponseInvalid");
                for (int i = 0; i < comma; i++) {
                    byte value = bytes[i];
                    if (i % 3 == 2 ? value != ':' : !((value >= '0' && value <= '9') || (value >= 'A' && value <= 'F')))
                        throw new NativeSdkException("PrintResponseInvalid");
                }
                kind = "uid";
            }
            // Only the validated numeric code becomes a string; UID bytes never do.
            for (int i = comma + 1; i < length; i++)
                if (!((bytes[i] >= '0' && bytes[i] <= '9') || (i == comma + 1 && bytes[i] == '-')))
                    throw new NativeSdkException("PrintResponseInvalid");
            string number = Encoding.ASCII.GetString(bytes, comma + 1, length - comma - 1);
            int code;
            if (!int.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out code) ||
                code.ToString(CultureInfo.InvariantCulture) != number) throw new NativeSdkException("PrintResponseInvalid");
            return new NativeLabelResponse(code, length, kind);
        }
    }
}
