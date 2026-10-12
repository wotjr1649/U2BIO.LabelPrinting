using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace U2BIO.BT003PROLabelPrinting
{
    public enum BtplTemplateFormat { Btpl, Json, Xml }

    public sealed partial class BtplTemplate
    {
        private const int MaxDocumentBytes = 4 * 1024 * 1024;
        private static readonly Encoding DocumentEncoding = new UTF8Encoding(false, true);
        private static BtplFormatException DocumentError() => new BtplFormatException("InvalidTemplateDocument");
        private static void CheckFormat(BtplTemplateFormat format)
        {
            if (format != BtplTemplateFormat.Btpl && format != BtplTemplateFormat.Json && format != BtplTemplateFormat.Xml)
                throw new ArgumentOutOfRangeException(nameof(format));
        }
        private static BtplTemplateFormat FileFormat(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            string extension = Path.GetExtension(path);
            return string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase) ? BtplTemplateFormat.Json :
                string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase) ? BtplTemplateFormat.Xml : BtplTemplateFormat.Btpl;
        }
        private static int Bytes(string value)
        {
            try { return DocumentEncoding.GetByteCount(value); }
            catch (EncoderFallbackException) { throw DocumentError(); }
        }
        public static BtplTemplate Parse(string content, BtplTemplateFormat format)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));
            CheckFormat(format);
            if (format == BtplTemplateFormat.Btpl) return Parse(content);
            if (Bytes(content) > MaxDocumentBytes) throw new BtplFormatException("TemplateTooLarge");
            try {
                var template = format == BtplTemplateFormat.Json ? ReadJson(content) : ReadXml(content);
                // All formats use the existing BTPL validation, normalization and print model.
                Parse(template.ToBtpl());
                return template;
            } catch (XmlException) { throw DocumentError(); }
            catch (BtplFormatException error) when (error.Code == "InvalidBindings" || error.Code == "DuplicateJsonKey") { throw DocumentError(); }
        }
        private static void Fields(Dictionary<string, XmlElement> fields, params string[] names)
        {
            if (fields.Count != names.Length || names.Any(n => !fields.ContainsKey(n))) throw DocumentError();
        }
        private static string JsonString(XmlElement element)
        {
            string value = BtplBindings.String(element, false); Bytes(value); return value;
        }
        private static string JsonCoordinate(XmlElement element)
        {
            if (element.GetAttribute("type") != "number") return JsonString(element);
            return element.InnerText;
        }
        private static XmlElement[] JsonArray(XmlElement element)
        {
            if (element.GetAttribute("type") != "array" || element.HasAttribute("__type")) throw DocumentError();
            var children = element.ChildNodes.OfType<XmlElement>().ToArray();
            if (children.Any(x => x.LocalName != "item")) throw DocumentError();
            return children;
        }
        private static BtplTemplate ReadJson(string json)
        {
            BtplBindings.CheckEnvelope(json);
            CheckJsonTokens(json);
            var quotas = new XmlDictionaryReaderQuotas { MaxDepth = 6, MaxStringContentLength = MaxDocumentBytes };
            using (var reader = JsonReaderWriterFactory.CreateJsonReader(DocumentEncoding.GetBytes(json), quotas)) {
                var document = new XmlDocument { XmlResolver = null }; document.Load(reader);
                var root = BtplBindings.Object(document.DocumentElement); Fields(root, "settings", "texts");
                var settings = new List<BtplSetting>(); var texts = new List<BtplTextElement>();
                foreach (var item in JsonArray(root["settings"])) {
                    var fields = BtplBindings.Object(item); Fields(fields, "command", "parameters");
                    settings.Add(new BtplSetting(JsonString(fields["command"]), JsonString(fields["parameters"])));
                }
                foreach (var item in JsonArray(root["texts"])) {
                    var fields = BtplBindings.Object(item); Fields(fields, "command", "x", "y", "arguments", "payload");
                    texts.Add(new BtplTextElement(JsonString(fields["command"]), JsonCoordinate(fields["x"]), JsonCoordinate(fields["y"]),
                        JsonArray(fields["arguments"]).Select(JsonString), JsonString(fields["payload"]), null));
                }
                return new BtplTemplate(settings, texts, null);
            }
        }
        private static bool JsonSpace(char value) => value == ' ' || value == '\t' || value == '\r' || value == '\n';
        private static void CheckJsonTokens(string json)
        {
            // The Framework reader accepts trailing commas and leading-zero numbers; reject them before decoding.
            char previous = '\0';
            for (int i = 0; i < json.Length; i++) {
                char c = json[i];
                if (JsonSpace(c)) continue;
                if (c == '"') {
                    bool closed = false;
                    for (i++; i < json.Length; i++) {
                        c = json[i];
                        if (c == '"') { closed = true; break; }
                        if (c < 32) throw DocumentError();
                        if (c != '\\') continue;
                        if (++i >= json.Length) throw DocumentError();
                        c = json[i];
                        if (c == 'u') {
                            if (i + 4 >= json.Length) throw DocumentError();
                            for (int k = 1; k <= 4; k++) {
                                char digit = json[i + k];
                                if (!((digit >= '0' && digit <= '9') || (digit >= 'a' && digit <= 'f') || (digit >= 'A' && digit <= 'F'))) throw DocumentError();
                            }
                            i += 4;
                        } else if ("\"\\/bfnrt".IndexOf(c) < 0) throw DocumentError();
                    }
                    if (!closed) throw DocumentError();
                    previous = 'v'; continue;
                }
                if ("{}[],:".IndexOf(c) >= 0) {
                    if ((c == '}' || c == ']') && (previous == ',' || previous == ':')) throw DocumentError();
                    if (c == ',' && "{[:,".IndexOf(previous) >= 0) throw DocumentError();
                    previous = c; continue;
                }
                int start = i;
                while (i < json.Length && !JsonSpace(json[i]) && "{}[],:\"".IndexOf(json[i]) < 0) i++;
                string token = json.Substring(start, i - start); i--;
                if (token != "true" && token != "false" && token != "null" &&
                    !Regex.IsMatch(token, "\\A-?(?:0|[1-9][0-9]*)(?:\\.[0-9]+)?(?:[eE][+-]?[0-9]+)?\\z", RegexOptions.CultureInvariant)) throw DocumentError();
                previous = 'v';
            }
        }
        private static XmlElement[] Children(XmlElement element, params string[] attributes)
        {
            if (element == null || element.NamespaceURI.Length != 0 || element.Attributes.Count != attributes.Length ||
                attributes.Any(a => !element.HasAttribute(a))) throw DocumentError();
            foreach (XmlNode node in element.ChildNodes)
                if (!(node is XmlElement) && !(node is XmlWhitespace)) throw DocumentError();
            var children = element.ChildNodes.OfType<XmlElement>().ToArray();
            if (children.Any(x => x.NamespaceURI.Length != 0)) throw DocumentError();
            return children;
        }
        private static XmlElement Child(XmlElement[] children, string name)
        {
            var matches = children.Where(x => x.Name == name).ToArray();
            if (matches.Length != 1) throw DocumentError();
            return matches[0];
        }
        private static string XmlString(XmlElement element)
        {
            if (element.Attributes.Count != 0 || element.NamespaceURI.Length != 0 ||
                element.ChildNodes.Cast<XmlNode>().Any(n => !(n is XmlText) && !(n is XmlCDataSection) && !(n is XmlWhitespace) && !(n is XmlSignificantWhitespace))) throw DocumentError();
            string value = element.InnerText; Bytes(value); return value;
        }
        private static BtplTemplate ReadXml(string xml)
        {
            var options = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = MaxDocumentBytes, IgnoreWhitespace = false };
            using (var input = new StringReader(xml))
            using (var reader = XmlReader.Create(input, options))
                while (reader.Read()) if (reader.Depth > 5) throw DocumentError();
            using (var input = new StringReader(xml))
            using (var reader = XmlReader.Create(input, options)) {
                var document = new XmlDocument { XmlResolver = null, PreserveWhitespace = true }; document.Load(reader);
                if (document.ChildNodes.Cast<XmlNode>().Any(n => !(n is XmlElement) && !(n is XmlDeclaration) && !(n is XmlWhitespace))) throw DocumentError();
                var declaration = document.FirstChild as XmlDeclaration;
                if (declaration != null && declaration.Encoding.Length != 0 &&
                    !string.Equals(declaration.Encoding, "utf-8", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(declaration.Encoding, "utf8", StringComparison.OrdinalIgnoreCase)) throw DocumentError();
                var root = document.DocumentElement;
                if (root == null || root.Name != "labelTemplate") throw DocumentError();
                var children = Children(root); if (children.Length != 2) throw DocumentError();
                var settings = new List<BtplSetting>(); var texts = new List<BtplTextElement>();
                foreach (var item in Children(Child(children, "settings"))) {
                    if (item.Name != "setting" || Children(item, "command", "parameters").Length != 0) throw DocumentError();
                    settings.Add(new BtplSetting(item.GetAttribute("command"), item.GetAttribute("parameters")));
                }
                foreach (var item in Children(Child(children, "texts"))) {
                    if (item.Name != "text") throw DocumentError();
                    var fields = Children(item, "command", "x", "y"); if (fields.Length != 2) throw DocumentError();
                    var arguments = Children(Child(fields, "arguments"));
                    if (arguments.Any(x => x.Name != "argument")) throw DocumentError();
                    texts.Add(new BtplTextElement(item.GetAttribute("command"), item.GetAttribute("x"), item.GetAttribute("y"),
                        arguments.Select(XmlString), XmlString(Child(fields, "payload")), null));
                }
                return new BtplTemplate(settings, texts, null);
            }
        }
        private void CheckDocumentSize()
        {
            long length = 0;
            foreach (string value in Settings.SelectMany(s => new[] { s.Command, s.Parameters }).Concat(
                TextElements.SelectMany(t => new[] { t.Command, t.XLiteral, t.YLiteral, t.Payload }.Concat(t.Arguments)))) {
                length += Bytes(value);
                if (length > MaxDocumentBytes) throw new BtplFormatException("TemplateTooLarge");
            }
            ToBtpl();
        }
        private sealed class DocumentBuffer : MemoryStream
        {
            public override void Write(byte[] buffer, int offset, int count)
            {
                if (Position + count > MaxDocumentBytes) throw new BtplFormatException("TemplateTooLarge");
                base.Write(buffer, offset, count);
            }
            public override void WriteByte(byte value)
            {
                if (Position >= MaxDocumentBytes) throw new BtplFormatException("TemplateTooLarge");
                base.WriteByte(value);
            }
        }
        private static void JsonValue(XmlDictionaryWriter writer, string name, string value)
        {
            writer.WriteStartElement(name); writer.WriteAttributeString("type", "string");
            writer.WriteString(value); writer.WriteEndElement();
        }
        public string ToJson()
        {
            CheckDocumentSize();
            using (var stream = new DocumentBuffer()) {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, DocumentEncoding, false, true, "  ")) {
                    writer.WriteStartElement("root"); writer.WriteAttributeString("type", "object");
                    writer.WriteStartElement("settings"); writer.WriteAttributeString("type", "array");
                    foreach (var setting in Settings) {
                        writer.WriteStartElement("item"); writer.WriteAttributeString("type", "object");
                        JsonValue(writer, "command", setting.Command); JsonValue(writer, "parameters", setting.Parameters); writer.WriteEndElement();
                    }
                    writer.WriteEndElement(); writer.WriteStartElement("texts"); writer.WriteAttributeString("type", "array");
                    foreach (var text in TextElements) {
                        writer.WriteStartElement("item"); writer.WriteAttributeString("type", "object");
                        JsonValue(writer, "command", text.Command); JsonValue(writer, "x", text.XLiteral); JsonValue(writer, "y", text.YLiteral);
                        writer.WriteStartElement("arguments"); writer.WriteAttributeString("type", "array");
                        foreach (string argument in text.Arguments) JsonValue(writer, "item", argument);
                        writer.WriteEndElement(); JsonValue(writer, "payload", text.Payload); writer.WriteEndElement();
                    }
                    writer.WriteEndElement(); writer.WriteEndElement();
                }
                return DocumentEncoding.GetString(stream.ToArray());
            }
        }
        public string ToXml()
        {
            CheckDocumentSize();
            try {
                using (var stream = new DocumentBuffer()) {
                    using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = DocumentEncoding, Indent = true,
                        OmitXmlDeclaration = true, NewLineHandling = NewLineHandling.Entitize })) {
                        writer.WriteStartElement("labelTemplate"); writer.WriteStartElement("settings");
                        foreach (var setting in Settings) {
                            writer.WriteStartElement("setting"); writer.WriteAttributeString("command", setting.Command);
                            writer.WriteAttributeString("parameters", setting.Parameters); writer.WriteEndElement();
                        }
                        writer.WriteEndElement(); writer.WriteStartElement("texts");
                        foreach (var text in TextElements) {
                            writer.WriteStartElement("text"); writer.WriteAttributeString("command", text.Command);
                            writer.WriteAttributeString("x", text.XLiteral); writer.WriteAttributeString("y", text.YLiteral);
                            writer.WriteStartElement("arguments");
                            foreach (string argument in text.Arguments) writer.WriteElementString("argument", argument);
                            writer.WriteEndElement(); writer.WriteElementString("payload", text.Payload); writer.WriteEndElement();
                        }
                        writer.WriteEndElement(); writer.WriteEndElement();
                    }
                    return DocumentEncoding.GetString(stream.ToArray());
                }
            } catch (ArgumentException) { throw DocumentError(); }
            catch (XmlException) { throw DocumentError(); }
        }
    }
}
