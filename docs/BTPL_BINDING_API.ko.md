# JSON 바인딩·관리 렌더링 API

TASK-04의 .NET Framework 4.6.1 관리 API다. DB·DevExpress·native SDK 없이 템플릿 하나와 DataTable/DataRow를 검사·렌더링한다. 실제 장치 지원이나 인쇄 가능 여부를 확인하는 API는 TASK-05다.

## 입력과 실행 예

```json
{
  "variables": {
    "^V03": { "column": "PatientName" },
    "^V01": { "column": "VisitDate", "format": "yy-MM-dd" },
    "^V02": { "column": "LockerNo", "nullText": "Not Used" }
  }
}
```

```csharp
using System;
using System.Data;
using U2BIO.BT003PROLabelPrinting;

var template = BtplTemplate.Parse(
    "^Q40,5,7.5+\r\n^W70\r\n^G0\r\n^S5\r\n^H19\r\n^R0\r\n^E15\r\n^L\r\n" +
    "AW,80,120,10,1,0,0,^V03  ^V01  ^V02\r\nE\r\n");
var bindings = BtplBindings.Load("bindings.json");
var data = new DataTable();
data.Columns.Add("PatientName", typeof(string));
data.Columns.Add("VisitDate", typeof(DateTime));
data.Columns.Add("LockerNo", typeof(string));
data.Rows.Add("SAMPLE", new DateTime(2026, 10, 8), DBNull.Value);

var request = BtplPrintRequest.FromTable(template, bindings, data);
var allRows = BtplProcessor.Validate(request);
var preview = BtplProcessor.Render(request, 0);
// 여기까지 연결/인쇄는 없다. preview.BtplText는 관리 검사 통과 후보일 뿐이다.
```

예시 규격·필드는 DLL 기본값이 아니다. `Fixed(template, options)`는 고정 서식 한 건, `FromRow`는 한 행, `FromTable`은 입력 순서의 모든 행을 생성한다. 옵션은 선택 인수다. DB 조회·빈 값 결정·실행 옵션 저장은 호출 프로그램 책임이다.

## 스키마와 저장

`BtplBindings.Parse/Load/ToJson/Save`, `WithVariable/RemoveVariable`을 제공한다. With/Remove/Save는 새 객체를 반환한다. Variables는 읽기 전용이다. Save 반환 객체를 현재 상태로 채택하여 다음 저장에 사용한다.

- 최상위는 variables 하나만 허용한다. 키는 정확한 ^V00~^V99이며 각 값은 필수 문자열 column, 선택 문자열 format, 선택 문자열/null인 nullText다. 빈/공백 컬럼명·중복 키·알 수 없는 키·잘못된 자료형·객체 뒤 추가 내용은 오류다. format:null은 문자열 타입 오류이며 nullText:null은 대체값 없음으로 처리한다.
- 위치·글꼴·DPI·SQL·연결 정보·표현식·스크립트를 JSON에 넣거나 실행하지 않는다. 미사용 매핑은 UnusedBinding 경고로 처리하며 없는 컬럼도 사용하지 않으면 출력 차단 오류로 만들지 않는다.
- 파일은 strict UTF-8, BOM 없음, 두 칸 들여쓰기다. UTF-8 BOM이 있는 파일은 읽을 수 있다. Load/Save는 TASK-03의 단일 파일 해시 충돌/임시 파일 교체 계약을 재사용한다. BTPL과 JSON을 하나의 원자 트랜잭션으로 저장하지 않는다.
- JSON 문서 전체는 UTF-8 기준 최대 4 MiB(4,194,304 바이트)다. Parse는 인코딩 전 크기를 검사하고, Load는 같은 열린 파일에서 크기를 검사한 뒤 제한된 바이트만 읽는다. 파일 상한에는 BOM도 포함한다. ToJson/Save는 들여쓰기·이스케이프를 포함한 생성 결과에도 상한을 적용하며 초과 시 BindingsTooLarge다. 저장 준비 전 오류로 기존 대상 파일을 보존한다. JSON에 들어가는 nullText도 이 문서 상한에 포함되지만 DataTable의 실제 인쇄 문자열에는 별도 길이 제한을 추가하지 않는다.

JSON은 .NET 기본 JsonReaderWriterFactory를 이용하고 중복 멤버를 따로 검사한다. 기본 reader가 무시하는 객체 뒤 내용과 __type 메타데이터도 거절한다. 선택한 overload의 .NET Framework 4.6.1 참조 컴파일·실행을 확인했으며 패키지나 전역 설정을 추가하지 않았다. 참고: [Microsoft JSON reader/writer](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.serialization.json.jsonreaderwriterfactory), [JSON 처리 규칙](https://learn.microsoft.com/en-us/dotnet/framework/wcf/feature-details/mapping-between-json-and-xml).

## 데이터와 바인딩 규칙

요청 생성 시 서식·매핑·컬럼명/자료형·필요한 모든 행의 바인딩 값을 복사한 작업 스냅샷을 만든다. 기존 모델과 옵션도 불변이다. 요청 생성 뒤 원본 행/컬럼/서식/옵션을 변경해도 요청에 반영되지 않는다. 생성 중 DataTable을 동시에 변경하는 프로그램 간 동기화는 호출자 책임이다. DLL은 미사용 컬럼 값을 복사하거나 사용자 객체를 직렬화하지 않는다.

컬럼은 Ordinal로 정확히 일치시킨다. 지원 컬럼/값 타입은 String, Boolean, DateTime, 기본 정수, Single/Double/Decimal이다. object·Guid·provider 타입 등은 UnsupportedColumnType으로 거절한다. 사용자 ToString을 실행하거나 문자열을 날짜/숫자로 추측하지 않는다. NaN/Infinity는 NonFiniteValue다. 삭제된 DataRow는 InvalidDataRow, 0행은 NoData다.

NULL/DBNull/빈 문자열/공백은 문자열 nullText를 적용한다. 없거나 null이면 EmptyValue, 명시적 ""는 빈 문구로 정상 처리한다. 컬럼 누락은 MissingColumn이며 nullText로 대신하지 않는다. 문자열의 정상 앞뒤 공백은 보존한다.

format은 DateTime/숫자에 InvariantCulture로 적용한다. 문자열·Boolean에 지정하면 FormatNotSupported, 잘못된 숫자/날짜 형식은 InvalidFormat이다. nullText는 리터럴로 쓰고 해당 컬럼의 format을 다시 적용하지 않는다.

표식은 한 번만 치환하며 반복/순서가 다른 표식에 같은 값을 사용한다. 쉼표·공백·!!·$ 문자도 그대로 유지한다. 값/대체 문구의 ^, ~, NUL 및 줄바꿈 외 제어 문자는 InvalidText다. 치환된 값을 다시 변수/명령으로 해석하지 않는다. Vt의239문자 등 명령 상한은 리터럴 부분과 치환 후 실제 각 줄에 적용한다. 넘침·겹침·자동 줄바꿈·DPI 변환은 없다.

## 여러 줄과 결과

가변 값의 CRLF/LF/CR을 각각 논리 줄로 펼친다. 해당 원래 요소 인덱스에 `new BtplRenderOptions().WithLineAdvance(index, new BtplLineAdvance(dx,dy))`를 요청 생성 시 전달한다. 빈 중간/마지막 줄도 간격을 차지한다. 옵션은 JSON에 저장하지 않는다. 간격이 없거나 (0,0)이면 MissingLineAdvance, 산술 overflow면 CoordinateOverflow다. 기존 고정 줄바꿈도 같은 배치 함수를 사용한다.

| 반환 | 의미 |
| --- | --- |
| BatchValidationResult | CommonIssues와 입력 순서 Rows. 공통 오류는 모든 행을 invalid로 표시. 한 행 오류만 있어도 배치는 invalid |
| RowValidationResult | RowIndex, IsValid, Issues. 오류의 RowIndex/TemplateLine/Variable/Column을 제공하며 Message에 실제 값을 넣지 않음 |
| BtplRenderResult.IsValid | 해당 행의 관리 검사 통과. 다른 행 오류나 실제 장치 준비 여부는 이 값으로 보증하지 않음 |
| CanPreview/IsPartialPreview | 해석 가능한 요소가 있을 때 미리보기 모델 제공. 오류 요소는 추측하지 않고 제외하며 불완전 여부 표시 |
| TextElements/BtplText | 실제 치환·줄 배치와 같은 요소 모델. 해당 행 관리 오류가 없을 때만 후보 BTPL 반환, 오류면 null |

Render는 선택 행의 참고 미리보기·후보 생성용이다. 제품 Print는 TASK-05/06에서 전체 Validate와 장치 검사를 다시 적용하여 한 행 오류 시 전체 인쇄를 시작하지 않는다. AW/다른 글꼴의 장치 미확인은 관리 오류로 넣지 않으며 IsValid=true를 인쇄 허가로 해석하지 않는다. 금지/미지원 원문 명령은 기존 Parse 오류로 중단하고 호출자가 보관한 원문을 수정해 다시 읽는다.

## 추적과 검사 범위

TASK-02의 미검증 제품 경계·장치 지원은 [#10](https://github.com/wotjr1649/U2BIO.LabelPrinting/issues/10)에서 기존 TASK-05/06/07 결과와 연결한다. #10은 TASK-04 관리 처리의 선행 장애가 아니다. 사용자 제외 RFID 실측을 필수 시험으로 복원하지 않는다.

BindingTests가 C01/C02/C19의 오프라인 조건, 모든 지원 타입과 오류·nullText·컬럼 일치·스냅샷·반복 치환·부분 미리보기·가변 줄 배치·JSON 연속 저장/충돌 및 JSON만 저장 실패를 검사한다. 기존 BTPL 관리 검사는 유지한다. DB 연결·실제 프린터/SDK·UI 그림·장치 지원 검사는 이번 범위 밖이다.
