using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using U2BIO.BT003PROLabelPrinting;

namespace U2BIO.BT003PROLabelPrinting.Validation
{
    internal static class WrapperAdoptionTests
    {
        private const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;
        private static void Check(bool value, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
        {
            if (!value) throw new InvalidOperationException("Manufacturer Wrapper adoption failed at check " + line);
        }
        private static void Code(Action action, string expected)
        {
            try { action(); } catch (TargetInvocationException error) {
                Check(error.InnerException != null && error.InnerException.Message == expected); return;
            }
            throw new InvalidOperationException("SDK deployment rejection missing.");
        }
        internal static void Run()
        {
            var library = typeof(Bt003Printer).Assembly;
            Type wrapper = library.GetType("HFSample.BT003CtrlWrapper", true);
            var methods = wrapper.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            Check(wrapper.IsNotPublic && methods.Length == 39);
            foreach (var method in methods) {
                var import = method.GetCustomAttribute<DllImportAttribute>();
                Check(import != null && import.Value == "BlackfishNfcLib.dll" && import.CallingConvention == CallingConvention.StdCall);
            }
            var print = wrapper.GetMethod("StartLabelPrint");
            var arguments = print.GetParameters();
            var output = arguments[1].GetCustomAttribute<MarshalAsAttribute>();
            Check(print.ReturnType == typeof(void) && arguments[0].ParameterType == typeof(ushort) &&
                arguments[1].ParameterType == typeof(StringBuilder) && output.Value == UnmanagedType.LPStr);
            var send = wrapper.GetMethod("SendData");
            Check(send.ReturnType == typeof(int) && send.GetParameters().Single().ParameterType == typeof(string));
            Check(library.GetType("U2BIO.BT003PROLabelPrinting.Native.NativeApi") == null &&
                library.GetType("U2BIO.BT003PROLabelPrinting.Native.NativeImage") == null &&
                library.GetType("U2BIO.BT003PROLabelPrinting.Native.NativeSdk") == null);
            Type calls = library.GetType("U2BIO.BT003PROLabelPrinting.Native.PrinterSdkCalls", true);
            object wired = calls.GetMethod("Create", Static).Invoke(null, null);
            foreach (string name in new[] { "SetPort", "Connect", "Status", "Disconnect", "Reset", "Form", "FileForm", "Variables", "Options" }) {
                var call = (Delegate)calls.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(wired);
                Check(call.Method.DeclaringType == wrapper);
            }
            Type runtime = library.GetType("U2BIO.BT003PROLabelPrinting.Native.SdkRuntime", true);
            var bridge = runtime.GetNestedTypes(BindingFlags.NonPublic).Single(type => typeof(Delegate).IsAssignableFrom(type));
            Check(bridge.GetCustomAttribute<UnmanagedFunctionPointerAttribute>().CallingConvention == CallingConvention.StdCall &&
                bridge.GetMethod("Invoke").GetParameters()[1].ParameterType == typeof(IntPtr));
            MethodInfo inspect = runtime.GetMethod("InspectPair", Static);
            string temporary = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".wrapper-deployment-" + Guid.NewGuid().ToString("N"));
            Code(() => inspect.Invoke(null, new object[] { temporary, Environment.Is64BitProcess }), "NativeLibraryMissing");
            Directory.CreateDirectory(temporary);
            try {
                var fixture = typeof(NativeTests).GetMethod("Image", Static);
                byte[] bytes = (byte[])fixture.Invoke(null, new object[] { Environment.Is64BitProcess });
                foreach (string name in new[] { "BlackfishNfcLib.dll", "HFModule.dll" }) File.WriteAllBytes(Path.Combine(temporary, name), bytes);
                Code(() => inspect.Invoke(null, new object[] { temporary, Environment.Is64BitProcess }), "NativeVersionMismatch");
                Code(() => inspect.Invoke(null, new object[] { temporary, !Environment.Is64BitProcess }), "NativeArchitectureMismatch");
                File.WriteAllBytes(Path.Combine(temporary, "BlackfishNfcLib.dll"), new byte[10]);
                Code(() => inspect.Invoke(null, new object[] { temporary, Environment.Is64BitProcess }), "NativeArchitectureMismatch");
                using ((IDisposable)runtime.GetMethod("LeaseDirectoryTree", Static).Invoke(null, new object[] { temporary })) {
                    bool blocked = false;
                    try { Directory.Move(temporary, temporary + "-moved"); } catch (IOException) { blocked = true; }
                    Check(blocked && !Directory.Exists(temporary + "-moved"));
                }
            } finally {
                foreach (string name in new[] { "BlackfishNfcLib.dll", "HFModule.dll" }) File.Delete(Path.Combine(temporary, name));
                Directory.Delete(temporary);
            }
            Console.WriteLine("PASS: manufacturer Wrapper declarations and product wiring; pinned deployment and file protection; output bridge retained; vendor SDK not loaded");
        }
    }
}
