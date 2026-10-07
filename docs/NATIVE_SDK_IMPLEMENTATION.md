# TASK-02 Native SDK 구현·검증 상태

기준: v0.1.1-design, SDK_API_COVERAGE.md의 두 native 배포 쌍. .NET Framework 4.6.1 관리 DLL은 AnyCPU이며 제조사 바이너리는 저장소에 포함하지 않는다.

## 구현 범위

- `NativeApi`는 헤더 40개와 샘플의 StartLabelPrint를 합한 41개 내부 delegate를 선언한다. StdCall, ANSI 입력 LPStr, ushort/U2·byte/U1·int32를 보존한다. x86은 기준 DLL에 실제 존재하는 `_Name@N`, x64는 실제 비장식 이름으로 바인딩한다. 현재 인수는 모두 x86에서 4-byte stack slot이며 지원 타입 외에는 이름을 추정하지 않는다.
- GetUidValue·StartPrintWithRfid·StartLabelPrint의 출력 인수는 caller-owned LPTSTR 주소 후보인 `[Out] IntPtr`다. 선언 자체가 버퍼 계약 확인을 뜻하지 않는다. 최대 쓰기 바이트·종료 규칙·문자 인코딩은 미확정이며 메모리를 할당하거나 이 함수를 호출하지 않는다. 샘플 SizeConst=100은 안전 상한으로 사용하지 않는다.
- StartPrintWithRfid2와 ReadResponseMessage는 선언·호출하지 않는다. public P/Invoke, raw SendData, RFID API 또는 검증 우회 옵션은 없다. `GetFunction<T>()`는 이 구현 버전에서 항상 PrintContractUnverified로 거부한다.
- `NativeImage`는 길이 제한·offset/RVA 범위 검사로 PE와 명명 export·일반 import를 읽는다. DLL을 map하거나 실행하지 않는다. 아키텍처·고정 해시·41개 exact export와 forwarder 여부를 대조한다. 기준 외 delay import·private import는 거부한다.
- `LoadForConnection()`은 AppDomain.BaseDirectory의 로컬 드라이브 한 쌍만 대상으로 한다. 부모 reparse 경로, 파일/아키텍처/해시, 사전 로드된 vendor 모듈, 앱 폴더의 시스템 DLL 가림을 검사한다. 파일 핸들의 최종 경로를 대조하고 부모 디렉터리에는 목록 읽기·share-delete 금지 lease를 유지한다. 절대경로 LoadLibraryExW(0x900), 모듈 경로 재검사, exact GetProcAddress를 사용하며 검색 경로 fallback·PATH 변경·COM 등록·unload는 하지 않는다. OS import의 기존/후속 모듈은 현재 프로세스 시스템 디렉터리(WOW64 실제 경로 포함)만 허용한다. acquired 모듈과 delegate는 process lifetime으로 유지하고 부분 로드 이후 재시도도 막으며 최초 실패의 코드·함수·Win32 오류를 보존한다.

로드는 DllMain과 dependency 초기화를 실행한다. 오프라인 PE 검사와 실제 로드 시험을 구분한다. 현재 로더는 internal이며 공개 Connect에 연결하지 않았다. TASK-05에서 worker·process/target 예약·대기 제한·늦은 정리 계약과 함께 연결해야 한다. 현재 캐시는 AppDomain 단위이며, 소유한 캐시가 아닌 기존 vendor 모듈은 같은 경로여도 보수적으로 NativeModuleConflict로 거부한다. 참여 AppDomain 간 안전한 재사용은 후속 수명 통합에서 별도 검증해야 한다.

배포 폴더와 Windows 시스템 폴더는 신뢰되는 관리 영역이라는 전제가 필요하다. 파일·디렉터리 lease는 정상 파일 I/O의 교체·쓰기·부모 rename을 제한하지만, 공격자가 배포 폴더에 새 dependency를 삽입할 수 있는 환경이나 악의적인 관리자·기존 memory mapping·동적 DLL 로드를 모두 격리하는 sandbox는 아니다. ACL·호스트 보안 설정은 바꾸지 않는다. 두 private DLL과 관측된 일반 import만 검사하며 제조사 내부의 동적 로드/초기화 효과는 실기 검증 대상으로 남긴다. 현재 보수적인 정책은 System32/WOW64 이외의 WinSxS 모듈도 허용하지 않으며, 필요 여부와 안전한 지원은 실제 로드 시험에서 확인해야 한다.

## 실행 검사

```text
node tools/verify-build.js
node tools/verify-build.js --configuration Debug
<Validation.exe> --inspect-sdk <SDK의 라이브러리 폴더>
```

일반 빌드 검사는 이전 framework·PE·프로세스 비트 수·native 미로드 검사에 41개 ABI golden 검사, 계약 차단, missing/corrupt/architecture/version 오류 검사를 추가한다. --inspect-sdk는 라이브러리 폴더 아래 x86/lib와 x64/lib의 실제 기준 파일을 읽고 두 쌍의 Machine·해시·exact 41 exports·일반 imports를 대조한다. vendor 코드는 실행하지 않으며 실제 연결·인쇄·RFID 시험을 대체하지 않는다.

SDK 검사 성공 출력은 `PASS: pinned x86/x64 native pairs architecture/hash/41 exports/imports inspected; vendor code not executed`다. 원본·실제 label/RFID 값·raw 응답은 출력하거나 저장하지 않는다. 검증 오류는 안전한 코드와 원래 OS 오류 번호만 보존한다.

## 남은 수용 근거

| TASK-02 조건 | 현재 경계 |
| --- | --- |
| 41개 내부 래핑 | 선언·바인딩 코드와 offline ABI 검사; native 호출 ABI 실기 증거는 별도 |
| 아키텍처·해시·충돌·export·절대경로 로더 | 구현 및 offline 검사; 실제 LoadLibraryEx/실패·모듈 수명 시험은 아직 미실행 |
| ANSI·방향·StdCall·버퍼 상한·종료 규칙 | 제공 선언 대조, buffer/문자열 실기·제조사 계약 미확인 |
| USB 식별·RFID 포트 없는 연결·해제 | 사용자 장치 준비 확인; sample 0/manual USB1 차이와 실제 연결은 미검증 |
| StartLabelPrint 전용 응답·완료 의미 | 미확인, UID/코드 parser나 void 성공으로 대체하지 않음 |
| 한글·BTPL 글꼴·한 행 한 장·RFID 보존 | 실제 출력 전 위 계약 확보 필요, 아직 미실행 |
| 장치·펌웨어·시험 결과 | 장치 정보·실제 시험 기록은 아직 없음 |

핵심 미확정 조건이 남아 있으므로 TASK-02 전체를 완료하거나 실제 인쇄가 가능하다고 보고하지 않는다.
