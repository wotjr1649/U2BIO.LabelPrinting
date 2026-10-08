using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace U2BIO.BT003PROLabelPrinting.Validation
{
    // Dedicated diagnostic process. Load mode binds only; connection mode requires an explicit target.
    internal static class NativeLoadProbe
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimits
        {
            internal long ProcessTime, JobTime;
            internal uint Flags;
            internal UIntPtr MinWorkingSet, MaxWorkingSet;
            internal uint ActiveProcesses;
            internal UIntPtr Affinity;
            internal uint Priority, Scheduling;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters { internal ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)]
        private struct JobLimits
        {
            internal BasicLimits Basic;
            internal IoCounters Io;
            internal UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
        }
        private static IntPtr job;
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern IntPtr CreateJobObjectW(IntPtr security, string name);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(IntPtr handle, int infoClass, ref JobLimits limits, uint size);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(IntPtr handle, IntPtr process);
        [DllImport("kernel32.dll", ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
        private static bool EnsureJob()
        {
            if (job != IntPtr.Zero) return true;
            IntPtr candidate = CreateJobObjectW(IntPtr.Zero, null);
            if (candidate == IntPtr.Zero) return false;
            var limits = new JobLimits { Basic = new BasicLimits { Flags = 0x2000 | 0x8 | 0x200, ActiveProcesses = 4 }, JobMemory = new UIntPtr(512u * 1024u * 1024u) };
            using (var process = Process.GetCurrentProcess()) {
                if (!SetInformationJobObject(candidate, 9, ref limits, (uint)Marshal.SizeOf(typeof(JobLimits))) || !AssignProcessToJobObject(candidate, process.Handle)) {
                    CloseHandle(candidate); return false;
                }
            }
            // Intentionally process-lifetime. OS handle cleanup kills normal descendants when this process exits.
            job = candidate;
            return true;
        }
        internal static int Run(Assembly library)
        { return RunWithNonce(library, ""); }
        internal static int RunWithNonce(Assembly library, string nonce, string transport = null, string port = null, string ip = "")
        {
            string suffix = nonce.Length == 0 ? "" : "|nonce=" + nonce;
            object options = null;
            if (transport != null) {
                try { options = library.GetType("U2BIO.BT003PROLabelPrinting.Native.NativeConnectionOptions", true)
                    .GetMethod("Create", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { transport, port, ip }); }
                catch (TargetInvocationException error) {
                    if (error.InnerException != null && error.InnerException.Message == "InvalidConnectionOptions") {
                        Console.WriteLine("NATIVE_LOAD|status=Failed|code=InvalidConnectionOptions" + suffix); return 2;
                    }
                    throw;
                }
            }
            if (!EnsureJob()) { Console.WriteLine("NATIVE_LOAD|status=Failed|code=ProbeJobUnavailable" + suffix); return 9; }
            int result = 8;
            string report = "NATIVE_LOAD|status=Failed|code=ProbeInternalError";
            string slotName = "Local\\U2BIO.BT003PROLabelPrinting.Process." + Process.GetCurrentProcess().Id;
            var acquired = new ManualResetEvent(false);
            var proceed = new ManualResetEvent(false);
            var finished = new ManualResetEvent(false);
            var worker = new Thread(() => {
                bool owns = false;
                Mutex target = null;
                bool ownsTarget = false;
                try {
                    using (var slot = new Mutex(false, slotName)) {
                        try {
                            try { owns = slot.WaitOne(0); }
                            catch (AbandonedMutexException) { owns = true; result = 4; report = "NATIVE_LOAD|status=Failed|code=ProbeSlotAbandoned"; return; }
                            if (!owns) { result = 4; report = "NATIVE_LOAD|status=Failed|code=ProbeSlotBusy"; return; }
                            acquired.Set();
                            if (!proceed.WaitOne(TimeSpan.FromSeconds(30))) { result = 6; report = "NATIVE_LOAD|status=Failed|code=ProbeTimeout"; return; }
                            if (options != null) {
                                string hash;
                                string key = (string)options.GetType().GetField("TargetKey", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(options);
                                using (var sha = System.Security.Cryptography.SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key))).Replace("-", "").ToLowerInvariant();
                                target = new Mutex(false, "Local\\U2BIO.BT003PROLabelPrinting.Target." + hash);
                                try { ownsTarget = target.WaitOne(0); }
                                catch (AbandonedMutexException) { ownsTarget = true; result = 4; report = "NATIVE_LOAD|status=Failed|code=ProbeSlotAbandoned"; return; }
                                if (!ownsTarget) { result = 4; report = "NATIVE_LOAD|status=Failed|code=ProbeSlotBusy"; return; }
                            }
                            Type sdk = library.GetType("U2BIO.BT003PROLabelPrinting.Native.NativeSdk", true);
                            sdk.GetMethod("LoadForConnection", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
                            var bound = (IDictionary)sdk.GetField("functions", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                            if (bound == null || bound.Count != 41) { result = 7; report = "NATIVE_LOAD|status=Failed|code=ProbeBindingMismatch"; return; }
                            if (options != null) {
                                object observed = sdk.GetMethod("ProbeConnection", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new[] { options });
                                bool passed = (bool)observed.GetType().GetProperty("Passed", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(observed);
                                report = "CONNECTION|transport=" + transport + "|status=" + (passed ? "Passed" : "Failed");
                                foreach (string field in new[] { "Pre", "SetPort", "Connect", "Open", "Disconnect", "Closed" }) {
                                    int? value = (int?)observed.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(observed);
                                    report += "|" + field.ToLowerInvariant() + "=" + (value.HasValue ? value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "none");
                                }
                                report += "|printApiCalls=0|rfidApiCalls=0";
                                result = passed ? 0 : 10;
                                return;
                            }
                            result = 0;
                            report = "NATIVE_LOAD|status=Passed|bound=41|deviceDelegatesInvoked=0";
                        }
                        finally { if (ownsTarget) target.ReleaseMutex(); if (target != null) target.Dispose(); if (owns) slot.ReleaseMutex(); }
                    }
                }
                catch (TargetInvocationException error) {
                    var cause = error.InnerException;
                    if (cause != null && cause.GetType().FullName == "U2BIO.BT003PROLabelPrinting.Native.NativeSdkException") {
                        string code = (string)cause.GetType().GetProperty("Code", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(cause);
                        string[] allowed = { "NativeLibraryMissing", "NativeArchitectureMismatch", "NativeVersionMismatch", "NativeModuleConflict",
                            "UnsupportedLoaderPlatform", "NativeLoadFailed", "NativeEntryPointMissing", "PrintContractUnverified" };
                        if (Array.IndexOf(allowed, code) >= 0) {
                            var system = (int?)cause.GetType().GetProperty("SystemErrorCode", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(cause);
                            result = code == "NativeLibraryMissing" ? 3 : 5;
                            report = "NATIVE_LOAD|status=Failed|code=" + code + "|win32=" + (system.HasValue ? system.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "none");
                            string detail = (string)cause.GetType().GetProperty("Function", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(cause);
                            if (code == "NativeModuleConflict" && detail != null && System.Text.RegularExpressions.Regex.IsMatch(detail,
                                @"\A(preload|postload):(system-import|app-local-system-import|vendor-module):(setupapi\.dll|kernel32\.dll|user32\.dll|gdi32\.dll|msimg32\.dll|winspool\.drv|advapi32\.dll|shell32\.dll|shlwapi\.dll|uxtheme\.dll|ole32\.dll|oleaut32\.dll|oledlg\.dll|crypt32\.dll|ws2_32\.dll|oleacc\.dll|gdiplus\.dll|imm32\.dll|winmm\.dll|blackfishnfclib\.dll|hfmodule\.dll):(system|sideBySide|other)\z"))
                                report += "|detail=" + detail;
                        }
                    }
                }
                catch (UnauthorizedAccessException) { result = 4; report = "NATIVE_LOAD|status=Failed|code=ProbeSlotDenied"; }
                catch (Exception) { result = 8; report = "NATIVE_LOAD|status=Failed|code=ProbeInternalError"; }
                finally { finished.Set(); }
            }) { IsBackground = true, Name = "BT003 native load probe" };
            worker.Start();
            int signalled = WaitHandle.WaitAny(new WaitHandle[] { acquired, finished }, TimeSpan.FromSeconds(30));
            if (signalled == 0) {
                // A different thread must check the slot: recursive acquisition on the worker would prove nothing.
                using (var checker = new Mutex(false, slotName)) {
                    bool checkerOwns = false;
                    try {
                        try { checkerOwns = checker.WaitOne(0); }
                        catch (AbandonedMutexException) { checkerOwns = true; }
                        if (checkerOwns) { Console.WriteLine("NATIVE_LOAD|status=Failed|code=ProbeReservationFailed" + suffix); return 4; }
                    }
                    finally { if (checkerOwns) checker.ReleaseMutex(); }
                }
                proceed.Set();
                if (!finished.WaitOne(TimeSpan.FromSeconds(30))) signalled = WaitHandle.WaitTimeout;
            }
            if (signalled == WaitHandle.WaitTimeout) {
                // The launcher supervises process exit; do not claim the pending worker released its reservation.
                Console.WriteLine("NATIVE_LOAD|status=Failed|code=ProbeTimeout" + suffix);
                return 6;
            }
            if (!worker.Join(5000)) { Console.WriteLine("NATIVE_LOAD|status=Failed|code=ProbeTimeout" + suffix); return 6; }
            acquired.Dispose(); proceed.Dispose(); finished.Dispose();
            Console.WriteLine(report + suffix);
            return result;
        }
    }
}
