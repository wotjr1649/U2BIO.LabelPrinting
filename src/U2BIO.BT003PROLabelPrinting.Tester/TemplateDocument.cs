using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace U2BIO.BT003PROLabelPrinting.Tester
{
    internal sealed class TemplateDocument
    {
        // 수정 여부는 이 원문과 비교한다. 비표준 숫자 표기(선행 0 등)는 값만 되돌려도 DLL이 표준 표기로 쓰므로 수정됨으로 남는다.
        private string savedText;

        internal BtplTemplate Template { get; private set; }
        internal bool IsDirty { get; private set; }
        internal DateTime? LastLoadedOrSaved { get; private set; }
        internal string DisplayPath => Template == null ? "열린 서식 없음" : Template.FilePath ?? "저장 안 됨";

        // DLL 호출이 끝난 뒤에만 상태를 바꿔 예외가 나면 이전 서식이 그대로 남는다.
        internal void Open(string path) => Adopt(BtplTemplate.Load(path));

        internal void Reload() => Open(Current.FilePath);

        // 저장 여부 확인은 화면이 먼저 한다. 처음 실행한 상태로 되돌린다.
        internal void Close()
        {
            Template = null;
            IsDirty = false;
            LastLoadedOrSaved = null;
            savedText = null;
        }

        internal void Apply(Func<BtplTemplate, BtplTemplate> edit)
        {
            BtplTemplate result = edit(Current) ?? throw new InvalidOperationException("편집 결과가 없습니다.");
            // 값이 같아 DLL이 같은 객체를 돌려주면 수정됨으로 표시하지 않는다.
            if (ReferenceEquals(result, Template)) return;
            Template = result;
            IsDirty = result.ToBtpl() != savedText;
        }

        internal void Save() => Adopt(Current.Save());

        internal void SaveAs(string path, bool overwrite) => Adopt(Current.SaveAs(path, overwrite));

        internal static string PreviewText(string payload, string variableData)
        {
            string[] values = variableData == null ? new string[0] : variableData.Split(new[] { "!!" }, StringSplitOptions.None);
            return Regex.Replace(payload, "\\^V([0-9]{2})", match =>
            {
                int index = int.Parse(match.Groups[1].Value);
                return index < values.Length ? values[index] : match.Value;
            }, RegexOptions.CultureInvariant);
        }

        internal static bool HasGapLabelSetting(BtplTemplate template)
        {
            if (template.Settings.Any(setting => setting.Command == "^Q" && setting.Parameters.Split(',').Length != 3)) return true;
            // 줄 번호는 DLL의 TemplateLine과 같은 줄끝 기준으로 센다.
            string[] lines = Regex.Split(template.ToBtpl(), "\r\n|\n|\r");
            return template.UnsupportedLineNumbers.Any(number => number >= 1 && number <= lines.Length
                && lines[number - 1].TrimStart().StartsWith("^Q", StringComparison.Ordinal));
        }

        // 문서에 넣지 않고 DLL에 적용해 보기만 한다. 같은 객체가 돌아오면 서식 값이 계산값과 같다. 적용할 수 없는 구조면 BtplFormatException이 난다.
        internal static bool SameMedia(BtplTemplate template, BtplBlackMarkMedia media) =>
            ReferenceEquals(template.WithSetting(media.LabelSetting).WithSetting(media.WidthSetting), template);

        // SDK가 LOGFONT 높이를 size*340/72 dot로 계산한다. 실제 잉크 높이가 아니라 참고 상자 높이다.
        internal static long TextHeight(BtplTextElement text) => (long)text.Size * 340 / 72;

        // 미리보기는 정렬·회전을 그리지 않으므로 그런 줄은 영역 판정에서 뺀다.
        internal static bool CanJudge(BtplTextElement text) => text.Alignment == 0 && text.Rotation == 0;

        // 참고 상자(높이 TextHeight, 폭은 화면 글꼴로 잰 추정)가 안전 영역을 벗어난 방향과 거리다. 안쪽이면 null이다.
        internal static string OutsideSafeArea(BtplTextElement text, BtplArea safe, long width)
        {
            var parts = new List<string>();
            long top = safe.Top - (long)text.Y;
            long bottom = text.Y + TextHeight(text) - safe.Bottom;
            long left = safe.Left - (long)text.X;
            long right = text.X + width - safe.Right;
            if (top > 0) parts.Add("위 " + top + " dot");
            if (bottom > 0) parts.Add("아래 " + bottom + " dot");
            if (left > 0) parts.Add("왼쪽 " + left + " dot");
            if (right > 0) parts.Add("오른쪽 " + right + " dot 추정");
            return parts.Count == 0 ? null : string.Join(", ", parts);
        }

        // 경고 줄 목록을 만든다. 정렬·회전 줄은 skipped에만, 안전 영역 밖 줄은 outside에만 들어간다.
        internal static void CheckSafeArea(BtplTemplate template, BtplArea safe, Func<BtplTextElement, long> widthOf,
            List<string> outside, List<string> skipped)
        {
            foreach (BtplTextElement text in template.TextElements)
            {
                string line = "줄 " + text.TemplateLine;
                if (!CanJudge(text))
                {
                    skipped.Add(line);
                    continue;
                }
                string where = OutsideSafeArea(text, safe, widthOf(text));
                if (where != null) outside.Add(line + "(" + where + ")");
            }
        }

        private BtplTemplate Current => Template ?? throw new InvalidOperationException("열린 서식이 없습니다.");

        private void Adopt(BtplTemplate template)
        {
            Template = template;
            IsDirty = false;
            LastLoadedOrSaved = DateTime.Now;
            savedText = template.ToBtpl();
        }
    }
}
