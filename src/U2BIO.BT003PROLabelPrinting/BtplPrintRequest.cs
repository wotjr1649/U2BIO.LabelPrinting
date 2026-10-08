using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;

namespace U2BIO.BT003PROLabelPrinting
{
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
            internal SnapshotRow(bool available, Dictionary<string, object> values) { Available = available; Values = values; }
        }
        private BtplPrintRequest(BtplTemplate template, BtplBindings bindings, DataTable table, IEnumerable<DataRow> rows, BtplRenderOptions options)
        {
            Template = template ?? throw new ArgumentNullException(nameof(template));
            Bindings = bindings ?? new BtplBindings(new Dictionary<string, BtplBinding>());
            RenderOptions = options ?? new BtplRenderOptions();
            if (RenderOptions.LineAdvances.Keys.Any(i => i >= Template.TextElements.Count)) throw new ArgumentOutOfRangeException(nameof(options));
            References = new Dictionary<string, int?>(StringComparer.Ordinal);
            foreach (var text in template.TextElements)
                foreach (Match match in BtplProcessor.Variables.Matches(text.Payload))
                    if (!References.ContainsKey(match.Value)) References.Add(match.Value, text.Line);
            Columns = new Dictionary<string, Type>(StringComparer.Ordinal);
            if (table == null) { Rows = new[] { new SnapshotRow(true, new Dictionary<string, object>(StringComparer.Ordinal)) }; return; }
            var needed = new HashSet<string>(References.Keys.Where(k => Bindings.Variables.ContainsKey(k)).Select(k => Bindings.Variables[k].Column), StringComparer.Ordinal);
            var columns = table.Columns.Cast<DataColumn>().Where(c => needed.Contains(c.ColumnName)).ToArray();
            foreach (var column in columns) Columns.Add(column.ColumnName, column.DataType);
            Rows = rows.Select(row => {
                var values = new Dictionary<string, object>(StringComparer.Ordinal);
                bool available = row.RowState != DataRowState.Deleted;
                if (available) foreach (var column in columns) {
                    if (!SupportedType(column.DataType)) continue;
                    object value = row[column];
                    if (value == null || ReferenceEquals(value, DBNull.Value) || SupportedType(value.GetType())) values.Add(column.ColumnName, value);
                }
                return new SnapshotRow(available, values);
            }).ToArray();
        }
        internal static bool SupportedType(Type type) => type == typeof(string) || type == typeof(bool) || type == typeof(DateTime) ||
            type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) || type == typeof(int) || type == typeof(uint) ||
            type == typeof(long) || type == typeof(ulong) || type == typeof(float) || type == typeof(double) || type == typeof(decimal);
        public static BtplPrintRequest Fixed(BtplTemplate template, BtplRenderOptions renderOptions = null)
            => new BtplPrintRequest(template, null, null, null, renderOptions);
        public static BtplPrintRequest FromRow(BtplTemplate template, BtplBindings bindings, DataRow row, BtplRenderOptions renderOptions = null)
        {
            if (bindings == null) throw new ArgumentNullException(nameof(bindings));
            if (row == null) throw new ArgumentNullException(nameof(row));
            return new BtplPrintRequest(template, bindings, row.Table, new[] { row }, renderOptions);
        }
        public static BtplPrintRequest FromTable(BtplTemplate template, BtplBindings bindings, DataTable data, BtplRenderOptions renderOptions = null)
        {
            if (bindings == null) throw new ArgumentNullException(nameof(bindings));
            if (data == null) throw new ArgumentNullException(nameof(data));
            return new BtplPrintRequest(template, bindings, data, data.Rows.Cast<DataRow>().ToArray(), renderOptions);
        }
    }
}
