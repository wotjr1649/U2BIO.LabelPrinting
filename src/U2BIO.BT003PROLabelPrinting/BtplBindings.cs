using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace U2BIO.BT003PROLabelPrinting
{
    public sealed class BtplBinding
    {
        public string Column { get; }
        public string Format { get; }
        public string NullText { get; }
        public BtplBinding(string column, string format = null, string nullText = null)
        {
            if (string.IsNullOrWhiteSpace(column)) throw new BtplFormatException("InvalidBindings");
            Column = column; Format = format; NullText = nullText;
        }
    }

    public sealed class BtplBindings
    {
        public IReadOnlyDictionary<string, BtplBinding> Variables { get; }
        private readonly BtplFileOrigin origin;
        internal BtplBindings(IDictionary<string, BtplBinding> variables, BtplFileOrigin source = null)
        {
            Variables = new ReadOnlyDictionary<string, BtplBinding>(new Dictionary<string, BtplBinding>(variables, StringComparer.Ordinal));
            origin = source;
        }
        private static bool VariableName(string value) => value != null && Regex.IsMatch(value, "\\A\\^V[0-9]{2}\\z", RegexOptions.CultureInvariant);
        private static string Name(XmlElement element) => element.NamespaceURI == "item" ? element.GetAttribute("item") : element.LocalName;
        private static Dictionary<string, XmlElement> Object(XmlElement element)
        {
            if (element == null || element.GetAttribute("type") != "object" || element.HasAttribute("__type")) throw new BtplFormatException("InvalidBindings");
            var result = new Dictionary<string, XmlElement>(StringComparer.Ordinal);
            foreach (XmlElement child in element.ChildNodes.OfType<XmlElement>()) {
                string name = Name(child);
                if (result.ContainsKey(name)) throw new BtplFormatException("DuplicateJsonKey");
                result.Add(name, child);
            }
            return result;
        }
        private static string String(XmlElement element, bool nullable)
        {
            if (nullable && element.GetAttribute("type") == "null") return null;
            if (element.GetAttribute("type") != "string") throw new BtplFormatException("InvalidBindings");
            return element.InnerText;
        }
        private static void CheckEnvelope(string json)
        {
            // The framework reader accepts trailing content; require exactly one root object.
            int depth = 0; bool quoted = false, escaped = false, started = false;
            for (int i = 0; i < json.Length; i++) {
                char c = json[i];
                if (quoted) {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') quoted = false;
                    continue;
                }
                if (!started && c != '{') {
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n') continue;
                    throw new BtplFormatException("InvalidBindings");
                }
                if (c == '"') quoted = true;
                else if (c == '{') { depth++; started = true; }
                else if (c == '}' && --depth == 0) {
                    if (json.Skip(i + 1).Any(x => x != ' ' && x != '\t' && x != '\r' && x != '\n')) throw new BtplFormatException("InvalidBindings");
                    return;
                }
            }
            throw new BtplFormatException("InvalidBindings");
        }
        public static BtplBindings Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            CheckEnvelope(json);
            try {
                var quotas = new XmlDictionaryReaderQuotas { MaxDepth = 4, MaxStringContentLength = int.MaxValue };
                using (var reader = JsonReaderWriterFactory.CreateJsonReader(new UTF8Encoding(false, true).GetBytes(json), quotas)) {
                    var document = new XmlDocument { XmlResolver = null }; document.Load(reader);
                    var root = Object(document.DocumentElement);
                    if (root.Count != 1 || !root.ContainsKey("variables")) throw new BtplFormatException("InvalidBindings");
                    var variables = new Dictionary<string, BtplBinding>(StringComparer.Ordinal);
                    foreach (var item in Object(root["variables"])) {
                        if (!VariableName(item.Key)) throw new BtplFormatException("InvalidBindings");
                        var fields = Object(item.Value);
                        if (!fields.ContainsKey("column") || fields.Keys.Any(k => k != "column" && k != "format" && k != "nullText"))
                            throw new BtplFormatException("InvalidBindings");
                        variables.Add(item.Key, new BtplBinding(String(fields["column"], false),
                            fields.ContainsKey("format") ? String(fields["format"], false) : null,
                            fields.ContainsKey("nullText") ? String(fields["nullText"], true) : null));
                    }
                    return new BtplBindings(variables);
                }
            } catch (XmlException) { throw new BtplFormatException("InvalidBindings"); }
            catch (EncoderFallbackException) { throw new BtplFormatException("InvalidBindings"); }
        }
        public static BtplBindings Load(string filePath)
        {
            if (filePath == null) throw new ArgumentNullException(nameof(filePath));
            BtplFileOrigin source; var parsed = Parse(BtplFileStore.Read(filePath, 65001, out source));
            return new BtplBindings(parsed.Variables.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal), source);
        }
        private static void WriteString(XmlDictionaryWriter writer, string name, string value)
        {
            writer.WriteStartElement(name); writer.WriteAttributeString("type", "string");
            writer.WriteString(value); writer.WriteEndElement();
        }
        public string ToJson()
        {
            using (var stream = new MemoryStream()) {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, new UTF8Encoding(false, true), false, true, "  ")) {
                    writer.WriteStartElement("root"); writer.WriteAttributeString("type", "object");
                    writer.WriteStartElement("variables"); writer.WriteAttributeString("type", "object");
                    foreach (var item in Variables.OrderBy(x => x.Key, StringComparer.Ordinal)) {
                        writer.WriteStartElement("item", "item"); writer.WriteAttributeString("item", item.Key); writer.WriteAttributeString("type", "object");
                        WriteString(writer, "column", item.Value.Column);
                        if (item.Value.Format != null) WriteString(writer, "format", item.Value.Format);
                        if (item.Value.NullText != null) WriteString(writer, "nullText", item.Value.NullText);
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement(); writer.WriteEndElement();
                }
                return new UTF8Encoding(false, true).GetString(stream.ToArray());
            }
        }
        public BtplBindings Save(string filePath)
        {
            if (filePath == null) throw new ArgumentNullException(nameof(filePath));
            string json = ToJson(); Parse(json);
            var source = BtplFileStore.Save(filePath, json, 65001, origin);
            return new BtplBindings(Variables.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal), source);
        }
        public BtplBindings WithVariable(string variable, BtplBinding binding)
        {
            if (!VariableName(variable)) throw new BtplFormatException("InvalidBindings");
            if (binding == null) throw new ArgumentNullException(nameof(binding));
            var copy = Variables.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal); copy[variable] = binding;
            return new BtplBindings(copy, origin);
        }
        public BtplBindings RemoveVariable(string variable)
        {
            if (!VariableName(variable)) throw new BtplFormatException("InvalidBindings");
            var copy = Variables.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal); copy.Remove(variable);
            return new BtplBindings(copy, origin);
        }
    }
}
