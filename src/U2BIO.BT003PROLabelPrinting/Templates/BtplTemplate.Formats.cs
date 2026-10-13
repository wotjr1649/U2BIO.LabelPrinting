using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text;
using System.Xml;

namespace U2BIO.BT003PROLabelPrinting
{
    /// <summary>서식 저장 형식이다. BTPL은 지정 코드 페이지, JSON/XML은 UTF-8을 사용한다.</summary>
    public enum BtplTemplateFormat
    {
        Btpl,
        Json,
        Xml
    }

    /// <summary>편집·미리보기용 BTPL 부분 집합 모델이다. 기본 프린터 API는 이 파서를 사용하지 않는다.</summary>
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
            if (path == null)
                throw new ArgumentNullException(nameof(path));
            string extension = Path.GetExtension(path);
            return string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase) ? BtplTemplateFormat.Json : string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase) ? BtplTemplateFormat.Xml : BtplTemplateFormat.Btpl;
        }

        private static int Bytes(string value)
        {
            try
            {
                return DocumentEncoding.GetByteCount(value);
            }
            catch (EncoderFallbackException)
            {
                throw DocumentError();
            }
        }

        public static BtplTemplate Parse(string content, BtplTemplateFormat format)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));
            CheckFormat(format);
            if (format == BtplTemplateFormat.Btpl)
                return Parse(content);
            if (Bytes(content) > MaxDocumentBytes)
                throw new BtplFormatException("TemplateTooLarge");
            try
            {
                var template = format == BtplTemplateFormat.Json ? ReadJson(content) : ReadXml(content);
                // 모든 형식에 같은 명령 검사를 적용한다. 검사한 BTPL 문자열의 재해석은 생략한다.
                template.ToBtpl();
                return template;
            }
            catch (XmlException)
            {
                throw DocumentError();
            }
            catch (BtplFormatException error)when (error.Code == "InvalidBindings" || error.Code == "DuplicateJsonKey")
            {
                throw DocumentError();
            }
        }

        private static void Fields(Dictionary<string, XmlElement> fields, params string[] names)
        {
            if (fields.Count != names.Length || names.Any(n => !fields.ContainsKey(n)))
                throw DocumentError();
        }

        private static string JsonString(XmlElement element)
        {
            string value = BtplJson.String(element, false);
            Bytes(value);
            return value;
        }

        private static string JsonCoordinate(XmlElement element)
        {
            if (element.GetAttribute("type") != "number")
                return JsonString(element);
            return element.InnerText;
        }

        private static XmlElement[] JsonArray(XmlElement element)
        {
            if (element.GetAttribute("type") != "array" || element.HasAttribute("__type"))
                throw DocumentError();
            var children = element.ChildNodes.OfType<XmlElement>().ToArray();
            if (children.Any(x => x.LocalName != "item"))
                throw DocumentError();
            return children;
        }

        private static BtplTemplate ReadJson(string json)
        {
            BtplJson.CheckEnvelope(json);
            BtplJson.CheckTokens(json, "InvalidTemplateDocument");
            var quotas = new XmlDictionaryReaderQuotas
            {
                MaxDepth = 6,
                MaxStringContentLength = MaxDocumentBytes
            };
            using (var reader = JsonReaderWriterFactory.CreateJsonReader(DocumentEncoding.GetBytes(json), quotas))
            {
                var document = new XmlDocument
                {
                    XmlResolver = null
                };
                document.Load(reader);
                var root = BtplJson.Object(document.DocumentElement);
                Fields(root, "settings", "texts");
                var settings = new List<BtplSetting>();
                var texts = new List<BtplTextElement>();
                foreach (var item in JsonArray(root["settings"]))
                {
                    var fields = BtplJson.Object(item);
                    Fields(fields, "command", "parameters");
                    settings.Add(new BtplSetting(JsonString(fields["command"]), JsonString(fields["parameters"])));
                }

                foreach (var item in JsonArray(root["texts"]))
                {
                    var fields = BtplJson.Object(item);
                    Fields(fields, "command", "x", "y", "arguments", "payload");
                    texts.Add(new BtplTextElement(JsonString(fields["command"]), JsonCoordinate(fields["x"]), JsonCoordinate(fields["y"]), JsonArray(fields["arguments"]).Select(JsonString), JsonString(fields["payload"]), null));
                }

                return new BtplTemplate(settings, texts, null);
            }
        }

        private static XmlElement[] Children(XmlElement element, params string[] attributes)
        {
            if (element == null || element.NamespaceURI.Length != 0 || element.Attributes.Count != attributes.Length || attributes.Any(a => !element.HasAttribute(a)))
                throw DocumentError();
            foreach (XmlNode node in element.ChildNodes)
                if (!(node is XmlElement) && !(node is XmlWhitespace))
                    throw DocumentError();
            var children = element.ChildNodes.OfType<XmlElement>().ToArray();
            if (children.Any(x => x.NamespaceURI.Length != 0))
                throw DocumentError();
            return children;
        }

        private static XmlElement Child(XmlElement[] children, string name)
        {
            var matches = children.Where(x => x.Name == name).ToArray();
            if (matches.Length != 1)
                throw DocumentError();
            return matches[0];
        }

        private static string XmlString(XmlElement element)
        {
            if (element.Attributes.Count != 0 || element.NamespaceURI.Length != 0 || element.ChildNodes.Cast<XmlNode>().Any(n => !(n is XmlText) && !(n is XmlCDataSection) && !(n is XmlWhitespace) && !(n is XmlSignificantWhitespace)))
                throw DocumentError();
            string value = element.InnerText;
            Bytes(value);
            return value;
        }

        private static BtplTemplate ReadXml(string xml)
        {
            var options = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxDocumentBytes,
                IgnoreWhitespace = false
            };
            using (var input = new StringReader(xml))
            using (var reader = XmlReader.Create(input, options))
                while (reader.Read())
                    if (reader.Depth > 5)
                        throw DocumentError();
            using (var input = new StringReader(xml))
            using (var reader = XmlReader.Create(input, options))
            {
                var document = new XmlDocument
                {
                    XmlResolver = null,
                    PreserveWhitespace = true
                };
                document.Load(reader);
                if (document.ChildNodes.Cast<XmlNode>().Any(n => !(n is XmlElement) && !(n is XmlDeclaration) && !(n is XmlWhitespace)))
                    throw DocumentError();
                var declaration = document.FirstChild as XmlDeclaration;
                if (declaration != null && declaration.Encoding.Length != 0 && !string.Equals(declaration.Encoding, "utf-8", StringComparison.OrdinalIgnoreCase) && !string.Equals(declaration.Encoding, "utf8", StringComparison.OrdinalIgnoreCase))
                    throw DocumentError();
                var root = document.DocumentElement;
                if (root == null || root.Name != "labelTemplate")
                    throw DocumentError();
                var children = Children(root);
                if (children.Length != 2)
                    throw DocumentError();
                var settings = new List<BtplSetting>();
                var texts = new List<BtplTextElement>();
                foreach (var item in Children(Child(children, "settings")))
                {
                    if (item.Name != "setting" || Children(item, "command", "parameters").Length != 0)
                        throw DocumentError();
                    settings.Add(new BtplSetting(item.GetAttribute("command"), item.GetAttribute("parameters")));
                }

                foreach (var item in Children(Child(children, "texts")))
                {
                    if (item.Name != "text")
                        throw DocumentError();
                    var fields = Children(item, "command", "x", "y");
                    if (fields.Length != 2)
                        throw DocumentError();
                    var arguments = Children(Child(fields, "arguments"));
                    if (arguments.Any(x => x.Name != "argument"))
                        throw DocumentError();
                    texts.Add(new BtplTextElement(item.GetAttribute("command"), item.GetAttribute("x"), item.GetAttribute("y"), arguments.Select(XmlString), XmlString(Child(fields, "payload")), null));
                }

                return new BtplTemplate(settings, texts, null);
            }
        }

        private void CheckDocumentSize()
        {
            long length = 0;
            foreach (string value in Settings.SelectMany(s => new[] { s.Command, s.Parameters }).Concat(TextElements.SelectMany(t => new[] { t.Command, t.XLiteral, t.YLiteral, t.Payload }.Concat(t.Arguments))))
            {
                length += Bytes(value);
                if (length > MaxDocumentBytes)
                    throw new BtplFormatException("TemplateTooLarge");
            }

            ToBtpl();
        }

        private sealed class DocumentBuffer : MemoryStream
        {
            public override void Write(byte[] buffer, int offset, int count)
            {
                if (Position + count > MaxDocumentBytes)
                    throw new BtplFormatException("TemplateTooLarge");
                base.Write(buffer, offset, count);
            }

            public override void WriteByte(byte value)
            {
                if (Position >= MaxDocumentBytes)
                    throw new BtplFormatException("TemplateTooLarge");
                base.WriteByte(value);
            }
        }

        private static void JsonValue(XmlDictionaryWriter writer, string name, string value)
        {
            writer.WriteStartElement(name);
            writer.WriteAttributeString("type", "string");
            writer.WriteString(value);
            writer.WriteEndElement();
        }

        public string ToJson()
        {
            CheckDocumentSize();
            using (var stream = new DocumentBuffer())
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, DocumentEncoding, false, true, "  "))
                {
                    writer.WriteStartElement("root");
                    writer.WriteAttributeString("type", "object");
                    writer.WriteStartElement("settings");
                    writer.WriteAttributeString("type", "array");
                    foreach (var setting in Settings)
                    {
                        writer.WriteStartElement("item");
                        writer.WriteAttributeString("type", "object");
                        JsonValue(writer, "command", setting.Command);
                        JsonValue(writer, "parameters", setting.Parameters);
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();
                    writer.WriteStartElement("texts");
                    writer.WriteAttributeString("type", "array");
                    foreach (var text in TextElements)
                    {
                        writer.WriteStartElement("item");
                        writer.WriteAttributeString("type", "object");
                        JsonValue(writer, "command", text.Command);
                        JsonValue(writer, "x", text.XLiteral);
                        JsonValue(writer, "y", text.YLiteral);
                        writer.WriteStartElement("arguments");
                        writer.WriteAttributeString("type", "array");
                        foreach (string argument in text.Arguments)
                            JsonValue(writer, "item", argument);
                        writer.WriteEndElement();
                        JsonValue(writer, "payload", text.Payload);
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();
                    writer.WriteEndElement();
                }

                return DocumentEncoding.GetString(stream.ToArray());
            }
        }

        public string ToXml()
        {
            CheckDocumentSize();
            try
            {
                using (var stream = new DocumentBuffer())
                {
                    using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = DocumentEncoding, Indent = true, OmitXmlDeclaration = true, NewLineHandling = NewLineHandling.Entitize }))
                    {
                        writer.WriteStartElement("labelTemplate");
                        writer.WriteStartElement("settings");
                        foreach (var setting in Settings)
                        {
                            writer.WriteStartElement("setting");
                            writer.WriteAttributeString("command", setting.Command);
                            writer.WriteAttributeString("parameters", setting.Parameters);
                            writer.WriteEndElement();
                        }

                        writer.WriteEndElement();
                        writer.WriteStartElement("texts");
                        foreach (var text in TextElements)
                        {
                            writer.WriteStartElement("text");
                            writer.WriteAttributeString("command", text.Command);
                            writer.WriteAttributeString("x", text.XLiteral);
                            writer.WriteAttributeString("y", text.YLiteral);
                            writer.WriteStartElement("arguments");
                            foreach (string argument in text.Arguments)
                                writer.WriteElementString("argument", argument);
                            writer.WriteEndElement();
                            writer.WriteElementString("payload", text.Payload);
                            writer.WriteEndElement();
                        }

                        writer.WriteEndElement();
                        writer.WriteEndElement();
                    }

                    return DocumentEncoding.GetString(stream.ToArray());
                }
            }
            catch (ArgumentException)
            {
                throw DocumentError();
            }
            catch (XmlException)
            {
                throw DocumentError();
            }
        }
    }

    internal sealed class BtplFileOrigin
    {
        internal readonly string Path;
        internal readonly byte[] Hash;
        internal BtplFileOrigin(string path, byte[] bytes)
        {
            Path = path;
            Hash = BtplFileStore.Hash(bytes);
        }
    }

    internal static class BtplFileStore
    {
        internal static byte[] Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return sha.ComputeHash(bytes);
        }

        internal static Encoding StrictEncoding(int codePage) => Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        internal static string Read(string filePath, int codePage, out BtplFileOrigin origin, int? maximumBytes = null, string sizeError = "BindingsTooLarge")
        {
            string path = System.IO.Path.GetFullPath(filePath);
            byte[] bytes;
            if (maximumBytes == null)
                bytes = File.ReadAllBytes(path);
            else
            {
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (file.Length > maximumBytes.Value)
                        throw new BtplFormatException(sizeError);
                    int length = (int)file.Length;
                    using (var reader = new BinaryReader(file))
                    {
                        bytes = reader.ReadBytes(length);
                        if (bytes.Length != length || file.ReadByte() != -1)
                            throw new IOException("File changed while reading.");
                    }
                }
            }

            origin = new BtplFileOrigin(path, bytes);
            var encoding = StrictEncoding(codePage);
            int offset = 0;
            foreach (int bomPage in new[]
            {
                12000,
                12001,
                65001,
                1200,
                1201
            } )
            {
                byte[] bom = Encoding.GetEncoding(bomPage).GetPreamble();
                if (bom.Length > 0 && bytes.Take(bom.Length).SequenceEqual(bom))
                {
                    if (bomPage != codePage)
                        throw new BtplFormatException("InvalidEncoding");
                    offset = bom.Length;
                    break;
                }
            }

            try
            {
                return encoding.GetString(bytes, offset, bytes.Length - offset);
            }
            catch (DecoderFallbackException)
            {
                throw new BtplFormatException("InvalidEncoding");
            }
        }

        internal static BtplFileOrigin Save(string filePath, string text, int codePage, BtplFileOrigin origin)
        {
            byte[] bytes;
            try
            {
                bytes = StrictEncoding(codePage).GetBytes(text);
            }
            catch (EncoderFallbackException)
            {
                throw new BtplFormatException("InvalidEncoding");
            }

            string path = System.IO.Path.GetFullPath(filePath);
            string temporary = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path), ".btpl-" + Guid.NewGuid().ToString("N") + ".tmp");
            bool created = false;
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    created = true;
                    file.Write(bytes, 0, bytes.Length);
                    file.Flush(true);
                }

                // 같은 원본에 저장할 때만 변경 여부를 확인한다. 다른 경로의 저장에는 원본 해시를 적용하지 않는다.
                if (origin != null && string.Equals(path, origin.Path, StringComparison.OrdinalIgnoreCase))
                {
                    byte[] currentHash;
                    try
                    {
                        using (var file = File.OpenRead(path))
                        using (var sha = SHA256.Create())
                            currentHash = sha.ComputeHash(file);
                    }
                    catch (FileNotFoundException)
                    {
                        throw new BtplSaveConflictException();
                    }
                    catch (DirectoryNotFoundException)
                    {
                        throw new BtplSaveConflictException();
                    }

                    if (!currentHash.SequenceEqual(origin.Hash))
                        throw new BtplSaveConflictException();
                }

                // 완성된 임시 파일로 교체해 중간 내용이 저장되지 않게 한다.
                if (File.Exists(path))
                    File.Replace(temporary, path, null, false);
                else
                    File.Move(temporary, path);
                created = false;
                return new BtplFileOrigin(path, bytes);
            }
            finally
            {
                if (created)
                    File.Delete(temporary);
            }
        }
    }

    // 서식과 바인딩의 JSON 읽기가 같은 구문·중복 키 규칙을 사용하도록 한다.
    internal static class BtplJson
    {
        private static string Name(XmlElement element) => element.NamespaceURI == "item" ? element.GetAttribute("item") : element.LocalName;
        internal static Dictionary<string, XmlElement> Object(XmlElement element)
        {
            if (element == null || element.GetAttribute("type") != "object" || element.HasAttribute("__type"))
                throw new BtplFormatException("InvalidBindings");
            var result = new Dictionary<string, XmlElement>(StringComparer.Ordinal);
            foreach (XmlElement child in element.ChildNodes.OfType<XmlElement>())
            {
                string name = Name(child);
                if (result.ContainsKey(name))
                    throw new BtplFormatException("DuplicateJsonKey");
                result.Add(name, child);
            }

            return result;
        }

        internal static string String(XmlElement element, bool nullable)
        {
            if (nullable && element.GetAttribute("type") == "null")
                return null;
            if (element.GetAttribute("type") != "string")
                throw new BtplFormatException("InvalidBindings");
            return element.InnerText;
        }

        internal static void CheckEnvelope(string json)
        {
            // 입력에는 루트 객체 하나만 있어야 한다. 뒤에 붙은 다른 JSON이나 문자는 허용하지 않는다.
            int depth = 0;
            bool quoted = false, escaped = false, started = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (quoted)
                {
                    if (escaped)
                        escaped = false;
                    else if (c == '\\')
                        escaped = true;
                    else if (c == '"')
                        quoted = false;
                    continue;
                }

                if (!started && c != '{')
                {
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                        continue;
                    throw new BtplFormatException("InvalidBindings");
                }

                if (c == '"')
                    quoted = true;
                else if (c == '{')
                {
                    depth++;
                    started = true;
                }
                else if (c == '}' && --depth == 0)
                {
                    if (json.Skip(i + 1).Any(x => x != ' ' && x != '\t' && x != '\r' && x != '\n'))
                        throw new BtplFormatException("InvalidBindings");
                    return;
                }
            }

            throw new BtplFormatException("InvalidBindings");
        }

        private static bool JsonSpace(char value) => value == ' ' || value == '\t' || value == '\r' || value == '\n';
        internal static void CheckTokens(string json, string errorCode)
        {
            // Framework 리더가 허용하는 끝 쉼표·잘못된 숫자·이스케이프를 읽기 전에 거부한다.
            char previous = '\0';
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (JsonSpace(c))
                    continue;
                if (c == '"')
                {
                    bool closed = false;
                    for (i++; i < json.Length; i++)
                    {
                        c = json[i];
                        if (c == '"')
                        {
                            closed = true;
                            break;
                        }

                        if (c < 32)
                            throw new BtplFormatException(errorCode);
                        if (c != '\\')
                            continue;
                        if (++i >= json.Length)
                            throw new BtplFormatException(errorCode);
                        c = json[i];
                        if (c == 'u')
                        {
                            if (i + 4 >= json.Length)
                                throw new BtplFormatException(errorCode);
                            for (int k = 1; k <= 4; k++)
                            {
                                char digit = json[i + k];
                                if (!((digit >= '0' && digit <= '9') || (digit >= 'a' && digit <= 'f') || (digit >= 'A' && digit <= 'F')))
                                    throw new BtplFormatException(errorCode);
                            }

                            i += 4;
                        }
                        else if ("\"\\/bfnrt".IndexOf(c) < 0)
                            throw new BtplFormatException(errorCode);
                    }

                    if (!closed)
                        throw new BtplFormatException(errorCode);
                    previous = 'v';
                    continue;
                }

                if ("{}[],:".IndexOf(c) >= 0)
                {
                    if ((c == '}' || c == ']') && (previous == ',' || previous == ':'))
                        throw new BtplFormatException(errorCode);
                    if (c == ',' && "{[:,".IndexOf(previous) >= 0)
                        throw new BtplFormatException(errorCode);
                    previous = c;
                    continue;
                }

                int start = i;
                while (i < json.Length && !JsonSpace(json[i]) && "{}[],:\"".IndexOf(json[i]) < 0)
                    i++;
                string token = json.Substring(start, i - start);
                i--;
                if (token != "true" && token != "false" && token != "null" && !Regex.IsMatch(token, "\\A-?(?:0|[1-9][0-9]*)(?:\\.[0-9]+)?(?:[eE][+-]?[0-9]+)?\\z", RegexOptions.CultureInvariant))
                    throw new BtplFormatException(errorCode);
                previous = 'v';
            }
        }
    }
}
