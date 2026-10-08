using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace U2BIO.BT003PROLabelPrinting
{
    internal static class BtplSyntax
    {
        internal static readonly string[] Settings = { "^Q", "^W", "^S", "^H", "^G", "^P", "^C", "^R", "^E", "^D", "^O" };
        internal static int Number(string value, int min, int max, int? line)
        {
            int number;
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number) || number < min || number > max)
                throw new BtplFormatException("InvalidArguments", line);
            return number;
        }
        private static void Millimetres(string value, bool positive, int? line)
        {
            decimal number;
            if (!Regex.IsMatch(value, "\\A[0-9]+(?:\\.[0-9]+)?\\z", RegexOptions.CultureInvariant) ||
                !decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number) ||
                (positive ? number <= 0 : number < 0)) throw new BtplFormatException("InvalidArguments", line);
        }
        internal static void CheckSetting(BtplSetting setting)
        {
            string c = setting.Command, value = setting.Parameters;
            int? line = setting.Line;
            if (!Settings.Contains(c, StringComparer.Ordinal)) throw new BtplFormatException("UnsupportedCommand", line);
            switch (c) {
                case "^Q":
                    string[] parts = value.Split(',');
                    if (parts.Length < 2 || parts.Length > 3) throw new BtplFormatException("InvalidArguments", line);
                    Millimetres(parts[0], true, line); Millimetres(parts[1], false, line);
                    if (parts.Length == 3) {
                        string offset = parts[2];
                        if (offset.EndsWith("+", StringComparison.Ordinal) || offset.EndsWith("-", StringComparison.Ordinal)) offset = offset.Substring(0, offset.Length - 1);
                        Millimetres(offset, false, line);
                    }
                    break;
                case "^W": Millimetres(value, true, line); break;
                case "^S": Number(value, 2, 7, line); break;
                case "^H": Number(value, 0, 19, line); break;
                case "^G": Number(value, 0, 2, line); break;
                case "^P": case "^C": Number(value, 1, 1, line); break;
                case "^R": Number(value, 0, 399, line); break;
                case "^D": case "^O": Number(value, 0, 0, line); break;
                case "^E":
                    string[] stop = value.Split(',');
                    if (stop.Length > 3) throw new BtplFormatException("InvalidArguments", line);
                    for (int i = 0; i < stop.Length; i++) Number(stop[i], 0, i < 2 ? 40 : int.MaxValue, line);
                    break;
            }
        }
        internal static int ArgumentCount(string command)
        {
            if (command == "AW" || Regex.IsMatch(command, "\\A(?:A[A-HIKL]|AZ[1-9]|V[A-Z])\\z", RegexOptions.CultureInvariant)) return 4;
            if (Regex.IsMatch(command, "\\AAT[A-Z]?\\z", RegexOptions.CultureInvariant)) return 6;
            return -1;
        }
        internal static BtplTextElement ParseText(string value, int line)
        {
            int comma = value.IndexOf(',');
            string command = comma < 0 ? value : value.Substring(0, comma);
            int count = ArgumentCount(command);
            if (count < 0) throw new BtplFormatException("UnsupportedCommand", line);
            string[] parts = value.Split(new[] { ',' }, count + 4);
            if (parts.Length != count + 4) throw new BtplFormatException("InvalidArguments", line);
            var element = new BtplTextElement(command, parts[1], parts[2], parts.Skip(3).Take(count), parts[count + 3], line);
            CheckText(element, false); return element;
        }
        internal static void CheckText(BtplTextElement text, bool allowLines)
        {
            int count = ArgumentCount(text.Command);
            if (count < 0) throw new BtplFormatException("UnsupportedCommand", text.Line);
            if (text.Arguments.Count != count || text.Arguments.Any(x => x == null)) throw new BtplFormatException("InvalidArguments", text.Line);
            var a = text.Arguments;
            if (text.Command == "AW") {
                foreach (string value in a) {
                    int number;
                    if (!Regex.IsMatch(value, "\\A[+-]?[0-9]+\\z", RegexOptions.CultureInvariant) ||
                        !int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number))
                        throw new BtplFormatException("InvalidArguments", text.Line);
                }
            } else if (count == 4) {
                Number(a[0], 1, 8, text.Line); Number(a[1], 1, 8, text.Line); Number(a[2], 0, int.MaxValue, text.Line);
                string rotation = text.Command.StartsWith("AZ", StringComparison.Ordinal) ? "\\A[0-7]I?\\z" : "\\A[0-3]I?\\z";
                if (!Regex.IsMatch(a[3], rotation, RegexOptions.CultureInvariant)) throw new BtplFormatException("InvalidArguments", text.Line);
            } else {
                int mode = Number(a[5], 0, 1, text.Line);
                int width = Number(a[0], 0, 2000, text.Line);
                if ((width == 0 && mode != 1) || (width != 0 && width < 8)) throw new BtplFormatException("InvalidArguments", text.Line);
                Number(a[1], 8, 2000, text.Line); Number(a[2], 0, 200, text.Line);
                if (!Regex.IsMatch(a[3], "\\A[0-3][BTU]*\\z", RegexOptions.CultureInvariant) || a[3].Skip(1).Distinct().Count() != a[3].Length - 1 ||
                    !Regex.IsMatch(a[4], text.Command == "AT" ? "\\A0\\z" : "\\A(?:0|[A-Z])\\z", RegexOptions.CultureInvariant))
                    throw new BtplFormatException("InvalidArguments", text.Line);
            }
            CheckPayload(text.Payload, allowLines, text.Line);
            if (text.Command.StartsWith("V", StringComparison.Ordinal) && SplitLines(text.Payload).Any(x => x.Length > 239))
                throw new BtplFormatException("TextCommandLimit", text.Line);
        }
        internal static string[] SplitLines(string payload) => payload.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        private static void CheckPayload(string payload, bool allowLines, int? line)
        {
            for (int i = 0; i < payload.Length; i++) {
                char c = payload[i];
                if (c == '^') {
                    if (i + 3 >= payload.Length || payload[i + 1] != 'V' || payload[i + 2] < '0' || payload[i + 2] > '9' || payload[i + 3] < '0' || payload[i + 3] > '9')
                        throw new BtplFormatException("InvalidText", line);
                    i += 3;
                } else if (c == '~' || (char.IsControl(c) && !(allowLines && (c == '\r' || c == '\n'))))
                    throw new BtplFormatException("InvalidText", line);
            }
        }
    }
}
