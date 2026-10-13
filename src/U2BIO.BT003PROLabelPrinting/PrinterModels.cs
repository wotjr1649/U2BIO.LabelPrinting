using System.Diagnostics;

namespace U2BIO.BT003PROLabelPrinting
{
    /// <summary>IsSuccess로 정상 여부를 판단하고 Message로 이유를 읽는 결과입니다. 인쇄 호출·응답은 별도로 보존합니다.</summary>
    [DebuggerDisplay("{IsSuccess}: {Message}")]
    public sealed class PrinterResult
    {
        // 실행하지 않은 결과를 호출자가 기본 생성자로 만들지 않도록 DLL 내부에서만 생성한다.
        internal PrinterResult() { }

        /// <summary>요청한 SDK 처리와 필요한 배출·정리가 정상적으로 끝나면 true입니다. 실제 용지 출력 완료를 뜻하지 않습니다.</summary>
        /// <remarks>실패 또는 결과 불명이면 false입니다. 일부 출력이 이루어졌을 수 있으므로 자동 재인쇄하지 않습니다.</remarks>
        public bool IsSuccess { get; internal set; }

        /// <summary>주 작업의 상태, SDK 함수와 이동·정리 오류를 설명하는 한글 문장입니다. 프로그램은 이 문장을 파싱하지 않고 IsSuccess로 판단합니다.</summary>
        public string Message { get; internal set; }

        /// <summary>주 작업의 마지막 SDK 응답입니다. 미호출·응답 전 예외는 null이며 인쇄 후 배출도 주 작업에 포함합니다. 후속 서식 정리는 이 값을 덮지 않습니다.</summary>
        public int? NativeCode { get; internal set; }

        /// <summary>StartLabelPrint 호출에 진입하려는 시점에 true로 기록합니다. 실제 출력 완료 여부를 뜻하지 않습니다.</summary>
        public bool PrintInvoked { get; internal set; }

        /// <summary>인쇄 응답에서 해석한 제조사 코드입니다. 미호출·응답 전 예외·잘못된 응답은 null이며 배출과 정리는 이 값을 덮지 않습니다.</summary>
        public int? PrintCode { get; internal set; }
    }
}
