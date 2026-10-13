using System.Diagnostics;

namespace U2BIO.BT003PROLabelPrinting
{
    /// <summary>SDK 응답 기준의 결과이며 실제 용지 출력 완료를 뜻하지 않습니다.</summary>
    public enum PrinterResultStatus
    {
        /// <summary>요청한 SDK 처리와 필요한 정리가 정상 응답으로 끝났습니다.</summary>
        Succeeded,

        /// <summary>호출 조건, SDK 응답 또는 정리 단계에서 실패했습니다. 실제 무출력을 뜻하지 않으므로 인쇄 증거도 확인합니다.</summary>
        Failed,

        /// <summary>인쇄 호출 이후 예외나 불명 응답 등으로 결과를 확정할 수 없습니다. 자동 재인쇄하지 않습니다.</summary>
        Unknown
    }

    /// <summary>Status로 처리 상태를 판단하고 Message로 이유를 읽는 결과입니다. 인쇄 호출·응답은 별도로 보존합니다.</summary>
    [DebuggerDisplay("{Status}: {Message}")]
    public sealed class PrinterResult
    {
        // 실행하지 않은 결과를 호출자가 기본 생성자로 만들지 않도록 DLL 내부에서만 생성한다.
        internal PrinterResult() { }

        /// <summary>요청 전체의 SDK 처리 결과입니다. 인쇄 응답이 정상이어도 배출이나 정리가 실패하면 정상 상태가 아닐 수 있습니다.</summary>
        public PrinterResultStatus Status { get; internal set; }

        /// <summary>주 작업의 상태, SDK 함수와 이동·정리 오류를 설명하는 한글 문장입니다. 프로그램은 이 문장을 파싱하지 않고 Status로 판단합니다.</summary>
        public string Message { get; internal set; }

        /// <summary>주 작업의 마지막 SDK 응답입니다. 미호출·응답 전 예외는 null이며 인쇄 후 배출도 주 작업에 포함합니다. 후속 서식 정리는 이 값을 덮지 않습니다.</summary>
        public int? NativeCode { get; internal set; }

        /// <summary>StartLabelPrint 호출에 진입하려는 시점에 true로 기록합니다. 실제 출력 완료 여부를 뜻하지 않습니다.</summary>
        public bool PrintInvoked { get; internal set; }

        /// <summary>인쇄 응답에서 해석한 제조사 코드입니다. 미호출·응답 전 예외·잘못된 응답은 null이며 배출과 정리는 이 값을 덮지 않습니다.</summary>
        public int? PrintCode { get; internal set; }
    }
}
