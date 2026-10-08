# U2BIO.LabelPrinting

RFID 값이 이미 기록된 BT-003 Pro 라벨에 프로그램이 전달한 텍스트를 BTPL로 인쇄하기 위한 공통 모듈과 테스트 프로그램입니다.

공통 DLL과 UI 없는 검증 프로젝트의 빌드 기반을 구성했고, 41개 내부 SDK 선언과 배포 검사·로더 코드를 추가했습니다. 현재 [Native SDK 구현 범위와 미검증 항목](docs/NATIVE_SDK_IMPLEMENTATION.md)을 구분합니다. 공개 장치 연결·인쇄 API와 DevExpress Tester는 후속 개발 작업입니다.

TASK-03의 공개 `BtplTemplate` 관리 API로 BTPL 해석·좌표/문구 편집·고정 줄바꿈·strict 인코딩·단일 파일 저장을 지원합니다. [사용 예와 저장 계약](docs/BTPL_TEMPLATE_API.ko.md)을 참고합니다. 이 API는 프린터 없이 동작하며 수치 편집·미리보기 화면은 후속 UI 작업입니다.

TASK-04는 `BtplBindings`, `BtplPrintRequest`, `BtplProcessor`로 JSON 매핑·DataTable 스냅샷·전체 관리 검사·행별 참고 미리보기 모델을 제공합니다. [바인딩·렌더링 API](docs/BTPL_BINDING_API.ko.md)를 참고합니다. DB 연결과 실제 출력은 하지 않으며 TASK-02의 미검증 제품 경계·장치 지원은 [후속 #10](https://github.com/wotjr1649/U2BIO.LabelPrinting/issues/10)에서 관리합니다.

구현 기준 버전은 **v0.1.1-design (2026-10-08)**입니다. [기준 문서 목록과 변경 규칙](docs/BASELINE.md), [개발 작업과 완료 조건](docs/IMPLEMENTATION_PLAN.md)을 기준으로 구현합니다. 이 버전은 설계 기준이며 제품 DLL의 릴리스 버전이 아닙니다.

| 구성 | 이름 / 기준 |
| --- | --- |
| 공통 DLL | `U2BIO.BT003PROLabelPrinting` |
| 테스트 프로그램 | `U2BIO.BT003PROLabelPrinting.Tester` |
| 플랫폼 | .NET Framework 4.6.1, Windows |
| 테스트 UI | WinForms, DevExpress 20.2.6 |
| 배포 | 관리 DLL AnyCPU, 제조사 DLL x86/x64 세트 |
| 기준 서식 | BTPL `.txt` / `.btpl` |
| 부가 설정 | JSON: 변수와 컬럼의 매핑, 출력 형식, `nullText` |
| 데이터 입력 | 호출 프로그램이 전달하는 `DataTable` |

[설계와 검증 계획](docs/DESIGN.md)에 공개 타입, 바인딩, 단계별 중단·결과·연결 수명, 저장·배포·명령 처리 계약과 24개 구현 인수 사례를 정리했습니다. [SDK 함수 대조표](docs/SDK_API_COVERAGE.md)는 내부 선언 대상 41개와 원형 미공개 export 1개, 기준 DLL 쌍의 해시를 관리합니다. 로컬 참고 자료 `docs/BTPL_API_GUIDE.ko.md`의 관련 내용을 대조했고, 일반 언어 기능과 이 제품의 1차 지원 범위를 구분했습니다.

저장소 기본 구성과 검사 명령은 [개발 환경](docs/DEVELOPMENT.md), GitHub 등록 현황은 [이슈 등록 상태](docs/GITHUB_ISSUES.md)를 참고합니다.

`U2BIO.LabelPrinting.sln`에는 관리 AnyCPU DLL과 `U2BIO.BT003PROLabelPrinting.Validation` 콘솔 프로젝트가 있습니다. `node tools/verify-build.js`로 Release x86/x64를 재빌드·실행하고, `--configuration Debug`로 Debug 구성도 확인합니다. 관리 템플릿 API 구현과 공개 인쇄 API 구현 완료를 구분합니다.

1차는 텍스트 출력에 집중하며 사용자가 선택한 `AW`를 기준으로 구현·검증합니다. USB를 우선 구현·검증하고 TCP/IP와 RS-232 연결 API도 제공합니다. DB 연결·조회·저장을 담당하지 않으며, 테스트 프로그램은 DB 없이 샘플 데이터로 동작합니다.

공통 DLL을 먼저 구현·검증한 다음 테스트 UI를 만듭니다. UI는 자유로운 텍스트 요소의 추가·삭제·수치 편집과 참고용 미리보기를 제공하며 같은 DLL의 검사·렌더링·인쇄 경로를 사용합니다. 수동 줄바꿈은 명시적 배치 간격으로 처리하고 자동 줄바꿈은 하지 않습니다.

관리 검사·미리보기는 프린터와 제조사 DLL 없이 실행합니다. `ValidateForPrint`가 실제 연결 대상의 검사까지 수행하며 Print는 이를 매번 다시 검사합니다. `AW` 등의 장치 지원 미확인과 관리 구문 오류를 구분하여 참고 미리보기와 인쇄 가능 여부를 따로 표시합니다. 검사를 안 했거나 완료하지 못한 결과는 null로 표현합니다.

템플릿 하나에 여러 데이터 행을 바인딩합니다. NULL/DBNull/빈 문자열/공백 값은 문자열 `nullText`를 적용하고, 설정이 없으면 검사 오류입니다. 컬럼 누락도 오류입니다. 전체 행을 먼저 검사하여 한 건이라도 오류면 전체를 출력하지 않습니다. 실행 중 실패·결과 불명이면 즉시 중단하고 남은 행은 미시도로 반환합니다. 취소 시 현재 SDK 호출의 결과를 기록하며 자동 재출력·대기열은 없습니다.

인쇄 함수 진입 전후를 `PrintInvoked`와 실행 단계로 구분합니다. 제한 초과 뒤 늦은 서식 등록이 성공해도 새 인쇄를 시작하지 않습니다. 이미 반환한 행 결과는 불변이며 늦은 정리 완료는 연결 상태 조회로 확인합니다. Dispose 후에도 pending·자원 정리 상태 조회는 가능합니다.

고정 줄바꿈은 `ExpandFixedLines`로 실제 BTPL 명령에 저장합니다. 가변 줄 간격은 호출 프로그램의 실행 옵션이며 바인딩 JSON에 추가하지 않습니다. Tester에서는 현재 세션만 유지하고 재시작/다른 서식 로드 뒤 필요한 간격을 다시 입력합니다. 누락 상태의 출력은 차단합니다. 파일 저장 충돌·실패는 표시하고 기존 파일 삭제로 우회하지 않습니다.

실행 프로그램 폴더에 실제 프로세스와 같은 x86 또는 x64의 `BlackfishNfcLib.dll`·`HFModule.dll` 한 쌍을 배치합니다. Tester의 두 비트 수 출력은 별도 폴더로 분리합니다. 파일·비트 수·해시·모듈 충돌·로드·export 오류는 연결 오류로 반환하며 전역 설치나 검색 경로 변경은 추가하지 않습니다.

RFID 읽기·쓰기·잠금·보안 설정은 공개하지 않습니다. 텍스트 넘침 검사와 별도 DPI 설정도 제공하지 않습니다.

`JODAL_WEBFORM_5.txt`의 `~HE0,0`은 RFID 안테나 설정으로 확인되어 인쇄 전용 서식에서는 제외합니다. 원본은 보존하며 지원하지 않는 명령을 자동 삭제하지 않습니다. TASK-02에서 고정 SDK의 x86/x64 USB 연결·해제, AW size10/weight1·ANSI949 한글·한 장 출력과 전용 응답을 확인했습니다. 응답0은 SDK 정상 응답이며 배출 완료와 구분합니다. 정상42-byte/NUL 분석 상한과 격리 진단의 제한은 [검증 상태](docs/NATIVE_SDK_IMPLEMENTATION.md)를 따릅니다. 제품 실행·보호 경계·결과 통합은 TASK-05/06 전까지 차단하고, RFID 저장값 전후 실측은 사용자 확정으로 제외했습니다. 다른 SDK/환경·용지·펌웨어·모든 장비 오류는 검증 완료로 표시하지 않습니다.

SDK 원본은 상위 작업 폴더의 `BT-003 pro SDK`를 참고합니다. 기존 `docs`의 제조사 문서는 보존합니다. 이 설계 문서 작성 단계에서는 원격 push나 제조사 DLL의 업로드를 수행하지 않습니다.
