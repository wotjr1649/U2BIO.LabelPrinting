using System;
using System.IO;
using System.Linq;
using System.Text;
using U2BIO.BT003PROLabelPrinting;

namespace U2BIO.BT003PROLabelPrinting.Validation
{
    // 실제 관리 DLL의 TXT 계약이다. 제조사 DLL을 로드하거나 프린터를 호출하지 않는다.
    internal static class TxtEditorTests
    {
        private const string Raw = "^L\r\nAW,0080,00120,+10,01,00,0,  가나다,^V03!!  \nUNKNOWN,keep\n\rE";
        private static void Check(bool value, string contract)
        {
            if (!value) throw new InvalidOperationException("TXT editor: " + contract);
        }

        private static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new InvalidOperationException("Expected " + typeof(T).Name);
        }

        private static void Format(string code, Action action)
        {
            try { action(); }
            catch (BtplFormatException error)
            {
                Check(error.Code == code && !error.Message.Contains("PRIVATE"), "error contract");
                return;
            }
            throw new InvalidOperationException("Expected editor error " + code);
        }

        internal static void Run()
        {
            SourceAndEdits();
            FramesAndSettings();
            Files();
            RemovedApi();
            BlackMarkMedia();
            LabelModeAndPrintability();
            PreviewText();
            Console.WriteLine("PASS: TXT source/field preservation, named AW edits, frame limits, file origin and Save/SaveAs, label mode, preview text; nativeSDK=not-loaded");
        }

        private static void SourceAndEdits()
        {
            foreach (string source in new[] { Raw, "", "\r\n\n\r", "UNKNOWN", "^L\nE\n", "^L\rAW,1,2,10,1,0,0,x\rE" })
                Check(BtplTemplate.Parse(source).ToBtpl() == source, "exact source round trip");

            var original = BtplTemplate.Parse(Raw);
            var text = original.TextElements[0];
            Check(text.X == 80 && text.Y == 120 && text.Size == 10 && text.Weight == 1, "named AW fields");
            Check(text.TemplateLine == 2 && original.UnsupportedLineNumbers.SequenceEqual(new[] { 3 }), "source lines");
            Check(text.Payload == "  가나다,^V03!!  ", "entire payload");
            Check(original.WithText(0, text.WithPosition(80, 120).WithSize(10).WithWeight(1)).ToBtpl() == Raw, "same numeric values keep literals");
            var moved = original.WithText(0, text.WithPosition(80, 140).WithPayload(" next,^V01!! "));
            Check(moved.ToBtpl() == Raw.Replace("00120", "140").Replace("  가나다,^V03!!  ", " next,^V01!! "), "only selected fields change");
            Check(original.ToBtpl() == Raw, "immutable original");
            var styled = original.WithText(0, text.WithSize(12).WithWeight(2));
            Check(styled.ToBtpl() == Raw.Replace("+10,01", "12,2"), "size and weight only");

            var copied = original.AddText(text.WithPosition(100, 200));
            Check(copied.TextElements.Count == 2 && copied.TextElements[1].TemplateLine == 5, "insert before E");
            Check(copied.ToBtpl().EndsWith("AW,100,200,+10,01,00,0,  가나다,^V03!!  \r\nE", StringComparison.Ordinal), "copied raw fields and chosen terminator");
            var removed = copied.RemoveText(0);
            Check(removed.TextElements.Count == 1 && removed.TextElements[0].TemplateLine == 4, "reextract after deletion");
            Check(removed.UnsupportedLineNumbers.SequenceEqual(new[] { 2 }), "unknown line remapped");
            Throws<ArgumentOutOfRangeException>(() => removed.RemoveText(1));
            Throws<ArgumentOutOfRangeException>(() => original.WithText(-1, text));
            Format("InvalidText", () => text.WithPayload("PRIVATE\nE"));
            Format("InvalidArguments", () => text.WithSize(0));
            Format("InvalidArguments", () => text.WithSize(int.MaxValue));
            Format("InvalidArguments", () => text.WithWeight(4));
            Format("InvalidArguments", () => new BtplTextElement(-1, 0, 10, 1, "x"));
            // 실기에서 AW X=0은 SDK StartLabelPrint 예외를 냈다. 편집으로 만드는 X는 1 이상이며 Y=0은 허용한다.
            Format("InvalidArguments", () => new BtplTextElement(0, 5, 10, 1, "x"));
            Format("InvalidArguments", () => text.WithPosition(0, 120));
            Check(new BtplTextElement(1, 0, 10, 1, "x").X == 1 && text.WithPosition(1, 0).Y == 0, "X one and Y zero allowed");
            Format("InvalidArguments", () => original.WithRawText("^L\nAW,0,10,10,1,0,0,x\nE"));
            // 숫자로 해석하지 못한 AW 줄도 펌웨어가 X를 0으로 읽을 수 있으므로 X가 1 이상 정수가 아니면 거부한다.
            Format("InvalidArguments", () => original.WithRawText("^L\nAW, 0,10,10,1,0,0,x\nE"));
            Format("InvalidArguments", () => original.WithRawText("^L\nAW,x,10,10,1,0,0,x\nE"));
            Check(original.WithRawText("^L\nAW, 1,10,10,1,0,0,x\nE").UnsupportedLineNumbers.Contains(2), "unparsed AW with X one stays raw");
            var protectedText = BtplTemplate.Parse("^L\nAW,1,2,10,1,1,3,x\nE");
            Format("LockedTextArguments", () => protectedText.WithText(0, new BtplTextElement(1, 2, 10, 1, "changed")));
            Check(protectedText.WithText(0, protectedText.TextElements[0].WithSize(12)).ToBtpl().Contains(",12,1,1,3,x"), "direction survives edits");

            var unusual = BtplTemplate.Parse("^L\nAW,-1,+2,-10,9,-3,+4,raw\nAW,x,2,10,1,0,0,invalid\nAC,1,2,1,1,0,0,other\nE");
            Check(unusual.TextElements.Count == 1 && unusual.UnsupportedLineNumbers.SequenceEqual(new[] { 3, 4 }), "opaque malformed and non-AW lines");
            Check(unusual.WithText(0, unusual.TextElements[0].WithPayload("kept")).ToBtpl().Contains("AW,-1,+2,-10,9,-3,+4,kept"), "unmodified raw values not clamped");
            var refreshed = original.WithRawText("^L\nAW,9,8,12,2,0,0,new\nE");
            Check(refreshed.TextElements[0].X == 9 && refreshed.UnsupportedLineNumbers.Count == 0, "raw edit reextracts");
        }

        private static void FramesAndSettings()
        {
            var minimal = BtplTemplate.Parse("^L\nAW,1,2,10,1,0,0,^V00\nE");
            Check(minimal.Settings.Count == 0 && !minimal.ToBtpl().Contains("^P1"), "no required settings or defaults");
            var added = minimal.AddText(new BtplTextElement(3, 4, 12, 2, "^V01"));
            Check(added.ToBtpl().EndsWith("AW,3,4,12,2,0,0,^V01\nE", StringComparison.Ordinal), "new AW");
            var settings = minimal.WithSetting(new BtplSetting("^Q", "040,5,7.5+"))
                .WithSetting(new BtplSetting("^W", "070")).WithSetting(new BtplSetting("^S", "5"))
                .WithSetting(new BtplSetting("^H", "19"));
            Check(settings.WithLabelLength(40m).WithLabelWidth(70m).ToBtpl() == settings.ToBtpl(), "same mm keeps literals");
            var resized = settings.WithLabelLength(45m).WithLabelWidth(60m);
            Check(resized.ToBtpl().Contains("^Q45,5,7.5+\n") && resized.ToBtpl().Contains("^W60\n"), "gap/blackmark preserved");
            Check(resized.RemoveSetting("^W").Settings.Count == 3, "setting deletion");
            var scalar = BtplTemplate.Parse("^W070\n^S05\n^H019\n^L\nE");
            Check(scalar.WithSetting(new BtplSetting("^W", "70"))
                .WithSetting(new BtplSetting("^S", "5")).WithSetting(new BtplSetting("^H", "19")).ToBtpl() == scalar.ToBtpl(), "unchanged scalar settings keep literals");
            // 일반 Q 수정에서도 실제로 바뀐 인수만 교체하고 나머지 원문 표기를 유지한다.
            var q = BtplTemplate.Parse("^Q040,005,07.50+\r\n^L\r\nE");
            Check(q.WithSetting(new BtplSetting("^Q", "40,5,7.5+")).ToBtpl() == q.ToBtpl(), "unchanged Q keeps every literal");
            Check(q.WithSetting(new BtplSetting("^Q", "45,5,7.5+")).ToBtpl() == q.ToBtpl().Replace("040", "45"), "Q length keeps gap and offset literals");
            Check(q.WithSetting(new BtplSetting("^Q", "40,6,7.5+")).ToBtpl() == q.ToBtpl().Replace("005", "6"), "Q gap keeps other literals");
            Check(q.WithSetting(new BtplSetting("^Q", "40,5,7.5-")).ToBtpl() == q.ToBtpl().Replace("07.50+", "7.5-"), "Q direction is an actual change");
            // 두 인수 Q는 프린터 라벨 유형을 Gap으로 바꾸므로 편집으로 만들거나 저장하지 않는다. 기존 원문은 읽고 세 인수로 고칠 수 있다.
            Format("GapLabelSetting", () => new BtplSetting("^Q", "40,5"));
            Format("GapLabelSetting", () => minimal.WithRawText("^Q40,5\n^L\nE"));
            var qWithoutOffset = BtplTemplate.Parse("^Q040,005\n^L\nE");
            Check(qWithoutOffset.Settings.Count == 1 && qWithoutOffset.ToBtpl() == "^Q040,005\n^L\nE", "legacy two-argument Q is readable");
            Format("GapLabelSetting", () => qWithoutOffset.WithLabelLength(45m));
            // 읽어 온 2인수 설정 객체를 다른 서식에 재사용해도 2인수 Q를 만들지 않는다.
            Format("GapLabelSetting", () => q.WithSetting(qWithoutOffset.Settings[0]));
            Format("GapLabelSetting", () => minimal.WithSetting(qWithoutOffset.Settings[0]));
            // 세 인수로 해석되지 않는 Q 변형도 원문 편집으로 넣지 못한다.
            foreach (string variant in new[] { "^Q40,5 ", "^Q40,5,", "^Q40, 5", " ^Q40,5" })
                Format("GapLabelSetting", () => minimal.WithRawText(variant + "\n^L\nE"));
            Check(qWithoutOffset.WithSetting(new BtplSetting("^Q", "40,5,7.5+")).ToBtpl() == "^Q040,005,7.5+\n^L\nE", "Q optional offset insertion");
            Format("MissingSetting", () => minimal.WithLabelLength(40m));
            Format("UnsupportedCommand", () => new BtplSetting("^E", "15"));
            Format("InvalidArguments", () => new BtplSetting("^S", "9"));
            Format("InvalidArguments", () => new BtplSetting("^H", "20"));
            Format("InvalidArguments", () => settings.WithLabelWidth(0m));
            var duplicate = BtplTemplate.Parse("^H5\n^H10\n^L\nE");
            Format("AmbiguousSetting", () => duplicate.WithSetting(new BtplSetting("^H", "8")));
            Format("AmbiguousSetting", () => duplicate.RemoveSetting("^H"));
            foreach (string raw in new[] { "", "^L\nAW,0,0,10,1,0,0,x", "^L\n^L\nE", "^L\nE\n^L\nE", "E\n^L" })
            {
                var ambiguous = BtplTemplate.Parse(raw);
                Check(ambiguous.ToBtpl() == raw, "ambiguous frame source preserved");
                Format("AmbiguousFrame", () => ambiguous.AddText(new BtplTextElement(1, 0, 10, 1, "x")));
                Format("AmbiguousFrame", () => ambiguous.WithSetting(new BtplSetting("^W", "70")));
            }
            var multiple = BtplTemplate.Parse("^L\nAW,1,2,10,1,0,0,A\nE\n^L\nAW,3,4,10,1,0,0,B\nE");
            Check(multiple.WithText(1, multiple.TextElements[1].WithPayload("B2")).ToBtpl().Contains("0,0,B2\nE"), "explicit existing row in multiple frames");
            Check(multiple.RemoveText(0).TextElements.Count == 1, "explicit row deletion in multiple frames");
        }

        private static void Files()
        {
            string directory = Path.Combine(Path.GetTempPath(), "txt-editor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string originalPath = Path.Combine(directory, "original.txt");
                string copyPath = Path.Combine(directory, "copy.txt");
                var unsaved = BtplTemplate.Parse(Raw);
                Throws<InvalidOperationException>(() => unsaved.Save());
                var saved = unsaved.SaveAs(originalPath);
                Check(unsaved.FilePath == null && saved.FilePath == Path.GetFullPath(originalPath), "SaveAs returns new file origin");
                Check(File.ReadAllBytes(originalPath).SequenceEqual(Encoding.GetEncoding(949).GetBytes(Raw)), "CP949 exact saved text");
                var loaded = BtplTemplate.Load(originalPath);
                // 편집 중에는 원본 읽기 잠금이 남지 않아야 한다.
                using (new FileStream(originalPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                var rawEdited = loaded.WithRawText(Raw.Replace("가나다", "수정"));
                Check(rawEdited.FilePath == loaded.FilePath && rawEdited.CodePage == 949, "raw edit origin and encoding");
                saved = rawEdited.Save();
                Check(File.ReadAllText(originalPath, Encoding.GetEncoding(949)) == rawEdited.ToBtpl(), "raw edit Save");
                saved = saved.WithText(0, saved.TextElements[0].WithSize(12)).Save();
                Throws<BtplSaveConflictException>(() => loaded.Save());

                string sourceBeforeCopy = File.ReadAllText(originalPath, Encoding.GetEncoding(949));
                var copy = saved.SaveAs(copyPath);
                Check(copy.FilePath == Path.GetFullPath(copyPath), "new current file");
                copy = copy.WithRawText(copy.ToBtpl().Replace("수정", "사본")).Save();
                Check(File.ReadAllText(originalPath, Encoding.GetEncoding(949)) == sourceBeforeCopy, "Save after SaveAs leaves first source");
                Check(File.ReadAllText(copyPath, Encoding.GetEncoding(949)) == copy.ToBtpl(), "copy Save uses copy origin");
                Throws<IOException>(() => saved.SaveAs(copyPath));
                Check(File.ReadAllText(copyPath, Encoding.GetEncoding(949)) == copy.ToBtpl(), "no overwrite by default");
                Format("SameSavePath", () => saved.SaveAs(Path.Combine(directory, ".", "original.txt"), true));
                var overwritten = saved.SaveAs(copyPath, true);
                Check(File.ReadAllText(copyPath, Encoding.GetEncoding(949)) == saved.ToBtpl(), "explicit overwrite");
                overwritten = overwritten.WithText(0, overwritten.TextElements[0].WithWeight(3)).Save();
                Check(File.ReadAllText(copyPath, Encoding.GetEncoding(949)) == overwritten.ToBtpl(), "overwrite origin supports next Save");

                var pending = overwritten.WithText(0, overwritten.TextElements[0].WithPayload("pending ^V00"));
                string beforeFailure = File.ReadAllText(copyPath, Encoding.GetEncoding(949));
                using (new FileStream(copyPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    Throws<IOException>(() => pending.Save());
                Check(pending.FilePath == overwritten.FilePath && pending.TextElements[0].Payload == "pending ^V00", "failed save keeps edit");
                Check(File.ReadAllText(copyPath, Encoding.GetEncoding(949)) == beforeFailure, "failed replacement preserves target");
                Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "failed temp cleanup");
                File.Delete(copyPath);
                Throws<BtplSaveConflictException>(() => overwritten.Save());
                Check(!File.Exists(copyPath), "deleted source not recreated");

                // 원본 폴더까지 사라진 Save는 충돌이며, 새 SaveAs의 없는 폴더는 일반 파일 오류다.
                string removedDirectory = Path.Combine(directory, "removed-parent");
                Directory.CreateDirectory(removedDirectory);
                string removedPath = Path.Combine(removedDirectory, "source.txt");
                var removedSource = unsaved.SaveAs(removedPath);
                File.Delete(removedPath);
                Directory.Delete(removedDirectory);
                try { removedSource.Save(); throw new InvalidOperationException("Expected SourceChanged"); }
                catch (BtplSaveConflictException error) { Check(error.Code == "SourceChanged", "deleted parent reports source conflict"); }
                Check(!Directory.Exists(removedDirectory) && removedSource.FilePath == removedPath && removedSource.ToBtpl() == Raw, "deleted parent and edit remain unchanged");
                Throws<DirectoryNotFoundException>(() => unsaved.SaveAs(removedPath));
                Check(!Directory.Exists(removedDirectory) && Directory.GetFiles(directory, "*.tmp").Length == 0, "missing SaveAs parent is not created");

                string utf8Path = Path.Combine(directory, "utf8.txt");
                byte[] utf8Bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(Raw)).ToArray();
                File.WriteAllBytes(utf8Path, utf8Bytes);
                Format("InvalidEncoding", () => BtplTemplate.Load(utf8Path));
                var utf8 = BtplTemplate.Load(utf8Path, 65001).WithRawText(Raw.Replace("가나다", "유니코드"));
                utf8 = utf8.Save();
                Check(utf8.CodePage == 65001 && File.ReadAllBytes(utf8Path).SequenceEqual(Encoding.UTF8.GetBytes(utf8.ToBtpl())), "UTF8 origin and BOM-free save");
                string invalidPath = Path.Combine(directory, "invalid.txt");
                File.WriteAllBytes(invalidPath, new byte[] { 0x81 });
                Format("InvalidEncoding", () => BtplTemplate.Load(invalidPath));
                string unencodablePath = Path.Combine(directory, "unencodable.txt");
                Format("InvalidEncoding", () => BtplTemplate.Parse("PRIVATE😀").SaveAs(unencodablePath));
                Check(!File.Exists(unencodablePath) && Directory.GetFiles(directory, "*.tmp").Length == 0, "encoding fails before publication");
                // 두 인수 Q가 남은 서식은 저장 전에 거부되어 파일을 만들지 않는다.
                string gapPath = Path.Combine(directory, "gap.txt");
                Format("GapLabelSetting", () => BtplTemplate.Parse("^Q40,5\r\n^L\r\nE").SaveAs(gapPath));
                Format("GapLabelSetting", () => BtplTemplate.Parse("^Q40,5,\r\n^L\r\nE").SaveAs(gapPath));
                Check(!File.Exists(gapPath) && Directory.GetFiles(directory, "*.tmp").Length == 0, "two-argument Q is not saved");
                string zeroPath = Path.Combine(directory, "zero-x.txt");
                Format("InvalidArguments", () => BtplTemplate.Parse("^L\r\nAW,0,10,10,1,0,0,x\r\nE").SaveAs(zeroPath));
                Format("InvalidArguments", () => BtplTemplate.Parse("^L\r\nAW, 0,10,10,1,0,0,x\r\nE").SaveAs(zeroPath));
                Check(!File.Exists(zeroPath) && BtplTemplate.Parse("^L\r\nAW,0,10,10,1,0,0,x\r\nE").TextElements[0].X == 0, "zero X is readable but not saved");
                string largePath = Path.Combine(directory, "large.txt");
                using (var large = new FileStream(largePath, FileMode.CreateNew, FileAccess.Write)) large.SetLength(4 * 1024 * 1024 + 1);
                Format("TemplateTooLarge", () => BtplTemplate.Load(largePath));
            }
            finally { Directory.Delete(directory, true); }
        }

        private const string BlackMark = "^Q35,5,9+\r\n^W70\r\n^L\r\nAW,80,120,10,1,0,0,A ^Q35,5\r\nE\r\n";

        private static void Mode(string text, BtplLabelMode mode, params int[] lines)
        {
            var template = BtplTemplate.Parse(text);
            Check(template.LabelMode == mode && template.LabelSettingLineNumbers.SequenceEqual(lines),
                "label mode " + mode + " for " + text.Replace("\r", "\\r").Replace("\n", "\\n"));
        }

        // 공개 목록으로 예상한 인쇄 거부(코드·줄)가 저장 검사(WithRawText)의 실제 결과와 같아야 한다. AW가 ^Q보다 먼저다.
        private static void SamePrintCheck(string text)
        {
            var template = BtplTemplate.Parse(text);
            string expectedCode = null;
            int? expectedLine = null;
            if (template.UnprintableTextLineNumbers.Count > 0)
            {
                expectedCode = "InvalidArguments";
                expectedLine = template.UnprintableTextLineNumbers[0];
            }
            else
            {
                int gap = template.LabelSettingLineNumbers.FirstOrDefault(line => !template.Settings.Any(setting =>
                    setting.Command == "^Q" && setting.TemplateLine == line && setting.Parameters.Split(',').Length == 3));
                if (gap > 0)
                {
                    expectedCode = "GapLabelSetting";
                    expectedLine = gap;
                }
            }
            string actualCode = null;
            int? actualLine = null;
            try { template.WithRawText(template.ToBtpl()); }
            catch (BtplFormatException error)
            {
                actualCode = error.Code;
                actualLine = error.TemplateLine;
            }
            Check(actualCode == expectedCode && actualLine == expectedLine,
                "print check matches public lines for " + text.Replace("\r", "\\r").Replace("\n", "\\n"));
        }

        private static void LabelModeAndPrintability()
        {
            Mode("^W70\r\n^L\r\nAW,80,120,10,1,0,0,A ^Q35,5\r\nE\r\n", BtplLabelMode.Unspecified);
            Mode(BlackMark, BtplLabelMode.BlackMark, 1);
            Mode(BlackMark.Replace("^Q35,5,9+", "^Q35,5,9-"), BtplLabelMode.BlackMark, 1);
            Mode(BlackMark.Replace("^Q35,5,9+", "^Q35,5"), BtplLabelMode.Gap, 1);
            Mode(BlackMark.Replace("^Q35,5,9+", " ^Q35,5,9+"), BtplLabelMode.Ambiguous, 1);
            Mode(BlackMark.Replace("^Q35,5,9+", "^Q35,5,9+,1"), BtplLabelMode.Ambiguous, 1);
            Mode(BlackMark.Replace("^Q35,5,9+", "^Q35,5,"), BtplLabelMode.Ambiguous, 1);
            Mode("^Q35,5,9+\r\n" + BlackMark, BtplLabelMode.Ambiguous, 1, 2);
            Mode("^Q35,5,9+\r\n^L\r\nE\r\n^Q35,5,9+\r\n^L\r\nE\r\n", BtplLabelMode.Ambiguous, 1, 4);
            Mode("^XX1\n^W70\n ^Q35,5,9+\n^L\nE\n", BtplLabelMode.Ambiguous, 3);
            Mode("^XX1\r^Q35,5\r^L\rE\r", BtplLabelMode.Gap, 2);
            Mode("^XX1\r\n^Q35,5,9+\r\n^L\r\nE\r\n", BtplLabelMode.BlackMark, 2);

            Check(BtplTemplate.Parse(BlackMark).UnprintableTextLineNumbers.Count == 0, "printable AW");
            Check(BtplTemplate.Parse(BlackMark.Replace("AW,80,", "AW,0,")).UnprintableTextLineNumbers.SequenceEqual(new[] { 4 }), "parsed AW X 0");
            Check(BtplTemplate.Parse(BlackMark.Replace("AW,80,", "AW, 0,")).UnprintableTextLineNumbers.SequenceEqual(new[] { 4 }), "unparsed AW X 0");
            Check(BtplTemplate.Parse(BlackMark.Replace("AW,80,", "AW,x,")).UnprintableTextLineNumbers.SequenceEqual(new[] { 4 }), "unparsed AW X text");

            foreach (string text in new[]
            {
                BlackMark, BlackMark.Replace("^Q35,5,9+", "^Q35,5"), BlackMark.Replace("^Q35,5,9+", " ^Q35,5,9+"),
                BlackMark.Replace("^Q35,5,9+", "^Q35,5,9+,1"), "^Q35,5,9+\r\n" + BlackMark, "^W70\r\n^L\r\nAW,80,120,10,1,0,0,A\r\nE\r\n",
                BlackMark.Replace("AW,80,", "AW,0,"), BlackMark.Replace("AW,80,", "AW, 0,"),
                // AW와 ^Q 오류가 함께 있으면 AW를 먼저 보고한다.
                BlackMark.Replace("^Q35,5,9+", "^Q35,5").Replace("AW,80,", "AW, 0,"),
                BlackMark.Replace("^Q35,5,9+", "^Q35,5").Replace("AW,80,", "AW,0,"),
            })
                SamePrintCheck(text);
            Format("InvalidArguments", () => BtplTemplate.Parse(BlackMark.Replace("^Q35,5,9+", "^Q35,5").Replace("AW,80,", "AW, 0,")).WithRawText(
                BlackMark.Replace("^Q35,5,9+", "^Q35,5").Replace("AW,80,", "AW, 0,")));
        }

        private static void PreviewText()
        {
            var text = BtplTemplate.Parse("^L\r\nAW,1,1,10,1,0,0,^V00/^V01/^V02/^V03\r\nE").TextElements[0];
            Check(text.GetPreviewText("A!!!!C!!") == "A//C/", "empty items stay empty");
            Check(text.GetPreviewText(null) == "^V00/^V01/^V02/^V03" && text.GetPreviewText() == text.Payload, "no data keeps markers");
            Check(text.GetPreviewText("FIRST!!SECOND") == "FIRST/SECOND/^V02/^V03", "missing numbers keep markers");
            var nested = BtplTemplate.Parse("^L\r\nAW,1,1,10,1,0,0,^V00|^V000\r\nE").TextElements[0];
            Check(nested.GetPreviewText("^V01!!SAFE") == "^V01|^V010", "values are not substituted again and only two digits are read");
            Check(nested.GetPreviewText("") == "|0", "empty data is one empty item");
            Check(text.Payload == "^V00/^V01/^V02/^V03", "payload unchanged");
        }

        private static bool Area(BtplArea area, int left, int top, int right, int bottom)
            => area.Left == left && area.Top == top && area.Right == right && area.Bottom == bottom;

        private static void BlackMarkMedia()
        {
            // 2026-10-14 실기 용지: 받침지 70, 마크 제외 35, 마크 5, 흰 부분 7.5, 흰 라벨 60×20 mm, 300dpi.
            var media = new BtplBlackMarkMedia(70m, 35m, 5m, 7.5m, 60m, 20m);
            Check(media.LabelSetting.Command == "^Q" && media.LabelSetting.Parameters == "35,5,9+", "black-mark Q from ruler values");
            Check(media.WidthSetting.Command == "^W" && media.WidthSetting.Parameters == "70", "W from liner width");
            // 기본 보정은 실측 위치다: 흰 라벨 상단이 원점보다 3.6 mm 아래, 왼쪽 끝이 받침지 가운데 기준보다 1.0 mm 왼쪽.
            Check(Area(media.LabelArea, 47, 43, 756, 279), "label area dots");
            Check(Area(media.SafeArea, 71, 66, 732, 255), "2 mm safe area dots");
            var centred = new BtplBlackMarkMedia(70m, 35m, 5m, 7.5m, 60m, 20m, 300, 1.5m, 0m, 0m);
            Check(Area(centred.LabelArea, 59, 0, 768, 236) && Area(centred.SafeArea, 83, 24, 744, 213), "offset arguments");
            Format("InvalidArguments", () => new BtplBlackMarkMedia(70m, 35m, 5m, 7.5m, 60m, 20m, 300, 1.5m, 3.6m, -6m));
            // 가로 보정이 라벨을 받침지 오른쪽 밖으로 밀면 거부하고, 오른쪽 끝에 딱 맞으면 허용한다.
            Format("InvalidArguments", () => new BtplBlackMarkMedia(70m, 35m, 5m, 7.5m, 60m, 20m, 300, 1.5m, 3.6m, 6m));
            Check(new BtplBlackMarkMedia(70m, 35m, 5m, 7.5m, 60m, 20m, 300, 1.5m, 3.6m, 5m).LabelArea.Right == 827, "label may end at the liner edge");
            Format("InvalidArguments", () => new BtplBlackMarkMedia(70m, 35m, 5m, 7.5m, 60m, 20m, 300, 1.5m, -1m, -1m));
            // z는 0.5 mm 단위로 반올림하고 센서 보정 상수를 바꿀 수 있다.
            Check(new BtplBlackMarkMedia(70m, 35m, 5m, 7.3m, 60m, 20m).LabelSetting.Parameters == "35,5,9+", "z rounds up to 0.5 mm");
            Check(new BtplBlackMarkMedia(70m, 35m, 5m, 7.1m, 60m, 20m).LabelSetting.Parameters == "35,5,8.5+", "z rounds down to 0.5 mm");
            Check(new BtplBlackMarkMedia(70m, 35m, 5m, 7.5m, 60m, 20m, 300, 0m).LabelSetting.Parameters == "35,5,7.5+", "sensor offset argument");
            Check(Area(new BtplBlackMarkMedia(70m, 35m, 5m, 7.5m, 60m, 20m, 203).LabelArea, 32, 29, 511, 189), "203 dpi area");
            // 계산한 Q는 기존 2인수 서식을 세 인수로 고치는 편집에 그대로 쓴다.
            Check(BtplTemplate.Parse("^Q40,5\n^L\nE").WithSetting(media.LabelSetting).ToBtpl() == "^Q35,5,9+\n^L\nE", "apply calculated Q");
            Format("InvalidArguments", () => new BtplBlackMarkMedia(70m, 35m, 5m, 7.5m, 60m, 20m, 250));
            Format("InvalidArguments", () => new BtplBlackMarkMedia(70m, 35m, 5m, 7.5m, 71m, 20m));
            Format("InvalidArguments", () => new BtplBlackMarkMedia(70m, 35m, 5m, 16m, 60m, 20m));
            Format("InvalidArguments", () => new BtplBlackMarkMedia(70m, 0m, 5m, 7.5m, 60m, 20m));
            Format("InvalidArguments", () => new BtplBlackMarkMedia(70m, 35m, 5m, -1m, 60m, 20m));
            Format("InvalidArguments", () => new BtplBlackMarkMedia(70m, 35m, 5m, 7.5m, 3m, 20m));
            // 반올림 경계는 0.5 mm 위로 올리고(8.75 → 9), 203dpi 안전 영역도 같은 식으로 계산한다.
            Check(new BtplBlackMarkMedia(70m, 35m, 5m, 7.25m, 60m, 20m).LabelSetting.Parameters == "35,5,9+", "midpoint rounds away from zero");
            Check(Area(new BtplBlackMarkMedia(70m, 35m, 5m, 7.5m, 60m, 20m, 203).SafeArea, 48, 45, 496, 173), "203 dpi safe area");
            // 큰 값·표현할 수 없는 소수·음수 z도 다른 예외 없이 InvalidArguments다.
            Format("InvalidArguments", () => new BtplBlackMarkMedia(70m, 35m, 5m, 7.5m, 60m, 20m, 300, -9m));
            Format("InvalidArguments", () => new BtplBlackMarkMedia(70m, 35m, 0.0004m, 7.5m, 60m, 20m));
            Format("InvalidArguments", () => new BtplBlackMarkMedia(decimal.MaxValue, 35m, 5m, 7.5m, 60m, 20m));
            Format("InvalidArguments", () => new BtplBlackMarkMedia(70m, 35m, 5m, decimal.MaxValue, 60m, 20m));
            Format("InvalidArguments", () => new BtplBlackMarkMedia(400000000m, 35m, 5m, 7.5m, 60m, 20m));
        }

        private static void RemovedApi()
        {
            var library = typeof(BtplTemplate).Assembly;
            foreach (string name in new[] { "BtplProcessor", "BtplPrintRequest", "BtplBindings", "BtplBinding", "BtplTemplateFormat", "BtplRenderOptions", "BtplRenderResult", "ValidationIssue", "ValidationSeverity", "BtplLineAdvance", "BatchValidationResult", "RowValidationResult" })
                Check(library.GetType("U2BIO.BT003PROLabelPrinting." + name) == null, "removed " + name);
            Check(typeof(BtplTemplate).GetMethod("Save", Type.EmptyTypes) != null, "explicit current Save");
            Check(typeof(BtplTemplate).GetMethod("SaveAs", new[] { typeof(string), typeof(bool) }).GetParameters()[1].DefaultValue.Equals(false), "overwrite default false");
            Check(typeof(BtplTextElement).GetMethod("WithArgument") == null, "named edits replace opaque argument API");
            foreach (string name in new[] { "System.Data", "System.Xml", "System.Runtime.Serialization", "System.Drawing", "System.Windows.Forms" })
                Check(!library.GetReferencedAssemblies().Any(reference => reference.Name == name), "UI/DB/format dependency removed");
        }
    }
}
