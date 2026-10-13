using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace U2BIO.BT003PROLabelPrinting
{
    /// <summary>데이터 한 행에 대한 검사 결과다. 오류가 없으면 IsValid가 true다.</summary>
    public sealed class RowValidationResult
    {
        public int RowIndex { get; }
        public bool IsValid => !Issues.Any(x => x.Severity == ValidationSeverity.Error);
        public IReadOnlyList<ValidationIssue> Issues { get; }

        internal RowValidationResult(int rowIndex, IEnumerable<ValidationIssue> issues)
        {
            RowIndex = rowIndex;
            Issues = Array.AsReadOnly(issues.ToArray());
        }
    }

    /// <summary>공통 서식 오류와 각 데이터 행의 검사 결과다.</summary>
    public sealed class BatchValidationResult
    {
        public bool IsValid => !CommonIssues.Any(x => x.Severity == ValidationSeverity.Error) && Rows.All(x => x.IsValid);
        public IReadOnlyList<ValidationIssue> CommonIssues { get; }
        public IReadOnlyList<RowValidationResult> Rows { get; }

        internal BatchValidationResult(IEnumerable<ValidationIssue> common, IEnumerable<RowValidationResult> rows)
        {
            CommonIssues = Array.AsReadOnly(common.ToArray());
            Rows = Array.AsReadOnly(rows.ToArray());
        }
    }

    /// <summary>한 행의 텍스트와 검사 결과다. 오류가 있으면 BtplText는 null이며 부분 미리보기만 가능하다.</summary>
    public sealed class BtplRenderResult
    {
        public RowValidationResult Validation { get; }
        public bool IsValid => Validation.IsValid;
        public bool CanPreview => TextElements.Count > 0;
        public bool IsPartialPreview => CanPreview && !IsValid;
        public IReadOnlyList<BtplTextElement> TextElements { get; }
        public IReadOnlyList<ValidationIssue> Issues => Validation.Issues;
        public string BtplText { get; }

        internal BtplRenderResult(RowValidationResult validation, IEnumerable<BtplTextElement> texts, string btpl)
        {
            Validation = validation;
            TextElements = Array.AsReadOnly(texts.ToArray());
            BtplText = btpl;
        }
    }

    /// <summary>데이터를 텍스트에 대입하여 검사·미리보기 BTPL을 만든다. 프린터나 SDK에는 접근하지 않는다.</summary>
    public static class BtplProcessor
    {
        internal static readonly Regex Variables = BtplSyntax.Variables;
        private static ValidationIssue Issue(string code, int? row, int? line, string variable = null, string column = null, ValidationSeverity severity = ValidationSeverity.Error) => new ValidationIssue(code, line, row, variable, column, severity);
        internal static List<ValidationIssue> Common(BtplPrintRequest request)
        {
            var issues = new List<ValidationIssue>();
            try
            {
                request.Template.CheckSettings();
            }
            catch (BtplFormatException e)
            {
                issues.AddRange(e.Issues);
            }

            if (request.RowCount == 0)
                issues.Add(Issue("NoData", null, null));
            foreach (var variable in request.References)
            {
                BtplBinding binding;
                Type type;
                if (!request.Bindings.Variables.TryGetValue(variable.Key, out binding))
                    issues.Add(Issue("MissingBinding", null, variable.Value, variable.Key));
                else if (!request.Columns.TryGetValue(binding.Column, out type))
                    issues.Add(Issue("MissingColumn", null, variable.Value, variable.Key, binding.Column));
                else if (!BtplPrintRequest.SupportedType(type))
                    issues.Add(Issue("UnsupportedColumnType", null, variable.Value, variable.Key, binding.Column));
                else if (binding.Format != null && (type == typeof(string) || type == typeof(bool)))
                    issues.Add(Issue("FormatNotSupported", null, variable.Value, variable.Key, binding.Column));
            }

            foreach (var item in request.Bindings.Variables.Where(x => !request.References.ContainsKey(x.Key)))
                issues.Add(Issue("UnusedBinding", null, null, item.Key, item.Value.Column, ValidationSeverity.Warning));
            return issues;
        }

        private static string Value(object value, BtplBinding binding)
        {
            bool blank = value == null || ReferenceEquals(value, DBNull.Value) || (value is string && string.IsNullOrWhiteSpace((string)value));
            if (blank)
            {
                if (binding.NullText == null)
                    throw new BtplFormatException("EmptyValue");
                return binding.NullText;
            }

            if (!BtplPrintRequest.SupportedType(value.GetType()))
                throw new BtplFormatException("UnsupportedValueType");
            if ((value is double && (double.IsNaN((double)value) || double.IsInfinity((double)value))) || (value is float && (float.IsNaN((float)value) || float.IsInfinity((float)value))))
                throw new BtplFormatException("NonFiniteValue");
            if (value is string)
                return (string)value;
            if (value is bool)
                return ((bool)value).ToString();
            try
            {
                return ((IFormattable)value).ToString(binding.Format, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                throw new BtplFormatException("InvalidFormat");
            }
        }

        internal static BtplRenderResult RenderCore(BtplPrintRequest request, int rowIndex, List<ValidationIssue> common)
        {
            var issues = new List<ValidationIssue>(common);
            var row = request.Rows[rowIndex];
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var failed = new HashSet<string>(common.Where(x => x.Severity == ValidationSeverity.Error && x.Variable != null).Select(x => x.Variable), StringComparer.Ordinal);
            if (!row.Available)
                issues.Add(Issue("InvalidDataRow", rowIndex, null));
            foreach (var item in request.References)
            {
                if (failed.Contains(item.Key))
                    continue;
                if (!row.Available)
                {
                    failed.Add(item.Key);
                    continue;
                }

                var binding = request.Bindings.Variables[item.Key];
                object value;
                try
                {
                    if (!row.Values.TryGetValue(binding.Column, out value))
                        throw new BtplFormatException("UnsupportedValueType");
                    string text = Value(value, binding);
                    BtplSyntax.CheckLiteral(text, item.Value);
                    values.Add(item.Key, text);
                }
                catch (BtplFormatException e)
                {
                    failed.Add(item.Key);
                    issues.Add(Issue(e.Code, rowIndex, item.Value, item.Key, binding.Column));
                }
            }

            var output = new List<BtplTextElement>();
            for (int i = 0; i < request.Template.TextElements.Count; i++)
            {
                var text = request.Template.TextElements[i];
                if (Variables.Matches(text.Payload).Cast<Match>().Any(m => failed.Contains(m.Value)))
                    continue;
                try
                {
                    string payload = Variables.Replace(text.Payload, m => values[m.Value]);
                    BtplSyntax.CheckLiteral(payload, text.Line);
                    BtplLineAdvance advance;
                    request.RenderOptions.LineAdvances.TryGetValue(i, out advance);
                    output.AddRange(text.WithPayload(payload).ExpandLines(advance));
                }
                catch (BtplFormatException e)
                {
                    issues.Add(Issue(e.Code, rowIndex, text.Line));
                }
            }

            // 오류가 있으면 미리보기 가능한 텍스트만 반환한다. 인쇄용 완성 문자열은 만들지 않는다.
            string btpl = null;
            if (!issues.Any(x => x.Severity == ValidationSeverity.Error))
            {
                try
                {
                    btpl = new BtplTemplate(request.Template.Settings, output, null).ToBtpl();
                }
                catch (BtplFormatException e)
                {
                    issues.AddRange(e.Issues.Select(x => Issue(x.Code, rowIndex, x.TemplateLine)));
                }
            }

            return new BtplRenderResult(new RowValidationResult(rowIndex, issues), output, btpl);
        }

        /// <summary>공통 문제와 모든 행을 검사한다. 실제 SDK 호출이나 일괄 인쇄는 하지 않는다.</summary>
        public static BatchValidationResult Validate(BtplPrintRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            var common = Common(request);
            return new BatchValidationResult(common, Enumerable.Range(0, request.RowCount).Select(i => RenderCore(request, i, common).Validation));
        }

        /// <summary>지정 행을 검사하고 텍스트를 치환한다. IsValid 확인 후 BtplText를 Print에 전달한다.</summary>
        public static BtplRenderResult Render(BtplPrintRequest request, int rowIndex)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            if (rowIndex < 0 || rowIndex >= request.RowCount)
                throw new ArgumentOutOfRangeException(nameof(rowIndex));
            return RenderCore(request, rowIndex, Common(request));
        }
    }
}
