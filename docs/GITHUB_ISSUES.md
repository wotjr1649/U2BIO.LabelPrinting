# GitHub 이슈 등록 상태

확인일: 2026-10-08 KST. 대상 저장소: [wotjr1649/U2BIO.LabelPrinting](https://github.com/wotjr1649/U2BIO.LabelPrinting).

문서 기준은 **v0.1.1-design**, 로컬 커밋은 `5c8c85b3e463d6eeb5314d22a1e85589dda4ed8b`다. 기준 태그는 로컬에 있으며 원격 push는 수행하지 않았다. 등록할 작업의 범위와 총 61개 완료 조건은 [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)에 있다.

## 등록 결과

**등록 미완료: 원격 이슈 0건.** 성공 응답이나 이슈 번호가 없으므로 등록된 것으로 취급하지 않는다. TASK ID는 로컬 계획의 식별자이며 GitHub 이슈 번호가 아니다.

| 작업 | 등록할 제목 | 선행 작업 | GitHub 등록 상태 |
| --- | --- | --- | --- |
| TASK-01 | 설계 기준 버전 확정 및 개발 프로젝트 구성 | 없음 | 대기, 번호 없음 |
| TASK-02 | Native SDK 래핑과 USB 인쇄 계약 검증 | TASK-01 | 대기, 번호 없음 |
| TASK-03 | BTPL 서식 해석·편집·저장 구현 | TASK-01 | 대기, 번호 없음 |
| TASK-04 | JSON 바인딩·DataTable 렌더링·관리 사전검사 구현 | TASK-03 | 대기, 번호 없음 |
| TASK-05 | 연결 수명·장치 사전검사·단건 인쇄 구현 | TASK-02, TASK-04 | 대기, 번호 없음 |
| TASK-06 | 일괄 인쇄·취소·시간 제한·실패 결과 구현 | TASK-05 | 대기, 번호 없음 |
| TASK-07 | 공통 DLL 인수 검증 및 배포 검증 | TASK-03~TASK-06 | 대기, 번호 없음 |
| TASK-08 | DevExpress WinForms Tester 구현 | TASK-07 | 대기, 번호 없음 |
| TASK-09 | Checkup·SmartCheckup 연동 예시 정리 | TASK-07 | 대기, 번호 없음 |

## 관측한 실패와 한계

1. `gh issue create`의 GraphQL 생성 요청이 서버 오류로 실패했다.
2. REST 생성 요청도 실패했다. JSON 입력을 로컬에서 검증한 뒤 별도 입력 방식으로 확인한 응답은 `HTTP/2.0 500 Internal Server Error`이며 CLI는 `unexpected end of JSON input`을 반환했다.
3. 각 실패 뒤 원격 이슈 목록을 조회했고 0건이었다. 이슈 조회, 저장소 조회와 issues 활성 상태 확인은 성공했다. 인증 정보나 설정은 변경하지 않았다.
4. 브라우저로 상태를 확인하려 했으나 사용 가능한 브라우저가 없었다. 같은 생성 API 요청을 더 반복하지 않았다.

원인은 확정하지 않았다. 장치 시험, DLL 구현, Git push를 수행한 기록이 아니다. 원격 상태 변경이나 API 복구가 확인되면 기존 이슈 목록부터 다시 조회하고 중복 없이 등록한다. 이슈 본문에는 기준 버전·커밋, 선행 이슈, 작업 범위, 완료 조건, 검사 근거를 넣는다. TASK-01은 문서 부분이 완료됐더라도 프로젝트 구성·빌드 조건을 충족하기 전에는 닫지 않는다.
