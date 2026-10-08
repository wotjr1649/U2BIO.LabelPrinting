using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace U2BIO.BT003PROLabelPrinting.Validation
{
    internal static class NativeTests
    {
        private static readonly string[] Signatures = {
            "GetUidValue:v:p", "SetRfidProtocol:i:u", "SetRfidPort:i:u,s,s", "SetPrinterPort:i:u,s,s",
            "SetPrintFormFile:i:s", "SetPrintFormString:i:s", "SetPrintFormVariableData:i:s", "ResetForm:v:", "SendData:i:s",
            "SetWritingDataType:i:u", "AddNfcUriRecord:i:s", "AddNfcTextRecord:i:s,s", "AddNfcAARecord:i:s",
            "AddNfcExternalRecord:i:s,s", "AddNfcMimeRecord:i:s,s", "AddNfcSmartPosterRecord:i:s,s,s",
            "AddNfcUserDefinedRecord:i:b,s,i,s,i,s", "AddBlockData:i:u,s", "ClearNfcRecord:i:", "SetTagBlockData:i:u,s",
            "ClearBlockData:i:", "ClearMFClassicSecurity:i:", "SetUseResponseBuffer:i:u", "IsUsableResponseBuffer:i:",
            "SetRfidCondition:i:u,u,u,u,u,u", "SetLabelSetting:i:u,u,u,u", "SetULEV1_NTAG21x_Security:i:s,s,s,u,u",
            "SetMFClassicSectorSecurity:i:u,s,u", "SetULCSecurity:i:s,s,u,u", "SetPrintingOption:i:u,u",
            "SetAfiOption:i:u,b,u,u", "SetDsfidOption:i:u,b,u", "SetIcodePassword:i:u,s,s", "SetIcodeEasOption:i:u,u,u,u",
            "SetIcodePasswordWritingOption:i:u,u", "StartPrintWithRfid:v:p", "CheckPrtConnection:i:", "CheckRFConnection:i:",
            "DeviceConnect:i:", "DeviceDisconnect:i:", "StartLabelPrint:v:u,p"
        };
        private const BindingFlags InternalStatic = BindingFlags.NonPublic | BindingFlags.Static;

        private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Native contract check failed."); }
        private static Type Required(Assembly assembly, string name) { return assembly.GetType("U2BIO.BT003PROLabelPrinting.Native." + name, true); }
        private static object Invoke(Type type, string method, params object[] args) { return type.GetMethod(method, InternalStatic).Invoke(null, args); }
        private static Type AbiType(string code)
        {
            switch (code) {
                case "v": return typeof(void); case "i": return typeof(int); case "u": return typeof(ushort);
                case "b": return typeof(byte); case "s": return typeof(string); case "p": return typeof(IntPtr);
                default: throw new InvalidOperationException();
            }
        }
        private static void ExpectCode(Action action, string code)
        {
            try { action(); }
            catch (TargetInvocationException error) {
                var cause = error.InnerException;
                Check(cause != null && cause.GetType().Name == "NativeSdkException");
                Check((string)cause.GetType().GetProperty("Code", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(cause) == code);
                Check(cause.InnerException == null && cause.Message == code);
                return;
            }
            throw new InvalidOperationException("Expected native error was not returned.");
        }

        internal static void Run(Assembly library)
        {
            ConnectionTests.Run(library);
            LabelResponseTests.Run(library);
            Type probe = typeof(NativeTests).Assembly.GetType("U2BIO.BT003PROLabelPrinting.Validation.NativeLoadProbe", true);
            Type job = typeof(NativeTests).Assembly.GetType("U2BIO.BT003PROLabelPrinting.Validation.NativeLoadProbe+JobLimits", true);
            Check(Marshal.SizeOf(job) == (Environment.Is64BitProcess ? 144 : 112));
            Check(!File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BlackfishNfcLib.dll")) &&
                !File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HFModule.dll")));
            Check((int)probe.GetMethod("Run", InternalStatic).Invoke(null, new object[] { library }) == 3);
            Type api = Required(library, "NativeApi");
            var types = api.GetNestedTypes(BindingFlags.NonPublic).Where(t => typeof(Delegate).IsAssignableFrom(t)).ToArray();
            Check(api.IsNotPublic && types.Length == 41);
            foreach (string signature in Signatures) {
                var parts = signature.Split(':');
                var type = types.Single(t => t.Name == parts[0] + "Delegate");
                var convention = type.GetCustomAttribute<UnmanagedFunctionPointerAttribute>();
                Check(type.IsNestedAssembly && convention != null && convention.CallingConvention == CallingConvention.StdCall &&
                    convention.CharSet == CharSet.Ansi && !convention.SetLastError && !convention.BestFitMapping && convention.ThrowOnUnmappableChar);
                var invoke = type.GetMethod("Invoke");
                Check(invoke.ReturnType == AbiType(parts[1]));
                var codes = parts[2].Length == 0 ? new string[0] : parts[2].Split(',');
                var parameters = invoke.GetParameters();
                Check(parameters.Length == codes.Length);
                for (int i = 0; i < codes.Length; i++) {
                    var parameter = parameters[i];
                    Check(parameter.ParameterType == AbiType(codes[i]));
                    var marshal = parameter.GetCustomAttribute<MarshalAsAttribute>();
                    if (codes[i] == "s") Check(parameter.IsIn && !parameter.IsOut && marshal != null && marshal.Value == UnmanagedType.LPStr);
                    if (codes[i] == "p") Check(parameter.IsOut && !parameter.IsIn);
                    if (codes[i] == "u" || codes[i] == "b") Check(marshal != null && marshal.Value == (codes[i] == "u" ? UnmanagedType.U2 : UnmanagedType.U1));
                }
                Check((string)Invoke(api, "ExportName", type, false) == "_" + parts[0] + "@" + (codes.Length * 4));
                Check((string)Invoke(api, "ExportName", type, true) == parts[0]);
            }
            Check(!types.Any(t => t.Name.StartsWith("StartPrintWithRfid2", StringComparison.Ordinal) || t.Name.StartsWith("ReadResponseMessage", StringComparison.Ordinal)));
            Type sdk = Required(library, "NativeSdk");
            CheckUsbProbe(sdk);
            Check((string)Invoke(sdk, "SystemModuleLocation", Path.Combine(Environment.SystemDirectory, "USER32.dll")) == "system");
            Check((string)Invoke(sdk, "SystemModuleLocation", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "WinSxS", "test", "gdiplus.dll")) == "sideBySide");
            Check((string)Invoke(sdk, "SystemModuleLocation", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp", "USER32.dll")) == "other");
            Invoke(sdk, "CheckSystemModulePath", Path.Combine(Environment.SystemDirectory, "USER32.dll"));
            ExpectCode(() => Invoke(sdk, "CheckSystemModulePath", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp", "USER32.dll")), "NativeModuleConflict");
            using (var reference = new MemoryStream(new byte[] { 1, 2, 3 }))
            using (var same = new MemoryStream(new byte[] { 1, 2, 3 }))
            using (var changed = new MemoryStream(new byte[] { 1, 2, 4 }))
            using (var empty = new MemoryStream()) {
                Check((bool)Invoke(sdk, "SameFileContent", reference, same));
                Check(!(bool)Invoke(sdk, "SameFileContent", reference, changed));
                Check(!(bool)Invoke(sdk, "SameFileContent", reference, empty));
            }
            string sxsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "WinSxS");
            ExpectCode(() => Invoke(sdk, "CheckSystemModulePath", Path.Combine(sxsRoot, "invalid", "USER32.dll")), "NativeModuleConflict");
            bool checkedSideBySide = false;
            using (var reference = File.OpenRead(Path.Combine(Environment.SystemDirectory, "gdiplus.dll"))) {
                string architecture = Environment.Is64BitProcess ? "amd64" : "x86";
                foreach (string dir in Directory.EnumerateDirectories(sxsRoot, architecture + "_microsoft.windows.gdiplus_*").Take(64)) {
                    string candidate = Path.Combine(dir, "gdiplus.dll");
                    if (!File.Exists(candidate)) continue;
                    using (var file = File.OpenRead(candidate)) {
                        if (!(bool)Invoke(sdk, "SameFileContent", reference, file)) {
                            ExpectCode(() => Invoke(sdk, "CheckSystemModulePath", candidate), "NativeModuleConflict");
                            continue;
                        }
                    }
                    Invoke(sdk, "CheckSystemModulePath", candidate);
                    checkedSideBySide = true;
                }
            }
            Check(checkedSideBySide);
            foreach (var error in new Exception[] { new DllNotFoundException(), new BadImageFormatException(), new EntryPointNotFoundException() }) {
                object classified = Invoke(sdk, "ClassifyLoaderException", error);
                string code = (string)classified.GetType().GetProperty("Code", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(classified);
                Check(code == (error is BadImageFormatException ? "NativeArchitectureMismatch" : error is EntryPointNotFoundException ? "UnsupportedLoaderPlatform" : "NativeLoadFailed"));
            }
            var generic = sdk.GetMethod("GetFunction", InternalStatic).MakeGenericMethod(types.Single(t => t.Name == "SetPrinterPortDelegate"));
            ExpectCode(() => generic.Invoke(null, null), "PrintContractUnverified");
            string temporary = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".native-validation-" + Guid.NewGuid().ToString("N"));
            ExpectCode(() => Invoke(sdk, "InspectPair", temporary, Environment.Is64BitProcess), "NativeLibraryMissing");
            Directory.CreateDirectory(temporary);
            try {
                using ((IDisposable)Invoke(sdk, "LeaseDirectoryTree", temporary)) {
                    bool refused = false;
                    try { Directory.Move(temporary, temporary + "-moved"); }
                    catch (IOException) { refused = true; }
                    Check(refused && Directory.Exists(temporary) && !Directory.Exists(temporary + "-moved"));
                }
                byte[] fixture = Image(Environment.Is64BitProcess);
                foreach (string name in new[] { "BlackfishNfcLib.dll", "HFModule.dll" }) File.WriteAllBytes(Path.Combine(temporary, name), fixture);
                using (var file = File.OpenRead(Path.Combine(temporary, "BlackfishNfcLib.dll")))
                    Check(string.Equals((string)Invoke(sdk, "FinalFilePath", file.SafeFileHandle), Path.GetFullPath(Path.Combine(temporary, "BlackfishNfcLib.dll")), StringComparison.OrdinalIgnoreCase));
                ExpectCode(() => Invoke(sdk, "InspectPair", temporary, Environment.Is64BitProcess), "NativeVersionMismatch");
                ExpectCode(() => Invoke(sdk, "InspectPair", temporary, !Environment.Is64BitProcess), "NativeArchitectureMismatch");
                File.WriteAllBytes(Path.Combine(temporary, "BlackfishNfcLib.dll"), new byte[10]);
                ExpectCode(() => Invoke(sdk, "InspectPair", temporary, Environment.Is64BitProcess), "NativeArchitectureMismatch");
            }
            finally {
                foreach (string name in new[] { "BlackfishNfcLib.dll", "HFModule.dll" }) File.Delete(Path.Combine(temporary, name));
                Directory.Delete(temporary);
            }
            Type image = Required(library, "NativeImage");
            foreach (byte[] bytes in new[] { new byte[0], new byte[63], new byte[128] }) {
                try { Invoke(image, "Parse", bytes); throw new InvalidOperationException("Corrupt PE accepted."); }
                catch (TargetInvocationException error) { Check(error.InnerException is InvalidDataException); }
            }
            Console.WriteLine("PASS: native declarations=41 output-calls=blocked deployment negatives=passed parser=offline");
        }

        private sealed class UsbFixture
        {
            internal int Pre, SetResult, ConnectResult, Open = 1, DisconnectResult = 91, Closed;
            internal bool ThrowConnect;
            internal string Calls = "";
            private int statusCalls;
            public int Set(ushort type, string port, string ip) { Check(type == 1 && port == "0" && ip == ""); Calls += "S"; return SetResult; }
            public int Connect() { Calls += "C"; if (ThrowConnect) throw new InvalidOperationException("test"); return ConnectResult; }
            public int Status() { Calls += "?"; return statusCalls++ == 0 ? Pre : (Calls.Contains("D") ? Closed : Open); }
            public int Disconnect() { Calls += "D"; return DisconnectResult; }
        }
        private static void CheckUsbProbe(Type sdk)
        {
            MethodInfo run = sdk.GetMethod("RunUsbConnectionProbe", InternalStatic);
            Check(run != null && sdk.GetMethod("ProbeUsbConnection", InternalStatic) != null);
            var cases = new[] {
                new UsbFixture(), new UsbFixture { Pre = 1 }, new UsbFixture { Pre = 9 },
                new UsbFixture { SetResult = 1 }, new UsbFixture { SetResult = 102 },
                new UsbFixture { ConnectResult = 102 }, new UsbFixture { Open = 0 },
                new UsbFixture { Open = 9 }, new UsbFixture { Closed = 1 }, new UsbFixture { ThrowConnect = true }
            };
            string[] expected = { "?SC?D?", "?", "?", "?S", "?S", "?SCD?", "?SC?D?", "?SC?D?", "?SC?D?", "?SC" };
            for (int i = 0; i < cases.Length; i++) {
                var fixture = cases[i];
                string[] names = { "Set", "Connect", "Status", "Disconnect" };
                object[] delegates = run.GetParameters().Select((p, n) => (object)Delegate.CreateDelegate(p.ParameterType, fixture, fixture.GetType().GetMethod(names[n]))).ToArray();
                if (fixture.ThrowConnect) {
                    bool threw = false;
                    try { run.Invoke(null, delegates); }
                    catch (TargetInvocationException error) { threw = error.InnerException is InvalidOperationException; }
                    Check(threw);
                }
                else {
                    object result = run.Invoke(null, delegates);
                    Check((bool)result.GetType().GetProperty("Passed", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(result) == (i == 0));
                    int? disconnected = (int?)result.GetType().GetField("Disconnect", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(result);
                    Check(disconnected == (fixture.Calls.Contains("D") ? (int?)91 : null));
                }
                Check(fixture.Calls == expected[i]);
            }
        }

        private static byte[] Image(bool is64)
        {
            byte[] bytes = new byte[768];
            Action<int, ushort> put16 = (offset, value) => Array.Copy(BitConverter.GetBytes(value), 0, bytes, offset, 2);
            Action<int, uint> put32 = (offset, value) => Array.Copy(BitConverter.GetBytes(value), 0, bytes, offset, 4);
            put16(0, 0x5a4d); put32(60, 128); put32(128, 0x4550); put16(132, (ushort)(is64 ? 0x8664 : 0x14c)); put16(134, 1);
            put16(148, (ushort)(is64 ? 240 : 224)); put16(150, 0x2000); put16(152, (ushort)(is64 ? 0x20b : 0x10b));
            put32(152 + 60, 512); put32(152 + (is64 ? 108 : 92), 16);
            int section = 152 + (is64 ? 240 : 224);
            put32(section + 8, 256); put32(section + 12, 4096); put32(section + 16, 256); put32(section + 20, 512);
            return bytes;
        }

        internal static void InspectSdk(Assembly library, string root)
        {
            Type sdk = Required(library, "NativeSdk");
            foreach (string platform in new[] { "x86", "x64" })
                Check((int)Invoke(sdk, "InspectPair", Path.Combine(root, platform, "lib"), platform == "x64") == 41);
            using (var process = System.Diagnostics.Process.GetCurrentProcess())
                foreach (System.Diagnostics.ProcessModule module in process.Modules)
                    Check(!string.Equals(module.ModuleName, "BlackfishNfcLib.dll", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(module.ModuleName, "HFModule.dll", StringComparison.OrdinalIgnoreCase));
            Console.WriteLine("PASS: pinned x86/x64 native pairs architecture/hash/41 exports/imports inspected; vendor code not executed");
        }
    }
}
