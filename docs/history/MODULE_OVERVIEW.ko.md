# 공통 USB 라벨 인쇄 DLL

기본 인쇄 계약은 [동기 USB MVP](../specs/SYNC_USB_MVP.md), 선택 편집 계약은 [TXT 편집 API](../specs/TXT_TEMPLATE_EDITOR.md)다. 이슈 #16의 TXT 재설계 문서이며 구현/검증 상태는 이슈와 실행 근거를 확인한다.

| 파일 | 책임 |
| --- | --- |
| Bt003Printer.cs | 동기 공개 인쇄 API, 기본 입력 검사, 직접 SDK 호출, 실패 중단·배출/복귀·정리 |
| PrinterModels.cs | IsSuccess/Message/NativeCode/PrintInvoked/PrintCode의 작은 결과 |
| Sdk/Interop/BT003CtrlWrapper.cs | 제조사 원본 39개 선언과 기존 주석 |
| Templates/BtplTemplate.cs | TXT 원문·행 보존, 읽기/편집, frame/설정 대상, Save/SaveAs |
| Templates/BtplModels.cs | 작은 AW/설정 모델과 편집·저장 오류 |

Templates의 JSON/XML·DataTable·렌더링 결과는 제거하고 네 소스를 두 소스로 축소한다. 편집 API는 SDK 인쇄 경로에 의존하지 않는다. Bt003Printer는 Templates를 사용하지 않고 SDK에 서식 해석·가변데이터 치환을 맡긴다. 세 고정 파일은 이번 편집 재설계에서 변경하지 않는다.

사용 프로젝트는 파일 선택·덮어쓰기 확인·편집 화면·참고 미리보기·DB·업무 변수 구성·백그라운드 실행·결과 표시를 담당한다. 미리보기 표시값을 원본 서식에 반영하지 않는다. 저장 성공한 반환 객체만 새 현재 파일로 채택한다.

인쇄 사용은 ConnectUsb → 선택 SetPrintingOptions → Print/PrintFile → Disconnect/Dispose다. 인쇄 후 배출과 다음 인쇄 전 복귀는 DLL 내부에서 처리한다. 호출자는 이동 명령을 중복 전송하지 않는다. 실패/불명 응답은 IsSuccess=false이며 자동 재인쇄하지 않는다. SDK 정상 응답도 물리 완료를 증명하지 않는다.

[사용 예제](../BTPL_TEMPLATE_API.ko.md), [프린터 API](../PRINTER_API.ko.md), [배포 계약](../SDK_DEPLOYMENT.ko.md)을 따른다. 관리 DLL 참조와 프로세스 비트수에 맞는 제조사 두 DLL이 필요하며 UI/DB provider를 제품에 추가하지 않는다.
