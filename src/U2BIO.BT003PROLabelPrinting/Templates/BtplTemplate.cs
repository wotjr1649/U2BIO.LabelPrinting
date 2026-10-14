using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace U2BIO.BT003PROLabelPrinting
{
    /// <summary>TXT 원문과 작은 AW 편집 모델이다. SDK 인쇄 경로와 화면 렌더링에는 관여하지 않는다.</summary>
    public sealed class BtplTemplate
    {
        private const int MaximumBytes = 4 * 1024 * 1024;
        private readonly TextLine[] lines;
        private readonly byte[] savedHash;
        public string FilePath { get; }
        public int CodePage { get; }
        public IReadOnlyList<BtplTextElement> TextElements { get; }
        public IReadOnlyList<BtplSetting> Settings { get; }
        public IReadOnlyList<int> UnsupportedLineNumbers { get; }

        // 행 본문과 각 줄끝을 따로 보관해야 혼합 줄끝과 마지막 줄끝 유무를 잃지 않는다.
        private sealed class TextLine
        {
            internal readonly string Text;
            internal readonly string Ending;
            internal TextLine(string text, string ending) { Text = text; Ending = ending; }
        }

        private BtplTemplate(TextLine[] sourceLines, string filePath, int codePage, byte[] hash)
        {
            lines = (TextLine[])sourceLines.Clone();
            FilePath = filePath;
            CodePage = codePage;
            savedHash = hash;
            var texts = new List<BtplTextElement>();
            var settings = new List<BtplSetting>();
            var unsupported = new List<int>();
            for (int index = 0; index < lines.Length; index++)
            {
                string raw = lines[index].Text;
                BtplTextElement text;
                BtplSetting setting;
                if (raw.StartsWith("AW,", StringComparison.Ordinal) && BtplTextElement.TryRead(raw, index + 1, out text))
                    texts.Add(text);
                else if (TryReadSetting(raw, index + 1, out setting))
                    settings.Add(setting);
                else if (!string.IsNullOrWhiteSpace(raw) && raw != "^L" && raw != "E")
                    unsupported.Add(index + 1);
            }
            TextElements = Array.AsReadOnly(texts.ToArray());
            Settings = Array.AsReadOnly(settings.ToArray());
            UnsupportedLineNumbers = Array.AsReadOnly(unsupported.ToArray());
        }

        /// <summary>현재 파일의 인코딩과 변경 감지 기준을 보관한다. 읽기 잠금은 이 호출 안에서만 유지한다.</summary>
        public static BtplTemplate Load(string path, int codePage = 949)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            string fullPath = Path.GetFullPath(path);
            byte[] bytes;
            using (var file = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (file.Length > MaximumBytes) throw new BtplFormatException("TemplateTooLarge");
                using (var buffer = new MemoryStream())
                {
                    // 길이를 재확인하여 읽는 사이 커진 파일도 제한 밖으로 가져오지 않는다.
                    byte[] chunk = new byte[8192];
                    int count;
                    while ((count = file.Read(chunk, 0, chunk.Length)) != 0)
                    {
                        if (buffer.Length + count > MaximumBytes) throw new BtplFormatException("TemplateTooLarge");
                        buffer.Write(chunk, 0, count);
                    }
                    bytes = buffer.ToArray();
                }
            }
            Encoding encoding = StrictEncoding(codePage);
            int offset = BomOffset(bytes, encoding.CodePage);
            try { return new BtplTemplate(SplitLines(encoding.GetString(bytes, offset, bytes.Length - offset)), fullPath, encoding.CodePage, Hash(bytes)); }
            catch (DecoderFallbackException) { throw new BtplFormatException("InvalidEncoding"); }
        }

        /// <summary>파일 기준이 없는 새 객체다. 첫 저장은 SaveAs를 사용한다. 전체 BTPL 의미를 검사하지 않는다.</summary>
        public static BtplTemplate Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            return new BtplTemplate(SplitLines(text), null, 949, null);
        }

        /// <summary>화면 원문을 반영하되 현재 경로·인코딩·마지막 저장 기준을 유지한다.</summary>
        public BtplTemplate WithRawText(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var changed = Copy(SplitLines(text));
            // 원문 편집으로 두 인수 Q 차단을 우회하지 못하게 한다.
            changed.CheckNoGapSetting();
            return changed;
        }

        /// <summary>편집하지 않은 내용은 줄끝까지 그대로 반환한다. SDK용 정규화는 프린터 API가 한다.</summary>
        public string ToBtpl() => string.Concat(lines.Select(line => line.Text + line.Ending));

        public BtplTemplate WithText(int index, BtplTextElement text)
        {
            BtplTextElement current = TextAt(index);
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (current.Alignment != text.Alignment || current.Rotation != text.Rotation)
                throw new BtplFormatException("LockedTextArguments", current.TemplateLine);
            // 교체 객체를 통째로 직렬화하지 않는다. 같은 값의 원래 숫자 표기와 정렬/방향을 남긴다.
            var changed = current.WithPosition(text.X, text.Y).WithSize(text.Size).WithWeight(text.Weight).WithPayload(text.Payload);
            return ReplaceLine(current.TemplateLine.Value - 1, changed.ToLine());
        }

        /// <summary>명확한 단일 서식의 E 직전에만 추가한다. 여러 서식의 추가 위치는 원문으로 지정한다.</summary>
        public BtplTemplate AddText(BtplTextElement text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            int end = SingleFrameEnd();
            var copy = lines.ToList();
            copy.Insert(end, new TextLine(text.ToLine(), NewLineEnding()));
            return Copy(copy.ToArray());
        }

        public BtplTemplate RemoveText(int index)
        {
            int sourceIndex = TextAt(index).TemplateLine.Value - 1;
            return Copy(lines.Where((line, position) => position != sourceIndex).ToArray());
        }

        /// <summary>요청한 Q/W/S/H만 수정한다. 중복 대상이나 추가 위치를 추측하지 않는다.</summary>
        public BtplTemplate WithSetting(BtplSetting setting)
        {
            if (setting == null) throw new ArgumentNullException(nameof(setting));
            // Settings에서 읽은 기존 2인수 Q 객체를 다시 넘겨도 2인수 Q를 만들지 않는다.
            if (setting.Command == "^Q" && setting.Parameters.Split(',').Length != 3) throw new BtplFormatException("GapLabelSetting", setting.TemplateLine);
            int index = SettingIndex(setting.Command);
            if (index >= 0)
            {
                var current = Settings.First(value => value.TemplateLine == index + 1);
                // Q도 인수별로 비교한다. 같은 수치와 방향은 기존 표기를 남기고 바뀐 인수만 교체한다.
                string[] original = current.Parameters.Split(',');
                string[] replacement = setting.Parameters.Split(',');
                for (int argument = 0; argument < Math.Min(original.Length, replacement.Length); argument++)
                {
                    string currentNumber = original[argument].TrimEnd('+', '-');
                    string requestedNumber = replacement[argument].TrimEnd('+', '-');
                    bool sameDirection = original[argument].Substring(currentNumber.Length)
                        == replacement[argument].Substring(requestedNumber.Length);
                    if (sameDirection && decimal.Parse(currentNumber, CultureInfo.InvariantCulture)
                        == decimal.Parse(requestedNumber, CultureInfo.InvariantCulture)) replacement[argument] = original[argument];
                }
                return ReplaceLine(index, setting.Command + string.Join(",", replacement));
            }
            SingleFrameEnd();
            int start = Array.FindIndex(lines, line => line.Text == "^L");
            var copy = lines.ToList();
            copy.Insert(start, new TextLine(setting.ToLine(), NewLineEnding()));
            return Copy(copy.ToArray());
        }

        public BtplTemplate RemoveSetting(string command)
        {
            CheckCommand(command);
            int index = SettingIndex(command);
            return index < 0 ? this : Copy(lines.Where((line, position) => position != index).ToArray());
        }

        /// <summary>기존 ^Q의 첫 인수만 수정한다. 블랙마크 용지는 피치가 아니라 마크를 뺀 길이(피치 − 마크 폭)다.</summary>
        /// <remarks>마크 폭·위치 인수는 보존하고 누락 설정을 만들지 않는다. 두 인수 ^Q가 남은 서식은 GapLabelSetting으로 거부한다.</remarks>
        public BtplTemplate WithLabelLength(decimal millimetres)
        {
            CheckMillimetres(millimetres);
            BtplSetting current = ExistingSetting("^Q");
            string[] parts = current.Parameters.Split(',');
            if (ReadMillimetres(parts[0], true) == millimetres) return this;
            parts[0] = millimetres.ToString(CultureInfo.InvariantCulture);
            return WithSetting(new BtplSetting("^Q", string.Join(",", parts)));
        }

        public BtplTemplate WithLabelWidth(decimal millimetres)
        {
            CheckMillimetres(millimetres);
            BtplSetting current = ExistingSetting("^W");
            if (ReadMillimetres(current.Parameters, true) == millimetres) return this;
            return WithSetting(new BtplSetting("^W", millimetres.ToString(CultureInfo.InvariantCulture)));
        }

        /// <summary>현재 파일이 마지막 읽기/저장 기준과 같을 때만 갱신한다. 삭제된 원본을 재생성하지 않는다.</summary>
        public BtplTemplate Save()
        {
            if (FilePath == null) throw new InvalidOperationException("현재 파일이 없습니다. SaveAs로 먼저 저장하세요.");
            return Store(FilePath, true, true);
        }

        /// <summary>다른 TXT로 저장한다. 덮어쓰기는 프로그램이 확인하여 true를 명시한 경우에만 허용한다.</summary>
        public BtplTemplate SaveAs(string path, bool overwrite = false)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            string target = Path.GetFullPath(path);
            if (string.Equals(target, FilePath, StringComparison.OrdinalIgnoreCase)) throw new BtplFormatException("SameSavePath");
            return Store(target, overwrite, false);
        }

        private BtplTemplate Store(string target, bool overwrite, bool checkSource)
        {
            // 읽어 온 두 인수 Q를 그대로 다시 저장하지 않는다. 세 인수로 고친 뒤 저장한다.
            CheckNoGapSetting();
            byte[] bytes;
            try { bytes = StrictEncoding(CodePage).GetBytes(ToBtpl()); }
            catch (EncoderFallbackException) { throw new BtplFormatException("InvalidEncoding"); }
            if (bytes.Length > MaximumBytes) throw new BtplFormatException("TemplateTooLarge");
            // 파일 확정 뒤 해석 오류로 실패를 보고하지 않도록 반환 객체까지 먼저 준비한다.
            var stored = new BtplTemplate(lines, target, CodePage, Hash(bytes));
            string temporary = Path.Combine(Path.GetDirectoryName(target), ".btpl-" + Guid.NewGuid().ToString("N") + ".tmp");
            bool created = false;
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    created = true;
                    file.Write(bytes, 0, bytes.Length);
                    file.Flush(true);
                }
                if (checkSource)
                {
                    CheckSource();
                    File.Replace(temporary, target, null, false);
                }
                else if (!overwrite)
                {
                    // Exists 검사 후 Replace하면 경합 중 생긴 파일도 덮어쓴다. Move 자체로 생성을 확정한다.
                    File.Move(temporary, target);
                }
                else if (File.Exists(target)) File.Replace(temporary, target, null, false);
                else File.Move(temporary, target);
                created = false;
                return stored;
            }
            catch (DirectoryNotFoundException)
            {
                // 임시 파일 준비 전에 원본 폴더가 사라져도 Save는 SourceChanged로 알린다.
                // SaveAs의 잘못된 새 경로는 원본 충돌과 구분하여 기존 파일 오류를 전달한다.
                if (checkSource) CheckSource();
                throw;
            }
            finally
            {
                if (created)
                {
                    // 임시 파일 정리 실패로 최초 저장 오류를 가리지 않는다.
                    try { File.Delete(temporary); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        private void CheckSource()
        {
            byte[] current;
            try
            {
                using (var file = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var sha = SHA256.Create()) current = sha.ComputeHash(file);
            }
            catch (FileNotFoundException) { throw new BtplSaveConflictException(); }
            catch (DirectoryNotFoundException) { throw new BtplSaveConflictException(); }
            if (!current.SequenceEqual(savedHash)) throw new BtplSaveConflictException();
        }

        private BtplTemplate Copy(TextLine[] changed) => new BtplTemplate(changed, FilePath, CodePage, savedHash);
        private BtplTemplate ReplaceLine(int index, string text)
        {
            if (lines[index].Text == text) return this;
            var copy = (TextLine[])lines.Clone();
            copy[index] = new TextLine(text, lines[index].Ending);
            return Copy(copy);
        }

        private BtplTextElement TextAt(int index)
        {
            if (index < 0 || index >= TextElements.Count) throw new ArgumentOutOfRangeException(nameof(index));
            return TextElements[index];
        }

        private int SingleFrameEnd()
        {
            var starts = Enumerable.Range(0, lines.Length).Where(index => lines[index].Text == "^L").ToArray();
            var ends = Enumerable.Range(0, lines.Length).Where(index => lines[index].Text == "E").ToArray();
            if (starts.Length != 1 || ends.Length != 1 || ends[0] <= starts[0]) throw new BtplFormatException("AmbiguousFrame");
            return ends[0];
        }

        private int SettingIndex(string command)
        {
            var matches = Enumerable.Range(0, lines.Length).Where(index => lines[index].Text.StartsWith(command, StringComparison.Ordinal)).ToArray();
            if (matches.Length > 1) throw new BtplFormatException("AmbiguousSetting");
            if (matches.Length == 0) return -1;
            // 읽을 수 없는 기존 설정을 편의 API가 조용히 바꾸거나 중복 추가하지 않는다.
            if (!Settings.Any(setting => setting.TemplateLine == matches[0] + 1)) throw new BtplFormatException("InvalidArguments", matches[0] + 1);
            return matches[0];
        }

        private void CheckNoGapSetting()
        {
            // 펌웨어가 공백·빈 인수를 어떻게 읽는지 모르므로 세 인수로 해석된 Q 줄만 남긴다.
            for (int index = 0; index < lines.Length; index++)
            {
                if (!lines[index].Text.TrimStart().StartsWith("^Q", StringComparison.Ordinal)) continue;
                bool blackMark = Settings.Any(setting => setting.TemplateLine == index + 1 && setting.Command == "^Q"
                    && setting.Parameters.Split(',').Length == 3);
                if (!blackMark) throw new BtplFormatException("GapLabelSetting", index + 1);
            }
        }

        private BtplSetting ExistingSetting(string command)
        {
            int index = SettingIndex(command);
            if (index < 0) throw new BtplFormatException("MissingSetting");
            return Settings.First(setting => setting.TemplateLine == index + 1);
        }

        private string NewLineEnding() => lines.Select(line => line.Ending).FirstOrDefault(ending => ending.Length != 0) ?? "\r\n";

        private static TextLine[] SplitLines(string text)
        {
            var result = new List<TextLine>();
            int start = 0;
            for (int index = 0; index < text.Length; index++)
            {
                if (text[index] != '\r' && text[index] != '\n') continue;
                int endingLength = text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1;
                result.Add(new TextLine(text.Substring(start, index - start), text.Substring(index, endingLength)));
                index += endingLength - 1;
                start = index + 1;
            }
            if (start < text.Length) result.Add(new TextLine(text.Substring(start), ""));
            return result.ToArray();
        }

        private static bool TryReadSetting(string raw, int line, out BtplSetting setting)
        {
            setting = null;
            if (raw.Length < 2 || !new[] { "^Q", "^W", "^S", "^H" }.Contains(raw.Substring(0, 2))) return false;
            try { setting = new BtplSetting(raw.Substring(0, 2), raw.Substring(2), line); return true; }
            catch (BtplFormatException) { return false; }
        }

        internal static void CheckSetting(BtplSetting setting)
        {
            CheckCommand(setting.Command);
            string value = setting.Parameters;
            if (setting.Command == "^Q")
            {
                string[] parts = value.Split(',');
                if (parts.Length < 2 || parts.Length > 3) throw new BtplFormatException("InvalidArguments", setting.TemplateLine);
                // 두 인수 Q는 프린터 라벨 유형을 Gap으로 바꾼다. 편집으로 새로 만들지 않고, 기존 원문 읽기(행 번호 있음)만 허용한다.
                if (parts.Length == 2 && setting.TemplateLine == null) throw new BtplFormatException("GapLabelSetting");
                ReadMillimetres(parts[0], true);
                ReadMillimetres(parts[1], false);
                if (parts.Length == 3)
                {
                    string offset = parts[2];
                    if (offset.EndsWith("+", StringComparison.Ordinal) || offset.EndsWith("-", StringComparison.Ordinal)) offset = offset.Substring(0, offset.Length - 1);
                    ReadMillimetres(offset, false);
                }
            }
            else if (setting.Command == "^W") ReadMillimetres(value, true);
            else
            {
                int number;
                int maximum = setting.Command == "^S" ? 7 : 19;
                int minimum = setting.Command == "^S" ? 2 : 0;
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number) || number < minimum || number > maximum)
                    throw new BtplFormatException("InvalidArguments", setting.TemplateLine);
            }
        }

        private static void CheckCommand(string command)
        {
            if (!new[] { "^Q", "^W", "^S", "^H" }.Contains(command)) throw new BtplFormatException("UnsupportedCommand");
        }
        private static void CheckMillimetres(decimal value) { if (value <= 0) throw new BtplFormatException("InvalidArguments"); }
        private static decimal ReadMillimetres(string value, bool positive)
        {
            decimal number;
            if (!Regex.IsMatch(value, "\\A[0-9]+(?:\\.[0-9]+)?\\z", RegexOptions.CultureInvariant)
                || !decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number)
                || (positive ? number <= 0 : number < 0)) throw new BtplFormatException("InvalidArguments");
            return number;
        }
        private static Encoding StrictEncoding(int codePage) => Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        private static byte[] Hash(byte[] bytes) { using (var sha = SHA256.Create()) return sha.ComputeHash(bytes); }
        private static int BomOffset(byte[] bytes, int codePage)
        {
            foreach (int bomPage in new[] { 12000, 12001, 65001, 1200, 1201 })
            {
                byte[] bom = Encoding.GetEncoding(bomPage).GetPreamble();
                if (bom.Length == 0 || !bytes.Take(bom.Length).SequenceEqual(bom)) continue;
                if (bomPage != codePage) throw new BtplFormatException("InvalidEncoding");
                return bom.Length;
            }
            return 0;
        }
    }
}
