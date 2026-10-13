using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace U2BIO.BT003PROLabelPrinting
{
    /// <summary>편집·미리보기용 BTPL 부분 집합 모델이다. 기본 프린터 API는 이 파서를 사용하지 않는다.</summary>
    public sealed partial class BtplTemplate
    {
        public IReadOnlyList<BtplSetting> Settings { get; }
        public IReadOnlyList<BtplTextElement> TextElements { get; }

        private readonly BtplFileOrigin origin;
        internal BtplTemplate(IEnumerable<BtplSetting> settings, IEnumerable<BtplTextElement> texts, BtplFileOrigin source)
        {
            Settings = Array.AsReadOnly(settings.ToArray());
            TextElements = Array.AsReadOnly(texts.ToArray());
            origin = source;
        }

        /// <summary>파일 확장자로 형식을 선택한다. 편집용 읽기 잠금은 내용을 읽을 때만 유지한다.</summary>
        public static BtplTemplate Load(string filePath, int codePage = 949) => Load(filePath, FileFormat(filePath), codePage);
        public static BtplTemplate Load(string filePath, BtplTemplateFormat format, int codePage = 949)
        {
            if (filePath == null)
                throw new ArgumentNullException(nameof(filePath));
            CheckFormat(format);
            BtplFileOrigin source;
            string text = BtplFileStore.Read(filePath, format == BtplTemplateFormat.Btpl ? codePage : 65001, out source, format == BtplTemplateFormat.Btpl ? (int?)null : MaxDocumentBytes, "TemplateTooLarge");
            var parsed = Parse(text, format);
            return new BtplTemplate(parsed.Settings, parsed.TextElements, source);
        }

        /// <summary>편집 가능한 명령 부분 집합만 읽는다. SDK의 전체 BTPL 지원 범위와는 다르다.</summary>
        public static BtplTemplate Parse(string btpl) => Parse(btpl, true);
        internal static BtplTemplate ParseSdk(string btpl) => Parse(btpl, false);
        private static BtplTemplate Parse(string btpl, bool requirePrintingSettings)
        {
            if (btpl == null)
                throw new ArgumentNullException(nameof(btpl));
            string[] lines = BtplSyntax.SplitLines(btpl);
            var settings = new List<BtplSetting>();
            var texts = new List<BtplTextElement>();
            bool started = false, ended = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.All(c => c == ' ' || c == '\t'))
                    continue;
                if (ended)
                    throw new BtplFormatException("CommandPosition", i + 1);
                if (line == "^L")
                {
                    if (started)
                        throw new BtplFormatException("InvalidFrame", i + 1);
                    started = true;
                    continue;
                }

                if (line == "E")
                {
                    if (!started)
                        throw new BtplFormatException("InvalidFrame", i + 1);
                    ended = true;
                    continue;
                }

                string command = line.Length >= 2 ? line.Substring(0, 2) : line;
                if (BtplSyntax.Settings.Contains(command, StringComparer.Ordinal))
                {
                    if (started)
                        throw new BtplFormatException("CommandPosition", i + 1);
                    var setting = new BtplSetting(command, line.Substring(2), i + 1);
                    BtplSyntax.CheckSetting(setting);
                    if (settings.Any(x => x.Command == command))
                        throw new BtplFormatException("DuplicateSetting", i + 1);
                    settings.Add(setting);
                }
                else
                {
                    if (BtplSyntax.ArgumentCount(line.Split(',')[0]) < 0)
                        throw new BtplFormatException("UnsupportedCommand", i + 1);
                    if (!started)
                        throw new BtplFormatException("CommandPosition", i + 1);
                    texts.Add(BtplSyntax.ParseText(line, i + 1));
                }
            }

            if (!started || !ended)
                throw new BtplFormatException("InvalidFrame");
            CheckRequired(settings, requirePrintingSettings);
            return new BtplTemplate(settings, texts, null);
        }

        private static void CheckRequired(IEnumerable<BtplSetting> settings, bool requirePrintingSettings = true)
        {
            var required = requirePrintingSettings ? new[] { "^Q", "^W", "^S", "^H" } : new[] { "^Q", "^W" };
            var missing = required.Where(c => !settings.Any(x => x.Command == c)).Select(c => new ValidationIssue("MissingSetting", null)).ToArray();
            if (missing.Length != 0)
                throw new BtplFormatException(missing);
        }

        internal void CheckSettings()
        {
            CheckRequired(Settings);
            var commands = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Settings.Count; i++)
            {
                var setting = Settings[i];
                BtplSyntax.CheckSetting(setting);
                // 재파싱 없이 중복을 검사하되, 기존 직렬화 순서의 오류 행 번호는 보존한다.
                if (!commands.Add(setting.Command))
                    throw new BtplFormatException("DuplicateSetting", setting.Line ?? i + 1);
            }
        }

        /// <summary>설정·텍스트 검사 후 CRLF 문자열을 만든다. 생략된 ^P1·^C1·^R0·^E0은 기본값으로 추가한다.</summary>
        public string ToBtpl()
        {
            CheckSettings();
            foreach (var text in TextElements)
            {
                if (text.Payload.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                    throw new BtplFormatException("UnexpandedTextLines", text.Line);
                BtplSyntax.CheckText(text, false);
            }

            var result = Settings.Select(x => x.ToLine()).ToList();
            foreach (string value in new[] { "^P1", "^C1", "^R0", "^E0" })
                if (!Settings.Any(x => x.Command == value.Substring(0, 2)))
                    result.Add(value);
            result.Add("^L");
            result.AddRange(TextElements.Select(x => x.ToLine()));
            result.Add("E");
            string btpl = string.Join("\r\n", result) + "\r\n";
            return btpl;
        }

        /// <summary>원본 변경 시 덮어쓰기를 중단한다. 반환한 객체를 다음 편집·저장의 기준으로 사용한다.</summary>
        public BtplTemplate Save(string filePath, int codePage = 949) => Save(filePath, FileFormat(filePath), codePage);
        public BtplTemplate Save(string filePath, BtplTemplateFormat format, int codePage = 949)
        {
            if (filePath == null)
                throw new ArgumentNullException(nameof(filePath));
            CheckFormat(format);
            string normalized = format == BtplTemplateFormat.Btpl ? ToBtpl() : format == BtplTemplateFormat.Json ? ToJson() : ToXml();
            var source = BtplFileStore.Save(filePath, normalized, format == BtplTemplateFormat.Btpl ? codePage : 65001, origin);
            var parsed = Parse(normalized, format);
            return new BtplTemplate(parsed.Settings, parsed.TextElements, source);
        }

        public BtplTemplate WithText(int textIndex, BtplTextElement text)
        {
            if (textIndex < 0 || textIndex >= TextElements.Count)
                throw new ArgumentOutOfRangeException(nameof(textIndex));
            if (text == null)
                throw new ArgumentNullException(nameof(text));
            BtplSyntax.CheckText(text, true);
            if (TextElements[textIndex].Command == "AW" && text.Command == "AW" && !TextElements[textIndex].Arguments.SequenceEqual(text.Arguments))
                throw new BtplFormatException("LockedTextArguments", TextElements[textIndex].Line);
            var copy = TextElements.ToArray();
            copy[textIndex] = text;
            return new BtplTemplate(Settings, copy, origin);
        }

        public BtplTemplate AddText(BtplTextElement text)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));
            BtplSyntax.CheckText(text, true);
            return new BtplTemplate(Settings, TextElements.Concat(new[] { text }), origin);
        }

        public BtplTemplate RemoveText(int textIndex)
        {
            if (textIndex < 0 || textIndex >= TextElements.Count)
                throw new ArgumentOutOfRangeException(nameof(textIndex));
            return new BtplTemplate(Settings, TextElements.Where((x, i) => i != textIndex), origin);
        }

        public BtplTemplate WithSetting(BtplSetting setting)
        {
            if (setting == null)
                throw new ArgumentNullException(nameof(setting));
            BtplSyntax.CheckSetting(setting);
            var copy = Settings.ToList();
            int index = copy.FindIndex(x => x.Command == setting.Command);
            if (index < 0)
                copy.Add(setting);
            else
                copy[index] = setting;
            return new BtplTemplate(copy, TextElements, origin);
        }

        public BtplTemplate RemoveSetting(string command)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));
            if (!BtplSyntax.Settings.Contains(command, StringComparer.Ordinal))
                throw new BtplFormatException("UnsupportedCommand");
            return new BtplTemplate(Settings.Where(x => x.Command != command), TextElements, origin);
        }

        public BtplTemplate ExpandFixedLines(BtplRenderOptions renderOptions, out BtplRenderOptions remappedRenderOptions)
        {
            var options = renderOptions ?? new BtplRenderOptions();
            if (options.LineAdvances.Keys.Any(x => x >= TextElements.Count))
                throw new ArgumentOutOfRangeException(nameof(renderOptions));
            var output = new List<BtplTextElement>();
            var remapped = new BtplRenderOptions();
            for (int i = 0; i < TextElements.Count; i++)
            {
                var text = TextElements[i];
                BtplLineAdvance advance;
                bool hasAdvance = options.LineAdvances.TryGetValue(i, out advance);
                foreach (var element in text.ExpandLines(advance))
                {
                    if (hasAdvance)
                        remapped = remapped.WithLineAdvance(output.Count, advance);
                    output.Add(element);
                }
            }

            remappedRenderOptions = remapped;
            return new BtplTemplate(Settings, output, origin);
        }
    }

    internal static class BtplSyntax
    {
        internal static readonly string[] Settings =
        {
            "^Q",
            "^W",
            "^S",
            "^H",
            "^G",
            "^P",
            "^C",
            "^R",
            "^E",
            "^D",
            "^O"
        };
        internal static readonly Regex Variables = new Regex("\\^V[0-9]{2}", RegexOptions.CultureInvariant);
        internal static int Number(string value, int min, int max, int? line)
        {
            int number;
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number) || number < min || number > max)
                throw new BtplFormatException("InvalidArguments", line);
            return number;
        }

        private static void Millimetres(string value, bool positive, int? line)
        {
            decimal number;
            if (!Regex.IsMatch(value, "\\A[0-9]+(?:\\.[0-9]+)?\\z", RegexOptions.CultureInvariant) || !decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number) || (positive ? number <= 0 : number < 0))
                throw new BtplFormatException("InvalidArguments", line);
        }

        internal static void CheckSetting(BtplSetting setting)
        {
            string c = setting.Command, value = setting.Parameters;
            int? line = setting.Line;
            if (!Settings.Contains(c, StringComparer.Ordinal))
                throw new BtplFormatException("UnsupportedCommand", line);
            switch (c)
            {
                case "^Q":
                    string[] parts = value.Split(',');
                    if (parts.Length < 2 || parts.Length > 3)
                        throw new BtplFormatException("InvalidArguments", line);
                    Millimetres(parts[0], true, line);
                    Millimetres(parts[1], false, line);
                    if (parts.Length == 3)
                    {
                        string offset = parts[2];
                        if (offset.EndsWith("+", StringComparison.Ordinal) || offset.EndsWith("-", StringComparison.Ordinal))
                            offset = offset.Substring(0, offset.Length - 1);
                        Millimetres(offset, false, line);
                    }

                    break;
                case "^W":
                    Millimetres(value, true, line);
                    break;
                case "^S":
                    Number(value, 2, 7, line);
                    break;
                case "^H":
                    Number(value, 0, 19, line);
                    break;
                case "^G":
                    Number(value, 0, 2, line);
                    break;
                case "^P":
                case "^C":
                    Number(value, 1, 1, line);
                    break;
                case "^R":
                    Number(value, 0, 399, line);
                    break;
                case "^D":
                case "^O":
                    Number(value, 0, 0, line);
                    break;
                case "^E":
                    string[] stop = value.Split(',');
                    if (stop.Length > 3)
                        throw new BtplFormatException("InvalidArguments", line);
                    for (int i = 0; i < stop.Length; i++)
                        Number(stop[i], 0, i < 2 ? 40 : int.MaxValue, line);
                    break;
            }
        }

        internal static int ArgumentCount(string command)
        {
            if (command == "AW" || Regex.IsMatch(command, "\\A(?:A[A-HIKL]|AZ[1-9]|V[A-Z])\\z", RegexOptions.CultureInvariant))
                return 4;
            if (Regex.IsMatch(command, "\\AAT[A-Z]?\\z", RegexOptions.CultureInvariant))
                return 6;
            return -1;
        }

        internal static BtplTextElement ParseText(string value, int line)
        {
            int comma = value.IndexOf(',');
            string command = comma < 0 ? value : value.Substring(0, comma);
            int count = ArgumentCount(command);
            if (count < 0)
                throw new BtplFormatException("UnsupportedCommand", line);
            string[] parts = value.Split(new[] { ',' }, count + 4);
            if (parts.Length != count + 4)
                throw new BtplFormatException("InvalidArguments", line);
            var element = new BtplTextElement(command, parts[1], parts[2], parts.Skip(3).Take(count), parts[count + 3], line);
            CheckText(element, false);
            return element;
        }

        internal static void CheckText(BtplTextElement text, bool allowLines)
        {
            int count = ArgumentCount(text.Command);
            if (count < 0)
                throw new BtplFormatException("UnsupportedCommand", text.Line);
            if (text.Arguments.Count != count || text.Arguments.Any(x => x == null))
                throw new BtplFormatException("InvalidArguments", text.Line);
            var a = text.Arguments;
            if (text.Command == "AW")
            {
                foreach (string value in a)
                {
                    int number;
                    if (!Regex.IsMatch(value, "\\A[+-]?[0-9]+\\z", RegexOptions.CultureInvariant) || !int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number))
                        throw new BtplFormatException("InvalidArguments", text.Line);
                }
            }
            else if (count == 4)
            {
                Number(a[0], 1, 8, text.Line);
                Number(a[1], 1, 8, text.Line);
                Number(a[2], 0, int.MaxValue, text.Line);
                string rotation = text.Command.StartsWith("AZ", StringComparison.Ordinal) ? "\\A[0-7]I?\\z" : "\\A[0-3]I?\\z";
                if (!Regex.IsMatch(a[3], rotation, RegexOptions.CultureInvariant))
                    throw new BtplFormatException("InvalidArguments", text.Line);
            }
            else
            {
                int mode = Number(a[5], 0, 1, text.Line);
                int width = Number(a[0], 0, 2000, text.Line);
                if ((width == 0 && mode != 1) || (width != 0 && width < 8))
                    throw new BtplFormatException("InvalidArguments", text.Line);
                Number(a[1], 8, 2000, text.Line);
                Number(a[2], 0, 200, text.Line);
                if (!Regex.IsMatch(a[3], "\\A[0-3][BTU]*\\z", RegexOptions.CultureInvariant) || a[3].Skip(1).Distinct().Count() != a[3].Length - 1 || !Regex.IsMatch(a[4], text.Command == "AT" ? "\\A0\\z" : "\\A(?:0|[A-Z])\\z", RegexOptions.CultureInvariant))
                    throw new BtplFormatException("InvalidArguments", text.Line);
            }

            CheckPayload(text.Payload, allowLines, text.Line);
            if (text.Command.StartsWith("V", StringComparison.Ordinal) && SplitLines(text.Payload).Any(x => Variables.Replace(x, "").Length > 239))
                throw new BtplFormatException("TextCommandLimit", text.Line);
        }

        internal static string[] SplitLines(string payload) => payload.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        internal static void CheckLiteral(string payload, int? line)
        {
            if (payload.IndexOf('^') >= 0)
                throw new BtplFormatException("InvalidText", line);
            CheckPayload(payload, true, line);
        }

        private static void CheckPayload(string payload, bool allowLines, int? line)
        {
            for (int i = 0; i < payload.Length; i++)
            {
                char c = payload[i];
                if (c == '^')
                {
                    if (i + 3 >= payload.Length || payload[i + 1] != 'V' || payload[i + 2] < '0' || payload[i + 2] > '9' || payload[i + 3] < '0' || payload[i + 3] > '9')
                        throw new BtplFormatException("InvalidText", line);
                    i += 3;
                }
                else if (c == '~' || (char.IsControl(c) && !(allowLines && (c == '\r' || c == '\n'))))
                    throw new BtplFormatException("InvalidText", line);
            }
        }
    }
}
