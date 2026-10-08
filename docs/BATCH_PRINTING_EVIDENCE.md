# TASK-06 일괄 API 검증 기록

2026-10-09, 구현 커밋 `3050c1fd1b769614e0b9d14e8fa60fdcdd869ba0`을 독립 verifier가 검사했다. 범위는 `760adbd5833ef8243d88191adfcebd898f23359d..3050c1f`다. 이 문서를 추가한 후속 문서 커밋과 제품 검증 커밋을 구분한다. 기존 사용자 솔루션 메타데이터 변경을 보존했으므로 evidence의 workingTreeDirty는 true다.

## 실행 증거

```text
node tools/verify-build.js
node tools/verify-build.js --configuration Debug
```

두 명령은 종료 코드 0이다. Release/Debug × x86/x64 네 구성의 빌드·실행, .NET Framework 4.6.1/AnyCPU 관리 DLL, 명시적 x86/x64 검증 exe, Prefer32Bit=false, SDK 미로드를 확인했다. MSBuild 16.11.6.22506과 compiler `3.11.0-4.26426.1`을 사용했다. 증거는 로컬 `.work/foundation/Release-1791475444595/evidence.json` 및 `Debug-1791475455290/evidence.json`에 보관했다. raw 로그와 바이너리 snapshot은 Git에 올리지 않는다.

| 조건 | 실제 검사 결과 |
| --- | --- |
| 전체 행 검사·입력 순서 | 두 번째 빈 값에서 valid=[true,false,true], NNN/ValidationFailed, SDK 호출 0회 |
| 첫 실패·불명 뒤 중단 | 등록 실패 SFN, 확인된 인쇄 오류 FNN, 둘째 인쇄 timeout SUN; 자동 재호출 없음 |
| nullable 검사·집계 | Busy/사전 취소는 미검사 null. 관리 검사 중 취소는 완료 행 검사만 보존하며 batch/device=null. 행 수와 상태 집계 합계 일치 |
| 취소·Dispose 경계 | 등록 중 취소 NNN/출력 0회, 인쇄 중 취소 SNN, 확인된 실패 FNN 우선, Dispose SNN, 마지막 완료 뒤 취소 SSS/None |
| 호출 제한·진입 여부 | 기본 30000ms; 1~2147483647ms 허용, 0/음수/초과 거절. 등록 timeout FNN/미진입, 인쇄 timeout SUN/진입 기록 |
| 늦은 결과·정리 | late 등록 후 인쇄 0회, late 응답으로 결과 변경 없음. 진행 중 timeout의 pending=true, 이미 반환한 native의 late timeout은 pending=false |
| 소유권·정리 실패 | 다른 객체 슬롯 점유는 사전 취소보다 Busy 우선/SDK 호출 0회. 정리 실패 뒤 SSS/None 유지, CleanupCode와 미해제 자원 표시 |

추가 검사: native preflight status0/9와 취소/Dispose의 네 조합에서 NotConnected/StatusUnavailable 및 완료된 장치 오류 검사를 보존했다. 관리 검사 중 취소는 10000행의 합성 데이터로 완료/미완료 행을 구분했다. fixture는 모든 SDK 호출이 같은 worker에서 실행됨을 검사한다.

## 실패 재현과 리뷰

PrintAsync 추가 전 `3b9753d`의 빌드는 CS1061로 실패했다. 추가 검사 잠금은 기존 관리 바이너리에서 MissingMethodException을 확인한 별도 오프라인 baseline을 사용했다. `cecf136`의 리뷰 재현은 process-owner admission, preflight failure priority, late-return pending 각각에서 실패했으며 `3050c1f`에서 수정 후 통과했다. 독립 정적 재리뷰는 세 수정과 연결된 correctness/security 결함을 찾지 못했다.

전체 검사에서 발견한 early timer callback은 남은 시간을 다시 예약하도록 수정했다. 취소/Dispose가 native를 강제 종료한다는 보장을 추가하지 않았다. 결과는 immutable snapshot이고 늦은 정리는 상태 조회로 확인한다.

## 검증 산출물

같은 구성의 관리 DLL은 x86/x64 검사에서 동일한 AnyCPU 바이너리다.

| 구성 | 산출물 | SHA-256 |
| --- | --- | --- |
| Release | 관리 DLL | `751cd21ab2a663fa33d1d05646144821eec18a2cde4566231bbebc0316fb5572` |
| Release | x86 검증 exe | `3bdc0c02e0cf42a535cbc01cac0c9955d7143940c772a625c253d2d47f7fb508` |
| Release | x64 검증 exe | `19ec54507b42f366949de8683c3b58b4c00669c416f1b7bb01d9197f873d4a60` |
| Debug | 관리 DLL | `8fefa74136c5f591e3ef7fcf1a8dfafdc268ab98a587249d0e0a808ca122f47f` |
| Debug | x86 검증 exe | `93bafe8aa2c2be9aa41daefbce7347ab353ce5615e1be8d705916d39527b0b5d` |
| Debug | x64 검증 exe | `8befd627005369acab02115e7e536af8977bf7de374d4d588ab613ab52190853` |

## 완료 경계

TASK-06의 관리·합성 SDK 제어 흐름 검증이다. 실제 vendor SDK 로드/USB 출력/물리 배출/RFID 저장값 비교/native 장애 격리는 실행하지 않았다. TASK-05의 실기 조건7과 TASK-07의 제품 인수 조건을 통과로 바꾸지 않는다. DevExpress Tester는 TASK-07 필수 조건 충족 후 시작한다. [제품 API](PRINTER_API.ko.md)와 [작업 계획](IMPLEMENTATION_PLAN.md)을 함께 따른다.
