# 동기 USB SDK 인쇄 MVP 스펙

이슈 #16. 기준 구현은 87e26a0이다. 기능 명세는 제조사 Programming Manual v1.3, 실행 선언은 제공된 HFSample.BT003CtrlWrapper다. 이 스펙은 #15의 호환 API·출력 bridge 유지 계약을 이번 작업에 한해 대체한다.

## 사용자 결정과 책임

- 기본 코드는 Bt003Printer.cs와 PrinterModels.cs 두 파일이다. 제조사 Wrapper와 바이너리는 변경하지 않는다.
- 새 USB API로 이행한다. 기존 비동기·취소·시간 제한·일괄·사전검사 API와 큰 결과 타입은 제거한다. 선택 Templates 기능은 보존한다.
- SDK 호출은 동기다. SDK 반환까지 기다리며 UI 백그라운드 실행은 호출 프로그램 책임이다. native 호출을 강제로 중단하거나 시간 초과를 성공으로 변환하지 않는다.
- StartLabelPrint는 제조사 원본 StringBuilder 선언을 사용한다. 자체 출력 메모리·가드 페이지·중간 버퍼 지우기 보장은 MVP에서 제외한다. 기존 보호와 동등하다고 주장하지 않는다.
- 로딩은 DllImport에 맡긴다. 기존 ApplicationDirectory/System32 검색 제한은 유지한다. 실행 시 고정 해시, 자체 로더, 모듈 충돌·시스템 모듈 검사는 제외한다. Validation이 배포 파일의 비트수와 원본 해시를 대조한다.
- 파일은 절대경로, 읽기 가능 여부와 제한된 크기·엄격한 CP949 인코딩을 확인한다. FileStream의 FileShare.Read 잠금은 실제 SDK 작업 종료까지 유지한다. 부모 디렉터리 잠금·최종 경로 검사는 제외한다.
- BTPL의 명령·좌표·서식 해석과 변수 치환은 SDK가 맡는다. BtplTemplate/BtplProcessor는 기본 인쇄 경로에서 사용하지 않는다. 입력 크기·빈 값·인코딩·변수 구분자 충돌 검사는 우리 책임이다.
- 원본 ANSI 전달과 CP949 파일 계약을 맞추기 위해 .NET Framework의 Encoding.Default.CodePage가 949인 환경만 지원한다. 포트·서식·변수·파일 경로를 SDK 호출 전에 엄격한 CP949 왕복 가능 문자열로 검사하며 NUL/구분자 충돌을 거부한다. 다른 코드 페이지를 조용히 변환해서 출력하지 않는다.
- 문자열 서식은 CRLF로 줄끝을 정규화하고 마지막 CRLF를 붙인다. 서식과 합친 변수 문자열은 각각 CP949 4 MiB 이내다. 변수 배열은 호출 시작 시 복제하며 null/빈 값/제어문자/!!/선행·후행 !를 거부한다. 기본 검사는 명령·좌표·변수 번호를 해석하는 자체 서식 파서가 아니다.
- 우리 코드는 한글 주석으로 SDK 호출 순서와 실패 중단·자원 수명의 이유를 설명한다. 공개 API는 한 파일에서 찾을 수 있게 하고 일반 실행 프레임워크나 delegate 카탈로그는 만들지 않는다.

## 공개 API

Bt003Printer는 IDisposable인 sealed 클래스다. ConnectUsb(string port, int presentationDistanceMm = 0), GetConnectionStatus(), SetPrintingOptions(ushort speed, ushort darkness), Print(string btpl, params string[] values), PrintFile(string path, params string[] values), ConfirmPrintPosition(), Disconnect()를 동기 PrinterResult로 제공한다.

ConnectUsb는 SetPrinterPort(1, port, "") → DeviceConnect → CheckPrtConnection 순서다. 속도는 양수, 농도는 1..19, 배출 거리는 0..1000 mm다. 옵션은 성공한 경우에 보존하며 각 인쇄 준비 시 재적용한다. 서식의 ^S/^H 우선순위는 SDK 계약을 따른다.

GetConnectionStatus의 1은 포트 Open, 0은 Closed다. 포트 상태와 물리 준비를 동일시하지 않는다. 같은 프로세스에서 SDK 전역 상태를 사용하는 객체는 하나의 연결 소유자만 허용하며 전체 작업을 직렬화한다. 사용자는 같은 물리 프린터를 한 프로그램에서만 사용하며 교차 프로세스 제어를 MVP에서 제외하기로 확정했다. 다른 프로그램이 동시에 같은 프린터를 사용하지 않도록 호출 환경에서 지켜야 한다.

소유권은 정적 gate와 소유자 참조로 관리한다. 소유자가 아닌 객체의 조회·옵션·해제·Dispose는 SDK를 호출하지 않는다. 모든 SDK 작업과 finally는 같은 gate 안에서 끝나므로 다른 스레드 Dispose는 실제 반환·정리까지 기다린다. 연결을 시도한 뒤에는 DeviceDisconnect 정상 응답과 CheckPrtConnection의 Closed가 확인돼야 소유권을 넘긴다. 정리 실패는 소유권을 보존하고 명시적 Disconnect 재시도를 허용한다. Dispose의 해제 실패는 식별 코드의 예외로 알리며 성공으로 숨기지 않는다.

## 인쇄 순서와 실패

1. 인수를 SDK 호출 전에 검사한다. 변수 값은 서식이 참조하는 최고 번호까지 V00부터 누락 없이 매 요청 제공하며 !!로 연결한다. 변수 등록 생략은 변수 참조가 없는 서식에서만 허용되는 호출자 계약이다. 이전 값이 ResetForm 또는 빈 문자열 등록으로 지워진다고 가정하지 않는다. 기본 DLL은 변수 번호/좌표를 해석하는 자체 서식 파서를 두지 않으며 불완전한 변수 입력을 안전하게 처리한다고 보장하지 않는다. 값이 있는 각 요청은 반드시 SetPrintFormVariableData를 다시 호출하고 성공을 확인한다.
2. 연결 상태를 확인한다. 이전 정상 배출이 있으면 ^B로 그 거리만 복귀한다.
3. ResetForm → 선택 SetPrintingOption → SetPrintFormFile 또는 SetPrintFormString → 선택 SetPrintFormVariableData를 호출한다. 등록 실패 뒤 인쇄하지 않는다.
4. new StringBuilder(100)으로 StartLabelPrint(1, response)를 호출한다. 내부 문법 검사로 숫자 코드만 추출하고 UID 원문은 결과·로그에 남기지 않는다.
5. 0은 SDK 정상 응답, 알려진 제조사 실패 코드는 실패, 101/잘못된 응답/미확인 코드는 불명이다. 인쇄 호출 이후 예외도 불명이며 자동 재인쇄하지 않는다.
6. 정상 인쇄 응답에서만 선택 ^M 배출을 수행한다. SendData 0은 명령 수락이며 물리 이동 완료 증거가 아니다. 배출 실패는 정상 인쇄 코드와 구분한다.
7. finally에서 서식을 정리하고 중첩 finally로 파일 잠금을 해제한다. 정리 오류가 원래 Function/NativeCode/PrintCode를 덮지 않게 별도 CleanupCode에 보존한다. 원결과 Unknown이면 cleanup 실패 후에도 Unknown을 유지한다. 원결과 Succeeded에 cleanup 실패가 더해지면 Status를 Failed로 집계하되 PrintInvoked/PrintCode=0을 보존하며 재인쇄 근거로 삼지 않는다. 실패했던 원결과는 그대로 보존한다. cleanup 실패 후 미정리 연결에서 다음 인쇄를 차단한다. Disconnect로 닫힘이 확인된 뒤 재연결해야 이 연결 오류를 해소할 수 있으며 위치 불명은 별도로 수동 확인한다.

인쇄·이동 결과가 불명이면 배출 거리 0에서도 위치를 불명으로 유지하고 다음 자동 인쇄를 거부한다. 위치 기록은 객체가 아니라 프로세스 안의 포트별 상태로 보존한다. ConfirmPrintPosition은 실제 위치를 수동으로 맞춘 호출자의 확인이며 SDK 센서 확인이나 자동 이동이 아니다. 연결 해제·재연결·새 객체 생성만으로 위치 불명을 지우지 않는다. 정상 SendData 반환도 물리 위치 확인이 아니라 명령 이력에 따른 복귀 거리 추적이다.

^M/^B는 호출 진입 전에 위치를 불명으로 기록한다. 0을 반환한 경우에만 명령 이력에 따른 거리(^M은 요청 거리, ^B는 0)로 갱신한다. 비0 실패와 예외는 모두 위치 불명을 유지한다. 다음 요청에서 자동 복귀·인쇄를 다시 수행하지 않고 수동 확인을 요구한다. 이 규칙은 복귀와 배출에 동일하게 적용한다.

## 작은 결과

PrinterResult는 Status(Succeeded/Failed/Unknown), Code, Function, NativeCode, PrintInvoked, PrintCode, MovementCode, CleanupCode를 제공한다. GetConnectionStatus에서 NativeCode는 원래 상태 코드다. MovementCode는 마지막으로 시도한 복귀 또는 배출의 응답이며 응답 전 예외는 null이다. 성공 이름은 SDK 수준의 처리 결과이며 물리 완료를 보장하지 않는다. 입력 오류는 인쇄 전의 인수 예외로 거부한다. DLL 누락·아키텍처·entrypoint 오류는 실패 단계와 식별 코드를 전달한다. 원문 서식·변수·UID·경로를 예외 메시지나 결과에 복사하지 않는다.

파일 크기·CP949 내용은 SDK에 넘길 경로를 열어 둔 동일 FileStream으로 읽고 확인한다. Path/FileStream/디코딩의 원래 예외 메시지와 InnerException을 넘기지 않고 고정 식별 코드로 바꾼다. finally 정리 실패에도 파일은 해제되며, 호출 중 공유 충돌이나 부모 경로 교체의 실제 SDK 동작을 관리 검사로 증명했다고 주장하지 않는다.

## 확인과 배포

관리 DLL은 .NET Framework 4.6.1/AnyCPU다. 실행 프로그램 비트수에 맞는 BlackfishNfcLib.dll과 HFModule.dll을 함께 배포한다. SDK·장치 실행, 인쇄·배출 실기는 이번 작업에서 하지 않는다.

제조사 원본 선언·배포 파일은 Validation으로 정적 확인한다. 관리 동작은 제품 두 소스를 같은 형태로 컴파일한 작은 SDK 대체 호출 harness에서 연결·호출 순서·등록 실패·응답·배출 실패·정리·잠금 수명을 확인한다. 제품 DLL의 실제 P/Invoke 성공과 혼동하지 않는다. 기존 Templates 검사와 역사 SDK 진단은 보존하되 새 API와 구분한다.

harness는 제품 두 파일을 프로젝트 Link로 컴파일하며 수정한 사본을 사용하지 않는다. fake ResetForm은 변수 저장소를 지우지 않는 사례를 포함한다. 별도로 실제 제품 DLL의 공개 계약/원본 선언/검색 제한을 확인한다. x86/x64 각각 Bt003NativePlatform 배포 빌드를 수행하고 실행 출력의 제조사 두 파일을 저장소 고정 해시·원본과 대조한다. 기본 관리 빌드만으로 배포 검사를 완료했다고 표시하지 않는다.

## 수용과 미검증

오른쪽 Codex가 코드/스펙/계획을 독립 검토하고 구현을 막는 결함을 모두 해결해야 구현을 시작한다. 이후 Release x86/x64 빌드, 영향받은 관리 검사, 원본/배포 대조, 문서 검사와 최종 독립 검증을 수행한다. 호환 호출자의 외부 이행과 실제 SDK/실물 정확성은 미검증으로 명시한다.
