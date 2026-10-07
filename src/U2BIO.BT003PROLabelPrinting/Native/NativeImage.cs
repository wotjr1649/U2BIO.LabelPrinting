using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace U2BIO.BT003PROLabelPrinting.Native
{
    // Metadata only: never maps or executes an image. All offsets are bounded by the supplied file.
    internal sealed class NativeImage
    {
        private readonly byte[] bytes;
        private readonly int sectionTable, sections;
        private readonly uint headers, exportRva, exportSize;
        internal ushort Machine { get; }
        internal Dictionary<string, uint> Exports { get; } = new Dictionary<string, uint>(StringComparer.Ordinal);
        internal HashSet<string> Imports { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private NativeImage(byte[] data)
        {
            bytes = data ?? throw new InvalidDataException();
            if (bytes.Length > 16 * 1024 * 1024 || U16(0) != 0x5a4d) throw new InvalidDataException();
            int pe = Offset(U32(60));
            if (U32(pe) != 0x4550) throw new InvalidDataException();
            Machine = U16(pe + 4);
            sections = U16(pe + 6);
            int optionalSize = U16(pe + 20), optional = pe + 24;
            if (sections == 0 || sections > 96 || (U16(pe + 22) & 0x2000) == 0) throw new InvalidDataException();
            bool is64 = Machine == 0x8664;
            if ((!is64 && Machine != 0x14c) || U16(optional) != (is64 ? 0x20b : 0x10b)) throw new InvalidDataException();
            int directoryOffset = is64 ? 112 : 96;
            if (optionalSize < directoryOffset + 16 || optionalSize > 4096) throw new InvalidDataException();
            Range(optional, optionalSize);
            uint directories = U32(optional + directoryOffset - 4);
            if (directories < 2 || directories > 16 || optionalSize < directoryOffset + directories * 8) throw new InvalidDataException();
            headers = U32(optional + 60);
            if (headers == 0 || headers > bytes.Length) throw new InvalidDataException();
            sectionTable = optional + optionalSize;
            Range(sectionTable, sections * 40);
            if (sectionTable + sections * 40 > headers) throw new InvalidDataException();
            for (int i = 0; i < sections; i++) {
                int s = sectionTable + i * 40;
                uint rawSize = U32(s + 16), rawOffset = U32(s + 20);
                if (rawSize != 0) Range(Offset(rawOffset), Offset(rawSize));
                if ((ulong)U32(s + 12) + Math.Max(U32(s + 8), rawSize) > uint.MaxValue) throw new InvalidDataException();
            }
            int directory = optional + directoryOffset;
            exportRva = U32(directory); exportSize = U32(directory + 4);
            if (directories > 13 && U32(directory + 13 * 8) != 0) throw new InvalidDataException("Delay imports are not part of the verified pair.");
            ReadExports();
            ReadImports(U32(directory + 8), U32(directory + 12));
        }

        internal static NativeImage Parse(byte[] bytes) { return new NativeImage(bytes); }
        private int Offset(uint value) { if (value > int.MaxValue) throw new InvalidDataException(); return (int)value; }
        private void Range(int offset, int count) { if (offset < 0 || count < 0 || offset > bytes.Length - count) throw new InvalidDataException(); }
        private ushort U16(int offset) { Range(offset, 2); return BitConverter.ToUInt16(bytes, offset); }
        private uint U32(int offset) { Range(offset, 4); return BitConverter.ToUInt32(bytes, offset); }
        private int Map(uint rva, int count)
        {
            if ((ulong)rva + (uint)count <= headers) { int h = Offset(rva); Range(h, count); return h; }
            int found = -1;
            for (int i = 0; i < sections; i++) {
                int s = sectionTable + i * 40;
                uint start = U32(s + 12), rawSize = U32(s + 16);
                if (rva < start || (ulong)rva - start + (uint)count > rawSize) continue;
                ulong offset = (ulong)U32(s + 20) + rva - start;
                if (offset > int.MaxValue || found != -1) throw new InvalidDataException();
                found = (int)offset;
            }
            if (found == -1) throw new InvalidDataException();
            Range(found, count);
            return found;
        }
        private string Name(uint rva)
        {
            var result = new StringBuilder();
            for (uint i = 0; i < 256; i++) {
                if ((ulong)rva + i > uint.MaxValue) throw new InvalidDataException();
                byte value = bytes[Map(rva + i, 1)];
                if (value == 0) { if (result.Length == 0) throw new InvalidDataException(); return result.ToString(); }
                if (value < 33 || value > 126 || value == '/' || value == '\\' || value == ':') throw new InvalidDataException();
                result.Append((char)value);
            }
            throw new InvalidDataException();
        }
        private void ReadExports()
        {
            if (exportRva == 0 && exportSize == 0) return;
            if (exportRva == 0 || exportSize < 40 || (ulong)exportRva + exportSize > uint.MaxValue) throw new InvalidDataException();
            int table = Map(exportRva, 40);
            uint functions = U32(table + 20), names = U32(table + 24);
            if (functions > 4096 || names > functions) throw new InvalidDataException();
            int addresses = Map(U32(table + 28), checked((int)functions * 4));
            int pointers = Map(U32(table + 32), checked((int)names * 4));
            int ordinals = Map(U32(table + 36), checked((int)names * 2));
            for (int i = 0; i < names; i++) {
                string name = Name(U32(pointers + i * 4));
                ushort ordinal = U16(ordinals + i * 2);
                if (ordinal >= functions || Exports.ContainsKey(name)) throw new InvalidDataException();
                uint address = U32(addresses + ordinal * 4);
                if (address == 0) throw new InvalidDataException();
                Map(address, 1);
                Exports.Add(name, address);
            }
        }
        internal bool IsForwarder(string name)
        {
            uint address = Exports[name];
            return address >= exportRva && (ulong)address < (ulong)exportRva + exportSize;
        }
        private void ReadImports(uint rva, uint size)
        {
            if (rva == 0 && size == 0) return;
            if (rva == 0 || size < 20 || size > 20 * 256) throw new InvalidDataException();
            for (uint offset = 0; offset <= size - 20; offset += 20) {
                if ((ulong)rva + offset > uint.MaxValue) throw new InvalidDataException();
                int descriptor = Map(rva + offset, 20);
                bool empty = true;
                for (int i = 0; i < 20; i++) if (bytes[descriptor + i] != 0) empty = false;
                if (empty) return;
                if (!Imports.Add(Name(U32(descriptor + 12)))) throw new InvalidDataException();
            }
            throw new InvalidDataException();
        }
    }
}
