# TASK-02 Native SDK 구현·검증 상태

이 문서는 버전별 native 진단·실기 이력이다. 현재 #16 제품은 제조사 원본 Wrapper 39개 선언과 StringBuilder를 직접 사용하고 자체 로더·실행 시 해시·포인터 bridge를 제거했다. 현재 계약은 [동기 스펙](specs/SYNC_USB_MVP.md)과 [공개 API](PRINTER_API.ko.md)다. 아래 과거 보호·바인딩·실기 기록은 새 구현의 native 성공 증거가 아니다.

기준: v0.1.1-design, SDK_API_COVERAGE.md의 두 native 배포 쌍. .NET Framework 4.6.1 관리 DLL은 AnyCPU다. 후속 TASK-05에서 고정 제조사 런타임 사본을 Sdk/x86·x64에 준비했으며 [배포 가이드](SDK_DEPLOYMENT.ko.md)를 따른다.

2026-10-09 TASK-05 현황: [제품 API 가이드](PRINTER_API.ko.md)의 동기 연결·사전검사·단건 wrapper를 구현·합성 검사했다. 아래는 TASK-02 시점의 진단/게이트 이력이다. raw GetFunction 게이트는 유지하고 현재 제품은 허용된 네 연결 함수·세 인쇄 함수의 typed 접근과 사전검사를 사용한다. 새 실제 장치 검증·일괄/취소·native 장애 격리 완료를 뜻하지 않는다. 과거 “제품 인쇄 미구현/차단” 문구는 당시 버전의 상태로 읽는다.

2026-10-08 모듈 역할 정정: 아래 기록은 TASK-02의 진단 이력이며 공개 제품 API의 구현 완료가 아니다. 후속 TASK-05는 [모듈 역할](MODULE_OVERVIEW.ko.md)에 따라 제조사 wrapper의 연결·인쇄 기능을 기본 사용으로 제공하고 DataTable/JSON은 선택 기능으로 둔다. 별도 NativeHost와 native 장애 격리 보장은1차에 포함하지 않는다. 과거 보호 경계 검토·진단 근거를 범용 안전 보장으로 바꾸지 않으며 미검증 범위는 #10에 남긴다. 현재 소스의 인쇄 게이트는 그대로 유지하고 확인된 호출 경로의 제품 구현·검증을 먼저 완료한다.

최신 판정은 마지막 [이번 실기 판정과 남은 계약](#이번-실기-판정과-남은-계약)을 기준으로 한다. 앞선 실행·미확인·다음 작업 기록은 각 시험 시점의 이력이며 현재 상태로 읽지 않는다. 제조사 추가 문서의 부재는 중단 사유에서 제외하되 제품 인쇄 차단은 유지한다.

## 구현 범위

- `NativeApi`는 헤더 40개와 샘플의 StartLabelPrint를 합한 41개 내부 delegate를 선언한다. StdCall, ANSI 입력 LPStr, ushort/U2·byte/U1·int32를 보존한다. x86은 기준 DLL에 실제 존재하는 `_Name@N`, x64는 실제 비장식 이름으로 바인딩한다. 현재 인수는 모두 x86에서 4-byte stack slot이며 지원 타입 외에는 이름을 추정하지 않는다.
- GetUidValue·StartPrintWithRfid·StartLabelPrint의 출력 인수는 caller-owned 주소의 `[Out] IntPtr`다. RFID 함수는 선언만 하고 호출하지 않는다. StartLabelPrint의 정상42-byte/NUL 분석 계약은 별도 격리 진단에서만 사용하며 제품 호출은 차단한다. 샘플 SizeConst=100은 안전 상한으로 사용하지 않는다.
- StartPrintWithRfid2와 ReadResponseMessage는 선언·호출하지 않는다. public P/Invoke, raw SendData, RFID API 또는 검증 우회 옵션은 없다. `GetFunction<T>()`는 이 구현 버전에서 항상 PrintContractUnverified로 거부한다.
- `NativeImage`는 길이 제한·offset/RVA 범위 검사로 PE와 명명 export·일반 import를 읽는다. DLL을 map하거나 실행하지 않는다. 아키텍처·고정 해시·41개 exact export와 forwarder 여부를 대조한다. 기준 외 delay import·private import는 거부한다.
- `LoadForConnection()`은 AppDomain.BaseDirectory의 로컬 드라이브 한 쌍만 대상으로 한다. 부모 reparse 경로, 파일/아키텍처/해시, 사전 로드된 vendor 모듈, 앱 폴더의 시스템 DLL 가림을 검사한다. 파일 핸들의 최종 경로를 대조하고 부모 디렉터리에는 목록 읽기·share-delete 금지 lease를 유지한다. 절대경로 LoadLibraryExW(0x900), 모듈 경로 재검사, exact GetProcAddress를 사용하며 검색 경로 fallback·PATH 변경·COM 등록·unload는 하지 않는다. OS import의 기존/후속 모듈은 현재 프로세스 시스템 디렉터리(WOW64 실제 경로 포함)만 허용한다. acquired 모듈과 delegate는 process lifetime으로 유지하고 부분 로드 이후 재시도도 막으며 최초 실패의 코드·함수·Win32 오류를 보존한다.

로드는 DllMain과 dependency 초기화를 실행한다. 오프라인 PE 검사와 실제 로드 시험을 구분한다. 현재 로더는 internal이며 공개 Connect에 연결하지 않았다. TASK-05에서 worker·process/target 예약·대기 제한·늦은 정리 계약과 함께 연결해야 한다. 현재 캐시는 AppDomain 단위이며, 소유한 캐시가 아닌 기존 vendor 모듈은 같은 경로여도 보수적으로 NativeModuleConflict로 거부한다. 참여 AppDomain 간 안전한 재사용은 후속 수명 통합에서 별도 검증해야 한다.

배포 폴더와 Windows 시스템 폴더는 신뢰되는 관리 영역이라는 전제가 필요하다. 파일·디렉터리 lease는 정상 파일 I/O의 교체·쓰기·부모 rename을 제한하지만, 공격자가 배포 폴더에 새 dependency를 삽입할 수 있는 환경이나 악의적인 관리자·기존 memory mapping·동적 DLL 로드를 모두 격리하는 sandbox는 아니다. ACL·호스트 보안 설정은 바꾸지 않는다. 두 private DLL과 관측된 일반 import만 검사하며 제조사 내부의 동적 로드/초기화 효과는 실기 검증 대상으로 남긴다. 최초 시험은 WinSxS gdiplus.dll을 거부했으나 후속 보완으로 OS WinSxS의 canonical gdiplus.dll 중 현재 프로세스 시스템 기준 파일과 길이·SHA-256이 같은 경우만 지원한다. 부모 reparse·핸들 final path·읽기 lease·1~16 MiB 상한을 확인한다. 다른 DLL이나 다른 바이트의 버전은 계속 거부한다.

## 실행 검사

```text
node tools/verify-build.js
node tools/verify-build.js --configuration Debug
<Validation.exe> --inspect-sdk <SDK의 라이브러리 폴더>
node tools/probe-native-load.js --sdk-directory <SDK의 라이브러리 폴더>
node tools/probe-native-load.js --sdk-directory <SDK의 라이브러리 폴더> --connection usb --port 0
```

일반 빌드 검사는 이전 framework·PE·프로세스 비트 수·native 미로드 검사에 41개 ABI golden 검사, 계약 차단, missing/corrupt/architecture/version 오류 검사를 추가한다. --inspect-sdk는 라이브러리 폴더 아래 x86/lib와 x64/lib의 실제 기준 파일을 읽고 두 쌍의 Machine·해시·exact 41 exports·일반 imports를 대조한다. vendor 코드는 실행하지 않으며 실제 연결·인쇄·RFID 시험을 대체하지 않는다.

SDK 검사 성공 출력은 `PASS: pinned x86/x64 native pairs architecture/hash/41 exports/imports inspected; vendor code not executed`다. 원본·실제 label/RFID 값·raw 응답은 출력하거나 저장하지 않는다. 검증 오류는 안전한 코드와 원래 OS 오류 번호만 보존한다.

### 관측한 실행 근거

2026-10-08, 제품 코드 커밋 `51d9ceaebdbc3167254b3627a512309c1728e495`의 clean 작업 트리에서 fresh verifier가 Release/Debug × x86/x64 네 빌드·실행을 수행했고 모두 종료 코드 0이었다. root가 같은 네 실행 파일로 실제 SDK의 x86/x64 쌍을 --inspect-sdk로 검사했고 모두 종료 코드 0이었다. 사본 8개 해시는 verifier가 실제 바이트로 재계산해 원본 evidence와 대조했다. 다음 표는 해당 실행의 식별값이며 이후 문서 커밋과 구분한다.

| 구성 | 산출물 | SHA-256 |
| --- | --- | --- |
| Release | DLL, x86/x64 공통 | `ac006c07a5519a6e91a819bdcae53f4345951892e57b71a652cdb406999afe57` |
| Release | x86 validation exe | `e94553163398b13699df19fbb072c25d0172d2cd410b742da6e3799f21e561fa` |
| Release | x64 validation exe | `6704bf13fbc0483d92d7aa1d56179b3def11ad1d540bc76c4a65a98b06762bf5` |
| Debug | DLL, x86/x64 공통 | `351efc67694fec99dfead4527da0efc96a3041b76d9a8dbb0199b2937612d628` |
| Debug | x86 validation exe | `84ab15c9933ae80e08e3db88cadb654bbf004d9b827643cab040f3d73f9fde4d` |
| Debug | x64 validation exe | `2f30fd328b85ddcf9a88b97e8c13941906e0625e43033265e3eb1a32650aa36a` |

원본 로컬 기록은 `.work/foundation/Release-1791411556958/evidence.json`과 `.work/foundation/Debug-1791411562492/evidence.json`에 보존한다.

최초 시험 커밋 f7163d0에서 wrapper 없는 실행이 TypeLoadException으로 실패했고 시험 잠금 뒤 구현했다. 리뷰 후 abfd915에서 새 경로·예외 회귀 시험의 실패를 확인하고 다시 잠갔다. 수정 후 실제 Windows 파일 핸들 canonical path, Windows Temp 모듈 경로 거부, 목록 읽기 lease 중 자기 임시 폴더 rename 거부 시험을 통과했다. attributes-only 핸들은 rename을 막지 못하는 것이 관측되어 list/read access로 보완했다. 이는 정상 I/O의 rename 거부 시험이며 vendor DLL 초기화·dependency 삽입 race의 통합 시험은 아니다.

Win32 구현 근거는 [LoadLibraryExW](https://learn.microsoft.com/en-us/windows/win32/api/libloaderapi/nf-libloaderapi-loadlibraryexw), [GetFinalPathNameByHandleW](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getfinalpathnamebyhandlew), [디렉터리 핸들](https://learn.microsoft.com/en-us/windows/win32/fileio/obtaining-a-handle-to-a-directory), [PE Format](https://learn.microsoft.com/en-us/windows/win32/debug/pe-format)을 대조했다.

### 후속 로딩 진단 결과

2026-10-08, `861ba5c967768d70b919e6323191d5ebb11f4ae3` clean 작업 트리에서 fresh verifier가 Release/Debug × x86/x64 빌드·검증을 다시 실행했고 모두 종료 코드 0이었다. 새 위치 분류 회귀 검사는 system/sideBySide/other를 구분하며 기존 허용 경로 검사와 PrintContractUnverified 차단을 유지했다. Release 산출물은 runner가 고정한 관리 코드 `8a582353fc73e186c50f34968820f980fb7f8b1e`의 해시와 일치했다.

| 산출물 | Release SHA-256 |
| --- | --- |
| DLL, x86/x64 공통 | `e7fe54b69bafba711831bc991a29d1f8ca252ac770a1ae9e467096a841cf517f` |
| x86 validation exe | `e7cdf24d325bfa255534416f8130cf27f26349b503d9247b87f5a7ecdbd036fe` |
| x64 validation exe | `5133502a2a21339cecb930274a94e008a7c1923946bbfed0a1dc8524dcd90efe` |

관리 검사 원본은 `.work/foundation/Release-1791415619425/evidence.json`과 `.work/foundation/Debug-1791415625954/evidence.json`이다. 앞의 51d9cea 근거와 현재 산출물을 혼동하지 않는다.

root가 고정된 제조사 SDK 쌍을 사용하여 `tools/probe-native-load.js`를 실행했다. 첫 실행은 양쪽 NativeModuleConflict/exit 5였고, 검사 단계와 허용 목록의 모듈명만 출력하도록 진단을 보완한 두 번째 실행에서도 다음과 같이 실패했다.

```text
x86: NATIVE_LOAD|status=Failed|code=NativeModuleConflict|win32=none|detail=postload:system-import:gdiplus.dll:sideBySide; exit=5
x64: NATIVE_LOAD|status=Failed|code=NativeModuleConflict|win32=none|detail=postload:system-import:gdiplus.dll:sideBySide; exit=5
NativeLoadProbeFailed
```

감독 프로그램의 종료 코드는 1이다. 이 결과는 두 vendor DLL의 LoadLibraryEx와 경로 대조 이후, postload 시스템 import 검사에서 WinSxS gdiplus.dll을 거부한 증거다. 41개 delegate 바인딩 완료 또는 USB 연결 성공을 뜻하지 않는다. 장치 delegate를 꺼내거나 호출하지 않았다. 로컬 근거는 `.work/native-load/1791415327431/evidence.json`과 `.work/native-load/1791415681323/evidence.json`이다.

진단은 검토한 Release exe/DLL과 제조사 두 쌍의 해시를 source/stage에서 검사한다. 관리 코드만 있는 별도 stage의 OS 시스템 디렉터리 조회로 환경과 호스트 도구 경로를 구성하며 caller PATH/SystemRoot를 사용하지 않는다. vendor load 전 Job Object를 설정하고 프로세스·메모리·시간·출력을 제한한다. 출력은 nonce가 일치하는 고정 report와 허용된 진단 필드만 기록하고 성공은 exact Passed report와 exit 0을 함께 요구한다. 이 도구는 현재 64-bit Windows host에서 x86/x64 child를 실행하는 진단이며, 네트워크 sandbox나 악성 in-process DLL의 격리를 보장하지 않는다. 제조사 초기화·dependency 코드의 실행과 장치 API 호출을 구분한다.

정확성·보안 리뷰는 이 단계의 진단 tag가 로드 허용 조건을 바꾸지 않고 실제 경로를 보고하지 않음을 확인했다. bare vendor ModulePath 충돌 등 일부 오류는 상세 tag 없이 실패할 수 있으며 이를 성공으로 해석하지 않는다. 다음 단계에서도 WinSxS 전체 허용, 경로 fallback 또는 무검증 preload는 추가하지 않았다.

### 로더 보완과 USB 연결·해제 결과

시스템 파일과 내용이 같은 gdiplus.dll만 허용하는 `172ce59c9805349038260843e033ebc291359aec` 보완 후, fresh verifier가 `efa30b09725fe29df0baa40149a2d911ef5b6230`에서 네 구성의 일반 빌드·실행 검사를 통과했다. 같은 기준을 만족하는 실제 OS WinSxS 파일은 통과하고 다른 이전 버전·빈 파일·변경된 바이트·잘못된 경로는 거부했다. root의 실제 SDK 로딩 시험은 x86/x64 모두 `NATIVE_LOAD|status=Passed|bound=41|deviceDelegatesInvoked=0`, exit 0이었다. 근거는 `.work/native-load/1791417880905/evidence.json`이며 앞의 실패 근거를 대체하지 않고 후속 수정 결과로 구분한다.

인쇄 출력 계약이 확인되지 않았다는 이유로 버퍼 없는 연결 시험까지 막았던 범위를 분리했다. `0477e619c81734f673231f5458d83eb6cca23fd4`는 internal `ProbeUsbConnection`을 추가하며 원래 `GetFunction<T>()`의 PrintContractUnverified 게이트는 유지한다. helper는 네 가지 확인된 delegate만 내부에서 사용하고 delegate 자체를 반환하지 않는다.

고정 진단 순서는 Pre=CheckPrtConnection → SetPrinterPort(1,"0","") → DeviceConnect → Open=CheckPrtConnection → DeviceDisconnect → Closed=CheckPrtConnection이다. Pre와 SetPort가 각각 정확히 0인 경우에만 연결을 시도한다. 기존 연결·미정의 사전 상태·설정 실패에서는 다른 연결을 끊지 않는다. DeviceConnect가 반환된 경우에만 같은 worker에서 한 번 해제하고 Closed를 확인한다. Disconnect의 int는 raw로 기록하며 정상 판정에 사용하지 않는다. Passed는 Pre=0·SetPort=0·Connect=0·Open=1·Closed=0을 모두 요구한다.

process 슬롯과 SHA-256("USB:0")의 Local 대상 mutex를 worker가 취득하고 최종 닫힘 확인까지 유지한다. abandoned/Busy를 성공으로 보정하지 않는다. 첫 USB 실패 이후 다른 architecture를 자동 실행하지 않으며 timeout/pending에서 닫힘을 추정하지 않는다. 실제 연결 전 대상 mutex를 점유한 시험에서는 x86 ProbeSlotBusy/exit 4 뒤 중단되고 x64를 시도하지 않는 것을 확인했다.

2026-10-08, root가 `3c935dd5b89703ec3e61b539b9644c2cea585cd4`의 진단으로 준비된 USB 장치를 시험했다.

| 아키텍처 | Pre | SetPrinterPort | DeviceConnect | Open | DeviceDisconnect raw | Closed | child exit |
| --- | --- | --- | --- | --- | --- | --- | --- |
| x86 | 0 | 0 | 0 | 1 | 0 | 0 | 0 |
| x64 | 0 | 0 | 0 | 1 | 0 | 0 | 0 |

supervisor도 exit 0이었다. 이는 제공 샘플의 USB 문자열 "0"을 사용한 프린터 포트 연결·해제 근거다. RFID 포트를 설정하지 않았고 관리 call graph에서 RFID·서식·인쇄 API를 호출하지 않았다. report의 printApiCalls/rfidApiCalls=0은 해당 관리 코드 경로에 대한 값이며 제조사 DllMain 내부를 계측한 값은 아니다. 용지·리본·헤드 준비, 인쇄 응답, 실제 출력 및 RFID 전후 일치는 이 결과로 증명하지 않는다. 실제 근거는 `.work/native-load/1791418473250/evidence.json`이다.

사용자 확인 결과, BT-003 PRO USB와 COM3은 각각 연결해도 Windows에 나타나지만 현재 장치의 프린터 연결은 케이블 하나만 연결하면 실패하고 두 케이블을 모두 연결해야 성공한다. 따라서 Windows에서 각각 표시되는 것을 단일 케이블로 프린터 연결이 가능하다는 뜻으로 해석하지 않는다. Windows의 읽기 전용 조회에서도 BT-003 Pro와 FTDI USB Serial Port(COM3)의 상태 OK를 확인했다. 단일 케이블 실패·두 케이블 성공은 사용자 확인 결과이며, 이 조건별 SDK 시험을 에이전트가 재현한 것은 아니다. 위 SDK 시험에서 확인한 것은 **SetRfidPort 미호출 상태의 연결 성공**이다. 두 케이블이 필요한 원인과 COM3의 통신 역할은 아직 확인하지 않았으므로 COM3을 자동으로 인쇄 포트 또는 리더 포트에 배정하거나 SetRfidPort 호출이 필수라고 단정하지 않는다.

| 현재 Release 산출물 | SHA-256 |
| --- | --- |
| DLL, x86/x64 공통 | `b0d649e17b97dd806c388f2863476274f32f3a83953bf496d8ea3cf8fdaf6495` |
| x86 validation exe | `6492848e12dc57d25dba8488f539423fee12fb1df10a7f2e5d6cd434e319d271` |
| x64 validation exe | `5278c12c29f6e007d47603365bed1376994a76b0b2b1ef84d44abac7ea477091` |

fresh verifier는 `1ea595b`에서 validation csproj를 직접 Release/Debug × x86/x64 재빌드·실행했고 모두 exit 0이며 위 manifest 해시와 일치했다. 호출 순서·occupied/unknown 상태·설정 실패·연결 실패 정리·해제 raw 값 보존·닫힘 실패·Connect 예외를 관리 delegate fixture로 검사했고 기존 인쇄 gate와 native 미로드 검사를 유지했다. 이 fixture는 실제 장치 결과의 대체물이 아니다.

작업 중 사용자가 Visual Studio를 열고 닫으면서 솔루션 구성이 Any CPU만 남도록 바뀌었다. 이 사용자 변경은 보존하고 커밋에 포함하지 않았다. 변경된 솔루션의 일반 verify-build는 MSB4126으로 실패했으며 통과로 보고하지 않는다. 후속 직접 빌드는 같은 검증 프로젝트와 MSBuild 인수에 Configuration/Platform을 명시한 결과다. 저장소 검사·diff 검사 및 tool CLI 검사는 통과했다. 솔루션 x86/x64 구성 정리는 별도로 남아 있다.

### 명시적 연결 입력 보완

진단 콘솔은 연결 방식을 `usb/serial/tcp`로 명시하고 포트를 입력받는다. 기본 포트·자동 연결·다른 포트 fallback은 없다. USB 문자열은 그대로 유지하며 ANSI 전달 가능 여부를 검사한다. COM3/com003/3은 SDK에 숫자 문자열 3으로 전달하고 TCP 포트는 1~65535의 십진 문자열, IP는 실제 주소의 정규 표기로 고정한다. native 전달 인수와 대상 mutex 이름은 동일한 옵션 객체에서 얻는다. 기존 USB 진단 helper는 회귀 검사 호환을 위해 내부에만 남으며 실행 CLI가 기본 USB 0을 고르는 경로는 없다.

```text
node tools/probe-native-load.js --sdk-directory <SDK의 라이브러리 폴더> --connection usb --port 0
node tools/probe-native-load.js --sdk-directory <SDK의 라이브러리 폴더> --connection serial --port COM3
node tools/probe-native-load.js --sdk-directory <SDK의 라이브러리 폴더> --connection tcp --port 9100 --ip 192.0.2.1
<Validation.exe> --list-ports
node tests/connection-probe-cli.test.js
```

RS-232/TCP/IP 명령은 입력 예시이며 COM3의 인쇄 포트 역할이나 예시 주소에 장치가 있다는 뜻이 아니다. COM 목록 조회는 .NET SerialPort.GetPortNames만 사용하고 포트를 열거나 선택하지 않는다. 실제 통신은 SDK가 수행하며 별도 SerialPort를 함께 열지 않는다. 현재 장치는 두 케이블을 모두 연결해야 한다는 사용자 확인 조건을 따른다. 이 보완은 TASK-02의 internal 진단이며 생산 공개 연결 API·수명 통합은 TASK-05다.

StartLabelPrint의 추가 정적 근거와 남은 버퍼·응답 경계는 [정적 분석 결과](START_LABEL_PRINT_ANALYSIS.md)에 기록한다. 실제 인쇄 호출을 실행하거나 게이트를 해제하지 않았다.

2026-10-08, `49e9b86`에서 fresh verifier가 CLI·문서 검사를 통과하고 Release 실행 파일/DLL 네 개가 관리 소스 `9c62367`의 manifest와 일치함을 확인했다. `3dbbad2`에서 독립적으로 실행한 validation csproj 직접 Release/Debug × x86/x64 빌드·검증도 모두 exit 0이었다. root의 일반 verify-build는 기존 사용자 솔루션에 Release|x86 구성이 없어 MSB4126/exit 1로 실패했다. 직접 프로젝트 검사를 솔루션 검사 성공으로 표시하지 않는다. 관리 검증에서는 vendor를 실행하지 않았고 양쪽 새 연결 CLI의 SDK 없는 경로는 NativeLibraryMissing/exit 3이었다. 양쪽 `--list-ports`는 COM3을 반환했으며 포트를 열지 않았다.

| 명시적 진단 Release 산출물 | SHA-256 |
| --- | --- |
| DLL, x86/x64 공통 | `4ea005499d7355d67e070c7d4aa9a40bd9e42e252b21f389e019c22ccbecea71` |
| x86 validation exe | `cec1f0c5e3845826a49086b000eb44d47cb0df2f838b8d5a9e7536c5f4af26b6` |
| x64 validation exe | `364338dced85c05a72ae7d0428a4ddbec21b3158b74a57c16446a944b5165ad4` |

같은 root 진단에서 명시적으로 입력한 USB 0의 대상 mutex 점유 시 x86 ProbeSlotBusy/exit 4, supervisor exit 1이며 x64는 미시도였다. 점유를 해제한 뒤 실제 x86/x64 연결은 모두 Pre=0·SetPort=0·Connect=0·Open=1·Disconnect raw=0·Closed=0, child/supervisor exit 0이었다. task probe 프로세스 잔여 수는 0이다. 근거는 `.work/native-load/1791422038951/evidence.json`(Busy)과 `.work/native-load/1791422039454/evidence.json`(실제 연결), `.work/explicit-connection-verification/`(관리 검사)이다. TCP/IP·RS-232 및 인쇄·RFID 실기는 실행하지 않았다. 인쇄/RFID 호출 0회는 앞서 설명한 관리 call graph 범위다.

## 남은 수용 근거

| TASK-02 조건 | 현재 경계 |
| --- | --- |
| 41개 내부 래핑 | 선언·offline ABI 검사와 실제 두 아키텍처 41개 바인딩 통과; 모든 native 함수 호출을 검증했다는 뜻은 아님 |
| 아키텍처·해시·충돌·export·절대경로 로더 | metadata/negative 검사와 gdiplus 내용 검증, 실제 두 아키텍처 로딩·바인딩 통과; 장시간 모듈 수명 통합은 후속 범위 |
| ANSI·방향·StdCall·버퍼 상한·종료 규칙 | 제공 선언 대조, 정상 상태의 42-byte 진단 후보와 종료 NUL을 정적 대조; 제조사 보편 계약·buffer/문자열 실기는 미확인 |
| USB 식별·SetRfidPort 미호출 연결·해제 | 샘플 문자열 "0", x86/x64 실제 연결·해제 통과. 물리적 케이블 조건은 위 사용자 확인 결과와 구분하며 "USB1" 별칭은 시도하지 않음 |
| StartLabelPrint 전용 응답·완료 의미 | x86 최초 진단에서 빈 prefix·native=0·길이 2 정상 응답, 사용자 확인은 실제 미출력. 코드 0은 물리적 완료 증거가 아님; 전체 계약 미확인 |
| 한글·BTPL 글꼴·한 행 한 장·RFID 보존 | ASCII 인쇄 진입 1회, 실제 미출력. 한글·수량·RFID 전후 저장 데이터 일치 미검증 |
| 장치·펌웨어·시험 결과 | Windows BT-003 Pro·COM3 열거와 최초 진단 기록 확보, 실제 펌웨어 미확인 |

핵심 미확정 조건이 남아 있으므로 TASK-02 전체를 완료하거나 실제 인쇄가 가능하다고 보고하지 않는다.

## 완료를 위해 필요한 조치

1. StartLabelPrint의 정확한 원형, caller 버퍼 소유권, 최대 쓰기 **바이트** 수와 종료 문자 포함 여부, 인코딩, 잘림·오류 처리 규칙을 확인한다. 매개변수에 길이가 없으므로 샘플 SizeConst=100이나 임의의 큰 버퍼만으로 안전성을 확정하지 않는다. 사용자는 추가 규격이 없으며 제공 Rev.1.3 문서와 샘플을 기준으로 진행하도록 확인했다. 해당 자료의 StartLabelPrint는 appendix 선언이며 버퍼 쓰기 상한과 전용 응답 의미가 추가로 설명되어 있지 않다.
2. 인쇄 전용 응답 문법·성공/오류 예·완료 시점·timeout/미정의 응답, copies와 BTPL 수량의 관계, RFID 무기록 동작을 확인한다. StartPrintWithRfid의 UID,code 형식을 전용하지 않는다.
3. 합성 한글 문구 한 건을 출력하고 장수·글꼴·RFID 전후 일치를 기록한다. RFID 비교는 검증된 별도 시험 절차로 수행하며 제품에 RFID 공개 API를 추가하지 않는다.

사용자 장치 준비와 실제 USB 연결·해제는 확인되어 있다. 현재 작업 순서는 TASK-02 완료 → TASK-03이며, 인쇄 계약·실기 조건이 남아 있으므로 TASK-03을 착수하지 않았다. TASK-02 조건 1·2·4의 근거는 GitHub #2에도 반영했으며 이슈는 OPEN이다. 코드 push는 수행하지 않았다.

## 50×25 mm 일회 출력 진단 준비

사용자가 확정한 시험 규격은 **폭 50 mm·높이 25 mm·간격 3 mm**다. 진단은 임의 원문·DB 입력을 받지 않고 고정 ASCII `U2BIO TEST`만 사용한다. `^Q25,3`, `^W50`, `^P1`, `^C1`과 SDK copies=1을 지정하며 RFID 명령은 포함하지 않는다. AC 글꼴은 참고 가이드의 문법에 따른 최초 후보이며 이 장치의 지원과 실제 장수는 출력 이후 확인해야 한다. 한글·AW 지원을 검증한 것으로 처리하지 않는다.

```text
node tools/probe-native-load.js --sdk-directory <SDK의 라이브러리 폴더> --connection usb --port 0 --label-test 50x25gap3 --architecture x86
node tests/label-probe-cli.test.js
```

실기 명령은 **실행 준비 예시**다. 인쇄 중 SDK 자체 로그가 작업 루트 밖 고정 디렉터리에 생성·추가 기록되는 것을 정적 확인했으므로, 정확한 대상에 대한 사용자 허용 전에는 호출하지 않는다. 원본 SDK를 패치하거나 로깅을 hook으로 차단하지 않는다.

`--label-test`는 USB와 단일 x86/x64 선택을 필수로 한다. 다른 아키텍처를 자동 시도하지 않으며, 실패·불명 후 재출력하지 않는다. ResetForm→SetPrintFormString→StartLabelPrint만 내부 typed 경로로 사용하고 생산 GetFunction 게이트는 유지한다. 연결 전 점유, 포트 설정·연결·Open 실패와 서식 등록 실패는 인쇄 진입을 막는다. 호출이 반환한 뒤 기존 연결 정리 규칙을 적용한다.

새 메타데이터 파서·42-byte 뒤 접근 불가 페이지·오류 정리의 근거와 한계는 [정적 분석 결과](START_LABEL_PRINT_ANALYSIS.md)를 따른다. CLI 결과의 `Returned`는 응답이 문법 검사를 통과했다는 뜻이다. native=0도 물리적 출력 완료나 RFID 보존을 확정하지 않는다. 원문 UID·vendor 출력은 보고하거나 Issue에 기록하지 않는다. native 예외/프로세스 종료 시 실제 인쇄 여부는 불명이며 호출 실패를 미출력으로 단정하지 않는다.

추가 재현 시험은 구현 전에 실패를 관측한 후 커밋했다. 관리 fixture는 최대 42-byte 응답, 실패 단계별 인쇄 차단, 반환 예외 후 해제를 검사하며 vendor 코드를 실행하지 않는다. CLI 검사는 명시적 단일 아키텍처와 메타데이터 allowlist를 검사한다. 이 검사를 실기 인쇄·한글·RFID 일치 증거로 확대하지 않는다.

2026-10-08, 응답 파서 수정 `10e38d9`에서 fresh verifier의 직접 프로젝트 Debug/Release × x86/x64 빌드·실행이 모두 exit 0이었다. 리뷰 지적에 따라 UID prefix를 변경 불가 문자열로 만들지 않고 byte 배열에서 직접 검사한다. 응답 코드 부분만 숫자 문법을 검사한 뒤 문자열로 변환한다.

사용자 요청으로 솔루션의 기존 Any CPU를 유지하고 x86/x64 구성을 보완했다. `6d6a2a7`의 일반 Release `verify-build`와 `a12c6d0`의 일반 Debug `verify-build --configuration Debug`는 모두 exit 0이며 각 x86/x64 재빌드·검증을 포함한다. 기존 MSB4126 실패는 이 구성 보완으로 해소했다. `a12c6d0`의 Any CPU solution도 Debug/Release 직접 Rebuild와 x86 검증 실행이 모두 exit 0이었다. DLL은 항상 AnyCPU이며 Any CPU solution의 검증 exe는 명시적 x86이다. 검증 프로젝트의 x86/x64 제한을 낮추지 않았다.

검증 근거는 `.work/foundation/Release-1791424552346/evidence.json`, `.work/foundation/Debug-1791424565633/evidence.json`, `.work/foundation/direct-solution-Release`, `.work/foundation/direct-solution-Debug`다. workingTreeDirty는 기존 Visual Studio 버전·속성 metadata를 보존한 상태를 반영한다. 해당 기존 metadata는 task 커밋에 포함하지 않았다. CLI 두 회귀 검사와 diff 검사는 exit 0이다. 이번 추가 검사에서는 vendor DLL을 로드하거나 장치·인쇄·RFID를 호출하지 않았다.

최초 진단 당시 supervisor의 관리 코드 기준은 `10e38d9`이며 fresh verifier와 root가 다음 Release 핀의 일치를 확인했다. 이후 기준은 아래 공통 DLL 서식 전달 보완 절을 따른다.

| 최초 일회 진단 산출물 | SHA-256 |
| --- | --- |
| DLL, x86/x64 공통 | `462ae54736b4e37afff76879aeaa12d49662b5c93335690b280ea633f7fef3f5` |
| x86 validation exe | `f6b64f7c2323ecabf47c26c5e75ffc8c7572719f666d2622b3185c6a15528bda` |
| x64 validation exe | `f9c033464b936d6050b60a743eec762e5485b17574fa9368f45f655e8f3d8e0d` |

위 준비 시점에는 SDK의 작업 루트 밖 로그 생성·추가 기록 허용 전까지 실기를 실행하지 않았다. 이후 최초 진단의 실제 결과는 아래와 같다. TASK-02 조건 3·5·6·7은 계속 열어 둔다. 문법 검사와 보호된 진단 준비가 생산 PrintContractUnverified 게이트를 해제하지 않는다.

## 최초 x86 라벨 진단: SDK 정상 응답, 실제 미출력

2026-10-08 11:01 KST, 사용자가 TASK-02 장치 시험 동안 SDK 자체 로그 생성·추가 기록을 명시 허용한 뒤 `8e8415c`에서 위 명령을 한 번 실행했다. 관리 코드 핀은 `10e38d9`이며 x86만 시도했다. 실행 전 두 아키텍처의 Release 핀 일치, 로그 대상의 reparse point 부재, task probe 잔여 프로세스 0을 확인했다. 로그 내용은 읽거나 복사하지 않았다.

| 단계 | 관측 결과 |
| --- | --- |
| 연결 | Pre=0, SetPort=0, Connect=0, Open=1 |
| 서식 | Form=0; Q25,3·W50·P1·C1, SDK copies=1 |
| 인쇄 진입·응답 | Invoked=1, response=parsed, native=0, prefix=empty, length=2 |
| 정리 | Disconnect raw=0, Closed=0 |
| 실행 수명 | child/supervisor exit 0, 실행 후 task probe 프로세스 0 |
| 물리적 출력 | 사용자가 출력 신호는 들어왔지만 라벨은 출력되지 않았다고 확인 |

근거는 `.work/native-load/1791424861968/evidence.json`이다. 반환 버퍼 문법 검사가 통과한 것과 물리적 출력 성공을 분리한다. 응답이 빨리 정상 반환했다는 사실로 장치가 출력을 완료했다고 판단하지 않는다. x64는 미시도이며 자동 재출력도 하지 않았다. 관리 경로의 RFID API 호출은 0이며 이 값을 vendor 내부 RF 계측 또는 저장 데이터 보존 증거로 해석하지 않는다.

Windows는 `BT-003 Pro`와 `USB Serial Port(COM3)`를 각각 열거했다. USB 관련 PnP 항목 하나의 service는 `usbprint`, COM3의 manufacturer/service는 FTDI/FTSER2K였다. 이 열거값은 케이블 역할이나 프린터 펌웨어 버전을 확정하지 않는다. 제조사 DLL 해시는 기존 고정 baseline 그대로다.

최초 응답만 확인한 시점에는 미출력 원인을 BTPL 프레임 변환·실제 전송 경로·라벨 배치·센서 설정에서 분석하기 시작했다. 당시 요청받은 50×25 mm·간격 3 mm는 실제 서식에 반영했다. 실물 사진을 확인하기 전에는 규격 누락이나 센서 오류를 원인으로 단정하지 않았다. 외부 리더·앱으로 출력 전후 저장 데이터를 비교할 수 있다는 사용자 확인을 받았으며, 이후 정상 출력 시험에서 실제 값 공개 없이 일치 여부를 기록한다. UID만 같은 것을 저장 데이터 보존으로 대체하지 않는다.

## 실물 사진과 두 로그 기록의 후속 분석

사용자가 제공한 실물 사진에는 흰색 라벨 사이의 넓은 빈 뒷종이와 반복되는 회색 띠, 라벨 경계 근처의 잘린 인쇄 흔적이 보인다. 원본 사진은 저장소·Issue에 복사하지 않았다. 사진에서 빈 구간은 흰색 라벨 높이와 비슷하게 보이며, 이를 일반 Gap 3 mm로 확정할 근거가 없다. 회색 띠가 뒷면 Black-Mark인지, 최초 3 mm가 라벨 틈인지 마크 폭인지는 실측·사용자 확인이 필요하다. 사진의 원근으로 정확한 mm를 산출하지 않는다.

BTPL 원문 10쪽 이미지에서 일반 Gap의 Q 인수는 라벨 길이와 실제 간격이고, Black-Mark는 마크 폭과 서식 시작점에 대한 부호 있는 offset을 추가로 다룸을 확인했다. 그림 2는 반복 마크와 서식 시작점 사이 관계도 보여 준다. 로컬 가이드의 G 명령은 반사/투과/자동 센서 선택이다. 현재 진단은 Q25,3으로 구성하고 센서 종류·마크 offset을 별도로 지정하지 않았다. 숫자를 전달한 것만으로 실제 미디어의 시작점과 주기가 검증된 것으로 처리하지 않는다.

원본 JODAL 서식은 W100·Q50,6과 AW의 x=100·y=250을 사용한다. 해당 큰 라벨용 설정을 현재 용지에 그대로 적용하면 규격과 원점이 맞는다고 보장할 수 없다. 사진의 XXX는 이번 고정 합성 서식에 들어 있지 않으며, 그 표시를 이번 인쇄 호출 횟수의 증거로 사용하지 않는다.

SDK 일일 로그에서는 자체 합성 요소를 두 번 확인했다. 첫 기록은 API 서식 등록 인수, 둘째는 SDK가 가공한 서식으로 구분된다. 가공된 자체 서식은 12행(빈 행 1개)·CRLF 기준 85 bytes·E 1개이며, 원문의 P1/R0가 빠지고 E0가 E0,0으로 바뀌었다. 임의 private 내용은 출력하거나 기록하지 않았다. 이 85 bytes는 텍스트 로그 크기이며 GDI 처리 후 실제 native 전송 버퍼 크기와 동일하다고 선언하지 않는다.

프로그램의 StartLabelPrint(1)은 1회이고 x64는 미시도다. 로그 두 서식 기록과 native buffer의 조각 전송 call site 두 개를 두 장 출력으로 해석하지 않는다. 자세한 x86 전송·로깅 경로 및 반환값의 한계는 [정적 분석 결과](START_LABEL_PRINT_ANALYSIS.md)를 따른다. 재출력 대신 흰색 라벨 크기·빈 뒷종이 간격·마크 유무와 위치를 확인한 뒤, 확정한 용지에 맞는 단일 서식으로 다시 검증한다. 한글·장수·RFID 전후 일치·펌웨어 조건은 계속 미확인이다.

### 실측값과 뒷면 Black-Mark 확인

사용자가 재측정한 현재 용지는 다음과 같다. 앞서의 50×25 mm·Gap 3 mm는 최초 시험에 적용한 과거 입력이며, 아래 실제 규격으로 대체한다. 뒷면 사진에서 검은 마크를 확인했지만 원본 사진은 저장소·Issue에 복사하지 않았다.

| 항목 | 확인 값 |
| --- | --- |
| 흰색 라벨 | 폭 60 mm·진행 방향 길이 20 mm |
| 라벨 끝부터 다음 라벨 시작까지 | 20 mm |
| 라벨 사이 구간 구성 | 흰 뒷종이 7.5 mm + 검은 마크 5 mm + 흰 뒷종이 7.5 mm |
| 반복 주기 | 20 + 20 = 40 mm |
| 뒷종이 전체 폭 | 70 mm |
| 인식 방식 | 뒷면 Black-Mark |

이 확인으로 최초 진단의 W50·Q25,3과 현재 미디어의 불일치는 확정되었다. 센서 기준점 및 반복 주기가 맞지 않으면 흰 라벨 밖 뒷종이에 인쇄하거나 경계에 걸릴 수 있다. 이를 유력 원인으로 두되, 아직 보정 후 출력으로 원인 제거를 검증하지 않았다.

다음 단일 진단은 Black-Mark 반복 주기 40 mm·마크 폭 5 mm를 기준으로 Q의 offset까지 다루고, 전체 뒷종이 기준 폭 70 mm와 흰 라벨 60 mm의 좌우 여백을 구분하는 후보로 준비한다. 반사 센서 G0의 해당 모델 적용과 SDK 가공 후 명령 보존도 확인해야 한다. 원문 BTPL의 센서/마크 offset과 SDK Registry 변경 함수 SetLabelSetting은 서로 다른 경로다. Registry를 대체 설정으로 쓰지 않는다.

사용자는 마크 앞·뒤 흰 뒷종이가 각각 7.5 mm라고 확정했고, 이 규격은 이번 시험에만 적용하며 공통 DLL에 고정하지 않도록 지시했다. 실측 거리를 BTPL offset과 동일시하기 전에 원문의 기준점·부호와 SDK의 명령 처리 경로를 확인해야 한다. `7600257` 작성 시점에는 후보의 관리 전달만 검사했다. 후속 단일 실기 결과는 문서 마지막 절을 따른다. 실제 한 장·한글·RFID 저장 데이터 전후 일치 조건은 계속 열어 둔다.

## 공통 DLL의 서식 전달 보완과 Black-Mark 후보 한계

`c163e24`에서 공통 DLL의 고정 시험 서식을 제거했다. 내부 `ProbeLabel(options, validatedForm)`은 검증 프로그램이 넘긴 서식을 `SetPrintFormString`에 전달한다. 시험용 용지 규격·센서 선택·합성 문구는 검증 프로젝트의 `LabelDiagnosticForms`에만 있다. 제품의 용지 설정은 호출자의 BTPL 서식에 속하며 이번 규격을 기본값으로 승계하지 않는다. DB 조회·DPI 설정·JSON의 전역 용지 규격을 추가하지 않았다.

검증 프로그램은 기존 `50x25gap3`와 새 `blackmark60x20` 두 고정 합성 후보만 선택한다. 새 후보는 `^Q40,5,7.5+`, `^W70`, `^G0`이며 아직 해당 SDK·펌웨어의 지원이 검증된 보정 서식이 아니다. CLI가 후보를 선택할 수 있다는 사실과 물리적 사용 가능 여부를 구분한다. 임의 원문·파일·DB 값을 진단 입력으로 받지 않는다. 내부 null/empty/NUL/길이 검사는 완전한 BTPL 검증기가 아니며, 공개 제품 인쇄 경로의 검증·게이트를 대신하지 않는다.

정적 분석에서 Q의 세 번째 값 처리에 구체적인 한계를 확인했다. x86 `^Q` literal과 3-field 분기를 대조했으며, 두 번째 값이 0이 아닌 경로는 세 번째 값을 내부 용지 metadata로 읽지 않는다. 후보의 `5`는 이 경로를 따른다. 원문이 가공 로그에 남더라도 GDI/raster 후 전송에 offset이 반영된다는 증거가 아니다. `^G0`의 처리와 모델 지원도 미확인이다. 상세 주소·소수 입력의 한계는 [정적 분석 결과](START_LABEL_PRINT_ANALYSIS.md)의 Black-Mark 절에 기록한다. 이 발견 때문에 후보를 보정 완료로 간주해 재출력하지 않았다. 다음 확인은 실제 전송 버퍼에 생성되는 용지/센서 설정과 원점 처리이며 Registry 변경이나 raw SendData로 우회하지 않는다.

fresh verifier가 `c163e24`에서 일반 `verify-build`의 Release/Debug × x86/x64 네 빌드·실행과 두 CLI 회귀 검사를 모두 exit 0으로 확인했다. 서로 다른 두 입력 서식의 전달, 기존 실패 단계별 인쇄 차단·연결 정리, 잘못된 서식의 callback 전 거부가 통과했다. 생산 `PrintContractUnverified` 게이트는 유지된다. 이 검증에서는 vendor DLL·장치·인쇄·RFID 함수를 실행하지 않았다. 기존 Visual Studio metadata 변경은 보존하고 task 커밋에서 제외했다.

11:35 AC 후보 진단의 관리 코드 핀은 `c163e24`이며 Release 산출물은 다음과 같다. 다음 AW 후보의 핀은 문서 마지막 절을 따른다. 핀 갱신은 실행 파일 식별이며 Black-Mark 후보의 실기 승인이나 완료 증거가 아니다.

| AC 후보 진단 산출물 | SHA-256 |
| --- | --- |
| DLL, x86/x64 공통 | `93bde4f28db91a90ed9f9d4058d916a61aa968ac903aa20f68dc1256cef15e95` |
| x86 validation exe | `2e3ce12f7f9dc869584a63c15e76ce79221ca081d4a6173fcbe89171373b924d` |
| x64 validation exe | `fea68e382e1152f092397a82ad0db59270f692db671cdeca6c62a79da2b61287` |

## Black-Mark 후보의 단일 실기: 응답 timeout, 이송·출력 없음

후속 정적 확인에서 Q가 이미 있을 때 원문 행을 보존하고, FONT/AUID/AW 외 일반 행에 CRLF를 붙여 전송 buffer에 append하는 경로를 확인했다. Q의 세 번째 값을 내부 metadata로 읽지 않는 관측을 프린터 전송 누락으로 확대하지 않는다. [추가 분석](START_LABEL_PRINT_ANALYSIS.md)의 후속 확인 절에 주소와 USB WriteFile·응답 대기 경로를 기록했다.

2026-10-08 11:35:28 KST, `7600257`에서 현재 관리 코드 핀 `c163e24`를 사용해 다음 명령을 한 번 실행했다. 실행 전 네 Release 산출물과 제조사 DLL 쌍의 해시 일치, SDK 로그 대상의 reparse point 부재, 기존 task probe 프로세스 0을 확인했다. SDK 로그 생성·추가 기록은 이전 사용자 허용 범위이며 재요청하지 않았다.

```text
node tools/probe-native-load.js --sdk-directory <SDK의 라이브러리 폴더> --connection usb --port 0 --label-test blackmark60x20 --architecture x86
```

| 항목 | 관측 결과 |
| --- | --- |
| 연결·서식 | Pre=0, SetPort=0, Connect=0, Open=1, Form=0 |
| 요청 | StartLabelPrint(1) 1회, Q40,5,7.5+·W70·G0·P1·C1, 합성 ASCII U2BIO TEST |
| 응답 | response=parsed, native=101, prefix=empty, length=4 |
| 정리·종료 | Disconnect raw=0, Closed=0; child exit 11, supervisor exit 1, 잔여 task probe 0 |
| 물리적 결과 | 사용자가 용지 움직임과 출력 모두 없음을 확인 |

근거는 `.work/native-load/1791426928556/evidence.json`이다. Rev.1.3의 오류표에서 101은 Response timeout이다. host 제한시간 초과가 아니라 SDK가 반환한 응답 오류이며 보호된 응답 문법 검사 자체는 통과했다. 오류를 성공으로 바꾸지 않았고 x64 또는 같은 서식으로 재시도하지 않았다. 이 시험에서 관리 코드의 RFID API 호출은 0이며 저장 데이터 보존을 검증한 것은 아니다.

Windows에서 BT-003 Pro의 USB 관련 두 PnP 항목과 FTDI COM3은 OK 상태로 열거됐다. 해당 이름에 앞쪽 공백이 있어 exact-name 조회가 누락될 수 있으며 열거 결과를 연결·출력 성공으로 대체하지 않는다. USB 출력 경로는 SDK 내부 WriteFile로 연결되고 Open은 handle 보유 검사다. 사용자는 본체 FEED가 정상 이송되고 오류 표시가 없음을 확인했다. 따라서 용지 규격만으로 현재 무이송 원인을 확정하지 않는다. SDK 로그의 최근 자체 합성 요소를 기준으로 Q40,5,7.5+·W70·G0·L의 보존도 확인했으나 실제 프린터 수신 증거로 사용하지 않는다. raw 로그와 다른 데이터는 출력·복사하지 않았다.

다음 진단은 동일 용지 후보에서 텍스트 명령만 제조사 샘플의 AW 형태로 바꾸어 native AC와 SDK의 AW 그림 변환 경로를 구분한다. AC 미지원이 원인으로 확인된 것은 아니며 AW 인수의 보편 계약도 확정하지 않는다. 합성 문구만 사용하고 위치/센서/용지 설정을 동시에 바꾸지 않는다.

추가 USB 경로 분석에서 SDK가 `SetPrinterPort` 중 사용자별 USB 연결 캐시를 삭제·재등록하는 별도 효과를 확인했다. 대상은 `HKCU\Software\BitekTechnology\AXBT003`의 USBn 값이다. 현재 USB 프린터 열거 수와 캐시 값은 각각 1이며 USB1의 vendor VID/PID·USB printer interface class뿐 아니라 활성 프린터 instance와의 일치도 내용 공개 없이 확인했다. 정확한 주소와 Registry API는 [정적 분석](START_LABEL_PRINT_ANALYSIS.md)의 USB 캐시 절을 따른다. 이 효과는 SDK 자체 로그 기록 허용과 별도이며, 발견 뒤 현재 USB1 삭제·재등록/갱신에 대한 명시 허용 전까지 추가 native 호출을 보류했다. 이후 사용자가 TASK-02 동안 현재 USB1 캐시 갱신을 명시 허용했다. root에서 Registry를 수동 수정하거나 SetLabelSetting을 호출하지 않았다. TASK-02 조건 3·5·6·7과 TASK-03 착수는 계속 열어 둔다.

## AW 비교 후보 준비

`b7aefd2`에서 AW 기대 서식의 관리 검사 실패(exit 1)를 먼저 관측하고 `d7a07ab`에서 검증 프로그램의 시험 텍스트를 `AW,80,24,10,2,0,0,U2BIO TEST`로 변경했다. 용지·센서·좌표·합성 문구·copies=1·공통 DLL은 그대로다. 기존 두 서식 전달과 단계별 실패·정리 검사를 보존했다. 제품에 글꼴 지원이나 실제 출력 성공을 선언한 변경이 아니다.

fresh verifier가 일반 Release/Debug × x86/x64 네 빌드·검증 실행, doctor·repository·diff 검사를 모두 exit 0으로 확인했다. Release evidence는 `.work/foundation/Release-1791427594118/evidence.json`이며 root의 두 CLI 회귀 검사도 exit 0이다. vendor/device 함수는 실행하지 않았다. supervisor는 다음 Release 파일을 `d7a07ab` 기준으로 고정했으며 root가 실제 파일의 해시를 대조했다.

| AW Y24 후보 진단 산출물 | SHA-256 |
| --- | --- |
| DLL, x86/x64 공통 | `93bde4f28db91a90ed9f9d4058d916a61aa968ac903aa20f68dc1256cef15e95` |
| x86 validation exe | `a568ba12d17d6c3c820d7ed81a323643fcdc67e3cd293e3294ce9e6a791ffbfe` |
| x64 validation exe | `f3268f0c54030a72f43b6d1e3ca04b33776dd939bdf8bb5f3363efc86f52a0ea` |

실기 명령은 위 blackmark60x20과 같지만 이 핀에서는 AW 후보가 선택된다. 명령 선택 이름이 같아도 실행 커밋·해시·텍스트 명령으로 두 실험을 구분한다. 허용 후 실제 호출 결과는 아래와 같다. 다른 USB cache 값이 존재하거나 활성 장치가 바뀌면 현재 한 값의 허용으로 확대하지 않는다.

## AW Y24 후보 단일 실기: SDK 정상 응답, 한 장 배출·문구 없음

사용자가 TASK-02 동안 현재 USB1 캐시 갱신을 명시 허용한 뒤, 2026-10-08 12:28:03 KST에 `c5c27f5`에서 관리 핀 `d7a07ab`으로 USB 0·x86의 AW 후보를 한 번 요청했다. 재연결한 BT-003 Pro USB와 COM3은 Windows에서 OK로 열거됐으며, 실행 전 캐시 USB1/USB print 열거 각각 1개와 활성 instance의 일치, 네 Release 해시, SDK 로그 대상 링크 부재, 잔여 probe 0을 확인했다. 기존 SDK 자체 로그 허용도 유효하다.

| 항목 | 관측 결과 |
| --- | --- |
| 연결·서식 | Pre=0, SetPort=0, Connect=0, Open=1, Form=0 |
| 요청 | StartLabelPrint(1) 1회; Q40,5,7.5+·W70·G0·P1·C1; AW,80,24,10,2,0,0,U2BIO TEST |
| 응답 | response=parsed, native=0, prefix=empty, length=2 |
| 정리·종료 | Disconnect raw=0, Closed=0; child/supervisor exit 0, 잔여 task probe 0 |
| 캐시 | 호출 후 USBn 값 1개, USB1 존재; 다른 값의 갱신 범위로 확대하지 않음 |
| 물리적 결과 | 최초 사용자 확인: 한 장 배출, 문구/잉크 흔적 없음. 이후 당겨서 확인한 확대 사진에서 윗경계 밖 일부 잉크 흔적 발견 |

근거는 `.work/native-load/1791430083799/evidence.json`이다. 사용자가 제공한 후속 사진에는 한 장의 흰 라벨이 배출됐고 원래 회색 RFID 모양만 보인다. 새 문구 출력 성공으로 처리하지 않는다. 원본 사진은 저장소·Issue에 복사하지 않았다. x64와 자동 재출력은 시도하지 않았다. 코드 0과 한 장 배출만으로 글꼴 지원·한글·RFID 저장 데이터 보존을 완료 처리하지 않는다. AW와 AC의 결과 차이에는 앞선 케이블 재연결도 포함되므로 글꼴만을 단독 원인으로 확정하지 않는다. 실제 값이나 원문 vendor 로그를 수집·게시하지 않는다. TASK-02 조건 3·5·6·7은 계속 열어 둔다.

후속 정적 대조에서 AW는 Windows GDI로 글자를 그린 후 Q 그림 명령과 bitmap bytes로 변환된다. 이번 회전 0·정렬 인수 0 경로의 x/y는 변경하지 않고 그림 헤더로 전달하므로, 확인하지 않은 다른 회전/정렬의 좌표 보정 분기를 이번 원인으로 사용하지 않는다. 그림 픽셀의 실제 생성·프린터 수신과 현재 원점·인쇄 모드·소모품 상태는 별도 검증 대상이다. 로컬 제조사 제품 자료는 열전사와 감열 두 인쇄 방식을 명시한다. 사용자는 리본 장착·열전사 모드라고 확인했다. 방식을 임의 변경하지 않았다. DPI 설정·텍스트 넘침 검사·SDK 패치·raw 전송을 추가하지 않았다.

### 확대 사진의 잉크 흔적과 Y120 비교 후보

사용자가 용지를 당겨 확인한 추가 사진에서 흰 라벨 윗경계 위에 잘린 글자처럼 보이는 잉크 흔적을 확인했다. 앞선 잉크 없음 관찰은 이 후속 증거로 보완한다. 흔적만으로 전체 문구나 인쇄 요청의 정확한 발생 시점을 식별하지 않으며 리본·헤드·그림 변환이 모두 정상이라고 확정하지 않는다. 현재는 원점/세로 배치 오류를 우선 가설로 두고 비교한다. 원본 사진은 복사·게시하지 않았다.

`7083395`에서 기대 서식을 Y120으로 바꿔 기존 구현의 관리 검사 실패(exit 1)를 관측한 뒤 `878a8e1`에서 검증 프로그램의 AW 행만 `AW,80,120,10,2,0,0,U2BIO TEST`로 변경했다. X·용지·마크·센서·인쇄 방식·농도·글꼴·copies는 그대로다. 숫자 120은 이번 비교용 dot 좌표이며 물리적 mm 위치 또는 모든 라벨의 기본값이 아니다. 공통 DLL은 바꾸지 않았다. root 일반 Release x86/x64 검사와 fresh verifier의 Release/Debug 네 빌드·실행, 두 CLI 회귀, doctor/diff는 모두 exit 0이다. Release evidence는 `.work/foundation/Release-1791430722987/evidence.json`이다.

사용자는 FEED 이송은 되지만 흰 라벨이 기대만큼 밖으로 나오지 않으며 calibration 후에도 같은 위치라고 설명했다. 본체 출구와 인쇄 헤드 위치를 같은 기준점으로 간주하지 않는다. 이 관측은 미디어 원점이 정상이라고 확정한 결과가 아니며, 같은 서식을 반복하거나 추가 설정을 바꾸지 않고 Y 변경의 위치 차이를 한 번 비교한다.

supervisor의 현재 핀은 `878a8e1`이며 fresh verifier와 root가 다음 실제 Release 해시를 대조했다. 이 후보의 실기 실행 전 준비 상태와 실제 결과를 구분한다.

| AW Y120 후보 진단 산출물 | SHA-256 |
| --- | --- |
| DLL, x86/x64 공통 | `93bde4f28db91a90ed9f9d4058d916a61aa968ac903aa20f68dc1256cef15e95` |
| x86 validation exe | `99ad0d7c0a6a49991800a03a4f16fa9638de4dd9b0754d0d4a8fddd8696fab42` |
| x64 validation exe | `160a827c6cc68be72137cac75763fd1a466c41443dfa4f8e25312c387a94ebaf` |

### Y120 단일 실기

2026-10-08 12:42:20 KST, `013b4bd`에서 관리 핀 `878a8e1`로 USB 0·x86의 Y120 후보를 한 번 요청했다. 현재 USB1/USB print 열거 각각 1개와 활성 instance 일치, SDK 로그 대상 링크 부재, 기존 probe 0을 실행 전에 확인했다. 현재 USB1 캐시 갱신과 SDK 로그 기록 허용은 유효하며 재요청하지 않았다.

Pre=0, SetPort=0, Connect=0, Open=1, Form=0, Invoked=1, response=parsed, native=0, prefix=empty, length=2, Disconnect raw=0, Closed=0이었다. child/supervisor exit 0, 잔여 task probe 0이며 근거는 `.work/native-load/1791430940971/evidence.json`이다. x64와 자동 재출력은 하지 않았다.

사용자는 문구가 흰 라벨 안에 출력됐지만 충분히 배출되지 않아 직접 당겨서 확인했다고 답했다. 후속 사진에서도 흰 라벨 왼쪽 위의 `U2BIO TEST`가 선명하게 보인다. 따라서 이번 x86·AW·합성 ASCII 조합의 실제 문구 출력은 확인됐다. 인쇄 후 배출은 미해결이며, API 한 번 호출과 실제 수량 계약 전체를 구분한다. 원본 사진은 복사·게시하지 않는다. Y120은 이번 시험 서식에만 적용되며 공통 DLL의 기본값으로 넣지 않는다. 이전 시험 사이에 본체 calibration도 있었으므로 Y 변경만을 모든 원인의 단독 해결로 확대하지 않는다.

배출 문제에는 Y를 더 옮기는 대신 정지·이송 경로를 확인한다. [정적 분석](START_LABEL_PRINT_ANALYSIS.md)의 `^E` 절에서 현재 SDK의 명령 재작성과 내부 대기시간을 구분했다. RFID 인쇄 함수의 후처리를 인쇄 전용 경로에 적용하거나, 거리와 대기시간을 혼동해 `^E`를 변경하지 않는다. 추가 인쇄·이송·설정 변경은 이 확인 과정에서 실행하지 않았다. TASK-02 조건 3·5·6·7은 한글/글꼴·x64 인쇄·RFID 저장 데이터 전후 일치·펌웨어와 응답 계약의 남은 증거가 필요하므로 계속 열어 둔다.

## Black-Mark 7.5 mm 인수 제외 비교 준비

사용자는 Black-Mark를 유지하고 Gap 방식은 사용하지 않으며, 7.5 mm 위치 후보를 제거하여 시험하도록 지정했다. `2d7b6ec`에서 기대 Q 행을 `^Q40,5`로 바꾼 관리 검사의 실패(exit 1)를 확인한 뒤 `72446e7`에서 시험 서식의 `^Q40,5,7.5+`만 `^Q40,5`로 바꿨다. `^G0`, W70, AW80,120, 속도·농도·수량·정지 옵션과 공통 DLL은 유지했다. 이는 이번 시험 입력이며 제품 기본값이나 배출 보정 완료가 아니다.

fresh verifier의 일반 Release/Debug × x86/x64 빌드·관리 검증, 두 CLI 회귀와 diff 검사가 모두 exit 0이다. Release evidence는 `.work/foundation/Release-1791433645194/evidence.json`이다. vendor/device 함수를 실행하지 않은 관리 검증이며 실기 결과는 아래에 별도로 기록한다. supervisor의 관리 핀은 `72446e7`로 갱신했다.

| 7.5 인수 제외 진단 산출물 | SHA-256 |
| --- | --- |
| DLL, x86/x64 공통 | `93bde4f28db91a90ed9f9d4058d916a61aa968ac903aa20f68dc1256cef15e95` |
| x86 validation exe | `3c4d88d28dbd82322c85507f72fb257cee3ef4dbf9e321a458f59788a511c016` |
| x64 validation exe | `9517c91144780841d67f5041c20c257745093b5b287e10599e310dde09747ff1` |

### 7.5 인수 제외 단일 실기

2026-10-08 13:29:55 KST, `4bea181`에서 관리 핀 `72446e7`로 USB 0·x86의 후보를 한 번 요청했다. 실행 전 네 Release 핀 일치, USB1 캐시/USB print 각각 1개와 활성 instance 일치, BT-003 USB·COM3 열거 정상, task probe 0, SDK 로그 대상 링크 부재를 확인했다. 기존 USB1 갱신·SDK 로그 기록 허용은 유효하다.

| 항목 | 관측 결과 |
| --- | --- |
| 연결·서식 | Pre=0, SetPort=0, Connect=0, Open=1, Form=0 |
| 요청 | StartLabelPrint(1) 1회; Q40,5·W70·G0·P1·C1; AW,80,120,10,2,0,0,U2BIO TEST |
| 응답 | response=parsed, native=0, prefix=empty, length=2 |
| 정리·종료 | Disconnect raw=0, Closed=0; child/supervisor exit 0, 잔여 task probe 0 |
| 물리적 결과 | 사용자에게 문구·위치·배출·실제 장수 확인 요청; 답변 대기 |

근거는 `.work/native-load/1791433795342/evidence.json`이다. 이전 Q40,5,7.5+ 시험과 이번 Q40,5 시험을 구분한다. 정상 응답만으로 7.5 제거가 배출 문제를 해결했다고 처리하지 않는다. 자동 재출력·x64·추가 이송은 실행하지 않았으며 TASK-02의 남은 조건과 RFID 저장 데이터 보존도 완료 처리하지 않는다.

## 사용자 정상 서식 반영과 직접전송 비교

사용자가 HFSample_UL에서 정상 출력·완전 배출됐다고 보고한 두 서식의 명령·순서를 그대로 반영했다. 기존 blackmark60x20은 AW, 신규 blackmark60x20ac는 AC를 StartLabelPrint로 전달한다. 별도 blackmark60x20direct는 동일 AC 서식을 SendData로 한 번 전달한다. 어느 선택도 공통 DLL의 용지·좌표·농도 기본값이 아니다. 50x25gap3 시험 데이터는 보존했다.

현재 샘플 소스가 원래 StartPrintWithRfid 루프에서 SendData 루프로 달라진 점을 재확인했다. 원인 분석은 [START_LABEL_PRINT_ANALYSIS.md](START_LABEL_PRINT_ANALYSIS.md)의 현재 샘플 절을 따른다. 현재 소스는 직접전송 AW를 거부하므로 사용자 보고만으로 두 글꼴의 모든 실행 경로를 검증 완료로 처리하지 않는다.

직접전송 진단은 기존 process/target 예약·worker·연결 정리를 재사용한다. ASCII·AW 거부는 native load/callback 전에 적용하며 고정 CLI recipe만 허용한다. SendData를 한 번 호출하고 ResetForm/SetPrintForm/StartLabelPrint/RFID API는 호출하지 않는다. 보고는 DIRECT_LABEL·send·invoked·completion=unknown·printApiCalls=0·rfidApiCalls=0만 추가한다. SDK0과 정상 연결 정리를 실기 인쇄 성공으로 선언하지 않으며 기존 제품 PrintContractUnverified gate는 유지한다.

79e8ec1에서 새 서식 기대값과 CLI의 기존 구현 실패를 확인하고 dfd5057에서 두 서식/선택을 반영했다. 직접전송 추가의 기대 검사도 2d5e754에서 exit1을 관측한 후 5c44e12에서 구현했다. C#7.3 nullable 검사식의 초기 컴파일 오류는 2d5e754에서 수정했다. 최종 관리 검증과 새 산출물 핀은 아래 결과를 따른다.

fresh verifier의 Release/Debug × x86/x64 빌드·관리 검증과 두 CLI 회귀·diff 검사가 모두 통과했다. 고정 서식 일치, ASCII/AW 사전 거부, 직접전송 1회·연결 정리·실패 반환, 기존 LABEL과 제품 게이트 보존을 확인했다. focused review에서 추가 결함은 보고되지 않았다. Release evidence는 `.work/foundation/Release-1791437277643/evidence.json`이며 관리 핀은 5c44e12다. verifier는 vendor/device를 실행하지 않았다.

| 사용자 서식/직접전송 진단 산출물 | SHA-256 |
| --- | --- |
| DLL, x86/x64 공통 | `098f0820c20a880112d63d09e8afa14d76585d0157984e7ea14db3da10d1b331` |
| x86 validation exe | `50e4e84e6a2291d8690e202dbba3b3c695eb435d33f4bef2cef433478a6b800d` |
| x64 validation exe | `945b7bc50ea2790c439def402288c597f8350ff5e1f039b48d5eeaa2100aa4a8` |

### 사용자 AC 서식의 단일 직접전송 실기

2026-10-08 14:30:37 KST, 7702cb5에서 관리 핀5c44e12로 USB0·x86·blackmark60x20direct를 한 번 실행했다. 실행 전 현재 USB1 캐시만 존재하며 USB print 1개와 활성 instance 일치, USB/COM3 정상 열거, task probe0, SDK 로그 대상 링크 부재, 네 Release 핀 일치를 확인했다. 기존 USB1 갱신·SDK 로그 허용 범위에서 수행했다.

DIRECT_LABEL 상태 Returned, pre0/setport0/connect0/open1/disconnect0/closed0/send0/invoked1, completion=unknown, printApiCalls0, rfidApiCalls0이었다. child/supervisor exit0, 잔여 task probe0이며 근거는 `.work/native-load/1791437437846/evidence.json`이다. 사용자 AC 서식의 Q/W/G/E7/S5/H19/P1/C1/L·AC80,120,1,1,0,0,U2BIO TEST2·E를 CRLF로 원문 전달했다. 실제 문구·배출·장수는 사용자에게 확인 요청했으며 답변 대기다. SendData0을 실제 전송/인쇄 완료로 판정하지 않는다. 추가 이송·자동 재출력·x64·RFID 함수는 실행하지 않았다.

## 후속 방향: AW 기준

사용자는 속도·농도의 영향이 크지 않았으며 AW를 사용할 것으로 확정했다. 현재 blackmark60x20 진단은 이미 제공된 AW 서식과 인수를 그대로 사용하는 상태다. 이후 기준은 이 AW 선택이며 AC 직접전송의 결과를 제품 인쇄 성공·AW 지원의 근거로 대체하지 않는다. 기존 AC 비교 코드와 이력은 보존하되 추가 AC 시험을 진행하지 않는다. SDK의 AW 처리와 정지·배출 경로를 우선 분석한다. 이번 방향 확정은 문서 변경만이며 새 빌드·장치 호출·출력은 수행하지 않았다.

## 최종 AW 시험 서식: R0·E15

사용자는 다음 서식을 사용하도록 지정했다. 후속 답변에서 실제 정상 시험은 AC였고 저장소에서 AW를 검증하기 위해 바꾼 것이라고 설명했다. 따라서 이 AW 서식은 최종 검증 목표이며 이미 물리 출력·배출이 해결된 AW의 완료 증거가 아니다. 원래 해결 보고의 해석은 이 설명으로 정정한다.

```btpl
^Q40,5,7.5+
^W70
^G0
^S5
^H19
^P1
^C1
^R0
^E15
^L
AW,80,120,1,1,0,0,U2BIO TEST2
E
```

73154bc에서 기대 서식을 수정하여 기존 구현의 검사 실패(exit1)를 관측한 뒤 d9d45fb에서 blackmark60x20의 AW 설정만 제공 순서·R0·E15로 반영했다. CRLF와 마지막 E 뒤 CRLF도 포함한다. 과거 AC/직접전송 비교·50x25 서식과 공통 DLL은 바꾸지 않았다. 서식/호출 순서·실패/응답/연결 정리 회귀 검사는 보존했다.

구문 대조에서 Q/W/G/S/H/P/C/R/E가 ^L 앞에 있고, AW에 X/Y 뒤 네 인수와 payload가 있으며 종료 E가 하나임을 확인했다. 중복 수량/정지 명령·RFID 기록/잠금/안테나 명령은 없다. 텍스트 넘침·DPI 변환은 수행하지 않는다. P1/C1 및 SDK copies1은 한 장 의도이며 반복/일괄 수량 전체의 검증을 대신하지 않는다.

E7→E15와 R0 추가 중 R은 가로 여백이고 E는 정지 위치 관련 설정이므로, 사용자 해결 결과는 정지 위치가 배출 문제에 관련됐다는 판단을 뒷받침한다. 속도·농도는 동일하며 사용자 보고상 영향이 작았다. SDK의 E 재작성과 실제 실행 프로그램의 호출 경로는 별개 확인 항목이다. DLL 전송 문자열이 정확히 E15라는 관리 검증을 firmware 수신·동일 배출의 확인으로 확대하지 않는다. 15와 이번 용지 규격은 공통 DLL 전역 기본값으로 넣지 않는다. 설계의 명시 E15 보존과 기존 E0-only 설명의 불일치도 정리했다.

최종 서식의 구성 보존을 위해 설계의 미지정 D0/O0 자동 생성 규칙도 제거했다. 명시된 D/O는 기존처럼 0만 허용하고 현재 장치가 인쇄 전용으로 준비됐다는 사전조건을 유지한다. 정상 서식에 없던 설정을 뒤에서 추가해 같은 조합이라고 판정하지 않는다. 아직 구현하지 않은 TASK-03 정규화 계약의 보완이며 현재 공통 DLL이나 실패 게이트를 바꾼 것은 아니다.

fresh verifier의 Release/Debug x86/x64 빌드·관리 실행, 두 CLI 회귀·diff는 exit0이다. Release evidence는 `.work/foundation/Release-1791440670323/evidence.json`이고 관리 핀은 d9d45fb다. 정확한 최종 AW 서식, 기존 AC 비교 보존, 호출 순서·오류·연결 정리·제품 게이트를 확인했다. 제조사 SDK와 장치는 실행하지 않았으며 root의 추가 물리 출력도 하지 않았다.

| 최종 AW E15 진단 산출물 | SHA-256 |
| --- | --- |
| DLL, x86/x64 공통 | `098f0820c20a880112d63d09e8afa14d76585d0157984e7ea14db3da10d1b331` |
| x86 validation exe | `554c5ab004b3cceb4c9af3ce63c6fc9c272bd5c59b28a2e36894bad8f1af94c7` |
| x64 validation exe | `52592c453d2913dfacb1cf7753b9433dbdfc53f32d4ef9704128a20a49194d96` |

## TASK-02 남은 검증 순서

현재 원격 조건1·2·4는 완료이고 조건3·5·6·7은 남아 있다. 이 순서는 새로운 기능을 추가하기 위한 것이 아니라 AW 인쇄의 실제 경로·응답·ABI를 닫는 절차다.

1. 최종 AW ASCII 서식의 x64 한 장 요청은 아래와 같이 수행했다. 물리 결과가 비정상/불명이므로 응답0을 성공으로 처리하지 않는다. 실제 문구·위치·배출·장수 설명에 따라 E 재작성·AW 처리·호출 경로 중 차이를 보완한다. AC 성공과 혼동하지 않고 자동 반복하지 않는다.
2. x64의 정상 조합을 확인한 뒤 x86 동일 조합을 한 번 확인한다. 이어 같은 AW 조합에서 합성 한글을 검증하고 ANSI 전달과 글꼴 범위를 기록한다. 서식과 아키텍처를 동시에 바꾸지 않는다.
3. 원형·StdCall·입출력 방향·NUL·인코딩과 반환 버퍼 상한, 정상/오류/불명 응답 의미를 고정 SDK별로 정리한다. 관리 검사·42byte 후보·정상0 하나를 제조사 최대 쓰기 보장이나 모든 완료 경로로 대체하지 않는다. 제공 자료와 정적 분석으로 닫히지 않는 정확한 계약만 제조사 확인 대상으로 남긴다.
4. 기존 연결/배포/응답 음성 검사와 실제 장치 근거를 함께 재리뷰한다. 이미 통과한 검사를 무관하게 반복하지 않는다. 펌웨어는 사용자 답변대로 미확인으로 기록한다. 현재 사용자 지시에 따라 RFID 리더/앱·새 태깅 시험은 하지 않고 인쇄 경로의 RFID API/BTPL 기록 명령 미사용을 확인한다. 저장 데이터 전후 측정을 수행했다고 주장하지 않는다. 이 범위와 원격 조건6의 실측 비교 문구는 종료 전 일치시켜야 한다.
5. 미확정 핵심 계약을 해결한 뒤 근거·제한·커밋을 이슈에 기록하고 충족한 조건만 체크하여 TASK-02를 종료한다. TASK-03은 그 후 시작한다.

## 최종 AW E15의 DLL x64 실기 결과

2026-10-08 15:50:46 KST, HEAD207747d에서 관리 핀 d9d45fb로 다음 진단을 한 번 실행했다. 현재 USB1만 존재하고 USB print1과 활성 instance가 일치하며 COM3 열거 정상, task probe0, SDK 로그 대상 링크 부재, 네 관리 Release 핀 일치를 실행 전에 확인했다. 기존 TASK-02 USB1 갱신·SDK 로그 기록 허용 범위다.

```text
node tools/probe-native-load.js --sdk-directory '..\BT-003 pro SDK\라이브러리' --connection usb --port 0 --label-test blackmark60x20 --architecture x64
```

근거는 `.work/native-load/1791442246611/evidence.json`이다. x64 결과 하나, form0·StartLabelPrint(1) 호출 경로·response parsed·native0·empty prefix·length2·disconnect0·closed0·RFID API calls0이며 supervisor exit0, 잔여 probe0이다. native0은 물리 성공 판정이 아니다. 사용자는 문구가 없거나 출력이 이상하다고 답했고 세부 상태 설명은 아직 받지 못했다. x86·한글·추가 FEED·자동 재출력은 하지 않았다.

후속 사용자 로그에서 입력 E15가 SDK 가공 기록의 E0,0으로 변경된 것을 확인했다. 두 기록은 서식 저장과 내부 가공으로 구분하며 두 번 인쇄 요청의 증거가 아니다. 관리 호출·supervisor에는 retry2 또는 자동 재출력이 없다. 자세한 판정과 잔여 한계는 [로그 분석](START_LABEL_PRINT_ANALYSIS.md)의 15:50 x64 절을 따른다. 현재 TASK-02는 OPEN이며 정상 AW 인쇄·완전 배출 및 남은 응답 계약 확인 전 TASK-03에 착수하지 않는다.

## AW 크기 단독 비교: 문구 확인·배출 부족

사용자 순서 진행에 따라 c2a26e1에서 AW size10의 기대 검사 실패(exit1)를 확인한 뒤9f0b97c에서 시험 factory의 size만1→10으로 바꿨다. Q/W/G·X/Y·weight1·R0/E15·속도·농도·copies1·AC 비교 데이터는 유지했다. 공통 DLL에 글자 크기를 고정하지 않았다.

fresh verifier의 Release/Debug x86/x64 빌드·관리 검사, 두 CLI 검사·doctor·diff는 exit0이다. 근거는 `.work/foundation/Release-1791446111905/evidence.json`이며9f0b97c 기준이다.0ceb022에서 supervisor 핀을 갱신하고 root의 Release와 CLI 검사를 다시 통과했다.

2026-10-08 16:57:04 KST, HEAD0ceb022·관리 핀9f0b97c에서 USB0·x64의 blackmark60x20을 한 번 요청했다. 실제 AW 행은 `AW,80,120,10,1,0,0,U2BIO TEST2`다. 실행 전 USB1 캐시만 존재·현재 장치 일치·현재 연결된 USB printer1·COM3 정상·task probe/샘플 프로세스0·SDK 로그 대상 링크 부재를 확인했다. HKLM의 동일 모델 하위 세 항목 수를 현재 연결 수로 해석하지 않고 PresentOnly 결과로 구분했다. 기존 사용자 SDK 로그/USB1 갱신 허용 범위다.

근거는 `.work/native-load/1791446224472/evidence.json`이다. form0·StartLabelPrint copies1 한 번·native0·prefix empty·length2·closed0·RFID API0, supervisor exit0·잔여 probe0이다. 사용자는 **문구 보임, 배출 부족**으로 확인했다. 이 비교는 size1의 작은 글자와 배출 문제를 분리하는 근거다. 실제 장수·한글·모든 글꼴 지원·저장 데이터 보존 실측을 완료했다고 확대하지 않는다. 자동 재출력·x86 시험·RFID 발행은 하지 않았다.

## wrapper의 전진 제어 단독 진단

5f62243에서 present15 fixture·CLI 기대 검사 실패를 확인한 뒤c3b0f5b에서 고정 제어 진단을 추가했다. payload는 `^M15\r\n` 한 행이며 서식 본문·문구·E 인쇄 트리거가 없다. 검증 프로젝트 factory에서만 시험 거리를 정의한다. 공통 DLL은 기존 내부 ProbeDirectLabel → RunConnectionCore → SDK SendData 호출을 사용하고 연결을 정리한다. ResetForm·SetPrintFormString·StartLabelPrint·RFID API는 이 제어 경로에서 호출하지 않는다. 임의 원문/거리/파일을 받는 공개 API를 추가하지 않았다.

이는 사용자 원문 템플릿 전체를 그대로 보내는 인쇄 방식으로 전환하는 것이 아니다. AW 인쇄는 기존 wrapper 경로를 유지하며, 이미 인쇄한 라벨의 배출을 확인하기 위한 별도 SDK 제어 호출이다. SDK0은 이송 완료를 보증하지 않으므로 report의 completion은 unknown이다. supervisor evidence는 presentation-diagnostic·printApiCalls0으로 구분한다. 현재 제품 GetFunction의 PrintContractUnverified 게이트는 유지한다.

root Release x86/x64·CLI는 통과했다. 실행 전 fresh verifier의 핀·검사와 현재 USB1 범위를 확인한다. 추가 인쇄 없이 전진만 한 번 요청하고 사용자 이송 관측을 받는다. 다음 인쇄의 원점 복귀와 반복 작업에서의 정렬은 별도 검증 대상으로 남긴다. M15를 모든 라벨의 기본값으로 하드코딩하거나 단일 이송을 완성된 Print-and-Present 제품 계약으로 선언하지 않는다.

fresh verifier의 Release/Debug x86/x64·CLI·doctor·diff는 exit0이다. Release 근거는 `.work/foundation/Release-1791446559994/evidence.json`이며 c3b0f5b 기준이다.0558ec2에서 핀을 갱신하고 root의 Release 검사도 통과했다. 새 핀의 DLL 해시는7e8399ea864cf412d42a84496fb1958afd2be52baeeee79790d22e5bce229be0, x86 exe40bb074d228f6e753daf502f5d750673576359b7cf5ba33b21fe2eefd0d14804, x64 exeff12f7d296f280a3681daaa1e5c323ab62c84e67a492ad36a0150057e32bbf0c이다. 공통 DLL은 진단 주석만 바뀌었으며 제품 인쇄 동작을 새로 구현한 것이 아니다.

2026-10-08 17:05:09 KST, HEAD0558ec2·관리 핀 c3b0f5b로 USB0·x64 present15를 한 번 실행했다. 이전과 같은 현재 USB1/활성 장치/PresentOnly USB printer1·로그 링크 부재·샘플/probe0 범위와 사용자 허용을 확인했다. 근거는 `.work/native-load/1791446709142/evidence.json`이다. send0·closed0·printApiCalls0·RFID API0·completion unknown, supervisor exit0·잔여 probe0이다. 사용자는 **전진했고 라벨을 확인할 수 있음**으로 확인했다. 인쇄 트리거가 없으므로 새 인쇄를 요청하지 않았다.

전진의 물리 효과 확인 뒤21c2ca2에서 복귀 기대 검사 실패(exit1)를 관측하고7d57856에서 return15의 고정 `^B15\r\n`을 같은 SDK wrapper 제어 경로에 추가했다. 현재 전진량의 역방향 비교이며 공통 DLL 기본값/자동 보정이 아니다. 복귀·다음 인쇄 정렬을 확인하기 전 x86/한글/일괄 인쇄로 확대하지 않는다. 복귀 단독 요청의 실제 결과는 아직 미확인이다.

fresh verifier의 Release/Debug x86/x64·두 CLI·repository/doctor·diff는 통과했다. 근거는 `.work/foundation/Release-1791446955062/evidence.json`이며7d57856 기준이다. c581f92에서 supervisor 핀을 갱신했고 root의 Release 검사도 통과했다. DLL 해시는7e8399ea864cf412d42a84496fb1958afd2be52baeeee79790d22e5bce229be0, x86 exea94ebb252fe8eb45fd152beaf5f71a3625b43c5772843fae53ffce680670b10a, x64 exe424eac7dada95810e7577fb3f5c5faf1c3d216d008d4b16f050fcd984d3a6e31이다.

2026-10-08 17:11:02 KST, HEADc581f92·관리 핀7d57856에서 USB0·x64 return15를 한 번 요청했다. 기존 현재 USB1/활성 printer1·로그 링크 부재·샘플/probe0와 사용자 허용을 확인했다. 근거는 `.work/native-load/1791447062099/evidence.json`이다. send0·closed0·printApiCalls0·RFID API0·completion unknown, supervisor exit0·잔여 probe0이다. 사용자는 **후진했고 전진 전 위치로 돌아옴**으로 확인했다. 물리적 역방향 이송 확인이며 다음 인쇄 정렬의 확인을 대신하지 않는다.

## 동일 AW 서식의 x86 다음 라벨 비교

후진 관측을 받은 뒤2026-10-08 17:12:13 KST, 같은 HEADc581f92·관리 핀7d57856에서 blackmark60x20의 AW size10·weight1·E15 서식을 x86으로 한 번 요청했다. 서식·용지·좌표·수량은 x64와 같다. 실행 전 현재 USB1/PresentOnly printer1·활성 일치·로그 링크 부재·샘플/probe0를 확인했다. 근거는 `.work/native-load/1791447132801/evidence.json`이다. form0·native0·prefix empty·length2·closed0·RFID API0, supervisor exit0이다. 실제 문구·다음 라벨 위치·인쇄 장수의 사용자 관측은 대기 중이다. 추가 전진/재인쇄는 하지 않았다.

현재 Windows ANSI code page는 NLS 설정 조회에서949로 확인했다. 한글 시험의 인코딩 조건으로 기록하되 실제 한글 글리프·출력 지원을 확인한 것으로 확대하지 않는다. 같은 서식의 한글 비교는 x86 물리 결과 확인 뒤 진행한다. firmware는 계속 미확인이고 RFID 리더/태깅 시험은 사용자 지시상 제외다.

사용자는 x86 문구 위치가 정상이며 살짝 우측으로 이동했지만 거의 동일하다고 확인했다. 다음 라벨의 가시적 정렬 확인으로 기록하고 x64/x86 픽셀 완전 동일 또는 실제 장수 확인으로 확대하지 않는다. 임의 X 보정이나 DPI 설정을 추가하지 않았다.

## 같은 AW 설정의 합성 한글 비교 준비

9ad071d에서 payload 기대값 변경의 실패(exit1)를 확인하고49a8ce4에서 AW 문구만 `가나다라마바`로 변경했다. size10·weight1·X80/Y120·R0/E15·용지·수량·AC와 M/B 제어 비교 데이터는 유지했다. blackmark60x20이라는 선택 이름은 같아도 실행 핀과 payload로 앞선 ASCII 시험을 구분한다. SDK wrapper의 SetPrintFormString → StartLabelPrint(copies1) 경로를 사용한다. 원문 템플릿 전체 직접전송·RFID API·자동 반복·새 글꼴/DPI 설정은 추가하지 않았다.

fresh verifier의 Release/Debug x86/x64·두 CLI·repository/doctor·diff는 통과했다. Release 근거는 `.work/foundation/Release-1791447471769/evidence.json`이며49a8ce4 기준이다. a52d8b7에서 supervisor를 이 핀으로 갱신하고 root의 최종 Release를 통과했다. DLL 해시는7e8399ea864cf412d42a84496fb1958afd2be52baeeee79790d22e5bce229be0, x86 exee86d272f5ab076169722e3f4e62a7108b72287a57e921bf09df26111f8a571df, x64 exea39874e5cedfc5633b1338444b8f7a21c540afd4ae6878e474cc89d5145dd7c7이다. workingTreeDirty는 기존 .sln 및 당시 작성 중인 문서 때문에 true이며 clean tree라고 선언하지 않는다.

| 시각(KST), 2026-10-08 | 핀49a8ce4의 요청 | 실행 근거 | 결과 |
| --- | --- | --- | --- |
| 17:19:50 | x64 한글 인쇄1회 | `.work/native-load/1791447588107/evidence.json` | form0/native0·응답 length2·closed0; 처음에는 배출 부족으로 문구 미관측 |
| 17:22:12 | x64 M15 전진1회, 재인쇄 없음 | `.work/native-load/1791447731845/evidence.json` | send0·printApiCalls0; 사용자 한글 여섯 글자 정상·한 장·배출에 가려졌음 확인 |
| 17:24:37 | x64 B15 복귀1회 | `.work/native-load/1791447876878/evidence.json` | send0·printApiCalls0·closed0 |
| 17:24:38 | x86 같은 한글 인쇄1회 | `.work/native-load/1791447878139/evidence.json` | form0/native0·응답 length2·closed0 |
| 17:24:39 | x86 M15 전진1회, 재인쇄 없음 | `.work/native-load/1791447879675/evidence.json` | send0·printApiCalls0·closed0; 사용자 한글·위치·배출 정상·한 장 확인 |

마지막 세 요청은 복귀 → 인쇄 → 전진 순서이며 제어2회와 인쇄1회를 구분한다. 단계 오류면 후속 요청을 중단하고 자동 반복하지 않는다. 모든 실행은 현재 USB1만 갱신·PresentOnly USB printer1/활성 일치·SDK 로그 대상 링크 부재·잔여 probe/샘플0의 허용 범위를 확인했다. supervisor exit0·RFID API0이며 마지막 잔여 probe도0이다. raw SDK 로그·실제 UID는 복사하거나 게시하지 않았다.

## 이번 실기 판정과 남은 계약

확인한 범위는 현재 고정 SDK 쌍·Windows ANSI949·현재 시험 용지·AW size10/weight1·copies1의 x64/x86 ASCII/합성 한글과 별도 M15/B15 제어다. x86 ASCII는 약간 우측으로 이동하나 거의 동일하다는 사용자 관측을 유지한다. 픽셀 완전 동일, 모든 글꼴/크기/용지/펌웨어에서의 동작을 보장하지 않는다.

문구 문제는 AC 배율1을 AW size1로 그대로 사용한 차이와 관련됐고, 배출 부족은 SDK의 E 재작성·인쇄 전용 후처리 차이와 함께 확인했다. AW 문구의 인쇄와 배출을 분리한 wrapper 제어가 이번 장치에서 동작했다. M/B15는 시험 fixture에만 있으며 자동 배출/원점 복귀 제품 API·여러 건의 반복 수명 계약은 아직 구현하지 않았다. TASK-05/06 통합 시 호출자 설정·상태·실패/불명 처리를 적용해야 한다.

### 문서 미기재 항목의 판정 기준

미검증 제품 경계와 장치 지원은 사용자 지시에 따라 별도 [후속 이슈 #10](https://github.com/wotjr1649/U2BIO.LabelPrinting/issues/10)에서 기존 TASK-05/06/07의 구현·검증 결과와 연결한다. #2의 진단 완료와 구분하며 TASK-04 관리 처리의 선행 장애로 두지 않는다.

2026-10-08 사용자 지시에 따라 제조사 문서·샘플에 없는 내용은 **버전 고정 정적 분석 + 격리된 실기 + 관리 경계값 검사**로 지원 범위를 확정한다. 실기에서 성공한 값 하나를 모든 입력의 보장으로 확대하지 않는다. 추가 제조사 문서의 부재 자체를 TASK-02 중단 사유로 두지 않는다.

| 항목 | 채택한 계약과 증거 | 남기는 제한 |
| --- | --- | --- |
| 문자·호출 ABI | 샘플 선언·export/호출 경로와 x86/x64 실기. 입력 ANSI949·CRLF, copies=ushort1, 출력 포인터, StdCall | 다른 ANSI 환경·SDK 쌍은 재검증 |
| 출력 버퍼 | 고정 formatter와 정상 UID 길이 경로의 42 bytes(종료 NUL 포함). 별도 child의 42-byte 영역 뒤 PAGE_NOACCESS, 반환 후 전용 파서 검사 | 단순 실측으로 보편 최대 길이를 증명하지 않음. in-process 안전은 미보장 |
| 전용 응답 | printable ASCII의 prefix,쉼표,canonical signed int32,NUL. prefix는 빈 값/Unknown/지원 UID 길이. 실제0·101 및 합성 최대·오류 문법 검사 | 실제 성공 응답은 빈 prefix·0으로 관측. 다른 prefix/102/103 등은 정적 분석·합성 검사 근거이며 실기와 구분 |
| 성공 의미 | 유효한 응답 코드0은 SDK 정상 응답 근거(SdkResponse). 별도 사용자 관측으로 한글·한 장 확인 | 실제 배출 완료·모든 전송의 물리 성공·미래 펌웨어 보장이 아님 |
| 오류·불명 | 유효한 비0 코드는 raw 값 보존. timeout101·잘못된/미종료 응답·child 종료·native 예외·대기 초과는 Unknown, 후속 인쇄 중단·자동 재출력 없음 | 제품 결과/정리 통합은 TASK-05/06. 모든 용지·헤드·리본 오류는 미실측 |
| 인쇄와 배출 | AW size10/weight1·copies1 및 별도 wrapper M15/B15와 다음 라벨 실기 확인 | 이번 용지의 시험값. 자동 전진/복귀 제품 수명 계약은 TASK-05/06 |

표의 Unknown·후속 중단은 제품 결과의 설계 계약이다. 현재 진단은 raw 코드/PrintResponseInvalid/프로세스 실패를 기록하고 자동 재호출하지 않으며 제품 Unknown 결과·늦은 반환·소유권·정리 상태의 통합은 미구현이다. 42-byte 보호 영역의 정상 호출과 파서 경계값 검사는 확인했지만 native 초과 쓰기를 주입해 child 종료를 확인하는 검사는 실행하지 않았다. 그 오류 주입 및 호출자 보호 검증은 TASK-05의 제품 실행 경계 완료 조건이다.

TASK-02의 완료 범위는 고정 SDK의 내부 래핑·USB 계약 진단과 위 근거의 확정이다. **제품 PrintContractUnverified 게이트는 유지한다.** TASK-05에서 검증된 응답/실행 경계를 제품 흐름에 통합하고 native 초과 쓰기·종료·timeout이 호출 프로그램을 손상시키지 않는 경계를 검증하기 전에는 제품 인쇄를 활성화하지 않는다. 이는 문서 부재를 기다리는 작업과 별개인 구현 완료 조건이다. 진단용 child를 그대로 제품 지원으로 선언하거나 반환 후 파서만으로 native 쓰기 안전을 보장하지 않는다.

RFID 리더/새 태깅은 사용자가 제외했으므로 API/BTPL 기록 명령 미사용을 확인하되 저장 데이터 전후 실측을 했다고 주장하지 않는다. firmware는 미확인으로 기록하고 현재 장치 관측으로 범위를 한정한다. TCP/IP·RS-232·다른 용지·반복 일괄 인쇄는 후속 해당 TASK에서 검증한다.


### 완료 근거의 최종 재검사

독립 verifier/reviewer가 9e3490c 이후 문서 변경과 기존 소스·관리 검사를 확인했다. devflow 경로를 전달하지 않은 첫 Release 시도는 doctor 탐색 실패로 빌드 전에 중단됐다. 과정별 DEVFLOW_ROOT를 설치된0.3.1로 지정한 정상 재검사는 Release/Debug × x86/x64 모두 exit0이며 근거는 `.work/foundation/Release-1791450745242/evidence.json`, `.work/foundation/Debug-1791450753425/evidence.json`이다. 두 evidence의 revision은98cefda, workingTreeDirty는 기존 사용자 솔루션 metadata로 true다. 관리 ABI·배포 오류·전용 파서의42-byte 최대/잘못된 응답·호출 순서·중단·정리 검사가 통과했고 vendor code는 실행하지 않았다. CLI 두 검사·repository/doctor·diff도 통과했다.

조건3은 제공 선언·고정 정상 formatter·격리 x86/x64 호출·관리 경계 검사를 함께 근거로 삼는다. 조건5의 실제0/101은 위 과거 실기 기록을 root가 UID 없는 sanitized metadata로 재대조했으며, 이번에 다시 인쇄한 결과가 아니다. 합성 정수 경계 검사가 특정101의 물리 동작 실측을 대신하지 않는다. 조건7의 해시·장치·일시·제한은 위 실행 기록과 채택 계약에 남겼다. native 초과 쓰기 주입, 제품 Unknown 결과 통합과 보호·수명 검증은 완료 주장에 포함하지 않는다. TASK-02의 고정 SDK 진단 계약 확정은 TASK-03 착수 근거이며 공통 DLL 전체·제품 인쇄·배포 완료를 뜻하지 않는다.
