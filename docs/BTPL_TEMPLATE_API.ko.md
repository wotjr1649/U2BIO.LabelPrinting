# TXT 서식 편집 API 사용 안내

이 문서는 프로그램에서 TXT를 편집·저장하거나 표시용 변수 치환을 사용하는 개발자만 읽는 선택 안내이며 현행 TXT 편집 계약을 포함한다. 기본 인쇄는 [프린터 API](PRINTER_API_V2.ko.md), Tester 조작은 [사용 안내](PRINT_PRESENTATION.ko.md#tester-사용)를 따른다. 이전 JSON/XML·바인딩·BtplProcessor API는 제공하지 않는다.

## 편집과 인쇄

다음은 현재 TXT 편집 API의 사용 예시다. Load/편집/저장 자체는 SDK를 로드하거나 프린터를 실행하지 않는다. printer는 호출 프로그램이 연결하고 관리한다.

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

TextElements는 AW 편집 목록이다. AW의 X는 1 이상이어야 한다(실기에서 X=0은 SDK 인쇄 예외를 냈다). 새로 만들거나 바꾸는 X, WithRawText와 Save/SaveAs에서 X 0 이하를 InvalidArguments로 거부한다. 숫자로 해석하지 못한 AW 줄도 X 칸을 앞뒤 공백을 뺀 정수 1 이상으로 읽을 수 없으면 같은 오류로 거부하며, 기존 파일은 읽어서 고칠 수 있다. 비AW 명령과 숫자로 읽을 수 없는 AW는 원문과 UnsupportedLineNumbers로 확인한다. 목록 인덱스는 해당 객체의 스냅샷이며 삭제/추가/원문 수정 후 새 목록에서 다시 선택한다. 정렬/방향은 원문으로 수정한다.

새 요소 추가는 단일 ^L/E frame의 E 직전으로 제한한다. 여러 frame도 원문 읽기·저장은 가능하지만 모호한 삽입은 원문 화면에서 지정한다. Q/W/S/H 없는 제조사 예제를 허용하며 기본 설정을 자동 추가하지 않는다.

```csharp
// ^Q 첫 인수(마크를 뺀 길이, 피치 − 마크 폭)만 바꾸며 마크 폭·위치 인수는 그대로 둔다. 두 인수 ^Q는 GapLabelSetting으로 거부한다.
template = template.WithLabelLength(35m);
template = template.WithLabelWidth(70m);
template = template.WithSetting(new BtplSetting("^S", "5"));
template = template.WithSetting(new BtplSetting("^H", "19"));
```

^Q는 `길이,마크 폭,위치±` 세 인수(Black-Mark)만 편집 결과로 허용한다. 두 인수 ^Q(Gap)는 프린터 라벨 유형까지 간격 라벨로 바꾸므로 `new BtplSetting`, WithRawText, Save/SaveAs가 `GapLabelSetting`으로 거부한다. 기존 파일의 두 인수 ^Q는 Load/Parse로 읽을 수 있으며 WithSetting으로 세 인수를 지정해 고친 뒤 저장한다. 없는 Q/W를 추가하려면 전체 인수를 WithSetting으로 명시한다. 블랙마크 용지는 `BtplBlackMarkMedia(받침지 폭, 마크 제외 길이, 마크 폭, 흰 부분, 라벨 폭, 라벨 높이, dpi = 300, 보정 = 1.5)`로 자로 잰 값(mm)에서 `LabelSetting`(^Q, z = 흰 부분 + 보정을 0.5 mm 단위 반올림)과 `WidthSetting`(^W), 흰 라벨 `LabelArea`와 2 mm 안쪽 `SafeArea`(dot)를 계산한다. 선택 인수 세로 보정(기본 3.6 mm, 원점에서 흰 라벨 상단까지)과 가로 보정(기본 −1.0 mm, 가운데 정렬 대비 왼쪽 끝 이동)으로 실측 위치를 반영한다. 보정 1.5 mm와 두 위치 보정은 BT-003 Pro 실측값이며 새 용지는 눈금 라벨로 z 안정성과 위치를 확인한다. 배출 거리와 글자 높이는 계산하지 않는다. 같은 설정 명령이 여러 개면 편의 수정은 거부하며 원문에서 위치를 정한다. 읽은 숫자 표기는 바꾸는 필드 외에는 보존한다. Size는 SDK 크기 값이며 화면 pt/mm의 동일성을 보장하지 않는다.

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

## 라벨 방식과 인쇄 전 확인

DLL은 서식의 라벨 방식과 SDK 인쇄 예외를 낼 AW 줄을 판정만 한다. Tester는 이 진단을 사용한다. 다른 호출 프로그램이 진단을 사용할지는 해당 프로그램에서 정한다. Print/PrintFile은 막지 않으므로 프로그램이 자기 용지 방식과 비교해 인쇄할지 정한다.

```csharp
var template = BtplTemplate.Load("label.txt");
if (template.UnprintableTextLineNumbers.Count > 0)
{
    // X가 1 이상 정수가 아닌 AW 줄이다. 줄 번호를 보이고 인쇄하지 않는다.
}
else if (template.LabelMode != BtplLabelMode.BlackMark)
{
    // 방식과 LabelSettingLineNumbers(^Q 줄)를 보이고 사용자 확인을 받는다.
}
```

LabelMode는 ^Q 줄이 없으면 Unspecified(프린터의 현재 라벨 유형), 한 줄이고 세 인수면 BlackMark, 두 인수면 Gap(프린터 라벨 유형을 간격으로 바꿈), 여러 줄·앞 공백·네 인수·해석 불가면 Ambiguous다. 프린터의 현재 라벨 유형은 SDK로 조회하지 않는다. 간격 서식의 편집·저장은 지금처럼 GapLabelSetting으로 거부한다.

## 호출 프로그램의 참고 미리보기

DLL은 그림이나 치환한 인쇄 서식을 반환하지 않는다. 프로그램이 TextElements의 X/Y·Size·Weight와 `GetPreviewText(rawVariableData)`를 읽어 그린다. GetPreviewText는 ^Vnn을 !!의 빈 항목/공백을 유지해 한 번 치환하고 없는 번호와 null은 표식으로 남긴다. 이 표시값을 WithText/저장/Print에 다시 넣지 않는다.

용지 설정이 없으면 화면용 크기를 별도로 지정하고 TXT에 자동 추가하지 않는다. UnsupportedLineNumbers가 있으면 그 행이 미리보기에 반영되지 않았음을 알린다. 실제 프린터 글꼴·문자 폭·물리 출력 일치는 이 화면의 보장 범위가 아니다.

[오프라인 호출 검증](../tests/OfflineSmoke/Program.cs)은 UI/DB/SDK가 없는 호출부 이행과 관리 계약을 확인한다. 실제 UI 그리기나 장치 성공 증거는 아니다.

## 원문과 편집의 보존 계약

편집 전 `Parse(text).ToBtpl()`은 입력 문자열과 정확히 같다. CRLF/LF/CR 혼용, 빈행과 마지막 줄끝 유무, 명령 순서·미지원 명령·공백·쉼표·`!!`·미수정 숫자 표기를 보존한다. 수치가 같은 편집은 원래 표기를 유지하고, 바꾼 필드만 새 표기로 쓴다. 새 줄은 첫 줄끝 형식을 쓰며 줄끝이 없으면 CRLF를 쓴다. SDK 입력의 CRLF 정규화는 인쇄 API가 맡는다.

AW는 앞 7개 쉼표까지만 항목을 나누고 나머지는 trim하지 않은 Payload다. 기존 signed 정수·선행 0·굵기·정렬·방향을 읽었다는 이유로 자동 보정하지 않는다. 편집하는 X는 1 이상, Y는 0 이상 Int32다. 새 Size는 양수이며 `Int32.MaxValue / 340` 이하, 새 Weight는 1/2/3이다. Payload는 리터럴 CR/LF·제어문자로 새 행을 끼워 넣을 수 없다. 기존 정렬·방향을 바꾸려면 원문 편집을 사용하며 새 요소의 정렬·방향만 0/0이다.

Load/Parse/ToBtpl과 원문 기반 편집은 다중·불완전 frame의 원문을 보존한다. 기존 AW 수정·삭제는 선택한 원문 행에만 반영한다. AddText·복사·누락 설정 삽입은 `^L` 하나와 대응 끝 `E` 하나를 식별할 때만 허용한다. AW는 끝 E 직전, 설정은 ^L 직전에 넣으며 `^E15`를 끝 E로 보지 않는다. 모호하면 `AmbiguousFrame` 등 명시적 오류로 거부하며 임의 frame을 만들지 않는다. 중복 Q/W/S/H의 수정·삭제도 `AmbiguousSetting`으로 거부한다. 필수 설정은 없고 P/C/R/E나 Q/W/S/H를 자동 주입하지 않는다.

WithLabelLength/Width는 기존 Q 첫 인수와 W 너비만 바꾸며 나머지 인수를 유지한다. 없으면 WithSetting으로 전체를 지정한다. 새 ^S는 2..7, ^H는 0..19이고, 용지 길이·너비는 양의 mm다. 인쇄 API SetPrintingOptions의 범위와 이 편집 범위를 혼동하지 않는다.

모든 With/Remove는 immutable 새 객체의 원문·목록을 제공하며 현재 FilePath·CodePage·마지막 저장 해시를 유지한다. WithRawText도 파일 기준을 유지한다. TextElements/Settings는 해당 객체의 스냅샷이므로 추가·삭제·원문 편집 후 인덱스를 다시 선택한다. BtplSetting은 Command/Parameters/TemplateLine, BtplTextElement는 Command·위치·Payload·Size/Weight·정렬/방향·TemplateLine을 제공한다. BtplFormatException은 Code·TemplateLine·한글 설명을 제공하고 저장 충돌은 BtplSaveConflictException의 `SourceChanged`다.

## 파일 저장의 보장과 한계

Load/Save/SaveAs의 파일과 최종 인코딩 바이트는 4 MiB 이내다. 명시한 CodePage의 strict 변환을 사용하며, 일치하는 BOM은 읽을 때 제거하고 저장은 BOM 없이 한다. 불일치 BOM·잘못된 바이트·표현 불가 문자는 거부하므로 문자열 원문 보존을 BOM 포함 파일 바이트 전체 동일성으로 해석하지 않는다. 파일 확장자로 JSON/XML을 선택하지 않는다.

Save는 마지막 읽기/저장 해시로 원본의 외부 변경·삭제를 검사하며 삭제된 원본을 재생성하지 않는다. SaveAs는 Path.GetFullPath와 Windows 대소문자 무시 비교로 같은 현재 경로를 `SameSavePath`로 거부한다. 기본 overwrite=false는 완성된 임시 파일을 File.Move로 확정하므로 대상이 저장 중 새로 생겨도 덮어쓰지 않는다. 사용자가 확인한 overwrite=true만 기존 대상 교체를 허용한다.

인코딩·최종 바이트·재추출·반환 객체와 저장 기준을 먼저 준비한다. 같은 폴더의 임시 파일에 완전히 쓰고 flush/close한 뒤 Move/Replace한다. 성공 후 필수 재읽기·재파싱은 하지 않는다. 실패하면 기존 파일·편집 객체를 유지하고 생성한 임시 파일만 정리하며 최초 실패를 가리거나 자동 덮어쓰기 재시도를 하지 않는다. 반환 객체는 성공 시에만 채택한다.

Load 잠금은 읽는 동안, 저장 잠금은 실제 쓰는 동안이다. 편집 중 잠금·부모 디렉터리 잠금·최종 경로 확인·완전 CAS·전원 장애 복구·모든 링크 동일성은 보장하지 않는다. 인쇄 및 finally까지 유지하는 PrintFile 잠금과 별개다.

GetPreviewText는 `!!`의 빈 항목·공백을 유지해 ^Vnn을 한 번 치환한다. `A!!!!C!!`는 A/빈 값/C/빈 값이며 값 안의 변수 표식을 다시 치환하지 않는다. 없는 번호와 null은 원래 표식으로 남는다. SDK의 빈 항목·부족 데이터 출력 의미를 보장하지 않는다.

LabelSettingLineNumbers는 앞 공백을 뺀 줄이 ^Q로 시작하는 원문 행 번호다. LabelMode·UnprintableTextLineNumbers는 객체 생성 때 계산한다. WithRawText·Save/SaveAs는 해석 불가 AW도 X 오류를 `InvalidArguments`로 먼저 거부하고, 세 인수가 아닌 Q는 `GapLabelSetting`으로 거부한다. 읽기·진단과 편집·저장 허용 여부는 다르며, Print/PrintFile이 이 진단을 자동 실행하는 것은 아니다. DLL에는 WinForms·DevExpress·DB 의존성을 넣지 않고 업무 데이터 구성·인쇄 순서·배경 실행은 호출 프로그램이 맡는다.
