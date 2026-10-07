# 저장소 개발 환경과 기본 템플릿

공통 DLL은 .NET Framework 4.6.1, Tester는 WinForms / DevExpress 20.2.6을 사용한다. DLL과 최소 검증 프로젝트의 구성·빌드는 TASK-01에서 진행한다. 저장소 구조 검사 성공을 제품 빌드 또는 장치 시험 성공으로 해석하지 않는다.

## 기본 파일

| 파일 | 역할 |
| --- | --- |
| AGENTS.md | 작업별 문서 지도, 명령, 저장소 경계 |
| .gitignore | devflow 비공개 경로, VS 빌드 산출물, 로컬 참고 자료 제외 |
| .gitattributes | Git 텍스트 LF, cmd/bat CRLF, 바이너리 구분 |
| .editorconfig | 새 파일의 기본 인코딩·들여쓰기·줄바꿈 |
| .devflow.json | 작업 브랜치 형식·검사·참고 자료 보호 프로필 |
| .github/ISSUE_TEMPLATE/intent.md | devflow Issue의 의도·수용 기준·현재 상태 |
| .github/PULL_REQUEST_TEMPLATE.md | 변경 범위·검증·제한 기록 |
| tools/verify-repository.js | 설치된 devflow doctor 및 설계 기준·인수 ID 검사 |

통합 방식은 `push-on-request`다. 훅 활성화·전역 설치·저장소 권한 설정은 하지 않는다. 기존 계약이 docs의 개별 파일에 있으므로 specs/decisions 폴더를 새로 강제하거나 문서를 이동하지 않는다. `.devflow.json`의 `specs`와 `decisions`는 false이며 계약의 위치는 AGENTS.md에서 안내한다.

## 저장소 검사

Node.js는 설치된 devflow의 검사 실행에 사용한다. 제품 DLL에 Node.js 의존성을 넣지 않는다.

```text
node tools/verify-repository.js
git diff --check
```

정상 출력은 doctor의 `ok`와 `Repository checks passed`다. 실행 프로세스 제한은 각 30초다. 검사기는 기본적으로 Codex 플러그인 캐시의 devflow 0.3.0을 찾는다. 다른 설치 위치를 사용하는 환경에서는 `DEVFLOW_ROOT` 환경 변수를 설치된 devflow 플러그인 루트로 지정한다. 값은 로컬 세션에서 설정하며 저장소 파일에 사용자 절대 경로를 넣지 않는다.

기준 문서 해시는 현재 작업 파일이 아니라 `v0.1.1-design` 태그의 Git 내용과 비교한다. 후속 문서 수정은 태그를 바꾸지 않는 새 커밋으로 남긴다. 신규 clone에서 검사를 수행할 때 기준 태그도 가져와야 한다.

## 추적과 로컬 자료

Issue 본문·체크포인트와 장부는 설치된 devflow의 `devflow-state` 명령을 사용한다. 재개 카드의 Tool 줄 또는 설치 플러그인의 `bin/devflow-state`가 실행 경로다. 인수 없이 실행하면 사용법을 확인할 수 있다. 템플릿은 게시 권한을 새로 부여하지 않는다.

빌드 산출물, 검토 임시 폴더, 제조사 변환 문서, native DLL, 로컬 BTPL 참고 가이드와 그 검증 스크립트는 Git에서 제외한다. 기존 파일은 이동·삭제·수정하지 않는다. 공개 설계 문서에서는 이 로컬 자료를 파일명으로 안내하고, clone에 포함된 것처럼 링크하지 않는다.

Issue가 생기기 전 저장소 최초 구성을 위한 브랜치는 `chore/repository-bootstrap`을 사용한다. 이 예외는 현재 초기 템플릿 작업만 대상으로 한다. 이후 작업은 `<type>/<issue>-<slug>` 형식으로 연결된 Issue와 함께 진행한다. 실제 제품 빌드 명령과 CI는 프로젝트가 만들어지고 실행 증거를 확인한 뒤 추가한다.
