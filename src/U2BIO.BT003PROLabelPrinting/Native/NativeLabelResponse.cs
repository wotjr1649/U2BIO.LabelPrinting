using System;
using System.Globalization;
using System.Text;

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

        internal static NativeLabelResponse Parse(byte[] bytes)
        {
            if (bytes == null || bytes.Length != BufferBytes) throw new NativeSdkException("PrintResponseInvalid");
            int length = Array.IndexOf(bytes, (byte)0);
            if (length < 0) throw new NativeSdkException("PrintResponseInvalid");
            for (int i = 0; i < length; i++)
                if (bytes[i] < 32 || bytes[i] > 126) throw new NativeSdkException("PrintResponseInvalid");
            string text = Encoding.ASCII.GetString(bytes, 0, length);
            int comma = text.IndexOf(',');
            int code;
            if (comma < 0 || !int.TryParse(text.Substring(comma + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out code) ||
                code.ToString(CultureInfo.InvariantCulture) != text.Substring(comma + 1)) throw new NativeSdkException("PrintResponseInvalid");
            string prefix = text.Substring(0, comma), kind = "empty";
            if (prefix == "Unknown") kind = "unknown";
            else if (prefix.Length != 0) {
                int count = (prefix.Length + 1) / 3;
                if (prefix.Length != count * 3 - 1 || (count != 4 && count != 7 && count != 8 && count != 10))
                    throw new NativeSdkException("PrintResponseInvalid");
                for (int i = 0; i < prefix.Length; i++) {
                    char value = prefix[i];
                    if (i % 3 == 2 ? value != ':' : !((value >= '0' && value <= '9') || (value >= 'A' && value <= 'F')))
                        throw new NativeSdkException("PrintResponseInvalid");
                }
                kind = "uid";
            }
            return new NativeLabelResponse(code, length, kind);
        }
    }
}
