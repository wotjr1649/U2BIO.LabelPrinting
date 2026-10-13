using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using System.Text;
using System.Xml;

namespace U2BIO.BT003PROLabelPrinting
{
    /// <summary>서식 검사에서 인쇄를 막는 오류와 참고 경고를 구분한다.</summary>
    public enum ValidationSeverity
    {
        Error,
        Warning
    }

    /// <summary>문제의 코드와 서식 행·데이터 행·변수 위치를 전달한다. 입력 원문은 메시지에 넣지 않는다.</summary>
    public sealed class ValidationIssue
    {
        public string Code { get; }
        public ValidationSeverity Severity { get; }
        public int? RowIndex { get; }
        public int? TemplateLine { get; }
        public string Variable { get; }
        public string Column { get; }
        public string Message { get; }

        internal ValidationIssue(string code, int? line, int? row = null, string variable = null, string column = null, ValidationSeverity severity = ValidationSeverity.Error)
        {
            Code = code;
            TemplateLine = line;
            Severity = severity;
            RowIndex = row;
            Variable = variable;
            Column = column;
            Message = "BTPL validation failed: " + code + ".";
        }
    }

    /// <summary>지원하는 서식 규칙을 벗어난 입력이다. Code와 Issues로 원인을 확인한다.</summary>
    public sealed class BtplFormatException : FormatException
    {
        public string Code { get; }
        public IReadOnlyList<ValidationIssue> Issues { get; }

        internal BtplFormatException(string code, int? line = null) : this(new[] { new ValidationIssue(code, line) })
        {
        }

        internal BtplFormatException(IEnumerable<ValidationIssue> issues) : base("BTPL validation failed. See Codes and TemplateLine; input values are omitted.")
        {
            var copy = issues.ToArray();
            Code = copy[0].Code;
            Issues = Array.AsReadOnly(copy);
        }
    }

    /// <summary>읽은 파일이 외부에서 바뀌어 덮어쓰기를 중단한 경우다. 다시 읽거나 다른 경로에 저장한다.</summary>
    public sealed class BtplSaveConflictException : IOException
    {
        public string Code => "SourceChanged";

        internal BtplSaveConflictException() : base("The source file changed or was removed. Reload or save to a different path.")
        {
        }
    }

    /// <summary>서식 앞부분의 설정 명령 하나다. 명령과 인자를 원래 표기로 보관한다.</summary>
    public sealed class BtplSetting
    {
        public string Command { get; }
        public string Parameters { get; }
        internal int? Line { get; }

        public BtplSetting(string command, string parameters) : this(command, parameters, null)
        {
        }

        internal BtplSetting(string command, string parameters, int? line)
        {
            Command = command ?? throw new ArgumentNullException(nameof(command));
            Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            Line = line;
        }

        internal string ToLine() => Command + Parameters;
    }

    /// <summary>텍스트 명령 하나다. 수정 메서드는 새 객체를 반환하며 기존 서식을 바꾸지 않는다.</summary>
    public sealed class BtplTextElement
    {
        public string Command { get; }
        public int X { get; }
        public int Y { get; }
        public IReadOnlyList<string> Arguments { get; }
        public string Payload { get; }
        internal int? Line { get; }

        private readonly string xLiteral, yLiteral;
        internal string XLiteral => xLiteral;
        internal string YLiteral => yLiteral;

        public BtplTextElement(string command, int x, int y, IEnumerable<string> arguments, string payload) : this(command, x.ToString(CultureInfo.InvariantCulture), y.ToString(CultureInfo.InvariantCulture), arguments, payload, null)
        {
            BtplSyntax.CheckText(this, true);
        }

        internal BtplTextElement(string command, string x, string y, IEnumerable<string> arguments, string payload, int? line)
        {
            Command = command ?? throw new ArgumentNullException(nameof(command));
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
            if (arguments == null)
                throw new ArgumentNullException(nameof(arguments));
            Arguments = Array.AsReadOnly(arguments.ToArray());
            Line = line;
            xLiteral = x;
            yLiteral = y;
            X = BtplSyntax.Number(x, 0, int.MaxValue, line);
            Y = BtplSyntax.Number(y, 0, int.MaxValue, line);
        }

        public BtplTextElement WithPosition(int x, int y) => new BtplTextElement(Command, x, y, Arguments, Payload);
        public BtplTextElement WithPayload(string payload)
        {
            var result = new BtplTextElement(Command, xLiteral, yLiteral, Arguments, payload, Line);
            BtplSyntax.CheckText(result, true);
            return result;
        }

        public BtplTextElement WithArgument(int argumentIndex, string value)
        {
            if (argumentIndex < 0 || argumentIndex >= Arguments.Count)
                throw new ArgumentOutOfRangeException(nameof(argumentIndex));
            if (Command == "AW")
                throw new BtplFormatException("LockedTextArguments", Line);
            string[] copy = Arguments.ToArray();
            copy[argumentIndex] = value;
            var result = new BtplTextElement(Command, xLiteral, yLiteral, copy, Payload, Line);
            BtplSyntax.CheckText(result, true);
            return result;
        }

        internal string ToLine() => Command + "," + xLiteral + "," + yLiteral + "," + string.Join(",", Arguments) + "," + Payload;
        internal IReadOnlyList<BtplTextElement> ExpandLines(BtplLineAdvance advance)
        {
            string[] lines = BtplSyntax.SplitLines(Payload);
            if (lines.Length > 1 && (advance == null || (advance.DeltaX == 0 && advance.DeltaY == 0)))
                throw new BtplFormatException("MissingLineAdvance", Line);
            var result = new List<BtplTextElement>();
            for (int k = 0; k < lines.Length; k++)
            {
                try
                {
                    result.Add(k == 0 ? WithPayload(lines[k]) : WithPosition(checked(X + checked(k * advance.DeltaX)), checked(Y + checked(k * advance.DeltaY))).WithPayload(lines[k]));
                }
                catch (OverflowException)
                {
                    throw new BtplFormatException("CoordinateOverflow", Line);
                }
            }

            return result.AsReadOnly();
        }
    }

    /// <summary>여러 줄 텍스트의 다음 줄로 이동할 좌표 차이다.</summary>
    public sealed class BtplLineAdvance
    {
        public int DeltaX { get; }
        public int DeltaY { get; }

        public BtplLineAdvance(int deltaX, int deltaY)
        {
            DeltaX = deltaX;
            DeltaY = deltaY;
        }
    }

    /// <summary>텍스트별 줄 간격이다. 편집 시 새 옵션을 반환하고 텍스트 삭제 시 인덱스를 맞춘다.</summary>
    public sealed class BtplRenderOptions
    {
        public IReadOnlyDictionary<int, BtplLineAdvance> LineAdvances { get; }

        public BtplRenderOptions() : this(new Dictionary<int, BtplLineAdvance>())
        {
        }

        private BtplRenderOptions(IDictionary<int, BtplLineAdvance> values)
        {
            LineAdvances = new ReadOnlyDictionary<int, BtplLineAdvance>(new Dictionary<int, BtplLineAdvance>(values));
        }

        public BtplRenderOptions WithLineAdvance(int textIndex, BtplLineAdvance advance)
        {
            if (textIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(textIndex));
            if (advance == null)
                throw new ArgumentNullException(nameof(advance));
            var copy = LineAdvances.ToDictionary(x => x.Key, x => x.Value);
            copy[textIndex] = advance;
            return new BtplRenderOptions(copy);
        }

        public BtplRenderOptions WithRemovedText(int textIndex)
        {
            if (textIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(textIndex));
            return new BtplRenderOptions(LineAdvances.Where(x => x.Key != textIndex).ToDictionary(x => x.Key > textIndex ? x.Key - 1 : x.Key, x => x.Value));
        }
    }

    /// <summary>변수에 사용할 데이터 열과 값 형식·빈 값 대체 문자열이다.</summary>
    public sealed class BtplBinding
    {
        public string Column { get; }
        public string Format { get; }
        public string NullText { get; }

        public BtplBinding(string column, string format = null, string nullText = null)
        {
            if (string.IsNullOrWhiteSpace(column))
                throw new BtplFormatException("InvalidBindings");
            Column = column;
            Format = format;
            NullText = nullText;
        }
    }

    /// <summary>변수와 열의 연결 정보다. JSON 읽기·저장과 원본 변경 감지를 함께 담당한다.</summary>
    public sealed class BtplBindings
    {
        private const int MaxJsonBytes = 4 * 1024 * 1024;
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        private sealed class JsonBuffer : MemoryStream
        {
            public override void Write(byte[] buffer, int offset, int count)
            {
                if (Position + count > MaxJsonBytes)
                    throw new BtplFormatException("BindingsTooLarge");
                base.Write(buffer, offset, count);
            }

            public override void WriteByte(byte value)
            {
                if (Position >= MaxJsonBytes)
                    throw new BtplFormatException("BindingsTooLarge");
                base.WriteByte(value);
            }
        }

        public IReadOnlyDictionary<string, BtplBinding> Variables { get; }

        private readonly BtplFileOrigin origin;
        internal BtplBindings(IDictionary<string, BtplBinding> variables, BtplFileOrigin source = null)
        {
            Variables = new ReadOnlyDictionary<string, BtplBinding>(new Dictionary<string, BtplBinding>(variables, StringComparer.Ordinal));
            origin = source;
        }

        private static bool VariableName(string value) => value != null && Regex.IsMatch(value, "\\A\\^V[0-9]{2}\\z", RegexOptions.CultureInvariant);
        /// <summary>바인딩 JSON의 구문·키·열 이름을 검사한다. 입력 원문을 예외 메시지에 노출하지 않는다.</summary>
        public static BtplBindings Parse(string json)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json));
            try
            {
                if (Utf8.GetByteCount(json) > MaxJsonBytes)
                    throw new BtplFormatException("BindingsTooLarge");
                BtplJson.CheckEnvelope(json);
                BtplJson.CheckTokens(json, "InvalidBindings");
                var quotas = new XmlDictionaryReaderQuotas
                {
                    MaxDepth = 4,
                    MaxStringContentLength = MaxJsonBytes
                };
                using (var reader = JsonReaderWriterFactory.CreateJsonReader(Utf8.GetBytes(json), quotas))
                {
                    var document = new XmlDocument
                    {
                        XmlResolver = null
                    };
                    document.Load(reader);
                    var root = BtplJson.Object(document.DocumentElement);
                    if (root.Count != 1 || !root.ContainsKey("variables"))
                        throw new BtplFormatException("InvalidBindings");
                    var variables = new Dictionary<string, BtplBinding>(StringComparer.Ordinal);
                    foreach (var item in BtplJson.Object(root["variables"]))
                    {
                        if (!VariableName(item.Key))
                            throw new BtplFormatException("InvalidBindings");
                        var fields = BtplJson.Object(item.Value);
                        if (!fields.ContainsKey("column") || fields.Keys.Any(k => k != "column" && k != "format" && k != "nullText"))
                            throw new BtplFormatException("InvalidBindings");
                        variables.Add(item.Key, new BtplBinding(BtplJson.String(fields["column"], false), fields.ContainsKey("format") ? BtplJson.String(fields["format"], false) : null, fields.ContainsKey("nullText") ? BtplJson.String(fields["nullText"], true) : null));
                    }

                    return new BtplBindings(variables);
                }
            }
            catch (XmlException)
            {
                throw new BtplFormatException("InvalidBindings");
            }
            catch (EncoderFallbackException)
            {
                throw new BtplFormatException("InvalidBindings");
            }
        }

        public static BtplBindings Load(string filePath)
        {
            if (filePath == null)
                throw new ArgumentNullException(nameof(filePath));
            BtplFileOrigin source;
            var parsed = Parse(BtplFileStore.Read(filePath, 65001, out source, MaxJsonBytes));
            return new BtplBindings(parsed.Variables.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal), source);
        }

        private static void WriteString(XmlDictionaryWriter writer, string name, string value)
        {
            writer.WriteStartElement(name);
            writer.WriteAttributeString("type", "string");
            writer.WriteString(value);
            writer.WriteEndElement();
        }

        public string ToJson()
        {
            long minimumBytes = 0;
            try
            {
                foreach (var item in Variables)
                {
                    minimumBytes += Utf8.GetByteCount(item.Value.Column);
                    if (item.Value.Format != null)
                        minimumBytes += Utf8.GetByteCount(item.Value.Format);
                    if (item.Value.NullText != null)
                        minimumBytes += Utf8.GetByteCount(item.Value.NullText);
                    if (minimumBytes > MaxJsonBytes)
                        throw new BtplFormatException("BindingsTooLarge");
                }
            }
            catch (EncoderFallbackException)
            {
                throw new BtplFormatException("InvalidBindings");
            }

            using (var stream = new JsonBuffer())
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Utf8, false, true, "  "))
                {
                    writer.WriteStartElement("root");
                    writer.WriteAttributeString("type", "object");
                    writer.WriteStartElement("variables");
                    writer.WriteAttributeString("type", "object");
                    foreach (var item in Variables.OrderBy(x => x.Key, StringComparer.Ordinal))
                    {
                        writer.WriteStartElement("item", "item");
                        writer.WriteAttributeString("item", item.Key);
                        writer.WriteAttributeString("type", "object");
                        WriteString(writer, "column", item.Value.Column);
                        if (item.Value.Format != null)
                            WriteString(writer, "format", item.Value.Format);
                        if (item.Value.NullText != null)
                            WriteString(writer, "nullText", item.Value.NullText);
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();
                    writer.WriteEndElement();
                }

                return Utf8.GetString(stream.ToArray());
            }
        }

        public BtplBindings Save(string filePath)
        {
            if (filePath == null)
                throw new ArgumentNullException(nameof(filePath));
            string json = ToJson();
            Parse(json);
            var source = BtplFileStore.Save(filePath, json, 65001, origin);
            return new BtplBindings(Variables.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal), source);
        }

        public BtplBindings WithVariable(string variable, BtplBinding binding)
        {
            if (!VariableName(variable))
                throw new BtplFormatException("InvalidBindings");
            if (binding == null)
                throw new ArgumentNullException(nameof(binding));
            var copy = Variables.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            copy[variable] = binding;
            return new BtplBindings(copy, origin);
        }

        public BtplBindings RemoveVariable(string variable)
        {
            if (!VariableName(variable))
                throw new BtplFormatException("InvalidBindings");
            var copy = Variables.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            copy.Remove(variable);
            return new BtplBindings(copy, origin);
        }
    }

    /// <summary>편집 서식과 데이터의 불변 스냅샷이다. 이름과 달리 SDK 인쇄를 실행하지 않는다.</summary>
    public sealed class BtplPrintRequest
    {
        public BtplTemplate Template { get; }
        public BtplBindings Bindings { get; }
        public BtplRenderOptions RenderOptions { get; }
        public int RowCount => Rows.Length;

        internal readonly Dictionary<string, int?> References;
        internal readonly Dictionary<string, Type> Columns;
        internal readonly SnapshotRow[] Rows;
        internal sealed class SnapshotRow
        {
            internal readonly bool Available;
            internal readonly Dictionary<string, object> Values;
            internal SnapshotRow(bool available, Dictionary<string, object> values)
            {
                Available = available;
                Values = values;
            }
        }

        private BtplPrintRequest(BtplTemplate template, BtplBindings bindings, DataTable table, IEnumerable<DataRow> rows, BtplRenderOptions options)
        {
            Template = template ?? throw new ArgumentNullException(nameof(template));
            Bindings = bindings ?? new BtplBindings(new Dictionary<string, BtplBinding>());
            RenderOptions = options ?? new BtplRenderOptions();
            if (RenderOptions.LineAdvances.Keys.Any(i => i >= Template.TextElements.Count))
                throw new ArgumentOutOfRangeException(nameof(options));
            References = new Dictionary<string, int?>(StringComparer.Ordinal);
            foreach (var text in template.TextElements)
                foreach (Match match in BtplProcessor.Variables.Matches(text.Payload))
                    if (!References.ContainsKey(match.Value))
                        References.Add(match.Value, text.Line);
            Columns = new Dictionary<string, Type>(StringComparer.Ordinal);
            if (table == null)
            {
                Rows = new[]
                {
                    new SnapshotRow(true, new Dictionary<string, object>(StringComparer.Ordinal))
                };
                return;
            }

            // 사용 열과 지원하는 값만 복사한다. 호출자가 DataTable을 바꿔도 요청의 데이터는 유지된다.
            var needed = new HashSet<string>(References.Keys.Where(k => Bindings.Variables.ContainsKey(k)).Select(k => Bindings.Variables[k].Column), StringComparer.Ordinal);
            var columns = table.Columns.Cast<DataColumn>().Where(c => needed.Contains(c.ColumnName)).ToArray();
            foreach (var column in columns)
                Columns.Add(column.ColumnName, column.DataType);
            Rows = rows.Select(row =>
            {
                var values = new Dictionary<string, object>(StringComparer.Ordinal);
                bool available = row.RowState != DataRowState.Deleted;
                if (available)
                    foreach (var column in columns)
                    {
                        if (!SupportedType(column.DataType))
                            continue;
                        object value = row[column];
                        if (value == null || ReferenceEquals(value, DBNull.Value) || SupportedType(value.GetType()))
                            values.Add(column.ColumnName, value);
                    }

                return new SnapshotRow(available, values);
            }).ToArray();
        }

        internal static bool SupportedType(Type type) => type == typeof(string) || type == typeof(bool) || type == typeof(DateTime) || type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong) || type == typeof(float) || type == typeof(double) || type == typeof(decimal);
        /// <summary>고정 문구 서식의 검사·렌더링 요청을 만든다. 실제 인쇄는 호출 프로그램이 결정한다.</summary>
        public static BtplPrintRequest Fixed(BtplTemplate template, BtplRenderOptions renderOptions = null) => new BtplPrintRequest(template, null, null, null, renderOptions);
        /// <summary>DataRow 한 행의 사용 값을 복사하여 요청을 만든다.</summary>
        public static BtplPrintRequest FromRow(BtplTemplate template, BtplBindings bindings, DataRow row, BtplRenderOptions renderOptions = null)
        {
            if (bindings == null)
                throw new ArgumentNullException(nameof(bindings));
            if (row == null)
                throw new ArgumentNullException(nameof(row));
            return new BtplPrintRequest(template, bindings, row.Table, new[] { row }, renderOptions);
        }

        /// <summary>DataTable의 현재 행과 사용 값을 복사한다. 이후 원본 변경은 렌더링에 반영되지 않는다.</summary>
        public static BtplPrintRequest FromTable(BtplTemplate template, BtplBindings bindings, DataTable data, BtplRenderOptions renderOptions = null)
        {
            if (bindings == null)
                throw new ArgumentNullException(nameof(bindings));
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            return new BtplPrintRequest(template, bindings, data, data.Rows.Cast<DataRow>().ToArray(), renderOptions);
        }
    }
}
