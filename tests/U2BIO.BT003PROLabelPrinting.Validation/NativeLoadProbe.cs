using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Threading;

namespace U2BIO.BT003PROLabelPrinting.Validation
{
    // Dedicated diagnostic process only. Binds delegates; never retrieves or invokes a device delegate.
    internal static class NativeLoadProbe
    {
        internal static int Run(Assembly library)
        {
            int result = 8;
            string report = "NATIVE_LOAD|status=Failed|code=ProbeInternalError";
            string slotName = "Local\\U2BIO.BT003PROLabelPrinting.Process." + Process.GetCurrentProcess().Id;
            var acquired = new ManualResetEvent(false);
            var proceed = new ManualResetEvent(false);
            var finished = new ManualResetEvent(false);
            var worker = new Thread(() => {
                bool owns = false;
                try {
                    using (var slot = new Mutex(false, slotName)) {
                        try {
                            try { owns = slot.WaitOne(0); }
                            catch (AbandonedMutexException) { owns = true; result = 4; report = "NATIVE_LOAD|status=Failed|code=ProbeSlotAbandoned"; return; }
                            if (!owns) { result = 4; report = "NATIVE_LOAD|status=Failed|code=ProbeSlotBusy"; return; }
                            acquired.Set();
                            if (!proceed.WaitOne(TimeSpan.FromSeconds(30))) { result = 6; report = "NATIVE_LOAD|status=Failed|code=ProbeTimeout"; return; }
                            Type sdk = library.GetType("U2BIO.BT003PROLabelPrinting.Native.NativeSdk", true);
                            sdk.GetMethod("LoadForConnection", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
                            var bound = (IDictionary)sdk.GetField("functions", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                            if (bound == null || bound.Count != 41) { result = 7; report = "NATIVE_LOAD|status=Failed|code=ProbeBindingMismatch"; return; }
                            result = 0;
                            report = "NATIVE_LOAD|status=Passed|bound=41|deviceDelegatesInvoked=0";
                        }
                        finally { if (owns) slot.ReleaseMutex(); }
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
                        if (checkerOwns) { Console.WriteLine("NATIVE_LOAD|status=Failed|code=ProbeReservationFailed"); return 4; }
                    }
                    finally { if (checkerOwns) checker.ReleaseMutex(); }
                }
                proceed.Set();
                if (!finished.WaitOne(TimeSpan.FromSeconds(30))) signalled = WaitHandle.WaitTimeout;
            }
            if (signalled == WaitHandle.WaitTimeout) {
                // The launcher supervises process exit; do not claim the pending worker released its reservation.
                Console.WriteLine("NATIVE_LOAD|status=Failed|code=ProbeTimeout");
                return 6;
            }
            if (!worker.Join(5000)) { Console.WriteLine("NATIVE_LOAD|status=Failed|code=ProbeTimeout"); return 6; }
            acquired.Dispose(); proceed.Dispose(); finished.Dispose();
            Console.WriteLine(report);
            return result;
        }
    }
}
