using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace U2BIO.BT003PROLabelPrinting
{
    internal sealed class BtplFileOrigin
    {
        internal readonly string Path;
        internal readonly byte[] Hash;
        internal BtplFileOrigin(string path, byte[] bytes) { Path = path; Hash = BtplFileStore.Hash(bytes); }
    }
    internal static class BtplFileStore
    {
        internal static byte[] Hash(byte[] bytes) { using (var sha = SHA256.Create()) return sha.ComputeHash(bytes); }
        internal static Encoding StrictEncoding(int codePage) => Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        internal static string Read(string filePath, int codePage, out BtplFileOrigin origin)
        {
            string path = System.IO.Path.GetFullPath(filePath);
            byte[] bytes = File.ReadAllBytes(path);
            origin = new BtplFileOrigin(path, bytes);
            var encoding = StrictEncoding(codePage);
            int offset = 0;
            foreach (int bomPage in new[] { 12000, 12001, 65001, 1200, 1201 }) {
                byte[] bom = Encoding.GetEncoding(bomPage).GetPreamble();
                if (bom.Length > 0 && bytes.Take(bom.Length).SequenceEqual(bom)) {
                    if (bomPage != codePage) throw new BtplFormatException("InvalidEncoding");
                    offset = bom.Length; break;
                }
            }
            try { return encoding.GetString(bytes, offset, bytes.Length - offset); }
            catch (DecoderFallbackException) { throw new BtplFormatException("InvalidEncoding"); }
        }
        internal static BtplFileOrigin Save(string filePath, string text, int codePage, BtplFileOrigin origin)
        {
            byte[] bytes;
            try { bytes = StrictEncoding(codePage).GetBytes(text); }
            catch (EncoderFallbackException) { throw new BtplFormatException("InvalidEncoding"); }
            string path = System.IO.Path.GetFullPath(filePath);
            string temporary = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), ".btpl-" + Guid.NewGuid().ToString("N") + ".tmp");
            bool created = false;
            try {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                    created = true; file.Write(bytes, 0, bytes.Length); file.Flush(true);
                }
                if (origin != null && string.Equals(path, origin.Path, StringComparison.OrdinalIgnoreCase)) {
                    byte[] current;
                    try { current = File.ReadAllBytes(path); }
                    catch (FileNotFoundException) { throw new BtplSaveConflictException(); }
                    catch (DirectoryNotFoundException) { throw new BtplSaveConflictException(); }
                    if (!Hash(current).SequenceEqual(origin.Hash)) throw new BtplSaveConflictException();
                }
                if (File.Exists(path)) File.Replace(temporary, path, null, false);
                else File.Move(temporary, path);
                created = false;
                return new BtplFileOrigin(path, bytes);
            }
            finally { if (created) File.Delete(temporary); }
        }
    }
}
