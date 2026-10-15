# TXT 서식 편집 API 계약

이슈 #16의 선택 서식 기능을 TXT 전용으로 축소하는 계약이다. 2026-10-14 사용자 결정과 right pane 설계 재검토 READY를 반영했다. 이 문서는 적용할 계약이며, 구현·실행 검증의 완료 여부는 이슈 상태와 검사 근거로 구분한다. 기본 인쇄 계약은 [동기 USB MVP](SYNC_USB_MVP.md)를 따른다.

## 목적과 지원 범위

SDK가 실제 서식을 해석하고 인쇄한다. 편집 API는 TXT 원문을 보존하면서 AW의 X/Y·문구 또는 변수·크기·굵기와 Q/W/S/H 설정을 편집한다. AW 추가·복사·삭제, 원문 편집, 저장과 다른 이름으로 저장을 제공한다. 완전 BTPL 해석기와 실제 프린터 글꼴을 재현하는 렌더러는 제공하지 않는다.

JSON/XML 형식, DataTable 바인딩, 일괄 렌더링, BtplProcessor와 큰 검증/렌더 결과 모델은 제거한다. 기존 Templates 호출부는 새 API로 이행하고 재빌드한다. 기본 Bt003Printer·PrinterModels·제조사 Wrapper와 바이너리는 이번 변경에서 유지한다.

## DLL과 호출 프로그램

| 기능 | DLL | 호출 프로그램 |
| --- | --- | --- |
| 서식 읽기 | 파일 I/O, 인코딩 검사, 원문과 작은 편집 모델 제공 | 파일 선택, 열린 파일 표시 |
| 구조 편집 | AW와 Q/W/S/H 값 수정, 추가/삭제 | 요소 선택, 입력 컨트롤, 업무별 입력 범위 |
| 원문 편집 | WithRawText로 새 객체 생성, 파일 기준 유지 | 원문 편집 화면, 수정 내용 전달 |
| 참고 미리보기 | TextElements와 UnsupportedLineNumbers 제공 | 그리기, 확대/축소, 선택 표시, 미지원 행 안내 |
| 변수 표시 | 원문 변수 표식 보존, 화면용 치환 GetPreviewText | 가변데이터 입력, 치환한 값은 화면에만 표시 |
| 라벨 방식 | LabelMode·LabelSettingLineNumbers·UnprintableTextLineNumbers 판정 | 설정한 용지 방식과 비교, 인쇄 확인·차단 |
| 용지 크기 | 원문 Q/W 값 제공 | 없으면 화면용 크기 별도 지정 |
| 저장 | 실제 파일 저장, 명시적 덮어쓰기 조건, 실패 예외 | 경로 선택, 덮어쓰기 확인, 미저장 표시 |
| 저장 후 전환 | 성공한 저장 기준의 새 객체 반환 | 성공 시 현재 객체/파일명 전환, 실패/취소 시 유지 |
| 업무/인쇄 | 기존 동기 SDK API와 결과 반환 | DB 조회, !! 가변데이터 구성, 요청 순서, UI 백그라운드 실행 |

DLL에 WinForms·DevExpress·DB 의존성을 넣지 않는다. 화면용 변수 치환은 2026-10-16 결정(#23)으로 DLL 선택 기능 GetPreviewText가 맡고, 인쇄용 치환은 계속 SDK가 맡는다. SDK에 전달할 서식과 가변데이터는 화면 표시값으로 바꾸지 않는다. 배출·다음 인쇄 전 복귀와 PrintFile 파일 잠금은 기존 DLL 책임이다.

## 원문과 편집 모델

Parse(text).ToBtpl()은 편집 전 입력 문자열과 정확히 같다. 행별 CRLF/LF/CR, 빈행, 마지막 줄끝 유무, 명령 순서, 미지원 명령, 공백·쉼표·!!와 미수정 숫자 표기를 보존한다. 기본 P/C/R/E나 누락 Q/W/S/H를 생성하지 않는다. SDK 입력의 CRLF 정규화는 Bt003Printer에서 수행한다.

수치가 같은 값으로 편집하면 원래 표기를 유지한다. 예를 들어 X=0080인 요소의 Y만 바꿔도 X는 0080이며, Size=+10에 WithSize(10)을 호출해도 +10이다. 바꾼 필드만 새 숫자 표기로 만든다. 새 줄은 원문의 첫 줄끝을 사용하며 원문에 줄끝이 없으면 CRLF를 사용한다.

AW는 앞 7개 쉼표까지만 항목을 나눈다. 나머지 전체가 Payload이며 trim하지 않는다. 숫자로 읽을 수 없는 AW와 비AW 명령은 원문을 보존하고 UnsupportedLineNumbers에 실제 1부터 시작하는 행 번호를 제공한다. 이는 편집/화면의 지원 범위이며 SDK의 인쇄 허용 판정이 아니다. 단, 그런 AW 줄도 X 칸을 앞뒤 공백을 뺀 정수 1 이상으로 읽을 수 없으면 WithRawText·Save/SaveAs가 InvalidArguments로 거부한다.

TextElements와 Settings는 현재 immutable 객체의 스냅샷이다. 추가·삭제·원문 편집 후 다시 추출한다. 호출자는 새 객체의 목록에서 인덱스를 다시 선택한다. 범위 밖 인덱스는 예외이며 과거 객체의 정수 인덱스 의미를 자동 추적하지 않는다.

## AW 편집

| 필드 | 편집 계약 |
| --- | --- |
| X/Y | 음이 아닌 Int32. 선택 필드만 수정 |
| Payload | 고정 문구 또는 ^V00..^V99가 포함된 문자열. 리터럴 CR/LF와 제어문자로 새 행을 끼워 넣지 않음 |
| Size | SDK size 값. 새 값은 양수이며 Int32의 size * 340 overflow를 방지하는 범위 |
| Weight | 새 편집값은 1/2/3. 현재 SDK 구현상 400/600/700 글꼴 굵기에 대응 |
| Alignment/Rotation | 원문 값을 보존. 전용 편집 API는 없고 WithRawText로 수정 |

읽은 signed 정수·선행0·원문 Weight 값에 새 편집 범위를 소급하여 보정하지 않는다. 다른 필드를 편집할 때 원래 값은 그대로 남는다. WithText도 정렬/방향 변경을 허용하지 않는다. WithArgument는 제거하고 WithSize/WithWeight로 의도를 드러낸다.

새 BtplTextElement(x, y, size, weight, payload)는 사용자가 제공한 AW 예제에 따라 정렬/방향 0/0인 새 행을 만든다. Size/Weight는 호출자가 명시한다. 기존 요소를 복사하면 기존 정렬/방향을 유지한다. 예제의 10,1,0,0을 읽은 서식에 자동 적용하지 않는다.

Size는 mm 또는 정확한 화면 pt가 아니다. 기존 제공 SDK의 정적 분석에서 size * 340 / 72 글꼴 높이 변환과 weight 대응을 확인한 범위다. 이 writer 범위는 모든 SDK/펌웨어 지원, 실제 글꼴 폭/높이와 인쇄 성공을 보장하지 않는다. 참고 미리보기는 배치 확인용이다.

## frame과 설정

Load/Parse/WithRawText/ToBtpl/Save는 다중·불완전 frame도 원문으로 보존한다. 인덱스를 지정한 기존 AW 수정·삭제는 해당 원문 행에만 반영한다.

AddText와 복사, 누락 설정의 새 삽입은 정확히 식별되는 ^L 하나와 대응 E 하나가 있을 때만 허용한다. AW는 끝 E 직전에, 설정은 ^L 직전에 넣는다. frame 없음·여러 frame·중첩·불완전이면 명시적 오류이며 원문 편집으로 위치를 정한다. ^E15는 끝 E가 아니다. 임의 frame이나 끝 명령을 생성하지 않는다.

Settings는 편의 편집 대상 Q/W/S/H다. 같은 명령이 중복되면 WithSetting과 RemoveSetting은 추측하여 첫 행을 바꾸지 않고 거부한다. 필수 설정은 없다.

WithLabelLength는 기존 ^Q의 첫 인수(블랙마크 용지는 마크를 뺀 길이, 피치 − 마크 폭)만, WithLabelWidth는 기존 ^W의 너비 값만 수정한다. 마크 폭·위치 인수는 보존한다. 두 인수 ^Q가 남은 서식은 GapLabelSetting으로 거부하므로 WithSetting으로 세 인수를 먼저 지정한다. Q/W가 없으면 호출자가 WithSetting으로 전체 설정 인수를 지정해야 한다. 간격·센서 값을 자동 추정하지 않는다. 속도와 농도는 WithSetting으로 수정하며 ^S는2..7, ^H는0..19의 기존 입력 범위를 사용한다. 용지 길이·너비는 양의 mm 수치다.

## 공개 API와 오류

- BtplTemplate: Load(path, codePage=949), Parse(text), FilePath, CodePage, TextElements, Settings, UnsupportedLineNumbers, LabelMode, LabelSettingLineNumbers, UnprintableTextLineNumbers, WithRawText, WithText, AddText, RemoveText, WithSetting, RemoveSetting, WithLabelLength, WithLabelWidth, ToBtpl, Save(), SaveAs(path, overwrite=false).
- BtplTextElement: Command(AW), X/Y, Payload, Size/Weight, Alignment/Rotation, TemplateLine과 WithPosition/WithPayload/WithSize/WithWeight, 화면용 GetPreviewText(variableData = null). 원문 값의 숫자 표기는 내부에서 보존한다.
- BtplSetting: Command, Parameters, TemplateLine.
- BtplLabelMode: Unspecified, BlackMark, Gap, Ambiguous.
- BtplFormatException: 한글 설명, Code, TemplateLine. 작은 입력 오류이며 ValidationIssue/Severity/Row/Column/렌더 결과 계층은 제거한다.
- BtplSaveConflictException: 현재 파일 변경/삭제 충돌. Code=SourceChanged.

FilePath는 새 Parse 객체에서 null이다. 모든 With/Remove 작업은 현재 파일 경로·인코딩·마지막 저장 해시 기준을 유지한다. WithRawText는 원문만 교체하고 모델을 재추출한다. 파일 기준이 없는 객체의 Save는 거부하고 SaveAs를 사용한다.

## TXT 파일과 저장

파일 내용은 TXT이며 확장자로 JSON/XML 형식을 선택하지 않는다. Load 기본 인코딩은 strict CP949이며 codePage를 명시한 읽기를 유지한다. Save/SaveAs는 현재 CodePage를 사용한다. 일치하는 BOM은 읽을 때 제거하며 저장은 기존처럼 BOM 없이 수행한다. 따라서 무편집 문자열 보존은 BOM 포함 파일 바이트 전체의 동일성 약속이 아니다. 불일치 BOM, 잘못된 바이트, 표현 불가 문자와 4 MiB 초과 파일/저장 바이트는 오류다.

UTF-8 편집 파일은 CP949 전용 PrintFile에 그대로 전달하지 않는다. CP949로 준비한 파일을 사용하거나, 편집 문자열을 Print(template.ToBtpl(), rawVariableData)로 전달한다. SDK 입력 인코딩과 기본 PrintFile 계약은 변경하지 않는다.

Save는 현재 파일의 외부 변경/삭제를 마지막 읽기/저장 해시로 검사하고 교체한다. 삭제된 원본을 자동 재생성하지 않는다. SaveAs는 Path.GetFullPath와 Windows 대소문자 무시 비교로 같은 현재 경로를 거부한다. 새 경로의 기본 overwrite=false는 완성된 임시 파일을 File.Move하여 확정한다. 대상이 처음부터 있거나 저장 중 생겨도 교체하지 않는다. 프로그램이 확인한 명시적 overwrite=true에서만 기존 대상 교체를 허용한다.

인코딩·최종 바이트·재추출·반환 객체와 저장 기준을 확정 전에 준비한다. 같은 폴더에 임시 파일을 완전히 쓰고 flush·close한 뒤 Move/Replace한다. 성공 뒤 필수 재읽기/재파싱은 하지 않는다. 실패 시 기존 파일과 편집 객체를 보존하고 생성한 임시 파일만 정리하며 최초 실패를 가리지 않는다. 자동 덮어쓰기 재시도는 없다.

Save/SaveAs는 성공한 새 객체를 반환한다. 호출 프로그램은 성공 시에만 이를 현재 편집 객체로 채택하고 파일명·미저장 표시를 갱신한다. SaveAs 성공 후 다음 Save는 새 파일에 적용된다. 취소는 DLL 호출 전 프로그램이 처리한다.

Load 잠금은 읽는 동안만, 저장 잠금은 실제 파일 쓰기 동안만이다. 편집 중 잠금·부모 디렉터리 잠금·Win32 최종 경로 검사·완전 CAS·전원 장애 복구·모든 링크 동일성은 보장하지 않는다. PrintFile의 인쇄 및 finally까지의 읽기 잠금과는 별개다.

## 참고 미리보기와 인쇄

GetPreviewText는 !!로 값을 분리할 때 빈 항목과 공백을 유지한다. A!!!!C!!는 A/빈 값/C/빈 값이다. ^Vnn을 번호에 맞춰 한 번만 치환하고 없는 번호는 원래 표식으로 표시한다. 값에 포함된 변수 표식을 재치환하지 않는다. null이면 표식을 그대로 둔다. 이 규칙은 화면 표시용이며 SDK의 빈 항목·부족 데이터 동작을 보증하지 않는다.

LabelMode는 ^Q 판정 줄(LabelSettingLineNumbers, 앞 공백을 뺀 줄이 ^Q로 시작하는 줄)만으로 정한다. 없으면 Unspecified, 한 줄이고 해석된 세 인수면 BlackMark, 두 인수면 Gap, 그 밖(여러 줄·앞 공백·네 인수·해석 불가)은 Ambiguous다. UnprintableTextLineNumbers는 X가 1 이상 정수가 아닌 AW 줄(해석된 줄과 해석하지 못한 줄)이다. 두 값은 생성자에서 한 번 계산하며, 저장 검사도 이 목록만 읽어 AW 줄을 InvalidArguments로 먼저, 세 인수가 아닌 ^Q 줄을 GapLabelSetting으로 거부한다. DLL은 판정만 하고 Print/PrintFile을 막지 않는다. 인쇄할지는 호출 프로그램이 자기 용지 방식과 비교해 정한다.

화면 표시값을 WithText·저장·SDK 인쇄 입력으로 되돌리지 않는다. 현재 편집본은 Print(template.ToBtpl(), rawVariableData), 저장된 파일은 PrintFile(path, rawVariableData)로 인쇄한다. 업무 데이터 구성·DB·변수 입력 UI·배경 실행·결과 표시·후속 요청 결정은 호출 프로그램 책임이다. 기존 IsSuccess와 자동 재인쇄 금지 계약을 유지한다.

## 완료 증거

baseline에서 새 계약 실패를 관측한 후 관리 회귀를 잠근다. 원문/필드 보존, frame 제한, 설정 미주입, 원문 편집 기준, Save/SaveAs의 실제 파일 성공·실패·경합 조건, 인코딩·파일 수명과 반환 객체 기준을 검사한다. Release x86/x64의 실제 관리 DLL 계약, 기존 SDK 대체 harness, OfflineSmoke, 배포 PE/해시와 저장소 검사를 유지한다. 새 문맥 실행 검증과 right pane 코드 검토를 받는다. 이 증거는 SDK 로드·USB·물리 출력/배출·화면 그리기 검증을 포함하지 않는다.
