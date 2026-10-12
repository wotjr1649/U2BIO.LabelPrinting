# TXT/BTPL · JSON · XML 라벨 서식

2026-10-12 사용자 확정과 [서식 확장 #12](https://github.com/wotjr1649/U2BIO.LabelPrinting/issues/12)에 따른 API다. 세 형식은 같은 `BtplTemplate`을 만들고 기존 편집·검사·바인딩·인쇄 경로를 사용한다. 최종 프린터 명령은 계속 BTPL이며 raw 전송이나 다른 인쇄 엔진을 추가하지 않는다.

## 사용법

```csharp
var txt = BtplTemplate.Load("label.txt");       // 기존 BTPL, 기본 CP949
var json = BtplTemplate.Load("label.json");    // JSON 서식, UTF-8
var xml = BtplTemplate.Load("label.xml");      // XML 서식, UTF-8

// 파일 이름과 실제 형식이 다르거나 문자열로 입력할 때 명시한다.
var explicitJson = BtplTemplate.Load("label.data", BtplTemplateFormat.Json);
var fromJson = BtplTemplate.Parse(jsonContent, BtplTemplateFormat.Json);
var fromXml = BtplTemplate.Parse(xmlContent, BtplTemplateFormat.Xml);

// 편집은 형식과 무관하다. With/Save의 반환 객체를 채택한다.
json = json.WithText(0, json.TextElements[0].WithPayload(firstLine));
json = json.Save("label.json");
var converted = json.Save("label.xml");
string btpl = converted.ToBtpl();
string jsonText = converted.ToJson();
string xmlText = converted.ToXml();
```

`BtplTemplateFormat`은 Btpl/Json/Xml이다. 기존 Parse(string)는 BTPL 전용으로 유지한다. Load/Save(path, codePage=949)는 대소문자 무관 .json/.xml 확장자를 선택하며 나머지 확장자는 기존 BTPL로 처리한다. 명시 overload는 Load/Save(path, format, codePage=949)다. 기존처럼 특정 확장자 이름에 BTPL을 저장했다면 format:Btpl을 명시한다.

codePage는 BTPL 읽기·저장에만 적용한다. UTF-8 BTPL에는65001을 지정한다. JSON/XML은 UTF-8이며 BOM을 읽을 수 있고 저장 시 BOM을 추가하지 않는다. 파일 인코딩과 다른 BOM은 InvalidEncoding이다. XML 선언은 생략하거나 UTF-8/utf8을 지정하며 다른 선언은 거절한다.

Print(string)/PrintAsync(string)는 여전히 완성 BTPL 문자열을 받는다. JSON/XML 문자열은 위 Parse overload로 템플릿을 만든 뒤 Print(template) 또는 기존 요청 API에 전달한다. 이 문서 예시는 파일·관리 모델 사용법이며 실제 SDK 실행 승인을 뜻하지 않는다.

## TXT/BTPL 예

예시 용지·좌표·배출 수치는 DLL 기본값이 아니다. 문자열의 두 AW는 한 라벨에 들어가는 두 요소다.

```text
^Q40,5,7.5+
^W70
^S5
^H19
^E15
^L
AW,80,120,10,1,0,0,CK000001 SAMPLE 98-03-21
AW,80,160,10,1,0,0,Not Used
E
```

## 같은 서식의 JSON

```json
{
  "settings": [
    { "command": "^Q", "parameters": "40,5,7.5+" },
    { "command": "^W", "parameters": "70" },
    { "command": "^S", "parameters": "5" },
    { "command": "^H", "parameters": "19" },
    { "command": "^E", "parameters": "15" }
  ],
  "texts": [
    {
      "command": "AW",
      "x": "80", "y": "120",
      "arguments": ["10", "1", "0", "0"],
      "payload": "CK000001 SAMPLE 98-03-21"
    },
    {
      "command": "AW",
      "x": "80", "y": "160",
      "arguments": ["10", "1", "0", "0"],
      "payload": "Not Used"
    }
  ]
}
```

최상위는 settings/texts 두 항목만 허용한다. settings는 command/parameters 문자열 객체 배열, texts는 command/x/y/arguments/payload 객체 배열이다.
X/Y는 기존 BTPL의 비음수 정수 문법을 따르는 문자열 또는 JSON 정수로 입력한다. ToJson은 선행0을 보존하기 위해 문자열로 저장한다. arguments는 명령별 순서가 있는 문자열 배열이며 AW의 부호·선행0을 보존한다. payload의 쉼표·공백·변수도 유지한다.

JSON 숫자는 표준 문법을 따른다. 숫자80은 허용하고 숫자0080/+80은 거절하며, 원문 선행0이 필요하면 문자열 "0080"을 사용한다. 객체·배열 끝의 불필요한 쉼표와 잘못된 escape도 거절한다. 문구 안의 쉼표·중괄호·숫자 문자열은 데이터로 보존한다.

## 같은 서식의 XML

```xml
<labelTemplate>
  <settings>
    <setting command="^Q" parameters="40,5,7.5+" />
    <setting command="^W" parameters="70" />
    <setting command="^S" parameters="5" />
    <setting command="^H" parameters="19" />
    <setting command="^E" parameters="15" />
  </settings>
  <texts>
    <text command="AW" x="80" y="120">
      <arguments>
        <argument>10</argument><argument>1</argument>
        <argument>0</argument><argument>0</argument>
      </arguments>
      <payload>CK000001 SAMPLE 98-03-21</payload>
    </text>
    <text command="AW" x="80" y="160">
      <arguments>
        <argument>10</argument><argument>1</argument>
        <argument>0</argument><argument>0</argument>
      </arguments>
      <payload>Not Used</payload>
    </text>
  </texts>
</labelTemplate>
```

루트는 labelTemplate이며 settings와 texts는 각각 하나다. 허용한 요소·속성만 읽고 setting/text/argument의 순서를 보존한다. payload는 텍스트/CDATA만 허용하며 앞뒤 공백을 유지한다. `&`/`<`는 XML 규칙에 따라 이스케이프한다. namespace·DTD·외부 entity·주석·처리 명령·추가 요소는 거절한다.

## 바인딩·검사·저장

서식 JSON(settings/texts)과 기존 바인딩 JSON(variables)은 별개다. 서식 문구의 ^V00~^V99는 기존 BtplBindings·FromRow/FromTable에서 치환한다. DB·SQL·연결 정보·가변 LineAdvances를 서식에 저장하거나 실행하지 않는다.

세 파서가 만든 모델은 기존 ToBtpl/Parse의 검증을 통과해야 한다. 필수 설정·중복 설정·허용 명령·인수·변수·제어 문자 검사를 형식별로 우회할 수 없다. 문법 성공은 실제 인쇄 허가가 아니며 기존 지원 프로필과 장치 사전검사를 그대로 적용한다.

설정·요소·인수 순서와 AW 원문 인수, BTPL 좌표의 선행0·문구 공백을 보존한다. JSON/XML은 CRLF 고정 줄을 자동 확장하지 않는다. 저장 전 ExpandFixedLines를 사용하며 가변 줄 이동은 기존 호출 옵션이다. 기본 P1/C1/R0/E0는 기존 ToBtpl 정규화 경로에서 보완한다.

JSON/XML 문서와 생성 결과는 UTF-8 기준4MiB(4,194,304 bytes) 이하다. 파일 BOM도 포함하며 초과는 TemplateTooLarge다. 파일 크기는 같은 열린 파일에서 먼저 확인하고 제한된 바이트만 읽는다. 생성 결과의 들여쓰기/이스케이프도 제한에 포함된다. 이 제한은 기존 TXT/BTPL 또는 실제 인쇄 문구에 새 상한을 추가하지 않는다. XML 깊이는 제한하고 DTD/외부 resolver는 사용하지 않는다.

서식 스키마·UTF-16 고립 surrogate·XML 구조 오류는 InvalidTemplateDocument, 기존 BTPL 의미 오류는 기존 Code를 반환한다. 예외에 입력 내용을 포함하지 않는다. 형식 enum·null·파일 I/O의 표준 예외도 호출자가 처리한다.

각 형식의 Load/Save가 기존 원본 SHA-256·새 객체·임시 파일 교체 계약을 재사용한다. 같은 경로의 외부 변경/삭제는 SourceChanged이며 실패 시 원본을 truncate/삭제하지 않는다. 다른 형식으로 저장하는 새 경로는 명시적 저장이다. 동시 writer에 대한 완전 CAS와 파일 쌍의 원자 저장은 보장하지 않는다.

## 검증 범위

TemplateFormatTests는 세 형식의 정규 BTPL/바인딩 결과 일치, raw 좌표·AW 인수·한글·변수·공백 보존, schema/중복/금지 명령/Unicode/DTD/크기 오류, UTF-8/BOM, 명시 형식, 저장 충돌·연속 저장·잠금 오류·원본 보존을 검사한다. 기존 바인딩·연결·합성 인쇄 검사도 유지한다.

실제 SDK·장치·출력·배출 검증은 [별도 실기 #11](HARDWARE_VALIDATION.ko.md)에 보류한다. 이전 적대적 검토의 첫 행 연결 조회 예외 상태 문제는 서식 확장으로 수정하지 않았으며 연결 수명 보완에서 해결해야 한다.
