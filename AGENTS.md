# U2BIO.LabelPrinting

현재 작업은 이슈 #16의 동기 USB 인쇄 MVP다. [스펙](docs/specs/SYNC_USB_MVP.md), [공개 API](docs/PRINTER_API.ko.md), [계획](docs/IMPLEMENTATION_PLAN.md)을 따른다. #15의 공개 API 호환·포인터 bridge 유지 조건은 사용자 결정으로 대체됐다.

- 기본 제품 코드는 Bt003Printer.cs와 PrinterModels.cs 두 파일이다. 제조사 HFSample Wrapper 원본 39개 선언을 직접 사용한다. SDK 원본·바이너리·매뉴얼은 읽기 전용이다.
- 호출 순서·실패 중단·자원 수명의 이유를 한글 주석으로 설명한다. 자체 로더·실행 프레임워크·BTPL 해석기를 추가하지 않는다. Templates는 선택 기능으로 보존한다.
- 한 프로그램에서만 물리 프린터를 사용한다. 프로세스 안의 단일 연결과 직렬화를 유지한다. SDK 정상 응답은 물리 출력 완료 증거가 아니다.
- 서식의 ^Q·배출 거리·인쇄 영역을 정하거나 실기를 준비할 때는 [운영 기준](docs/PRINT_PRESENTATION.ko.md#운영-기준)을 따른다. ^Q의 x는 마크를 뺀 라벨 길이이며, 값 하나가 바뀌면 프린터 설정과 정지 위치까지 바뀐다.
- SDK 로딩·장치 인쇄·배출은 사용자가 그 시험을 승인하고 출력을 직접 관측할 때만 실행한다. push·merge·배포는 대상과 효과를 지정한 사용자 지시가 필요하다. 사용자의 다른 worktree 변경은 보존한다.
- Issue 읽기·쓰기는 설치된 devflow-state를 사용한다. 공개 기록에는 검토한 요약과 합성 증거만 넣으며 경로·라벨 값·UID·비밀 정보는 넣지 않는다.

## Commands

검사: node tools/verify-build.js, node tools/verify-repository.js, git diff --check. DEVFLOW_ROOT는 설치된 플러그인 위치를 프로세스 환경으로 지정한다. 관리 대체 harness와 실제 제품 DLL의 메타데이터 검사는 SDK 실기와 구분한다.

## Boundaries

배포: 호출자는 관리 DLL을 참조하고 실제 프로세스 비트수와 맞는 BlackfishNfcLib.dll/HFModule.dll 두 파일을 실행 폴더에 둔다. 바이너리 포함·추출이나 COM 등록은 하지 않는다.
