# 동기 USB SDK 공개 API

[이슈 #16 스펙](specs/SYNC_USB_MVP.md)에 맞춘 새 API다. 기존 비동기·취소·일괄·BtplTemplate 인쇄 오버로드와 큰 결과 타입은 제거됐다. 선택 Templates 기능으로 렌더링한 문자열은 Print에 넘길 수 있다.

| API | 용도 |
| --- | --- |
| ConnectUsb(string port, int presentationDistanceMm = 0) | USB 포트 지정·연결·Open 확인, 배출 0..1000 mm |
| GetConnectionStatus() | 소유 연결 확인; NativeCode 1=Open, 0=Closed |
| SetPrintingOptions(ushort speed, ushort darkness) | 양의 속도·농도 1..19; 정상 설정을 각 인쇄에 재적용 |
| Print(string btpl, params string[] values) | CRLF 서식과 선택 변수를 SDK에 등록하여 한 장 인쇄 |
| PrintFile(string path, params string[] values) | CP949 파일을 읽기 잠금 상태로 SDK에 등록하여 한 장 인쇄 |
| ConfirmPrintPosition() | 호출자가 실제 용지를 정렬한 뒤 위치 기록을 0으로 확인 |
| Disconnect() / Dispose() | SDK 해제·Closed 확인; 미확인 해제는 소유권 보존 |

Dispose 외의 메서드는 PrinterResult를 반환한다. API는 동기이며 UI 백그라운드 실행은 호출자가 맡는다. 한 프로그램에서만 프린터를 사용한다. 같은 프로세스의 다른 객체는 소유자 SDK 상태를 조회·해제하지 않는다.

```csharp
using (var printer = new Bt003Printer())
{
    var connected = printer.ConnectUsb(selectedUsbPort, presentationDistanceMm);
    if (!connected.IsSuccess) return;
    if (!printer.SetPrintingOptions(4, 12).IsSuccess) return;

    // 서식이 V00/V01을 참조하면 모든 값을 요청마다 제공한다.
    var result = printer.PrintFile(formPath, "FIRST LINE", "SECOND LINE");
    // Status, Code, Function, NativeCode, PrintCode, MovementCode, CleanupCode를 표시한다.
    // 실패·불명 결과라도 PrintCode=0이면 자동 재인쇄하지 않는다.
}
```

selectedUsbPort, formPath와 배출 거리는 호출자가 준비한다. 용지 좌표와 거리 기본값을 임의 적용하지 않는다. 서식의 ^S/^H가 SDK 옵션보다 우선하며 DLL은 서식을 해석·수정하지 않는다. 위 예제는 SDK를 실행하는 사용법이며 자동 소프트웨어 검사는 [오프라인 예제](../examples/OfflineSmoke/Program.cs)만 실행한다.

변수 서식은 V00부터 최고 참조 번호까지 누락 없이 매 요청 값을 제공해야 한다. ResetForm이 이전 변수를 지운다고 보장하지 않는다. 변수 등록 생략은 변수 없는 서식에서만 허용된다. DLL은 슬롯 번호를 해석하지 않으므로 불완전한 입력의 안전성을 보장하지 않는다. 빈 값·제어문자·!!·양 끝 !는 거부한다.

ANSI 코드 페이지 949 환경만 지원한다. 포트·경로·문자열·변수는 엄격한 CP949 왕복과 NUL 검사를 거친다. 서식/합친 변수/파일은 각각 4 MiB 이내다. 입력 오류는 고정 식별 코드의 인수 예외로 전달하며 원문·경로·InnerException을 포함하지 않는다. 지원하지 않는 코드 페이지는 UnsupportedAnsiCodePage 예외다.

PrinterResult.Status는 Succeeded/Failed/Unknown이다. PrintInvoked와 PrintCode는 인쇄 단계 증거, MovementCode는 마지막으로 시도한 배출/복귀의 응답(응답 전 예외는 null), CleanupCode는 별도 정리 오류다. SDK 코드 0과 SendData 0은 물리 완료 증거가 아니다. 인쇄 불명 또는 이동 실패 뒤 다음 자동 인쇄는 MediaPositionUnknown으로 차단한다. 재연결·새 객체도 이를 지우지 않는다. 수동 정렬 후 ConfirmPrintPosition이 필요하다.

cleanup 실패는 원래 인쇄 코드와 Unknown 상태를 보존한다. 원래 정상 응답에 cleanup 실패가 더해지면 전체 Failed이며 PrintCode=0은 유지된다. 연결 정리 실패는 Disconnect로 Closed 확인 후 재연결해야 해제된다. Dispose가 해제를 확인하지 못하면 DisconnectFailed 예외를 던지고 명시적 Disconnect 재시도를 허용한다.

제조사 원본 Wrapper의 StartLabelPrint(1, new StringBuilder(100))를 사용한다. 이전 포인터 보호와 SDK 자체 버퍼 결함 방어는 제외했다. 실제 SDK 로딩·출력은 이번 관리 검사에서 확인하지 않았다.
