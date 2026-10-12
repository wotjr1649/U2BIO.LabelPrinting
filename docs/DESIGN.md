# 제조사 Wrapper 기반 USB 인쇄 MVP 설계

현재 기준: 이슈 #15. 기능 명세는 **NFC_BT003-M01-04_Programming_Manual_v1.3.md**만 사용한다. 제공 C# Wrapper의 API 원형을 연결 코드로 사용하고 제조사 SDK 기능을 재구현하지 않는다. 구현 전 설계·인수 기록은 [기존 설계](history/DESIGN_before_wrapper_mvp.md)에 보존한다. 기준 태그 v0.1.1-design은 변경하지 않는다.

## 1. 목적과 범위

공통 DLL은 .NET Framework 4.6.1에서 USB 연결 → 텍스트 라벨 인쇄 → 해제를 제공한다. 파일·문자열 서식과 SDK 가변 데이터 치환, 속도·농도 및 선택 배출을 기본 기능으로 둔다. DB·UI는 호출 프로그램의 책임이다. TCP/IP·RS-232·RFID 기록·잠금·보안 API는 이번 MVP에서 사용하거나 시험하지 않는다. 기존 연결 factory와 편집·바인딩 API는 호출 호환성을 위해 보존하지만 USB 기본 사용에 요구하지 않는다.

제조사 내부 결함 수정, 장애 격리 프로세스, SDK 재작성, 광범위한 장치·오류 주입 시험은 MVP 범위 밖이다. 우리 DLL의 인수 검증·호출 순서·실패 중단·연결 정리와 반환 코드 전달은 구현 책임이다. 기존 로드·입력·응답 검증과 필수 검사 경로는 유지한다. SDK 정상 응답을 물리 출력·배출 완료로 확대하지 않는다.

## 2. 코드 지도

| 파일·영역 | 책임 |
| --- | --- |
| Bt003Printer.cs | 모든 공개 프린터 메서드와 기존 overload |
| Bt003Printer.Execution.cs | 기존 worker·연결 소유권·인쇄·배출·정리 |
| PrinterModels.cs | 옵션·상태·결과 타입 |
| PrintInput.cs | SDK 파일/문자열 입력, 변수 슬롯, 파일 읽기 보호 |
| Sdk/Interop/BT003CtrlWrapper.cs | 제조사 API 단일 진입점과 기존 내부 선언·검사 주입점 |
| Sdk/Interop의 지원 파일 | 기존 SDK 로드·DLL 형식·응답 검증 재사용 |
| Templates/ | 기존 편집·TXT/JSON/XML·DataTable 바인딩의 선택 기능 |

기존 Native 소스 디렉터리는 없애고 지원 코드를 Sdk/Interop으로 배치한다. Interop의 기존 보호 분류를 유지한다. 내부 Native namespace와 선언 카탈로그는 기존 진단 호환성을 위해 유지하며 공개 API가 아니다. 제조사 샘플의 단순 DllImport 재검색 및 StringBuilder 출력 버퍼로 기존 검증을 대체하지 않는다. Wrapper의 기능 구현은 제조사 DLL이고, 우리 연결부는 확인한 module handle과 기존 출력 포인터를 사용한다. 제조사 파일은 수정하지 않는다.

## 3. 공개 API와 제조사 API

| 공개 API | 제조사 호출 |
| --- | --- |
| ConnectUsb(port, presentationDistanceMm=0) | SetPrinterPort(1,port,빈 문자열) → DeviceConnect → CheckPrtConnection |
| GetConnectionStatus | CheckPrtConnection 또는 작업 중 cached 상태 |
| SetPrintingOptions(speed,darkness) | SetPrintingOption; 성공한 값을 현재 객체에 보존 |
| PrintFile(path, values) | ResetForm → SetPrintFormFile → 선택 SetPrintFormVariableData → StartLabelPrint(1) |
| Print(btpl, values), Print(string) | ResetForm → SetPrintFormString → 선택 SetPrintFormVariableData → StartLabelPrint(1) |
| PrintAsync, PrintFileAsync | 같은 SDK worker와 실행 경로 |
| Disconnect, Dispose | 소유한 연결의 DeviceDisconnect·닫힘 확인 |
| ConfirmPrintPosition | 사용자가 수동 기준 위치를 확인한 뒤 위치 기록 정리 |

ResetForm은 void다. 준비 함수의 비0 결과 또는 입력 오류 뒤에는 인쇄하지 않는다. CheckPrtConnection의 1은 포트 연결이며 물리 준비 보증이 아니다. CheckRFConnection은 USB 텍스트 인쇄의 선행 조건으로 사용하지 않는다.

## 4. 서식·가변 데이터·인쇄 옵션

파일과 문자열 중 하나만 등록한다. 파일이 문자열보다 우선하므로 매 작업에서 ResetForm 후 새 서식을 지정한다. 문자열 명령은 CRLF로 정규화하고 원래 설정과 ^Vxx를 SDK에 전달한다. 파일은 CP949 TXT/BTPL이며 절대경로를 사용한다. 검사한 파일과 부모 경로는 실제 SDK worker 반환까지 읽기 보호한다. 신규 파일 입력의 읽기 상한은4 MiB다. SDK 파일 공유·인코딩의 실물 동작은 이번 작업에서 실행하지 않는다.

values[0]은 ^V00, values[1]은 ^V01이다. 최고 참조 번호+1개의 전체 슬롯을 매번 지정하며 SDK가 !! 구분자로 치환한다. 미사용 중간 슬롯에도 호출자가 자리 값을 전달한다. 매뉴얼에 없는 빈 슬롯 동작은 추측하지 않고 빈 값·null·구분자 충돌·명령/제어문자·인코딩 불가 입력을 거부한다. 문자열 배열을 snapshot하여 호출 후 변경의 영향을 막는다. 자체 바인딩 렌더러는 기존 선택 API에서만 사용한다.

SetPrintingOption의 농도는1~19, 속도는 양의 ushort이며 실제 모델 범위는 SDK가 판정한다. 명시 설정이 성공한 뒤 각 인쇄에서 다시 적용한다. 서식의 ^S/^H가 각각 우선하며 DLL은 이 명령을 삭제하지 않는다. ^S 또는 ^H를 생략한 SDK 입력은 성공한 SetPrintingOptions가 있어야 한다. 기존 선택 BtplTemplate의 요구 설정 계약은 변경하지 않는다.

기본 입력 검사에서 기존 텍스트 명령·수량·인코딩·글꼴 프로필을 보존한다. 현재 인쇄 프로필은 USB·ANSI949·AW size10/weight1/나머지0·0이며 다른 조합을 지원 완료로 표시하지 않는다. 이 프로필 확장이나 SDK 내부 결함 해결은 이번 MVP 목표가 아니다.

## 5. 배출과 결과

이전 전진이 있으면 검사 성공 뒤 B 명령으로 복귀하고, 전체 정상 응답 뒤 설정 거리만큼 M 명령을 한 번 보낸다. SendData의 임의 공개 API는 제공하지 않는다. 배출 거리는 호출자가 지정하며15mm를 공통 기본값으로 고정하지 않는다. ^E는 정지 위치 설정이고 ^M은 명시 전진이므로 ^E15를 단독 SendData로 보내는 효과를 배출 성공으로 가정하지 않는다. 원문 전체 SendData 인쇄로 전환하지 않는다.

기존 Succeeded/Failed/Unknown/NotAttempted, 인쇄 진입 여부, raw 숫자 코드, cleanup/pending, 배출·복귀 상태와 불명 위치 차단을 보존한다. 자동 재출력·대기열을 추가하지 않는다. BTPL·실제 문구·UID를 우리 진단에 기록하지 않는다.

## 6. 배포·MVP 확인

참조 DLL은 U2BIO.BT003PROLabelPrinting 하나이며 실행 폴더에는 BlackfishNfcLib.dll과 HFModule.dll을 함께 둔다. 실행 프로세스와 제조사 DLL의 x86/x64를 맞춘다. Bt003NativePlatform을 지정한 빌드가 해당 두 파일을 복사한다. 제조사 DLL을 내장·추출하는 단일 파일 배포 코드는 만들지 않는다.

변경한 공개 흐름과 필수 검사를 한 번 수행한다. 대표 검사는 USB 인수·파일/문자열 전환·변수 원문 전달·옵션 적용·등록 실패 중단·파일 보호·기존 호출 호환성이다. 반복적인 전체 재시험·추가 오류 주입은 하지 않는다. 실제 SDK·장치·인쇄·배출은 이번 작업에서 실행하지 않는다.

## 7. 기존 인수 식별자 보존

다음 C01-C24는 기존 구현·검사와의 대응을 보존하는 식별자다. 과거 장치·UI·심층 검증을 새 MVP 완료의 선행 조건으로 추가하지 않는다. 기존 정책과 기록의 상세는 역사 설계를 참조한다. 이번 완료 기준은 이슈 #15와 현재 계획이다.

| ID | 기존 인수 사례 | 보존된 판정 기준 |
| --- | --- | --- |
| C01 | 해석 가능한 AW, 배포 출력 조합 미검증 | offline Render는 참고 preview, candidate BTPL 반환 가능. ValidateForPrint는 DeviceSupportUnverified, NNN·인쇄 진입 0회 |
| C02 | 두 번째 값 빈 값, nullText 없음 | 관리 Rows valid=[true,false,true], NNN, batch ValidationFailed, DeviceValidation=null |
| C03 | token 사전 취소 | NNN/Cancelled, batch·row 검사=null, 인쇄 진입 0회 |
| C04 | 다른 작업/pending으로 Busy | NNN/Busy, 미검사 null, 새 요청 NativeCallPending=false·자기 SDK 호출 0회 |
| C05 | 관리/장치 검사 통과 뒤 첫 행 포트 Closed | FNN, PrintInvoked=[false,false,false], StopReason=NotConnected |
| C06 | 첫 행 정상, 둘째 서식 등록 실패 | SFN, PrintInvoked=[true,false,false], SdkFailure/FormRegistrationFailed·raw 코드 유지 |
| C07 | 첫 서식 등록이 managed 제한 초과 후 늦게 정상 반환 | FNN/Timeout, PrintInvoked 전부 false. late StartLabelPrint 0회, 소유 해제 정리, 결과 불변 |
| C08 | 둘째 인쇄 호출 managed 제한 초과 | SUN/Timeout, PrintInvoked=[true,true,false], 반환 시 pending=true. 늦은 정상 응답으로 U를 S로 변경 안 함 |
| C09 | 첫 인쇄 중 취소 후 정상 응답 | SNN/Cancelled, 첫 행 완료 증거 유지 |
| C10 | 첫 인쇄 중 취소 후 알려진 실패 응답 | FNN/SdkFailure. 실패가 취소보다 우선 |
| C11 | 첫 인쇄 중 Dispose 후 정상 응답 | SNN/Disposed. 논리 상태 Disposed, 직렬 해제 뒤 ResourcesReleased=true 또는 정리 실패 명시 |
| C12 | 세 행 완료 후 취소 | SSS/None, 집계 3. 뒤늦은 취소로 결과 변경 안 함 |
| C13 | 세 행 성공 뒤 정리/해제 실패 | SSS/None 유지, CleanupCode 실패·Faulted(폐기했으면 Disposed), 미정리 소유 자원 유지 |
| C14 | DeviceConnect 실패 뒤 부분 연결 해제·Closed 확인 | 연결 실패 결과, Disconnected, ResourcesReleased=true |
| C15 | DeviceDisconnect 뒤에도 Open | Faulted/DisconnectUnconfirmed, ResourcesReleased=false, native 동시 재사용 금지 |
| C16 | pending 상태에서 상태 조회, 이후 폐기 후 조회 | SDK 호출 없이 cached pending/정리 상태. 다른 public 사용은 ObjectDisposedException |
| C17 | 첫 literal 2줄 요소와 다음 가변 요소 저장 | 첫 노드는 BTPL 2개로 저장, 다음 인덱스 1→2, 옵션 재매핑, ^Vxx 보존. 재로드 fixed 줄 좌표 동일 |
| C18 | RemoveText(0), 끝 복사, WithText | 뒤쪽 옵션 1 감소, 삭제 옵션 폐기, 복사 벡터 명시 복사, 교체 인덱스 유지 |
| C19 | 재시작/다른 서식 로드 뒤 가변 값에 LF | 세션 옵션 없음 표시, MissingLineAdvance, 전체 NNN. 이전 서식 옵션 자동 적용 없음 |
| C20 | native 누락/아키텍처·해시·기존 모듈 불일치 | 구분된 Code, 제조사 장치 호출 0회. 관리 편집·preview 유지 |
| C21 | 제한된 로더 flags 사용 불가/필요 export 없음 | UnsupportedLoaderPlatform/NativeEntryPointMissing. 검색 보호를 낮추는 재시도 없음 |
| C22 | ^H 중복/설정이 ^L 뒤/E 뒤 명령 | DuplicateSetting/CommandPosition/프레임 오류, 인쇄 0회. 같은 값의 중복도 오류 |
| C23 | 필수 Q/W/S/H만 있고 선택 설정 없음 | P1,C1,R0,E0를 ^L 직전에 한 번씩 생성. 미지정 D/O는 생성하지 않음. 명시값 보존. 1행=1장 실기는 별도 |
| C24 | 인코딩 오류·원본 해시 변경·교체 I/O 실패·JSON만 저장 실패·연속 저장 | 저장 오류/미저장 표시. 원본 직접 truncate/삭제 우회 없음. UI 출력 차단, 파일 쌍의 원자 저장 주장 없음. 성공한 Save의 반환 객체로 다시 저장하면 자기 변경을 충돌로 오판하지 않음 |
