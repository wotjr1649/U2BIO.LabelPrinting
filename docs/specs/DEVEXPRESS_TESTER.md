# DevExpress 서식 Tester 설계

이슈 #8의 설계 기준이다. 서식 담당자가 DB 없이 TXT 서식을 편집하고 Tester 화면에서 실제로 인쇄해 확인하는 도구다. 인쇄·위치·서식 처리는 [동기 USB MVP](SYNC_USB_MVP.md)와 [TXT 서식 편집 API](TXT_TEMPLATE_EDITOR.md)의 공개 API만 호출한다. 대안 비교는 [결정 기록](../design/decisions/2026-10-14-devexpress-tester.md)에 있다.

## 프로젝트와 빌드

`src/U2BIO.BT003PROLabelPrinting.Tester`에 WinExe 프로젝트 하나를 두고 솔루션에 넣는다.

| 항목 | 값 |
| --- | --- |
| 대상 | .NET Framework 4.6.1, C# 7.3, `PlatformTarget=$(Platform)`, `Prefer32Bit=false`, 경고를 오류로 처리 |
| 출력 | `bin\$(Platform)\$(Configuration)\`. x86/x64 이외의 플랫폼은 빌드 오류 |
| 제품 DLL | OfflineSmoke와 같은 `ProjectReference`(`Platform=AnyCPU`) |
| DevExpress | 20.2.6.0 GAC 참조, `Private=True`로 실행 폴더에 복사(phase2와 같음). `Properties\licenses.licx`를 포함 |
| SDK native | `..\U2BIO.BT003PROLabelPrinting\Sdk\$(Platform)\`의 두 파일을 `None` 링크로 실행 폴더에 복사한다. 결과는 [SDK 배포](../SDK_DEPLOYMENT.ko.md)의 실행 폴더 구성(같은 비트수 한 쌍)과 같다. `Bt003NativePlatform`이 지정됐는데 Platform과 다르면 빌드 오류로 막는다. |
| 솔루션 | x86→x86, x64→x64, Any CPU→x86. 빌드 설정 메시지 `BUILD_SETTINGS|...`를 다른 프로젝트와 같은 형식으로 출력 |

verify-build는 기존 검사를 그대로 두고 플랫폼마다 다음을 추가한다.
- Tester의 빌드 설정 메시지를 확인한다.
- `--self-check --expect-<platform>`을 실행한다.
- 실행 폴더의 SDK 두 파일이 고정 해시와 같은지 확인한다.
- 실행 폴더의 관리 DLL이 검증한 제품 DLL과 같은지 확인한다.

이 빌드에는 DevExpress 20.2.6이 설치된 개발 PC가 필요하다.

## 파일 구성

| 파일 | 책임 |
| --- | --- |
| Program.cs | `--self-check`이면 화면 초기화 전에 SelfCheck로 분기한다. 아니면 phase2 Checkup 순서로 초기화한다: 스킨 등록, 예외 처리, 스킨 VS2010, 기본 글꼴 굴림 9pt. |
| FrmMain(.Designer).cs | 편집·미리보기·연결·인쇄 화면. Bt003Printer 하나와 TemplateDocument 하나를 가진다. |
| FrmSettings(.Designer).cs | phase2 환경설정 모양의 설정 화면 |
| TesterSettings.cs | 설정 값과 XML 읽기·쓰기. 파일 경로는 인수로 받는다. |
| UsbPrinterList.cs | Windows USB 프린터 드라이버(usbprint) 장치 목록을 읽어 포트 번호와 장치 이름을 만든다. SDK를 쓰지 않는다. |
| TemplateDocument.cs | 열린 서식의 상태(현재 BtplTemplate, 경로, 수정 여부, 마지막 열기·저장 시각)와 저장 규칙. 미리보기용 변수 치환 함수도 둔다. |
| SelfCheck.cs | 장치 없는 검사 |

화면 파일은 Visual Studio 디자이너로 열 수 있는 Designer 형식으로 만든다. 화면 밖에서 검사해야 하는 규칙은 TesterSettings, TemplateDocument, UsbPrinterList에만 둔다.

## 설정

- 위치: `Environment.SpecialFolder.LocalApplicationData` 아래 `U2BIO\BT003Tester\settings.xml` 하나. x86/x64가 같이 쓴다.
- 형식: XDocument로 쓴 버전 1 XML이다.

```xml
<TesterSettings version="1">
  <Port>0</Port>
  <PresentationDistanceMm>15</PresentationDistanceMm>
  <TemplateFolder></TemplateFolder>
  <RecentFiles><File>…</File></RecentFiles>
</TesterSettings>
```

| 항목 | 기본값 | 검사 |
| --- | --- | --- |
| Port | `0`(운영 USB 포트) | 빈 값과 제어문자 거부 |
| PresentationDistanceMm | 15 | 정수 0~1000 |
| TemplateFolder | 빈 값 | 빈 값이거나 있는 폴더 |
| RecentFiles | 없음 | 최대 10개. 대소문자를 무시해 중복을 없애고 최근 것이 앞에 온다. |

**읽기**
- 파일이 없으면 기본값으로 만든다.
- 읽을 수 없거나 값이 잘못됐으면 경고하고 기본값으로 시작한다. 이때 사용자가 설정 화면에서 확인을 누르기 전까지 기존 파일을 덮어쓰지 않는다. 그동안 최근 파일 목록은 메모리에서만 갱신하고, 확인을 누를 때 함께 저장한다.

**쓰기**
- 같은 폴더의 임시 파일에 먼저 쓴 뒤 교체한다.
- 연결 중에 설정을 바꾸면 다음 연결부터 적용된다. 상태 줄에는 연결할 때 쓴 값을 표시한다.

## 서식 상태와 저장

TemplateDocument는 화면과 독립된 규칙을 가진다.
- **편집**: `Apply(편집 함수)`가 성공하면 결과 객체를 채택하고 수정됨으로 둔다. DLL 예외가 나면 이전 객체를 그대로 둔다.
- **Save·SaveAs**: 성공하면 반환 객체를 채택하고 경로·수정 여부·시각을 갱신한다. 실패하면 상태를 바꾸지 않는다. 같은 경로를 SaveAs 대상으로 고르면 DLL의 SameSavePath를 받아 Save를 안내한다. 덮어쓰기는 화면이 확인을 받은 경우에만 `overwrite: true`로 호출한다.
- **SourceChanged**: 다시 읽기, 다른 이름 저장, 편집 유지 중 하나를 고르게 한다. 원본이 삭제된 경우는 다시 읽기를 제외한다.
- **버리기 전 확인**: 열기, 최근 파일 열기, 닫기, 종료 전에 수정됨이면 저장/저장 안 함/취소를 묻는다. 저장이 실패하거나(SourceChanged 포함) 취소되면 전환이나 종료를 하지 않는다. 세 경우 모두 같은 함수 하나로 처리한다. SourceChanged 다시 읽기는 같은 경로 저장이 항상 다시 충돌하므로 "편집 버리고 다시 읽기/취소"만 묻는다.
- **닫기**: 서식을 내려놓고 처음 실행한 상태로 돌아간다. 블랙마크 계산 입력과 가변데이터는 편집기 세션 값이라 그대로 둔다.
- **열린 서식이 없을 때**: 경로 줄은 "열린 서식 없음"을 표시하고 편집·인쇄 버튼을 막는다. 새 서식 만들기 기능은 없다. 기존 TXT를 열어 다른 이름으로 저장해 만든다.
- **경로가 없는 서식**: `FilePath`가 null이면 경로 칸에 "저장 안 됨"을 표시한다. 현재 버전은 `Load`로만 열기 때문에 이 상태가 생기지 않는다.

## 메인 화면

LayoutControl 하나가 창 전체를 채운다. 왼쪽 열(4·5)과 오른쪽 탭(6) 사이에 끌 수 있는 구분선(SplitterItem)을 두며, 처음 비율은 760:511이다. 열 너비는 글자 길이로 바뀌지 않는다.

1. **도구 모음**: 열기(DropDownButton + DXPopupMenu로 최근 파일 전체 경로), 저장, 다른 이름 저장, 닫기 / 연결, 해제, 상태 / 위치 확인, FEED+확인("#19 실기 확인 전" 표시) / 설정
2. **현재 파일 줄**: 읽기 전용 TextEdit(전체 경로, 툴팁), 수정 표시, 마지막 열기·저장 시각. 창 제목에는 `파일 이름 *`를 표시한다.
3. **공통 오류 줄**: 입력 칸에 속하지 않는 오류를 표시하는 LabelControl 하나다.
   - 대상: 열기·편집·저장·인쇄 준비에서 나는 BtplFormatException과 IO 오류
   - 표시 내용: `Code`, `TemplateLine`(있을 때), 메시지
   - 추가 안내: `AmbiguousFrame`·`AmbiguousSetting`·`LockedTextArguments`이면 "외부 편집기에서 고친 뒤 다시 여세요"라고 붙인다.
   - 셀 입력 오류는 이 줄이 아니라 해당 칸의 `e.ErrorText`에 표시한다.
4. **왼쪽: 참고 미리보기**
   - 표시 기준: 블랙마크 계산 입력으로 구한 LabelArea(실선)와 SafeArea(점선), TextElements의 문구. 범례는 "계산 입력 기준"으로 적어 TXT에서 실측값을 복원한 것처럼 보이지 않게 한다. 계산 입력이 잘못됐으면 영역을 그리지 않는다.
   - 글자 크기: 높이는 `size * 340 / 72` dot(SDK LOGFONT 계산)이다. 굵기는 1이면 보통, 2 이상이면 굵게, 글꼴은 굴림이다.
   - 정렬·회전은 반영하지 않는다.
   - 미지원 줄: 미리보기 아래 고정 너비 줄에 `UnsupportedLineNumbers`의 줄 번호를 빨간색으로 나열하고, 없으면 "미지원 줄: 없음"을 회색으로 표시한다. 넘치는 글자는 `…`로 줄이고 툴팁에 전체를 보인다.
5. **왼쪽 아래: AW 목록**: GridControl(줄, X, Y, 크기, 굵기, 문구)과 추가·삭제 버튼. 셀 값이 거부되면 ValidatingEditor의 `e.ErrorText`에 DLL 오류 코드를 표시한다.
6. **오른쪽: XtraTabControl**
   - 용지·인쇄: VGrid로 `^Q`(세 인수 문자열), `^W`, `^S`(2~7), `^H`(0~19)를 편집한다.
   - 블랙마크 계산: VGrid로 받침지 너비, 마크 사이 길이, 마크 길이, 마크~라벨 간격, 라벨 너비·길이(mm), dpi(203/300), 센서·세로·가로 보정을 입력한다. 너비는 급지 방향에 수직, 길이는 급지 방향 치수이며 행마다 측정 방법을 툴팁으로 보인다. 시작값은 운영 기준(70, 35, 5, 7.5, 60, 20, 300, 1.5, 3.6, −1.0)이다. [서식에 반영]을 누를 때만 `^Q`·`^W`를 바꾼다.
   - 원문: 읽기 전용 MemoEdit로 `ToBtpl()`을 보여 준다.
   - `^Q`가 바뀌면(블랙마크 반영이든 VGrid 직접 편집이든) "새 서식으로 한 장 인쇄한 뒤 물리 FEED 키와 위치 확인으로 정렬하세요"라고 안내한다.
7. **아래: 가변데이터와 인쇄**
   - 가변데이터 TextEdit: 빈 칸이면 null을 보내고, 입력이 있으면 그대로 보낸다. 옆에 "!!로 구분, 변수 서식은 V00부터 필요한 값을 매번 입력"이라는 고정 안내를 둔다.
   - [편집 중 서식 인쇄]: 누르는 순간 `ToBtpl()`을 고정한 값으로 `Print`를 호출한다.
   - [저장된 파일 인쇄]: `PrintFile`을 호출한다. 수정됨 상태이면 저장하지 않은 수정은 반영되지 않는다고 확인을 받는다.
   - 두 인수 `^Q` 차단: 인쇄할 서식에 인수 두 개짜리 `^Q`가 있거나, 미지원 줄(`UnsupportedLineNumbers`) 중 `^Q`로 시작하는 줄이 있으면 인쇄하지 않고 공통 오류 줄에 `GapLabelSetting`을 안내한다. 이런 서식을 인쇄하면 프린터의 라벨 유형이 간격 라벨로 바뀌기 때문이다. [편집 중 서식 인쇄]는 편집본으로 검사하고, [저장된 파일 인쇄]는 호출 직전에 `BtplTemplate.Load(경로)`로 디스크 내용을 다시 읽어 검사한다.
8. **결과 표**: 시각, 동작, IsSuccess, NativeCode, PrintInvoked, PrintCode, Message. null 코드는 "미응답"으로 표시한다. 예외로 끝난 동작은 예외 종류와 이유를 실패 행으로 남긴다.
9. **상태 줄**: 마지막 연결 조회 결과, 연결에 쓴 포트·배출 거리, 이번 세션의 마지막 위치 확인 동작과 결과(없으면 "미확인")

**장치 동작**
- 모든 장치 호출은 `async`/`await Task.Run`으로 실행한다(제조사 샘플도 작업 스레드에서 인쇄한다).
- 호출 하나가 끝날 때까지 장치 버튼, 열기·저장, 편집 영역, 설정을 비활성화한다.
- 실패나 불명 응답 뒤에 자동으로 다시 인쇄하거나 위치를 확인하지 않는다.

**종료**
- 작업 중이면 끝난 뒤 닫는다.
- 수정됨 상태이면 저장할지 묻는다. 저장이 실패하거나 취소되면 종료를 중단한다.
- 연결돼 있으면 Disconnect 결과를 확인하고, 실패하면 알린다. 이어서 Dispose 예외도 알린다.

## 설정 화면

phase2 FrmSettings의 모양을 따른다.

- **배치**: LayoutControl 안에 XtraTabControl(연결, 서식)을 두고, 아래에 확인(F12)/취소(ESC) SimpleButton(100×27)을 둔다.
- **키 처리**: `KeyPreview`와 KeyDown에서 F12로 확인 버튼을 누르고, ESC는 `CancelButton`으로 처리한다.
- **VGrid**
  - 행 구성: CategoryRow + EditorRow
  - 크기: 행 이름 280, 값 200, 최소 행 높이 20
  - 분류 글꼴: 굴림 9 굵게
- **포트 감지**: SDK에는 USB 장치 조회 API가 없다. 레지스트리 `HKLM\SYSTEM\CurrentControlSet\Services\usbprint\Enum`의 번호(`0`, `1`, …)를 포트 값으로, 각 장치의 `FriendlyName`(없으면 `DeviceDesc`)을 이름으로 읽는다. SDK의 `SetPrinterPort`도 이 목록에서 장치를 찾지만, 번호와 SDK 포트 값의 대응은 프린터 1대·포트 `0`에서만 실기로 확인했다. 장치 인스턴스 ID는 화면·설정·기록에 남기지 않는다.
- **입력 편집기**: 포트는 ImageComboBox(목록 선택만, 표시 `번호 — 이름`, 저장 값은 번호)와 [다시 감지] 버튼이다. 설정 화면을 열 때 목록을 읽고, 저장된 포트가 목록에 없으면 `번호 — 감지 안 됨`으로 남긴다. 아래 읽기 전용 "감지 상태" 행에 감지 대수를 보인다. 0대면 감지되지 않았다고, 2대 이상이면 운영 기준은 1대이고 번호와 장치 대응은 미검증이라고 표시한다. 배출 거리는 SpinEdit(정수 0~1000), 서식 폴더는 ButtonEdit + 폴더 선택이다.
- **검증과 저장**: 잘못된 값은 `e.ErrorText`로 표시하고 확인을 막는다. 확인을 누르면 TesterSettings로 저장한다.
- **제외**: phase2의 검색 기능은 넣지 않는다.

## 장치 없는 검사

`--self-check --expect-x86|x64`는 화면과 SDK 없이 다음을 확인하고 종료 코드로 결과를 알린다(성공 0, 실패 1, 잘못된 인수 2).

1. 프로세스 비트수가 기대값과 같다.
2. 설정: 없는 파일은 기본값으로 생성한다. 값을 바꿔 저장한 뒤 다시 읽으면 같은 값이다. 잘못된 값(빈 포트, 1001 mm)은 거부한다. 손상된 파일은 기본값으로 시작하고 원본을 바꾸지 않는다.
3. 서식: 합성 CP949 TXT를 열어 편집하면 수정됨이 된다. Save 뒤 파일과 `ToBtpl()`이 같고, 연속 Save도 된다. SaveAs 뒤 다음 Save는 새 경로에만 쓰고 원래 파일은 바뀌지 않는다.
4. 실패 보존: 외부에서 파일을 바꾼 뒤 Save하면 SourceChanged가 나고, 편집본·수정 표시·외부 내용이 그대로다. 기존 다른 파일로 SaveAs(덮어쓰기 거절)하면 그 파일과 편집본이 그대로다. 손상된 설정으로 시작한 뒤 최근 파일을 갱신해도 원본 설정 파일이 바뀌지 않는다.
5. 미리보기 치환: OfflineSmoke와 같은 사례로 확인한다(빈 항목 유지, 없는 번호는 표식 유지, 값 안의 표식은 다시 치환하지 않음).
6. USB 목록: 합성 목록으로 번호·이름 정리, 빈 번호 건너뛰기, 저장 포트의 감지 안 됨 표시, 감지 대수 안내를 확인한다. 실제 레지스트리는 읽지 않는다.
7. 실행 폴더에 SDK 두 파일이 있지만 프로세스에 로드되지 않았고, 화면을 만들지 않았다.

임시 폴더만 쓰며 실제 사용자 설정과 운영 서식은 건드리지 않는다. 화면 배치, F12/ESC, 오류 표시, 저장 확인은 장치 없이 실제 화면에서 따로 확인한다. 물리 출력은 사용자 승인과 직접 관측으로만 확인한다.
