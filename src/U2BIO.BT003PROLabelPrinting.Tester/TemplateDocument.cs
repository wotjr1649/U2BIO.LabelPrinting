using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace U2BIO.BT003PROLabelPrinting.Tester
{
    internal enum InkState { Inside, Unsafe, Cut, Blank, Skipped, Failed }

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

        // 문서에 넣지 않고 DLL에 적용해 보기만 한다. 같은 객체가 돌아오면 서식 값이 이 설정과 같다. 적용할 수 없는 구조면 BtplFormatException이 난다.
        internal static bool SameSetting(BtplTemplate template, BtplSetting setting) => ReferenceEquals(template.WithSetting(setting), template);

        // ^Q 세 인수를 계산 입력으로 옮기는 표시 매핑이다. 방향이 '+'가 아니거나 숫자가 아니면 옮기지 않는다.
        // 그대로 다시 만들어지는지(0.5 mm 단위, 표기)는 SameSetting 왕복으로만 판단한다.
        internal static bool TryReadLabel(string parameters, out decimal segment, out decimal mark, out decimal z)
        {
            segment = mark = z = 0;
            string[] parts = parameters?.Split(',');
            return parts != null && parts.Length == 3 && parts[2].EndsWith("+", StringComparison.Ordinal)
                && TryMm(parts[0], out segment) && TryMm(parts[1], out mark) && TryMm(parts[2].Substring(0, parts[2].Length - 1), out z);
        }

        internal static bool TryReadWidth(string parameters, out decimal liner)
        {
            liner = 0;
            return parameters != null && TryMm(parameters, out liner);
        }

        private static bool TryMm(string text, out decimal value) =>
            decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);

        // 미리보기는 정렬·회전을 그리지 않으므로 그런 줄은 영역 판정에서 뺀다.
        internal static bool CanJudge(BtplTextElement text) => text.Alignment == 0 && text.Rotation == 0;

        // 잉크 범위(문구 그림 기준 반열림)를 서식 좌표로 옮겨 영역(반열림) 밖으로 나간 방향과 거리를 적는다. 안쪽이면 null이다.
        internal static string Outside(Rectangle ink, int x, int y, BtplArea area)
        {
            var parts = new List<string>();
            long top = area.Top - ((long)y + ink.Top);
            long bottom = (long)y + ink.Bottom - area.Bottom;
            long left = area.Left - ((long)x + ink.Left);
            long right = (long)x + ink.Right - area.Right;
            if (top > 0) parts.Add("위 " + top + " dot");
            if (bottom > 0) parts.Add("아래 " + bottom + " dot");
            if (left > 0) parts.Add("왼쪽 " + left + " dot");
            if (right > 0) parts.Add("오른쪽 " + right + " dot");
            return parts.Count == 0 ? null : string.Join(", ", parts);
        }

        // 라벨 밖이면 잘림, 라벨 안이지만 안전 영역 밖이면 주의다. 그리지 못한 줄은 정상으로 보지 않는다.
        internal static InkState Classify(BtplTextElement text, TextRaster raster, BtplArea label, BtplArea safe, out string detail)
        {
            detail = null;
            if (!CanJudge(text)) return InkState.Skipped;
            if (raster == null || raster.Failure != null)
            {
                detail = raster?.Failure ?? "그림 없음";
                return InkState.Failed;
            }
            if (!raster.Ink.HasValue) return InkState.Blank;
            detail = Outside(raster.Ink.Value, text.X, text.Y, label);
            if (detail != null) return InkState.Cut;
            detail = Outside(raster.Ink.Value, text.X, text.Y, safe);
            return detail != null ? InkState.Unsafe : InkState.Inside;
        }

        // 화면 간격이 needPixels 이상이 될 때까지 눈금 간격(dot)을 두 배로 늘린다. 캔버스 최대 크기를 넘으면 더 늘리지 않는다.
        internal static int RulerStep(int start, float scale, float needPixels)
        {
            long step = Math.Max(1, start);
            while (step * scale < needPixels && step < MaxRulerStep) step *= 2;
            return (int)Math.Min(step, MaxRulerStep);
        }

        private const long MaxRulerStep = 1 << 20;

        // 원하는 위치(desired, 같은 순서로 돌려준다)에 높이 height인 글자를 겹치지 않게 놓는다. 아래로 밀다가 maxY를 넘으면
        // 마지막부터 위로 되돌린다. 모두 담을 높이가 없으면 null이다.
        internal static float[] StackLabels(float[] desired, float height, float minY, float maxY)
        {
            int[] order = Enumerable.Range(0, desired.Length).OrderBy(i => desired[i]).ToArray();
            if (order.Length * height > maxY - minY) return null;
            var places = new float[desired.Length];
            float next = minY;
            foreach (int i in order)
            {
                places[i] = Math.Max(desired[i], next);
                next = places[i] + height;
            }
            float limit = maxY;
            for (int k = order.Length - 1; k >= 0; k--)
            {
                places[order[k]] = Math.Min(places[order[k]], limit - height);
                limit = places[order[k]];
            }
            return places;
        }

        // 미리보기 아래 설명 줄 문구다. 좌표는 서식 dot이며 끝 좌표는 반열림이다. 경고 판정과 별개로 표시만 한다.
        internal static string DescribeText(BtplTextElement text, TextRaster raster)
        {
            string body;
            if (raster == null || raster.Failure != null) body = "판정 불가: " + (raster?.Failure ?? "그림 없음");
            else if (raster.Image == null) body = "잉크 없음(빈 문구)";
            else
            {
                body = "셀 Y" + text.Y + "~" + ((long)text.Y + raster.Image.Height) + " · 그림 폭 " + raster.Image.Width;
                body += raster.Ink.HasValue
                    ? " · 잉크 Y" + ((long)text.Y + raster.Ink.Value.Top) + "~" + ((long)text.Y + raster.Ink.Value.Bottom)
                        + ", X" + ((long)text.X + raster.Ink.Value.Left) + "~" + ((long)text.X + raster.Ink.Value.Right)
                    : " · 잉크 없음";
            }
            return "줄 " + text.TemplateLine + " · X" + text.X + " Y" + text.Y + " · " + body + (CanJudge(text) ? "" : " (정렬·회전 미반영 참고값)");
        }

        // 경고 줄 목록이다. 한 줄은 한 목록에만 들어간다.
        internal static void CheckInk(BtplTemplate template, BtplArea label, BtplArea safe, Func<BtplTextElement, TextRaster> rasterOf,
            List<string> cut, List<string> unsafeLines, List<string> skipped, List<string> failed)
        {
            foreach (BtplTextElement text in template.TextElements)
            {
                string line = "줄 " + text.TemplateLine;
                InkState state = Classify(text, CanJudge(text) ? rasterOf(text) : null, label, safe, out string detail);
                if (state == InkState.Cut) cut.Add(line + "(" + detail + ")");
                else if (state == InkState.Unsafe) unsafeLines.Add(line + "(" + detail + ")");
                else if (state == InkState.Skipped) skipped.Add(line);
                else if (state == InkState.Failed) failed.Add(line + "(" + detail + ")");
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
