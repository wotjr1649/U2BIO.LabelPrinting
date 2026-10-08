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
node tools/probe-native-load.js --sdk-directory <SDK의 라이브러리 폴더> --usb-connection
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

| 현재 Release 산출물 | SHA-256 |
| --- | --- |
| DLL, x86/x64 공통 | `b0d649e17b97dd806c388f2863476274f32f3a83953bf496d8ea3cf8fdaf6495` |
| x86 validation exe | `6492848e12dc57d25dba8488f539423fee12fb1df10a7f2e5d6cd434e319d271` |
| x64 validation exe | `5278c12c29f6e007d47603365bed1376994a76b0b2b1ef84d44abac7ea477091` |

fresh verifier는 `1ea595b`에서 validation csproj를 직접 Release/Debug × x86/x64 재빌드·실행했고 모두 exit 0이며 위 manifest 해시와 일치했다. 호출 순서·occupied/unknown 상태·설정 실패·연결 실패 정리·해제 raw 값 보존·닫힘 실패·Connect 예외를 관리 delegate fixture로 검사했고 기존 인쇄 gate와 native 미로드 검사를 유지했다. 이 fixture는 실제 장치 결과의 대체물이 아니다.

작업 중 사용자가 Visual Studio를 열고 닫으면서 솔루션 구성이 Any CPU만 남도록 바뀌었다. 이 사용자 변경은 보존하고 커밋에 포함하지 않았다. 변경된 솔루션의 일반 verify-build는 MSB4126으로 실패했으며 통과로 보고하지 않는다. 후속 직접 빌드는 같은 검증 프로젝트와 MSBuild 인수에 Configuration/Platform을 명시한 결과다. 저장소 검사·diff 검사 및 tool CLI 검사는 통과했다. 솔루션 x86/x64 구성 정리는 별도로 남아 있다.

## 남은 수용 근거

| TASK-02 조건 | 현재 경계 |
| --- | --- |
| 41개 내부 래핑 | 선언·offline ABI 검사와 실제 두 아키텍처 41개 바인딩 통과; 모든 native 함수 호출을 검증했다는 뜻은 아님 |
| 아키텍처·해시·충돌·export·절대경로 로더 | metadata/negative 검사와 gdiplus 내용 검증, 실제 두 아키텍처 로딩·바인딩 통과; 장시간 모듈 수명 통합은 후속 범위 |
| ANSI·방향·StdCall·버퍼 상한·종료 규칙 | 제공 선언 대조, buffer/문자열 실기·제조사 계약 미확인 |
| USB 식별·RFID 포트 없는 연결·해제 | 샘플 문자열 "0", x86/x64 실제 연결·해제 통과. "USB1" 별칭은 시도하지 않음 |
| StartLabelPrint 전용 응답·완료 의미 | 미확인, UID/코드 parser나 void 성공으로 대체하지 않음 |
| 한글·BTPL 글꼴·한 행 한 장·RFID 보존 | 실제 출력 전 위 계약 확보 필요, 아직 미실행 |
| 장치·펌웨어·시험 결과 | 장치 정보·실제 시험 기록은 아직 없음 |

핵심 미확정 조건이 남아 있으므로 TASK-02 전체를 완료하거나 실제 인쇄가 가능하다고 보고하지 않는다.

## 완료를 위해 필요한 조치

1. StartLabelPrint의 정확한 원형, caller 버퍼 소유권, 최대 쓰기 **바이트** 수와 종료 문자 포함 여부, 인코딩, 잘림·오류 처리 규칙을 확인한다. 매개변수에 길이가 없으므로 샘플 SizeConst=100이나 임의의 큰 버퍼만으로 안전성을 확정하지 않는다. 사용자는 추가 규격이 없으며 제공 Rev.1.3 문서와 샘플을 기준으로 진행하도록 확인했다. 해당 자료의 StartLabelPrint는 appendix 선언이며 버퍼 쓰기 상한과 전용 응답 의미가 추가로 설명되어 있지 않다.
2. 인쇄 전용 응답 문법·성공/오류 예·완료 시점·timeout/미정의 응답, copies와 BTPL 수량의 관계, RFID 무기록 동작을 확인한다. StartPrintWithRfid의 UID,code 형식을 전용하지 않는다.
3. 합성 한글 문구 한 건을 출력하고 장수·글꼴·RFID 전후 일치를 기록한다. RFID 비교는 검증된 별도 시험 절차로 수행하며 제품에 RFID 공개 API를 추가하지 않는다.

사용자 장치 준비와 실제 USB 연결·해제는 확인되어 있다. 현재 작업 순서는 TASK-02 완료 → TASK-03이며, 인쇄 계약·실기 조건이 남아 있으므로 TASK-03을 착수하지 않았다. 계획 문서의 TASK-02 조건 1·2·4는 로컬 근거로 체크했으나 GitHub #2는 갱신·종료하지 않았고 이번 코드와 근거 문서는 로컬 작업 브랜치에만 기록했다.
