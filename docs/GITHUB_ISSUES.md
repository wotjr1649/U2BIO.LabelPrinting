# GitHub 이슈 등록 상태

확인일: 2026-10-08 KST. 대상 저장소: [wotjr1649/U2BIO.LabelPrinting](https://github.com/wotjr1649/U2BIO.LabelPrinting).

문서 기준은 **v0.1.1-design**, 기준 커밋은 `5c8c85b3e463d6eeb5314d22a1e85589dda4ed8b`다. 원격 main과 기준 태그를 최초 push하고 실제 ref를 확인했다. 저장소 템플릿의 통합 커밋은 `fed6a62fb2aadc3530b1f7a6ae997b9d2de36e16`이며 기준 태그는 변경하지 않았다. 작업 범위와 총 61개 완료 조건은 [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md)에 있다.

## 등록 결과

**9개 이슈 등록 완료.** 설치된 devflow-state로 생성하고 이슈 본문을 읽어 완료 조건·선행 관계를 확인했다. 이슈의 실제 진행·체크 상태는 원격 이슈를 기준으로 한다. TASK ID는 로컬 계획의 식별자다.

| 작업 | 제목 | 선행 이슈 | GitHub 이슈 |
| --- | --- | --- | --- |
| TASK-01 | 설계 기준 버전 확정 및 개발 프로젝트 구성 | 없음 | [#1](https://github.com/wotjr1649/U2BIO.LabelPrinting/issues/1) |
| TASK-02 | Native SDK 래핑과 USB 인쇄 계약 검증 | #1 | [#2](https://github.com/wotjr1649/U2BIO.LabelPrinting/issues/2) |
| TASK-03 | BTPL 서식 해석·편집·저장 구현 | #1 | [#3](https://github.com/wotjr1649/U2BIO.LabelPrinting/issues/3) |
| TASK-04 | JSON 바인딩·DataTable 렌더링·관리 사전검사 구현 | #3 | [#4](https://github.com/wotjr1649/U2BIO.LabelPrinting/issues/4) |
| TASK-05 | 연결 수명·장치 사전검사·단건 인쇄 구현 | #2, #4 | [#5](https://github.com/wotjr1649/U2BIO.LabelPrinting/issues/5) |
| TASK-06 | 일괄 인쇄·취소·시간 제한·실패 결과 구현 | #5 | [#6](https://github.com/wotjr1649/U2BIO.LabelPrinting/issues/6) |
| TASK-07 | 공통 DLL 인수 검증 및 배포 검증 | #3, #4, #5, #6 | [#7](https://github.com/wotjr1649/U2BIO.LabelPrinting/issues/7) |
| TASK-08 | DevExpress WinForms Tester 구현 | #7 | [#8](https://github.com/wotjr1649/U2BIO.LabelPrinting/issues/8) |
| TASK-09 | Checkup·SmartCheckup 연동 예시 정리 | #7 | [#9](https://github.com/wotjr1649/U2BIO.LabelPrinting/issues/9) |

후속 추적 이슈 [#10](https://github.com/wotjr1649/U2BIO.LabelPrinting/issues/10)을 사용자 지시에 따라 등록하고 #2의 후속 관계를 확인했다. TASK-02의 미검증 보호·결과·수명·장치 지원을 기존 #5/#6/#7의 근거와 연결하며, 별도 인쇄 엔진이나 중복 시험을 구현하는 작업이 아니다. TASK-02의 진단 완료 상태와 사용자 제외 RFID 실측 정책은 유지한다.

## 초기 실패와 후속 확인

1. `gh issue create`의 GraphQL 생성 요청이 서버 오류로 실패했다.
2. REST 생성 요청도 실패했다. JSON 입력을 로컬에서 검증한 뒤 별도 입력 방식으로 확인한 응답은 `HTTP/2.0 500 Internal Server Error`이며 CLI는 `unexpected end of JSON input`을 반환했다.
3. 각 실패 뒤 원격 이슈 목록을 조회했고 0건이었다. 이슈 조회, 저장소 조회와 issues 활성 상태 확인은 성공했다. 인증 정보나 설정은 변경하지 않았다.
4. 브라우저로 상태를 확인하려 했으나 사용 가능한 브라우저가 없었다. 같은 생성 API 요청을 더 반복하지 않았다.

원격 main이 없음을 확인하고 기본 템플릿 준비 후 main과 기준 태그를 최초 반영했다. 이후 이슈 1~8 생성이 성공했고, 마지막 생성 요청의 실패 뒤 중복 여부와 앞선 이슈들을 다시 조회한 다음 9번을 등록했다. 초기 HTTP 500의 원인을 브랜치 부재로 단정하지 않는다.

## 현재 완료 경계

devflow-doctor, 추적 파일만 있는 별도 체크아웃, 잘못된 인수 ID 거부, 제외 규칙과 기준 해시 검사를 수행했다. SDK 원본·로컬 참고 자료는 보존했다. 후속 TASK-01에서 솔루션·AnyCPU DLL·x86/x64 검증 실행 파일을 구성하고 네 Debug/Release 빌드·실행을 확인했다. 명령과 완료 범위는 [DEVELOPMENT.md](DEVELOPMENT.md)에 기록한다. BTPL·SDK API·Tester와 실제 장치 시험은 후속 작업이다. 이슈의 실제 체크·종료 상태는 원격 이슈를 기준으로 한다.
