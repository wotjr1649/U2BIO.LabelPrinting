# 동기 인쇄의 선택 배출·복귀

현재 계약은 [이슈 #16 스펙](specs/SYNC_USB_MVP.md)과 [공개 API](PRINTER_API.ko.md)다. 이전 #13 worker/일괄/취소 계약과 검증 기록은 [역사 문서](history/PRINT_PRESENTATION_issue13.md)에 보존한다.

```csharp
using (var printer = new Bt003Printer())
{
    // 15는 시험 예시이며 실제 용지와 장치에 맞춰 지정한다. 기본값은 0이다.
    var connected = printer.ConnectUsb(selectedUsbPort, 15);
    if (!connected.IsSuccess) return;
    var result = printer.PrintFile(formPath);
    // bool로 정상 여부를 판단하고 Message로 이유를 읽는다. PrintCode는 인쇄 응답을 보존한다.
    if (!result.IsSuccess) Console.WriteLine(result.Message);
    // 실패·불명 또는 정상 인쇄 뒤 배출 실패에 자동 재출력하지 않는다.
}
```

배출 거리는 ConnectUsb의 0..1000 mm 인수다. 정상 응답을 받은 한 장 인쇄 뒤에만 ^M을 보내고, 다음 인쇄 전에 이전 정상 배출 거리를 ^B로 복귀한다. 이전 15·새 설정 7이면 B15 → 인쇄 → M7이다. 새 설정 0이면 이전 거리는 복귀하되 추가 전진하지 않는다. 연결 해제 자체는 용지를 움직이지 않는다.

이동 진입 전에 포트별 위치를 불명으로 둔다. 반환 0에서만 명령 이력 거리를 기록한다. 비0·예외나 인쇄 불명 뒤에는 배출 설정 0이어도 다음 자동 인쇄를 IsSuccess=false로 차단하고 Message에 수동 정렬 필요를 설명한다. 재연결·새 객체가 위치 기록을 지우지 않는다. 주 작업의 마지막 이동 응답은 NativeCode이며 응답 전 예외는 null이다. Message는 복귀 또는 배출 실패를 구분하여 설명하고 PrintCode=0은 후속 이동·정리 실패에도 보존된다.

SDK 응답과 명령 이력은 물리 인쇄·이동 완료나 센서 위치 확인이 아니다. 사용자가 실제 용지를 인쇄 기준 위치로 정렬한 뒤 ConfirmPrintPosition을 호출해야 한다. 연결 정리 오류는 Disconnect의 Closed 확인 후 재연결해야 복구된다. 수동 확인을 오류 무시 수단으로 자동 호출하지 않는다.

포트별 기록은 프로세스 메모리와 `C:\Bitek\BT003\position.txt`에 함께 둔다. 프로그램을 다시 실행하면 파일 기록으로 직전 배출 거리를 복귀한다. 파일이 없는 최초 실행이나 손상·다른 포트·중단된 기록은 위치 불명이며, 운영자가 정렬한 뒤 ConfirmPrintPosition을 호출하면 파일이 0으로 생성된다. 다른 프로그램의 사용, 장치 교체, 수동 FEED/당김과 용지 교체는 자동 감지하지 않는다. LCD 정지 위치는 잠금 중에도 바뀐 관측이 있어 운영 기준으로 쓰지 않는다. 서식의 E 설정은 SDK 서식 설정이며 DLL의 배출 완료 보장이 아니다. 배출을 위해 템플릿에 M/B를 추가하지 않는다.

관리 Link harness가 복귀/배출 실패·예외, 마지막 이동 응답, 위치 불명과 재연결, 수동 확인, 해제 실패 뒤 재사용 차단을 확인한다. 실제 물리 출력·반복 정렬·배출은 [별도 실기](HARDWARE_VALIDATION.ko.md)에서 확인해야 한다.
