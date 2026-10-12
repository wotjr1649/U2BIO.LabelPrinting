namespace U2BIO.BT003PROLabelPrinting
{
    /// <summary>SDK 응답 기준의 결과이며 실제 용지 출력 완료를 뜻하지 않습니다.</summary>
    public enum PrinterResultStatus { Succeeded, Failed, Unknown }

    /// <summary>인쇄 응답과 이동·정리 실패를 구분하여 재인쇄 판단에 필요한 증거를 보존합니다.</summary>
    public sealed class PrinterResult
    {
        public PrinterResultStatus Status { get; internal set; }
        public string Code { get; internal set; }
        public string Function { get; internal set; }
        public int? NativeCode { get; internal set; }
        public bool PrintInvoked { get; internal set; }
        public int? PrintCode { get; internal set; }
        public int? MovementCode { get; internal set; }
        public string CleanupCode { get; internal set; }
        public bool IsSuccess { get { return Status == PrinterResultStatus.Succeeded; } }
    }
}
