using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace U2BIO.BT003PROLabelPrinting.Native
{
    internal sealed class NativeSdkException : Exception
    {
        internal string Code { get; }
        internal int? SystemErrorCode { get; }
        internal string Function { get; }
        internal NativeSdkException(string code, int? systemError = null, string function = null) : base(code)
        { Code = code; SystemErrorCode = systemError; Function = function; }
    }

    // Prepare the pinned SDK pair; manufacturer P/Invoke declarations own the normal calls.
    internal static class SdkRuntime
    {
        private const uint SearchFlags = 0x100 | 0x800;
        private static readonly object Sync = new object();
        private static readonly string[] Files = { "BlackfishNfcLib.dll", "HFModule.dll" };
        private static readonly string[][] Hashes = {
            new[] { "fed09bae47835dcf78f58579e3cfb9382561799b369e54fd9fa0902e5b89dbf5", "bb2721801bc853ead75e647608c6b36a5a3a75f344095e4e29ffc35e822319d4" },
            new[] { "64c7874980cb0a774a766e299359bc9cba5dd66fd052d758f1d4d1678d8cf72c", "542ebfbccca1f953b4dcafb2a1faed198707d8fbcdb0653c0fc121d3c273edf5" }
        };
        private static readonly HashSet<string> SystemImports = new HashSet<string>(new[] {
            "SETUPAPI.dll", "KERNEL32.dll", "USER32.dll", "GDI32.dll", "MSIMG32.dll", "WINSPOOL.DRV", "ADVAPI32.dll",
            "SHELL32.dll", "SHLWAPI.dll", "UxTheme.dll", "ole32.dll", "OLEAUT32.dll", "oledlg.dll", "CRYPT32.dll",
            "WS2_32.dll", "OLEACC.dll", "gdiplus.dll", "IMM32.dll", "WINMM.dll"
        }, StringComparer.OrdinalIgnoreCase);
        private static IntPtr blackfish, hfModule;
        private static StartLabelDelegate print;
        private static bool loadAttempted;
        private static NativeSdkException loadFailure;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr LoadLibraryExW(string name, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, [MarshalAs(UnmanagedType.LPStr)] string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern IntPtr GetModuleHandleW(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern uint GetModuleFileNameW(IntPtr module, StringBuilder name, uint size);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, StringBuilder name, uint size, uint flags);

        // shortcut: retain the guarded output pointer until StringBuilder has an equivalent output boundary.
        [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        private delegate void StartLabelDelegate([MarshalAs(UnmanagedType.U2)] ushort copies, [Out] IntPtr response);
        internal static void StartLabelPrint(ushort copies, IntPtr response)
        {
            StartLabelDelegate call;
            lock (Sync) {
                if (print == null) throw new NativeSdkException("NativeLibraryMissing");
                call = print;
            }
            call(copies, response);
        }

        private static ushort ReadMachine(byte[] bytes)
        {
            if (bytes.Length < 64 || BitConverter.ToUInt16(bytes, 0) != 0x5a4d) throw new InvalidDataException();
            uint offset = BitConverter.ToUInt32(bytes, 60);
            if (offset > bytes.Length - 24 || BitConverter.ToUInt32(bytes, (int)offset) != 0x4550) throw new InvalidDataException();
            return BitConverter.ToUInt16(bytes, (int)offset + 4);
        }

        private sealed class Pair : IDisposable
        {
            internal readonly FileStream[] Streams = new FileStream[2];
            internal readonly string[] Paths = new string[2];
            internal IDisposable Directories;
            public void Dispose() {
                foreach (var stream in Streams) if (stream != null) stream.Dispose();
                if (Directories != null) Directories.Dispose();
            }
        }

        internal static string FinalFilePath(SafeFileHandle file)
        {
            var buffer = new StringBuilder(32768);
            uint length = GetFinalPathNameByHandleW(file, buffer, (uint)buffer.Capacity, 0);
            if (length == 0 || length >= buffer.Capacity) throw new NativeSdkException("NativeModuleConflict", Marshal.GetLastWin32Error(), "GetFinalPathNameByHandleW");
            string path = buffer.ToString();
            if (!path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.Length < 7 || path[5] != ':') throw new NativeSdkException("NativeModuleConflict");
            return Path.GetFullPath(path.Substring(4));
        }
        private sealed class DirectoryLease : IDisposable
        {
            internal readonly List<SafeFileHandle> Handles = new List<SafeFileHandle>();
            public void Dispose() { foreach (var handle in Handles) handle.Dispose(); }
        }
        internal static IDisposable LeaseDirectoryTree(string directory)
        {
            var lease = new DirectoryLease();
            try {
                string current = Path.GetFullPath(directory);
                if (current.Length < 3 || current[1] != ':' || current[2] != '\\') throw new NativeSdkException("NativeModuleConflict");
                while (!string.IsNullOrEmpty(current)) {
                    NoReparse(current);
                    // List/read access participates in Windows sharing checks; attributes-only does not block rename.
                    var handle = CreateFileW(current, 0x1 | 0x80, 0x1 | 0x2, IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero);
                    lease.Handles.Add(handle);
                    if (handle.IsInvalid) throw new NativeSdkException("NativeLoadFailed", Marshal.GetLastWin32Error(), "CreateFileW");
                    if (!string.Equals(FinalFilePath(handle).TrimEnd('\\'), current.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) throw new NativeSdkException("NativeModuleConflict");
                    current = Path.GetDirectoryName(current);
                }
                return lease;
            }
            catch { lease.Dispose(); throw; }
        }

        private static void NoReparse(string path)
        {
            string current = Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(current)) {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new NativeSdkException("NativeModuleConflict");
                current = Path.GetDirectoryName(current);
            }
        }
        private static Pair Inspect(string directory, bool is64)
        {
            var pair = new Pair();
            try {
                pair.Directories = LeaseDirectoryTree(directory);
                for (int i = 0; i < Files.Length; i++) {
                    pair.Paths[i] = Path.GetFullPath(Path.Combine(directory, Files[i]));
                    NoReparse(pair.Paths[i]);
                    var stream = new FileStream(pair.Paths[i], FileMode.Open, FileAccess.Read, FileShare.Read);
                    pair.Streams[i] = stream;
                    string final = FinalFilePath(stream.SafeFileHandle);
                    if (!string.Equals(final, pair.Paths[i], StringComparison.OrdinalIgnoreCase)) throw new NativeSdkException("NativeModuleConflict");
                    pair.Paths[i] = final;
                    if (stream.Length > 16 * 1024 * 1024) throw new NativeSdkException("NativeArchitectureMismatch");
                    byte[] data = new byte[checked((int)stream.Length)];
                    int read = 0;
                    while (read < data.Length) { int count = stream.Read(data, read, data.Length - read); if (count == 0) throw new IOException(); read += count; }
                    if (ReadMachine(data) != (is64 ? 0x8664 : 0x14c)) throw new NativeSdkException("NativeArchitectureMismatch");
                    using (var sha = SHA256.Create())
                        if (BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant() != Hashes[is64 ? 1 : 0][i])
                            throw new NativeSdkException("NativeVersionMismatch");
                }
                // Exact hashes pin the previously inspected imports/exports/PE format without a general PE parser.
                return pair;
            }
            catch (Exception error) when (error is IOException || error is InvalidDataException || error is UnauthorizedAccessException || error is SecurityException || error is ArgumentException ||
                error is NativeSdkException || error is DllNotFoundException || error is BadImageFormatException || error is EntryPointNotFoundException) {
                pair.Dispose();
                var known = error as NativeSdkException;
                if (known != null) throw;
                if (error is InvalidDataException) throw new NativeSdkException("NativeArchitectureMismatch");
                if (error is FileNotFoundException || error is DirectoryNotFoundException) throw new NativeSdkException("NativeLibraryMissing");
                throw ClassifyLoaderException(error);
            }
            catch { pair.Dispose(); throw; }
        }
        internal static int InspectPair(string directory, bool is64)
        {
            using (var pair = Inspect(directory, is64)) return Files.Length;
        }

        private static string ModulePath(IntPtr module)
        {
            var buffer = new StringBuilder(32768);
            uint length = GetModuleFileNameW(module, buffer, (uint)buffer.Capacity);
            if (length == 0 || length >= buffer.Capacity) throw new NativeSdkException("NativeModuleConflict", Marshal.GetLastWin32Error(), "GetModuleFileNameW");
            string path = Path.GetFullPath(buffer.ToString());
            NoReparse(path);
            return path;
        }
        private static void CheckExisting(Pair pair, string phase)
        {
            using (var process = Process.GetCurrentProcess())
                foreach (ProcessModule module in process.Modules) {
                    string name = module.ModuleName;
                    foreach (string file in Files)
                        if (string.Equals(name, file, StringComparison.OrdinalIgnoreCase) &&
                            module.BaseAddress != (file == Files[0] ? blackfish : hfModule)) throw new NativeSdkException("NativeModuleConflict", null, phase + ":vendor-module:" + file.ToLowerInvariant() + ":other");
                    if (SystemImports.Contains(name)) {
                        try { CheckSystemModulePath(module.FileName); }
                        catch (NativeSdkException error) when (error.Code == "NativeModuleConflict") {
                            throw new NativeSdkException(error.Code, error.SystemErrorCode, phase + ":system-import:" + name.ToLowerInvariant() + ":" + SystemModuleLocation(module.FileName));
                        }
                    }
                }
            foreach (string import in SystemImports)
                if (File.Exists(Path.Combine(Path.GetDirectoryName(pair.Paths[0]), import))) throw new NativeSdkException("NativeModuleConflict", null, phase + ":app-local-system-import:" + import.ToLowerInvariant() + ":other");
        }
        // Diagnostic category only; this never authorizes a module or changes CheckSystemModulePath.
        internal static string SystemModuleLocation(string path)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (string.Equals(directory, Environment.SystemDirectory, StringComparison.OrdinalIgnoreCase)) return "system";
            string sideBySide = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "WinSxS") + Path.DirectorySeparatorChar;
            return directory.StartsWith(sideBySide, StringComparison.OrdinalIgnoreCase) ? "sideBySide" : "other";
        }
        internal static void CheckSystemModulePath(string path)
        {
            string systemFile = Path.GetFullPath(Path.Combine(Environment.SystemDirectory, Path.GetFileName(path)));
            NoReparse(systemFile);
            using (var file = File.OpenRead(systemFile)) {
                string actual = FinalFilePath(file.SafeFileHandle);
                string candidate = Path.GetFullPath(path);
                if (string.Equals(candidate, systemFile, StringComparison.OrdinalIgnoreCase) || string.Equals(candidate, actual, StringComparison.OrdinalIgnoreCase)) {
                    NoReparse(candidate); return;
                }
                string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                string wowFile = Path.Combine(windows, "SysWOW64", Path.GetFileName(systemFile));
                if (!string.Equals(actual, systemFile, StringComparison.OrdinalIgnoreCase) &&
                    !(Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess && string.Equals(actual, wowFile, StringComparison.OrdinalIgnoreCase)))
                    throw new NativeSdkException("NativeModuleConflict");
                string sxsRoot = Path.Combine(windows, "WinSxS") + Path.DirectorySeparatorChar;
                if (!string.Equals(Path.GetFileName(candidate), "gdiplus.dll", StringComparison.OrdinalIgnoreCase) ||
                    !candidate.StartsWith(sxsRoot, StringComparison.OrdinalIgnoreCase)) throw new NativeSdkException("NativeModuleConflict");
                NoReparse(actual);
                try {
                    NoReparse(candidate);
                    using (var sideBySide = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.Read)) {
                        string final = FinalFilePath(sideBySide.SafeFileHandle);
                        if (!string.Equals(final, candidate, StringComparison.OrdinalIgnoreCase) || !final.StartsWith(sxsRoot, StringComparison.OrdinalIgnoreCase) ||
                            !SameFileContent(file, sideBySide)) throw new NativeSdkException("NativeModuleConflict");
                    }
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is SecurityException) {
                    throw new NativeSdkException("NativeModuleConflict", ClassifyLoaderException(error).SystemErrorCode);
                }
            }
        }
        internal static bool SameFileContent(Stream reference, Stream candidate)
        {
            if (!reference.CanRead || !candidate.CanRead || !reference.CanSeek || !candidate.CanSeek ||
                reference.Length < 1 || reference.Length > 16 * 1024 * 1024 || reference.Length != candidate.Length) return false;
            reference.Position = candidate.Position = 0;
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(reference)) == BitConverter.ToString(sha.ComputeHash(candidate));
        }
        internal static NativeSdkException ClassifyLoaderException(Exception error)
        {
            var known = error as NativeSdkException;
            if (known != null) return known;
            var win32 = error as Win32Exception;
            int? system = win32 != null ? (int?)win32.NativeErrorCode :
                ((uint)error.HResult >> 16 == 0x8007 ? (int?)(error.HResult & 0xffff) : null);
            return new NativeSdkException(error is BadImageFormatException ? "NativeArchitectureMismatch" :
                error is EntryPointNotFoundException ? "UnsupportedLoaderPlatform" : "NativeLoadFailed", system);
        }
        internal static void LoadForConnection()
        {
            lock (Sync) {
                if (loadFailure != null) throw new NativeSdkException(loadFailure.Code, loadFailure.SystemErrorCode, loadFailure.Function);
                using (var pair = Inspect(AppDomain.CurrentDomain.BaseDirectory, Environment.Is64BitProcess)) {
                    try {
                        CheckExisting(pair, "preload");
                        if (print != null) {
                            if (!string.Equals(ModulePath(hfModule), pair.Paths[1], StringComparison.OrdinalIgnoreCase) ||
                                !string.Equals(ModulePath(blackfish), pair.Paths[0], StringComparison.OrdinalIgnoreCase)) throw new NativeSdkException("NativeModuleConflict");
                            return;
                        }
                        IntPtr kernel = GetModuleHandleW("kernel32.dll");
                        if (kernel == IntPtr.Zero || GetProcAddress(kernel, "AddDllDirectory") == IntPtr.Zero) throw new NativeSdkException("UnsupportedLoaderPlatform");
                        loadAttempted = true;
                        loadFailure = new NativeSdkException("NativeLoadFailed");
                        // Retain every acquired reference, even on partial failure: never unload pending vendor state.
                        hfModule = LoadLibraryExW(pair.Paths[1], IntPtr.Zero, SearchFlags);
                        if (hfModule == IntPtr.Zero) throw new NativeSdkException("NativeLoadFailed", Marshal.GetLastWin32Error(), "LoadLibraryExW");
                        blackfish = LoadLibraryExW(pair.Paths[0], IntPtr.Zero, SearchFlags);
                        if (blackfish == IntPtr.Zero) throw new NativeSdkException("NativeLoadFailed", Marshal.GetLastWin32Error(), "LoadLibraryExW");
                        if (!string.Equals(ModulePath(hfModule), pair.Paths[1], StringComparison.OrdinalIgnoreCase) ||
                            !string.Equals(ModulePath(blackfish), pair.Paths[0], StringComparison.OrdinalIgnoreCase)) throw new NativeSdkException("NativeModuleConflict");
                        CheckExisting(pair, "postload");
                        if (GetModuleHandleW(Files[0]) != blackfish || GetModuleHandleW(Files[1]) != hfModule)
                            throw new NativeSdkException("NativeModuleConflict");
                        string name = Environment.Is64BitProcess ? "StartLabelPrint" : "_StartLabelPrint@8";
                        IntPtr address = GetProcAddress(blackfish, name);
                        if (address == IntPtr.Zero) throw new NativeSdkException("NativeEntryPointMissing", Marshal.GetLastWin32Error(), name);
                        print = (StartLabelDelegate)Marshal.GetDelegateForFunctionPointer(address, typeof(StartLabelDelegate));
                        loadFailure = null;
                    }
                    catch (Exception error) when (error is IOException || error is Win32Exception || error is SecurityException || error is UnauthorizedAccessException ||
                        error is ArgumentException || error is NativeSdkException || error is DllNotFoundException || error is BadImageFormatException || error is EntryPointNotFoundException) {
                        var failure = ClassifyLoaderException(error);
                        if (loadAttempted) loadFailure = failure;
                        throw failure;
                    }
                }
            }
        }
    }
}
