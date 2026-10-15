using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace U2BIO.BT003PROLabelPrinting.Tester
{
    // 화면·SDK·장치 없이 설정과 서식 규칙만 검사한다. 임시 폴더 밖의 파일은 건드리지 않는다.
    internal static class SelfCheck
    {
        private static readonly Encoding Cp949 = Encoding.GetEncoding(949);
        private const string SampleText = "^Q35,5,9+\r\n^W70\r\n^S5\r\n^H19\r\n^L\r\n"
            + "AW,80,120,10,1,0,0,검사번호 ^V00\r\nAW,80,160,10,1,0,0,^V01\r\nE\r\n";
        private static string step = "arguments";

        internal static int Run(string[] args)
        {
            if (args.Length != 2 || args[0] != "--self-check" || (args[1] != "--expect-x86" && args[1] != "--expect-x64")) return 2;
            bool expect64 = args[1] == "--expect-x64";
            string directory = Path.Combine(Path.GetTempPath(), "bt003-tester-" + Guid.NewGuid().ToString("N"));
            try
            {
                step = "bitness";
                Check(Environment.Is64BitProcess == expect64);
                Directory.CreateDirectory(directory);
                CheckSettings(directory);
                CheckDocument(directory);
                CheckPreview();
                CheckMedia();
                CheckGapLabel();
                CheckUsbPorts();
                CheckNoNativeOrUi();
                Console.WriteLine("PASS: tester self-check " + (expect64 ? "x64" : "x86")
                    + " settings/document/preview/media/safe-area/gap-label/usb-list; nativeSDK=not-loaded; no UI/device");
                return 0;
            }
            catch (Exception error)
            {
                Console.WriteLine("FAIL: " + step + " " + error.GetType().Name);
                return 1;
            }
            finally
            {
                // 정리 실패가 결과 줄 대신 처리되지 않은 예외 창을 띄우지 않게 한다.
                try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static void CheckSettings(string directory)
        {
            step = "settings-default";
            string path = Path.Combine(directory, "settings", "settings.xml");
            var settings = TesterSettings.Load(path);
            Check(settings.LoadWarning == null && File.Exists(path) && settings.FilePath == path);
            Check(settings.Port == TesterSettings.DefaultPort && settings.PresentationDistanceMm == TesterSettings.DefaultDistanceMm
                && settings.TemplateFolder == "" && settings.RecentFiles.Count == 0);

            step = "settings-validation";
            Check(TesterSettings.CheckPort("") != null && TesterSettings.CheckPort("  ") != null
                && TesterSettings.CheckPort("0\t") != null && TesterSettings.CheckPort("0") == null);
            Check(TesterSettings.CheckDistance(-1) != null && TesterSettings.CheckDistance(1001) != null
                && TesterSettings.CheckDistance(0) == null && TesterSettings.CheckDistance(1000) == null);
            Check(TesterSettings.CheckFolder("") == null && TesterSettings.CheckFolder(directory) == null
                && TesterSettings.CheckFolder(Path.Combine(directory, "missing")) != null);
            Expect<ArgumentException>(() => settings.Update("", 20, ""));
            Expect<ArgumentException>(() => settings.Update("1", 1001, ""));
            Check(settings.Port == TesterSettings.DefaultPort && settings.PresentationDistanceMm == TesterSettings.DefaultDistanceMm);

            step = "settings-roundtrip";
            settings.Update("1", 20, directory);
            for (int index = 0; index < TesterSettings.MaxRecentFiles + 2; index++)
                settings.AddRecent(Path.Combine(directory, "label" + index + ".txt"));
            settings.AddRecent(Path.Combine(directory, "LABEL5.TXT"));
            Check(settings.RecentFiles.Count == TesterSettings.MaxRecentFiles
                && settings.RecentFiles[0] == Path.Combine(directory, "LABEL5.TXT")
                && settings.RecentFiles.Count(file => string.Equals(file, Path.Combine(directory, "label5.txt"), StringComparison.OrdinalIgnoreCase)) == 1
                && settings.RecentFiles[1] == Path.Combine(directory, "label11.txt"));
            var reloaded = TesterSettings.Load(path);
            Check(reloaded.LoadWarning == null && reloaded.Port == "1" && reloaded.PresentationDistanceMm == 20
                && reloaded.TemplateFolder == directory && reloaded.RecentFiles.SequenceEqual(settings.RecentFiles));

            step = "settings-corrupt";
            CheckDamagedSettings(Path.Combine(directory, "broken.xml"), "<TesterSettings version=\"1\"><Port>");
            CheckDamagedSettings(Path.Combine(directory, "range.xml"), "<TesterSettings version=\"1\"><Port>0</Port>"
                + "<PresentationDistanceMm>1001</PresentationDistanceMm><TemplateFolder></TemplateFolder><RecentFiles /></TesterSettings>");

            step = "settings-locked";
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var locked = TesterSettings.Load(path);
                Check(locked.LoadWarning != null && locked.Port == TesterSettings.DefaultPort);
            }
        }

        // 손상된 설정은 기본값으로 시작하고, 최근 파일을 갱신해도 원본을 덮어쓰지 않는다.
        private static void CheckDamagedSettings(string path, string content)
        {
            File.WriteAllText(path, content, Encoding.UTF8);
            byte[] original = File.ReadAllBytes(path);
            var settings = TesterSettings.Load(path);
            Check(settings.LoadWarning != null && settings.Port == TesterSettings.DefaultPort
                && settings.PresentationDistanceMm == TesterSettings.DefaultDistanceMm && settings.RecentFiles.Count == 0);
            string recent = Path.Combine(Path.GetDirectoryName(path), "recent.txt");
            settings.AddRecent(recent);
            Check(settings.RecentFiles.Count == 1 && settings.RecentFiles[0] == recent);
            Check(File.ReadAllBytes(path).SequenceEqual(original));
            // 설정 화면에서 확인을 누르면 경고를 풀고 메모리의 최근 목록까지 함께 저장한다.
            settings.Update("2", 30, "");
            var saved = TesterSettings.Load(path);
            Check(settings.LoadWarning == null && saved.LoadWarning == null && saved.Port == "2"
                && saved.PresentationDistanceMm == 30 && saved.RecentFiles.SequenceEqual(new[] { recent }));
        }

        private static void CheckDocument(string directory)
        {
            step = "document-open";
            string path = WriteTemplate(directory, "label.txt", SampleText);
            var document = new TemplateDocument();
            Check(document.Template == null && !document.IsDirty && document.LastLoadedOrSaved == null && document.DisplayPath == "열린 서식 없음");
            Expect<IOException>(() => document.Open(Path.Combine(directory, "missing.txt")));
            Check(document.Template == null);
            document.Open(path);
            Check(document.Template != null && document.Template.FilePath == path && document.DisplayPath == path
                && !document.IsDirty && document.LastLoadedOrSaved != null);
            var opened = document.Template;
            DateTime? openedAt = document.LastLoadedOrSaved;
            Expect<IOException>(() => document.Open(Path.Combine(directory, "missing.txt")));
            Check(ReferenceEquals(document.Template, opened) && document.DisplayPath == path && !document.IsDirty
                && document.LastLoadedOrSaved == openedAt);

            step = "document-apply";
            document.Apply(t => t.WithLabelWidth(70m));
            Check(ReferenceEquals(document.Template, opened) && !document.IsDirty);
            Expect<BtplFormatException>(() => document.Apply(t => t.WithText(0, t.TextElements[0].WithPosition(0, 120))));
            Check(ReferenceEquals(document.Template, opened) && !document.IsDirty);
            CheckBlackMarkAtomic(directory);

            step = "document-revert";
            document.Apply(t => t.WithText(0, t.TextElements[0].WithPosition(80, 140)));
            Check(document.IsDirty);
            document.Apply(t => t.WithText(0, t.TextElements[0].WithPosition(80, 120)));
            Check(!document.IsDirty && document.Template.ToBtpl() == SampleText);
            document.Apply(t => t.WithSetting(new BtplSetting("^Q", "36,5,9+")));
            Check(document.IsDirty);
            document.Apply(t => t.WithSetting(new BtplSetting("^Q", "35,5,9+")));
            Check(!document.IsDirty);
            // 비표준 표기는 값만 되돌려도 DLL이 표준 표기로 써서 원문이 달라지므로 수정됨으로 남는다.
            var padded = new TemplateDocument();
            padded.Open(WriteTemplate(directory, "padded.txt", SampleText.Replace("AW,80,120,", "AW,80,00120,")));
            padded.Apply(t => t.WithText(0, t.TextElements[0].WithPosition(80, 140)));
            padded.Apply(t => t.WithText(0, t.TextElements[0].WithPosition(80, 120)));
            Check(padded.IsDirty);

            step = "document-save";
            Edit(document, "첫 저장");
            Check(document.IsDirty);
            DateTime savedBefore = document.LastLoadedOrSaved.Value;
            document.Save();
            Check(!document.IsDirty && document.LastLoadedOrSaved >= savedBefore && ReadText(path) == document.Template.ToBtpl());
            Edit(document, "연속 저장");
            document.Save();
            Check(!document.IsDirty && ReadText(path) == document.Template.ToBtpl());

            step = "document-save-as";
            string originalText = ReadText(path);
            string copy = Path.Combine(directory, "copy.txt");
            Edit(document, "다른 이름");
            document.SaveAs(copy, false);
            Check(!document.IsDirty && document.DisplayPath == copy && ReadText(copy) == document.Template.ToBtpl());
            Edit(document, "새 경로");
            document.Save();
            Check(ReadText(copy) == document.Template.ToBtpl() && ReadText(path) == originalText);
            Expect<BtplFormatException>(() => document.SaveAs(copy, false));

            step = "document-conflict";
            CheckSaveFailuresKeepState(document, directory, copy);

            step = "document-close";
            Edit(document, "닫기 전");
            document.Close();
            Check(document.Template == null && !document.IsDirty && document.LastLoadedOrSaved == null && document.DisplayPath == "열린 서식 없음");
            document.Open(path);
            Check(document.Template != null && document.DisplayPath == path && !document.IsDirty);
        }

        // 저장이 실패하면 편집본·수정 표시·디스크 내용이 모두 그대로여야 한다.
        private static void CheckSaveFailuresKeepState(TemplateDocument document, string directory, string copy)
        {
            string external = SampleText.Replace("^S5", "^S6");
            File.WriteAllText(copy, external, Cp949);
            string before = document.Template.TextElements[1].Payload;
            Edit(document, "충돌 편집");
            var edited = document.Template;
            Expect<BtplSaveConflictException>(() => document.Save());
            Check(ReferenceEquals(document.Template, edited) && document.IsDirty && ReadText(copy) == external);

            string other = WriteTemplate(directory, "other.txt", SampleText);
            Expect<IOException>(() => document.SaveAs(other, false));
            Check(ReferenceEquals(document.Template, edited) && document.IsDirty && document.DisplayPath == copy && ReadText(other) == SampleText);
            // 실패한 저장은 비교 기준을 바꾸지 않으므로 되돌리면 수정됨이 아니다.
            document.Apply(t => t.WithText(1, t.TextElements[1].WithPayload(before)));
            Check(!document.IsDirty);

            step = "document-reload";
            document.Reload();
            Check(!document.IsDirty && document.Template.ToBtpl() == external && document.DisplayPath == copy);

            step = "document-overwrite";
            Edit(document, "덮어쓰기");
            document.SaveAs(other, true);
            Check(!document.IsDirty && document.DisplayPath == other && ReadText(other) == document.Template.ToBtpl()
                && ReadText(copy) == external);
        }

        // 블랙마크 반영은 Q와 W를 한 번에 바꾼다. W가 모호하면 Q도 바뀌지 않아야 한다.
        private static void CheckBlackMarkAtomic(string directory)
        {
            string path = WriteTemplate(directory, "two-width.txt", SampleText.Replace("^W70\r\n", "^W70\r\n^W70\r\n"));
            var document = new TemplateDocument();
            document.Open(path);
            var opened = document.Template;
            Expect<BtplFormatException>(() => document.Apply(t => t
                .WithSetting(new BtplSetting("^Q", "36,5,9+")).WithSetting(new BtplSetting("^W", "60"))));
            Check(ReferenceEquals(document.Template, opened) && !document.IsDirty && document.Template.ToBtpl().Contains("^Q35,5,9+"));
        }

        private static void CheckPreview()
        {
            step = "preview";
            string variableData = "FIRST LINE!!SECOND LINE";
            Check(TemplateDocument.PreviewText("^V00", variableData) == "FIRST LINE");
            Check(TemplateDocument.PreviewText("^V00/^V01/^V02/^V03", "A!!!!C!!") == "A//C/");
            Check(TemplateDocument.PreviewText("^V00", "^V01!!SAFE") == "^V01");
            Check(TemplateDocument.PreviewText("^V02", variableData) == "^V02");
            Check(TemplateDocument.PreviewText("^V00", null) == "^V00");
        }

        // 계산값 비교는 DLL에 적용해 보기만 한다. 영역 판정은 운영 기준 용지의 SafeArea(X 71~732, Y 66~255)로 확인한다.
        private static void CheckMedia()
        {
            step = "media";
            var media = new BtplBlackMarkMedia(70m, 35m, 5m, 7.5m, 60m, 20m);
            BtplTemplate sample = BtplTemplate.Parse(SampleText);
            Check(TemplateDocument.SameMedia(sample, media));
            Check(!TemplateDocument.SameMedia(BtplTemplate.Parse(SampleText.Replace("^W70", "^W60")), media));
            Check(!TemplateDocument.SameMedia(BtplTemplate.Parse(SampleText.Replace("^W70\r\n", "")), media));
            Check(!TemplateDocument.SameMedia(BtplTemplate.Parse(SampleText.Replace("^Q35,5,9+", "^Q35,5")), media));
            try
            {
                TemplateDocument.SameMedia(BtplTemplate.Parse(SampleText.Replace("^W70\r\n", "^W70\r\n^W70\r\n")), media);
                Check(false);
            }
            catch (BtplFormatException error)
            {
                Check(error.Code == "AmbiguousSetting");
            }

            step = "safe-area";
            BtplArea safe = media.SafeArea;
            Check(safe.Left == 71 && safe.Top == 66 && safe.Right == 732 && safe.Bottom == 255);
            BtplTemplate edge = BtplTemplate.Parse(SampleText.Replace("AW,80,120,", "AW,80,208,").Replace("AW,80,160,", "AW,80,209,"));
            Check(TemplateDocument.OutsideSafeArea(edge.TextElements[0], safe, 100) == null);
            Check(TemplateDocument.OutsideSafeArea(edge.TextElements[1], safe, 100) == "아래 1 dot");
            BtplTemplate far = BtplTemplate.Parse(SampleText.Replace("AW,80,120,", "AW,80,260,").Replace("AW,80,160,10,", "AW,70,65,10,"));
            Check(TemplateDocument.OutsideSafeArea(far.TextElements[0], safe, 100) == "아래 52 dot");
            Check(TemplateDocument.OutsideSafeArea(far.TextElements[1], safe, 663) == "위 1 dot, 왼쪽 1 dot, 오른쪽 1 dot 추정");
            // 경고 거리는 그리기 제한(100000) 전 높이로 구한다.
            BtplTemplate huge = BtplTemplate.Parse(SampleText.Replace("AW,80,120,10,", "AW,80,66,30000,"));
            Check(TemplateDocument.OutsideSafeArea(huge.TextElements[0], safe, 100) == "아래 141477 dot");
            BtplTemplate turned = BtplTemplate.Parse(SampleText.Replace("AW,80,120,10,1,0,0,", "AW,80,120,10,1,1,0,")
                .Replace("AW,80,160,10,1,0,0,", "AW,80,160,10,1,0,1,"));
            Check(TemplateDocument.CanJudge(sample.TextElements[0]) && !TemplateDocument.CanJudge(turned.TextElements[0])
                && !TemplateDocument.CanJudge(turned.TextElements[1]));
            // 정렬 줄(6)은 영역 밖 위치여도 제외 목록에만, 0/0 줄(7)은 영역 밖 목록에만 들어간다.
            BtplTemplate mixed = BtplTemplate.Parse(SampleText.Replace("AW,80,120,10,1,0,0,", "AW,80,260,10,1,1,0,")
                .Replace("AW,80,160,", "AW,80,260,"));
            var outside = new List<string>();
            var skipped = new List<string>();
            TemplateDocument.CheckSafeArea(mixed, safe, text => 100, outside, skipped);
            Check(outside.SequenceEqual(new[] { "줄 7(아래 52 dot)" }) && skipped.SequenceEqual(new[] { "줄 6" }));
        }

        private static void CheckGapLabel()
        {
            step = "gap-label";
            Check(TemplateDocument.HasGapLabelSetting(BtplTemplate.Parse(SampleText.Replace("^Q35,5,9+", "^Q35,5"))));
            Check(TemplateDocument.HasGapLabelSetting(BtplTemplate.Parse(SampleText.Replace("^Q35,5,9+", " ^Q35,5,9+"))));
            Check(TemplateDocument.HasGapLabelSetting(BtplTemplate.Parse(SampleText.Replace("^Q35,5,9+", "^Q35,5,9+,1"))));
            // 줄 번호는 CRLF·LF·CR 어느 줄끝이든 DLL의 TemplateLine과 같은 기준이어야 한다.
            Check(TemplateDocument.HasGapLabelSetting(BtplTemplate.Parse("^XX1\n ^Q35,5,9+\n^W70\n^L\nE\n")));
            Check(TemplateDocument.HasGapLabelSetting(BtplTemplate.Parse("^XX1\r ^Q35,5,9+\r^W70\r^L\rE\r")));
            Check(TemplateDocument.HasGapLabelSetting(BtplTemplate.Parse("^XX1\r\n^W70\r\n^Q35,5,9+,1\r\n^L\r\nE\r\n")));
            Check(!TemplateDocument.HasGapLabelSetting(BtplTemplate.Parse("^XX1\n^Q35,5,9+\n^W70\n^L\nE\n")));
            Check(!TemplateDocument.HasGapLabelSetting(BtplTemplate.Parse(SampleText)));
            Check(!TemplateDocument.HasGapLabelSetting(BtplTemplate.Parse(SampleText.Replace("^H19", "^H19\r\n^XX1"))));
        }

        // 실행 폴더에는 같은 비트수 SDK 한 쌍이 있지만 이 검사는 로드하지 않고 화면도 만들지 않는다.
        // 레지스트리 대신 합성 목록으로 번호·이름·저장 포트 처리만 확인한다.
        private static void CheckUsbPorts()
        {
            step = "usb-list";
            Func<string, string> names = instance => instance == "noname" ? null : "  " + instance + " ";
            IReadOnlyList<UsbPrinterPort> none = UsbPrinterList.Build(new string[0], names, "0");
            Check(none.Count == 1 && none[0].Port == "0" && !none[0].Detected && none[0].Name == UsbPrinterList.NotDetected
                && UsbPrinterList.Summary(none) == "USB 프린터가 감지되지 않았습니다.");
            IReadOnlyList<UsbPrinterPort> one = UsbPrinterList.Build(new[] { "BT-003 Pro" }, names, "0");
            Check(one.Count == 1 && one[0].Port == "0" && one[0].Detected && one[0].Display == "0 — BT-003 Pro"
                && UsbPrinterList.Summary(one) == "USB 프린터 1대 감지");
            IReadOnlyList<UsbPrinterPort> two = UsbPrinterList.Build(new[] { null, "A", "noname" }, names, "0");
            Check(two.Count == 3 && two[0].Port == "0" && !two[0].Detected && two[1].Port == "1" && two[1].Name == "A"
                && two[2].Port == "2" && two[2].Name == "이름 없음" && UsbPrinterList.Summary(two).Contains("미검증"));
            Check(UsbPrinterList.Build(new[] { "A" }, names, "").Count == 1);
        }

        private static void CheckNoNativeOrUi()
        {
            step = "native";
            foreach (string file in new[] { "BlackfishNfcLib.dll", "HFModule.dll" })
                Check(File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, file)));
            using (var process = Process.GetCurrentProcess())
                foreach (ProcessModule module in process.Modules)
                    Check(!string.Equals(module.ModuleName, "BlackfishNfcLib.dll", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(module.ModuleName, "HFModule.dll", StringComparison.OrdinalIgnoreCase));
            step = "no-ui";
            Check(Application.OpenForms.Count == 0);
        }

        private static void Edit(TemplateDocument document, string payload)
        {
            document.Apply(t => t.WithText(1, t.TextElements[1].WithPayload(payload + " ^V01")));
        }

        private static string WriteTemplate(string directory, string name, string text)
        {
            string path = Path.Combine(directory, name);
            File.WriteAllText(path, text, Cp949);
            return path;
        }

        private static string ReadText(string path) => File.ReadAllText(path, Cp949);

        private static void Check(bool valid)
        {
            if (!valid) throw new InvalidOperationException("Tester self-check failed.");
        }

        private static void Expect<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name + ".");
        }
    }
}
