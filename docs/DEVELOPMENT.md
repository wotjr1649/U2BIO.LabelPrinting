# 저장소 개발 환경과 기본 템플릿

공통 DLL은 .NET Framework 4.6.1, Tester는 WinForms / DevExpress 20.2.6을 사용한다. TASK-01의 솔루션·DLL·최소 검증 콘솔 프로젝트를 구성했다. 저장소 구조 검사, 제품 기반 빌드 검사, 실제 장치 시험의 증거를 구분한다.

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
| U2BIO.LabelPrinting.sln | Debug/Release × Any CPU/x86/x64 구성. DLL은 AnyCPU, 검증 exe는 x86/x64이며 Any CPU solution은 검증 exe를 x86으로 매핑 |
| src/U2BIO.BT003PROLabelPrinting | 공통 DLL 프로젝트, internal SDK 선언·배포 검사·로더 구현 중 |
| tests/U2BIO.BT003PROLabelPrinting.Validation | DLL 독립 로드·framework·PE·실행 비트 수 검사 |
| tools/verify-build.js | 실제 재빌드·실행, 평가된 설정과 산출물 증거 기록 |

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

빌드 산출물, 검토 임시 폴더, 제조사 변환 문서, 로컬 BTPL 참고 가이드와 그 검증 스크립트는 Git에서 제외한다. native DLL은 `src/U2BIO.BT003PROLabelPrinting/Sdk`의 고정 x86/x64 런타임 네 파일만 예외로 관리한다. 다른 SDK 사본과 기존 참고 자료는 이동·삭제·수정하지 않는다. 공개 설계 문서에서는 제외된 로컬 자료를 파일명으로 안내하고, clone에 포함된 것처럼 링크하지 않는다. [SDK 빌드·배포](SDK_DEPLOYMENT.ko.md)는 선택한 비트 수의 파일을 앱 출력 폴더에 복사하는 방법을 설명한다.

Issue가 생기기 전 저장소 최초 구성에는 `chore/repository-bootstrap`을 사용했다. 이후 작업은 `<type>/<issue>-<slug>` 형식으로 연결된 Issue와 함께 진행한다. 실제 빌드 명령은 아래에 기록하고 CI는 별도 구성 작업으로 남긴다.

## DLL 기반 빌드와 실행 검사

TASK-02의 추가 검사와 native 실행 경계는 [NATIVE_SDK_IMPLEMENTATION.md](history/NATIVE_SDK_IMPLEMENTATION.md)를 따른다. 아래 최초 missing-library 시험과 [BUILD_EVIDENCE.md](BUILD_EVIDENCE.md)는 TASK-01 당시의 근거다.

검증한 커밋·구성별 결과·산출물 해시는 [BUILD_EVIDENCE.md](BUILD_EVIDENCE.md)에 기록한다.

```text
node tools/verify-build.js
node tools/verify-build.js --configuration Debug
node tools/verify-build.js --scope txt
```

첫 명령은 Release의 x86/x64 두 구성을 재빌드하고 각각의 검증 실행 파일을 실행한다. 두 번째 명령은 Debug에 같은 검사를 적용한다. MSBuild를 vswhere로 설치된 Visual Studio에서 찾고 .NET Framework 4.6.1 targeting pack 존재 여부를 검사한다. 이번 구성에서 사용한 MSBuild는 16.11.6.22506, C# compiler는 3.11.0-4.26426.1이다. C# 언어 기준은 7.3이며 외부 NuGet 패키지·restore·DevExpress·DB 접속 설정을 추가하지 않았다.

실제 MSBuild 호출 인수는 다음과 같다. 설치된 MSBuild 실행 파일에 전달하며 호스트별 절대 경로는 공개 문서에 저장하지 않는다.

```text
U2BIO.LabelPrinting.sln /t:Rebuild /m:1 /nr:false /nologo /v:minimal /p:Configuration=Release /p:Platform=x86 /p:UseSharedCompilation=false
U2BIO.LabelPrinting.sln /t:Rebuild /m:1 /nr:false /nologo /v:minimal /p:Configuration=Release /p:Platform=x64 /p:UseSharedCompilation=false
```

Debug는 Configuration 값을 Debug로 바꾼다. DLL은 `src/U2BIO.BT003PROLabelPrinting/bin/<Configuration>/U2BIO.BT003PROLabelPrinting.dll`에 생성한다. 검증 프로그램과 참조된 DLL 사본은 `tests/U2BIO.BT003PROLabelPrinting.Validation/bin/<x86 또는 x64>/<Configuration>/`에 각각 생성한다. 검증 실행 파일에는 해당 플랫폼의 `--expect-x86` 또는 `--expect-x64` 인수를 전달한다.

MSBuild가 평가한 TargetFrameworkVersion·PlatformTarget·Prefer32Bit를 확인한 뒤 실제 어셈블리의 TargetFrameworkAttribute·PE와 실행 프로세스 비트 수를 검사한다. 검증 DLL은 실행 파일과 같은 폴더의 정확한 경로에서 로드한다. DevExpress/DB provider 참조와 프린터 native 모듈 로드 여부도 검사한다. 성공 출력은 `PASS: framework=4.6.1 library=AnyCPU process=x86 또는 x64 nativeSDK=not-loaded`와 `Build checks passed`다.

빌드·검증 각 프로세스는 120초, 출력은 2 MiB로 제한하며 실패·제한 초과는 검사 실패다. 제한 초과 시 해당 프로세스 트리 종료를 요청하고 child 종료를 최대 5초 확인하며 미확인은 실패로 표시한다. 공유 compiler와 MSBuild node reuse는 끈다. 하위 프로세스에는 필요한 Windows·도구 환경만 전달하고 TEMP/TMP는 실행별 작업 폴더로 지정한다. 실행 도구와 로그 폴더는 실제 경로를 확인하며 저장소 밖으로 이어지는 로그 junction을 거절한다.

로그·선택한 도구·명령·종료 코드·Git 커밋·작업 트리 변경 여부·targeting pack 식별 해시·산출물 경로와 SHA-256은 `.work/foundation/` 아래 실행별 폴더에 저장한다. DLL과 검증 exe 사본도 실행별 x86/x64 폴더에 새 파일로 보존하여 다음 Rebuild가 이전 실행의 사본을 덮어쓰지 않는다. workingTreeDirty=true인 결과는 현재 작업 파일의 검증이며 해당 Git 커밋 자체를 검증한 증거로 사용하지 않는다. 로그는 UTF-8과 이 환경의 CP949 출력을 판별하여 읽으며 호스트의 코드 페이지 설정은 바꾸지 않는다.

DLL을 만들기 전 같은 Program.cs를 .NET Framework 4.6.1 참조로 standalone x86 exe로 컴파일·실행하여 `MANAGED_LIBRARY_MISSING` 및 종료 코드 3을 확인했다. 시험 소스를 커밋하고 devflow 시험 잠금을 적용한 뒤 DLL을 추가했다. Release/Debug × x86/x64의 네 실제 빌드·실행이 통과했다. DLL은 현재 공개 인쇄 API가 없는 기반 어셈블리이며 실제 출력·RFID 보존·ABI 검증은 TASK-02 이후에 수행한다. 문서 기준 태그는 그대로 보존한다.

`--scope txt`는 TXT 파싱/저장과 공통 연결·인쇄·배출/복귀 계약을 x86/x64로 검사한다. TXT 전용 재설계 후 JSON/XML·바인딩 API는 제거되며 미뤄 둔 검사로 기록하지 않는다. 기본 전체 검사와 --scope txt는 모두 현재 TXT/프린터 범위를 검사한다. 범위 인수는 마지막에 지정하고 Debug와 함께 사용하려면 `node tools/verify-build.js --configuration Debug --scope txt`를 사용한다. 검증 exe에는 `--expect-x86 --txt-only` 또는 `--expect-x64 --txt-only`를 전달한다. 관리 검사는 실제 SDK/장치 인쇄를 실행하지 않는다.
