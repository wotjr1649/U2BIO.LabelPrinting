# USB Wrapper MVP 개발 계획

현재 작업은 이슈 #15다. 기능 명세는 NFC_BT003-M01-04_Programming_Manual_v1.3.md만 사용하며 제조사 Wrapper API로 구현한다. [설계](DESIGN.md)를 따르고 이전 작업의 완료 증거는 [기존 계획](history/IMPLEMENTATION_PLAN_before_wrapper_mvp.md)에 보존한다. 기존 기준 태그와 이슈 완료 이력은 변경하지 않는다.

## 실행 순서

1. 제공 API·USB 범위·책임·배포 단위를 설계·작업 지침·사용 가이드에 반영한다.
2. 기존 실행·정리·보호를 재사용하여 파일/문자열 서식, SDK 가변 데이터, 인쇄 옵션을 연결한다.
3. 공개 메서드를 한 파일에 모으고 Native 디렉터리를 Sdk/Interop으로, 선택 기능을 Templates로 정리한다.
4. 필요한 빌드·변경 핵심·문서 검사를 수행하고 사용자 원본 작업 보존을 확인한다.

실제 SDK·USB 장치·인쇄·배출·외부 SDK 로그와 캐시는 실행하지 않는다. push/merge와 새 Tester 구현은 이번 작업 범위가 아니다. 제조사 결함 수정·장애 격리·광범위한 실기·오류 주입은 MVP 완료 조건에서 제외한다. 기존 보안·입력 검증 및 필수 검사 경로는 유지한다.

## TASK-01: 현재 기준 문서와 작업 지침

DESIGN·AGENTS·PRINTER_API를 현재 기준으로 일치시킨다. 기존 문서는 역사 자료로 보존하고 #15를 등록한다.

## TASK-02: 제조사 Wrapper 연결

SetPrinterPort·DeviceConnect·CheckPrtConnection·DeviceDisconnect·ResetForm·SetPrintFormFile·SetPrintFormString·SetPrintFormVariableData·SetPrintingOption·StartLabelPrint·SendData를 연결한다. 기존 로드/응답 보호를 재사용하며 제조사 원본을 수정하지 않는다.

## TASK-03: 선택 서식 기능 보존

기존 BtplTemplate과 TXT/JSON/XML 편집·저장을 Templates에 배치한다. namespace와 기존 공개 계약을 보존한다.

## TASK-04: 선택 바인딩 분리

기존 BtplBindings·DataTable·BtplProcessor를 보존한다. 새 기본 SDK 변수 입력에는 DataTable/JSON을 요구하지 않는다.

## TASK-05: USB 기본 인쇄 API

ConnectUsb·SetPrintingOptions·PrintFile·Print(btpl,values)를 제공하고 SDK 변수 치환을 호출한다. 파일/문자열 등록을 배타적으로 처리하며 실패 뒤 인쇄하지 않는다. 인코딩·슬롯·파일 보호를 확인한다. TCP/IP·RS-232는 새 MVP 연결/시험 대상에서 제외한다.

## TASK-06: 실행과 배출 재사용

기존 worker·비동기·취소·시간 제한·소유권·불명 결과·배출/복귀를 재사용한다. 새 engine/queue/NativeHost를 추가하지 않는다. ^E15 단독 전송을 물리 배출 성공으로 선언하지 않는다.

## TASK-07: 최소 완료 확인과 배포

Release x86/x64 빌드와 기존 필수 검사 및 새 핵심 사례를 한 실행으로 확인한다. 제조사 DLL을 로드하지 않는 검사와 실기 미실행을 구분한다. 관리 DLL+동일 비트 수의 제조사 DLL 두 파일이 배포 단위다. 실패 또는 새 수정이 있을 때만 필요한 검사를 다시 실행한다.

## TASK-08: Tester 후속 연결

기존 이슈 #8은 새 공개 API를 사용하도록 이후 반영한다. 새 UI 구현은 이번 #15의 완료 조건이 아니다.

## TASK-09: 호출 예제와 이슈 기록

기본 파일/문자열/변수 인쇄 예제와 오류 처리·배포를 문서화하고 기존 오프라인 예제를 유지한다. 기존 #12/#13/#14 작업과 수정 중인 사용자 파일을 보존한다. 기존 #10/#11 미검증 추적은 해결 완료로 바꾸지 않고 MVP 선행 조건과 분리한다.

## 이번 작업의 완료 기준

- 문서와 지침의 단일 명세·USB·Wrapper·배포 기준 일치.
- 파일/문자열/변수/인쇄 옵션 API의 공유 실행 및 오류 중단.
- 선택 기능·호출 호환성·기존 보호·사용자 작업 보존.
- 관측한 빌드/검사 결과와 실기 미실행 기록.

## #15 구현 확인

공개 API와 Wrapper 호출 연결, 선택 기능 분리를 구현했다. 제품 C# 소스는19개에서17개로 줄였으며 기존 공개 타입과 선택 기능을 보존했다. Release 솔루션의 x86/x64 빌드와 두 검증 실행 파일, 문서 검사가 통과했다. 검증은 제조사 DLL을 로드하지 않고 합성 호출로 순서·오류 중단·파일 보호를 확인했다.

Bt003NativePlatform=x86/x64의 Release 제품 빌드도 통과했다. 각 출력 폴더의 관리 DLL은 검증한 바이너리와 같고, 제조사 두 DLL은 원본과 SHA-256이 일치한다. 출력 위치는 src/U2BIO.BT003PROLabelPrinting/bin/{x86,x64}/Release다.

devflow의 별도 시험 잠금은 선행 실패 증거 조건을 충족하지 못해 등록되지 않았다. 이 상태를 검사 통과로 표시하지 않으며 실제 빌드 검사의 통과 결과와 구분한다. 실제 SDK/장치 호출, 인쇄·배출, push/merge는 실행하지 않았다. 이번 구현은 전용 작업 브랜치에 있으며 원래 작업 트리와 원격 통합은 별도다.
