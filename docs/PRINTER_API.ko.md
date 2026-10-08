# 연결·사전검사·단건·일괄 인쇄 API

2026-10-09 TASK-05에서 `Bt003Printer`의 연결·상태·사전검사·인쇄·해제를, TASK-06에서 `PrintAsync`·취소·일괄 결과를 구현했다. 아래 API는 컴파일 가능하며 합성 SDK로 검증했다. 새 제품 경로의 실제 장치 출력은 이번 작업에서 실행하지 않았고 TASK-02의 과거 실기와 구분한다.

## 기본 사용

```csharp
var template = BtplTemplate.Load("label.btpl");
template = template.WithText(0, template.TextElements[0].WithPayload(firstLine));
template = template.WithText(1, template.TextElements[1].WithPayload(secondLine));

using (var printer = new Bt003Printer())
{
    var connected = printer.Connect(Bt003ConnectionOptions.Usb(selectedUsbPort));
    if (!connected.IsSuccess)
        return; // 호출 UI에서 Code, NativeFunction, NativeCode를 표시한다.

    var check = printer.ValidateForPrint(template);
    if (!check.IsReady)
        return; // 관리/장치 검사 결과와 Code를 표시한다.

    var result = printer.Print(template);
    // Print는 다시 검사한다. Rows[0]의 Status, PrintInvoked, NativeCode를 확인한다.
}
```

이 코드는 메서드 내부에서 사용하는 예다. 서식에는 텍스트 요소가 두 개 이상 있어야 한다. 문자열 BTPL을 이미 준비했다면 `Print(btpl)` 또는 `ValidateForPrint(btpl)`를 사용한다. DataTable·JSON은 필수가 아니다. 동기 API를 UI 이벤트에서 직접 오래 실행하지 않는다.

## 비동기·일괄·취소

`PrintAsync(string|BtplTemplate|BtplPrintRequest, CancellationToken)`은 같은 worker에서 실행한다. 동기 `Print`도 같은 작업의 결과를 기다린다. 비동기 호출은 대기열에 넣지 않으며 사용 중이면 즉시 `Busy` 결과를 반환한다. token은 생략할 수 있다. 이미 Disposed인 객체와 null 요청은 표준 예외이며 정상 취소는 예외 대신 건별 결과를 반환한다.

```csharp
// 호출자가 준비한 DataTable과 JSON 매핑이 필요한 경우에만 사용한다.
var request = BtplPrintRequest.FromTable(template, bindings, rows);
BatchPrintResult result = await printer.PrintAsync(request, cancellationToken);
foreach (var row in result.Rows)
{
    // RowIndex는 입력 순서다. Status, StopReason, PrintInvoked를 호출 UI에 반영한다.
}
```

템플릿 하나의 전체 행을 먼저 검사한다. 한 행이라도 관리/장치 오류가 있으면 인쇄를 시작하지 않는다. 실행 중 첫 `Failed`/`Unknown` 뒤에는 새 인쇄를 호출하지 않으며 나머지는 `NotAttempted`다. 행 수와 네 상태의 집계 합계는 입력 건수와 같다.

진입 순서는 Disposed/null 계약 확인 → Busy → 사전 취소 → 연결 확인이다. 검사 전 종료하면 batch/row 검사는 null이다. 관리 검사 도중 취소하면 batch `Validation=null`이고 이미 완료한 행의 `Validation`만 보존한다. 관리 검사를 완료했다면 장치 검사 도중 중단돼도 관리 결과를 보존하며 미완료 `DeviceValidation`은 null이다.

같은 프로세스의 다른 `Bt003Printer`가 SDK 슬롯을 소유하면 Print/ValidateForPrint/Disconnect도 `Busy`다. 장치 preflight에서 확인된 포트 오류는 취소/Dispose보다 우선하며 완료된 장치 검사와 NativeCode를 보존한다.

취소는 현재 native 호출을 강제로 중단하지 않는다. 인쇄 호출 중 취소/Dispose 뒤 정상 응답이 돌아오면 현재 행은 `Succeeded`, 다음 행부터 미시도다. 확인된 실패/불명/timeout이 취소보다 우선한다. 마지막 행까지 완료했다면 뒤늦은 취소로 `StopReason=None`을 바꾸지 않는다. 정상 취소 후 정리가 성공하면 연결을 유지한다.

## 연결

| API | 처리 |
| --- | --- |
| `Bt003ConnectionOptions.Usb(port)` | 명시한 USB 식별 문자열. 자동 선택하지 않음 |
| `Bt003ConnectionOptions.Serial(comPort)` | COM 번호 정규화 후 SDK ifType=0에 전달 |
| `Bt003ConnectionOptions.Tcp(ip, port)` | 실제 IP·포트 검증 후 SDK ifType=2에 전달 |
| `Connect(options)` | 프로세스/대상 예약, 지연 SDK 로드, SetPrinterPort·DeviceConnect·포트 확인 |
| `GetConnectionStatus()` | 작업 중/Disconnected/Disposed는 cached 조회; 유휴 소유 연결은 worker에서 포트 확인 |
| `Disconnect()` / `Dispose()` | 소유 연결 해제와 Closed 확인. pending이면 반환 뒤 같은 worker에서 정리 |

동일 객체의 다른 옵션으로 연결을 교체하면 `AlreadyConnected`다. 다른 소유자 또는 진행 중 작업은 `Busy`이며 대기열을 만들지 않는다. abandoned mutex 취득 후에는 설정·연결 전에 native 상태를 재확인한다. `SetPrinterPort=1` 또는 기존 Open 충돌에서는 다른 연결의 `DeviceDisconnect`를 호출하지 않는다.

기본 managed 대기 제한은 각 native 호출당 30초이며 `Bt003Printer(TimeSpan)`으로 1ms~Int32.MaxValue ms 안에서 설정한다. SDK의 통신 timeout 설정이나 실제 장치 중단이 아니다. timeout 결과의 `NativeCallPending`은 불변이며 늦은 정리 상태는 `GetConnectionStatus`에서 확인한다. Dispose 후에는 상태 조회만 허용한다.

실제 native가 아직 진행 중일 때 timeout을 확정하면 `NativeCallPending=true`다. native가 이미 반환한 뒤 제한 초과를 판정하면 false이며, 늦은 정상 응답으로 Unknown을 Succeeded로 바꾸지 않는다.

## 사전검사와 현재 지원 프로필

사전검사는 전체 관리 검사 뒤 인코딩·지원 조합·포트를 검사한다. ResetForm·서식 등록·인쇄·전진/후진은 호출하지 않는다. 관리 오류면 `DeviceValidation=null`이며 검사하지 않은 값을 실패 검사처럼 꾸미지 않는다.

현재 인쇄 프로필은 고정 SDK 쌍, USB, Windows ANSI949, `AW`의 size10/weight1/나머지 인수0·0이다. TASK-02의 확인 범위이며 다른 글꼴 인수나 TCP/IP·RS-232 인쇄는 `DeviceSupportUnverified`로 차단한다. TCP/IP·RS-232 연결 API는 구현했지만 물리 지원은 미검증이다. 인코딩 불가는 `EncodingNotSupported`로 반환하며 모든 글리프 설치를 자동 조회했다고 주장하지 않는다.

좌표·문구·요소 개수와 파서가 지원하는 용지·설정은 호출자가 준비한다. 특정 라벨 규격, 7.5mm offset 또는 배출15mm를 DLL 기본값으로 넣지 않는다. 문구에 42바이트 제한을 적용하지 않으며 넘침·자르기·자동 축소·DPI 검사도 하지 않는다.

## 결과와 정리

Print는 검사를 반복하고 각 행에서 `CheckPrtConnection → ResetForm → SetPrintFormString → StartLabelPrint(1)`을 수행한다. CRLF 완성 서식을 등록하며 `^P1/^C1`은 기존 렌더링 계약을 따른다. 공개 원문 SendData·RFID 기능을 추가하지 않는다.

| 결과 | 판정 |
| --- | --- |
| `Succeeded` | 전용 파서로 확인한 응답 코드0. `CompletionEvidence=SdkResponse` |
| `Failed` | 준비 함수 실패 또는 문서의 확인된 프린터 오류 코드. raw 값 보존 |
| `Unknown` | 응답101, 잘못된 응답, 미확인 응답 코드, 인쇄 진입 뒤 예외/managed timeout |
| `NotAttempted` | 검사 실패·Busy·연결 없음 또는 앞선 중단으로 시작하지 않은 건 |

확인된 실패 코드는 문서 오류표의 1~11,20,21,22,50,60,102~105,205다. 나머지 숫자를 임의 성공/확정 실패로 바꾸지 않으며 `UnrecognizedPrintResponse`와 원래 NativeCode를 반환한다.

`PrintInvoked`는 인쇄 진입 승인/시도이고 실제 출력 증거가 아니다. `Succeeded`도 물리 배출 완료를 뜻하지 않는다. 자동 재출력하지 않는다. 정상 응답42바이트 분석 범위는 문구 길이와 무관하다.

등록이 늦게 끝나도 종결된 작업의 인쇄를 시작하지 않는다. 인쇄 중 Dispose 후 정상 응답이 돌아오면 현재 행 결과를 보존하고 소유 연결을 정리한다. 정리 실패는 `CleanupCode`와 `Calls`에 남기며 완료 행을 실패로 바꾸지 않는다. `NativeCallResult`는 함수명, nullable NativeCode/SystemErrorCode만 보관하며 BTPL·환자 값·UID를 기록하지 않는다. void/예외 호출에 임의 성공 숫자를 만들지 않는다.

SDK 모듈은 프로세스 수명 동안 유지한다. mutex는 취득한 worker가 해제하며 pending/해제 미확인 시 점유를 유지한다. 별도 NativeHost는 없고 native 메모리 손상·프로세스 종료의 격리를 보장하지 않는다. 제조사 자체 로그는 공통 모듈의 메타데이터 진단과 별도 동작이다.

## 실행 증거와 남은 작업

Release/Debug × x86/x64에서 기존 관리 검사와 synthetic 연결/충돌/부분 실패/abandoned/Dispose 경합·late loader, 출력 없는 preflight·단건 호출 순서·긴 두 줄 문구·오류/불명 응답·late 등록/인쇄 및 C02~C13/C16 일괄 계약을 검증한다. 정상 검증은 `nativeSDK=not-loaded`이며 실제 장치 성공 증거로 사용하지 않는다. TASK-06에는 관리 검사 중 취소와 등록 중 취소의 추가 검사가 포함된다.

TASK-05의 새로운 실제 USB 정상/오류 검증과 TASK-07의 제품 실기는 미실행이다. 자동 배출/원점 복귀, 다른 SDK·환경·글꼴/transport·native 장애 경계는 TASK-07 및 후속 #10에서 추적한다. TASK-02의 M15/B15 진단을 자동 제품 기능으로 선언하지 않는다. 배포는 [SDK 배포 가이드](SDK_DEPLOYMENT.ko.md)를 따른다.

TASK-06의 구현 커밋·실행 명령·조건별 결과·산출물 식별은 [일괄 API 검증 기록](BATCH_PRINTING_EVIDENCE.md)에 있다.
