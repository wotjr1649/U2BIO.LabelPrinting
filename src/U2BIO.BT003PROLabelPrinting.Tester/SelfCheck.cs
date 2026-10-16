using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Xml.Linq;

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
                CheckSettingsWithoutFolder(directory);
                CheckSettings(directory);
                CheckPrintCountAndOpenDirectory(directory);
                CheckDocument(directory);
                CheckMedia();
                CheckRenderer();
                CheckPrintGuard(directory);
                CheckUsbPorts();
                CheckNoNativeOrUi();
                Console.WriteLine("PASS: tester self-check " + (expect64 ? "x64" : "x86")
                    + " settings/document/media/ink-area/renderer/print-check/print-file-lock/usb-list; nativeSDK=not-loaded; no UI/device");
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

        private static void CheckSettingsWithoutFolder(string directory)
        {
            step = "settings-without-folder-preserves-calibration";
            string path = Path.Combine(directory, "without-folder.xml");
            new XDocument(new XElement("TesterSettings", new XAttribute("version", "1"),
                new XElement("Port", "7"), new XElement("PresentationDistanceMm", "23"),
                new XElement("RecentFiles", new XElement("File", Path.Combine(directory, "recent.txt"))),
                new XElement("Dpi", "300"), new XElement("SensorOffsetMm", "1.5"),
                new XElement("VerticalOffsetMm", "4.47"), new XElement("HorizontalOffsetMm", "-2.06"),
                new XElement("LabelMediaMode", "Gap"))).Save(path);
            byte[] original = File.ReadAllBytes(path);
            var settings = TesterSettings.Load(path);
            Check(settings.LoadWarning == null && settings.Port == "7" && settings.PresentationDistanceMm == 23
                && settings.Calibration.VerticalOffsetMm == 4.47m && settings.Calibration.HorizontalOffsetMm == -2.06m
                && settings.LabelMediaMode == BtplLabelMode.Gap && settings.RecentFiles.Count == 1
                && original.SequenceEqual(File.ReadAllBytes(path)));
        }

        private static void CheckPrintCountAndOpenDirectory(string directory)
        {
            step = "print-count-and-open-directory";
            Check(FrmMain.ReadPrintCount(1m) == 1 && FrmMain.ReadPrintCount(20) == 20);
            foreach (object value in new object[] { null, "", "wrong", 0, 21, 1.5m, decimal.MaxValue })
                Expect<ArgumentException>(() => FrmMain.ReadPrintCount(value));
            string recent = WriteTemplate(directory, "recent-open.txt", SampleText);
            string currentDirectory = Path.Combine(directory, "current");
            Directory.CreateDirectory(currentDirectory);
            Check(FrmMain.OpenDirectory(Path.Combine(currentDirectory, "gone.txt"), new[] { recent }) == currentDirectory);
            Check(FrmMain.OpenDirectory(null, new[] { Path.Combine(directory, "missing.txt"), recent }) == directory);
            Check(FrmMain.OpenDirectory(null, new string[0]) == null);
        }

        private static void CheckSettings(string directory)
        {
            step = "settings-default";
            string path = Path.Combine(directory, "settings", "settings.xml");
            var settings = TesterSettings.Load(path);
            Check(settings.LoadWarning == null && File.Exists(path) && settings.FilePath == path);
            Check(settings.Port == TesterSettings.DefaultPort && settings.PresentationDistanceMm == TesterSettings.DefaultDistanceMm
                && settings.RecentFiles.Count == 0);

            step = "settings-validation";
            Check(TesterSettings.CheckPort("") != null && TesterSettings.CheckPort("  ") != null
                && TesterSettings.CheckPort("0\t") != null && TesterSettings.CheckPort("0") == null);
            Check(TesterSettings.CheckDistance(-1) != null && TesterSettings.CheckDistance(1001) != null
                && TesterSettings.CheckDistance(0) == null && TesterSettings.CheckDistance(1000) == null);
            Expect<ArgumentException>(() => settings.Update("", 20));
            Expect<ArgumentException>(() => settings.Update("1", 1001));
            Check(settings.Port == TesterSettings.DefaultPort && settings.PresentationDistanceMm == TesterSettings.DefaultDistanceMm);

            step = "settings-roundtrip";
            settings.Update("1", 20);
            for (int index = 0; index < TesterSettings.MaxRecentFiles + 2; index++)
                settings.AddRecent(Path.Combine(directory, "label" + index + ".txt"));
            settings.AddRecent(Path.Combine(directory, "LABEL5.TXT"));
            Check(settings.RecentFiles.Count == TesterSettings.MaxRecentFiles
                && settings.RecentFiles[0] == Path.Combine(directory, "LABEL5.TXT")
                && settings.RecentFiles.Count(file => string.Equals(file, Path.Combine(directory, "label5.txt"), StringComparison.OrdinalIgnoreCase)) == 1
                && settings.RecentFiles[1] == Path.Combine(directory, "label11.txt"));
            var reloaded = TesterSettings.Load(path);
            Check(reloaded.LoadWarning == null && reloaded.Port == "1" && reloaded.PresentationDistanceMm == 20
                && reloaded.RecentFiles.SequenceEqual(settings.RecentFiles));

            step = "settings-calibration";
            Check(reloaded.Calibration.Dpi == 300 && reloaded.Calibration.SensorOffsetMm == 1.5m
                && reloaded.Calibration.VerticalOffsetMm == 3.6m && reloaded.Calibration.HorizontalOffsetMm == -1.0m);
            settings.Update("1", 20, new PrinterCalibration(203, -2.125m, 0m, 1000m));
            PrinterCalibration saved = TesterSettings.Load(path).Calibration;
            Check(saved.Dpi == 203 && saved.SensorOffsetMm == -2.125m && saved.VerticalOffsetMm == 0m && saved.HorizontalOffsetMm == 1000m);
            // 세로 보정이 음수면 DLL이 항상 거부하므로 설정에서 막는다.
            Expect<ArgumentException>(() => settings.Update("1", 20, new PrinterCalibration(300, 1.5m, -0.5m, 0m)));
            Expect<ArgumentException>(() => settings.Update("1", 20, new PrinterCalibration(200, 1.5m, 1m, 0m)));
            Expect<ArgumentException>(() => settings.Update("1", 20, new PrinterCalibration(300, 1.0005m, 1m, 0m)));
            Check(TesterSettings.Load(path).Calibration.Dpi == 203);

            step = "settings-media-mode";
            Check(TesterSettings.Load(path).LabelMediaMode == BtplLabelMode.BlackMark);
            settings.Update("1", 20, null, BtplLabelMode.Gap);
            Check(TesterSettings.Load(path).LabelMediaMode == BtplLabelMode.Gap && TesterSettings.Load(path).Calibration.Dpi == 203);
            Expect<ArgumentException>(() => settings.Update("1", 20, null, BtplLabelMode.Ambiguous));
            Expect<ArgumentException>(() => settings.Update("1", 20, null, BtplLabelMode.Unspecified));
            Check(TesterSettings.Load(path).LabelMediaMode == BtplLabelMode.Gap);
            settings.Update("1", 20, null, BtplLabelMode.BlackMark);
            // 보정 항목이 없는 이전 파일은 읽기만 해서는 다시 쓰지 않고, 최근 파일을 더하면 보정 기본값과 함께 저장한다.
            string legacy = Path.Combine(directory, "legacy.xml");
            File.WriteAllText(legacy, "<TesterSettings version=\"1\"><Port>0</Port><PresentationDistanceMm>15</PresentationDistanceMm>"
                + "<TemplateFolder></TemplateFolder><RecentFiles /></TesterSettings>", Encoding.UTF8);
            byte[] legacyBytes = File.ReadAllBytes(legacy);
            var old = TesterSettings.Load(legacy);
            Check(old.LoadWarning == null && old.LabelMediaMode == BtplLabelMode.BlackMark && old.Calibration.Dpi == 300 && old.Calibration.VerticalOffsetMm == 3.6m
                && File.ReadAllBytes(legacy).SequenceEqual(legacyBytes));
            old.AddRecent(Path.Combine(directory, "legacy-recent.txt"));
            var upgraded = TesterSettings.Load(legacy);
            Check(upgraded.LoadWarning == null && upgraded.RecentFiles.Count == 1 && upgraded.Calibration.HorizontalOffsetMm == -1.0m
                && File.ReadAllText(legacy).Contains("<VerticalOffsetMm>3.6</VerticalOffsetMm>")
                && File.ReadAllText(legacy).Contains("<LabelMediaMode>BlackMark</LabelMediaMode>"));

            step = "settings-legacy-folder-preserves-calibration";
            XDocument legacyWithCalibration = XDocument.Load(path);
            legacyWithCalibration.Root.Add(new XElement("TemplateFolder", Path.Combine(directory, "removed-folder")));
            legacyWithCalibration.Save(legacy);
            byte[] beforeRead = File.ReadAllBytes(legacy);
            var withFolder = TesterSettings.Load(legacy);
            Check(withFolder.LoadWarning == null && withFolder.Port == settings.Port
                && withFolder.PresentationDistanceMm == settings.PresentationDistanceMm
                && withFolder.Calibration.HorizontalOffsetMm == settings.Calibration.HorizontalOffsetMm
                && withFolder.RecentFiles.SequenceEqual(settings.RecentFiles)
                && beforeRead.SequenceEqual(File.ReadAllBytes(legacy)));
            withFolder.Update(withFolder.Port, withFolder.PresentationDistanceMm);
            Check(XDocument.Load(legacy).Root.Element("TemplateFolder") == null
                && TesterSettings.Load(legacy).Calibration.HorizontalOffsetMm == settings.Calibration.HorizontalOffsetMm);

            step = "settings-corrupt";
            CheckDamagedSettings(Path.Combine(directory, "broken.xml"), "<TesterSettings version=\"1\"><Port>");
            // 값이 있는데 잘못된 보정은 손상으로 보고 원본을 지킨다.
            CheckDamagedSettings(Path.Combine(directory, "bad-calibration.xml"), "<TesterSettings version=\"1\"><Port>0</Port>"
                + "<PresentationDistanceMm>15</PresentationDistanceMm><TemplateFolder></TemplateFolder><RecentFiles />"
                + "<VerticalOffsetMm>-1</VerticalOffsetMm></TesterSettings>");
            CheckDamagedSettings(Path.Combine(directory, "bad-media.xml"), "<TesterSettings version=\"1\"><Port>0</Port>"
                + "<PresentationDistanceMm>15</PresentationDistanceMm><TemplateFolder></TemplateFolder><RecentFiles />"
                + "<LabelMediaMode>Ambiguous</LabelMediaMode></TesterSettings>");
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
            settings.Update("2", 30);
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
            CheckUndo(directory);
        }

        // 되돌리기 기록은 원문으로 두고 현재 객체의 WithRawText로 되살린다. 저장 뒤 되돌려도 다음 저장이 막히지 않아야 한다.
        private static void CheckUndo(string directory)
        {
            step = "document-undo";
            string path = WriteTemplate(directory, "undo.txt", SampleText);
            var document = new TemplateDocument();
            document.Open(path);
            Check(!document.CanUndo && !document.CanRedo && !document.DiffersFromSaved && !document.DiffersFromOpened);
            Edit(document, "하나");
            string first = document.Template.ToBtpl();
            Edit(document, "둘");
            Check(document.CanUndo && document.IsDirty);
            document.Undo();
            Check(document.Template.ToBtpl() == first && document.CanRedo);
            document.Undo();
            Check(document.Template.ToBtpl() == SampleText && !document.IsDirty && !document.CanUndo);
            document.Redo();
            Check(document.Template.ToBtpl() == first && document.IsDirty);
            // 새 편집은 다시 실행 기록을 비우고, 원문이 같은 편집은 기록하지 않는다.
            Edit(document, "셋");
            Check(!document.CanRedo);
            int before = document.UndoCount;
            document.Apply(t => t.WithRawText(t.ToBtpl()));
            Check(document.UndoCount == before);

            step = "document-undo-save";
            document.Save();
            Check(!document.IsDirty && document.CanUndo && document.DiffersFromOpened);
            document.Undo();
            Check(document.IsDirty);
            document.Save();
            Check(!document.IsDirty && ReadText(path) == document.Template.ToBtpl());
            Edit(document, "넷");
            document.RevertToSaved();
            Check(!document.IsDirty && document.Template.ToBtpl() == ReadText(path));
            document.Undo();
            Check(document.Template.ToBtpl().Contains("넷"));
            document.RevertToOpened();
            Check(document.Template.ToBtpl() == SampleText && document.IsDirty && !document.DiffersFromOpened);
            document.Save();
            Check(!document.IsDirty && ReadText(path) == SampleText);

            step = "document-undo-limit";
            for (int index = 0; index < 101; index++) Edit(document, "반복 " + index);
            Check(document.UndoCount == 100);
            document.Close();
            Check(!document.CanUndo && !document.CanRedo);
            // 다시 열면 기록을 비우고 처음 연 원문을 새로 잡는다.
            document.Open(path);
            Edit(document, "다시");
            document.Reload();
            Check(!document.CanUndo && !document.DiffersFromOpened);

            step = "document-undo-gap";
            // 처음 연 원문에 두 인수 ^Q가 있으면 DLL이 원문 복원을 거부하고 상태는 그대로다.
            string gap = WriteTemplate(directory, "undo-gap.txt", SampleText.Replace("^Q35,5,9+", "^Q35,5"));
            var gapDocument = new TemplateDocument();
            gapDocument.Open(gap);
            gapDocument.Apply(t => t.WithSetting(new BtplSetting("^Q", "35,5,9+")));
            BtplTemplate fixedTemplate = gapDocument.Template;
            Expect<BtplFormatException>(() => gapDocument.RevertToOpened());
            Expect<BtplFormatException>(() => gapDocument.Undo());
            Check(ReferenceEquals(gapDocument.Template, fixedTemplate) && gapDocument.CanUndo && gapDocument.IsDirty);
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

        // 블랙마크 칸 연결은 DLL에 적용해 보기만 한다. 영역 판정은 운영 기준 용지의 LabelArea(Y 43~279)·SafeArea(Y 66~255)와 잉크 범위로 확인한다.
        private static void CheckMedia()
        {
            step = "media";
            var media = new BtplBlackMarkMedia(70m, 35m, 5m, 7.5m, 60m, 20m);
            BtplTemplate sample = BtplTemplate.Parse(SampleText);
            Check(TemplateDocument.SameSetting(sample, media.LabelSetting) && TemplateDocument.SameSetting(sample, media.WidthSetting));
            Check(!TemplateDocument.SameSetting(BtplTemplate.Parse(SampleText.Replace("^W70", "^W60")), media.WidthSetting));
            Check(!TemplateDocument.SameSetting(BtplTemplate.Parse(SampleText.Replace("^W70\r\n", "")), media.WidthSetting));
            // 방향 −, 0.5 mm 단위가 아닌 위치, 두 인수 ^Q는 계산기로 그대로 만들 수 없어 연결하지 않는다.
            foreach (string q in new[] { "^Q35,5,9-", "^Q35,5,9.2+", "^Q35,5" })
                Check(!TemplateDocument.SameSetting(BtplTemplate.Parse(SampleText.Replace("^Q35,5,9+", q)), media.LabelSetting));
            Check(new BtplBlackMarkMedia(70m, 35m, 5m, 7.7m, 60m, 20m).LabelSetting.Parameters == "35,5,9+");
            Check(TemplateDocument.TryReadLabel("35,5,9+", out decimal segment, out decimal mark, out decimal z) && segment == 35 && mark == 5 && z == 9);
            Check(!TemplateDocument.TryReadLabel("35,5,9-", out _, out _, out _) && !TemplateDocument.TryReadLabel("35,5", out _, out _, out _)
                && !TemplateDocument.TryReadLabel(null, out _, out _, out _));
            Check(TemplateDocument.TryReadWidth("70", out decimal liner) && liner == 70 && !TemplateDocument.TryReadWidth("7O", out _)
                && !TemplateDocument.TryReadWidth(null, out _));
            try
            {
                TemplateDocument.SameSetting(BtplTemplate.Parse(SampleText.Replace("^W70\r\n", "^W70\r\n^W70\r\n")), media.WidthSetting);
                Check(false);
            }
            catch (BtplFormatException error)
            {
                Check(error.Code == "AmbiguousSetting");
            }

            step = "ink-area";
            BtplArea label = media.LabelArea;
            BtplArea safe = media.SafeArea;
            Check(label.Top == 43 && label.Bottom == 279 && safe.Left == 71 && safe.Top == 66 && safe.Right == 732 && safe.Bottom == 255);
            // 이 PC 돋움체 크기 10 "Not Used"의 잉크 예: 로컬 [3,189)×[6,40).
            Rectangle ink = Rectangle.FromLTRB(3, 6, 189, 40);
            Check(TemplateDocument.Outside(ink, 80, 215, safe) == null && TemplateDocument.Outside(ink, 80, 216, safe) == "아래 1 dot");
            Check(TemplateDocument.Outside(ink, 80, 239, label) == null && TemplateDocument.Outside(ink, 80, 240, label) == "아래 1 dot");
            Check(TemplateDocument.Outside(ink, 80, 260, label) == "아래 21 dot");
            Check(TemplateDocument.Outside(ink, 67, 59, safe) == "위 1 dot, 왼쪽 1 dot" && TemplateDocument.Outside(ink, 544, 100, safe) == "오른쪽 1 dot");
            var raster = new TextRaster(null, ink, 0, null);
            BtplTemplate low = BtplTemplate.Parse(SampleText.Replace("AW,80,120,", "AW,80,260,").Replace("AW,80,160,", "AW,80,230,"));
            Check(TemplateDocument.Classify(low.TextElements[0], raster, label, safe, out string detail) == InkState.Cut && detail == "아래 21 dot");
            Check(TemplateDocument.Classify(low.TextElements[1], raster, label, safe, out detail) == InkState.Unsafe && detail == "아래 15 dot");
            Check(TemplateDocument.Classify(sample.TextElements[0], raster, label, safe, out detail) == InkState.Inside && detail == null);
            Check(TemplateDocument.Classify(sample.TextElements[0], new TextRaster(null, null, 0, null), label, safe, out _) == InkState.Blank);
            Check(TemplateDocument.Classify(sample.TextElements[0], new TextRaster(null, null, 0, "크기 한도 초과"), label, safe, out detail) == InkState.Failed
                && detail == "크기 한도 초과");
            // 정렬 줄(6)은 영역 밖 위치여도 제외 목록에만, 0/0 줄(7)은 안전 영역 밖 목록에만 들어간다.
            BtplTemplate mixed = BtplTemplate.Parse(SampleText.Replace("AW,80,120,10,1,0,0,", "AW,80,260,10,1,1,0,").Replace("AW,80,160,", "AW,80,230,"));
            var cut = new List<string>();
            var unsafeLines = new List<string>();
            var skipped = new List<string>();
            var failed = new List<string>();
            TemplateDocument.CheckInk(mixed, label, safe, text => raster, cut, unsafeLines, skipped, failed);
            Check(cut.Count == 0 && unsafeLines.SequenceEqual(new[] { "줄 7(아래 15 dot)" }) && skipped.SequenceEqual(new[] { "줄 6" }) && failed.Count == 0);

            step = "preview-guide";
            // 눈금 숫자 간격은 화면 간격이 필요한 픽셀 이상이 될 때까지 두 배로 늘린다.
            Check(TemplateDocument.RulerStep(40, 1f, 11f) == 40 && TemplateDocument.RulerStep(40, 0.25f, 11f) == 80
                && TemplateDocument.RulerStep(40, 0.027f, 11f) == 640 && TemplateDocument.RulerStep(100, 0.00001f, 30f) == 1 << 20);
            // 경계값 배치: 겹치면 아래로 밀고, 아래 끝을 넘으면 위로 되돌리며, 높이가 모자라면 생략한다.
            float[] spread = TemplateDocument.StackLabels(new[] { 10f, 12f, 100f, 103f }, 9f, 0f, 200f);
            Check(spread.SequenceEqual(new[] { 10f, 19f, 100f, 109f }));
            float[] bottom = TemplateDocument.StackLabels(new[] { 255f, 255f, 260f, 260f }, 9f, 0f, 266f);
            Check(bottom.SequenceEqual(new[] { 230f, 239f, 248f, 257f }));
            Check(TemplateDocument.StackLabels(new[] { 0f, 0f, 0f, 0f }, 9f, 0f, 30f) == null);
            // 선택 줄 설명: 잉크 있음, 공백(그림 있고 잉크 없음), 빈 문구(그림 없음), 판정 불가, 정렬 줄.
            using (var image = new Bitmap(192, 47))
            {
                Check(TemplateDocument.DescribeText(low.TextElements[0], new TextRaster(image, ink, 0, null))
                    == "줄 6 · X80 Y260 · 셀 Y260~307 · 그림 폭 192 · 잉크 Y266~300, X83~269");
                Check(TemplateDocument.DescribeText(low.TextElements[0], new TextRaster(image, null, 0, null))
                    == "줄 6 · X80 Y260 · 셀 Y260~307 · 그림 폭 192 · 잉크 없음");
                Check(TemplateDocument.DescribeText(mixed.TextElements[0], new TextRaster(image, ink, 0, null)).EndsWith(" (정렬·회전 미반영 참고값)"));
            }
            Check(TemplateDocument.DescribeText(low.TextElements[0], new TextRaster(null, null, 0, null)) == "줄 6 · X80 Y260 · 잉크 없음(빈 문구)");
            Check(TemplateDocument.DescribeText(low.TextElements[0], new TextRaster(null, null, 0, "크기 한도 초과")) == "줄 6 · X80 Y260 · 판정 불가: 크기 한도 초과");
            Check(TemplateDocument.DescribeText(low.TextElements[0], null) == "줄 6 · X80 Y260 · 판정 불가: 그림 없음");
        }

        // 화면 없이 SDK와 같은 GDI 경로로 그림을 만든다. 글꼴이 PC마다 다를 수 있어 잉크 수치 대신 성공·실패와 자원만 확인한다.
        private static void CheckRenderer()
        {
            step = "renderer";
            string face = SdkTextRenderer.DefaultFace;
            using (TextRaster mixed = SdkTextRenderer.Render(face, 10, 1, "Ag 한글 280", 0))
            {
                Check(mixed.Failure == null && mixed.Image != null && mixed.Ink.HasValue && mixed.Bytes > 0 && mixed.Image.Width % 32 == 0
                    && mixed.Ink.Value.Top >= 0 && mixed.Ink.Value.Bottom <= mixed.Image.Height && mixed.Ink.Value.Right <= mixed.Image.Width);
            }
            using (TextRaster blank = SdkTextRenderer.Render(face, 10, 1, "", 0)) Check(blank.Failure == null && blank.Image == null && !blank.Ink.HasValue);
            using (TextRaster spaces = SdkTextRenderer.Render(face, 10, 1, "   ", 0)) Check(spaces.Failure == null && spaces.Image != null && !spaces.Ink.HasValue);
            using (TextRaster foreign = SdkTextRenderer.Render(face, 10, 1, "Ā", 0)) Check(foreign.Failure == "CP949로 바꿀 수 없음");
            using (TextRaster huge = SdkTextRenderer.Render(face, 30000, 1, "A", 0)) Check(huge.Failure == "크기 한도 초과");
            using (TextRaster full = SdkTextRenderer.Render(face, 10, 1, "A", SdkTextRenderer.BudgetBytes)) Check(full.Failure == SdkTextRenderer.BudgetFailure);
            // 없는 글꼴은 GDI가 다른 글꼴로 바꾸므로 실현 이름이 요청과 달라야 범례가 대체를 알릴 수 있다.
            string realized = SdkTextRenderer.RealizedFace("U2BIO 없는 글꼴");
            Check(!string.IsNullOrEmpty(realized) && realized != "U2BIO 없는 글꼴" && !string.IsNullOrEmpty(SdkTextRenderer.RealizedFace(face)));
            Check(SdkTextRenderer.Fits(1024, 0) && !SdkTextRenderer.Fits(SdkTextRenderer.BudgetBytes / 2 + 1, 0)
                && !SdkTextRenderer.Fits(1024, SdkTextRenderer.BudgetBytes - 1024));
            Check(SdkTextRenderer.Weight(0) == 400 && SdkTextRenderer.Weight(1) == 400 && SdkTextRenderer.Weight(2) == 600 && SdkTextRenderer.Weight(3) == 700);

            step = "renderer-resources";
            using (Process process = Process.GetCurrentProcess())
            {
                uint before = GetGuiResources(process.Handle, 0);
                for (int index = 0; index < 200; index++)
                    using (TextRaster made = SdkTextRenderer.Render(face, 10, 2, "자원 " + index, 0)) Check(made.Failure == null);
                Check(GetGuiResources(process.Handle, 0) <= before + 2);
            }
        }

        [DllImport("user32.dll")]
        private static extern uint GetGuiResources(IntPtr process, uint flags);

        // 인쇄 전 검사: X가 1 이상 정수가 아닌 AW는 확인으로도 넘기지 않고, 라벨 방식이 설정 용지와 다르거나 정할 수 없으면 확인 문구를 낸다.
        private static void CheckPrintGuard(string directory)
        {
            step = "print-check";
            string gap = SampleText.Replace("^Q35,5,9+", "^Q35,5");
            Check(Block(SampleText) == null && Warn(SampleText, BtplLabelMode.BlackMark) == null && Warn(SampleText, BtplLabelMode.Gap) != null);
            Check(Block(gap) == null && Warn(gap, BtplLabelMode.Gap) == null && Warn(gap, BtplLabelMode.BlackMark).Contains("^Q 줄 1"));
            // ^Q 없음·앞 공백·네 인수는 어느 용지 방식이든 확인을 받는다. 줄끝이 CRLF·LF·CR 어느 것이든 같다.
            foreach (string text in new[] { SampleText.Replace("^Q35,5,9+\r\n", ""), SampleText.Replace("^Q35,5,9+", " ^Q35,5,9+"),
                SampleText.Replace("^Q35,5,9+", "^Q35,5,9+,1"), "^XX1\n ^Q35,5,9+\n^W70\n^L\nE\n", "^XX1\r ^Q35,5,9+\r^W70\r^L\rE\r" })
                Check(Block(text) == null && Warn(text, BtplLabelMode.BlackMark) != null && Warn(text, BtplLabelMode.Gap) != null);
            Check(Warn("^XX1\n^Q35,5,9+\n^W70\n^L\nE\n", BtplLabelMode.BlackMark) == null);
            // 기존 파일의 AW X 0은 읽을 수 있지만 SDK 예외를 내므로 간격 서식이어도 막는다(해석된 줄과 해석하지 못한 줄 모두).
            Check(Block(SampleText.Replace("AW,80,120,", "AW,0,120,")).Contains("줄 6"));
            Check(Block(gap.Replace("AW,80,120,", "AW, 0,120,")).Contains("줄 6"));

            step = "print-file-lock";
            // 다시 읽기부터 인쇄 호출이 끝날 때까지 파일 쓰기·교체를 막고, 끝나거나 예외가 나면 잠금을 푼다.
            string path = WriteTemplate(directory, "print-lock.txt", SampleText);
            string confirmed = BtplTemplate.Load(path).ToBtpl();
            bool blocked = TemplateDocument.WithConfirmedFile(path, confirmed, () =>
            {
                try
                {
                    File.WriteAllText(path, gap, Cp949);
                    return false;
                }
                catch (IOException)
                {
                    return true;
                }
            });
            Check(blocked && ReadText(path) == SampleText);
            // 확인 뒤 바뀐 파일은 인쇄하지 않는다.
            File.WriteAllText(path, gap, Cp949);
            bool printed = false;
            Expect<IOException>(() => TemplateDocument.WithConfirmedFile(path, confirmed, () => printed = true));
            Check(!printed);
            File.WriteAllText(path, SampleText, Cp949);
            Check(TemplateDocument.WithConfirmedFile(path, confirmed, () => true) && ReadText(path) == SampleText);
        }

        private static string Block(string text) => TemplateDocument.PrintBlock(BtplTemplate.Parse(text));

        private static string Warn(string text, BtplLabelMode media) => TemplateDocument.LabelModeWarning(BtplTemplate.Parse(text), media);

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
