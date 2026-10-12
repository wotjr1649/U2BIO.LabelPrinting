# 공통 USB 라벨 인쇄 DLL

현재 기준은 이슈 #16의 [동기 USB MVP 스펙](specs/SYNC_USB_MVP.md), [설계](DESIGN.md)와 [공개 API](PRINTER_API.ko.md)다. 기능 명세는 제조사 Programming Manual v1.3, 호출 선언은 원본 HFSample Wrapper다.

Bt003Printer.cs는 동기 공개 API와 입력 검사·직접 SDK 호출·실패 중단·정리를 제공한다. PrinterModels.cs는 작은 PrinterResult와 상태를 담는다. Sdk/Interop/BT003CtrlWrapper.cs는 제조사 원본 39개 선언이며 StartLabelPrint도 원본 StringBuilder를 사용한다. 자체 로더·실행 시 SDK 해시·출력 포인터 보호와 worker는 제품에서 제거했다. PE·고정 해시·과거 진단은 Validation/LegacySdk 책임이다.

기본 사용은 ConnectUsb → 선택 SetPrintingOptions → PrintFile/Print → Disconnect/Dispose다. SDK가 BTPL과 변수를 해석한다. 한 프로그램에서만 같은 물리 프린터를 사용하며 DLL은 프로세스 안의 단일 소유자와 전체 호출 직렬화를 관리한다. UI 실행과 DB 조회는 호출자 책임이다.

Templates는 TXT/JSON/XML 편집·검사·선택 바인딩 기능이다. 완성 문자열은 ToBtpl 또는 BtplProcessor.Render의 BtplText로 얻어 Print(string)에 전달한다. 기본 DLL은 Templates 해석이나 전체 요청 사전검사를 자동 수행하지 않는다. 여러 행을 처리한다면 호출 프로그램이 렌더링·오류 확인·호출 순서를 결정하며 불명 결과를 자동 재인쇄하지 않는다.

배포는 U2BIO.BT003PROLabelPrinting.dll과 실제 프로세스 비트수에 맞는 BlackfishNfcLib.dll/HFModule.dll이다. [SDK 배포](SDK_DEPLOYMENT.ko.md)를 따른다. 실제 SDK·장치·인쇄·배출은 이번 관리 검사에서 실행하지 않았다. SDK 정상 응답은 물리 완료 증거가 아니다.
