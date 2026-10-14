using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace U2BIO.BT003PROLabelPrinting.Validation
{
    // 실제 제품 DLL의 공개 계약을 확인하며 제조사 SDK는 호출하지 않는다.
    internal static class SyncMvpContractTests
    {
        internal static void Run(Assembly library)
        {
            Type printer = library.GetType("U2BIO.BT003PROLabelPrinting.Bt003Printer", true);
            Type result = library.GetType("U2BIO.BT003PROLabelPrinting.PrinterResult", true);
            string[] methods = { "ConnectUsb", "GetConnectionStatus", "SetPrintingOptions", "Print", "PrintFile", "ConfirmPrintPosition", "Disconnect", "Dispose" };
            var actual = printer.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (actual.Length != methods.Length || actual.Any(method => !methods.Contains(method.Name)))
                throw new InvalidOperationException("Unexpected public printer API.");
            foreach (var method in actual.Where(method => method.Name != "Dispose"))
                if (method.ReturnType != result) throw new InvalidOperationException("PrinterResult contract missing.");
            // 실제 DLL은 SDK 가변데이터 문자열 하나만 받는다. 배열/params와 혼용하지 않는다.
            foreach (string name in new[] { "Print", "PrintFile" }) {
                var method = printer.GetMethod(name, new[] { typeof(string), typeof(string) });
                if (method == null || printer.GetMethod(name, new[] { typeof(string), typeof(string[]) }) != null)
                    throw new InvalidOperationException("Raw SDK variable string API missing.");
                var parameter = method.GetParameters()[1];
                if (parameter.Name != "variableData" || !parameter.IsOptional || parameter.DefaultValue != null
                    || parameter.GetCustomAttribute<ParamArrayAttribute>() != null)
                    throw new InvalidOperationException("Variable data must be optional string with null default.");
            }
            // 결과를 해석하는 공개 항목은 정확히 다섯 개여야 하며 제거한 속성이 남아도 실패한다.
            string[] names = { "IsSuccess", "Message", "NativeCode", "PrintInvoked", "PrintCode" };
            Type[] types = { typeof(bool), typeof(string), typeof(int?), typeof(bool), typeof(int?) };
            var properties = result.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (properties.Length != names.Length || properties.Any(property => !names.Contains(property.Name)))
                throw new InvalidOperationException("PrinterResult must expose exactly five properties.");
            for (int i = 0; i < names.Length; i++) {
                var property = result.GetProperty(names[i]);
                var getter = property.GetGetMethod();
                var setter = property.GetSetMethod(true);
                if (property.PropertyType != types[i] || getter == null || !getter.IsPublic || setter == null || !setter.IsAssembly)
                    throw new InvalidOperationException("PrinterResult property type or access differs.");
            }
            if (result.GetConstructors(BindingFlags.Public | BindingFlags.Instance).Length != 0)
                throw new InvalidOperationException("PrinterResult cannot be constructed by callers.");
            var constructor = result.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (constructor == null || !constructor.IsAssembly)
                throw new InvalidOperationException("PrinterResult requires an internal constructor.");
            // 아직 실행하지 않은 결과를 성공으로 오해하지 않도록 bool 기본값은 false로 둔다.
            if ((bool)result.GetProperty("IsSuccess").GetValue(constructor.Invoke(null)))
                throw new InvalidOperationException("A new PrinterResult cannot start successful.");
            var display = result.GetCustomAttribute<DebuggerDisplayAttribute>();
            if (display == null || display.Value != "{IsSuccess}: {Message}")
                throw new InvalidOperationException("PrinterResult debugger display must show success and message.");
            foreach (string removed in new[] { "PrinterResultStatus", "BatchPrintResult", "Bt003ConnectionOptions", "PrintInput", "Native.SdkRuntime", "Native.NativeLabelResponse", "Native.PrinterSdkCalls" })
                if (library.GetType("U2BIO.BT003PROLabelPrinting." + removed) != null) throw new InvalidOperationException("Legacy runtime remains.");
            var search = library.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>();
            if (search == null || search.Paths != (DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.System32))
                throw new InvalidOperationException("DLL search restriction differs.");
            Type wrapper = library.GetType("HFSample.BT003CtrlWrapper", true);
            var declarations = wrapper.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (declarations.Length != 39) throw new InvalidOperationException("Manufacturer declarations differ.");
            foreach (var declaration in declarations) {
                var import = declaration.GetCustomAttribute<DllImportAttribute>();
                if (import == null || import.Value != "BlackfishNfcLib.dll" || import.CallingConvention != CallingConvention.StdCall)
                    throw new InvalidOperationException("Manufacturer ABI declaration differs.");
            }
            var print = wrapper.GetMethod("StartLabelPrint");
            if (print.ReturnType != typeof(void) || print.GetParameters()[1].ParameterType != typeof(StringBuilder))
                throw new InvalidOperationException("Original StringBuilder contract missing.");
            Console.WriteLine("PASS: actual product synchronous API, result, original Wrapper and DLL search contract; SDK not loaded");
        }

        private static int Main(string[] args)
        {
            try { Run(Assembly.LoadFrom(Path.GetFullPath(args[0]))); return 0; }
            catch (Exception error) { Console.Error.WriteLine("FAIL: sync API contract (" + error.GetType().Name + ")"); return 1; }
        }
    }
}
