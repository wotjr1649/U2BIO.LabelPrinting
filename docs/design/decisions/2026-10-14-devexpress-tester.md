# DevExpress 서식 Tester의 구조

2026-10-14 사용자 grilling 결정. 이슈 #8. [설계 기준](../../specs/DEVEXPRESS_TESTER.md)이 구조와 동작의 기준이다.

사용자는 다음을 정했다.
- **용도**: 서식 담당자용 도구로 쓴다.
- **화면**: phase2 환경설정의 외관과 조작을 따르고, Designer 파일로 만든다.
- **설정**: 포트·배출 거리·서식 폴더·최근 파일만 사용자 로컬 앱 데이터 폴더의 XML 하나에 둔다.
- **속도·농도**: 서식의 `^S`·`^H`로만 다룬다. 제조사 매뉴얼상 서식 값이 SetPrintingOption보다 우선한다.
- **용지 실측값**: 편집기 세션에서만 유지한다.

| 결정 | 대안 | 판단 |
| --- | --- | --- |
| 프로젝트 구성 | 화면 안에 모든 규칙 / 별도 Core 라이브러리 + tests 검사 / **WinExe 하나 + 화면 밖 작은 클래스 두 개 + `--self-check`** | 첫째는 저장 실패 규칙을 장치·화면 없이 검사할 수 없다. 둘째는 프로젝트와 공개 경계가 하나 더 늘어난다. 셋째를 채택했다. |
| SDK native 복사 | 제품 `ProjectReference`에 `Bt003NativePlatform` 전달 / **Tester가 `Sdk\$(Platform)` 두 파일을 링크해 복사** | 첫째는 솔루션 빌드에서 제품을 다른 전역 속성으로 두 번 빌드하고 obj 폴더를 같이 쓴다. 둘째는 한 ItemGroup으로 끝나고 verify-build가 해시를 검사한다. 둘째를 채택했다. |
| 설정 형식 | XmlSerializer / 장비 프로그램의 DataSet 형식 / **XDocument** | XmlSerializer는 실행 중 임시 어셈블리를 생성한다. DataSet은 값 네 개에 비해 형식이 무겁다. XDocument를 채택했다. |
| 장치 호출 | UI 스레드 동기 호출 / BackgroundWorker / 전용 스레드 / **async·await `Task.Run` + 작업 하나만 허용** | 첫째는 2초 간격 대기 동안 화면이 멈춘다. BackgroundWorker와 전용 스레드는 코드가 늘지만 얻는 것이 없다. 제조사 샘플도 연결과 인쇄를 서로 다른 스레드에서 한다. 넷째를 채택했다. |
| 화면 작성 | 코드로 구성 / **Designer 파일** | 사용자 결정이다. 팀이 VS 디자이너로 배치를 고칠 수 있다. |

**버릴 시험 프로젝트로 먼저 확인한 사실**
- verify-build와 같게 PATH·TEMP를 제한한 환경에서 DevExpress 20.2.6 WinExe가 licx를 포함해 x86/x64로 빌드된다.
- WinExe의 표준 출력과 종료 코드를 그대로 받을 수 있다.
- `Sdk\$(Platform)`의 두 파일과 DevExpress DLL이 실행 폴더에 복사된다.

**남은 위험**
- Designer 화면 배치와 키 동작은 빌드로 증명되지 않는다. 실제 화면에서 확인한다.
- 작업 스레드에서의 SDK 호출과 미리보기 글자 크기는 실기에서 관측한다.
