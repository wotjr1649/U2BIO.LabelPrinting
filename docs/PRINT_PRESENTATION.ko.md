# Print의 선택 배출·복귀

2026-10-12 [배출 통합 #13](https://github.com/wotjr1649/U2BIO.LabelPrinting/issues/13)의 소프트웨어 계약이다. TASK-02의 M/B 제어 진단을 기존 공개 Print worker에 통합한다. 실제 새 공개 API의 장치 검증은 [#11](HARDWARE_VALIDATION.ko.md)에 보류한다.

## 설정과 사용

```csharp
var template = BtplTemplate.Load("samples/templates/checkup-two-lines.json");
var connection = Bt003ConnectionOptions.Usb(selectedUsbPort)
    .WithPresentationDistance(15); // 이번 시험용 값. 실제 용지/장치에 맞춰 지정한다.

using (var printer = new Bt003Printer())
{
    var connected = printer.Connect(connection);
    if (!connected.IsSuccess) return;

    var result = printer.Print(template);
    // Rows는 인쇄 결과, PresentationStatus는 별도 전진 명령 결과다.
    // Code/CleanupCode/NativeCallPending도 확인하며 오류 시 자동 재출력하지 않는다.

    // 다음 Print는 사전검사 성공 뒤 이전 전진 거리로 먼저 복귀한다.
    // Disconnect/Dispose는 라벨 확인을 방해하는 자동 후진을 하지 않는다.
}
```

기존 Print/PrintAsync overload는 그대로다. WithPresentationDistance는 새 immutable 연결 옵션을 반환하며 기존 옵션을 바꾸지 않는다. 0~1000의 정수 mm를 허용한다. 기본0은 전진 비활성이다. 이는 BTPL M/B 인수 문법 범위이며 모든 거리·용지의 실제 지원을 검증한 범위가 아니다. 현재 물리 근거는 TASK-02의 특정 용지와15mm 진단뿐이다. 다른 transport는 기존 장치 검사에서 계속 차단한다.

연결 중 다른 배출 거리로 Connect하면 AlreadyConnected다. 먼저 Disconnect하고 새 옵션으로 Connect한다. 이전 전진 거리와 새 설정 거리를 구분한다: 이전15mm·새7mm이면 B15 → 인쇄 → M7이다. 새 설정0이면 이전 거리로 복귀하고 인쇄하되 M0은 보내지 않는다.

## 처리 순서

1. 전체 관리/장치 사전검사. 이 단계에서는 전진·후진·서식 등록·인쇄를 하지 않는다.
2. 이전 SDK-수락 전진 거리가 있으면 SendData의 B 명령으로 복귀한다.
3. 기존 worker가 각 행을 CheckPrtConnection → ResetForm → SetPrintFormString → StartLabelPrint(1)로 처리한다.
4. 모든 행이 Succeeded이고 취소/Dispose가 없으면 한 번만 M 명령으로 전진한다. 일괄의 중간 행에서는 전진하지 않는다.
5. 기존 ResetForm/연결 정리 계약을 실행한다.

관리/장치 오류, NoData, Busy, 사전 취소에서는 이송0회다. 첫 Failed/Unknown 뒤 전진하지 않는다. 인쇄 함수의 실패/불명으로 용지 위치를 확정할 수 없으면 배출 활성 연결의 위치를 불명으로 보존한다. 이미 정상 응답을 받은 마지막 인쇄 뒤 취소/Dispose로 전진을 생략한 경우 인쇄 행의 성공을 유지하면서 batch의 Cancelled/Disposed를 표시한다. 전진 비활성인 기존 마지막 행 취소 계약은 유지한다.

호출 전 취소는 명령을 보내지 않는다. 이미 시작한 native 이송은 강제로 중단하지 않으며 반환0은 CommandAccepted로 보존한다. 복귀 중 취소되면 복귀 수락 뒤 새 인쇄를 하지 않는다. 전진 중 취소되면 새 명령/rollback을 추가하지 않는다. timeout은 Unknown으로 반환하고 늦은0으로 결과나 위치 불명을 해소하지 않는다.

## 결과와 위치 상태

| API/필드 | 의미 |
| --- | --- |
| Bt003ConnectionOptions.PresentationDistanceMm | 이 연결의 인쇄 후 전진 거리 |
| BatchPrintResult.PositionReturnStatus | 이번 요청의 복귀 명령: NotAttempted/CommandAccepted/Failed/Unknown |
| BatchPrintResult.PresentationStatus | 이번 요청의 전진 명령: 같은 네 상태 |
| BatchPrintResult.PresentationDistanceMm | 요청 연결의 전진 설정 |
| BatchPrintResult.PendingReturnDistanceMm | 반환 시점의 위치 가정 snapshot |
| PrinterConnectionStatus.PendingReturnDistanceMm | 현재 위치 가정: 0=복귀 불필요, 양수=다음 인쇄 전 복귀 거리, null=불명 |
| Calls | SendData의 raw 반환 코드/예외 메타데이터. 명령 원문이나 인쇄 문구는 기록하지 않음 |

인쇄 행의 Succeeded를 전진 오류 때문에 Failed로 바꾸지 않는다. 예를 들어 인쇄1건 정상·전진9이면 Succeeded=1, batch Code=MediaMovementFailed, PresentationStatus=Failed, 위치=null이다. 이 경우 같은 인쇄를 재출력하지 않는다. 전진/복귀의 예외는 MediaMovementUnknown이며 timeout은 NativeCallTimeout이다. 불명 위치의 preflight/Print는 MediaPositionUnknown으로 차단한다.

위치는 물리 센서로 읽은 값이 아니라 SDK에 수락된 명령에 대한 가정이다. 이전 전진을 복귀한 요청에서는 새 전진 설정이0이어도 인쇄 실패/불명 뒤 위치를null로 보존해 다음 인쇄를 차단한다. CommandAccepted/응답0은 물리 배출 완료를 뜻하지 않는다. 단일 worker와 소유권이 SDK 호출 순서를 보장하며, 프린터 내부 실행 순서·완료 시점은 #11 실기에서 확인해야 한다. 새로운 임의 SendData API나 RFID 호출은 제공하지 않는다.

## 수명과 수동 복구

명시한 정규화 대상별 상태를 같은 프로세스의 메모리에 보존한다. Disconnect/Dispose 뒤 새 객체가 같은 대상에 연결해도 이전 복귀 거리를 사용한다. 해제·폐기는 자체적으로 용지를 움직이지 않는다. 이송 중에는 기존 worker/장치 점유를 유지하며 늦은 종료와 정리를 기다린다.

이 저장은 프린터의 실제 identity/센서 상태 조회나 영구 저장이 아니다. 프로그램 재시작, 장치 교체/전원 재설정, 다른 프로그램의 사용, 수동 FEED/당김, 용지 교체는 자동 감지하지 않는다. 처음 사용은 호출자가 용지를 인쇄 기준 위치에 준비해야 한다. 이전 기록의 대상과 물리 장치가 같고 중간에 다른 이송이 없다는 전제에서만 자동 복귀를 사용한다.

위치를 수동으로 다시 맞춘 뒤에는 유휴 Connected 객체에서 다음을 호출한다.

```csharp
// 사용자가 먼저 용지를 실제 인쇄 기준 위치로 맞추고 확인한 경우에만 호출한다.
var acknowledged = printer.ConfirmPrintPosition();
```

ConfirmPrintPosition은 물리 위치를 검증/이송하는 함수가 아닌 호출자의 명시적 확인이다. 성공 시 대상의 불명/복귀 기록을0으로 바꾼다. 같은 프로세스의 다른 소유자나 pending 작업이 있으면 Busy, 미연결이면 NotConnected, Disposed면 표준 예외다. Faulted인 객체는 pending 종료·해제 확인 → Disconnect → 재연결 → 수동 위치 확인 → Confirm 순으로 복구한다. 오류를 무시하고 무조건 Confirm하거나 새 객체로 불명 상태를 우회하지 않는다.

## 샘플 서식

TXT/JSON/XML의 settings/texts 스키마·문구·좌표·AW 인수·용지 설정은 변경하지 않는다. 서식에 M/B나 배출 전용 필드를 넣지 않는다. E15는 원래 설정으로 보존하지만 SDK 전처리가 이를 바꿀 수 있어 배출 보장이 아니다. 15mm는 샘플 호출 코드의 옵션이며 DLL의 공통 기본값이 아니다. 저장소 샘플의 파일 줄바꿈은 LF, SDK에 등록하는 정규 BTPL은 기존 ToBtpl의 CRLF다.

## 검증 경계

PrinterPresentationTests는 거리 경계, 사전검사 무이송, 기본0, 단건/일괄 마지막 전진, 이전/새 거리 구분, 연결 해제/새 객체, 이송 실패/예외, 명시 확인, 취소/Dispose, timeout과 늦은 반환을 합성 SDK로 검사한다. 실제 vendor SDK/장치/출력은 실행하지 않는다. in-process native 손상/프로세스 종료와 다른 장치/용지/거리의 보장은 기존 미검증 범위로 유지한다.
