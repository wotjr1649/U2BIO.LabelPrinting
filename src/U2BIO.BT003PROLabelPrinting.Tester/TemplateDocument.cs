using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace U2BIO.BT003PROLabelPrinting.Tester
{
    internal sealed class TemplateDocument
    {
        internal BtplTemplate Template { get; private set; }
        internal bool IsDirty { get; private set; }
        internal DateTime? LastLoadedOrSaved { get; private set; }
        internal string DisplayPath => Template == null ? "열린 서식 없음" : Template.FilePath ?? "저장 안 됨";

        // DLL 호출이 끝난 뒤에만 상태를 바꿔 예외가 나면 이전 서식이 그대로 남는다.
        internal void Open(string path) => Adopt(BtplTemplate.Load(path));

        internal void Reload() => Open(Current.FilePath);

        internal void Apply(Func<BtplTemplate, BtplTemplate> edit)
        {
            BtplTemplate result = edit(Current) ?? throw new InvalidOperationException("편집 결과가 없습니다.");
            // 값이 같아 DLL이 같은 객체를 돌려주면 수정됨으로 표시하지 않는다.
            if (ReferenceEquals(result, Template)) return;
            Template = result;
            IsDirty = true;
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

        private BtplTemplate Current => Template ?? throw new InvalidOperationException("열린 서식이 없습니다.");

        private void Adopt(BtplTemplate template)
        {
            Template = template;
            IsDirty = false;
            LastLoadedOrSaved = DateTime.Now;
        }
    }
}
