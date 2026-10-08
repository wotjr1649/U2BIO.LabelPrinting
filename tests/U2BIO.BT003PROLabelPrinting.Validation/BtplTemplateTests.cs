using System;
using System.IO;
using System.Linq;
using System.Text;
using U2BIO.BT003PROLabelPrinting;

namespace U2BIO.BT003PROLabelPrinting.Validation
{
    internal static class BtplTemplateTests
    {
        private const string Header = "^Q40,5,7.5+\n^W70\n^G0\n^S5\n^H19\n^E15\n^L\n";
        private const string Text = "AW,80,120,10,1,0,0,  가나다,^V03!!  ";
        private static void Check(bool value) { if (!value) throw new InvalidOperationException("BTPL contract failed."); }
        private static void Error(string code, Action action, int? line = null)
        {
            try { action(); }
            catch (BtplFormatException e) {
                Check(e.Code == code && (!line.HasValue || e.Issues[0].TemplateLine == line));
                Check(!e.Message.Contains("PRIVATE"));
                return;
            }
            throw new InvalidOperationException("BTPL error was not returned.");
        }

        internal static void Run()
        {
            var template = BtplTemplate.Parse(Header + Text + "\nE");
            Check(template.TextElements.Count == 1 && template.TextElements[0].Payload == "  가나다,^V03!!  ");
            string saved = template.ToBtpl();
            Check(saved.EndsWith("E\r\n", StringComparison.Ordinal) && !saved.Replace("\r\n", "").Contains("\n"));
            Check(saved.Contains("^Q40,5,7.5+\r\n") && saved.Contains("^E15\r\n"));
            Check(!saved.Contains("^D0") && !saved.Contains("^O0"));
            foreach (string setting in new[] { "^P1", "^C1", "^R0" }) Check(saved.Split(new[] { setting + "\r\n" }, StringSplitOptions.None).Length == 2);
            Check(BtplTemplate.Parse(saved).ToBtpl() == saved);
            string raw = "AW,0080,00120,-10,+1,00,0,raw";
            Check(BtplTemplate.Parse(Header + raw + "\nE").ToBtpl().Contains(raw + "\r\n"));
            string[] arguments = { "10", "1", "0", "0" };
            var snapshot = new BtplTextElement("AW", 0, 0, arguments, "snapshot");
            arguments[0] = "20";
            Check(snapshot.Arguments[0] == "10");

            Error("DuplicateSetting", () => BtplTemplate.Parse("^H19\n" + Header + Text + "\nE"), 6);
            Error("CommandPosition", () => BtplTemplate.Parse(Header + "^H19\n" + Text + "\nE"), 8);
            Error("CommandPosition", () => BtplTemplate.Parse(Header + Text + "\nE\n^P1"), 10);
            Error("InvalidFrame", () => BtplTemplate.Parse(Header + Text));
            Error("UnsupportedCommand", () => BtplTemplate.Parse(Header + "~HE0,0\nE"), 8);
            Error("UnsupportedCommand", () => BtplTemplate.Parse(Header + "RM,PRIVATE\nE"));
            Error("InvalidArguments", () => BtplTemplate.Parse(Header.Replace("^G0", "^G3") + Text + "\nE"));
            Error("InvalidArguments", () => BtplTemplate.Parse(Header.Replace("^S5", "^S99999999999999") + Text + "\nE"));
            Error("InvalidArguments", () => BtplTemplate.Parse(Header + "AC,0,0,9,1,0,0,x\nE"));
            Error("InvalidArguments", () => BtplTemplate.Parse(Header + "AC,0,0,1,1,0,4,x\nE"));
            Error("InvalidText", () => BtplTemplate.Parse(Header + "AW,0,0,10,1,0,0,PRIVATE~PW\nE"));
            Error("InvalidText", () => BtplTemplate.Parse(Header + "AW,0,0,10,1,0,0,^V0\nE"));
            Error("InvalidText", () => BtplTemplate.Parse(Header + "AW,0,0,10,1,0,0,^V00^D\nE"));
            Error("InvalidText", () => BtplTemplate.Parse(Header + "AW,0,0,10,1,0,0,x\0\nE"));
            Error("InvalidArguments", () => BtplTemplate.Parse(Header.Replace("^E15", "^P2") + Text + "\nE"));
            Error("MissingSetting", () => template.RemoveSetting("^W").ToBtpl());
            Check(template.RemoveSetting("^W").WithSetting(new BtplSetting("^W", "60")).ToBtpl().Contains("^W60\r\n"));
            Error("UnsupportedCommand", () => template.WithSetting(new BtplSetting("~HE", "0,0")));

            var moved = template.WithText(0, template.TextElements[0].WithPosition(90, 140).WithPayload("first\n\n^V03,!!"));
            Check(template.TextElements[0].X == 80 && template.TextElements[0].Payload == "  가나다,^V03!!  ");
            Error("LockedTextArguments", () => template.TextElements[0].WithArgument(0, "12"));
            Error("LockedTextArguments", () => template.WithText(0, new BtplTextElement("AW", 80, 120, new[] { "12", "1", "0", "0" }, "x")));
            Error("UnexpandedTextLines", () => moved.ToBtpl());
            Error("MissingLineAdvance", () => moved.ExpandFixedLines(null, out var ignored));
            var second = new BtplTextElement("AC", 20, 30, new[] { "1", "1", "0", "0I" }, "second");
            moved = moved.AddText(second);
            var options = new BtplRenderOptions().WithLineAdvance(0, new BtplLineAdvance(0, 40)).WithLineAdvance(1, new BtplLineAdvance(5, 9));
            BtplRenderOptions remapped;
            var expanded = moved.ExpandFixedLines(options, out remapped);
            Check(expanded.TextElements.Select(x => x.Y).SequenceEqual(new[] { 140, 180, 220, 30 }));
            Check(expanded.TextElements[1].Payload == "" && expanded.TextElements[2].Payload == "^V03,!!");
            Check(remapped.LineAdvances.Count == 4 && remapped.LineAdvances[3].DeltaX == 5);
            Check(BtplTemplate.Parse(expanded.ToBtpl()).TextElements[2].Y == 220);
            Check(options.LineAdvances.Count == 2 && moved.TextElements.Count == 2);
            var removed = expanded.RemoveText(0);
            var removedOptions = remapped.WithRemovedText(0);
            Check(removed.TextElements.Count == 3 && removedOptions.LineAdvances[2].DeltaY == 9);
            var copied = removed.AddText(removed.TextElements[0]);
            var copiedOptions = removedOptions.WithLineAdvance(3, removedOptions.LineAdvances[0]);
            Check(copied.TextElements.Count == 4 && copiedOptions.LineAdvances.Count == 4);
            Error("CoordinateOverflow", () => template.WithText(0, template.TextElements[0].WithPosition(0, int.MaxValue).WithPayload("a\nb")).ExpandFixedLines(new BtplRenderOptions().WithLineAdvance(0, new BtplLineAdvance(0, 40)), out var ignored));

            foreach (string command in new[] { "AA", "AH", "AI", "AK", "AL", "AZ9", "VA", "VZ" })
                Check(BtplTemplate.Parse(Header + command + ",1,2,1,1,0,0,x,y\nE").TextElements[0].Payload == "x,y");
            Check(BtplTemplate.Parse(Header + "AT,1,2,8,2000,200,3BTU,0,0,x\nE").TextElements.Count == 1);
            Check(BtplTemplate.Parse(Header + "ATA,1,2,0,8,0,0,A,1,x\nE").TextElements.Count == 1);
            Error("InvalidArguments", () => BtplTemplate.Parse(Header + "AT,1,2,0,8,0,0,0,0,x\nE"));
            Error("InvalidArguments", () => BtplTemplate.Parse(Header + "AT,1,2,8,8,0,0BB,0,0,x\nE"));
            Error("TextCommandLimit", () => BtplTemplate.Parse(Header + "VA,0,0,1,1,0,0," + new string('a', 240) + "\nE"));
            Check(BtplTemplate.Parse(Header + "AW,2147483647,2147483647,10,1,0,0," + new string('x', 1000) + "\nE").TextElements.Count == 1);
            Files(template);
            Console.WriteLine("PASS: BTPL C17/C18/C22/C23/C24-managed parsing, immutable edits, lines, strict encoding, atomic save and conflicts; no device calls");
        }

        private static void Files(BtplTemplate template)
        {
            string directory = Path.Combine(Path.GetTempPath(), "btpl-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try {
                string file = Path.Combine(directory, "label.btpl");
                var stored = template.Save(file);
                Check(File.ReadAllBytes(file).SequenceEqual(Encoding.GetEncoding(949).GetBytes(template.ToBtpl())));
                var loaded = BtplTemplate.Load(file);
                Check(loaded.ToBtpl() == template.ToBtpl());
                stored = stored.WithText(0, stored.TextElements[0].WithPayload("next")).Save(file);
                stored = stored.WithSetting(new BtplSetting("^H", "10")).Save(file);
                Check(File.ReadAllText(file, Encoding.GetEncoding(949)).Contains("^H10"));
                try { loaded.Save(file); throw new InvalidOperationException("External change missed."); }
                catch (BtplSaveConflictException e) { Check(e.Code == "SourceChanged"); }
                File.Delete(file);
                try { stored.Save(file); throw new InvalidOperationException("Deleted source missed."); }
                catch (BtplSaveConflictException e) { Check(e.Code == "SourceChanged"); }
                string utf8 = Path.Combine(directory, "utf8.btpl");
                template.Save(utf8, 65001);
                Check(!File.ReadAllBytes(utf8).Take(3).SequenceEqual(new byte[] { 239, 187, 191 }));
                Check(BtplTemplate.Load(utf8, 65001).ToBtpl() == template.ToBtpl());
                File.WriteAllBytes(utf8, Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(template.ToBtpl())).ToArray());
                Check(BtplTemplate.Load(utf8, 65001).TextElements.Count == 1);
                Error("InvalidEncoding", () => BtplTemplate.Load(utf8, 949));
                File.WriteAllBytes(utf8, new byte[] { 255 });
                Error("InvalidEncoding", () => BtplTemplate.Load(utf8, 65001));
                File.WriteAllText(utf8, "preserve", Encoding.ASCII);
                var emoji = template.WithText(0, template.TextElements[0].WithPayload("\U0001F600"));
                Error("InvalidEncoding", () => emoji.Save(utf8, 949));
                Check(File.ReadAllText(utf8) == "preserve");
                using (var locked = new FileStream(utf8, FileMode.Open, FileAccess.Read, FileShare.None)) {
                    try { template.Save(utf8, 65001); throw new InvalidOperationException("Locked replacement succeeded."); }
                    catch (IOException) { }
                }
                Check(File.ReadAllText(utf8) == "preserve" && !Directory.GetFiles(directory).Any(x => x.EndsWith(".tmp", StringComparison.Ordinal)));
            }
            finally {
                foreach (string path in Directory.GetFiles(directory)) File.Delete(path);
                Directory.Delete(directory);
            }
        }
    }
}
