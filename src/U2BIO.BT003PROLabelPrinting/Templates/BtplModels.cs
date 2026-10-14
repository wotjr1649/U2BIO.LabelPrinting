using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace U2BIO.BT003PROLabelPrinting
{
    /// <summary>TXT 편집 입력의 오류다. 원문을 메시지에 복사하지 않고 코드와 행 번호를 제공한다.</summary>
    public sealed class BtplFormatException : FormatException
    {
        public string Code { get; }
        public int? TemplateLine { get; }

        internal BtplFormatException(string code, int? line = null) : base(Describe(code))
        {
            Code = code;
            TemplateLine = line;
        }

        private static string Describe(string code)
        {
            switch (code)
            {
                case "InvalidEncoding": return "지정한 인코딩으로 읽거나 저장할 수 없습니다.";
                case "TemplateTooLarge": return "서식 파일 또는 저장 내용이 4 MiB를 초과합니다.";
                case "InvalidText": return "한 문구에 줄바꿈이나 제어 문자를 넣을 수 없습니다. 원문 편집을 사용하세요.";
                case "AmbiguousFrame": return "추가 위치를 정할 수 없습니다. 하나의 ^L/E 구간에서 추가하거나 원문을 편집하세요.";
                case "AmbiguousSetting": return "같은 설정 명령이 여러 개입니다. 원문에서 수정할 위치를 선택하세요.";
                case "MissingSetting": return "수정할 설정이 없습니다. 전체 설정 인수를 먼저 지정하세요.";
                case "UnsupportedCommand": return "전용 편집을 지원하지 않는 명령입니다. 원문 편집을 사용하세요.";
                case "LockedTextArguments": return "정렬과 방향은 전용 편집에서 바꾸지 않습니다. 원문 편집을 사용하세요.";
                case "SameSavePath": return "현재 파일과 같은 경로입니다. 다른 이름 저장 대신 Save를 사용하세요.";
                default: return "편집값이 허용된 범위나 형식에 맞지 않습니다.";
            }
        }
    }

    /// <summary>마지막 읽기/저장 이후 원본이 변경되거나 삭제되어 현재 파일 저장을 중단했다.</summary>
    public sealed class BtplSaveConflictException : IOException
    {
        public string Code => "SourceChanged";
        internal BtplSaveConflictException() : base("원본 파일이 변경되거나 삭제되었습니다. 다시 읽거나 다른 이름으로 저장하세요.") { }
    }

    /// <summary>전용 편집 대상 Q/W/S/H 설정이다. 나머지 명령은 TXT 원문에 그대로 둔다.</summary>
    public sealed class BtplSetting
    {
        public string Command { get; }
        public string Parameters { get; }
        public int? TemplateLine { get; }

        public BtplSetting(string command, string parameters) : this(command, parameters, null) { }

        internal BtplSetting(string command, string parameters, int? line)
        {
            Command = command ?? throw new ArgumentNullException(nameof(command));
            Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            TemplateLine = line;
            BtplTemplate.CheckSetting(this);
        }

        internal string ToLine() => Command + Parameters;
    }

    /// <summary>AW 한 줄이다. 이름 있는 값으로 편집하며, 바꾸지 않은 필드의 원래 표기는 유지한다.</summary>
    public sealed class BtplTextElement
    {
        // AW,X,Y,Size,Weight,Alignment,Rotation,Payload 순서다. 마지막 문구의 쉼표는 분리하지 않는다.
        private readonly string[] fields;
        public string Command => "AW";
        public int X => Number(1);
        public int Y => Number(2);
        public int Size => Number(3);
        public int Weight => Number(4);
        public int Alignment => Number(5);
        public int Rotation => Number(6);
        public string Payload => fields[7];
        public int? TemplateLine { get; }

        /// <summary>예제의 정렬/방향 0/0으로 새 AW를 만든다. 크기와 굵기는 호출자가 명시한다.</summary>
        public BtplTextElement(int x, int y, int size, int weight, string payload)
        {
            CheckCoordinate(x);
            CheckCoordinate(y);
            CheckSize(size);
            CheckWeight(weight);
            CheckPayload(payload);
            fields = new[] { "AW", Format(x), Format(y), Format(size), Format(weight), "0", "0", payload };
        }

        private BtplTextElement(string[] sourceFields, int? line)
        {
            fields = (string[])sourceFields.Clone();
            TemplateLine = line;
        }

        /// <summary>읽을 수 없는 AW는 false로 남겨 원문과 미지원 행 번호로 제공한다. 원문 값을 보정하지 않는다.</summary>
        internal static bool TryRead(string raw, int line, out BtplTextElement element)
        {
            element = null;
            string[] parts = raw.Split(new[] { ',' }, 8, StringSplitOptions.None);
            int ignored;
            if (parts.Length != 8 || parts[0] != "AW" || parts.Skip(1).Take(6).Any(value => !TryNumber(value, out ignored))
                || parts[7].Any(char.IsControl)) return false;
            element = new BtplTextElement(parts, line);
            return true;
        }

        /// <summary>X/Y 중 실제로 바뀐 값만 검사·교체한다. 같은 값의 선행0과 부호는 유지한다.</summary>
        public BtplTextElement WithPosition(int x, int y)
        {
            if (x != X) CheckCoordinate(x);
            if (y != Y) CheckCoordinate(y);
            var next = Change(1, x, X);
            return next.Change(2, y, Y);
        }

        public BtplTextElement WithSize(int size)
        {
            if (size == Size) return this;
            CheckSize(size);
            return Change(3, size, Size);
        }

        public BtplTextElement WithWeight(int weight)
        {
            if (weight == Weight) return this;
            CheckWeight(weight);
            return Change(4, weight, Weight);
        }

        /// <summary>문구의 공백·쉼표·!!·변수 표식을 그대로 둔다. 새 명령 행을 끼워 넣는 편집은 거부한다.</summary>
        public BtplTextElement WithPayload(string payload)
        {
            CheckPayload(payload);
            if (payload == Payload) return this;
            var copy = (string[])fields.Clone();
            copy[7] = payload;
            return new BtplTextElement(copy, TemplateLine);
        }

        private BtplTextElement Change(int index, int value, int current)
        {
            if (value == current) return this;
            var copy = (string[])fields.Clone();
            copy[index] = Format(value);
            return new BtplTextElement(copy, TemplateLine);
        }

        private int Number(int index) => int.Parse(fields[index], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        private static string Format(int number) => number.ToString(CultureInfo.InvariantCulture);
        internal string ToLine() => string.Join(",", fields);

        private static bool TryNumber(string value, out int number)
        {
            // 숫자 주변 공백은 해석하여 버리지 않는다. 그런 행은 원문 편집 대상으로 남긴다.
            number = 0;
            if (value.Length == 0 || value.Any(c => (c < '0' || c > '9') && c != '+' && c != '-')) return false;
            return int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number);
        }

        private static void CheckCoordinate(int value) { if (value < 0) throw new BtplFormatException("InvalidArguments"); }
        private static void CheckSize(int value)
        {
            // 제공 SDK의 size * 340 계산이 넘치지 않게 한다. 실제 글꼴/장치의 지원 범위 보장은 아니다.
            if (value <= 0 || value > int.MaxValue / 340) throw new BtplFormatException("InvalidArguments");
        }
        private static void CheckWeight(int value) { if (value < 1 || value > 3) throw new BtplFormatException("InvalidArguments"); }
        private static void CheckPayload(string payload)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (payload.Any(char.IsControl)) throw new BtplFormatException("InvalidText");
        }
    }
}
