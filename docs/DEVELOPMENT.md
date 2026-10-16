# 저장소 개발 환경과 검증

유지보수자용 안내다. 일반 사용자는 [README의 읽는 순서](../README.md#무엇부터-읽으면-되나요)를 따른다. 공통 DLL은 .NET Framework 4.6.1, Tester는 WinForms / DevExpress 20.2.6을 사용한다. 소스 빌드·관리 검사·실제 SDK·물리 시험을 구분한다.

## 기본 파일

| 자료 | 역할 |
| --- | --- |
| `AGENTS.md` | 작업에 따른 문서 안내와 안정적인 제품·보호 경계 |
| `docs/specs/SYNC_USB_MVP.md`, `TXT_TEMPLATE_EDITOR.md`, `DEVEXPRESS_TESTER.md` | 인쇄·TXT·Tester 구현 계약 |
| `docs/DESIGN.md` | 내부 연결·인쇄·정리 구조 |
| `docs/BASELINE.md` | 구현 전 기준 태그와 해시 |
| `docs/SDK_API_COVERAGE.md`, 제품 `Sdk/README.ko.md` | 제조사 선언·파일 출처·고정 해시 및 과거 대조 |
| `docs/design/decisions/`, `docs/history/`, `docs/IMPLEMENTATION_PLAN.md` | 결정 이유·당시 계획·과거 검증 근거 |
| `.devflow.json`, `.github/` 템플릿 | Issue 연결·검사·공개 기록 정책 |
| `tools/verify-repository.js`, `tools/verify-build.js` | 저장소 구조 검사와 전체 빌드·관리 검증 |

## 저장소 검사

```text
node tools/verify-repository.js
git diff --check
```

정상 출력은 doctor의 `ok`와 `Repository checks passed`다. Node.js는 개발 검사 도구이며 제품 DLL의 런타임 의존성이 아니다. 검사기는 기본으로 설치된 devflow 0.3.0 경로를 찾는다. 설치 위치가 다르면 `DEVFLOW_ROOT`를 설치된 플러그인 루트로 **프로세스 환경**에 지정하고 개인 절대 경로를 저장소에 쓰지 않는다.

문서 검사는 `BASELINE.md`의 네 해시를 현재 문서가 아니라 `v0.1.1-design` 태그의 Git 내용과 비교한다. 신규 clone에는 기준 태그도 가져온다. 문서를 고쳤다는 이유로 기준 태그·해시를 갱신하지 않는다. 검사기가 직접 읽는 문서 경로와 README/AGENTS/DESIGN/IMPLEMENTATION_PLAN/PRINTER_API의 `SYNC_USB_MVP.md` 참고 링크를 유지한다.

## 추적과 로컬 자료

변경은 연결된 Issue와 `<type>/<issue>-<slug>` 전용 브랜치에서 진행한다. Issue I/O와 장부는 설치된 `devflow-state`를 사용하며, 본문·체크포인트에는 공개 가능한 요약만 남긴다. 통합 방식은 `push-on-request`다. 설정 파일이나 템플릿만으로 push·merge·배포 권한이 생기지는 않는다.

`.devflow.json`의 `specs`와 `decisions`가 false여도 기존 계약·결정 문서를 삭제하거나 비공개로 만든다는 뜻은 아니다. 현재 구조와 경로를 유지하고 필요한 자료를 작업별로 읽는다.

빌드 산출물·비공개 계획·검토 보고서·변환한 제조사 문서와 로컬 참고 자료는 Git에서 제외한다. 제조사 native 파일은 제품 `Sdk/x86`, `Sdk/x64`의 고정 네 파일만 추적한다. 원본·사용자 서식·다른 worktree를 정리 명목으로 바꾸지 않는다.

`tests/`와 `examples/OfflineSmoke/`는 **개발 소스와 자동 검증에 보존**한다. Validation은 솔루션에 포함되고 SdkHarness와 OfflineSmoke는 검사기가 별도로 빌드·실행한다. 제품 배포에서 제외한다는 정책을 소스 삭제·추적 해제·검사 생략으로 구현하지 않는다. 추적된 개발 자료는 사용자 필독에서 빠져도 공개 상태이며 `.gitignore` 추가로 기존 Git 이력이 비공개가 되지는 않는다.

## DLL 기반 빌드와 실행 검사

Visual Studio/MSBuild와 .NET Framework 4.6.1 targeting pack이 필요하다. 솔루션 전체에는 DevExpress 20.2.6이 설치된 개발 PC가 필요하지만 공통 DLL에는 DevExpress·DB 의존성이 없다.

```text
node tools/verify-build.js
node tools/verify-build.js --configuration Debug
node tools/verify-build.js --scope txt
```

기본 명령은 Release x86/x64를 재빌드한다. DLL은 AnyCPU, 검증 exe와 Tester는 x86/x64이며 Any CPU 솔루션 구성은 exe를 x86으로 매핑한다. `--scope txt`도 현재 TXT와 공통 프린터 범위를 검사하고 harness·OfflineSmoke를 생략하지 않는다. Debug와 범위를 함께 지정하려면 `node tools/verify-build.js --configuration Debug --scope txt`처럼 범위를 마지막에 둔다.

| 검사 | 확인하는 내용 |
| --- | --- |
| 실제 제품 DLL의 Validation | Framework·PE·실행 비트수·공개 API·원본 Wrapper 선언·TXT 편집/저장 |
| 제품 두 소스를 링크한 SdkHarness | SDK 대체 호출로 순서·실패 중단·소유권·파일 잠금·배출/복귀·정리 수명 |
| OfflineSmoke | 독립 호출자의 TXT 편집/저장·표시용 변수 치환·미연결 API·합성 DataTable 예시 |
| Tester `--self-check` | 화면·SDK 초기화 전 설정·서식·표시/입력 규칙 |
| 배포 파일 대조 | 관리 DLL 동일성과 해당 플랫폼 제조사 DLL 두 파일의 고정 해시 |

이 검사는 제조사 SDK를 로드하거나 장치 인쇄·이송을 하지 않는다. 성공 출력은 각 검사 PASS와 `Build checks passed`다. SDK 응답·실제 출력 완료·사용자 관측은 별도 근거다.

검사기는 vswhere로 MSBuild를 찾고 평가된 프로젝트 설정·컴파일러·targeting pack과 산출물 해시를 기록한다. 각 하위 프로세스는 120초·출력 2 MiB 제한이며 제한 초과는 실패다. 도구·로그 실제 경로와 프로세스 종료를 확인하고 compiler 공유와 MSBuild node reuse를 끈다. 명령·종료 코드·revision·dirty 상태·로그·관리 DLL/검증 exe 사본은 실행별 `.work/foundation/`에 남긴다. dirty 결과는 해당 작업 파일의 검사이며 같은 커밋 자체를 검증한 결과로 바꾸어 쓰지 않는다.

TASK-01 당시의 기반 DLL·missing-library 시험은 [BUILD_EVIDENCE](history/BUILD_EVIDENCE.md), 초기 native 단계는 [NATIVE_SDK_IMPLEMENTATION](history/NATIVE_SDK_IMPLEMENTATION.md)에 보존한다. 당시의 미구현/미검증은 현재 제품 상태가 아니다. 현행 API는 [프린터 안내](PRINTER_API.ko.md)를 따른다.

## SDK 배포용 빌드

Visual Studio Developer Command Prompt에서 실행한다.

```text
MSBuild src\U2BIO.BT003PROLabelPrinting\U2BIO.BT003PROLabelPrinting.csproj /t:Rebuild /m:1 /nr:false /p:Configuration=Release /p:Bt003NativePlatform=x86
MSBuild src\U2BIO.BT003PROLabelPrinting\U2BIO.BT003PROLabelPrinting.csproj /t:Rebuild /m:1 /nr:false /p:Configuration=Release /p:Bt003NativePlatform=x64
```

출력은 제품 프로젝트의 `bin/x86/Release/` 또는 `bin/x64/Release/`이며 관리 DLL과 지정한 제조사 DLL 두 개가 생성된다. Debug는 Configuration을 바꾼다. `Bt003NativePlatform`을 생략하면 `bin/<Configuration>/`에 관리 DLL만 빌드하며 기존 출력의 SDK를 지우지는 않는다. 오프라인 검사에는 SDK가 없는 별도 검증 출력 폴더를 사용한다.

`ProjectReference` 호출 앱을 빌드할 때도 `/p:Platform=x64 /p:Bt003NativePlatform=x64`처럼 실제 실행 비트수를 명시하면 SDK 복사가 앱 출력으로 전달된다. 파일 참조 앱은 직접 배포한다. Tester는 솔루션의 제품 재빌드 중복을 피하려고 `Sdk/<Platform>` 두 파일을 직접 링크한다. 실행 폴더와 제품 배포 제외 목록은 [배포 안내](SDK_DEPLOYMENT.ko.md)에 있다.

## 서식 Tester 실행

빌드 출력의 Tester.exe를 사용한다. 사용자 조작법과 설정·저장·반복 인쇄·복구는 [Tester 사용](PRINT_PRESENTATION.ko.md#tester-사용), 화면·내부 처리·SelfCheck의 구현 계약은 [Tester 설계](specs/DEVEXPRESS_TESTER.md)에 있다. SDK 로딩·연결·인쇄·FEED는 실기 승인과 직접 관측이 있는 시험에서만 실행한다.
