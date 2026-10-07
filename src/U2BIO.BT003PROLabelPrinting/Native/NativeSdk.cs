using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;

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

    // Caller must hold the process SDK slot on its worker before loading; TASK-05 wires that lifecycle.
    // No vendor function is callable in this implementation version: the print ABI is unverified.
    internal static class NativeSdk
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
        private static Dictionary<Type, Delegate> functions;
        private static bool loadAttempted;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr LoadLibraryExW(string name, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr module, [MarshalAs(UnmanagedType.LPStr)] string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern IntPtr GetModuleHandleW(string name);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern uint GetModuleFileNameW(IntPtr module, StringBuilder name, uint size);

        internal static T GetFunction<T>() where T : class
        {
            // No runtime switch can bypass this gate. A verified implementation version must replace it.
            throw new NativeSdkException("PrintContractUnverified");
        }

        private sealed class Pair : IDisposable
        {
            internal readonly FileStream[] Streams = new FileStream[2];
            internal readonly NativeImage[] Images = new NativeImage[2];
            internal readonly string[] Paths = new string[2];
            public void Dispose() { foreach (var stream in Streams) if (stream != null) stream.Dispose(); }
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
                for (int i = 0; i < Files.Length; i++) {
                    pair.Paths[i] = Path.GetFullPath(Path.Combine(directory, Files[i]));
                    NoReparse(pair.Paths[i]);
                    var stream = new FileStream(pair.Paths[i], FileMode.Open, FileAccess.Read, FileShare.Read);
                    pair.Streams[i] = stream;
                    if (stream.Length > 16 * 1024 * 1024) throw new NativeSdkException("NativeArchitectureMismatch");
                    byte[] data = new byte[checked((int)stream.Length)];
                    int read = 0;
                    while (read < data.Length) { int count = stream.Read(data, read, data.Length - read); if (count == 0) throw new IOException(); read += count; }
                    pair.Images[i] = NativeImage.Parse(data);
                    if (pair.Images[i].Machine != (is64 ? 0x8664 : 0x14c)) throw new NativeSdkException("NativeArchitectureMismatch");
                    using (var sha = SHA256.Create())
                        if (BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant() != Hashes[is64 ? 1 : 0][i])
                            throw new NativeSdkException("NativeVersionMismatch");
                    foreach (string import in pair.Images[i].Imports)
                        if (!SystemImports.Contains(import) && !(i == 0 && string.Equals(import, Files[1], StringComparison.OrdinalIgnoreCase)))
                            throw new NativeSdkException("NativeVersionMismatch");
                }
                foreach (Type type in NativeApi.Types()) {
                    string name = NativeApi.ExportName(type, is64);
                    if (!pair.Images[0].Exports.ContainsKey(name) || pair.Images[0].IsForwarder(name)) throw new NativeSdkException("NativeEntryPointMissing", null, name);
                }
                return pair;
            }
            catch (Exception error) when (error is IOException || error is InvalidDataException || error is UnauthorizedAccessException || error is SecurityException || error is ArgumentException || error is NativeSdkException) {
                pair.Dispose();
                var known = error as NativeSdkException;
                if (known != null) throw;
                if (error is InvalidDataException) throw new NativeSdkException("NativeArchitectureMismatch");
                if (error is FileNotFoundException || error is DirectoryNotFoundException) throw new NativeSdkException("NativeLibraryMissing");
                throw new NativeSdkException("NativeLoadFailed", error.HResult & 0xffff);
            }
            catch { pair.Dispose(); throw; }
        }
        internal static int InspectPair(string directory, bool is64)
        {
            using (var pair = Inspect(directory, is64)) return NativeApi.Types().Length;
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
        private static void CheckExisting(Pair pair)
        {
            string windows = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.Windows)).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using (var process = Process.GetCurrentProcess())
                foreach (ProcessModule module in process.Modules) {
                    string name = module.ModuleName;
                    foreach (string file in Files)
                        if (string.Equals(name, file, StringComparison.OrdinalIgnoreCase)) throw new NativeSdkException("NativeModuleConflict");
                    if (SystemImports.Contains(name) && !Path.GetFullPath(module.FileName).StartsWith(windows, StringComparison.OrdinalIgnoreCase))
                        throw new NativeSdkException("NativeModuleConflict");
                }
            foreach (string import in SystemImports)
                if (File.Exists(Path.Combine(Path.GetDirectoryName(pair.Paths[0]), import))) throw new NativeSdkException("NativeModuleConflict");
        }
        internal static void LoadForConnection()
        {
            lock (Sync) {
                if (functions != null) return;
                if (loadAttempted) throw new NativeSdkException("NativeLoadFailed");
                using (var pair = Inspect(AppDomain.CurrentDomain.BaseDirectory, Environment.Is64BitProcess)) {
                    try {
                        CheckExisting(pair);
                        IntPtr kernel = GetModuleHandleW("kernel32.dll");
                        if (kernel == IntPtr.Zero || GetProcAddress(kernel, "AddDllDirectory") == IntPtr.Zero) throw new NativeSdkException("UnsupportedLoaderPlatform");
                        loadAttempted = true;
                        // Retain every acquired reference, even on partial failure: never unload pending vendor state.
                        hfModule = LoadLibraryExW(pair.Paths[1], IntPtr.Zero, SearchFlags);
                        if (hfModule == IntPtr.Zero) throw new NativeSdkException("NativeLoadFailed", Marshal.GetLastWin32Error(), "LoadLibraryExW");
                        blackfish = LoadLibraryExW(pair.Paths[0], IntPtr.Zero, SearchFlags);
                        if (blackfish == IntPtr.Zero) throw new NativeSdkException("NativeLoadFailed", Marshal.GetLastWin32Error(), "LoadLibraryExW");
                        if (!string.Equals(ModulePath(hfModule), pair.Paths[1], StringComparison.OrdinalIgnoreCase) ||
                            !string.Equals(ModulePath(blackfish), pair.Paths[0], StringComparison.OrdinalIgnoreCase)) throw new NativeSdkException("NativeModuleConflict");
                        var bound = new Dictionary<Type, Delegate>();
                        foreach (Type type in NativeApi.Types()) {
                            string name = NativeApi.ExportName(type, Environment.Is64BitProcess);
                            IntPtr address = GetProcAddress(blackfish, name);
                            if (address == IntPtr.Zero) throw new NativeSdkException("NativeEntryPointMissing", Marshal.GetLastWin32Error(), name);
                            bound.Add(type, Marshal.GetDelegateForFunctionPointer(address, type));
                        }
                        functions = bound;
                    }
                    catch (Exception error) when (error is IOException || error is Win32Exception || error is SecurityException || error is UnauthorizedAccessException || error is ArgumentException) {
                        throw new NativeSdkException("NativeLoadFailed", error.HResult & 0xffff);
                    }
                }
            }
        }
    }
}
