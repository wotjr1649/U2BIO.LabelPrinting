# BTPL 템플릿 관리 API

TASK-03의 프린터 없는 관리 API다. .NET Framework 4.6.1 공통 DLL만 참조한다. Parse/Load/편집/ToBtpl/Save/ExpandFixedLines는 native SDK 로드·장치 연결·출력을 하지 않는다. JSON 바인딩과 DataTable 렌더링은 TASK-04, 참고 미리보기와 수치 편집 화면은 TASK-08에서 이 모델을 사용한다.

## 읽기와 편집

```csharp
using U2BIO.BT003PROLabelPrinting;

var template = BtplTemplate.Load("label.btpl"); // 기본 CP949
var text = template.TextElements[0];
template = template.WithText(0,
    text.WithPosition(80, 120).WithPayload("검사번호 ^V03, 옷장 !!"));
template = template.WithSetting(new BtplSetting("^H", "19"));
string btpl = template.ToBtpl();
template = template.Save("label.btpl"); // 반환 객체를 다음 편집/저장에 사용
```

경로 대신 문자열은 `BtplTemplate.Parse(btpl)`로 읽는다. UTF-8 파일은 Load/Save의 `codePage: 65001`을 명시한다. BOM은 지정 인코딩과 일치할 때만 읽으며 저장 파일에는 BOM을 추가하지 않는다. 해독/인코딩할 수 없는 문자를 대체 문자로 덮지 않는다.

`Settings`, `TextElements`, `Arguments`, `LineAdvances`는 읽기 전용 스냅샷이다. 편집 API는 새 객체를 반환하며 원본과 호출자가 전달한 배열을 변경하지 않는다.

| 작업 | API / 규칙 |
| --- | --- |
| 좌표·문구 | `WithText(i, text.WithPosition(x,y).WithPayload(payload))` |
| 일반 텍스트의 인수 | `WithArgument(index, value)`; 명령별 문법 검사 |
| AW 인수 | 뒤 네 정수의 부호·선행0·원문 보존. 편집 잠금; 구문 해석은 장치 지원 확인을 뜻하지 않음 |
| 추가 | `AddText(text)`; 마지막에 추가 |
| 복사 | `AddText(template.TextElements[i])`; 기존 요소·인덱스 유지 |
| 삭제 | `RemoveText(i)`; 해당 옵션도 `options.WithRemovedText(i)`로 제거·재매핑 |
| 설정 | `WithSetting(new BtplSetting("^Q", "40,5,7.5+"))`; 기존 위치 교체 또는 ^L 직전 추가 |
| 설정 삭제 | `RemoveSetting("^W")`; 필수 설정이 없는 중간 편집 상태는 가능하지만 ToBtpl/Save는 오류 |

요소 개수·위치·용지 규격은 업무별로 입력한다. 예시 좌표·용지 수치는 DLL 기본값이 아니다. DPI 변환·넘침·겹침 검사·자동 줄바꿈을 하지 않는다.

## 고정 줄바꿈 저장

```csharp
template = template.WithText(0, template.TextElements[0]
    .WithPayload("첫째\n\n둘째 ^V03"));
var options = new BtplRenderOptions()
    .WithLineAdvance(0, new BtplLineAdvance(0, 40));
BtplRenderOptions remapped;
template = template.ExpandFixedLines(options, out remapped);
options = remapped;
template = template.Save("label.btpl");
```

빈 중간 줄도 간격을 차지하고 변수 표식은 그대로 남긴다. 이후 요소와 옵션 인덱스를 함께 이동한다. 삭제는 위 표의 옵션 재매핑 API를 사용하고, 복사한 요소의 벡터는 호출자가 `WithLineAdvance(새인덱스, 기존벡터)`로 명시적으로 복사한다. WithText는 요소 인덱스를 유지한다.

여러 줄인데 간격이 없거나 벡터가 (0,0)이면 `MissingLineAdvance`, 좌표 산술 overflow면 `CoordinateOverflow`다. 실패 시 원본 템플릿·옵션은 보존된다. 펼치지 않은 리터럴 CR/LF는 ToBtpl/Save에서 `UnexpandedTextLines`다. DB 값의 가변 줄바꿈 간격은 후속 렌더 호출 옵션이며 BTPL/JSON에 자동 저장하지 않는다.

## 구조 검사와 정규화

- 프레임은 설정 → ^L 하나 → 텍스트 → E 하나 → 끝이다. 빈 행은 보내지 않고 CRLF와 마지막 E의 CRLF를 보장한다.
- 필수 설정은 Q/W/S/H다. 선택 기본값 P1/C1/R0/E0만 없는 경우 ^L 직전에 생성한다. 명시된 E15·기존 설정 순서·선택 인수는 보존한다. 없는 D/O를 추가하지 않는다.
- 텍스트는 AW, AA~AH/AI/AK/AL/AZ1~AZ9, AT/ATA~ATZ, VA~VZ의 명령별 인수 개수를 적용한다. 남은 전체가 payload이므로 쉼표·앞뒤 공백·!!를 보존한다. Vt의239문자 등 언어 자체 상한만 검사한다.
- payload에서 ^V00~^V99만 변수 표식으로 허용한다. 다른 ^/~/제어 문자는 오류다. RFID·미지원 명령은 오류이며 자동 삭제/명령 실행이 없다.
- Parse/Load의 오류는 `BtplFormatException.Code/Issues`로 확인한다. TemplateLine은 원문 기준 1부터 시작하며 원문이 없는 편집 객체/필수 설정 누락은 null일 수 있다. 예외 메시지에는 입력 문구를 넣지 않는다. UI 원문 보기는 호출자가 보관한 원문으로 제공한다. 중복/금지 명령은 원문을 명시적으로 수정해 다시 Parse한다.

## 저장 충돌과 실패

Load와 성공한 Save는 실제 원본 경로·바이트 SHA-256을 내부에 보관한다. 같은 경로로 저장할 때 교체 직전 현재 원본이 변경/삭제됐으면 `BtplSaveConflictException` (`IOException`, Code=`SourceChanged`)이다. 새 Parse 객체나 다른 이름 저장은 선택한 대상의 명시적 저장이다.

ToBtpl 검사와 strict 인코딩을 끝낸 뒤 같은 폴더에 고유 임시 파일을 만들고 닫는다. 기존 파일은 File.Replace, 새 파일은 File.Move로 확정한다. 교체 실패를 원본 truncate/삭제로 우회하지 않고 만든 임시 파일만 정리한다. 호출자는 성공한 Save 반환 객체를 채택해야 연속 저장을 자기 변경과 충돌시키지 않는다.

단일 파일 교체이며 BTPL/JSON 파일 쌍의 원자성을 보장하지 않는다. 해시 검사와 교체 사이까지 다른 프로그램을 잠그는 CAS나 프로세스 종료·모든 파일 시스템의 복구 보장도 아니다. JSON 저장 실패와 UI 미저장/출력 차단 상태는 TASK-04/08 검증 범위다.

## 검증 범위

Validation 프로젝트의 BtplTemplateTests가 C17/C18/C22/C23와 C24의 BTPL 관리 조건을 실행한다. CP949/UTF-8·BOM 불일치·잘못된 바이트·표현 불가 문자, 외부 변경/삭제, 잠긴 파일 교체 실패, 임시 파일 정리 및 연속 저장을 검사한다. 실제 출력과 새 SDK/글꼴 조합 검증을 대신하지 않는다. native 인쇄 차단은 유지한다.
