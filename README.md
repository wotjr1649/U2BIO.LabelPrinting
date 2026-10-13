# U2BIO.LabelPrinting

BT-003 Pro의 텍스트 라벨 인쇄용 .NET Framework 4.6.1 DLL입니다. 현재 작업은 [이슈 #16](https://github.com/wotjr1649/U2BIO.LabelPrinting/issues/16)의 동기 USB SDK MVP이며 기존 고급 공개 API와 호환되지 않습니다.

기본 사용은 ConnectUsb → SetPrintingOptions → Print/PrintFile → Disconnect입니다. 제품 코드는 Bt003Printer.cs와 PrinterModels.cs 두 파일이며 제조사 원본 BT003CtrlWrapper의 39개 선언을 직접 사용합니다. StartLabelPrint도 샘플의 StringBuilder 방식입니다. 자체 로더·실행 시 해시 검사·포인터 bridge·worker·취소·일괄 처리 체계는 제거했습니다.

[확정 스펙](docs/specs/SYNC_USB_MVP.md), [공개 API와 이행 예제](docs/PRINTER_API.ko.md), [설계](docs/DESIGN.md), [계획](docs/IMPLEMENTATION_PLAN.md)을 참고합니다. 기능 명세는 제조사 Programming Manual v1.3이며 선언·마샬링은 원본 Wrapper를 따릅니다. BTPL 해석과 변수 치환은 SDK가 수행합니다. TXT/JSON/XML 편집·바인딩은 Templates의 선택 기능으로 보존합니다.

한 프로그램에서만 같은 프린터를 사용하며, DLL은 프로세스 안의 단일 연결과 전체 호출 직렬화를 관리합니다. API는 SDK 반환까지 기다립니다. UI의 백그라운드 실행은 호출 프로그램 책임입니다. ANSI 코드 페이지 949 환경에서만 지원합니다. 파일은 CP949이며 경로·크기·읽기와 FileStream 읽기 잠금을 확인합니다.

PrinterResult는 Status, Message, NativeCode, PrintInvoked, PrintCode 다섯 속성입니다. 정상 여부는 Status 하나로 판단하고 한글 Message로 실패 이유를 읽습니다. 인쇄 응답은 PrintCode에 보존되며 배출·정리 오류는 Message에서 확인합니다. SDK 정상 응답이나 SendData 0은 물리 완료 증거가 아닙니다. 불명 결과를 자동 재인쇄하지 않습니다. 위치 불명은 재연결로 지워지지 않으며 수동 정렬 후 ConfirmPrintPosition을 호출해야 합니다.

관리 DLL 외에 실행 프로세스 비트수와 맞는 BlackfishNfcLib.dll/HFModule.dll을 함께 배포합니다. [SDK 배포](docs/SDK_DEPLOYMENT.ko.md)의 Bt003NativePlatform 빌드를 사용합니다. 제품 실행 시 해시 검사 없이 .NET 기본 DllImport 로딩을 사용하고 검색 범위는 ApplicationDirectory/System32로 제한합니다.

```text
node tools/verify-repository.js
node tools/verify-build.js
git diff --check
```

검사기는 Release x86/x64에서 실제 제품 DLL의 공개 계약·제조사 선언, 선택 Templates, 제품 두 소스를 링크한 관리 SDK 대체 harness, 오프라인 호출 예제, 실제 배포 속성의 파일 비트수·고정 해시를 확인합니다. DEVFLOW_ROOT는 설치된 플러그인 경로를 프로세스 환경으로 지정합니다. 제조사 SDK·장치·인쇄·배출은 이 검사에서 실행하지 않습니다.

[서식 API](docs/TEMPLATE_FORMATS.ko.md), [바인딩 API](docs/BTPL_BINDING_API.ko.md), [오프라인 예제](examples/OfflineSmoke/Program.cs)와 과거 docs/history 기록을 구분합니다. 실제 P/Invoke와 출력 정확성은 별도 실기 확인이 필요합니다.
