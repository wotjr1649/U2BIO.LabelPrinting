# 현재 계약 안내

이슈 #16의 [동기 USB MVP 스펙](specs/SYNC_USB_MVP.md)과 [공개 API](PRINTER_API.ko.md)를 따른다. 아래 모듈 설명의 비동기·일괄·BtplTemplate 인쇄와 큰 결과 체계는 이전 버전 이력이다. 현재 기본 코드는 Bt003Printer.cs와 PrinterModels.cs 두 파일이며 선택 Templates는 독립적으로 보존한다.

# 공통 USB 라벨 인쇄 DLL

현재 기준은 [USB Wrapper MVP 설계](DESIGN.md)와 이슈 #15다. 기능 명세는 NFC_BT003-M01-04_Programming_Manual_v1.3.md만 사용한다. 제공 Wrapper API를 연결하고 제조사 파일·기능은 수정하거나 재구현하지 않는다.

기본 사용은 ConnectUsb → 선택 SetPrintingOptions → PrintFile/Print → Disconnect/Dispose다. 파일·문자열 서식과 SDK의 ^Vxx 변수 치환을 사용하며 호출자에게 DataTable·JSON·별도 요청 객체를 요구하지 않는다. DB 조회와 UI는 호출자 책임이다. [공개 API 지도](PRINTER_API.ko.md)를 따른다.

공개 API는 Bt003Printer.cs, 내부 실행은 Bt003Printer.Execution.cs다. Sdk/Interop/BT003CtrlWrapper.cs는 제조사 원본의 HFSample namespace·39개 DllImport 선언이다. SdkRuntime은 배포·경로·모듈 확인, PrintResponse는 기존 출력 보호를 맡는다. 일반 PE 파서·41개 delegate 카탈로그·Probe는 Validation/LegacySdk로 분리했다. 연결 인수는 PrinterModels, 작은 호출 묶음은 Execution에 있다. 템플릿 편집·TXT/JSON/XML·선택 바인딩은 Templates에 보존한다. 제품 C# source 파일은17개에서16개로 줄였다.

StartLabelPrint는 출력 보호 대체 근거가 없어 포인터 bridge를 유지한다. 원본 StringBuilder 선언은 보존하지만 제품 출력 전환은 미완료다. 원본 Wrapper를 채택했다는 것과 sample-only 전환 완료는 구분한다.

제조사 내부 결함 수정·native 장애 격리·광범위한 실기는 MVP 개발 선행 조건이 아니다. 우리 호출의 인수·순서·오류·정리는 책임 범위이며 기존 보호와 필수 검사를 유지한다. 이번 작업에서는 실제 SDK/장치/인쇄를 실행하지 않는다. TCP/IP·RS-232·RFID는 새 MVP 대상에서 제외한다.

호출 프로그램이 참조하는 DLL은 하나지만 배포는 U2BIO.BT003PROLabelPrinting.dll + BlackfishNfcLib.dll + HFModule.dll의 세트다. 제조사 파일 비트 수는 실행 프로세스와 맞춘다. [SDK 배포](SDK_DEPLOYMENT.ko.md)를 따른다.
