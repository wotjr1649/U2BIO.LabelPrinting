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
            Console.WriteLine("PASS: TXT source/field preservation, named AW edits, frame limits, file origin and Save/SaveAs; nativeSDK=not-loaded");
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
            Check(q.WithSetting(new BtplSetting("^Q", "40,5")).ToBtpl() == "^Q040,005\r\n^L\r\nE", "Q optional offset removal");
            var qWithoutOffset = BtplTemplate.Parse("^Q040,005\n^L\nE");
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
                Format("AmbiguousFrame", () => ambiguous.AddText(new BtplTextElement(0, 0, 10, 1, "x")));
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
                string largePath = Path.Combine(directory, "large.txt");
                using (var large = new FileStream(largePath, FileMode.CreateNew, FileAccess.Write)) large.SetLength(4 * 1024 * 1024 + 1);
                Format("TemplateTooLarge", () => BtplTemplate.Load(largePath));
            }
            finally { Directory.Delete(directory, true); }
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
