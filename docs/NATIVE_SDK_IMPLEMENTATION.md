# TASK-02 Native SDK 구현·검증 상태

기준: v0.1.1-design, SDK_API_COVERAGE.md의 두 native 배포 쌍. .NET Framework 4.6.1 관리 DLL은 AnyCPU이며 제조사 바이너리는 저장소에 포함하지 않는다.

## 구현 범위

- `NativeApi`는 헤더 40개와 샘플의 StartLabelPrint를 합한 41개 내부 delegate를 선언한다. StdCall, ANSI 입력 LPStr, ushort/U2·byte/U1·int32를 보존한다. x86은 기준 DLL에 실제 존재하는 `_Name@N`, x64는 실제 비장식 이름으로 바인딩한다. 현재 인수는 모두 x86에서 4-byte stack slot이며 지원 타입 외에는 이름을 추정하지 않는다.
- GetUidValue·StartPrintWithRfid·StartLabelPrint의 출력 인수는 caller-owned LPTSTR 주소 후보인 `[Out] IntPtr`다. 선언 자체가 버퍼 계약 확인을 뜻하지 않는다. 최대 쓰기 바이트·종료 규칙·문자 인코딩은 미확정이며 메모리를 할당하거나 이 함수를 호출하지 않는다. 샘플 SizeConst=100은 안전 상한으로 사용하지 않는다.
- StartPrintWithRfid2와 ReadResponseMessage는 선언·호출하지 않는다. public P/Invoke, raw SendData, RFID API 또는 검증 우회 옵션은 없다. `GetFunction<T>()`는 이 구현 버전에서 항상 PrintContractUnverified로 거부한다.
- `NativeImage`는 길이 제한·offset/RVA 범위 검사로 PE와 명명 export·일반 import를 읽는다. DLL을 map하거나 실행하지 않는다. 아키텍처·고정 해시·41개 exact export와 forwarder 여부를 대조한다. 기준 외 delay import·private import는 거부한다.
- `LoadForConnection()`은 AppDomain.BaseDirectory의 로컬 드라이브 한 쌍만 대상으로 한다. 부모 reparse 경로, 파일/아키텍처/해시, 사전 로드된 vendor 모듈, 앱 폴더의 시스템 DLL 가림을 검사한다. 파일 핸들의 최종 경로를 대조하고 부모 디렉터리에는 목록 읽기·share-delete 금지 lease를 유지한다. 절대경로 LoadLibraryExW(0x900), 모듈 경로 재검사, exact GetProcAddress를 사용하며 검색 경로 fallback·PATH 변경·COM 등록·unload는 하지 않는다. OS import의 기존/후속 모듈은 현재 프로세스 시스템 디렉터리(WOW64 실제 경로 포함)만 허용한다. acquired 모듈과 delegate는 process lifetime으로 유지하고 부분 로드 이후 재시도도 막으며 최초 실패의 코드·함수·Win32 오류를 보존한다.

로드는 DllMain과 dependency 초기화를 실행한다. 오프라인 PE 검사와 실제 로드 시험을 구분한다. 현재 로더는 internal이며 공개 Connect에 연결하지 않았다. TASK-05에서 worker·process/target 예약·대기 제한·늦은 정리 계약과 함께 연결해야 한다. 현재 캐시는 AppDomain 단위이며, 소유한 캐시가 아닌 기존 vendor 모듈은 같은 경로여도 보수적으로 NativeModuleConflict로 거부한다. 참여 AppDomain 간 안전한 재사용은 후속 수명 통합에서 별도 검증해야 한다.

배포 폴더와 Windows 시스템 폴더는 신뢰되는 관리 영역이라는 전제가 필요하다. 파일·디렉터리 lease는 정상 파일 I/O의 교체·쓰기·부모 rename을 제한하지만, 공격자가 배포 폴더에 새 dependency를 삽입할 수 있는 환경이나 악의적인 관리자·기존 memory mapping·동적 DLL 로드를 모두 격리하는 sandbox는 아니다. ACL·호스트 보안 설정은 바꾸지 않는다. 두 private DLL과 관측된 일반 import만 검사하며 제조사 내부의 동적 로드/초기화 효과는 실기 검증 대상으로 남긴다. 현재 보수적인 정책은 System32/WOW64 이외의 WinSxS 모듈도 허용하지 않는다. 후속 실제 로드 시험에서 양쪽 아키텍처의 WinSxS gdiplus.dll이 이 정책에 의해 거부됨을 확인했다. 안전한 side-by-side 모듈 검증 지원은 미구현이다.

## 실행 검사

```text
node tools/verify-build.js
node tools/verify-build.js --configuration Debug
<Validation.exe> --inspect-sdk <SDK의 라이브러리 폴더>
node tools/probe-native-load.js --sdk-directory <SDK의 라이브러리 폴더>
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

정확성·보안 리뷰는 새 진단이 로드 허용 조건을 바꾸지 않고 실제 경로를 보고하지 않음을 확인했다. bare vendor ModulePath 충돌 등 일부 오류는 상세 tag 없이 실패할 수 있으며 이를 성공으로 해석하지 않는다. 실패를 없애기 위한 WinSxS 전체 허용, 경로 fallback 또는 무검증 preload는 추가하지 않았다.

## 남은 수용 근거

| TASK-02 조건 | 현재 경계 |
| --- | --- |
| 41개 내부 래핑 | 선언·바인딩 코드와 offline ABI 검사; native 호출 ABI 실기 증거는 별도 |
| 아키텍처·해시·충돌·export·절대경로 로더 | offline 검사 통과. 실제 두 프로세스의 postload WinSxS gdiplus.dll 검사 실패 확인; 바인딩 성공·모듈 수명 통합은 미검증 |
| ANSI·방향·StdCall·버퍼 상한·종료 규칙 | 제공 선언 대조, buffer/문자열 실기·제조사 계약 미확인 |
| USB 식별·RFID 포트 없는 연결·해제 | 사용자 장치 준비 확인; sample 0/manual USB1 차이와 실제 연결은 미검증 |
| StartLabelPrint 전용 응답·완료 의미 | 미확인, UID/코드 parser나 void 성공으로 대체하지 않음 |
| 한글·BTPL 글꼴·한 행 한 장·RFID 보존 | 실제 출력 전 위 계약 확보 필요, 아직 미실행 |
| 장치·펌웨어·시험 결과 | 장치 정보·실제 시험 기록은 아직 없음 |

핵심 미확정 조건이 남아 있으므로 TASK-02 전체를 완료하거나 실제 인쇄가 가능하다고 보고하지 않는다.

## 완료를 위해 필요한 조치

1. 로더가 현재 OS에서 사용하는 Windows side-by-side 모듈을 검증할 수 있도록 설계를 보완하고 정상 모듈·잘못된 경로·변조·충돌을 검사한다. 위 실패는 제조사 응답 자료 부족과 별개의 로더 호환성 문제다.
2. StartLabelPrint의 정확한 원형, caller 버퍼 소유권, 최대 쓰기 **바이트** 수와 종료 문자 포함 여부, 인코딩, 잘림·오류 처리 규칙을 제조사로부터 확보한다. 매개변수에 길이가 없으므로 샘플 SizeConst=100이나 임의의 큰 버퍼만으로 안전성을 확정하지 않는다.
3. 인쇄 전용 응답 문법·성공/오류 예·완료 시점·timeout/미정의 응답, copies와 BTPL 수량의 관계, RFID 무기록 동작을 확인한다. StartPrintWithRfid의 UID,code 형식을 전용하지 않는다.
4. USB 식별 문자열과 RFID 포트 없는 연결·해제를 확인한 뒤 합성 한글 문구 한 건을 출력하고 장수·글꼴·RFID 전후 일치를 기록한다. RFID 비교는 검증된 별도 시험 절차로 수행하며 제품에 RFID 공개 API를 추가하지 않는다.

사용자 장치 준비는 확인되어 있다. 현재 작업 순서는 TASK-02 완료 → TASK-03이며, 이 두 blocker가 남은 상태에서 TASK-03을 착수하지 않았다. GitHub #2는 종료하지 않았고 이번 코드와 근거 문서는 로컬 작업 브랜치에만 기록했다.
