using System;
using System.Reflection;
using System.Text;

namespace U2BIO.BT003PROLabelPrinting.Validation
{
    internal static class LabelResponseTests
    {
        private const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Static;
        private static byte[] Buffer(string text)
        {
            var bytes = new byte[42];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = 255;
            byte[] encoded = Encoding.ASCII.GetBytes(text + "\0");
            Array.Copy(encoded, bytes, Math.Min(bytes.Length, encoded.Length));
            return bytes;
        }
        internal static void Run(Assembly library)
        {
            Type type = library.GetType("U2BIO.BT003PROLabelPrinting.Native.NativeLabelResponse", true);
            if ((int)type.GetField("BufferBytes", Flags).GetRawConstantValue() != 42) throw new InvalidOperationException("Response bound differs.");
            MethodInfo parse = type.GetMethod("Parse", Flags);
            foreach (int count in new[] { 0, 4, 7, 8, 10 }) {
                string prefix = count == 0 ? "" : string.Join(":", new string('A', count).ToCharArray()).Replace("A", "00");
                foreach (int code in new[] { 0, 102, 103, int.MinValue, int.MaxValue }) {
                    string text = prefix + "," + code.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    object result = parse.Invoke(null, new object[] { Buffer(text) });
                    if ((int)result.GetType().GetField("NativeCode", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(result) != code ||
                        (int)result.GetType().GetField("Length", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(result) != text.Length)
                        throw new InvalidOperationException("Response metadata differs.");
                }
            }
            parse.Invoke(null, new object[] { Buffer("Unknown,103") });
            foreach (string bad in new[] { "", ",", ",+0", ",-0", ",00", ",2147483648", "Unknown,text", "unknown,0", "00:00:00,0", "zz:00:00:00,0", "00:00:00:00,0\n", "Unknown,0,extra", new string('X', 42) }) {
                bool rejected = false;
                try { parse.Invoke(null, new object[] { Buffer(bad) }); }
                catch (TargetInvocationException error) { rejected = error.InnerException.Message == "PrintResponseInvalid"; }
                if (!rejected) throw new InvalidOperationException("Invalid response accepted.");
            }
            Console.WriteLine("PASS: bounded ASCII label response metadata including42byte worst case; no print success inferred");
        }
    }
}
