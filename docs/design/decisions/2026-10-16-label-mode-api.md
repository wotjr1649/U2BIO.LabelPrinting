# BTPL 라벨 방식 판정, 인쇄 불가 AW 줄, 화면용 변수 치환

2026-10-16 사용자 grilling 결정. 이슈 #23. Templates 선택 기능에 공개 API를 추가한다.

사용자는 다음을 정했다.
- **라벨 방식**: 블랙마크(`^Q` 세 인수)와 간격(`^Q` 두 인수)을 모두 지원한다. DLL은 서식의 방식을 판정해 알려 주기만 하고, 인쇄할지는 앱이 설정한 용지 방식과 비교해 확인 창으로 정한다.
- **간격 방식 편집·저장**: 간격 용지 실기 뒤 다음 단계로 미룬다. 이번에는 기존 편집·저장 거부(`GapLabelSetting`)를 유지한다.
- **넘침 검사**: 운영 앱은 경고만 한다. SDK 방식 글자 측정(GDI)은 당분간 Tester에만 두므로 측정 결과를 쓰는 영역 초과량 API도 이번에는 DLL에 넣지 않는다.

## 공개 API

```csharp
public enum BtplLabelMode { Unspecified, BlackMark, Gap, Ambiguous }

// BtplTemplate (생성자에서 한 번 계산하는 불변 값)
public BtplLabelMode LabelMode { get; }
public IReadOnlyList<int> LabelSettingLineNumbers { get; }       // 앞 공백을 뺀 줄이 ^Q로 시작하는 줄(1부터)
public IReadOnlyList<int> UnprintableTextLineNumbers { get; }    // X가 1 이상 정수가 아닌 AW 줄(해석된·해석하지 못한 줄, 1부터)

// BtplTextElement
public string GetPreviewText(string variableData = null);
```

- **LabelMode**: `^Q` 판정 줄만으로 정한다. 0줄이면 Unspecified. 1줄이고 그 줄이 `Settings`에 해석된 `^Q`이면 인수 3개는 BlackMark, 2개는 Gap. 그 밖(여러 줄, 앞 공백, 네 인수, 해석 불가)은 Ambiguous. 서식 블록 수는 따로 세지 않는다(블록마다 `^Q`가 있으면 여러 줄이라 Ambiguous가 된다).
- **UnprintableTextLineNumbers**: 해석된 AW는 X &lt; 1, 해석하지 못한 AW 줄은 X 칸이 1 이상 정수로 읽히지 않는 줄이다(현재 `CheckPrintable`의 AW 판정과 같다).
- **CheckPrintable 공유**: 두 목록을 생성자에서 계산하고 `CheckPrintable`은 이 목록만 읽어 예외를 낸다. AW 목록이 있으면 `InvalidArguments`, `^Q` 판정 줄 중 해석된 세 인수가 아닌 줄이 있으면 `GapLabelSetting`이다. 판정 규칙이 한 곳에만 있으므로 저장 검사와 인쇄 전 검사가 어긋날 수 없다.
- **GetPreviewText**: Payload의 `^V` + 두 자리 숫자를 `!!`로 나눈 값으로 한 번만 치환한다. 빈 항목은 빈 값, 없는 번호와 null은 표식 유지, 값 안의 `^Vnn`은 다시 치환하지 않는다. 원문·저장·인쇄 입력은 바꾸지 않는 화면 전용이다.

## 결정과 대안

| 결정 | 대안 | 판단 |
| --- | --- | --- |
| 라벨 방식 안전장치의 위치 | `Print`·`PrintFile`에서 두 인수 `^Q`를 항상 거부 / **Templates에서 방식 판정, 차단은 앱** / 앱마다 직접 판정 | 첫째는 간격 용지 현장을 막고 핵심 인쇄 코드가 BTPL 명령을 해석하게 된다(사용자가 과설계로 판단). 셋째는 같은 규칙이 앱마다 갈라진다. 둘째를 채택했다. |
| 판정 결과의 모양 | `bool HasGap` / 위반 줄 목록만 / **`BtplLabelMode` 넷과 판정 줄 번호 목록** | `bool`은 `^Q` 없음·해석 불가를 블랙마크와 구분하지 못한다. 줄 목록만으로는 앱이 규칙을 다시 만들어야 한다. 셋째를 채택했다. |
| 인쇄 전 검사의 분리 | Tester가 저장 검사(`WithRawText`)를 계속 사용(간격 서식을 영원히 막음) / Tester가 원문 AW를 직접 해석 / **`UnprintableTextLineNumbers` 공개** | 간격 서식을 확인 후 인쇄하려면 저장 검사에서 `^Q` 부분만 빼고 AW 검사는 남겨야 한다. 첫째는 사용자 결정을 막고, 둘째는 UI에 BTPL 해석을 복제한다. 셋째는 `CheckPrintable`과 같은 목록을 공개한다. |
| 저장된 파일 인쇄의 확인 순서 | 확인 창이 떠 있는 동안 파일 잠금 유지 / **UI 스레드에서 디스크 내용을 읽어 판정·확인한 뒤, 작업 스레드의 잠금 안에서 다시 읽어 원문이 확인한 원문과 같을 때만 인쇄** | 첫째는 확인 창을 오래 두면 다른 프로그램의 저장을 막는다. 둘째는 #25의 "검사와 인쇄 사이 교체 금지"를 지키면서 확인 창을 UI 스레드에 둔다. 원문 비교는 `Load`한 서식의 `ToBtpl()` 문자열로 한다(BOM 차이처럼 SDK에 보내는 내용이 같은 경우는 같은 파일로 본다). 다시 읽은 원문이 다르면 인쇄하지 않고 "확인 뒤 파일이 바뀌었습니다"를 알린다. 둘째를 채택했다. |
| 판정 위치 | 새 파일 / **기존 `Templates\BtplTemplate.cs`·`BtplModels.cs`** | 새 파일이면 csproj 컴파일 목록이 바뀐다(`verify-repository`의 핵심 두 파일 검사는 Templates를 제외하므로 영향 없음). 기존 파일에 두면 컴파일 목록·참조·공개 메서드 검사가 그대로다. 둘째를 채택했다. |
| 화면용 치환 | 앱마다 구현(현재 Tester·OfflineSmoke 중복) / **`BtplTextElement.GetPreviewText`** | 같은 규칙이 두 곳에 복제돼 있다. 인쇄 입력은 그대로 SDK가 치환하고, 이 함수는 화면 확인 전용이다. |

## 기존 스펙을 대체하는 부분

- `docs/specs/TXT_TEMPLATE_EDITOR.md`의 "DLL에 화면용 변수 치환 의존성을 넣지 않는다", "치환은 호출 프로그램 책임" 문장과 공개 API 표를 이 결정으로 바꾼다. 화면용 치환은 DLL 선택 기능(`GetPreviewText`)이 맡고, 인쇄용 치환은 계속 SDK가 맡는다.
- 함께 고칠 문서: `docs/BTPL_TEMPLATE_API.ko.md`(공개 API·치환 책임), `docs/specs/DEVEXPRESS_TESTER.md`(인쇄 전 검사·용지 방식 설정·자체 검사 목록), `docs/PRINT_PRESENTATION.ko.md`(두 인수 `^Q` 설명에 판정·확인 정책), `examples/OfflineSmoke/Program.cs` 주석.

## Tester 인쇄 전 검사 (변경 뒤)

1. `UnprintableTextLineNumbers`가 비어 있지 않으면 인쇄하지 않는다(확인 창으로 우회할 수 없다). 줄 번호와 이유를 공통 오류 줄에 보인다.
2. `LabelMode`가 설정한 용지 방식과 다르거나 Unspecified·Ambiguous이면 방식과 `^Q` 줄을 보여 주고 확인을 받는다.
3. 저장된 파일 인쇄는 위 표의 확인 순서를 따른다.

## 잘못되면 비용이 큰 가정 (검사를 먼저 둔다)

- 앞 공백이 있는 `^Q` 줄은 `Settings`에 들어가지 않는다(`TryReadSetting`이 원문 줄을 그대로 읽는다). 틀리면 들여쓴 `^Q`를 블랙마크로 판정한다.
- 판정 줄 번호는 `TemplateLine`과 같은 줄끝 기준(CR·LF·CRLF)이다.
- `CheckPrintable`을 목록 기반으로 바꾼 뒤에도 거부되는 서식은 지금과 같아야 한다. 오류가 둘 이상이면 보고 순서는 AW 줄(`InvalidArguments`)이 `^Q` 줄(`GapLabelSetting`)보다 먼저로 정한다(지금은 줄 순서라 두 인수 `^Q`가 앞 줄이면 `GapLabelSetting`이 먼저 나온다). 섞인 사례 하나를 사례표에 넣어 이 순서를 고정한다. 기존 검증 사례와 Tester 자체 검사의 인쇄 전 사례표(해석된·해석하지 못한 AW X 0, 앞 공백·네 인수 `^Q`, 세 가지 줄끝)를 `CheckPrintable` 경로와 공개 목록 경로에 똑같이 적용해 결과를 대조한다.

## 남은 위험

- 간격 서식의 실제 인쇄 위치와 프린터 라벨 유형 전환은 실기 기록이 없다. 다음 단계에서 간격 용지로 확인한다.
- 프린터의 현재 라벨 유형은 SDK로 조회하지 않는다. 앱 설정의 용지 방식이 기준이다.
- 간격 서식을 연 Tester에서 편집 뒤 되돌리기·처음 연 상태로를 누르면 `WithRawText`가 `GapLabelSetting`으로 거부한다(기존 동작). 간격 편집 허용 단계에서 함께 다룬다.
