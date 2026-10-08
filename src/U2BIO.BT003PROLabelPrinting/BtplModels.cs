using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;

namespace U2BIO.BT003PROLabelPrinting
{
    public enum ValidationSeverity { Error, Warning }

    public sealed class ValidationIssue
    {
        public string Code { get; }
        public ValidationSeverity Severity { get; }
        public int? RowIndex { get; }
        public int? TemplateLine { get; }
        public string Variable { get; }
        public string Column { get; }
        public string Message { get; }

        internal ValidationIssue(string code, int? line)
        {
            Code = code; TemplateLine = line; Severity = ValidationSeverity.Error;
            Message = "BTPL validation failed: " + code + ".";
        }
    }

    public sealed class BtplFormatException : FormatException
    {
        public string Code { get; }
        public IReadOnlyList<ValidationIssue> Issues { get; }
        internal BtplFormatException(string code, int? line = null)
            : this(new[] { new ValidationIssue(code, line) }) { }
        internal BtplFormatException(IEnumerable<ValidationIssue> issues)
            : base("BTPL validation failed. See Codes and TemplateLine; input values are omitted.")
        {
            var copy = issues.ToArray();
            Code = copy[0].Code; Issues = Array.AsReadOnly(copy);
        }
    }

    public sealed class BtplSaveConflictException : IOException
    {
        public string Code => "SourceChanged";
        internal BtplSaveConflictException() : base("The source file changed or was removed. Reload or save to a different path.") { }
    }

    public sealed class BtplSetting
    {
        public string Command { get; }
        public string Parameters { get; }
        internal int? Line { get; }
        public BtplSetting(string command, string parameters) : this(command, parameters, null) { }
        internal BtplSetting(string command, string parameters, int? line)
        {
            Command = command ?? throw new ArgumentNullException(nameof(command));
            Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            Line = line;
        }
        internal string ToLine() => Command + Parameters;
    }

    public sealed class BtplTextElement
    {
        public string Command { get; }
        public int X { get; }
        public int Y { get; }
        public IReadOnlyList<string> Arguments { get; }
        public string Payload { get; }
        internal int? Line { get; }
        private readonly string xLiteral, yLiteral;

        public BtplTextElement(string command, int x, int y, IEnumerable<string> arguments, string payload)
            : this(command, x.ToString(CultureInfo.InvariantCulture), y.ToString(CultureInfo.InvariantCulture), arguments, payload, null)
        { BtplSyntax.CheckText(this, true); }

        internal BtplTextElement(string command, string x, string y, IEnumerable<string> arguments, string payload, int? line)
        {
            Command = command ?? throw new ArgumentNullException(nameof(command));
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));
            Arguments = Array.AsReadOnly(arguments.ToArray()); Line = line;
            xLiteral = x; yLiteral = y;
            X = BtplSyntax.Number(x, 0, int.MaxValue, line); Y = BtplSyntax.Number(y, 0, int.MaxValue, line);
        }
        public BtplTextElement WithPosition(int x, int y) => new BtplTextElement(Command, x, y, Arguments, Payload);
        public BtplTextElement WithPayload(string payload)
        {
            var result = new BtplTextElement(Command, xLiteral, yLiteral, Arguments, payload, Line);
            BtplSyntax.CheckText(result, true); return result;
        }
        public BtplTextElement WithArgument(int argumentIndex, string value)
        {
            if (argumentIndex < 0 || argumentIndex >= Arguments.Count) throw new ArgumentOutOfRangeException(nameof(argumentIndex));
            if (Command == "AW") throw new BtplFormatException("LockedTextArguments", Line);
            string[] copy = Arguments.ToArray(); copy[argumentIndex] = value;
            var result = new BtplTextElement(Command, xLiteral, yLiteral, copy, Payload, Line);
            BtplSyntax.CheckText(result, true); return result;
        }
        internal string ToLine() => Command + "," + xLiteral + "," + yLiteral + "," + string.Join(",", Arguments) + "," + Payload;
    }

    public sealed class BtplLineAdvance
    {
        public int DeltaX { get; }
        public int DeltaY { get; }
        public BtplLineAdvance(int deltaX, int deltaY) { DeltaX = deltaX; DeltaY = deltaY; }
    }

    public sealed class BtplRenderOptions
    {
        public IReadOnlyDictionary<int, BtplLineAdvance> LineAdvances { get; }
        public BtplRenderOptions() : this(new Dictionary<int, BtplLineAdvance>()) { }
        private BtplRenderOptions(IDictionary<int, BtplLineAdvance> values)
        { LineAdvances = new ReadOnlyDictionary<int, BtplLineAdvance>(new Dictionary<int, BtplLineAdvance>(values)); }
        public BtplRenderOptions WithLineAdvance(int textIndex, BtplLineAdvance advance)
        {
            if (textIndex < 0) throw new ArgumentOutOfRangeException(nameof(textIndex));
            if (advance == null) throw new ArgumentNullException(nameof(advance));
            var copy = LineAdvances.ToDictionary(x => x.Key, x => x.Value); copy[textIndex] = advance;
            return new BtplRenderOptions(copy);
        }
        public BtplRenderOptions WithRemovedText(int textIndex)
        {
            if (textIndex < 0) throw new ArgumentOutOfRangeException(nameof(textIndex));
            return new BtplRenderOptions(LineAdvances.Where(x => x.Key != textIndex)
                .ToDictionary(x => x.Key > textIndex ? x.Key - 1 : x.Key, x => x.Value));
        }
    }
}
