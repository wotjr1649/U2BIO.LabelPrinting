using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using U2BIO.BT003PROLabelPrinting;

namespace U2BIO.BT003PROLabelPrinting.Validation
{
    internal static class TemplateFormatTests
    {
        private static Type formatType;
        private const string Btpl = "^Q40,5,7.5+\n^W70\n^S5\n^H19\n^E15\n^L\nAW,0080,00120,+10,01,00,0,  가나다,^V03!!  \nAW,80,160,10,1,0,0,SECOND\nE\n";
        private static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Template format contract failed."); }
        private static object Format(string name) => Enum.Parse(formatType, name);
        private static object Call(string name, object instance, Type[] types, params object[] args)
        {
            var method = typeof(BtplTemplate).GetMethod(name, types);
            Check(method != null);
            try { return method.Invoke(instance, args); }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }
        private static BtplTemplate Parse(string content, string format)
            => (BtplTemplate)Call("Parse", null, new[] { typeof(string), formatType }, content, Format(format));
        private static string Serialize(BtplTemplate template, string format)
            => (string)Call(format == "Json" ? "ToJson" : "ToXml", template, Type.EmptyTypes);
        private static void Error(Action action, string code = null)
        {
            try { action(); }
            catch (BtplFormatException error) { Check(code == null || error.Code == code); Check(!error.Message.Contains("PRIVATE")); return; }
            throw new InvalidOperationException("Unsafe template document was accepted.");
        }
        internal static void Run()
        {
            formatType = typeof(BtplTemplate).Assembly.GetType("U2BIO.BT003PROLabelPrinting.BtplTemplateFormat");
            if (formatType == null) throw new InvalidOperationException("TemplateFormatsMissing");
            var source = BtplTemplate.Parse(Btpl);
            string normalized = source.ToBtpl();
            foreach (string format in new[] { "Json", "Xml" }) {
                string content = Serialize(source, format);
                var parsed = Parse(content, format);
                Check(parsed.ToBtpl() == normalized);
                Check(parsed.TextElements[0].Payload == "  가나다,^V03!!  ");
                Check(parsed.TextElements[0].Arguments.SequenceEqual(new[] { "+10", "01", "00", "0" }));
                var table = new DataTable(); table.Columns.Add("Name"); table.Rows.Add("SAMPLE");
                var bindings = BtplBindings.Parse("{\"variables\":{\"^V03\":{\"column\":\"Name\"}}}");
                Check(BtplProcessor.Render(BtplPrintRequest.FromTable(parsed, bindings, table), 0).BtplText ==
                    BtplProcessor.Render(BtplPrintRequest.FromTable(source, bindings, table), 0).BtplText);
                Error(() => Parse(content.Replace("AW", "RM"), format), "UnsupportedCommand");
                Error(() => Parse(content.Replace("SECOND", "PRIVATE~PW"), format), "InvalidText");
                Error(() => Parse(content.Replace("SECOND", "PRIVATE^D"), format), "InvalidText");
                Error(() => Parse(content + "PRIVATE", format));
                Error(() => Parse(new string(' ', 4 * 1024 * 1024 + 1), format), "TemplateTooLarge");
                Error(() => Serialize(source.WithText(1, source.TextElements[1].WithPayload(new string('x', 4 * 1024 * 1024 + 1))), format), "TemplateTooLarge");
            }
            string json = Serialize(source, "Json"), xml = Serialize(source, "Xml");
            Check(Serialize(Parse(json, "Json"), "Xml") == Serialize(Parse(xml, "Xml"), "Xml"));
            Error(() => Parse("{\"settings\":[],\"settings\":[],\"texts\":[]}", "Json"));
            Error(() => Parse(json.Replace("\"texts\"", "\"unknown\""), "Json"));
            Error(() => Parse(json.Replace("\"parameters\"", "\"PRIVATE\""), "Json"));
            Error(() => Parse(json.Replace("SECOND", "\\uD800"), "Json"));
            Error(() => Parse("{\"variables\":{}}", "Json"));
            const string numericJson = "{\"settings\":[{\"command\":\"^Q\",\"parameters\":\"40,5\"},{\"command\":\"^W\",\"parameters\":\"70\"},{\"command\":\"^S\",\"parameters\":\"5\"},{\"command\":\"^H\",\"parameters\":\"19\"}],\"texts\":[{\"command\":\"AW\",\"x\":80,\"y\":120,\"arguments\":[\"10\",\"1\",\"0\",\"0\"],\"payload\":\"X\"}]}";
            Check(Parse(numericJson, "Json").TextElements[0].X == 80);
            foreach (string malformed in new[] {
                numericJson.Insert(numericJson.Length - 1, ","),
                numericJson.Replace("\"payload\":\"X\"}", "\"payload\":\"X\",}"),
                numericJson.Replace("\"0\"]", "\"0\",]"),
                numericJson.Replace("\"x\":80", "\"x\":0080"),
                numericJson.Replace("\"x\":80", "\"x\":+80"),
                numericJson.Replace("\"payload\":\"X\"", "\"payload\":\"\\q\"")
            }) Error(() => Parse(malformed, "Json"), "InvalidTemplateDocument");
            Check(Parse(numericJson.Replace("\"payload\":\"X\"", "\"payload\":\"literal ,} ,] and 0080\""), "Json").TextElements[0].Payload == "literal ,} ,] and 0080");
            Error(() => Parse("<!DOCTYPE labelTemplate [<!ENTITY x SYSTEM 'file:///PRIVATE'>]>" + xml, "Xml"));
            Error(() => Parse(xml.Replace("<labelTemplate>", "<labelTemplate PRIVATE='x'>"), "Xml"));
            Error(() => Parse(xml.Replace("<texts>", "<texts/><texts>"), "Xml"));
            Error(() => Parse(xml.Replace("<payload>SECOND</payload>", "<payload><PRIVATE/></payload>"), "Xml"));
            Error(() => Serialize(source.WithText(1, source.TextElements[1].WithPayload("a\nb")), "Json"), "UnexpandedTextLines");
            Files(source, normalized);
            Console.WriteLine("PASS: TXT/JSON/XML same model, ordered lossless roundtrip/render, strict schema/Unicode/DTD/limits, encoding/conflicts/I-O; no SDK/device calls");
        }
        private static void Files(BtplTemplate source, string normalized)
        {
            string directory = Path.Combine(Path.GetTempPath(), "template-formats-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try {
                foreach (string extension in new[] { "txt", "JSON", "xml" }) {
                    string file = Path.Combine(directory, "label." + extension);
                    var saved = source.Save(file);
                    Check(BtplTemplate.Load(file).ToBtpl() == normalized);
                    byte[] bytes = File.ReadAllBytes(file);
                    string format = extension == "txt" ? "Btpl" : extension == "JSON" ? "Json" : "Xml";
                    var explicitLoad = (BtplTemplate)Call("Load", null, new[] { typeof(string), formatType, typeof(int) }, file, Format(format), 949);
                    Check(explicitLoad.ToBtpl() == normalized);
                    if (extension != "txt") {
                        Check(!bytes.Take(3).SequenceEqual(Encoding.UTF8.GetPreamble()));
                        Check(new UTF8Encoding(false, true).GetString(bytes) == Serialize(source, format));
                        File.WriteAllBytes(file, Encoding.UTF8.GetPreamble().Concat(bytes).ToArray());
                        saved = BtplTemplate.Load(file);
                    }
                    var old = BtplTemplate.Load(file);
                    saved = saved.WithText(1, saved.TextElements[1].WithPayload("NEXT")).Save(file);
                    saved = saved.WithSetting(new BtplSetting("^H", "10")).Save(file);
                    Check(BtplTemplate.Load(file).TextElements[1].Payload == "NEXT");
                    try { old.Save(file); throw new InvalidOperationException("Format save conflict missed."); }
                    catch (BtplSaveConflictException) { }
                    bytes = File.ReadAllBytes(file);
                    using (var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None)) {
                        try { saved.Save(file); throw new InvalidOperationException("Format locked file replaced."); }
                        catch (IOException) { }
                    }
                    Check(bytes.SequenceEqual(File.ReadAllBytes(file)));
                    if (extension != "txt") {
                        Error(() => source.WithText(1, source.TextElements[1].WithPayload(new string('x', 4 * 1024 * 1024 + 1))).Save(file), "TemplateTooLarge");
                        Check(bytes.SequenceEqual(File.ReadAllBytes(file)));
                        File.WriteAllBytes(file, new byte[] { 255 });
                        Error(() => BtplTemplate.Load(file), "InvalidEncoding");
                        File.WriteAllBytes(file, new byte[4 * 1024 * 1024 + 1]);
                        Error(() => BtplTemplate.Load(file), "TemplateTooLarge");
                    }
                }
                string overrideFile = Path.Combine(directory, "explicit.dat");
                Call("Save", source, new[] { typeof(string), formatType, typeof(int) }, overrideFile, Format("Json"), 949);
                Check(((BtplTemplate)Call("Load", null, new[] { typeof(string), formatType, typeof(int) }, overrideFile, Format("Json"), 949)).ToBtpl() == normalized);
                Check(!Directory.GetFiles(directory).Any(p => p.EndsWith(".tmp", StringComparison.Ordinal)));
            }
            finally { foreach (string file in Directory.GetFiles(directory)) File.Delete(file); Directory.Delete(directory); }
        }
    }
}
