# U2BIO.LabelPrinting

BT-003 Pro의 기존 RFID 라벨에 텍스트를 인쇄하는 .NET Framework 4.6.1 공통 DLL입니다. 현재 개발은 [이슈 #15](https://github.com/wotjr1649/U2BIO.LabelPrinting/issues/15)의 **제조사 Wrapper 기반 USB 인쇄 MVP**입니다.

기능 명세는 `NFC_BT003-M01-04_Programming_Manual_v1.3.md`만 사용합니다. 제조사 Wrapper API를 연결하고 원본 SDK 기능을 수정하거나 재구현하지 않습니다. 기본 사용은 `ConnectUsb → SetPrintingOptions → PrintFile/Print → Disconnect`입니다. 파일·문자열 서식과 SDK의 `^Vxx` 가변 데이터 치환을 사용합니다. TCP/IP·RS-232·RFID 기록·DB·새 UI는 이번 MVP에서 제외합니다.

[공개 API 지도와 예제](docs/PRINTER_API.ko.md), [현재 설계](docs/DESIGN.md), [개발 계획](docs/IMPLEMENTATION_PLAN.md)을 참고합니다. 공개 프린터 메서드는 `Bt003Printer.cs`, 실행·정리는 `Bt003Printer.Execution.cs`에 있습니다. `Sdk/Interop/BT003CtrlWrapper.cs`는 제조사 원본39개 선언을 namespace까지 채택하고 인코딩·줄끝만 저장소 형식으로 정규화했습니다. 연결·서식·변수·옵션·SendData는 이 선언을 직접 사용합니다. 기존 편집·TXT/JSON/XML·바인딩 기능은 `Templates/`에 보존합니다.

제품의 일반 PE 파서·41개 delegate 카탈로그·과거 Probe는 제거하고 진단은 Validation/LegacySdk로 분리했습니다. SdkRuntime은 고정 파일·경로·모듈 확인을 맡습니다. **StartLabelPrint는 출력 보호를 대체할 근거가 없어 기존 포인터 bridge를 유지합니다.** 원본 StringBuilder 선언은 보존하지만 제품 출력 전환은 미완료입니다. sample-only 전환 완료나 실기 성공으로 표시하지 않습니다.

호출 프로젝트는 `U2BIO.BT003PROLabelPrinting.dll` 하나를 참조합니다. 실행 배포에는 프로세스 x86/x64와 맞는 `BlackfishNfcLib.dll`과 `HFModule.dll`도 필요합니다. [SDK 빌드·배포](docs/SDK_DEPLOYMENT.ko.md)를 따릅니다.

```text
node tools/verify-repository.js
node tools/verify-build.js
git diff --check
```

설치된 devflow 위치는 프로세스별 DEVFLOW_ROOT로 지정합니다. 이번 변경은 필요한 빌드·핵심 호출 검사에 집중하며 실제 제조사 SDK·장치·인쇄·배출은 실행하지 않습니다. 기존 필수 검사와 기본 보호는 유지하고, SDK 정상 응답을 물리 출력 완료로 표시하지 않습니다. 제조사 내부 결함 수정·장애 격리·광범위한 실기는 MVP 개발 선행 조건이 아닙니다.

선택 기능은 [서식 API](docs/TEMPLATE_FORMATS.ko.md), [바인딩 API](docs/BTPL_BINDING_API.ko.md)를 참고합니다. [오프라인 예제](examples/OfflineSmoke/Program.cs)는 프린터·DB·UI 없이 기존 및 기본 공개 API를 확인합니다. 실제 SDK/장치 증거와 과거 완료 이력은 보존하며 [이슈 목록](docs/GITHUB_ISSUES.md)에서 구분합니다. 기준 태그 `v0.1.1-design`은 이전 기록으로 유지하고 변경하지 않습니다.
