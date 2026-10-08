using System;
using System.Collections.Generic;
using System.Linq;

namespace U2BIO.BT003PROLabelPrinting
{
    public sealed class BtplTemplate
    {
        public IReadOnlyList<BtplSetting> Settings { get; }
        public IReadOnlyList<BtplTextElement> TextElements { get; }
        private readonly BtplFileOrigin origin;
        private BtplTemplate(IEnumerable<BtplSetting> settings, IEnumerable<BtplTextElement> texts, BtplFileOrigin source)
        {
            Settings = Array.AsReadOnly(settings.ToArray()); TextElements = Array.AsReadOnly(texts.ToArray()); origin = source;
        }
        public static BtplTemplate Load(string filePath, int codePage = 949)
        {
            if (filePath == null) throw new ArgumentNullException(nameof(filePath));
            BtplFileOrigin source;
            string text = BtplFileStore.Read(filePath, codePage, out source);
            var parsed = Parse(text);
            return new BtplTemplate(parsed.Settings, parsed.TextElements, source);
        }
        public static BtplTemplate Parse(string btpl)
        {
            if (btpl == null) throw new ArgumentNullException(nameof(btpl));
            string[] lines = BtplSyntax.SplitLines(btpl);
            var settings = new List<BtplSetting>(); var texts = new List<BtplTextElement>();
            bool started = false, ended = false;
            for (int i = 0; i < lines.Length; i++) {
                string line = lines[i];
                if (line.All(c => c == ' ' || c == '\t')) continue;
                if (ended) throw new BtplFormatException("CommandPosition", i + 1);
                if (line == "^L") {
                    if (started) throw new BtplFormatException("InvalidFrame", i + 1);
                    started = true; continue;
                }
                if (line == "E") {
                    if (!started) throw new BtplFormatException("InvalidFrame", i + 1);
                    ended = true; continue;
                }
                string command = line.Length >= 2 ? line.Substring(0, 2) : line;
                if (BtplSyntax.Settings.Contains(command, StringComparer.Ordinal)) {
                    if (started) throw new BtplFormatException("CommandPosition", i + 1);
                    var setting = new BtplSetting(command, line.Substring(2), i + 1);
                    BtplSyntax.CheckSetting(setting);
                    if (settings.Any(x => x.Command == command)) throw new BtplFormatException("DuplicateSetting", i + 1);
                    settings.Add(setting);
                } else {
                    if (BtplSyntax.ArgumentCount(line.Split(',')[0]) < 0) throw new BtplFormatException("UnsupportedCommand", i + 1);
                    if (!started) throw new BtplFormatException("CommandPosition", i + 1);
                    texts.Add(BtplSyntax.ParseText(line, i + 1));
                }
            }
            if (!started || !ended) throw new BtplFormatException("InvalidFrame");
            CheckRequired(settings);
            return new BtplTemplate(settings, texts, null);
        }
        private static void CheckRequired(IEnumerable<BtplSetting> settings)
        {
            var missing = new[] { "^Q", "^W", "^S", "^H" }.Where(c => !settings.Any(x => x.Command == c)).Select(c => new ValidationIssue("MissingSetting", null)).ToArray();
            if (missing.Length != 0) throw new BtplFormatException(missing);
        }
        public string ToBtpl()
        {
            CheckRequired(Settings);
            foreach (var setting in Settings) BtplSyntax.CheckSetting(setting);
            foreach (var text in TextElements) {
                if (text.Payload.IndexOfAny(new[] { '\r', '\n' }) >= 0) throw new BtplFormatException("UnexpandedTextLines", text.Line);
                BtplSyntax.CheckText(text, false);
            }
            var result = Settings.Select(x => x.ToLine()).ToList();
            foreach (string value in new[] { "^P1", "^C1", "^R0", "^E0" })
                if (!Settings.Any(x => x.Command == value.Substring(0, 2))) result.Add(value);
            result.Add("^L"); result.AddRange(TextElements.Select(x => x.ToLine())); result.Add("E");
            string btpl = string.Join("\r\n", result) + "\r\n";
            Parse(btpl); return btpl;
        }
        public BtplTemplate Save(string filePath, int codePage = 949)
        {
            if (filePath == null) throw new ArgumentNullException(nameof(filePath));
            string normalized = ToBtpl();
            var source = BtplFileStore.Save(filePath, normalized, codePage, origin);
            var parsed = Parse(normalized);
            return new BtplTemplate(parsed.Settings, parsed.TextElements, source);
        }
        public BtplTemplate WithText(int textIndex, BtplTextElement text)
        {
            if (textIndex < 0 || textIndex >= TextElements.Count) throw new ArgumentOutOfRangeException(nameof(textIndex));
            if (text == null) throw new ArgumentNullException(nameof(text));
            BtplSyntax.CheckText(text, true);
            if (TextElements[textIndex].Command == "AW" && text.Command == "AW" && !TextElements[textIndex].Arguments.SequenceEqual(text.Arguments))
                throw new BtplFormatException("LockedTextArguments", TextElements[textIndex].Line);
            var copy = TextElements.ToArray(); copy[textIndex] = text;
            return new BtplTemplate(Settings, copy, origin);
        }
        public BtplTemplate AddText(BtplTextElement text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            BtplSyntax.CheckText(text, true);
            return new BtplTemplate(Settings, TextElements.Concat(new[] { text }), origin);
        }
        public BtplTemplate RemoveText(int textIndex)
        {
            if (textIndex < 0 || textIndex >= TextElements.Count) throw new ArgumentOutOfRangeException(nameof(textIndex));
            return new BtplTemplate(Settings, TextElements.Where((x, i) => i != textIndex), origin);
        }
        public BtplTemplate WithSetting(BtplSetting setting)
        {
            if (setting == null) throw new ArgumentNullException(nameof(setting));
            BtplSyntax.CheckSetting(setting);
            var copy = Settings.ToList(); int index = copy.FindIndex(x => x.Command == setting.Command);
            if (index < 0) copy.Add(setting); else copy[index] = setting;
            return new BtplTemplate(copy, TextElements, origin);
        }
        public BtplTemplate RemoveSetting(string command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (!BtplSyntax.Settings.Contains(command, StringComparer.Ordinal)) throw new BtplFormatException("UnsupportedCommand");
            return new BtplTemplate(Settings.Where(x => x.Command != command), TextElements, origin);
        }
        public BtplTemplate ExpandFixedLines(BtplRenderOptions renderOptions, out BtplRenderOptions remappedRenderOptions)
        {
            var options = renderOptions ?? new BtplRenderOptions();
            if (options.LineAdvances.Keys.Any(x => x >= TextElements.Count)) throw new ArgumentOutOfRangeException(nameof(renderOptions));
            var output = new List<BtplTextElement>(); var remapped = new BtplRenderOptions();
            for (int i = 0; i < TextElements.Count; i++) {
                var text = TextElements[i]; string[] lines = BtplSyntax.SplitLines(text.Payload);
                BtplLineAdvance advance; bool hasAdvance = options.LineAdvances.TryGetValue(i, out advance);
                if (lines.Length > 1 && (!hasAdvance || (advance.DeltaX == 0 && advance.DeltaY == 0)))
                    throw new BtplFormatException("MissingLineAdvance", text.Line);
                for (int k = 0; k < lines.Length; k++) {
                    BtplTextElement element;
                    try {
                        element = k == 0 ? text.WithPayload(lines[k]) : text.WithPosition(checked(text.X + checked(k * advance.DeltaX)), checked(text.Y + checked(k * advance.DeltaY))).WithPayload(lines[k]);
                    } catch (OverflowException) { throw new BtplFormatException("CoordinateOverflow", text.Line); }
                    if (hasAdvance) remapped = remapped.WithLineAdvance(output.Count, advance);
                    output.Add(element);
                }
            }
            remappedRenderOptions = remapped;
            return new BtplTemplate(Settings, output, origin);
        }
    }
}
