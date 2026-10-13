# 공통 USB 라벨 인쇄 DLL

현재 기준은 이슈 #16의 [동기 USB MVP 스펙](specs/SYNC_USB_MVP.md), [설계](DESIGN.md)와 [공개 API](PRINTER_API.ko.md)다. 기능 명세는 제조사 Programming Manual v1.3, 호출 선언은 원본 HFSample Wrapper다.

Bt003Printer.cs는 동기 공개 API와 입력 검사·직접 SDK 호출·실패 중단·정리를 제공한다. PrinterModels.cs는 작은 PrinterResult와 상태를 담는다. Sdk/Interop/BT003CtrlWrapper.cs는 제조사 원본 39개 선언이며 StartLabelPrint도 원본 StringBuilder를 사용한다. 자체 로더·실행 시 SDK 해시·출력 포인터 보호와 worker는 제품에서 제거했다. PE·고정 해시·과거 진단은 Validation/LegacySdk 책임이다.

기본 사용은 ConnectUsb → 선택 SetPrintingOptions → PrintFile/Print → Disconnect/Dispose다. SDK가 BTPL과 변수를 해석한다. 한 프로그램에서만 같은 물리 프린터를 사용하며 DLL은 프로세스 안의 단일 소유자와 전체 호출 직렬화를 관리한다. UI 실행과 DB 조회는 호출자 책임이다.

결과는 IsSuccess(bool), Message, NativeCode, PrintInvoked, PrintCode 다섯 속성이다. 호출자는 `if (!result.IsSuccess)`로 분기하고 Message의 한글 설명으로 SDK 단계·배출·정리 오류를 읽는다. 실패와 결과 불명은 모두 false이며 자동 재인쇄하지 않는다. Status와 결과 enum은 제공하지 않는다. PrintCode는 후속 오류가 있어도 인쇄 응답을 보존한다. 결과 객체는 DLL 내부에서만 생성한다.

Templates는 TXT/JSON/XML 편집·검사·선택 바인딩 기능이다. 완성 문자열은 ToBtpl 또는 BtplProcessor.Render의 BtplText로 얻어 Print(string)에 전달한다. 기본 DLL은 Templates 해석이나 전체 요청 사전검사를 자동 수행하지 않는다. 여러 행을 처리한다면 호출 프로그램이 렌더링·오류 확인·호출 순서를 결정하며 불명 결과를 자동 재인쇄하지 않는다.

Templates는 기존 공개 API와 기능을 유지하면서 8개 파일을 다음 4개로 통합했다. 편집 객체와 요청은 읽기 전용 스냅샷이며 새 실행 계층이나 SDK 호출 경로를 추가하지 않았다.

| 파일 | 수정할 때 확인할 책임 |
| --- | --- |
| BtplTemplate.cs | BTPL 읽기·편집·직렬화, 지원 명령과 인자 검사 |
| BtplTemplate.Formats.cs | JSON/XML 변환, 공통 JSON 구문 검사, 파일 읽기·저장·원본 변경 감지 |
| BtplModels.cs | 설정·텍스트·바인딩·렌더링 옵션·요청 데이터 스냅샷 |
| BtplProcessor.cs | 데이터 치환, 행별 검사, 완성 문자열·부분 미리보기 결과 |

`ToBtpl`은 직접 설정·텍스트를 검사하여 이미 만든 문자열의 중복 재파싱을 생략한다. JSON/XML도 같은 검사를 통과한다. 바인딩 JSON의 끝 쉼표는 서식 JSON과 같은 규칙으로 거부한다. 저장 후 반환할 정규화 모델을 만드는 파싱과 저장 충돌 검사는 유지한다.

Templates의 파일 읽기 잠금은 내용을 복사하는 동안만 유지한다. 편집은 메모리에서 수행하고 저장 시 원본 변경을 검사한다. `Bt003Printer.PrintFile`의 잠금은 인쇄용으로, SDK 호출과 `PrintCore`의 정리가 끝난 뒤 해제한다. 두 잠금의 목적과 수명은 서로 다르다.

배포는 U2BIO.BT003PROLabelPrinting.dll과 실제 프로세스 비트수에 맞는 BlackfishNfcLib.dll/HFModule.dll이다. [SDK 배포](SDK_DEPLOYMENT.ko.md)를 따른다. 실제 SDK·장치·인쇄·배출은 이번 관리 검사에서 실행하지 않았다. SDK 정상 응답은 물리 완료 증거가 아니다.
