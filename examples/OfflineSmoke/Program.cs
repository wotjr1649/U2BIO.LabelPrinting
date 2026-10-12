using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using U2BIO.BT003PROLabelPrinting;

internal static class Program
{
    private static void Check(bool valid) { if (!valid) throw new InvalidOperationException("Offline public API check failed."); }
    private static int Main(string[] args)
    {
        if (args.Length != 1 || (args[0] != "--expect-x86" && args[0] != "--expect-x64")) return 2;
        try {
            Check(Environment.Is64BitProcess == (args[0] == "--expect-x64"));
            Check(typeof(Bt003Printer).Assembly.GetCustomAttribute<TargetFrameworkAttribute>().FrameworkName == ".NETFramework,Version=v4.6.1");
            // 합성 서식의 선택 기능만 확인한다. SDK 연결과 실제 인쇄는 실행하지 않는다.
            var template = BtplTemplate.Parse("^Q40,5,7.5+\n^W70\n^G0\n^S5\n^H19\n^P1\n^C1\n^R0\n^E15\n^L\nAW,80,120,10,1,0,0,FIRST LINE\nAW,80,160,10,1,0,0,SECOND LINE\nE\n");
            var request = BtplPrintRequest.Fixed(template);
            Check(BtplProcessor.Validate(request).IsValid);
            var preview = BtplProcessor.Render(request, 0);
            Check(preview.IsValid && preview.TextElements.Count == 2 && preview.BtplText.Contains("SECOND LINE"));
            using (var printer = new Bt003Printer()) {
                Check(printer.GetConnectionStatus().Code == "NotConnected");
                var result = printer.Print(preview.BtplText);
                Check(result.Status == PrinterResultStatus.Failed && result.Code == "NotConnected" && !result.PrintInvoked);
                Check(printer.Disconnect().IsSuccess);
            }
            foreach (string file in new[] { "BlackfishNfcLib.dll", "HFModule.dll" })
                Check(!File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, file)));
            using (var process = Process.GetCurrentProcess())
                foreach (ProcessModule module in process.Modules)
                    Check(!string.Equals(module.ModuleName, "BlackfishNfcLib.dll", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(module.ModuleName, "HFModule.dll", StringComparison.OrdinalIgnoreCase));
            Console.WriteLine("PASS: independent public API .NET4.6.1 " + (Environment.Is64BitProcess ? "x64" : "x86") + " optional templates/render/disconnected; nativeSDK=not-loaded; no DB/UI/device");
            return 0;
        } catch (Exception error) { Console.WriteLine("FAIL: offline check " + error.GetType().Name); return 1; }
    }
}
