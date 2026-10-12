# U2BIO.BT003PROLabelPrinting의 역할과 확장 범위

2026-10-08 사용자 정정에 따라 모듈의 기본 사용을 다시 정리했다. 구현 기준 태그 v0.1.1-design은 보존한다. 2026-10-09 TASK-05에서 연결·사전검사·인쇄 API를, TASK-06에서 PrintAsync·취소·일괄 결과를 구현하고 synthetic SDK로 검증했다. [현재 사용법·지원 조건·실기 미검증](PRINTER_API.ko.md)을 구분한다.

## 모듈의 정의

U2BIO.BT003PROLabelPrinting은 제조사 BlackfishNfcLib.dll wrapper의 연결·텍스트 라벨 인쇄 기능을 Checkup, SmartCheckup, Tester에서 재사용하는 .NET Framework 4.6.1 공통 DLL이다. 호출 프로그램은 명시한 연결 대상과 BTPL을 전달하여 연결·검사·인쇄·결과 확인·해제를 수행한다. BTPL이 위치·문구·용지·글꼴 설정을 결정한다.

기본 사용은 Connect → Print(BTPL) → Disconnect다. DataTable, JSON, DB 연결, DevExpress 또는 별도 NativeHost.exe를 기본 사용의 필수 구성으로 요구하지 않는다. 기존 템플릿 편집·바인딩 구현은 같은 DLL의 선택 기능으로 유지한다.

2026-10-12 서식 확장: TXT/BTPL·JSON·XML을 모두 같은 BtplTemplate으로 변환하며 저장 형식만 다르다. JSON/XML의 레이아웃 정보는 별도 인쇄 엔진이 아니라 기존 설정·텍스트 요소의 표현이다. 선택 바인딩 JSON은 별도 API와 스키마를 유지한다. [서식 API와 예](TEMPLATE_FORMATS.ko.md)를 따른다.

```text
Checkup / SmartCheckup / Tester
    → U2BIO.BT003PROLabelPrinting.dll
        → BlackfishNfcLib.dll
            → HFModule.dll
```

HFModule.dll은 원본 x86/x64 Blackfish의 PE import에서 확인한 제조사 의존 파일이다. 우리 공개 API가 HFModule의 장치 함수를 직접 호출하지 않는다. HFSample.exe는 샘플 프로그램이다. 실행 프로그램과 같은 비트 수의 제조사 DLL 한 쌍을 배포하며 공통 관리 DLL은 AnyCPU다.

## 기본 API와 제조사 wrapper의 관계

| 공개 API | 내부 wrapper 기능 | 책임 |
| --- | --- | --- |
| Connect(Bt003ConnectionOptions) | SetPrinterPort → DeviceConnect → CheckPrtConnection | USB 식별자, IP/port, COM 번호를 명시하고 연결 결과 확인 |
| GetConnectionStatus() | 유휴 때 CheckPrtConnection; 작업 중/폐기 후 cached 상태 | 포트 연결·사용 중·정리 상태 조회. 용지·리본 준비 보증과 구분 |
| ValidateForPrint(...) | 관리 검사, 인코딩·지원 조합 검사, CheckPrtConnection | 출력 없이 검사. ResetForm/등록/용지 이송 호출 없음 |
| Print(string btpl) | 같은 private Print 경로의 ResetForm → SetPrintFormString → StartLabelPrint(1) → 전용 응답 판정 | 별도 요청/바인딩 객체를 요구하지 않는 한 장 편의 API |
| Print(BtplTemplate template) | 같은 경로 | 편집·저장한 템플릿의 완성 문구를 한 장 출력 |
| Print(BtplPrintRequest request) | 같은 경로 | 선택한 바인딩·다건 입력 사용. 전체 사전검사 뒤 입력 순서대로 실행 |
| PrintAsync(string/template/request, token) | 같은 SDK worker와 인쇄 경로 | UI를 점유하지 않는 단건·일괄 처리와 취소·건별 결과 |
| Disconnect()/Dispose() | 소유한 연결의 DeviceDisconnect → CheckPrtConnection | 연결 해제·사용 자원 정리; 다른 소유자의 연결을 해제하지 않음 |

Print(string)/Print(template)는 입력을 Parse 또는 Fixed 요청으로 변환하고 같은 Print(request) 경로를 재사용한다. 별도 인쇄 엔진을 만들지 않는다. Print(string)의 string은 기존 파서가 지원하는 텍스트 전용 BTPL이다. 임의 SendData나 RFID 명령을 통과시키는 API가 아니다. 구문 오류의 위치·Code는 기존 BtplFormatException 계약을 따르며 실제 SDK 실행 실패·불명은 결과 객체로 구분한다.

SDK 선언을 그대로 public static으로 복제하는 방식 대신 위 기본 API에서 동일한 wrapper 기능을 호출한다. 연결 소유권·서식과 인쇄의 순서·결과 판정은 공통 DLL이 관리하므로 호출 프로그램이 이를 반복 구현하지 않는다. 문서에서 원형을 확인한41개는 내부 선언 범위이며, 기존 정책대로 RFID 읽기·기록·잠금·보안 API는 외부 공개하거나 인쇄 흐름에서 호출하지 않는다.

## 선택적으로 사용하는 기능

| 필요 | 기존 API / 구성 | 기본 인쇄에 필수인가 |
| --- | --- | --- |
| 텍스트 좌표·문구·요소 개수·용지 설정 편집 | BtplTemplate, BtplTextElement, BtplSetting | BTPL을 이미 준비했다면 선택 |
| 연결 없이 검사·참고 미리보기 모델 생성 | BtplProcessor.Validate/Render, BtplPrintRequest.Fixed | UI가 필요할 때 선택 |
| DB 조회 결과의 컬럼 자동 매핑·format·nullText | BtplBindings + FromRow/FromTable | 선택. DB 조회는 호출 프로그램 책임 |
| 템플릿 하나와 여러 건의 인쇄·건별 결과 | BtplPrintRequest, 동일 Print/PrintAsync 경로 | 다건 입력에 사용. TASK-06에서 구현 |
| 수치 편집·프린터 연결·출력 시험 화면 | 별도 WinForms/DevExpress Tester | DLL 사용에 불필요 |

직접 문구 방식은 호출 프로그램이 완성한 문자열을 텍스트 요소에 전달한다. DLL은 업무 필드가 무엇인지 추측하지 않는다. 개별 DB 필드의 누락·nullText·형식화를 자동 검사하려면 선택 바인딩 경로를 사용한다. 바인딩을 사용하는 요청의 기존 NULL/빈 값 규칙을 바꾸지 않는다.

## 현재 가능한 두 줄 준비 예

다음 코드는 텍스트 요소가 두 개인 기존 서식을 사용하는 관리 코드다. DataTable·JSON·SDK 연결 없이 준비·참고 렌더링까지 가능하다.

```csharp
var template = BtplTemplate.Load("label.btpl");
template = template.WithText(0,
    template.TextElements[0].WithPayload(firstLine));
template = template.WithText(1,
    template.TextElements[1].WithPayload(secondLine));

var request = BtplPrintRequest.Fixed(template);
var preview = BtplProcessor.Render(request, 0);
```

firstLine/secondLine은 호출 프로그램이 준비하는 문구다. 차트번호·이름·생년월일이나 두 줄 개수를 DLL의 고정 속성으로 만들지 않는다. 요소 두 개는 한 라벨 프레임 안에 들어가며 SDK 인쇄 요청은 한 행 한 장으로 처리한다. 텍스트 길이에 대한 화면 넘침 검사·자르기·자동 축소·자동 줄바꿈·DPI 설정을 추가하지 않는다. 이름의 실제 표현 가능 여부는 인코딩과 글꼴의 지원 범위를 확인한다.

## 확장 가능의 구체적인 의미

1. 서식 확장: 호출자가 텍스트 요소의 개수·좌표·문구와 BTPL 설정을 바꾸어 업무별 서식을 사용한다. 용지60×20mm 또는 배출15mm를 모든 서식의 기본값으로 고정하지 않는다.
2. 연결 확장: 같은 Connect API에서 USB/TCP/IP/RS-232 옵션을 사용한다. 제조사 SetPrinterPort의 인터페이스 인수로 연결하며 미검증 transport를 실기 성공으로 표시하지 않는다.
3. 사용 방식 확장: 직접 BTPL/템플릿 입력에서 시작하고 필요할 때 기존 바인딩·다건·미리보기 API를 함께 사용한다. DB나 UI를 공통 DLL에 넣지 않는다.
4. SDK 기능 확장: 새 요구가 생기면 확인된 제조사 원형·반환 규칙을 내부 wrapper에 연결하고 필요한 공개 메서드와 검사를 추가한다. 현재부터 plugin framework, 범용 프린터 driver 또는 public 임의 명령 통로를 만들지 않는다.

## 1차 보장 범위와 현재 상태

1차는 검증한 제조사 SDK 쌍의 연결·인쇄 wrapper를 같은 프로세스에서 사용하는 구성으로 정리한다. 별도 NativeHost 배포는1차에 추가하지 않는다. native 메모리 손상·프로세스 종료를 격리하여 호출 프로그램을 보호한다는 보장은 포함하지 않는다. 해당 미검증·추가 보호 범위는 #10에서 추적한다. 이는 문서 역할 정정이며 현재 GetFunction의 인쇄 게이트를 runtime 옵션이나 문서만으로 해제하지 않는다. 실제 허용 함수·인수·응답 경로의 구현·검증을 먼저 완료한다.

기본 검사는 유지한다: BTPL 구조·허용 텍스트 명령·예약/제어 문자·인코딩·확인된 SDK/글꼴/명령 조합·연결 상태·수량·응답과 호출 순서. 정상 응답42byte는 인쇄 문구 길이 제한이 아니다. 반환0이나 포트 Open을 실제 배출 완료라고 표시하지 않고, 오류/불명 후 자동 재출력하지 않는다. 우리 DLL의 진단은 함수·단계·숫자 코드로 제한하며 제조사 SDK 자체 로그는 별도 동작이다.

| 구성 | 상태 |
| --- | --- |
| 템플릿 Parse/편집/저장과 관리 검사·참고 모델 | TASK-03/04 구현·검증 완료 |
| 내부 SDK41개 원형과 고정 USB 진단 | TASK-02 완료 범위. 제품 공개 인쇄와 구분 |
| 공개 연결 옵션·결과 타입 | b7281f6에 구현. SDK 연결/인쇄 동작 완료를 뜻하지 않음 |
| Bt003Printer와 동기 Print/Validate 편의 overload | TASK-05 구현·합성 SDK 검사 통과. 새로운 제품 실기는 미실행 |
| 일괄 수명 계약, 실제 지원 인수, Tester, phase2 적용 예 | 후속 TASK-06/07/08/09 |

현재 작업에서는 SDK·장치·인쇄·이송·DB를 실행하지 않는다. 기존 RFID API 미호출·업무 필드 비고정 정책과 원본 SDK·phase2 보존을 유지한다.
