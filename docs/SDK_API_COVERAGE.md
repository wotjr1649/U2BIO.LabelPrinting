현재 #16은 [동기 USB MVP 스펙](specs/SYNC_USB_MVP.md)을 따른다. 제조사 원본 39개 Wrapper 선언을 직접 사용하며 StartLabelPrint도 StringBuilder 방식이다. 아래 41개 카탈로그·과거 포인터 보호 설명은 Validation/LegacySdk의 역사 진단 기록이며 현재 제품 실행 구조가 아니다.

# SDK 장치 API 대조표

검사일: 2026-10-07. 헤더·C# 샘플·PE export를 정적으로 대조했다. DLL을 로드하거나 장치 함수를 실행한 결과가 아니다. 후속 TASK-02에서 41개 내부 선언과 배포 검사·로더 코드를 추가했으며, 실행 가능 여부와 미확정 계약은 [NATIVE_SDK_IMPLEMENTATION.md](NATIVE_SDK_IMPLEMENTATION.md)에 기록한다.

## 1. 기준과 커버리지

다음 41개 대조표는 이전 진단과 #15 Wrapper 전환 당시의 역사 기록이다. 표의 #15 표기는 당시 추적 번호이며 현재 #16 제품의 실행 보장이나 완료 판정이 아니다. 현재 사용법은 [동기 USB 스펙](specs/SYNC_USB_MVP.md)을 따른다.

- 기준 파일: SDK `라이브러리/x86/lib`, `라이브러리/x64/lib`의 `BlackfishNfcLib.dll` + `HFModule.dll`.
- 헤더 40개, 샘플 선언 39개, 합집합 41개. 두 아키텍처의 장치 export는 정규화 후 각각 42개다. COM 인프라 `Dll*` export는 제외했다.
- 합집합 41개는 내부 선언 대상이다. `StartPrintWithRfid2` 1개는 원형이 없는 미공개 export이므로 선언·호출 보류 목록에 둔다. 이를 임의 래핑하지 않으며 “전체 구현 완료”로 주장하지 않는다.
- public P/Invoke, public SendData, RFID 읽기·기록·잠금·보안 API는 제공하지 않는다. 내부 존재와 인쇄 경로 호출 허용은 구분한다.
- H=두 아키텍처 제공 헤더, S=HFSample_UL 샘플 선언. 아래 모든 함수는 두 기준 DLL에서 존재한다.

| 함수 | 제공 원형 / 샘플 원형 | H / S | 인쇄 흐름 | 반환 계약 / 확인 상태 |
| --- | --- | --- | --- | --- |
| `GetUidValue` | `void GetUidValue(LPTSTR lptUid)` | O / O | RFID 내부 전용; 출력에서 호출 금지 | void 출력 버퍼; 새 스캔/이전 UID 구분 미정의 |
| `SetRfidProtocol` | `int SetRfidProtocol(USHORT iProtocol)` | O / O | RFID 내부 전용; 출력에서 호출 금지 | 헤더/선언의 int 원형 확인. Rev.1.3 상세 반환 규약 미정의; 임의 정상 판정 금지 |
| `SetRfidPort` | `int SetRfidPort(USHORT ifType, LPCTSTR strPortName, LPCTSTR strIpAddr)` | O / O | RFID 내부 전용; 출력에서 호출 금지 | 0=정상, -1=미지원 인터페이스 |
| `SetPrinterPort` | `int SetPrinterPort(USHORT ifType, LPCTSTR strPortName, LPCTSTR strIpAddr)` | O / O | 연결·인쇄 후보 | 0=정상 설정, 1=이미 연결. 1을 용지 없음 오류로 변환하지 않음 |
| `SetPrintFormFile` | `int SetPrintFormFile(LPCTSTR strFormFile)` | O / O | #15 PrintFile에서 호출 | 0=정상; 절대경로, 문자열보다 우선 |
| `SetPrintFormString` | `int SetPrintFormString(LPCTSTR strFormData)` | O / O | 연결·인쇄 후보 | 0=정상; CRLF 필수 |
| `SetPrintFormVariableData` | `int SetPrintFormVariableData(LPCTSTR strVariableData)` | O / O | #15 기본 SDK 변수 입력에서 호출 | 0=정상; !! 슬롯과 ^Vxx 번호 대응 |
| `ResetForm` | `void ResetForm()` | O / O | 연결·인쇄 후보 | void. 파일·문자열 서식 초기화; 변수 초기화 보장 없음 |
| `SendData` | `int SendData(LPCTSTR strData)` | O / O | 선택 배출/복귀의 제한된 M/B 명령; 원문 서식과 임의 명령은 공개하지 않음 | 0=메소드 반환; 고정 x86/x64 SDK는 하위 전송 결과와 무관하게 0 반환. 실제 출력·이송 완료 증거 아님. 원문 템플릿 직접전송은 제품 방식으로 채택하지 않음 |
| `SetWritingDataType` | `int SetWritingDataType(USHORT iType)` | O / O | RFID 내부 전용; 출력에서 호출 금지 | API 설명: 0=정상, 기타=오류표 참조. 함수별 raw 값 보존 |
| `AddNfcUriRecord` | `int AddNfcUriRecord(LPCTSTR strUri)` | O / O | RFID 내부 전용; 출력에서 호출 금지 | API 설명: 0=정상, 기타=오류표 참조. 함수별 raw 값 보존 |
| `AddNfcTextRecord` | `int AddNfcTextRecord(LPCTSTR strText, LPCTSTR strLocale)` | O / O | RFID 내부 전용; 출력에서 호출 금지 | API 설명: 0=정상, 기타=오류표 참조. 함수별 raw 값 보존 |
| `AddNfcAARecord` | `int AddNfcAARecord(LPCTSTR strPkgName)` | O / O | RFID 내부 전용; 출력에서 호출 금지 | API 설명: 0=정상, 기타=오류표 참조. 함수별 raw 값 보존 |
| `AddNfcExternalRecord` | `int AddNfcExternalRecord(LPCTSTR strTypeName, LPCTSTR strBinPayload)` | O / O | RFID 내부 전용; 출력에서 호출 금지 | API 설명: 0=정상, 기타=오류표 참조. 함수별 raw 값 보존 |
| `AddNfcMimeRecord` | `int AddNfcMimeRecord(LPCTSTR strTypeName, LPCTSTR strHexData)` | O / O | RFID 내부 전용; 출력에서 호출 금지 | API 설명: 0=정상, 기타=오류표 참조. 함수별 raw 값 보존 |
| `AddNfcSmartPosterRecord` | `int AddNfcSmartPosterRecord(LPCTSTR strUri, LPCTSTR strText, LPCTSTR strLocale)` | O / O | RFID 내부 전용; 출력에서 호출 금지 | API 설명: 0=정상, 기타=오류표 참조. 함수별 raw 값 보존 |
| `AddNfcUserDefinedRecord` | `int AddNfcUserDefinedRecord(BYTE header, LPCTSTR typeName, int typeLen, LPCTSTR id, int idLen, LPCTSTR strHexData)` | O / O | RFID 내부 전용; 출력에서 호출 금지 | API 설명: 0=정상, 기타=오류표 참조. 함수별 raw 값 보존 |
| `AddBlockData` | `int AddBlockData(USHORT iStartBlockNo, LPCTSTR strHexData)` | O / O | RFID 내부 전용; 출력에서 호출 금지 | API 설명: 0=정상, 기타=오류표 참조. 함수별 raw 값 보존 |
| `ClearNfcRecord` | `int ClearNfcRecord()` | O / O | RFID 내부 전용; 출력에서 호출 금지 | API 설명: 0=정상, 기타=오류표 참조. 함수별 raw 값 보존 |
| `SetTagBlockData` | `int SetTagBlockData(USHORT iStartBlockNo, LPCTSTR strHexData)` | O / O | RFID 내부 전용; 출력에서 호출 금지 | 헤더/선언의 int 원형 확인. Rev.1.3 상세 반환 규약 미정의; 임의 정상 판정 금지 |
| `ClearBlockData` | `int ClearBlockData()` | O / O | RFID 내부 전용; 출력에서 호출 금지 | API 설명: 0=정상, 기타=오류표 참조. 함수별 raw 값 보존 |
| `ClearMFClassicSecurity` | `int ClearMFClassicSecurity()` | O / — | RFID 내부 전용; 출력에서 호출 금지 | int 원형만 확인; 샘플·상세 규약 없음 |
| `SetUseResponseBuffer` | `int SetUseResponseBuffer(USHORT iUseBuffer)` | O / O | 내부 선언만; 기본 출력에서 미사용 | 인수 0/1 사용 설정. 반환 0=정상, 기타=오류표 참조. 응답 읽기 export는 없음 |
| `IsUsableResponseBuffer` | `int IsUsableResponseBuffer()` | O / O | 내부 선언만; 기본 출력에서 미사용 | int; 상세 반환 규약 미정의 |
| `SetRfidCondition` | `int SetRfidCondition(USHORT iCardType, USHORT iLockMode, USHORT iVerify, USHORT iRetry, USHORT iOperationSafeMode, USHORT iProtection)` | O / O | RFID 내부 전용; 출력에서 호출 금지 | API 설명: 0=정상, 기타=오류표 참조. 함수별 raw 값 보존 |
| `SetLabelSetting` | `int SetLabelSetting(USHORT iLabelWidth, USHORT iLabelHeight, USHORT iGap, USHORT iType)` | O / O | 내부 선언만; 기본 출력에서 미사용 | 0=정상, 기타=문서 오류 참조; 서식 우선, Registry 저장 부작용 |
| `SetULEV1_NTAG21x_Security` | `int SetULEV1_NTAG21x_Security(LPCTSTR _strOldPwd, LPCTSTR _strNewPwd, LPCTSTR _strPack, USHORT _blockRange, USHORT _protectionMode)` | O / O | RFID 내부 전용; 출력에서 호출 금지 | API 설명: 0=정상, 기타=오류표 참조. 함수별 raw 값 보존 |
| `SetMFClassicSectorSecurity` | `int SetMFClassicSectorSecurity(USHORT iSectorNum, LPCTSTR authKey, USHORT authKeyType)` | O / — | RFID 내부 전용; 출력에서 호출 금지 | int 원형만 확인; 샘플·상세 규약 없음 |
| `SetULCSecurity` | `int SetULCSecurity(LPCTSTR _strOldPWD, LPCTSTR _strNewPWD, USHORT blockRange, USHORT protectionMode)` | O / O | RFID 내부 전용; 출력에서 호출 금지 | API 설명: 0=정상, 기타=오류표 참조. 함수별 raw 값 보존 |
| `SetPrintingOption` | `int SetPrintingOption(USHORT iSpeed, USHORT iDarkness)` | O / O | #15 SetPrintingOptions와 인쇄에 호출 | 0=정상, 기타=문서 오류 참조; 서식 ^S/^H 우선 |
| `SetAfiOption` | `int SetAfiOption(USHORT mode, BYTE afi, USHORT bProtect, USHORT bLock)` | O / O | RFID 내부 전용; 출력에서 호출 금지 | 헤더/선언의 int 원형 확인. Rev.1.3 상세 반환 규약 미정의; 임의 정상 판정 금지 |
| `SetDsfidOption` | `int SetDsfidOption(USHORT mode, BYTE dsfid, USHORT bLock)` | O / O | RFID 내부 전용; 출력에서 호출 금지 | 헤더/선언의 int 원형 확인. Rev.1.3 상세 반환 규약 미정의; 임의 정상 판정 금지 |
| `SetIcodePassword` | `int SetIcodePassword(USHORT pwdId, LPCTSTR strOperPassword, LPCTSTR strNewPassword)` | O / O | RFID 내부 전용; 출력에서 호출 금지 | 헤더/선언의 int 원형 확인. Rev.1.3 상세 반환 규약 미정의; 임의 정상 판정 금지 |
| `SetIcodeEasOption` | `int SetIcodeEasOption(USHORT mode, USHORT type, USHORT bProtect, USHORT bLock)` | O / O | RFID 내부 전용; 출력에서 호출 금지 | 헤더/선언의 int 원형 확인. Rev.1.3 상세 반환 규약 미정의; 임의 정상 판정 금지 |
| `SetIcodePasswordWritingOption` | `int SetIcodePasswordWritingOption(USHORT pwdOption, USHORT lockOption)` | O / O | RFID 내부 전용; 출력에서 호출 금지 | 헤더/선언의 int 원형 확인. Rev.1.3 상세 반환 규약 미정의; 임의 정상 판정 금지 |
| `StartPrintWithRfid` | `void StartPrintWithRfid(LPTSTR _strReturn)` | O / O | RFID 내부 전용; 출력에서 호출 금지 | void 출력 버퍼; 문서의 UID,결과코드. 인쇄 전용 경로 금지 |
| `CheckPrtConnection` | `int CheckPrtConnection()` | O / O | 연결·인쇄 후보 | 1=연결, 0=닫힘; 기타 값은 미확인 상태 |
| `CheckRFConnection` | `int CheckRFConnection()` | O / O | RFID 내부 전용; 출력에서 호출 금지 | 1=연결, 0=닫힘; 태그 감지 아님 |
| `DeviceConnect` | `int DeviceConnect()` | O / O | 연결·인쇄 후보 | 문서의 정상값 0만으로 연결을 판정하지 않으며 CheckPrtConnection=1도 요구. SetRfidPort 미호출 USB 실기와 정적 근거는 구현·검증 문서 참조 |
| `DeviceDisconnect` | `int DeviceDisconnect()` | O / O | 연결·인쇄 후보 | int 원형, 의미 있는 반환값 미정의. CheckPrtConnection으로 해제 확인 |
| `StartLabelPrint` | 샘플: `void StartLabelPrint(ushort iCopies, StringBuilder strReturn)` / LPStr, SizeConst=100 | — / O | 기본·선택 인쇄 경로에서 copies=1 호출; 기존 출력 포인터 보호 재사용 | 과거 고정 SDK의 copies1·ANSI949·ASCII/NUL 응답 및 x86/x64 한글 실기 기록은 보존한다. 이번 #15는 제조사 SDK·장치를 실행하지 않았다. 응답0은 SDK 정상 응답이며 배출 완료 아님. RFID API/기록 명령 미사용 |

### 원형을 알 수 없거나 export가 없는 항목

| 항목 | 관측 | 처리 |
| --- | --- | --- |
| `StartPrintWithRfid2` | x86 `_StartPrintWithRfid2@0`, x64 `StartPrintWithRfid2` | @0만으로 void()/인수·반환 의미를 추정하지 않음. 제조사 원형 확보 전 보류 |
| `ReadResponseMessage` | API 설명에 있으나 두 기준 DLL export에 없음 | 호출 선언을 만들지 않음. 수신 버퍼만 켜서 상태 응답을 확인했다고 간주하지 않음 |
| `StartPrintingWithRfid` | API 설명의 명칭, 실제 `StartPrintWithRfid`와 불일치 | 실제 이름으로 내부 래핑. 구예제의 int 반환/BT003Lib.dll 선언을 복사하지 않음 |
| `AddNfcSpRecord`, `AddNfcExterRecord` | 문서 표기와 실제 SmartPoster/External 이름 불일치 | 위 표의 실제 export 이름 사용 |

## 2. ABI와 배포

헤더의 `USHORT`→`ushort`, `BYTE`→`byte`, `int`→`int`를 유지한다. 호출 규약은 원본 선언의 StdCall이다. 원본의 DLL 이름·ANSI/LPStr와 기본 문자열 선언은 보존하며 LPCTSTR/LPTSTR만 보고 Unicode로 변경하지 않는다. 현재 제품은 자체 preload·경로/모듈 검사 없이 원본 DllImport와 .NET 기본 로딩을 사용한다. StartLabelPrint도 원본 StringBuilder 선언을 직접 호출한다. ApplicationDirectory/System32 검색 제한은 유지하고 고정 해시는 Validation에서만 검사한다. 제품에 일반 함수 검색과 RFID 공개 API는 없다. 원본에 RFID 선언이 있는 것과 제품이 사용하는 것은 구분한다. 과거 실기 기록은 현재 전환의 검증 결과가 아니며 SizeConst=100은 출력 안전성 증거가 아니다. 실제 x86/x64 P/Invoke 연결과 StringBuilder native 반환은 미검증이다.

문서의 두 보안 함수는 반환을 C/C++ `long`으로 표기하지만 헤더/샘플은 int다. Windows native long도 32-bit이므로 .NET `long`(Int64)으로 바꾸지 않는다. 모든 원형에서 문자열의 입력·출력 방향도 유지하고 x86 장식 이름과 실제 인수 크기를 대조한다.

| 아키텍처 | 파일 | SHA-256 |
| --- | --- | --- |
| x86 | `BlackfishNfcLib.dll` | `fed09bae47835dcf78f58579e3cfb9382561799b369e54fd9fa0902e5b89dbf5` |
| x86 | `HFModule.dll` | `bb2721801bc853ead75e647608c6b36a5a3a75f344095e4e29ffc35e822319d4` |
| x64 | `BlackfishNfcLib.dll` | `64c7874980cb0a774a766e299359bc9cba5dd66fd052d758f1d4d1678d8cf72c` |
| x64 | `HFModule.dll` | `542ebfbccca1f953b4dcafb2a1faed198707d8fbcdb0653c0fc121d3c273edf5` |

두 Blackfish 파일의 버전 표기는 2.1.0.0이다. SDK 내 같은 버전 번호의 다른 해시 파일과 혼합하지 않는다. Blackfish의 HFModule 의존성을 배포 세트로 고정한다. 관리 DLL AnyCPU와 native 아키텍처 일치는 별도로 확인한다. COM 등록·전역 설치는 하지 않는다.

실행 프로그램 폴더에 프로세스와 같은 native 한 세트를 배치한다. 현재 #16의 배포 파일 PE·아키텍처·고정 해시는 Validation에서 검사한다. 실행 시 해시나 기존 모듈 충돌 검사는 하지 않는다. ConnectUsb에서 원본 DllImport를 처음 호출하며 DLL 누락·아키텍처·entrypoint 예외는 현재 공개 API의 고정 식별 코드로 전달한다. 오프라인 검사·미리보기는 SDK 파일 없이 동작한다. 실제 로딩 정책과 수명은 [동기 USB 스펙](specs/SYNC_USB_MVP.md), 오류 전달은 [공개 API](PRINTER_API.ko.md)가 기준이다.

과거 TASK-02는 당시 Windows 환경의 실제 x86/x64 로딩·41개 바인딩과 USB "0"의 SetRfidPort 미호출 연결·해제를 확인했다. 파일·해시·기존 모듈 충돌을 구분한 자체 로더 오류 체계도 당시 구현의 역사다. RFID 케이블을 물리적으로 분리한 조건은 검증하지 않았다. [당시 구현·검증 상태](NATIVE_SDK_IMPLEMENTATION.md)의 근거는 현재 #16 로더·StringBuilder 또는 모든 OS·인쇄 계약의 실행 검증으로 사용하지 않는다.

## 3. 반환값 해석과 인쇄 경로

아래는 제조사 원형과 과거 진단의 반환값 검토 기록이다. 현재 제품의 정상 응답·Closed 확인, 실패/불명 분류와 서식 정리는 [#16 스펙](specs/SYNC_USB_MVP.md)의 명시적 정책을 따른다.

- `SetPrinterPort`의 1(이미 연결), `CheckPrtConnection`의 1(연결), 장치 오류표의 1(Paper out)은 서로 다른 계약이다. 함수명·호출 단계·원래 int를 함께 보존한다.
- `DeviceDisconnect`의 int를 근거 없이 0만 성공이라고 정의하지 않는다. 반환값과 후속 포트 상태를 각각 기록한다.
- void 함수의 반환을 0 성공으로 만들지 않는다. 특히 `StartLabelPrint`에 RFID 함수의 UID,code 파서를 전용해서 성공 판정하지 않는다.
- 원본 오류표의 Hex/Dec 불일치는 6행이다. 0x006A는 원문 Dec=107/계산=106, 0x271B~0x271F는 원문 Dec=10010~10014/계산=10011~10015다. 관측한 raw 정수를 임의 보정하지 않는다.
- 현재 연결·인쇄 순서와 중단 조건은 [동기 USB 스펙](specs/SYNC_USB_MVP.md)에 있다. RFID 함수와 SetLabelSetting의 Registry 변경을 기본 흐름에 섞지 않는다.

## 4. 확인 근거

- SDK `라이브러리/x86/include/BlackfishNfcLib.h`, `라이브러리/x64/include/BlackfishNfcLib.h`.
- SDK `샘플소스/HFSample/HFSample_UL/BT003CtrlWrapper.cs`.
- 로컬 제공 API 변환본 `NFC_BT003-M01-04_Programming_Manual_v1.3_markdown/NFC_BT003-M01-04_Programming_Manual_v1.3.md`. 원본 PDF Rev.1.3, 2022-05-20.
- 로컬 BTPL 참고 가이드 `BTPL_API_GUIDE.ko.md` 14~15절. 가이드의 39개 이름 확인은 샘플 선언 범위이며 전체 헤더/export 커버리지가 아니다.
