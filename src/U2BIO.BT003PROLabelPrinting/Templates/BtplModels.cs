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
                case "GapLabelSetting": return "^Q는 길이,마크 폭,위치± 세 인수로 지정하세요. 두 인수 ^Q는 프린터 라벨 유형을 간격 라벨로 바꿉니다.";
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
            CheckX(x);
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
            if (x != X) CheckX(x);
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
        // 실기에서 AW X=0은 SDK StartLabelPrint 예외를 냈다(X=1부터 정상). Y=0은 정상이므로 X만 1 이상으로 제한한다.
        internal static void CheckX(int value) { if (value < 1) throw new BtplFormatException("InvalidArguments"); }
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

    /// <summary>서식 좌표(dot)의 직사각형 범위다. Right·Bottom은 범위 끝 좌표다.</summary>
    public sealed class BtplArea
    {
        public int Left { get; }
        public int Top { get; }
        public int Right { get; }
        public int Bottom { get; }

        internal BtplArea(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }
    }

    /// <summary>블랙마크 용지를 자로 잰 값(mm)으로 ^Q·^W 설정과 흰 라벨 인쇄 영역(dot)을 계산합니다.</summary>
    /// <remarks>
    /// <para>모든 길이는 mm이며 소수 셋째 자리까지, 절댓값 1000 이하입니다. "폭"은 급지 방향에 수직, "길이·높이"는 급지 방향 치수입니다.</para>
    /// <para>계산: <c>^Q{segmentLengthMm},{markWidthMm},{z}+</c>, z = markToLabelMm + sensorOffsetMm(0.5 mm 단위 반올림), <c>^W{linerWidthMm}</c>.
    /// LabelArea는 서식 원점(0,0)에서 실측 보정만큼 떨어진 흰 라벨의 서식 좌표이고 SafeArea는 그보다 2 mm 안쪽입니다.</para>
    /// <para>예: 받침지 70, 마크 제외 길이 35(피치 40 − 마크 5), 마크 5, 흰 부분 7.5, 흰 라벨 60×20, 300dpi → ^Q35,5,9+, ^W70,
    /// LabelArea X 47~756·Y 43~279, SafeArea X 71~732·Y 66~255. BT-003 Pro 실기에서 이 z는 FEED 직후·연속·복귀·재실행 모두 같은 위치였고,
    /// 흰 라벨 상단은 원점보다 3.6 mm 아래, 왼쪽 끝은 받침지 가운데 기준보다 1.0 mm 왼쪽이었습니다(기본 보정값).
    /// 같은 용지에서 z=12.5(마크 + 흰 부분)는 원점이 라벨 상단에 오지만 FEED 직후 약 20 mm, 연속 셋째 장부터 약 3 mm 어긋나 쓰지 않습니다.</para>
    /// <para>다른 용지나 센서 재보정 뒤에는 계산값으로 시작해 눈금 라벨로 z의 안정성과 보정값을 확인합니다. 인쇄 후 배출 거리와 글자 높이는 계산하지 않습니다.
    /// 운영 중 ^Q 값을 바꾸면 바꾼 뒤 첫 장은 이전 원점으로 인쇄되므로 한 장 인쇄 뒤 FEED로 다시 맞춥니다.</para>
    /// </remarks>
    public sealed class BtplBlackMarkMedia
    {
        // 미리보기·배치 검사용 여유다. 라벨 위치 편차와 치수 측정 오차(실측 약 2 mm)를 흡수한다.
        private const decimal SafeMarginMm = 2m;
        // BTPL의 mm 인수 범위(예: ^B/^M 0~1000 mm)에 맞춘 입력 상한이다.
        private const decimal MaximumMm = 1000m;

        /// <param name="linerWidthMm">받침지(뒷종이) 전체 폭입니다. 급지 방향에 수직으로 재며 ^W 값이 됩니다. 예: 70.</param>
        /// <param name="segmentLengthMm">한 블랙마크의 끝에서 다음 블랙마크의 시작까지 급지 방향 길이입니다. 피치에서 마크 폭을 뺀 값이며 ^Q의 첫 인수입니다.
        /// 피치(마크 포함 40)를 넣으면 프린터가 피치를 45 mm로 알아 빈 라벨이 나올 수 있습니다. 예: 35.</param>
        /// <param name="markWidthMm">블랙마크 하나의 급지 방향 길이입니다. 받침지 뒷면에서 재며 ^Q의 둘째 인수입니다. 예: 5.</param>
        /// <param name="markToLabelMm">블랙마크의 흰 라벨 쪽 경계에서 흰 라벨 상단(서식 Y0가 놓일 끝)까지의 흰 받침지 길이입니다.
        /// 마크 폭은 포함하지 않습니다(마크를 더한 12.5가 아니라 7.5). 0 이상입니다. 예: 7.5.</param>
        /// <param name="labelWidthMm">흰 라벨(스티커) 폭입니다. 급지 방향에 수직으로 재며 받침지 가운데에 있다고 가정합니다. 4 mm보다 커야 합니다. 예: 60.</param>
        /// <param name="labelHeightMm">흰 라벨 높이(급지 방향)입니다. markToLabelMm과 더한 값이 segmentLengthMm 이하여야 하며 4 mm보다 커야 합니다. 예: 20.</param>
        /// <param name="dpi">프린터 헤드 해상도입니다. 203 또는 300만 허용하며 SafeArea·LabelArea의 dot 환산에 씁니다. 예: 300.</param>
        /// <param name="sensorOffsetMm">자로 잰 흰 부분과 프린터가 쓰는 z의 차이(mm)입니다. 기본값 1.5는 이 BT-003 Pro의 실측값입니다.
        /// z를 바꾸면 원점이 1 mm당 약 dpi ÷ 25.4 dot 움직이지만, 마크 경계와 맞물리는 값은 위치가 흔들리므로 눈금 라벨 여러 장으로 안정성을 확인합니다.</param>
        /// <param name="verticalOffsetMm">서식 원점(Y0)에서 흰 라벨 상단까지의 거리(mm)입니다. 눈금 라벨에서 라벨 상단 경계가 걸린 Y dot ÷ (dpi ÷ 25.4)로 잽니다.
        /// 기본값 3.6은 이 프린터·운영 기준 용지의 실측값(Y≈42)입니다. 0 이상이며 라벨이 원점 위로 나가면 인쇄할 수 없어 거부합니다.</param>
        /// <param name="horizontalOffsetMm">흰 라벨 왼쪽 끝이 받침지 가운데 정렬 위치보다 오른쪽(+)·왼쪽(−)으로 떨어진 거리(mm)입니다.
        /// 눈금 라벨의 실제 왼쪽 끝 X dot ÷ (dpi ÷ 25.4) − (받침지 폭 − 라벨 폭) ÷ 2로 잽니다. 기본값 −1.0은 이 프린터의 실측값(X≈47)입니다.</param>
        /// <exception cref="BtplFormatException">값이 범위(절댓값 1000 mm 이하, 소수 셋째 자리까지)를 벗어나거나 라벨이 받침지·마크 사이 길이에 들어가지 않으면 InvalidArguments입니다.</exception>
        public BtplBlackMarkMedia(decimal linerWidthMm, decimal segmentLengthMm, decimal markWidthMm, decimal markToLabelMm,
            decimal labelWidthMm, decimal labelHeightMm, int dpi = 300, decimal sensorOffsetMm = 1.5m, decimal verticalOffsetMm = 3.6m, decimal horizontalOffsetMm = -1.0m)
        {
            // 계산 전에 범위와 자릿수를 검사해 overflow나 출력 반올림으로 다른 값이 만들어지지 않게 한다.
            decimal[] values = { linerWidthMm, segmentLengthMm, markWidthMm, markToLabelMm, labelWidthMm, labelHeightMm, sensorOffsetMm, verticalOffsetMm, horizontalOffsetMm };
            if (values.Any(value => Math.Abs(value) > MaximumMm || value != Math.Round(value, 3)))
            {
                throw new BtplFormatException("InvalidArguments");
            }
            decimal z = Math.Round((markToLabelMm + sensorOffsetMm) * 2, MidpointRounding.AwayFromZero) / 2;
            if (linerWidthMm <= 0 || segmentLengthMm <= 0 || markWidthMm <= 0 || markToLabelMm < 0 || labelWidthMm <= 0 || labelHeightMm <= 0
                || (dpi != 203 && dpi != 300) || z < 0
                || labelWidthMm > linerWidthMm || markToLabelMm + labelHeightMm > segmentLengthMm
                || labelWidthMm <= SafeMarginMm * 2 || labelHeightMm <= SafeMarginMm * 2)
            {
                throw new BtplFormatException("InvalidArguments");
            }

            LabelSetting = new BtplSetting("^Q", Mm(segmentLengthMm) + "," + Mm(markWidthMm) + "," + Mm(z) + "+");
            WidthSetting = new BtplSetting("^W", Mm(linerWidthMm));
            // 라벨은 받침지 가운데 기준에서 실측 가로 보정만큼, 원점에서 실측 세로 보정만큼 떨어져 놓인다.
            decimal left = (linerWidthMm - labelWidthMm) / 2 + horizontalOffsetMm;
            decimal top = verticalOffsetMm;
            if (left < 0 || top < 0)
            {
                throw new BtplFormatException("InvalidArguments");
            }
            LabelArea = Area(dpi, left, top, left + labelWidthMm, top + labelHeightMm);
            SafeArea = Area(dpi, left + SafeMarginMm, top + SafeMarginMm, left + labelWidthMm - SafeMarginMm, top + labelHeightMm - SafeMarginMm);
        }

        /// <summary>계산한 ^Q 설정이며 BtplTemplate.WithSetting에 그대로 쓸 수 있습니다.</summary>
        public BtplSetting LabelSetting { get; }

        /// <summary>계산한 ^W 설정입니다.</summary>
        public BtplSetting WidthSetting { get; }

        /// <summary>입력 치수와 보정으로 계산한 흰 라벨 전체의 서식 좌표 범위(dot)입니다. 라벨 폭 측정 오차만큼 실측과 다를 수 있어 배치에는 SafeArea를 씁니다.</summary>
        public BtplArea LabelArea { get; }

        /// <summary>흰 라벨에서 2 mm 안쪽의 권장 배치 범위(dot)입니다.</summary>
        public BtplArea SafeArea { get; }

        private static string Mm(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        private static BtplArea Area(int dpi, decimal left, decimal top, decimal right, decimal bottom)
        {
            decimal dotsPerMm = dpi / 25.4m;
            return new BtplArea(Dots(left * dotsPerMm), Dots(top * dotsPerMm), Dots(right * dotsPerMm), Dots(bottom * dotsPerMm));
        }

        private static int Dots(decimal value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }
}
