using System;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using U2BIO.BT003PROLabelPrinting;

namespace U2BIO.BT003PROLabelPrinting.Validation
{
    internal static class BindingTests
    {
        private const string Header = "^Q40,5,7.5+\n^W70\n^S5\n^H19\n^L\n";
        private static BtplTemplate Template(string payload, string extra = "") => BtplTemplate.Parse(Header + "AW,80,120,10,1,0,0," + payload + "\n" + extra + "E\n");
        private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Binding contract failed."); }
        private static void InvalidJson(string json)
        {
            try { BtplBindings.Parse(json); }
            catch (BtplFormatException e) { Check(!e.Message.Contains("PRIVATE")); return; }
            throw new InvalidOperationException("Invalid binding JSON accepted.");
        }
        private static BtplPrintRequest One(BtplTemplate template, BtplBindings bindings, object value, Type type, BtplRenderOptions options = null)
        {
            var table = new DataTable(); table.Columns.Add("Value", type); table.Rows.Add(value);
            return BtplPrintRequest.FromRow(template, bindings, table.Rows[0], options);
        }
        private static bool Has(BtplRenderResult result, string code) => result.Issues.Any(x => x.Code == code);
        private sealed class Poison
        {
            internal static int Calls;
            public override string ToString() { Calls++; throw new InvalidOperationException("PRIVATE"); }
        }
        internal static void Run()
        {
            var bindings = BtplBindings.Parse("{\"variables\":{\"^V03\":{\"column\":\"Value\"},\"^V01\":{\"column\":\"Date\",\"format\":\"yy-MM-dd\"}}}");
            Check(bindings.Variables.Count == 2 && bindings.Variables["^V03"].Column == "Value");
            Check(BtplBindings.Parse(bindings.ToJson()).Variables["^V01"].Format == "yy-MM-dd");
            foreach (string bad in new[] {
                "", "[]", "null", "{}", "{\"variables\":[]}", "{\"variables\":{},\"layout\":{}}",
                "{\"__type\":\"PRIVATE\",\"variables\":{}}",
                "{\"variables\":{\"^V00\":{\"__type\":\"PRIVATE\",\"column\":\"Value\"}}}",
                "{\"variables\":{},\"variables\":{}}",
                "{\"variables\":{\"^V00\":{\"column\":\"Value\"},\"\\u005eV00\":{\"column\":\"Other\"}}}",
                "{\"variables\":{\"^V00\":{\"column\":\"Value\",\"column\":\"Other\"}}}",
                "{\"variables\":{\"^v00\":{\"column\":\"Value\"}}}",
                "{\"variables\":{\"^V0\":{\"column\":\"Value\"}}}",
                "{\"variables\":{\"^V00\":{\"column\":\"  \"}}}",
                "{\"variables\":{\"^V00\":{\"column\":1}}}",
                "{\"variables\":{\"^V00\":{\"column\":\"Value\",\"format\":null}}}",
                "{\"variables\":{\"^V00\":{\"column\":\"Value\",\"nullText\":false}}}",
                "{\"variables\":{\"^V00\":{\"column\":\"Value\",\"dpi\":300}}}",
                "{\"variables\":{}} false"
            }) InvalidJson(bad);

            var table = new DataTable(); table.Columns.Add("Value", typeof(string)); table.Columns.Add("Date", typeof(DateTime));
            table.Rows.Add("  alpha,!!$1  ", new DateTime(2026, 10, 8));
            table.Rows.Add(DBNull.Value, new DateTime(2026, 10, 9));
            table.Rows.Add("omega", new DateTime(2026, 10, 10));
            var template = Template("^V03 /^V01 /^V03", "AW,20,40,10,1,0,0,fixed\n");
            var request = BtplPrintRequest.FromTable(template, bindings, table);
            var validation = BtplProcessor.Validate(request);
            Check(!validation.IsValid && validation.Rows.Select(x => x.IsValid).SequenceEqual(new[] { true, false, true }));
            Check(validation.Rows[1].Issues.Any(x => x.Code == "EmptyValue" && x.RowIndex == 1 && x.Variable == "^V03" && x.Column == "Value"));
            var first = BtplProcessor.Render(request, 0);
            Check(first.IsValid && first.CanPreview && !first.IsPartialPreview && first.BtplText != null);
            Check(first.TextElements[0].Payload == "  alpha,!!$1   /26-10-08 /  alpha,!!$1  ");
            var partial = BtplProcessor.Render(request, 1);
            Check(!partial.IsValid && partial.CanPreview && partial.IsPartialPreview && partial.BtplText == null && partial.TextElements.Count == 1);
            table.Rows[0]["Value"] = "changed"; table.Columns["Value"].ColumnName = "Changed"; table.Rows.Clear();
            Check(request.RowCount == 3 && BtplProcessor.Render(request, 0).TextElements[0].Payload == first.TextElements[0].Payload);
            Check(BtplProcessor.Render(request, 2).IsValid);
            var fallback = bindings.WithVariable("^V03", new BtplBinding("Value", nullText: "Not Used"));
            Check(bindings.Variables["^V03"].NullText == null && fallback.RemoveVariable("^V01").Variables.Count == 1);
            var single = Template("^V03");
            foreach (object empty in new object[] { null, DBNull.Value, "", " \t\r\n " }) {
                Check(BtplProcessor.Render(One(single, fallback, empty, typeof(string)), 0).TextElements[0].Payload == "Not Used");
                Check(Has(BtplProcessor.Render(One(single, bindings, empty, typeof(string)), 0), "EmptyValue"));
            }
            var explicitEmpty = fallback.WithVariable("^V03", new BtplBinding("Value", nullText: ""));
            Check(BtplProcessor.Render(One(single, explicitEmpty, DBNull.Value, typeof(string)), 0).TextElements[0].Payload == "");
            var nullFallback = BtplBindings.Parse("{\"variables\":{\"^V03\":{\"column\":\"Value\",\"nullText\":null}}}");
            Check(Has(BtplProcessor.Render(One(single, nullFallback, "", typeof(string)), 0), "EmptyValue"));
            var caseTable = new DataTable(); caseTable.Columns.Add("value", typeof(string)); caseTable.Rows.Add("x");
            Check(Has(BtplProcessor.Render(BtplPrintRequest.FromTable(single, fallback, caseTable), 0), "MissingColumn"));
            Check(!BtplProcessor.Validate(BtplPrintRequest.Fixed(single)).IsValid);
            Check(BtplProcessor.Render(BtplPrintRequest.Fixed(Template("literal, !!")), 0).IsValid);
            var unused = fallback.WithVariable("^V99", new BtplBinding("Absent"));
            var unusedResult = BtplProcessor.Render(One(single, unused, "ok", typeof(string)), 0);
            Check(unusedResult.IsValid && unusedResult.Issues.Any(x => x.Code == "UnusedBinding" && x.Severity == ValidationSeverity.Warning));
            foreach (string unsafeValue in new[] { "PRIVATE^V00", "PRIVATE~WM", "PRIVATE\0", "PRIVATE\t", "PRIVATE\u007f" }) {
                var result = BtplProcessor.Render(One(single, fallback, unsafeValue, typeof(string)), 0);
                Check(!result.IsValid && result.BtplText == null && result.Issues.All(x => !x.Message.Contains("PRIVATE")));
            }
            var unsafeFallback = fallback.WithVariable("^V03", new BtplBinding("Value", nullText: "PRIVATE^V03"));
            Check(!BtplProcessor.Render(One(single, unsafeFallback, "", typeof(string)), 0).IsValid);

            var options = new BtplRenderOptions().WithLineAdvance(0, new BtplLineAdvance(0, 40));
            var lines = One(single, fallback, "first\r\n\rthird\n", typeof(string), options);
            var multi = BtplProcessor.Render(lines, 0);
            Check(multi.IsValid && multi.TextElements.Select(x => x.Y).SequenceEqual(new[] { 120, 160, 200, 240 }));
            Check(multi.TextElements.Select(x => x.Payload).SequenceEqual(new[] { "first", "", "third", "" }));
            Check(Has(BtplProcessor.Render(One(single, fallback, "a\nb", typeof(string)), 0), "MissingLineAdvance"));
            var literalLines = single.WithText(0, single.TextElements[0].WithPayload("fixed\n^V03"));
            Check(BtplProcessor.Render(One(literalLines, fallback, "bound", typeof(string), options), 0).TextElements[1].Payload == "bound");
            Check(Has(BtplProcessor.Render(One(single.WithText(0, single.TextElements[0].WithPosition(0, int.MaxValue)), fallback, "a\nb", typeof(string), options), 0), "CoordinateOverflow"));
            var noData = new DataTable(); noData.Columns.Add("Value", typeof(string));
            var zero = BtplProcessor.Validate(BtplPrintRequest.FromTable(single, fallback, noData));
            Check(!zero.IsValid && zero.Rows.Count == 0 && zero.CommonIssues.Any(x => x.Code == "NoData"));
            var invalidTemplate = Template("fixed").RemoveSetting("^W");
            Check(!BtplProcessor.Validate(BtplPrintRequest.Fixed(invalidTemplate)).IsValid);
            Check(BtplProcessor.Render(BtplPrintRequest.Fixed(invalidTemplate), 0).IsPartialPreview);

            var previous = CultureInfo.CurrentCulture;
            try {
                CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
                var numeric = fallback.WithVariable("^V03", new BtplBinding("Value", "F2", "EMPTY"));
                Check(BtplProcessor.Render(One(single, numeric, 12.5m, typeof(decimal)), 0).TextElements[0].Payload == "12.50");
                Check(BtplProcessor.Render(One(single, numeric, DBNull.Value, typeof(decimal)), 0).TextElements[0].Payload == "EMPTY");
                Check(Has(BtplProcessor.Render(One(single, numeric, "x", typeof(string)), 0), "FormatNotSupported"));
                Check(Has(BtplProcessor.Render(One(single, numeric, true, typeof(bool)), 0), "FormatNotSupported"));
                Check(Has(BtplProcessor.Render(One(single, fallback.WithVariable("^V03", new BtplBinding("Value", "Q")), 2, typeof(int)), 0), "InvalidFormat"));
            } finally { CultureInfo.CurrentCulture = previous; }
            foreach (object value in new object[] { (byte)1, (sbyte)-1, (short)-2, (ushort)2, -3, (uint)3, (long)-4, (ulong)4, 1.25f, 2.5d, 3.75m, true, new DateTime(2026, 10, 8) })
                Check(BtplProcessor.Render(One(single, fallback, value, value.GetType()), 0).IsValid);
            foreach (object value in new object[] { double.NaN, double.PositiveInfinity, float.NegativeInfinity })
                Check(Has(BtplProcessor.Render(One(single, fallback, value, value.GetType()), 0), "NonFiniteValue"));
            Check(!BtplProcessor.Render(One(single, fallback, new Poison(), typeof(object)), 0).IsValid && Poison.Calls == 0);
            var deleted = new DataTable(); deleted.Columns.Add("Value", typeof(string)); deleted.Rows.Add("old"); deleted.AcceptChanges(); deleted.Rows[0].Delete();
            Check(Has(BtplProcessor.Render(BtplPrintRequest.FromTable(single, fallback, deleted), 0), "InvalidDataRow"));
            var longText = Template(new string('x', 5000));
            Check(BtplProcessor.Render(BtplPrintRequest.Fixed(longText), 0).IsValid);
            Files(bindings);
            Console.WriteLine("PASS: bindings C01/C02/C19-offline, strict schema, immutable table snapshots, nullText, formats, all-row validation and partial preview; no DB/device calls");
        }
        private static void Files(BtplBindings bindings)
        {
            string directory = Path.Combine(Path.GetTempPath(), "bindings-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
            try {
                string file = Path.Combine(directory, "bindings.json");
                var saved = bindings.Save(file); var stale = BtplBindings.Load(file);
                saved = saved.WithVariable("^V03", new BtplBinding("Value", nullText: "quote\"\\가나다\n")).Save(file);
                saved = saved.Save(file);
                Check(BtplBindings.Load(file).Variables["^V03"].NullText == "quote\"\\가나다\n");
                Check(!File.ReadAllBytes(file).Take(3).SequenceEqual(Encoding.UTF8.GetPreamble()));
                try { stale.Save(file); throw new InvalidOperationException("Binding source conflict missed."); }
                catch (BtplSaveConflictException) { }
                string btpl = Path.Combine(directory, "label.btpl"); Template("fixed").Save(btpl);
                using (var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None)) {
                    try { bindings.Save(file); throw new InvalidOperationException("Locked binding replacement succeeded."); }
                    catch (IOException) { }
                }
                Check(File.Exists(btpl) && !Directory.GetFiles(directory).Any(x => x.EndsWith(".tmp", StringComparison.Ordinal)));
            } finally { foreach (string file in Directory.GetFiles(directory)) File.Delete(file); Directory.Delete(directory); }
        }
    }
}
