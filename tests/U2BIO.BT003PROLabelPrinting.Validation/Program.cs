using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;

namespace U2BIO.BT003PROLabelPrinting.Validation
{
    internal static class Program
    {
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void CheckFramework(Assembly assembly)
        {
            var target = (TargetFrameworkAttribute)Attribute.GetCustomAttribute(assembly, typeof(TargetFrameworkAttribute));
            Require(target != null && target.FrameworkName == ".NETFramework,Version=v4.6.1", "Target framework must be .NET Framework 4.6.1.");
        }

        private static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--host-system-directory")
            {
                Console.WriteLine("HOST_SYSTEM_DIRECTORY|" + Environment.SystemDirectory);
                return 0;
            }
            if ((args.Length == 1 || args.Length == 2) && (args[0] == "--probe-native-load" || args[0] == "--probe-usb-connection"))
            {
                string nonce = args.Length == 2 ? args[1] : "";
                if (nonce.Length != 0 && !System.Text.RegularExpressions.Regex.IsMatch(nonce, "\\A[a-f0-9]{32}\\z")) return 2;
                try { return NativeLoadProbe.RunWithNonce(Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "U2BIO.BT003PROLabelPrinting.dll")), nonce, args[0] == "--probe-usb-connection"); }
                catch (Exception) { Console.Error.WriteLine("NATIVE_LOAD|status=Failed|code=ProbeInternalError" + (nonce.Length == 0 ? "" : "|nonce=" + nonce)); return 8; }
            }
            if (args.Length == 2 && args[0] == "--inspect-sdk")
            {
                try {
                    NativeTests.InspectSdk(Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "U2BIO.BT003PROLabelPrinting.dll")), args[1]);
                    return 0;
                }
                catch (Exception error) { Console.Error.WriteLine("FAIL: SDK inspection (" + error.GetType().Name + ")"); return 1; }
            }
            if (args.Length != 1 || (args[0] != "--expect-x86" && args[0] != "--expect-x64"))
            {
                Console.Error.WriteLine("Usage: Validation.exe --expect-x86|--expect-x64");
                return 2;
            }

            try
            {
                string expectedFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "U2BIO.BT003PROLabelPrinting.dll");
                if (!File.Exists(expectedFile))
                {
                    Console.Error.WriteLine("FAIL: MANAGED_LIBRARY_MISSING");
                    return 3;
                }
                Assembly library = Assembly.LoadFrom(expectedFile);
                Require(string.Equals(Path.GetFullPath(library.Location), Path.GetFullPath(expectedFile), StringComparison.OrdinalIgnoreCase), "Loaded a different library location.");
                Require(library.GetName().Name == "U2BIO.BT003PROLabelPrinting", "Library assembly name differs.");
                CheckFramework(library);
                CheckFramework(Assembly.GetExecutingAssembly());
                NativeTests.Run(library);

                PortableExecutableKinds libraryKind;
                ImageFileMachine libraryMachine;
                library.ManifestModule.GetPEKind(out libraryKind, out libraryMachine);
                Require(libraryMachine == ImageFileMachine.I386 && (libraryKind & PortableExecutableKinds.ILOnly) != 0 &&
                    (libraryKind & (PortableExecutableKinds.Required32Bit | PortableExecutableKinds.Preferred32Bit | PortableExecutableKinds.PE32Plus)) == 0,
                    "Managed library must be AnyCPU without a 32-bit preference.");

                bool expect64 = args[0] == "--expect-x64";
                PortableExecutableKinds executableKind;
                ImageFileMachine executableMachine;
                Assembly.GetExecutingAssembly().ManifestModule.GetPEKind(out executableKind, out executableMachine);
                Require(Environment.Is64BitProcess == expect64, "Process bitness differs from the expected platform.");
                Require((executableKind & PortableExecutableKinds.Preferred32Bit) == 0, "Executable has a 32-bit preference.");
                Require(expect64
                    ? executableMachine == ImageFileMachine.AMD64 && (executableKind & PortableExecutableKinds.PE32Plus) != 0
                    : executableMachine == ImageFileMachine.I386 && (executableKind & PortableExecutableKinds.Required32Bit) != 0,
                    "Executable PE does not match its explicit platform.");

                foreach (AssemblyName reference in library.GetReferencedAssemblies())
                {
                    foreach (string prefix in new[] { "DevExpress", "Oracle", "MySql", "Npgsql", "Microsoft.Data.SqlClient", "System.Data.SqlClient" })
                        Require(!reference.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase), "Library references a UI or database provider.");
                }
                using (Process process = Process.GetCurrentProcess())
                {
                    foreach (ProcessModule module in process.Modules)
                        Require(!string.Equals(module.ModuleName, "BlackfishNfcLib.dll", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(module.ModuleName, "HFModule.dll", StringComparison.OrdinalIgnoreCase), "Validation loaded a printer SDK module.");
                }
                Console.WriteLine("PASS: framework=4.6.1 library=AnyCPU process=" + (expect64 ? "x64" : "x86") + " nativeSDK=not-loaded");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("FAIL: validation error (" + error.GetType().Name + ")");
                return 1;
            }
        }
    }
}
