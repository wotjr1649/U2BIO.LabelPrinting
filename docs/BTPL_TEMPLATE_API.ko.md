# TXT 서식 편집 API 사용 안내

[TXT 편집 계약](specs/TXT_TEMPLATE_EDITOR.md)이 지원 범위와 저장 동작의 기준이다. 이슈 #16의 후속 재설계 안내이며, 새 API 적용 여부는 이슈 상태와 실행 검증으로 확인한다. 현재 구현을 이전 JSON/XML·바인딩 API와 혼동하지 않는다. 실제 인쇄는 [공개 프린터 API](PRINTER_API.ko.md)를 따른다.

## 편집과 인쇄

다음 코드는 새 계약의 사용 예시다. Load/편집/저장 자체는 SDK를 로드하거나 프린터를 실행하지 않는다. printer는 호출 프로그램이 연결하고 관리한다.

```csharp
var template = BtplTemplate.Load("label.txt");
var text = template.TextElements[0];
template = template.WithText(0,
    text.WithPosition(80, 120)
        .WithSize(12)
        .WithWeight(2)
        .WithPayload("검사번호 ^V00"));

// 사용자가 제시한 AW 예제로 새 텍스트를 추가한다.
template = template.AddText(new BtplTextElement(
    x: 100, y: 200, size: 10, weight: 1, payload: "^V01"));

// 화면 원문 편집 후에도 파일 경로와 저장 기준을 유지한다.
template = template.WithRawText(editedText);

// 인쇄에는 변수 표식이 남은 원문과 SDK 가변데이터를 전달한다.
PrinterResult result = printer.Print(template.ToBtpl(), rawVariableData);
```

TextElements는 AW 편집 목록이다. 비AW 명령과 숫자로 읽을 수 없는 AW는 원문과 UnsupportedLineNumbers로 확인한다. 목록 인덱스는 해당 객체의 스냅샷이며 삭제/추가/원문 수정 후 새 목록에서 다시 선택한다. 정렬/방향은 원문으로 수정한다.

새 요소 추가는 단일 ^L/E frame의 E 직전으로 제한한다. 여러 frame도 원문 읽기·저장은 가능하지만 모호한 삽입은 원문 화면에서 지정한다. Q/W/S/H 없는 제조사 예제를 허용하며 기본 설정을 자동 추가하지 않는다.

```csharp
// ^Q 첫 인수(마크를 뺀 길이, 피치 − 마크 폭)만 바꾸며 마크 폭·위치 인수는 그대로 둔다. 두 인수 ^Q는 GapLabelSetting으로 거부한다.
template = template.WithLabelLength(35m);
template = template.WithLabelWidth(70m);
template = template.WithSetting(new BtplSetting("^S", "5"));
template = template.WithSetting(new BtplSetting("^H", "19"));
```

^Q는 `길이,마크 폭,위치±` 세 인수(Black-Mark)만 편집 결과로 허용한다. 두 인수 ^Q(Gap)는 프린터 라벨 유형까지 간격 라벨로 바꾸므로 `new BtplSetting`, WithRawText, Save/SaveAs가 `GapLabelSetting`으로 거부한다. 기존 파일의 두 인수 ^Q는 Load/Parse로 읽을 수 있으며 WithSetting으로 세 인수를 지정해 고친 뒤 저장한다. 없는 Q/W를 추가하려면 전체 인수를 WithSetting으로 명시한다. 블랙마크 용지는 `BtplBlackMarkMedia(받침지 폭, 마크 제외 길이, 마크 폭, 흰 부분, 라벨 폭, 라벨 높이, dpi = 300, 보정 = 1.5)`로 자로 잰 값(mm)에서 `LabelSetting`(^Q, z = 흰 부분 + 보정을 0.5 mm 단위 반올림)과 `WidthSetting`(^W), 흰 라벨 `LabelArea`와 2 mm 안쪽 `SafeArea`(dot)를 계산한다. 보정 1.5 mm는 BT-003 Pro 실측값이며 새 용지는 눈금 라벨 한 장으로 확인한다. 배출 거리와 글자 높이는 계산하지 않는다. 같은 설정 명령이 여러 개면 편의 수정은 거부하며 원문에서 위치를 정한다. 읽은 숫자 표기는 바꾸는 필드 외에는 보존한다. Size는 SDK 크기 값이며 화면 pt/mm의 동일성을 보장하지 않는다.

## 저장과 다른 이름으로 저장

```csharp
// 현재 파일에 저장한다. 성공한 객체를 다음 편집 기준으로 받는다.
template = template.Save();

// 원본을 유지하고 다른 TXT로 저장한다.
template = template.SaveAs("label-reception.txt");

// 프로그램에서 현재 파일과 다른 기존 대상의 덮어쓰기를 확인한 경우다.
template = template.SaveAs("label-archive.txt", overwrite: true);
```

파일 선택·취소·기존 대상 덮어쓰기 확인은 프로그램 책임이다. SaveAs 성공 후 반환 객체를 채택하면 새 파일이 이후 Save의 기준이 된다. 실패하면 대입하지 않고 기존 객체와 편집 내용을 유지한다. Parse로 새 서식을 만든 경우 첫 저장은 SaveAs다. FilePath/CodePage로 현재 기준을 읽는다.

SaveAs의 기본 동작은 기존 대상을 교체하지 않으며, 같은 원본 경로도 거부한다. 원본 변경/삭제는 Save에서 BtplSaveConflictException으로 알린다. Load/Save/SaveAs의 strict 인코딩과 파일 오류는 예외로 전달한다. BtplFormatException의 Code/TemplateLine을 확인하고 한글 설명을 표시할 수 있다.

기본 Load는 CP949다. 명시한 읽기 인코딩은 저장에서도 유지하며 BOM 없는 TXT를 쓴다. UTF-8 파일을 CP949 전용 PrintFile에 그대로 넘기지 않는다. 현재 편집본은 Print(ToBtpl(), rawVariableData), 저장된 CP949 파일은 PrintFile(path, rawVariableData)를 사용한다.

## 호출 프로그램의 참고 미리보기

DLL은 그림이나 치환한 인쇄 서식을 반환하지 않는다. 프로그램이 TextElements의 X/Y·Payload·Size·Weight를 읽어 그린다. ^Vnn은 !!의 빈 항목/공백을 유지해 화면에서만 한 번 치환하고 없는 번호는 표식으로 남긴다. 이 표시값을 WithText/저장/Print에 다시 넣지 않는다.

용지 설정이 없으면 화면용 크기를 별도로 지정하고 TXT에 자동 추가하지 않는다. UnsupportedLineNumbers가 있으면 그 행이 미리보기에 반영되지 않았음을 알린다. 실제 프린터 글꼴·문자 폭·물리 출력 일치는 이 화면의 보장 범위가 아니다.

[오프라인 호출 예제](../examples/OfflineSmoke/Program.cs)는 UI/DB/SDK가 없는 호출부 이행과 관리 계약을 확인한다. 실제 UI 그리기나 장치 성공 증거는 아니다.
