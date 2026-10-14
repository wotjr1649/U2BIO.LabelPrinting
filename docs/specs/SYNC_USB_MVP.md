# 동기 USB SDK 인쇄 MVP 스펙

이슈 #16. 기준 구현은 87e26a0이다. 기능 명세는 제조사 Programming Manual v1.3, 실행 선언은 제공된 HFSample.BT003CtrlWrapper다. 이 스펙은 #15의 호환 API·출력 bridge 유지 계약을 이번 작업에 한해 대체한다.

2026-10-14 사용자 결정으로 공개 결과를 다섯 속성으로 단순화하고 정상 판단 기준을 bool IsSuccess 하나로 통일한다. Status와 PrinterResultStatus enum은 제거한다. 실패와 결과 불명은 false로 전달하고 사람이 읽는 Message로 단계와 이유를 설명한다. 기존 연결·위치·파일 수명과 SDK 동작은 유지한다.

## 사용자 결정과 책임

- 기본 코드는 Bt003Printer.cs와 PrinterModels.cs 두 파일이다. 제조사 Wrapper와 바이너리는 변경하지 않는다.
- 새 USB API로 이행한다. 기존 비동기·취소·시간 제한·일괄·사전검사 API와 큰 결과 타입은 제거한다. 선택 Templates는 [TXT 편집 계약](TXT_TEMPLATE_EDITOR.md)으로 축소하며 기본 인쇄와 독립이다.
- SDK 호출은 동기다. SDK 반환까지 기다리며 UI 백그라운드 실행은 호출 프로그램 책임이다. native 호출을 강제로 중단하거나 시간 초과를 성공으로 변환하지 않는다.
- StartLabelPrint는 제조사 원본 StringBuilder 선언을 사용한다. 자체 출력 메모리·가드 페이지·중간 버퍼 지우기 보장은 MVP에서 제외한다. 기존 보호와 동등하다고 주장하지 않는다.
- 로딩은 DllImport에 맡긴다. 기존 ApplicationDirectory/System32 검색 제한은 유지한다. 실행 시 고정 해시, 자체 로더, 모듈 충돌·시스템 모듈 검사는 제외한다. Validation이 배포 파일의 비트수와 원본 해시를 대조한다.
- 파일은 절대경로, 읽기 가능 여부와 제한된 크기·엄격한 CP949 인코딩을 확인한다. FileStream의 FileShare.Read 잠금은 실제 SDK 작업 종료까지 유지한다. 부모 디렉터리 잠금·최종 경로 검사는 제외한다.
- BTPL의 명령·좌표·서식 해석과 변수 치환은 SDK가 맡는다. BtplTemplate/BtplProcessor는 기본 인쇄 경로에서 사용하지 않는다. 입력 크기·빈 전체 문자열·인코딩·NUL·제어문자 검사는 우리 책임이다. SDK 가변데이터 구분자 !!와 변수 번호는 자체 해석하지 않는다.
- 원본 ANSI 전달과 CP949 파일 계약을 맞추기 위해 .NET Framework의 Encoding.Default.CodePage가 949인 환경만 지원한다. 포트·서식·변수·파일 경로를 SDK 호출 전에 엄격한 CP949 왕복 가능 문자열로 검사하며 NUL을 거부하고 가변데이터의 제어문자도 SDK 진입 전에 거부한다. 다른 코드 페이지를 조용히 변환해서 출력하지 않는다.
- 문자열 서식과 파일에서 읽은 서식은 CRLF로 줄끝을 정규화하고 마지막 CRLF를 붙인다. 정규화 전후 서식과 가변데이터 원문은 각각 CP949 4 MiB 이내다. variableData=null은 변수 등록 생략이며 빈 전체 문자열·공백만인 문자열·제어문자는 거부한다. !!·단일 !·항목 공백은 원문 그대로 전달하고 split/join/trim하지 않는다. 빈 항목·부족 데이터의 SDK 의미와 변수 초기화는 보장하지 않는다. 기본 검사는 명령·좌표·변수 번호를 해석하는 자체 서식 파서가 아니다.
- 우리 코드는 한글 주석으로 SDK 호출 순서와 실패 중단·자원 수명의 이유를 설명한다. 공개 API는 한 파일에서 찾을 수 있게 하고 일반 실행 프레임워크나 delegate 카탈로그는 만들지 않는다.

## 공개 API

Bt003Printer는 IDisposable인 sealed 클래스다. ConnectUsb(string port, int presentationDistanceMm = 0), GetConnectionStatus(), SetPrintingOptions(ushort speed, ushort darkness), Print(string btpl, string variableData = null), PrintFile(string path, string variableData = null), ConfirmPrintPosition(), Disconnect()를 동기 PrinterResult로 제공한다.

ConnectUsb는 SetPrinterPort(1, port, "") → DeviceConnect → CheckPrtConnection 순서다. 속도는 양수, 농도는 1..19, 배출 거리는 0..1000 mm다. 옵션은 성공한 경우에 보존하며 각 인쇄 준비 시 재적용한다. 서식의 ^S/^H 우선순위는 SDK 계약을 따른다.

GetConnectionStatus의 1은 포트 Open, 0은 Closed다. 포트 상태와 물리 준비를 동일시하지 않는다. 같은 프로세스에서 SDK 전역 상태를 사용하는 객체는 하나의 연결 소유자만 허용하며 전체 작업을 직렬화한다. 사용자는 같은 물리 프린터를 한 프로그램에서만 사용하며 교차 프로세스 제어를 MVP에서 제외하기로 확정했다. 다른 프로그램이 동시에 같은 프린터를 사용하지 않도록 호출 환경에서 지켜야 한다.

소유권은 정적 gate와 소유자 참조로 관리한다. 소유자가 아닌 객체의 조회·옵션·해제·Dispose는 SDK를 호출하지 않는다. 모든 SDK 작업과 finally는 같은 gate 안에서 끝나므로 다른 스레드 Dispose는 실제 반환·정리까지 기다린다. 연결을 시도한 뒤에는 DeviceDisconnect 정상 응답과 CheckPrtConnection의 Closed가 확인돼야 소유권을 넘긴다. 정리 실패는 소유권을 보존하고 명시적 Disconnect 재시도를 허용한다. Dispose의 해제 실패는 식별 코드의 예외로 알리며 성공으로 숨기지 않는다.

## 인쇄 순서와 실패

1. 인수를 SDK 호출 전에 검사한다. 호출자는 서식이 참조하는 최고 번호까지 V00부터 누락 없이 매 요청 데이터를 제공한다. !!로 구분된 문자열은 검사 후 SDK에 그대로 전달하며 번호 매핑과 미사용 추가 데이터 처리는 SDK에 맡긴다. 변수 등록 생략은 변수 참조가 없는 서식에서만 허용되는 호출자 계약이다. 이전 값이 ResetForm 또는 빈 문자열 등록으로 지워진다고 가정하지 않는다. 기본 DLL은 변수 번호/좌표를 해석하는 자체 서식 파서를 두지 않으며 불완전한 변수 입력을 안전하게 처리한다고 보장하지 않는다. 값이 있는 각 요청은 반드시 SetPrintFormVariableData를 다시 호출하고 성공을 확인한다.
2. 연결 상태를 확인한다. 이전 정상 배출이 있으면 ^B로 그 거리만 복귀한다.
3. ResetForm → 선택 SetPrintingOption → SetPrintFormString → 선택 SetPrintFormVariableData를 호출한다. PrintFile도 검증한 파일 내용을 문자열로 등록한다. 등록 실패 뒤 인쇄하지 않는다. 발행 실패 뒤 다른 등록 방식으로 자동 재인쇄하지 않는다.
4. new StringBuilder(100)으로 StartLabelPrint(1, response)를 호출한다. 내부 문법 검사로 숫자 코드만 추출하고 UID 원문은 결과·로그에 남기지 않는다.
5. 0은 SDK 정상 인쇄 응답이다. 알려진 제조사 실패와 101/잘못된 응답/미확인 코드, 인쇄 호출 이후 예외는 모두 IsSuccess=false다. 알려진 실패와 불명 응답의 이유는 Message에 구분해 남기고 숫자·인쇄 호출/응답을 보존한다. 어느 경우에도 자동 재인쇄하지 않는다.
6. 정상 인쇄 응답에서만 선택 ^M 배출을 수행한다. SendData 0은 명령 수락이며 물리 이동 완료 증거가 아니다. 배출 실패는 정상 인쇄 코드와 구분한다.
7. finally에서 서식을 정리하고 중첩 finally로 파일 잠금을 해제한다. 정리 오류는 IsSuccess=false로 기록하고 원래 NativeCode/PrintCode/Message를 보존하며 Message에 설명을 추가한다. 인쇄 정상 응답 뒤 정리가 실패해도 PrintInvoked/PrintCode=0을 보존하며 재인쇄 근거로 삼지 않는다. 원래 실패·불명 응답도 false와 원래 이유를 보존한다. cleanup 실패 후 미정리 연결에서 다음 인쇄를 차단한다. Disconnect로 닫힘이 확인된 뒤 재연결해야 이 연결 오류를 해소할 수 있으며 위치 불명은 별도로 수동 확인한다.

인쇄·이동 결과가 불명이면 배출 거리 0에서도 위치를 불명으로 유지하고 다음 자동 인쇄를 거부한다. 위치 기록은 객체가 아니라 프로세스 안의 포트별 상태로 보존한다. ConfirmPrintPosition은 실제 위치를 수동으로 맞춘 호출자의 확인이며 SDK 센서 확인이나 자동 이동이 아니다. 연결 해제·재연결·새 객체 생성만으로 위치 불명을 지우지 않는다. 정상 SendData 반환도 물리 위치 확인이 아니라 명령 이력에 따른 복귀 거리 추적이다.

^M/^B는 호출 진입 전에 위치를 불명으로 기록한다. 0을 반환한 경우에만 명령 이력에 따른 거리(^M은 요청 거리, ^B는 0)로 갱신한다. 비0 실패와 예외는 모두 위치 불명을 유지한다. 다음 요청에서 자동 복귀·인쇄를 다시 수행하지 않고 수동 확인을 요구한다. 이 규칙은 복귀와 배출에 동일하게 적용한다.

## 작은 결과

PrinterResult의 공개 속성은 IsSuccess(bool), Message(string), NativeCode(int?), PrintInvoked(bool), PrintCode(int?) 다섯 개다. 정상 여부는 IsSuccess로만 판단하며 호출자는 `if (!result.IsSuccess)`로 바로 분기한다. true는 요청한 SDK 처리와 필요한 배출·정리의 정상 종료다. 실패와 결과 불명은 모두 false이며 무출력을 뜻하지 않는다. Status와 PrinterResultStatus enum, bool 반환 overload·암시적 변환·LastResult는 제공하지 않는다. Message는 주 작업 단계와 SDK 함수·이동/정리 오류를 고정 한글로 설명하며 호출자는 문장을 파싱해 자동 복구하지 않는다. NativeCode는 주 작업의 마지막 SDK 응답으로 미호출·응답 전 예외는 null이며, 후속 서식 정리는 이 값을 덮지 않는다. GetConnectionStatus의 1은 열린 연결, 0은 닫힌 연결이다. 인쇄 뒤 배출 실패/예외에도 PrintCode는 정상 인쇄 응답 0을 보존한다. 연결 실패 후 해제 실패도 원래 연결 응답을 유지하고 Message에 추가한다.

결과 생성자는 internal이고, 속성 getter는 public/setter는 internal이다. 새 객체의 IsSuccess 기본값은 false이며 정상 경로에서 true를 명시한다. DebuggerDisplay는 IsSuccess와 Message를 먼저 표시한다. SDK 수준의 정상 종료는 물리 완료를 보장하지 않는다. 입력 오류는 인쇄 전의 인수 예외로 거부하고 미지원 코드 페이지·해제된 객체와 Dispose 해제 실패 등의 기존 예외 계약도 유지한다. 모든 오류를 false로 반환하는 계약으로 확대하지 않는다. DLL 누락·아키텍처·entrypoint 오류는 SDK 함수와 고정 한글 설명으로 전달한다. 원문 서식·변수·UID·경로 또는 원래 SDK 예외 메시지를 결과에 복사하지 않는다.

파일 크기·CP949 내용은 열어 둔 동일 FileStream으로 읽고 확인한다. SDK에는 검사한 내용의 CRLF 문자열을 SetPrintFormString으로 넘겨 파일을 다시 열지 않게 한다. 파일 읽기 잠금은 SDK 발행·서식 정리까지 유지한다. Path/FileStream/디코딩의 원래 예외 메시지와 InnerException을 넘기지 않고 고정 식별 코드로 바꾼다. finally 정리 실패에도 파일은 해제된다. SDK의 SetPrintFormFile에서 관측한 103의 내부 원인을 확정하거나 모든 파일 접근 경쟁을 해결했다고 주장하지 않는다.

## 확인과 배포

관리 DLL은 .NET Framework 4.6.1/AnyCPU다. 실행 프로그램 비트수에 맞는 BlackfishNfcLib.dll과 HFModule.dll을 함께 배포한다. SDK·장치 실행, 인쇄·배출 실기는 이번 작업에서 하지 않는다.

제조사 원본 선언·배포 파일은 Validation으로 정적 확인한다. 관리 동작은 제품 두 소스를 같은 형태로 컴파일한 작은 SDK 대체 호출 harness에서 연결·호출 순서·등록 실패·응답·배출 실패·정리·잠금 수명을 확인한다. 제품 DLL의 실제 P/Invoke 성공과 혼동하지 않는다. 선택 TXT 편집 관리 검사와 역사 SDK 진단을 기본 인쇄 계약과 구분한다. 이전 JSON/XML/바인딩 검사는 새 편집 범위로 이행한다.

harness는 제품 두 파일을 프로젝트 Link로 컴파일하며 수정한 사본을 사용하지 않는다. fake ResetForm은 변수 저장소를 지우지 않는 사례를 포함한다. 별도로 실제 제품 DLL의 공개 계약/원본 선언/검색 제한을 확인한다. x86/x64 각각 Bt003NativePlatform 배포 빌드를 수행하고 실행 출력의 제조사 두 파일을 저장소 고정 해시·원본과 대조한다. 기본 관리 빌드만으로 배포 검사를 완료했다고 표시하지 않는다.

## 수용과 미검증

오른쪽 Codex가 코드/스펙/계획을 독립 검토하고 구현을 막는 결함을 모두 해결해야 구현을 시작한다. 이후 Release x86/x64 빌드, 영향받은 관리 검사, 원본/배포 대조, 문서 검사와 최종 독립 검증을 수행한다. 호환 호출자의 외부 이행과 실제 SDK/실물 정확성은 미검증으로 명시한다.
