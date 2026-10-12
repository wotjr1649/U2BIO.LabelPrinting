# USB Wrapper MVP 공개 API

기능 명세는 NFC_BT003-M01-04_Programming_Manual_v1.3.md다. 공개 메서드는 Bt003Printer.cs 한 파일에 모여 있다. 제조사 SDK·실제 장치 동작은 이번 소프트웨어 검사에서 실행하지 않는다.

## 기본 파일 인쇄

```csharp
using (var printer = new Bt003Printer())
{
    var connected = printer.ConnectUsb(selectedUsbPort, presentationDistanceMm);
    if (!connected.IsSuccess) return;

    var configured = printer.SetPrintingOptions(4, 12);
    if (!configured.IsSuccess) return;

    var result = printer.PrintFile(formPath, "FIRST LINE", "SECOND LINE");
    // result.Rows[0].Status, NativeCode, PrintInvoked를 표시한다.
    // PresentationStatus는 별도 배출 명령 결과다. 불명일 때 자동 재출력하지 않는다.
}
```

예제는 호출 메서드 안에서 사용한다. selectedUsbPort·formPath·presentationDistanceMm은 호출자가 준비한다. 배출 기본값은0이다. SDK 변수 인쇄 서식 예:

```text
^Q40,5,7.5+
^W70
^P1
^C1
^L
AW,80,120,10,1,0,0,^V00
AW,80,160,10,1,0,0,^V01
E
```

용지와 좌표는 예제이며 모든 장치에 적용하는 기본값이 아니다. ^S/^H가 없으므로 먼저 성공한 SetPrintingOptions가 필요하다. 서식에 ^S/^H가 있으면 각각 그 값이 우선하며 API 값을 강제 적용하려고 서식을 수정하지 않는다.

## API 지도

| API | 용도 |
| --- | --- |
| ConnectUsb(string port, int presentationDistanceMm=0) | USB 포트 지정·연결·확인 |
| GetConnectionStatus() | 포트/작업/정리 상태 |
| SetPrintingOptions(ushort speed, ushort darkness) | SDK 속도·농도; 성공한 값을 각 인쇄에서 재적용 |
| PrintFile(string path, params string[] values) | CP949 TXT/BTPL 절대경로를 SDK SetPrintFormFile로 등록 |
| Print(string btpl, params string[] values) | SDK SetPrintFormString 및 선택 SetPrintFormVariableData |
| Print(string btpl) | 고정 문구 문자열의 기본 SDK 인쇄 |
| PrintAsync(string btpl, string[] values, CancellationToken token) | 같은 변수 인쇄 경로의 비동기 사용 |
| PrintFileAsync(string path, string[] values=null, CancellationToken token=default) | 파일 보호를 worker 실제 종료까지 유지하는 비동기 사용 |
| Disconnect()/Dispose() | 소유 연결 해제·정리 |
| ConfirmPrintPosition() | 수동 위치 확인 후 기록 초기화 |

속도는 양의 ushort를 받으며 실제 모델 지원 범위는 SDK가 판정한다. 농도는1~19다. 옵션 설정 실패 뒤에는 설정 성공으로 표시하지 않는다. 입력이 잘못된 경우 ArgumentException/BtplFormatException 또는 파일 IO 예외를 호출자에게 전달한다. SDK 수행 결과는 기존 결과 객체의 함수명·코드·실패/불명/미시도로 전달한다.

## 변수 규칙

values[0]=^V00이다. 서식 등장 순서와 무관하게 최고 번호+1개를 전달한다. ^V02만 있어도0·1·2의 세 슬롯이 필요하며 미사용 슬롯에도 비어 있지 않은 자리 값을 넣는다. 모든 슬롯은 매번 전체 전달하며 !!로 연결한 원문을 SDK가 치환한다. SDK에 보낼 서식은 ^Vxx를 유지하고 자체 렌더러가 치환하지 않는다.

빈 값/null·!!·구분자와 충돌하는 선행/후행 !·명령/제어문자·CP949로 표현 불가능한 값은 거부한다. SDK의 빈 슬롯 동작을 추측하지 않는다. 서식·값 배열을 검사·snapshot한 뒤 사용한다. 파일 입력은4 MiB까지 읽고 실제 native 반환까지 쓰기·교체와 부모 경로 변경을 방지한다. 파일 공유·인코딩에 대한 실물 검사는 미실행이다.

## 선택 기능과 호환성

기존 Print(BtplTemplate/BtplPrintRequest), PrintAsync, ValidateForPrint, Connect(options), 연결 factory와 결과 타입은 보존한다. 기존 편집/바인딩의 선택 입력은 이전 검사·렌더링 계약을 사용한다. ValidateForPrint의 기존 overload는 이 선택 모델 계약이며, 새 기본 인쇄는 내부에서 자체 사전검사를 수행하므로 호출자가 선행 호출할 필요가 없다. USB 외 연결 factory는 호환성 보존용이며 이번 MVP의 연결·인쇄·시험 대상으로 제공하지 않는다.

기존 일괄 요청은 전체 사전검사와 실패/불명 뒤 중단을 유지한다. 사용 중에는 Busy이고 대기열을 만들지 않는다. native 호출을 강제로 취소하지 않으며 timeout/늦은 정리를 재사용한다. Succeeded는 SDK 정상 응답 근거이며 물리 장수·출력·배출 완료 보증이 아니다. [배출 계약](PRINT_PRESENTATION.ko.md)을 따른다.

## 배포

호출 프로젝트는 U2BIO.BT003PROLabelPrinting.dll 하나를 참조한다. 실행 폴더에는 관리 DLL과 해당 프로세스 x86/x64에 맞는 BlackfishNfcLib.dll·HFModule.dll을 같이 배포한다. Wrapper 소스는 우리 DLL에 컴파일되고 제조사 DLL은 별도 파일이다. [빌드·배포](SDK_DEPLOYMENT.ko.md)를 따른다.
