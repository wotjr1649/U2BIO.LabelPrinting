# TASK-02 Native SDK 구현·검증 상태

기준: v0.1.1-design, SDK_API_COVERAGE.md의 두 native 배포 쌍. .NET Framework 4.6.1 관리 DLL은 AnyCPU이며 제조사 바이너리는 저장소에 포함하지 않는다.

## 구현 범위

- `NativeApi`는 헤더 40개와 샘플의 StartLabelPrint를 합한 41개 내부 delegate를 선언한다. StdCall, ANSI 입력 LPStr, ushort/U2·byte/U1·int32를 보존한다. x86은 기준 DLL에 실제 존재하는 `_Name@N`, x64는 실제 비장식 이름으로 바인딩한다. 현재 인수는 모두 x86에서 4-byte stack slot이며 지원 타입 외에는 이름을 추정하지 않는다.
- GetUidValue·StartPrintWithRfid·StartLabelPrint의 출력 인수는 caller-owned LPTSTR 주소 후보인 `[Out] IntPtr`다. 선언 자체가 버퍼 계약 확인을 뜻하지 않는다. 최대 쓰기 바이트·종료 규칙·문자 인코딩은 미확정이며 메모리를 할당하거나 이 함수를 호출하지 않는다. 샘플 SizeConst=100은 안전 상한으로 사용하지 않는다.
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

현재 supervisor의 관리 코드 기준은 `10e38d9`이며 fresh verifier와 root가 다음 Release 핀의 일치를 확인했다.

| 현재 일회 진단 산출물 | SHA-256 |
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
| 라벨 사이 구간 구성 | 흰 뒷종이 합계 15 mm + 검은 마크 5 mm |
| 반복 주기 | 20 + 20 = 40 mm |
| 뒷종이 전체 폭 | 70 mm |
| 인식 방식 | 뒷면 Black-Mark |

이 확인으로 최초 진단의 W50·Q25,3과 현재 미디어의 불일치는 확정되었다. 센서 기준점 및 반복 주기가 맞지 않으면 흰 라벨 밖 뒷종이에 인쇄하거나 경계에 걸릴 수 있다. 이를 유력 원인으로 두되, 아직 보정 후 출력으로 원인 제거를 검증하지 않았다.

다음 단일 진단은 Black-Mark 반복 주기 40 mm·마크 폭 5 mm를 기준으로 Q의 offset까지 다루고, 전체 뒷종이 기준 폭 70 mm와 흰 라벨 60 mm의 좌우 여백을 구분하는 후보로 준비한다. 반사 센서 G0의 해당 모델 적용과 SDK 가공 후 명령 보존도 확인해야 한다. 원문 BTPL의 센서/마크 offset과 SDK Registry 변경 함수 SetLabelSetting은 서로 다른 경로다. Registry를 대체 설정으로 쓰지 않는다.

마크 앞·뒤의 흰 뒷종이 두 구간을 A/B로 두면 A+B=15 mm이나 A와 B 각각은 아직 확인하지 않았다. 사진만으로 각각 7.5 mm라고 확정하지 않는다. 용지 진행 방향의 마크 끝→흰 라벨 시작 거리를 사용자에게 요청했으며, 확인 전에는 offset을 임의 지정해 재출력하지 않는다. 현재 실행 코드는 최초 50×25/G3 고정 진단이므로 새 규격을 코드에 반영하고 관리 검사·산출물 핀을 갱신한 뒤 실기를 실행해야 한다. 실제 한 장·한글·RFID 저장 데이터 전후 일치 조건은 계속 열어 둔다.
