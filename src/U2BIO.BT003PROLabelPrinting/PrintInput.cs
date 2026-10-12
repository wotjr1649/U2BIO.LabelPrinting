using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using U2BIO.BT003PROLabelPrinting.Native;

namespace U2BIO.BT003PROLabelPrinting
{
    internal sealed class PrintInput : IDisposable
    {
        internal readonly BtplTemplate Template;
        internal readonly string Form, VariableData;
        internal string FilePath { get; private set; }
        private FileStream file;
        private IDisposable directories;

        internal PrintInput(string btpl, string[] values)
        {
            Template = BtplTemplate.ParseSdk(btpl);
            Form = string.Join("\r\n", BtplSyntax.SplitLines(btpl)).TrimEnd('\r', '\n') + "\r\n";
            var slots = Template.TextElements.SelectMany(text => BtplSyntax.Variables.Matches(text.Payload).Cast<System.Text.RegularExpressions.Match>())
                .Select(match => int.Parse(match.Value.Substring(2), CultureInfo.InvariantCulture)).Distinct().ToArray();
            var snapshot = values == null ? new string[0] : (string[])values.Clone();
            int needed = slots.Length == 0 ? 0 : slots.Max() + 1;
            if (snapshot.Length != needed) throw new BtplFormatException("VariableCountMismatch");
            foreach (string value in snapshot) {
                if (value == null || value.Contains("!!") || value.StartsWith("!", StringComparison.Ordinal) || value.EndsWith("!", StringComparison.Ordinal) || value.Any(char.IsControl))
                    throw new BtplFormatException("InvalidVariableData");
                BtplSyntax.CheckLiteral(value, null);
            }
            if (snapshot.Any(string.IsNullOrWhiteSpace)) throw new BtplFormatException("EmptyValue");
            VariableData = needed == 0 ? null : string.Join("!!", snapshot);
        }

        internal static PrintInput FromFile(string path, string[] values)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            string absolute = Path.GetFullPath(path);
            IDisposable lease = NativeSdk.LeaseDirectoryTree(Path.GetDirectoryName(absolute));
            FileStream stream = null;
            try {
                stream = new FileStream(absolute, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (!string.Equals(NativeSdk.FinalFilePath(stream.SafeFileHandle), absolute, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Print form path changed.");
                BtplFileOrigin unused;
                string form = BtplFileStore.Read(absolute, 949, out unused, 4 * 1024 * 1024, "TemplateTooLarge");
                var input = new PrintInput(form, values) { FilePath = absolute, file = stream, directories = lease };
                // Keep the inspected file and its directory path stable until the SDK worker has actually returned.
                stream = null; lease = null;
                return input;
            } finally { stream?.Dispose(); lease?.Dispose(); }
        }

        internal List<ValidationIssue> Validate(bool printingOptionsSet)
        {
            var issues = new List<ValidationIssue>();
            if (!printingOptionsSet && (!Template.Settings.Any(setting => setting.Command == "^S") || !Template.Settings.Any(setting => setting.Command == "^H")))
                issues.Add(new ValidationIssue("MissingPrintingOptions", null));
            return issues;
        }
        internal BtplRenderResult ValidationRow(List<ValidationIssue> issues)
            => new BtplRenderResult(new RowValidationResult(0, issues), Template.TextElements, Form);

        public void Dispose()
        {
            file?.Dispose(); file = null;
            directories?.Dispose(); directories = null;
        }
    }
}
