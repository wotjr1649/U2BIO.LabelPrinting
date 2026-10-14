using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using U2BIO.BT003PROLabelPrinting;

internal static class Program
{
    private static void Check(bool valid) { if (!valid) throw new InvalidOperationException("Offline public API check failed."); }

    // 화면 표시용 치환은 호출 프로그램 책임이다. DLL에 다시 넣을 인쇄 서식을 만들지 않는다.
    private static string PreviewText(string payload, string variableData)
    {
        string[] values = variableData == null ? new string[0] : variableData.Split(new[] { "!!" }, StringSplitOptions.None);
        return Regex.Replace(payload, "\\^V([0-9]{2})", match =>
        {
            int index = int.Parse(match.Groups[1].Value);
            return index < values.Length ? values[index] : match.Value;
        }, RegexOptions.CultureInvariant);
    }

    private static int Main(string[] args)
    {
        if (args.Length != 1 || (args[0] != "--expect-x86" && args[0] != "--expect-x64")) return 2;
        string directory = Path.Combine(Path.GetTempPath(), "txt-caller-" + Guid.NewGuid().ToString("N"));
        try
        {
            Check(Environment.Is64BitProcess == (args[0] == "--expect-x64"));
            Check(typeof(Bt003Printer).Assembly.GetCustomAttribute<TargetFrameworkAttribute>().FrameworkName == ".NETFramework,Version=v4.6.1");
            string variableData = "FIRST LINE!!SECOND LINE";
            var template = BtplTemplate.Parse("^L\nAW,80,120,10,1,0,0,^V00\nAW,80,160,10,1,0,0,^V01\nE\n");
            Check(PreviewText(template.TextElements[0].Payload, variableData) == "FIRST LINE");
            Check(PreviewText("^V00/^V01/^V02/^V03", "A!!!!C!!") == "A//C/");
            Check(PreviewText("^V00", "^V01!!SAFE") == "^V01");
            Check(PreviewText("^V02", variableData) == "^V02");
            template = template.WithText(0, template.TextElements[0].WithPosition(90, 130).WithSize(12).WithWeight(2));
            Check(template.TextElements[0].Payload == "^V00" && template.ToBtpl().Contains("^V01"));

            Directory.CreateDirectory(directory);
            string firstPath = Path.Combine(directory, "label.txt");
            template = template.SaveAs(firstPath);
            string firstText = File.ReadAllText(firstPath, System.Text.Encoding.GetEncoding(949));
            template = template.SaveAs(Path.Combine(directory, "label-copy.txt"));
            template = template.WithText(1, template.TextElements[1].WithPayload("검사번호 ^V01")).Save();
            Check(File.ReadAllText(firstPath, System.Text.Encoding.GetEncoding(949)) == firstText);
            Check(File.ReadAllText(template.FilePath, System.Text.Encoding.GetEncoding(949)) == template.ToBtpl());

            // 연결 없는 Print로 공개 경계만 확인한다. SDK 연결·물리 인쇄는 실행하지 않는다.
            using (var printer = new Bt003Printer())
            {
                var connection = printer.GetConnectionStatus();
                Check(!connection.IsSuccess && connection.NativeCode == null && !string.IsNullOrWhiteSpace(connection.Message));
                var result = printer.Print(template.ToBtpl(), variableData);
                Check(!result.IsSuccess && result.NativeCode == null && !result.PrintInvoked && !string.IsNullOrWhiteSpace(result.Message));
                Check(printer.Disconnect().IsSuccess);
            }
            foreach (string file in new[] { "BlackfishNfcLib.dll", "HFModule.dll" })
                Check(!File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, file)));
            using (var process = Process.GetCurrentProcess())
                foreach (ProcessModule module in process.Modules)
                    Check(!string.Equals(module.ModuleName, "BlackfishNfcLib.dll", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(module.ModuleName, "HFModule.dll", StringComparison.OrdinalIgnoreCase));
            Console.WriteLine("PASS: independent public API .NET4.6.1 " + (Environment.Is64BitProcess ? "x64" : "x86") + " TXT edit/save/caller-preview/disconnected; nativeSDK=not-loaded; no DB/UI/device");
            return 0;
        }
        catch (Exception error) { Console.WriteLine("FAIL: offline check " + error.GetType().Name); return 1; }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
