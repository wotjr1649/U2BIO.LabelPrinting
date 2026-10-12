using System;
using System.Text;
using System.Net;
using System.Globalization;

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
