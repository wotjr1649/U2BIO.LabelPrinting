# BT-003 Pro 공통 라벨 인쇄 모듈 설계

구현 기준: **v0.1.1-design (2026-10-08)**. [기준 문서](BASELINE.md)와 [개발 작업·완료 조건](IMPLEMENTATION_PLAN.md)을 함께 적용한다. 사용자 정책을 변경하는 작업과 확인이 필요한 장치 사실을 구분하며, 이 버전을 제품 구현 완료로 간주하지 않는다.

상태: TASK-01 기반과 TASK-02 진단, TASK-03/04 관리 처리, TASK-05 공개 API의 관리/합성 구현 및 TASK-06 일괄 계약은 완료했다. TASK-05의 제품 실기와 TASK-07의 인수·runtime ABI 검증은 미완료이며 UI는 후속 대상이다. [인수 상태](DLL_ACCEPTANCE.ko.md)와 2026-10-08 사용자 정정에 따른 [공통 모듈의 역할·확장 범위](MODULE_OVERVIEW.ko.md)를 적용한다. 기본 사용은 wrapper의 연결·인쇄 기능이며 DataTable/JSON/편집 모델은 선택 기능이다. 1절의 사용자 정책, 그 정책을 만족시키는 아래 구현 설계, 제조사 계약·실기로 확인해야 할 항목을 구분한다. 사용자 가이드의 일반 기능 전체를 제품의 1차 범위로 가져오지 않는다. 이번 보완은 검사·실행·결과·수명(3~4절)을 먼저 정리하고 저장·배포·명령 처리(2·5·6절)를 이어 정리한 것이다. 계약별 인수 사례는 11절에서 추적한다.

[SDK 함수 대조표](SDK_API_COVERAGE.md)는 native 원형·반환 규약의 근거다. [새 BTPL 가이드 검토](#10-btpl-가이드-검토)는 별도로 기록한다. 구조화된 요소·명령 정의는 메모리 편집 모델이며, 저장 서식의 기준은 계속 BTPL이다.

## 1. 목적과 범위

`U2BIO.BT003PROLabelPrinting`은 제조사 wrapper의 연결·텍스트 인쇄 기능을 `Checkup`, `SmartCheckup` 등에서 바로 재사용하는 공통 DLL이다. 기본 입력은 호출자가 준비한 텍스트 전용 BTPL/템플릿이며 기본 흐름은 Connect → Print → Disconnect다. 서식 편집·데이터 자동 바인딩·미리보기는 같은 DLL에서 선택적으로 사용한다. 기존 RFID 값과 라벨에 원래 있는 회색 RFID 모양은 유지한다.

| 항목 | 확정 내용 |
| --- | --- |
| 모듈명 | `U2BIO.BT003PROLabelPrinting` |
| 테스트 프로젝트명 | `U2BIO.BT003PROLabelPrinting.Tester` |
| 작업 / Git 루트 | `UCSR-1813/U2BIO.LabelPrinting` |
| 원격 | `https://github.com/wotjr1649/U2BIO.LabelPrinting.git` |
| 개발 우선순위 | 공통 DLL 구현·검증 후 테스트 UI |
| 플랫폼 | .NET Framework 4.6.1 |
| 테스트 UI | WinForms + DevExpress 20.2.6 |
| 아키텍처 | 관리 DLL AnyCPU, 제조사 DLL x86/x64 배포 세트 |
| 연결 | USB 우선 구현·검증, TCP/IP와 RS-232 API도 제공 |
| SDK | `BlackfishNfcLib.dll` 장치 제어 API를 내부 래핑 |
| 공개 범위 | 연결, 포트 연결 상태, 서식·바인딩 처리, 검사, 인쇄, 해제 |
| 기준 서식 | BTPL `.txt` / `.btpl` |
| JSON | 변수 매핑, 값의 출력 형식, `nullText`만 관리 |
| 출력 | 1차는 텍스트, 확인된 BTPL 글꼴 사용 |
| 일괄 인쇄 | 템플릿 하나에 여러 데이터 행 적용, 건별 결과 반환 |
| 취소 | 현재 SDK 호출의 결과 확인 후 다음 건부터 중단 |
| 동시 요청 | 같은 프린터가 사용 중이면 새 요청에 사용 중 오류 반환 |
| 여러 줄 | 사용자가 넣은 줄바꿈 허용, 자동 줄바꿈 없음 |
| 편집·미리보기 | 수치 편집과 미리보기 포함. 미리보기 정확도의 구현 기준은 6절 |
| DB | 호출 프로그램의 책임. DLL과 테스트 프로그램에서 연결·관리하지 않음 |

사용자의 최종 결정에 따라 텍스트 넘침·텍스트별 허용 영역 검사를 하지 않는다. 라벨 전체 경계, 요소 겹침, 텍스트 길이에 대한 기하학적 검사도 1차 인쇄 차단 기준으로 사용하지 않는다. 별도 DPI 입력, DPI 프로필, DPI에 따른 좌표 변환을 추가하지 않는다. BTPL의 좌표와 글꼴 매개변수를 그대로 사용한다.

이미지, 로고, 바코드, QR 출력은 1차 지원 범위에 포함하지 않는다. RFID 기록·잠금·보안 설정은 공개 API에 포함하지 않고 인쇄 흐름에서 호출하지 않는다.

## 2. 서식과 데이터의 역할

BTPL은 레이아웃과 실제 프린터 명령의 기준이다. 위치, 글꼴, 라벨 규격, 속도, 농도, 고정 문구를 JSON에 중복 저장하지 않는다. 수치 편집 화면에서 변경한 설정은 해당 BTPL 명령에 반영한다.

완성 문구를 가진 BTPL/템플릿은 DataTable·JSON 없이 인쇄 입력으로 사용할 수 있다. 기존 Fixed 요청이 이 경로를 담당한다. JSON은 선택 바인딩에서 BTPL 변수와 `DataTable` 컬럼을 연결하는 부가 설정이다. 임의의 SQL, 연결 정보, 스크립트나 표현식을 실행하는 기능을 넣지 않는다.

TASK-04에서 확정한 JSON 자원 경계는 문서 전체 UTF-8 기준 4 MiB(4,194,304 바이트)다. Parse/Load와 ToJson/Save의 들여쓰기·이스케이프를 포함한 결과에 적용하며 초과는 BindingsTooLarge다. 파일은 BOM도 상한에 포함한다. JSON의 nullText도 포함되지만 DataTable의 실제 인쇄 문자열에는 별도 길이 제한을 추가하지 않는다. API와 실행 근거는 [BTPL_BINDING_API.ko.md](BTPL_BINDING_API.ko.md)를 따른다.

```text
BTPL 파일 또는 문자열 + 바인딩 JSON + DataTable
    → 서식 해석 및 변수 매핑
    → 값 형식화 및 빈 값 처리
    → 전체 건 사전검사
    → 검증된 BTPL을 SDK 인쇄 경로로 전달
    → 건별 결과 반환
```

기존 서식 참고 파일은 작업 폴더의 `BT-003 pro SDK/JODAL_WEBFORM_5.txt`이다. CP949이며 `가나다라마바`라는 고정 문구, `AW`, RFID 안테나 설정 `~HE0,0`을 포함한다. **원본 전체를 인쇄 전용 사전검사 통과 서식으로 취급하지 않는다.** 원본은 보존하고 인쇄용 복사본에서 `~HE`를 명시적으로 제거한다. `AW`는 6절의 제한과 실기 확인을 적용한다.

가변 서식에서는 텍스트 payload 안의 `^V00`~`^V99`를 바인딩 표식으로 사용한다. DLL이 호스트에서 치환하여 변수 참조가 없는 완성 BTPL을 SDK에 등록한다. 프린터 변수 선언·저장이나 SDK의 `SetPrintFormVariableData()`는 기본 인쇄 경로에서 사용하지 않는다. 고정 문구만 있는 서식은 바인딩·DataTable 없이 인쇄한다.

바인딩 JSON 예시:

```json
{
  "variables": {
    "^V00": { "column": "PatientName" },
    "^V01": { "column": "VisitDate", "format": "yy-MM-dd" },
    "^V02": { "column": "LockerNo", "nullText": "Not Used" }
  }
}
```

컬럼명은 예시다. 업무 필드를 DLL에 고정하지 않는다. 서식에 실제로 참조된 변수의 매핑을 확인하고, 지정한 컬럼을 이름으로 찾는다. 유사한 컬럼명을 추측하여 연결하지 않는다.

`format`은 .NET Framework 4.6.1의 DateTime·숫자 형식 문자열이며 `InvariantCulture`로 적용한다. 기본값도 같은 문화권의 형식화 결과다. 문자열·Boolean에는 `format`을 적용하지 않으며 지정하면 검사 오류다. 지원 값 타입은 String, Boolean, DateTime, 기본 정수 타입, Single, Double, Decimal이다. NaN/Infinity와 다른 객체 타입은 오류이며 사용자 객체의 ToString을 임의 실행하지 않는다. 문자열을 날짜나 숫자로 추측해서 변환하지 않는다. 업무별 형식은 호출 측에서 JSON에 명시한다.

### 바인딩·렌더링 알고리즘

1. `Load(filePath, codePage)`는 파일을 한 번만 읽는다. 기본 codePage는 샘플과 같은 949, UTF-8 파일은 65001을 명시한다. 잘못된 바이트를 대체문자로 덮지 않는 strict decoding을 사용한다. BOM은 지정한 인코딩과 일치할 때만 제거한다. 문자열 입력은 `Parse()`를 사용한다. 줄 끝은 CRLF로 정규화하고 마지막 `E` 뒤에도 CRLF를 보장한다.
2. BTPL을 설정/텍스트/종료 노드로 해석한다. `^L` 한 개와 마지막 `E` 한 개를 요구한다. 텍스트의 정해진 인수 개수까지만 쉼표로 분리하고 **나머지 전체를 payload**로 보존한다. payload 안 쉼표와 앞뒤 공백을 손상시키지 않는다. 빈 행은 전송하지 않으며 임의 주석 문법을 만들지 않는다.
3. 변수 표식은 텍스트 payload 안에서만 인식한다. 좌표·설정값의 변수는 오류다. JSON 키는 `^V`와 두 자리 번호의 정확한 형태여야 한다. JSON 열거 순서와 서식 등장 순서는 무관하다. `^V03`만 있어도 앞선 슬롯을 채우지 않는다. 같은 표식이 반복되면 같은 형식화 값을 사용한다.
4. JSON은 `variables`와 각 항목의 `column`, 선택 `format`, 선택 `nullText`만 허용한다. 중복 키·알 수 없는 키·타입 오류는 거부한다. `column`은 필수 비어 있지 않은 문자열이다. 미사용 변수 매핑은 경고이며 인쇄 차단 사유가 아니다. 참조된 변수의 누락 매핑은 오류다.
5. 컬럼명은 `Ordinal`로 정확히 일치시킨다. 대소문자·유사 이름을 추측하지 않는다. 모든 행의 값을 복사해 빈 값 규칙과 형식화를 적용한다. `nullText`는 문자 그대로 쓰며 해당 컬럼 타입의 format을 다시 적용하지 않는다. 각 행의 매핑·값 오류를 모두 모은다.
6. 치환은 파싱된 payload의 표식에 한 번만 수행한다. 치환된 값을 다시 변수나 명령으로 해석하지 않는다. `!!`와 쉼표는 payload 문자로 보존한다. 고정 문구·값·nullText의 리터럴에 같은 검사를 적용한다. `^`, `~`, NUL 및 줄바꿈 외 제어 문자는 검증된 리터럴 이스케이프 규약이 없으므로 오류다. 고정 payload에서 허용하는 ^Vxx 표식만 이 예외이며 ^D/^T/^C 등 다른 동적 구문은 거부한다. 임의 길이 제한·화면 넘침을 기준으로 자르거나 거부하지 않고, 명령 자체에 정의된 프로토콜 상한만 검사한다.
7. 수동 CRLF/LF/CR은 논리 줄바꿈으로 정규화한다. 명령 문자열에 그대로 삽입하지 않고 텍스트 노드를 줄별로 복제한다. 빈 중간 줄도 줄 간격을 차지한다. 원래의 글꼴·배율·회전·문구 순서를 유지한다. 줄별 좌표는 아래 `LineAdvance`를 적용한다. 자동 줄바꿈은 없다.
8. 순수 관리 검사에서는 명령·문자열 구조를 검사한다. 실제 SDK의 ANSI 코드페이지 round-trip, 검증된 글꼴·SDK ABI 조합은 연결 대상에 대한 장치 검사에서 추가 확인한다. UTF-8 파일을 읽었다는 사실로 SDK가 UTF-8을 전송한다고 판단하지 않는다. 두 검사 층의 반환 규약은 3~4절을 적용한다.
9. 생성 BTPL을 같은 허용 명령 파서로 재검사한다. 변수·RFID 명령이 남지 않은 후보 메모리 스냅샷과 동일한 노드로 미리보기를 구성한다. 장치 검사까지 통과한 경우에만 SDK로 전달한다. 원본 파일·편집 중인 DataTable을 인쇄 중 다시 읽지 않는다.

#### 수동 줄바꿈의 배치

기본 BTPL 텍스트 명령은 CR/LF를 payload로 보내는 여러 줄 계약이 없다. 따라서 필요한 요소에 `BtplRenderOptions.LineAdvances`로 텍스트 요소 인덱스별 `(DeltaX, DeltaY)`를 지정한다. 단위는 해당 BTPL의 원래 좌표 단위다. 줄 번호 k의 좌표는 `(x + k*DeltaX, y + k*DeltaY)`이며 회전 때도 지정한 벡터를 그대로 사용한다. 요소 인덱스는 `^L` 뒤 지원 텍스트 요소의 0부터 시작하는 순서다.

이 값은 호출 시 전달하는 배치 옵션이며 바인딩 JSON에 저장하지 않는다. 테스트 UI에서 필요한 요소의 줄 간격 수치를 편집하여 전달한다. 글꼴 크기·DPI에서 간격을 추정하지 않는다. 줄바꿈이 있는데 해당 요소에 (0,0)이 아닌 벡터가 없으면 `MissingLineAdvance` 관리 검사 오류다. 넘침·겹침은 검사하지 않는다. 저장·재로드 책임은 아래 계약으로 고정한다.

예: 첫 텍스트가 `(100,250)`이고 옵션이 `(0,40)`이면 `첫째\n둘째`는 y=250/290의 두 명령으로 변환한다. 이는 사용자 확정 정책을 만족시키기 위한 구현 설계이며 새 JSON 레이아웃 형식이 아니다.

#### 편집·저장·재로드 계약

`ExpandFixedLines(renderOptions, out remappedRenderOptions)`는 템플릿 payload에 편집자가 넣은 **리터럴 줄바꿈**만 좌표가 다른 BTPL 텍스트 노드로 펼쳐 새 템플릿을 반환한다. DataRow 치환을 수행하지 않고 ^Vxx를 그대로 보존한다. 기존 노드 i가 m개로 늘어나면 이후 노드 인덱스는 m-1 증가하며, 해당 노드의 옵션은 새 노드들에 복사한다. 뒤쪽 옵션도 새 인덱스로 재매핑한다. 필요한 간격 누락·좌표 정수 overflow면 BtplFormatException으로 중단하고 원래 템플릿·옵션을 바꾸지 않는다.

Save/ToBtpl는 payload에 리터럴 CR/LF가 남아 있으면 `UnexpandedTextLines`로 거부한다. 테스트 UI의 저장 순서는 편집 상태 복사 → ExpandFixedLines → 동일 파서의 구조/지원 문법 검사 → strict 인코딩 → Save다. 성공 후에만 정규화된 템플릿과 remapped 옵션을 UI의 현재 상태로 채택한다. 장치 미확인만으로 BTPL 저장을 막지 않고, 저장이 인쇄 허가를 의미하지 않는다. unsupported/RFID 명령은 원문 보기 단계에서 보존하지만 인쇄용 저장은 해당 오류를 명시적으로 수정한 뒤 수행한다.

고정 줄 배치는 BTPL 명령에 저장되므로 재로드만으로 재현된다. **DB 값에 들어올 가변 줄바꿈의 간격은 여전히 호출 옵션**이다. BTPL+바인딩 JSON은 템플릿 자산이며 전체 실행 설정을 저장한 작업 파일이 아니다. 호출 프로그램은 가변 LineAdvances를 자신의 작업 설정에 보관하고 FromRow/FromTable에 전달한다. DLL은 DB/호출 프로그램 설정을 저장하거나 추측하지 않는다. 별도 필수 JSON 레이아웃 파일은 만들지 않는다.

테스트 UI는 가변 간격을 현재 실행 세션의 메모리에서 유지한다. 저장 후 동일한 정규 BTPL 내용의 재로드는 옵션을 유지하고, 프로그램 재시작 또는 다른 내용의 서식 로드는 옵션을 초기화한다. 초기화 후 여러 줄 데이터를 사용할 때 간격을 다시 입력하도록 표시하며 누락 상태의 출력은 차단한다. 자동 영속화 기능은 1차에 추가하지 않는다. 화면에 “가변 줄 간격은 현재 세션/호출 옵션”을 표시하여 저장한 BTPL이 이를 포함한다고 오해하지 않게 한다.

편집 인덱스 규칙은 AddText=끝에 추가(기존 옵션 유지), RemoveText(i)=i의 옵션 삭제·뒤 인덱스 1 감소, WithText(i)=요소 인덱스 유지다. UI 복사는 AddText로 끝에 추가하고 필요하면 원본 벡터를 새 마지막 인덱스에 복사한다. 이동·순서 변경 API는 1차에 만들지 않는다. 템플릿과 재매핑한 옵션을 함께 교체한 뒤 새 작업 스냅샷을 생성한다. 외부에서 다시 Parse한 내용에 오래된 인덱스 옵션을 무조건 재사용하지 않는다.

저장할 문자열/바이트는 대상 파일을 열기 전에 완성·검사한다. Load는 원본 경로와 SHA-256을 내부에 보관하고 With/Add/Remove 파생 객체에 전달한다. 같은 원본 경로로 Save할 때 교체 직전 현재 해시를 비교하여 변경/삭제를 발견하면 BtplSaveConflictException(IOException 파생, Code=SourceChanged)을 반환한다. UI에서 덮어쓰기로 자동 우회하지 않고 재로드/다른 이름 저장으로 안내한다. 새로 Parse한 객체와 다른 이름 저장은 사용자가 선택한 대상의 명시적 저장이며 원본 해시 비교는 없다.

대상과 같은 폴더의 고유 임시 파일에 기록·닫은 뒤, 기존 대상은 File.Replace(source,target,null,false), 새 대상은 File.Move로 확정한다. 성공하면 Save는 실제 저장 경로/바이트 해시를 새 원본 기준으로 가진 새 템플릿(또는 바인딩)을 반환한다. 호출 측은 반환 객체로 현재 편집 상태를 교체하여 다음 저장을 자기 이전 저장과 충돌로 판정하지 않게 한다. 기존 불변 객체의 원본 해시는 변경하지 않는다. 교체 실패를 원본 삭제+이동으로 우회하지 않는다. 실패 시 원본을 직접 truncate하지 않고 task-created 임시 파일만 정리하며 I/O 예외를 반환한다. 해시 확인과 교체 사이 외부 프로그램까지 완전히 잠그는 CAS는 아니며 파일 시스템 실패·프로세스 종료까지 복구를 보증하지 않는다. File.Replace의 기존 파일 교체·볼륨 제약은 [Microsoft 문서](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.replace?view=netframework-4.8.1)를 대조했다. 실제 DLL 저장·충돌·교체 실패·연속 저장의 관리 코드 검증은 TASK-03 및 TASK-04의 검사 범위를 따르며, 모든 파일 시스템이나 프로세스 종료 복구의 증거로 확대하지 않는다.

BTPL과 바인딩 JSON은 독립 파일이다. 두 파일을 하나의 원자적 트랜잭션으로 저장했다고 주장하지 않는다. UI는 각각의 저장 성공/실패·미저장 상태를 표시하고 저장 실패가 남아 있으면 출력 버튼을 차단한다. 재로드 뒤에는 둘의 매핑을 다시 검사한다. 바인딩 Save도 위의 단일 파일 저장 규칙과 UTF-8/BOM 없음/들여쓰기 정책을 적용한다.

### 빈 값 규칙

`null`, `DBNull.Value`, 빈 문자열, 공백만 있는 문자열을 빈 값으로 처리한다.

- `nullText`가 문자열로 지정되어 있으면 해당 문구를 사용한다.
- `nullText`가 없거나 null이면 해당 행의 사전검사 오류다.
- `nullText: ""`는 의도적으로 빈 문구를 사용하는 명시적 설정으로 취급한다.
- 빈 값이 아닌 일반 텍스트의 앞뒤 공백은 유지한다.
- 컬럼 자체가 없는 경우는 `nullText`를 적용하지 않고 매핑 오류로 반환한다.

## 3. 공개 API의 책임

공개 API는 BTPL과 데이터를 받는 관리 API다. 제조사의 P/Invoke 함수와 임의 명령 전송 함수는 내부에 둔다. 공통 DLL은 DevExpress, Checkup의 데이터 계층, DB 접속 정보에 의존하지 않는다.

공개 메서드의 설계 계약은 다음 타입·시그니처를 사용한다.

```text
// .NET Framework 4.6.1. 메서드 표기이며 구현 소스가 아니다.
BtplTemplate BtplTemplate.Load(string filePath, int codePage = 949);
BtplTemplate BtplTemplate.Parse(string btpl);
BtplTemplate BtplTemplate.ExpandFixedLines(BtplRenderOptions renderOptions,
    out BtplRenderOptions remappedRenderOptions);
string BtplTemplate.ToBtpl();
BtplTemplate BtplTemplate.Save(string filePath, int codePage = 949);
BtplTemplate BtplTemplate.WithText(int textIndex, BtplTextElement text);
BtplTemplate BtplTemplate.AddText(BtplTextElement text);
BtplTemplate BtplTemplate.RemoveText(int textIndex);
BtplTemplate BtplTemplate.WithSetting(BtplSetting setting);
BtplTemplate BtplTemplate.RemoveSetting(string command);
BtplBindings BtplBindings.Load(string filePath);
BtplBindings BtplBindings.Parse(string json);
string BtplBindings.ToJson();
BtplBindings BtplBindings.Save(string filePath);
BtplBindings BtplBindings.WithVariable(string variable, BtplBinding binding);
BtplBindings BtplBindings.RemoveVariable(string variable);

// 템플릿 하나 + 고정 문구/단일 행/여러 행. 요청 생성 시 값을 복사한다.
BtplPrintRequest BtplPrintRequest.Fixed(BtplTemplate template,
    BtplRenderOptions renderOptions = null);
BtplPrintRequest BtplPrintRequest.FromRow(BtplTemplate template,
    BtplBindings bindings, DataRow row, BtplRenderOptions renderOptions = null);
BtplPrintRequest BtplPrintRequest.FromTable(BtplTemplate template,
    BtplBindings bindings, DataTable data, BtplRenderOptions renderOptions = null);

// SDK 없이 가능한 관리 처리. Render도 검사한다.
BatchValidationResult BtplProcessor.Validate(BtplPrintRequest request);
BtplRenderResult BtplProcessor.Render(BtplPrintRequest request, int rowIndex);

PrinterOperationResult Bt003Printer.Connect(Bt003ConnectionOptions options);
PrinterConnectionStatus Bt003Printer.GetConnectionStatus();
PrintPreflightResult Bt003Printer.ValidateForPrint(BtplPrintRequest request);
PrintPreflightResult Bt003Printer.ValidateForPrint(string btpl);
PrintPreflightResult Bt003Printer.ValidateForPrint(BtplTemplate template);
PrinterOperationResult Bt003Printer.Disconnect();
void Bt003Printer.Dispose();
BatchPrintResult Bt003Printer.Print(BtplPrintRequest request);
BatchPrintResult Bt003Printer.Print(string btpl);
BatchPrintResult Bt003Printer.Print(BtplTemplate template);
Task<BatchPrintResult> Bt003Printer.PrintAsync(BtplPrintRequest request,
    CancellationToken cancellationToken);
```

기본 호출자는 Print(string)/Print(template)와 같은 편의 API를 사용하고 BtplPrintRequest를 직접 만들 필요가 없다. string은 같은 Parse, template는 Fixed 요청으로 변환하여 동일한 private 검사·인쇄 경로를 사용한다. string 구문 오류는 Parse의 위치·Code 예외 계약을 따른다. ValidateForPrint의 편의 overload도 같은 방식으로 변환한다. 고정·단건·일괄은 내부에서 같은 요청·결과 구조를 사용한다. 이름만 다른 PrintBatch 처리기나 별도 일괄 엔진을 만들지 않는다. 고정 서식은 결과 한 건, DataRow는 한 건, DataTable은 입력 행 수만큼 결과를 반환한다. 0행 DataTable은 NoData 작업 오류이며 native 인쇄를 호출하지 않는다. 여기의 선언 표기는 문서용이며 C# 소스에 그대로 붙이는 구현 파일이 아니다.

| 타입 | 필수 계약 |
| --- | --- |
| `BtplPrintRequest` | 템플릿·바인딩·행 값·렌더 옵션의 불변 작업 스냅샷. 요청 생성 뒤 원본 DataTable 변경이 요청에 반영되지 않음 |
| `BtplBinding` | `Column`, 선택 `Format`, 선택 `NullText`. 레이아웃·업무별 필드·DB 정보 없음. WithVariable/RemoveVariable은 새 설정 반환, ToJson/Save는 같은 허용 스키마의 UTF-8 JSON 저장 |
| `BtplRenderOptions` | 필요한 텍스트 요소만 `LineAdvances` 지정. JSON·DPI·넘침 정책 추가 없음 |
| `BtplRenderResult` | `Validation`(해당 행의 관리 검사), `IsValid`(관리 오류 없음), `CanPreview`, `IsPartialPreview`, `TextElements`, `Issues`, nullable `BtplText`. 관리 검사 통과 시만 후보 BTPL 반환. 장치 인쇄 허가를 뜻하지 않음 |
| `ValidationIssue` | `Code`, `Severity`, `RowIndex`(공통 오류는 null), `TemplateLine`, `Variable`, `Column`, 민감 값 없는 `Message` |
| `RowValidationResult` | `RowIndex`, `IsValid`, `Issues`. 각 행 0부터 입력 순서 |
| `BatchValidationResult` | `IsValid`, `CommonIssues`, `Rows`. 서식 공통 오류도 모든 행을 인쇄 불가로 표시 |
| `PrintPreflightResult` | `IsReady`, `Code`, nullable `ManagedValidation`/`DeviceValidation`, `State`, `NativeFunction`, nullable `NativeCode`, `NativeCallPending`. IsReady는 확인 가능한 실행 조건 통과이며 물리적 용지/헤드 준비 보증이 아님 |
| `DeviceValidationResult` | `IsValid`, `CommonIssues`, 입력 순서 `Rows`(RowValidationResult), `PortState`. 실제 인코딩·배포 SDK·검증된 명령/글꼴 조합·포트 검사. 용지/리본/헤드 준비를 보증하지 않음 |
| `PrinterOperationResult` | `IsSuccess`, `Code`, `NativeFunction`, nullable `NativeCode`, nullable `SystemErrorCode`(로더 오류), `State`, `NativeCallPending`, `ResourcesReleased`, `CleanupCode`, 민감 값 없는 `Message` |
| `NativeCallResult` | `Function`, nullable `NativeCode`, nullable `SystemErrorCode`. `Calls`의 메타데이터 항목이며 void/예외의 원래 int 없음은 null |
| `PrinterConnectionStatus` | managed `State`, `PortState`(`Open/Closed/Unknown`), `Code`, nullable `NativeCode`, `NativeCallPending`, `ResourcesReleased`, `CleanupCode`. 용지·리본·헤드 준비 상태는 보증하지 않음 |
| `RowPrintResult` | `RowIndex`, `Status`, nullable `Validation`/`DeviceValidation`, `StopReason`, `Code`, `ExecutionStage`, `PrintInvoked`, `NativeFunction`, nullable `NativeCode`, `CompletionEvidence`, 민감 값 없는 `Message` |
| `BatchPrintResult` | `StopReason`, `Code`, nullable `Validation`/`DeviceValidation`, 입력 순서 `Rows`, `Succeeded/Failed/Unknown/NotAttemptedCount`, `NativeFunction`, nullable `NativeCode`, `NativeCallPending`, `CleanupCode`. 배치 공통 SDK 조회 실패의 raw 값도 보존 |

일반 사용자 입력 오류는 결과 객체의 오류 목록으로 반환한다. 로드·Parse의 구문 오류는 행 위치를 포함한 `BtplFormatException`으로 알리고, I/O 실패는 값을 노출하지 않는 경로 진단으로 전달한다. null 요청·범위 밖 rowIndex 같은 호출 계약 위반은 표준 Argument 예외다. 정상 작업의 취소는 `OperationCanceledException`으로 부분 결과를 잃게 하지 않고 `BatchPrintResult`로 반환한다. 비동기 API는 UI 스레드를 점유하지 않으며 UI 컨트롤에 직접 접근하지 않는다.

### 관리 검사·미리보기와 인쇄 전 검사

`BtplProcessor.Validate/Render`는 순수 관리 처리이며 제조사 DLL 로드·장치 연결을 하지 않는다. 관리 검사 대상은 구조, 허용 명령, 위치·중복, 인수 문법, 바인딩·빈 값·format, 예약 문자·명령 자체 상한, 수동 줄 배치다. 장치 지원 미확인을 이 단계의 데이터 오류로 넣지 않는다.

`Render`의 `CanPreview`는 해석·바인딩 가능한 텍스트 요소가 한 개 이상 있음을 뜻한다. 실패한 요소는 값·좌표를 추측하지 않고 오류 목록에 표시한다. 다른 요소의 바인딩·문법·줄 배치 오류가 있으면 `IsPartialPreview=true`로 표시하며 화면에 불완전 미리보기임을 알린다. 이때 `BtplText=null`이다. 구조가 파싱 불가능하거나 금지/미지원 원문 명령이 있으면 Load/Parse 단계에서 예외로 중단하며 원문 보기는 호출자가 보관한 입력으로 제공한다. AW는 원형으로 해석할 수 있으므로 관리 인수가 정상이라면 참고 앵커·문구 미리보기가 가능하다. 의미 미확정 글꼴 인수는 원문을 유지한다.

`ValidateForPrint`는 작업 점유를 얻은 뒤 전체 관리 검사 → 전체 장치 검사를 수행한다. 장치 검사는 실제 ANSI 변환, 기준 배포/ABI, 배포 버전에서 검증한 명령·글꼴 조합, `CheckPrtConnection`이다. 장치 지원 미확인은 `DeviceSupportUnverified`로 차단한다. 해당 장치에 글꼴이 설치되었다는 자동 조회나 모든 글리프 검증을 했다고 주장하지 않는다. 관리 오류면 장치 검사를 하지 않고, 장치 오류면 모든 행의 출력이 차단된다.

`ValidateForPrint`는 서식 등록·ResetForm·용지 이동·인쇄를 하지 않는다. 장치·사용 중 상태는 검사 뒤 바뀔 수 있으므로 Print는 수동 검사 결과를 재사용하여 검사를 생략하지 않는다. 같은 객체의 점유된 Print 내부에서는 동일한 private 검사 경로를 직접 사용한다. public ValidateForPrint를 재호출하여 Busy가 되는 재진입 구조를 만들지 않는다.

장치 검사 실패의 사유는 명령 조합/인코딩 오류=ValidationFailed, 포트 Closed=NotConnected, 미정의 포트 반환/복구 가능한 조회 예외=SdkFailure/StatusUnavailable, 대기 제한=Timeout이다. 전부 인쇄 전이므로 모든 행 NotAttempted이며 batch/preflight의 NativeFunction·NativeCode에 공통 native 조회 정보를 남긴다. 해당 행에서 호출하지 않은 값을 row의 native 정보로 복제하지 않는다. 포트 조회가 정상적으로 끝나 invalid 결과면 DeviceValidation을 완료된 오류 결과로 반환하고, 조회가 예외/pending이면 DeviceValidation=null이다.

관리 또는 장치의 데이터 오류만 있으면 기존 Connected 상태를 유지한다. 장치 검사에서 Closed/미정의 포트 상태면 Faulted로 전환하고 소유 점유는 명시적 Disconnect가 정리할 때까지 유지한다. timeout은 반환 후 소유 연결 해제 규칙을 적용한다. 검사 실패를 Connected/Ready로 자동 복구하지 않는다.

Busy·사전 취소·Disposed·NotConnected 등으로 검사 전에 종료한 결과는 `Validation=null`이다. 관리 검사를 완료했으면 관리 결과는 보존하고, 장치 검사를 시작하지 않았거나 완료하지 못했다면 `DeviceValidation=null`이다. 일부 native 포트 확인이 pending이라면 완료된 것으로 표시하지 않는다. 중도 취소로 batch 검사가 null이어도 개별적으로 완료한 행 검사는 RowPrintResult에 보존한다. IsValid=false는 검사 실행 후 오류가 발견된 경우만 사용한다. 0행은 순수 Validate 또는 연결된 Print의 관리 검사에서 `NoData` CommonIssues와 빈 Rows를 반환한다. 진입 단계에서 Busy/Cancelled/NotConnected로 먼저 종료하면 그 사유와 빈 Rows를 반환한다.

`BtplTemplate.TextElements`/`Settings`는 읽기 전용이다. `BtplTextElement`는 명령명·X/Y·명령별 글꼴 인수·Payload, `BtplSetting`은 명령명·해당 인수다. With/Add/Remove는 새 템플릿을 반환한다. 업무별 요소 모델이나 별도 JSON 레이아웃을 만들지 않는다. 아직 지원하지 않는 원문 명령은 Parse에서 UnsupportedCommand로 반환하고 원문 표시 단계에서 호출자가 보존한다. 편집 API는 지원 명령만 만들 수 있으며 저장은 같은 파서로 재검사한 BTPL을 strict encoding·BOM 없는 CRLF로 기록한다. 기존 파일 덮어쓰기는 테스트 UI가 명시적 저장 동작으로 수행한다.

### 연결 옵션과 수명

| 생성 API | SDK 매핑 / 검증 |
| --- | --- |
| `Bt003ConnectionOptions.Usb(string port)` | ifType=1, port는 명시적 식별 문자열, IP는 빈 문자열. 기본 USB0/USB1을 임의 선택하지 않음 |
| `Bt003ConnectionOptions.Tcp(string ipAddress, int port)` | ifType=2, 실제 IP 주소·1~65535 포트를 검증하고 포트를 invariant 십진 문자열로 전달. 기본 9100을 하드코딩하지 않음 |
| `Bt003ConnectionOptions.Serial(string comPort)` | ifType=0, `COM5`/`5`를 정규화해 숫자 문자열 `5`, IP는 빈 문자열. 잘못된·빈 번호는 오류 |

SDK가 제공하지 않은 baud/parity/timeout 장치 설정을 `SetPrinterPort` 인수로 꾸미지 않는다. `^Y`로 프린터 통신 설정을 변경하는 기능도 1차에 넣지 않는다. TCP/IP·RS-232 옵션과 연결 API는 제공하되 성공 보장은 해당 실기 완료 후에만 한다.

옵션 생성 시 빈 값·NUL·제어 문자·앞뒤 공백을 거부하고, SDK에 전달할 문자열과 mutex 대상 식별 문자열을 같은 정규화 값으로 고정한다. 문자열 마샬링이 NUL에서 잘리면서 다른 잠금 이름으로 같은 포트를 선택하는 입력을 허용하지 않는다. IP/COM 별칭 추측과 USB 자동 탐색은 하지 않는다.

`Bt003Printer()`는 기본 managed 대기 제한을 30초로 설계하고 `Bt003Printer(TimeSpan nativeWaitTimeout)`에서 변경할 수 있게 한다. 설정값은 1ms~Int32.MaxValue ms 범위다. 한 native 호출 진입부터 반환까지의 제한이며 배치 전체 또는 관리 검사 시간 제한이 아니다. 이는 SDK 통신 timeout 설정이 아니라 응용프로그램의 대기 제한이다. 실기에서 정상 시간을 확인하여 조정한다. 연결·해제의 제한 초과도 Timeout/Faulted와 NativeCallPending을 반환하며 호출이 끝나기 전 자원을 재사용하지 않는다.

| 상태 / 동작 | 처리 |
| --- | --- |
| `Disconnected → Connecting → Connected` | SDK 로드는 Connect 시점. 포트 설정·연결·포트 상태가 계약대로 확인된 경우만 Connected |
| `Connected → Validating → Printing → Connected` | ValidateForPrint는 Validating 뒤 Connected, Print는 전체 검사 통과 후 Printing. 추가 요청은 즉시 Busy; 대기열 없음 |
| Connecting/Validating/Printing/Disconnecting 또는 NativeCallPending | 조회는 cached 상태·포트 Unknown과 Busy 반환. Connect/ValidateForPrint/Print/Disconnect는 Busy(논리 Disposed는 표준 예외 우선). Dispose는 취소·정리 예약 |
| 이미 Connected인 객체의 Connect | 같은 옵션은 현재 상태 확인, 다른 옵션은 AlreadyConnected 오류. 몰래 연결을 교체하지 않음 |
| native 결과 불명·대기 제한 초과 | Faulted. 결과를 확인하거나 해제를 검증하기 전 새 연결·인쇄 금지 |
| Disconnected의 Disconnect | 성공하는 no-op. Connected/Faulted이며 소유 연결이 있으면 Disconnecting → 직렬 해제 → 포트 닫힘 확인 |
| Dispose | 한 번만 논리 폐기. 유휴 때 bounded 해제, 호출 진행 중이면 작업 중단·정리 예약. native 강제 종료 없음. 이후 Connect/ValidateForPrint/Print/Disconnect는 ObjectDisposedException. 조회만 폐기 후에도 cached 상태와 pending 정리 상태를 읽을 수 있음 |

핸들이 없는 SDK 상태 보호를 위해 한 프로세스에서 활성 SDK 연결은 하나로 제한한다. SDK 작업 전용 스레드 한 개가 native 호출과 mutex 소유·해제를 맡는다. PrintAsync와 동기 API도 같은 실행 경로를 사용하며 사용 중 요청을 worker 대기열에 누적하지 않는다.

프로세스 슬롯은 managed 소유자 검사와 `Local\U2BIO.BT003PROLabelPrinting.Process.<PID>` named mutex로 예약하여 참여 AppDomain/객체 사이의 중복 연결도 차단한다. native 실행 worker는 소유자가 있는 동안만 활성 하나다. 동일 소유자의 작업은 재귀 mutex 취득으로 통과시키지 않고 별도 작업 진입 검사로 제어한다. 예약 권한 오류는 MutexUnavailable/ReservationDenied로 반환하며 ACL을 수정하지 않는다.

같은 PC·같은 Windows 로그인 세션의 참여 앱끼리는 정규화한 연결 대상(USB 식별자/COM 번호/IP+port)의 SHA-256으로 `Local\U2BIO.BT003PROLabelPrinting.Target.<hash>` mutex를 만들어 Connect부터 Disconnect까지 점유한다. 취득은 대기하지 않고 Busy를 반환한다. COM은 숫자로, IP는 주소의 정규 표기로, USB는 검증한 동일 식별 문자열로 맞춘다. USB 별칭을 자동 추측하지 않는다. 다른 대상 표기로 같은 장치를 가리키거나 mutex를 사용하지 않는 외부 앱, 다른 세션/PC까지 조정했다고 주장하지 않는다. 별도 서비스·서버·ACL 설정은 추가하지 않는다. abandoned 취득 후에는 현재 작업의 소유권을 설정하고 native 포트 상태를 재확인한다. 기존 작업이 성공했다고 판단하거나 재인쇄하지 않는다.

### 실패·정리 뒤 상태와 소유권

managed 상태는 `Disconnected/Connecting/Connected/Validating/Printing/Disconnecting/Faulted/Disposed`다. internal 소유권은 프로세스 SDK 슬롯, 대상 mutex, native 연결 시도/연결 소유, 진행 중 native 호출을 별도로 기록한다. 다른 요청이 소유한 연결은 정리하지 않는다. mutex는 이를 취득한 SDK 스레드가 해제하고 pending 호출·소유 연결이 남아 있으면 스레드와 점유를 유지한다. 논리 Dispose는 mutex 해제 성공을 의미하지 않는다.

| 실패 지점 | 정리·최종 상태 | 다음 동작 |
| --- | --- | --- |
| 로더 사전검사 또는 mutex 취득 실패 | 소유한 자원만 반납, Disconnected. 다른 앱 SDK 호출 없음 | 수정 후 Connect 가능 |
| 새 연결에서 SetPrinterPort가 1 | SdkStateConflict/Faulted. native 연결 소유권을 주장하지 않고 DeviceDisconnect를 호출하지 않음. 프로세스 슬롯은 유지하되 소유 대상 mutex는 반납 | 외부/기존 소유자가 해제한 뒤 명시적 Disconnect로 Closed 확인하고 Disconnected 전환. 포트 교체 금지 |
| SetPrinterPort의 알려진 설정 실패(0/1 외 반환) | DeviceConnect 미호출, 소유 점유 반납, Disconnected | 설정 수정 후 Connect 가능 |
| DeviceConnect 실패 또는 연결 후 CheckPrtConnection이 0/미정의 | 현재 시도의 부분 연결을 직렬 DeviceDisconnect → CheckPrtConnection으로 정리 | Closed 확인 시 Disconnected·점유 반납. 그 외 Faulted·점유 유지, 명시적 Disconnect만 재시도 가능 |
| 설정·연결·조회 호출 자체의 대기 제한 초과/예외로 소유권 불명 | Faulted·NativeCallPending 또는 연결 불명. 인쇄 금지, 소유 점유 유지 | 반환 후 아래 정리 규칙 적용. 호출이 안 끝나면 새 SDK 작업 금지 |
| 결과가 확정된 인쇄 실패 | 배치 중단 후 ResetForm. 연결은 재사용 판정을 위한 CheckPrtConnection으로 확인 | Open이면 Connected; Closed면 연결 소유 정리 후 Disconnected·점유 반납; 미정의/예외면 Faulted |
| 인쇄 응답 불명 또는 인쇄 호출 대기 제한 초과 | Faulted, 배치 종료. 늦은 반환 뒤 ResetForm → DeviceDisconnect → Closed 확인 | Closed 확인 뒤 점유 반납하되 Faulted 유지. 명시적 Disconnect로 Disconnected 전환 후 Connect 가능. 자동 재연결/재인쇄 없음 |
| DeviceDisconnect/정리 뒤에도 Open 또는 Unknown | Faulted(논리 폐기했으면 Disposed), CleanupCode=DisconnectUnconfirmed, 소유 점유 유지 | 폐기 전이면 명시적 Disconnect로 재시도. 폐기 후는 자동 재시도 없음, 상태 조회로 미정리 확인 |
| 연결 미소유의 Faulted에서 Disconnect | SdkStateConflict이면 소유권 없는 DeviceDisconnect 금지, 포트 Closed만 확인. 로더만 끝나고 장치 함수 미호출·점유 반납이 확인된 경우는 native 호출 없는 no-op | Closed 또는 위 no-op이면 Disconnected; Open/Unknown이면 SdkStateConflict 유지 |
| 인쇄 자체는 확정됐으나 ResetForm·후속 조회가 실패 | 기존 RowPrintResult를 보존, 상태 Faulted·CleanupCode 기록 | 해당 배치에 새 행 없음. 명시적 Disconnect로 정리 |

자동 정리는 각 필요한 메서드를 한 번만 호출하며 무한 반복하지 않는다. 대기 제한으로 호출이 pending일 때는 먼저 반환을 기다리고 다른 native 정리를 겹치지 않는다. 늦게 정상 반환하면 이미 종결된 작업의 다음 단계로 진행하지 않고 정리한다. 정상 취소이며 불명 결과가 없으면 ResetForm 후 연결을 유지할 수 있다. Dispose가 요청됐으면 연결 유지 대신 해제를 시도한다.

DeviceConnect 진입 이력이 없는 설정 호출의 불명 결과는 연결을 소유했다고 추측하지 않는다. 늦은 반환 뒤 Closed만 조회하여 자원 반납 가능 여부를 확인하고, Open/Unknown이면 SdkStateConflict/Faulted로 보류한다. 소유권 없는 DeviceDisconnect는 호출하지 않는다. 행 준비 호출에서 제한 초과했으면 PrintInvoked=false인 Failed를 반환하고 현재 소유 연결은 반환 후 해제까지 정리한다. 정리 성공 뒤에도 Faulted는 명시적 Disconnect 확인으로 해소한다.

`GetConnectionStatus`는 Disconnected/Disposed 또는 작업 진행 중이면 SDK를 호출하지 않고 cached 상태를 반환한다. Disposed에서는 Code=Disposed와 pending·정리 필드를 반환한다. Connected/Faulted이고 작업이 유휴인 경우에만 SDK 스레드에서 포트를 조회한다. 조회가 실패하면 Unknown과 StatusUnavailable/Timeout을 반환하며 연결을 자동 복구하지 않는다. Disposed는 상태 조회만 허용한다.

operation/preflight/batch의 NativeCallPending은 **그 요청이 반환할 때 자기 native 호출이 아직 진행 중이었는지**를 나타내는 불변 스냅샷이다. Busy로 거부한 새 요청은 자기 호출이 없으므로 false다. PrinterConnectionStatus의 NativeCallPending·ResourcesReleased·CleanupCode는 조회 시점의 live cached 상태다. 늦은 정리 완료는 상태 조회로 확인하고 반환한 배치 결과를 수정하지 않는다. 논리 폐기 뒤 Dispose 재호출은 no-op으로 같은 상태를 보존한다.

`ResourcesReleased=true`는 소유 점유·연결 정리가 완료된 경우만 설정한다. 프로세스 수명 동안 유지하는 native 모듈 매핑의 해제 여부는 뜻하지 않는다. 폐기된 객체의 미정리가 계속되면 앱은 이를 표시하고 해당 프로세스에서 SDK 재사용을 시도하지 않는다. 이 제한은 native 호출을 중단할 계약이 없는 SDK의 알려진 한계다.

동기·비동기 API와 테스트 UI는 동일한 바인딩·검사·인쇄 처리 경로를 사용한다. 별도의 수동 `Validate()` 호출 결과로 `Print()`의 검사를 생략할 수 없다.

USB·TCP/IP·RS-232 연결은 `SetPrinterPort()`의 인터페이스 종류와 포트 인수로 연결한다. SDK가 공개하지 않은 통신 설정을 지원하는 것처럼 추가하지 않는다. USB 포트 식별 문자열은 샘플의 `"0"`과 매뉴얼의 `"USB1"` 예시가 달라 실제 배포 DLL 기준으로 확인한다.

## 4. 검사와 출력 처리

### 사전검사

검사 대상은 서식·바인딩·프로토콜의 유효성이다.

- 확인된 인쇄 명령인지, 필수 구조와 매개변수가 유효한지 확인한다.
- 1차는 지원이 확인된 텍스트 인쇄 명령과 필요한 라벨·인쇄 설정 명령만 허용한다.
- RFID 변경 명령과 지원하지 않는 명령은 오류로 처리한다.
- 참조 변수의 매핑, 컬럼 존재, 값 타입, `format`, 빈 값 규칙을 확인한다.
- 문자열의 인코딩 가능 여부와 명령 구문을 깨뜨리는 입력을 확인한다.
- 데이터 값은 명령으로 해석하지 않는다. 사용자가 넣은 줄바꿈도 별도의 텍스트 출력으로 처리해야 하며, BTPL 명령 경계를 만들도록 단순 치환하지 않는다.
- 여러 줄 출력은 2절의 노드 분리·명시적 좌표 간격을 사용하고 payload CR/LF로 전송하지 않는다.

텍스트 넘침, 크기에 맞춘 자르기·축소, 자동 줄바꿈은 수행하지 않는다. 별도 DPI 검사를 수행하지 않는다.

호스트 렌더링을 기본으로 고정하므로 SDK의 `!!` 슬롯 치환은 사용하지 않는다. 이 결정으로 빈 슬롯·변수 초기화·중첩 치환의 미확인 동작에 의존하지 않는다. DataTable을 복사하는 동안 호출 측은 스키마·행 편집을 멈춰야 한다. 복사 중 변경·삭제 행 등 일관된 값을 읽을 수 없는 경우는 명시적 입력 오류로 반환하며 해당 값을 추측하지 않는다.

### 인쇄 전용 SDK 호출 순서

| 단계 | 호출·입력 | 확인 / 실패 시 처리 |
| --- | --- | --- |
| 연결 | `SetPrinterPort(ifType, port, ip)` | 0만 새 설정 성공. 새 소유권에서 1은 SdkStateConflict. 소유권 없는 포트 교체·해제 없음 |
| 연결 | `DeviceConnect()` → `CheckPrtConnection()` | DeviceConnect=0 및 CheckPrtConnection=1 요구. USB "0"의 SetRfidPort 미호출 연결·해제는 x86/x64 실기로 확인. 물리적 케이블 조건은 구현·검증 문서에 기록하며 RFID 연결을 추가하는 우회 없음 |
| 작업 준비 | 전체 관리 검사·렌더링 → 전체 장치 검사 | 두 층 모두 통과해야 시작. 한 건이라도 오류면 인쇄 호출 0회 |
| 각 행 직전 | 중단 검사 → `CheckPrtConnection()` | 1이면 계속. 0이면 Failed/NotConnected, 기타면 Failed/StatusUnavailable. StopReason 대응은 아래 결과 표 적용 |
| 서식 초기화 | `ResetForm()` | 파일·문자열 초기화. void이므로 성공 코드 생성 안 함. 이전 변수 데이터 초기화까지 보장한다고 가정하지 않음 |
| 서식 등록 | `SetPrintFormString(renderedBtpl)` | 완성 문자열, 모든 명령 CRLF·마지막 E CRLF. 0 아닌 값은 현재 행 Failed, 인쇄 호출 금지 |
| 한 건 실행 | 마지막 중단 검사 → `StartLabelPrint(1, responseBuffer)` | 진입 승인과 PrintInvoked 기록을 원자적으로 처리. void. 검증된 전용 응답 계약·버퍼 ABI/상한 확인 선행 |
| 결과 처리 | 전용 응답 파서 | 확인된 성공 응답만 Succeeded. 보고된 실패는 Failed. 빈·깨진·미정의 응답 또는 timeout은 Unknown. 즉시 남은 행 중단 |
| 종료 | pending 호출 반환 후 직렬 정리 | 정상 결과/확정 실패/취소는 ResetForm·포트 확인, 불명/소유권 불명/Dispose는 해제까지 수행. 3절 실패·정리 표 적용. 인쇄 결과를 덮어쓰지 않음 |

`SetPrintFormFile`은 내부에 래핑하되 공개 인쇄 경로에서는 사용하지 않는다. 매뉴얼상 파일이 문자열보다 우선하므로 각 행에서 ResetForm 후 SetPrintFormString 순서를 지킨다. 이미 검사한 원본 파일 경로를 SDK에 넘겨 재독해하도록 하지 않는다.

`SetPrintFormVariableData`, `SetRfidPort`, `SetRfidProtocol`, `SetWritingDataType(0 포함)`, `StartPrintWithRfid` 등은 이 경로에 넣지 않는다. UID-only도 RFID API이므로 인쇄 전용 증명으로 대체할 수 없다. `SetLabelSetting`은 Registry에 값을 저장하므로 기본 연결·출력에서 호출하지 않는다. 인쇄 설정은 BTPL에 명시한다. `SendData`, 응답 버퍼 설정도 결과 확인의 대체 경로로 추가하지 않는다.

`StartLabelPrint`는 TASK-02에서 고정 SDK 정상 경로의 ASCII/NUL 응답 문법·42-byte 분석 상한과 x86/x64 ANSI949 실기를 확인했다. 유효한 코드0은 SdkResponse 근거이며 물리적 배출 완료를 뜻하지 않는다. timeout101·잘못된 응답 등 호출자가 회수할 수 있는 인쇄 결과 불명은 Unknown으로 처리한다. 같은 프로세스의 native 메모리 손상·프로세스 종료에서는 결과 객체 반환을 보장하지 않으며1차의 장애 격리 범위 밖이다. 제조사 문서에 없는 항목은 버전 고정 분석·실측·경계값 검사와 명시된 제한으로 확정할 수 있다. 상세 근거는 [구현·검증 상태](NATIVE_SDK_IMPLEMENTATION.md)의 채택 계약을 따른다. **제품 전용 파서·wrapper 실행 경계의 통합은 TASK-05에서 완료하며, 확인된 SDK·인수·응답·호출 순서·timeout/pending 정리의 구현 검증 전에는 PrintContractUnverified 게이트를 유지한다. 2026-10-08 모듈 역할 정정에 따라 별도 NativeHost와 native 손상·종료 격리 보장은1차에 포함하지 않고 #10에서 미검증으로 추적한다.** RFID API·기록 명령 미사용 검토와 저장 데이터 전후 실측을 구분한다.

### 수량·대기 제한

한 데이터 행은 한 라벨이다. 서식의 `^P`, `^C`는 없으면 렌더링 시 각각 1을 넣고, 있으면 1만 허용한다. `^PI`, `^PA`, 재인쇄 `~P`는 거부한다. `StartLabelPrint(1)`과 최종 ^P1/^C1 조합의 실제 한 장 출력은 실기로 확인한다. 수량 >1을 조용히 1로 덮어쓰거나 두 수량을 곱해 예상 장수로 주장하지 않는다.

대기 제한 초과를 일괄 Unknown으로 처리하지 않는다. 인쇄 함수 진입 전후를 아래 실행 단계로 구분한다.

### 실행 단계와 중단 검사

`ExecutionStage`는 `None/ManagedValidation/DeviceValidation/ConnectionCheck/FormReset/FormRegistration/PrintInvocation/ResponseValidation/Cleanup`이다. RowPrintResult에는 현재 행의 결과를 결정한 단계와 함수명을 보존한다. 정리 단계는 기존 행의 단계·Code·응답 결과를 덮지 않고 별도 CleanupCode로 기록한다.

worker는 작업 종결 플래그, token 취소, Dispose 요청을 각 행 경계·각 native 호출 전후·StartLabelPrint 직전에 확인한다. 관리 검사도 행 경계에서 중단 가능하다. 취소로 전체 검사가 완료되지 않았으면 batch 검사 결과는 null, 이미 완료한 행 검사만 해당 RowPrintResult에 보존한다. 이때도 결과 Rows 개수는 입력 개수와 같다.

인쇄 진입 승인과 `PrintInvoked=true` 기록은 중단 플래그와 같은 동기화 영역에서 처리한다. 중단이 먼저 확정되면 StartLabelPrint를 호출하지 않는다. 진입이 먼저 승인되면 현재 native 호출은 중단하지 않고 응답/timeout 규칙으로 결과를 결정한다. PrintInvoked는 인쇄 함수 진입 시도로, 실제 용지 출력의 증거는 아니다.

대기 제한은 각 native 호출에서 monotonic 경과 시간으로 판정한다. native 반환 처리와 제한 초과 처리 중 먼저 원자적으로 작업 결과를 종결한 쪽이 우선이다. 제한 초과를 확정한 뒤 늦은 반환으로 결과를 다시 종결하지 않는다. 늦은 SetPrintFormString 정상 반환 뒤에도 종결된 작업의 StartLabelPrint로 진행하지 않는다.

| 중단/실패 위치 | 현재 행 | 나머지 행 / 자원 |
| --- | --- | --- |
| 관리·장치 검사 중 오류 | 전 행 NotAttempted, PrintInvoked=false | 전체 시작 안 함. 완료한 검사만 반환 |
| 인쇄 진입 전 취소/Dispose | 현재 행 포함 미시도 행 NotAttempted | Cancelled/Disposed. 등록한 서식만 직렬 정리 |
| 행 준비 함수의 확정 실패 또는 대기 제한 초과 | 현재 행 Failed, PrintInvoked=false | SDK 함수가 인쇄를 시작하지 않는 계약 확인 선행. 나머지 NotAttempted. pending이면 Faulted·점유 유지 |
| 장치 검사 native 조회의 대기 제한 초과 | 전 행 NotAttempted, PrintInvoked=false | batch StopReason=Timeout, DeviceValidation=null, NativeCallPending=true |
| StartLabelPrint의 알려진 실패 응답 | Failed, PrintInvoked=true | 나머지 NotAttempted. 인쇄 일부 수행 가능성을 성공이나 미출력으로 보정하지 않음 |
| StartLabelPrint의 timeout/미정의 응답/진입 후 예외 | Unknown, PrintInvoked=true | 나머지 NotAttempted. Faulted·해제 정리. 자동 재출력 없음 |
| 인쇄 호출 중 취소 뒤 정상 응답 | 현재 행 Succeeded, PrintInvoked=true | 다음 행부터 NotAttempted/Cancelled(Dispose 요청이면 Disposed) |
| 인쇄 호출 중 취소 뒤 실패/불명 응답 | 현재 행 Failed/Unknown | 실패·불명/Timeout이 취소보다 우선 |

managed 대기 제한으로 호출이 아직 진행 중이면 NativeCallPending=true다. SDK가 timeout 응답을 반환하여 호출이 끝났으면 pending=false다. native 호출을 강제로 끊거나 mutex를 먼저 해제하지 않는다. 돌아오지 않으면 같은 프로세스에서는 새 SDK 작업을 차단한다. 대기 제한은 실제 장치 출력 중단이나 native 메모리 안전을 보장하지 않는다.

### 일괄 실행

1. 요청 생성 시 템플릿·바인딩·값·옵션을 고정한다. null 요청 등 호출 위반과 논리 Disposed는 표준 예외다.
2. 같은 객체/프로세스 SDK 슬롯이 사용 중이거나 pending이면 Busy를 즉시 반환한다. 사전 취소이면 Cancelled, 그 다음 Connected가 아니면 NotConnected를 반환한다. 이 진입 실패에서는 batch·row 검사가 null이다.
3. 작업 점유를 원자적으로 취득하고 Validating으로 전환한다. 전체 관리 검사를 수행하고, 오류가 있으면 모든 행 NotAttempted/ValidationFailed로 종료한다.
4. 전체 장치 검사를 수행한다. 오류·timeout이면 모든 행 NotAttempted로 종료한다. 장치 검사 완료 결과만 반환한다.
5. 두 검사 모두 통과하면 Printing으로 전환하고 입력 순서대로 행 준비·인쇄를 실행한다. 작업당 한 템플릿, 한 행 한 라벨이다.
6. 각 단계의 중단 검사를 적용한다. 첫 실패·불명·취소 뒤에는 새로운 인쇄 호출이 없다. 나머지 행은 NotAttempted다.
7. 완료된 검사·행 결과를 불변 스냅샷으로 반환한다. 정리 실패는 별도 필드로 보존한다. 자동 재출력을 하지 않는다.

중단 사유 우선순위는 종결된 결과를 변경하지 않는 범위에서 확정 실패/불명·timeout → Dispose → token 취소다. 현재 native 호출의 결과를 우선 기록한다. 전체 행이 이미 완료된 뒤 취소가 도착하면 StopReason=None을 취소로 바꾸지 않는다. 마지막 행이 정상 완료된 경우 중단할 다음 행이 없으면 배치는 완료다. 정리가 실패해도 이미 완료한 행을 Failed로 바꾸지 않는다.

### 결과

| 건별 상태 | 의미 |
| --- | --- |
| `Succeeded` | 확인된 SDK 응답 규칙에 따라 정상 결과를 보고받음 |
| `Failed` | 현재 행 준비를 시도했으나 실패했거나 SDK가 인쇄 실패를 보고함. PrintInvoked로 인쇄 진입 전후 구분 |
| `Unknown` | 인쇄 함수 진입 뒤 timeout·응답 불명 등으로 결과를 확인할 수 없음 |
| `NotAttempted` | 검사 오류, 사용 중, 앞선 오류, 취소로 시도하지 않음 |

각 결과에는 입력 행 인덱스, 검사 오류, 확인된 native 오류 코드, 중단 사유를 제공한다. 결과 상태는 실제 라벨을 사람이 확인한 증거를 의미하지 않는다. 오류 메시지·진단 로그에 전체 입력 행이나 환자 정보를 자동으로 복제하지 않는다.

진단은 함수명·단계·숫자 코드·응답 유무/길이 등 필요한 메타데이터로 제한한다. 미확정 native 응답이나 렌더링한 원문을 로그에 자동 기록하지 않는다. 늦은 응답 진단도 같은 규칙을 지킨다. 미리보기의 데이터 표시와 자동 진단 로그는 별도 책임이다.

`CompletionEvidence`는 `None/SdkResponse`다. 전용 파서로 확인한 인쇄 성공/실패 응답만 SdkResponse이며 포트 상태·설정 성공은 인쇄 증거가 아니다. `NativeCode`는 해당 SDK 함수/응답의 원래 int 또는 null이다. 미정의 코드도 그대로 보존하고 함수별 의미를 임의 보정하지 않는다. `Code`는 라이브러리 진단용 문자열이며 아래 표의 진단 코드와 ValidationIssue.Code를 사용한다.

| StopReason | 대표 Code | Status / 검사 반환 |
| --- | --- | --- |
| None | Ok | 정상 완료. 각 검사 결과 보존 |
| ValidationFailed | InvalidTemplate/MissingColumn/EmptyValue/InvalidFormat/DeviceSupportUnverified/EncodingNotSupported | 전 행 NotAttempted. 관리 또는 장치 오류는 해당 검사 목록으로 상세 표시 |
| Busy | Busy | 전 행 NotAttempted. 미검사 null |
| NotConnected | NotConnected/PrinterFaulted | 진입/장치 검사 때는 전 행 NotAttempted. 완료한 검사만 보존. 행 준비 중 포트 닫힘은 현재 Failed, 후속 미시도 |
| ConnectionFailed | PortConfigurationFailed/ConnectFailed/SdkStateConflict/MutexUnavailable/ReservationDenied 및 5절 로더 Code | 연결 작업 오류. 인쇄 요청에서는 연결 실패를 인쇄 결과로 추측하지 않음 |
| SdkFailure | FormRegistrationFailed/StatusUnavailable/NativeCallFailed/PrintFailed | 장치 검사 중이면 전 행 NotAttempted. 행 준비/인쇄 중이면 현재 Failed. native 함수명·raw 코드 보존 |
| ResponseUnknown | InvalidPrintResponse/NativeCallFailed | 현재 Unknown, PrintInvoked=true |
| Timeout | NativeCallTimeout/SdkTimeout | 단계별 표에 따라 NotAttempted/Failed/Unknown. native pending 여부 별도 |
| Cancelled | Cancelled | 미시도 행 NotAttempted. 완료된 행 유지 |
| NoData | NoData | 입력 0행, 결과 0행, CommonIssues에 오류 |
| Disposed | Disposed | 진행 중 작업의 폐기 중단. 미시도 행 NotAttempted. 폐기 후 새 요청은 표준 예외 |

정리 코드 `CleanupCode`는 `None/Pending/FormResetFailed/DisconnectUnconfirmed/NativeCallFailed/Timeout`이다. 정리 실패만으로 BatchPrintResult.StopReason이나 완료 행을 덮어쓰지 않는다. 정리 실패로 새 행을 시작할 수 없을 때만 SdkFailure로 해당 작업을 중단한다. ValidationFailed라면 오류 없는 행도 Status=NotAttempted이며 그 행의 관리 Validation.IsValid=true는 유지한다. batch/row 검사가 null이면 UI에는 검사 안 됨/미완료로 표시한다.

각 결과 배열은 RowIndex=0..N-1이며 고정 서식은 N=1, 빈 DataTable은 N=0이다. 상태별 집계 합계는 N과 일치해야 한다. 후속 미시도 행은 앞선 중단 사유를 갖고 native 함수·코드는 null, PrintInvoked=false, ExecutionStage=None이다. 각 행의 자기 Code와 batch 첫 중단 Code는 별도로 유지한다. 공통 검사 중단의 native 원인은 batch/preflight에만 보존한다. 반환 후 늦은 응답으로 배열·검사·집계를 수정하지 않는다.

## 5. SDK 내부 래핑과 배포

실제 DLL export, 제공 헤더, 샘플 선언을 대조하여 장치 제어 API를 내부 래핑한다. RFID API도 내부 선언 범위에 포함하지만 인쇄의 공개 경로에서 호출하지 않는다. COM 등록 등 장치 제어가 아닌 인프라 export를 실행하거나 자동 등록하지 않는다.

함수별 원형·존재·반환 규약·래핑 범위와 기준 DLL 해시는 [SDK_API_COVERAGE.md](SDK_API_COVERAGE.md)에 정리했다. 헤더/샘플 합집합 41개는 내부 선언 대상이다. 헤더에만 있는 `ClearMFClassicSecurity`, `SetMFClassicSectorSecurity`도 누락하지 않는다. 별도 미공개 export `StartPrintWithRfid2`는 원형 확보 전 보류한다. `ReadResponseMessage`는 기준 DLL에 없으므로 선언하지 않는다. 이 보류 목록까지 정상 호출 가능한 것으로 주장하지 않는다.

`BlackfishNfcLib.dll`은 `HFModule.dll`에 의존한다. 실행 프로그램의 프로세스 아키텍처에 맞는 두 파일을 같은 배포 세트로 사용한다. 관리 DLL을 AnyCPU로 빌드하는 것만으로 제조사 DLL의 비트 수 문제가 해결되지는 않는다.

### 배포 폴더와 선택 규칙

각 실행 프로그램 폴더(AppDomain.BaseDirectory)에 **해당 프로세스 아키텍처 한 세트만** 둔다. 임의 native 경로 설정이나 런타임의 x86↔x64 전환을 추가하지 않는다.

```text
호출 프로그램 폴더/
  Checkup.exe 또는 SmartCheckup.exe 또는 U2BIO.BT003PROLabelPrinting.Tester.exe
  U2BIO.BT003PROLabelPrinting.dll    (관리 AnyCPU)
  BlackfishNfcLib.dll               (실행 프로세스와 같은 아키텍처)
  HFModule.dll                     (같은 기준 배포 세트)
```

Tester 빌드 출력은 bin/x86/<Configuration>, bin/x64/<Configuration>으로 분리하고 각각 PlatformTarget=x86/x64, Prefer32Bit=false를 사용한다. AnyCPU 관리 DLL의 비트 수가 아니라 실행 프로세스 비트 수로 선택한다. phase2의 AnyCPU 호스트는 배포 시 실제 실행 비트 수를 확인하여 해당 한 쌍을 배치한다. 두 native 아키텍처를 같은 파일명으로 한 폴더에 덮어쓰지 않는다. 제조사 파일 원본과 기준 해시는 SDK_API_COVERAGE.md를 따른다.

### native 로더와 실패 계약

1. Connect에서만 프로세스 슬롯과 대상 mutex를 취득한 뒤 로더를 실행한다. 객체 생성, Load/Parse, 편집, 관리 Validate/Render, Save는 제조사 DLL을 로드하지 않는다.
2. AppDomain.BaseDirectory의 절대경로 두 파일 존재·PE 아키텍처·기준 해시를 확인한다. 이미 같은 basename으로 로드된 모듈이 있다면 경로/기준 파일 일치를 확인한다. 다른 파일이면 NativeModuleConflict로 중단하고 기존 모듈을 강제 unload하지 않는다.
3. DLL 절대경로로 LoadLibraryExW를 호출한다. flags는 LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32(0x100 | 0x800)이며 의존 파일을 해당 폴더와 OS System32에서 찾도록 제한한다. PATH·현재 디렉터리·프로세스 전역 검색 경로를 변경하지 않는다. 이 flags를 지원하지 않는 OS에서는 UnsupportedLoaderPlatform으로 중단한다. 구형 Windows의 지원 조건은 [Microsoft LoadLibraryExW 문서](https://learn.microsoft.com/en-us/windows/win32/api/libloaderapi/nf-libloaderapi-loadlibraryexw)를 따른다. 패치·전역 설치는 자동 수행하지 않는다.
4. 로드된 Blackfish/HF 모듈의 실제 경로를 다시 확인하고 해당 Blackfish 핸들에서 정확한 이름으로 GetProcAddress하여 내부 StdCall delegate에 바인딩한다. 문자열·숫자·버퍼 ABI는 제공 원형과 별도 실기 계약을 적용한다. x86의 실제 장식/비장식 이름은 기준 export 대조표로 결정하고 @N을 원형 추측에 사용하지 않는다. 호출은 DLL 이름만으로 재검색하는 별도 P/Invoke 경로와 혼합하지 않는다. export 이름 일치 규약은 [Microsoft GetProcAddress 문서](https://learn.microsoft.com/en-us/windows/win32/api/libloaderapi/nf-libloaderapi-getprocaddress)를 대조했다.
5. 원형이 있는 41개 export가 기준대로 존재하는지 확인한다. 인쇄·RFID delegate의 호출은 인쇄 ABI·전용 응답 계약이 준비된 구현 버전까지 차단한다. 출력 버퍼가 없는 SetPrinterPort·DeviceConnect·CheckPrtConnection·DeviceDisconnect 네 원형은 제공 헤더·샘플을 대조한 고정 USB 연결 진단에서 별도로 검증한다. 이 진단은 process/USB 대상 예약을 같은 worker에서 유지하며 SetRfidPort·서식 등록·인쇄를 호출하거나 raw delegate를 외부로 반환하지 않는다. 생산 Connect의 공개 API·수명 통합은 TASK-05다. StartPrintWithRfid2는 호출 대상에서 제외한 보류 목록 그대로다. 기준 모듈·delegate는 프로세스 수명 동안 유지하여 Dispose/늦은 호출과 unload가 경합하지 않게 한다. instance 연결/점유 자원은 별도 정리한다.

시스템 import의 실제 경로는 현재 프로세스의 SystemDirectory와 canonical WOW64 파일 경로를 검사한다. 추가로 OS WinSxS 안의 gdiplus.dll에 한해 부모 reparse 거부·열린 파일의 final path 일치·시스템 기준 파일과 동일 길이(1~16 MiB)·SHA-256 일치를 모두 확인한다. 경로만으로 WinSxS 전체를 신뢰하거나 다른 버전/내용을 허용하지 않는다. 시스템 기준과 내용이 다른 정상 Windows SxS 버전도 현재 지원 밖이므로 오류로 남긴다.

로더 native 호출에도 대기 제한을 적용한다. 제한 초과 시 Faulted/NativeCallTimeout·pending과 소유 점유를 유지한다. 늦게 로드가 끝나도 종결된 Connect의 장치 연결 단계로 진행하지 않는다. 장치 함수를 아직 호출하지 않았으면 모듈만 process-lifetime으로 유지하고 인스턴스 점유를 반납하되 Faulted는 명시적 해제 확인으로 해소한다. 로더가 반환하지 않으면 같은 프로세스에서 재시도하지 않는다.

| 실패 | PrinterOperationResult.Code | 효과 |
| --- | --- | --- |
| 기준 두 파일 중 누락 | NativeLibraryMissing | SDK 장치 함수 0회, Disconnected·점유 반납 |
| PE 손상/프로세스 아키텍처 불일치 | NativeArchitectureMismatch | 같은 처리. 다른 비트 수로 재시도하지 않음 |
| 기준 파일 해시/쌍 불일치 | NativeVersionMismatch | 같은 처리. 같은 버전 문자열만으로 허용하지 않음 |
| 기존 로드 모듈 경로/파일 충돌 | NativeModuleConflict | 기존 모듈 그대로 보존, 새 연결 중단 |
| flags 사용 불가 OS | UnsupportedLoaderPlatform | 보호된 로드 방식을 낮추는 fallback 없음 |
| 로드 실패/전이 의존 파일 없음 | NativeLoadFailed | SystemErrorCode에 원래 Win32 값, NativeCode=null. 정확한 누락 파일을 알 수 없으면 추측하지 않음 |
| 필요한 이름의 export 누락 | NativeEntryPointMissing | SDK 장치 함수 미호출, 연결 중단 |
| 출력 ABI/응답 지원 계약 미준비 | PrintContractUnverified | 연결을 인쇄 성공 경로로 간주하지 않음. 구현 버전의 검증 게이트에서 차단 |

DllNotFoundException/BadImageFormatException/EntryPointNotFoundException 등 복구 가능한 로더 실패도 위 코드로 명시적으로 반환한다. 예외를 삼켜 연결 성공으로 처리하지 않는다. I/O·OS 접근 거부는 원래 원인을 식별 가능한 안전한 진단으로 보존한다. native 메모리 손상·프로세스 종료 같은 복구 불가능한 실패를 결과 객체로 항상 회수할 수 있다고 보증하지 않는다. 소유 pending/부분 연결이 없는 로더 실패만 점유를 즉시 반납한다.

동시 호출·같은 PC의 참여 앱 사이 점유는 3절의 수명·mutex 계약을 적용한다. 실제 SDK가 단일 프린터 전용인지, 작업 간 상태를 어떻게 유지하는지는 시험한다. 전역 경로에 DLL을 복사하거나 PATH·COM 등록을 변경하는 배포 기능을 만들지 않는다.

## 6. DB 없는 테스트 프로그램

테스트 프로그램에서 DB 연결·접속 설정·SQL 실행 화면을 만들지 않는다. 가상의 샘플 데이터로 `DataTable`을 만들고, 표에서 컬럼과 값을 편집하여 DLL에 전달한다.

| 영역 | 기능 |
| --- | --- |
| 서식 | BTPL 파일 읽기, 원문 확인, 지원 텍스트 명령의 수치 편집·저장 |
| 바인딩 | 변수별 컬럼·출력 형식·`nullText` 편집 및 JSON 저장 |
| 데이터 | 테스트 컬럼과 타입, 여러 행의 가상 값 편집 |
| 확인 | 사전검사 결과, 바인딩된 BTPL, 참고용 배치 미리보기 |
| 연결 | USB 우선, TCP/IP·RS-232 연결 설정과 포트 상태 |
| 출력 | 한 건·일괄 인쇄, 취소, 건별 결과 표시 |

수치 편집은 BTPL 좌표·글꼴·라벨 설정의 원래 단위를 표시한다. 마우스로 요소를 이동하는 디자이너와 DPI에 따른 mm/dot 변환을 추가하지 않는다.

### 명령 위치·중복·기본값

파서의 프레임은 설정 구역 → ^L 한 개 → 텍스트 구역 → E 한 개 → 파일 끝이다. 빈 행은 무시하지만 명령 인수/payload 공백은 유지한다. 설정은 ^L 앞, 텍스트는 ^L/E 사이만 허용한다. 설정이 요소 구역에 있으면 CommandPosition 오류이며 효과가 없을 수 있는 명령을 통과시키지 않는다. E 뒤 명령/두 번째 프레임은 오류다. 문자열 인수 내부 CR/LF는 명령 경계로 전송하지 않는다.

| 설정 | 미지정 시 | 중복/잘못된 값 |
| --- | --- | --- |
| ^Q, ^W, ^S, ^H | MissingSetting 관리 오류. 이전 장치/Registry 값을 대체값으로 쓰지 않음 | 같은 명령은 같은 값이어도 DuplicateSetting 오류 |
| ^P, ^C | 각각 ^P1, ^C1 생성 | 1 이외 또는 중복은 오류 |
| ^R, ^E | 미지정 때 각각 ^R0, ^E0 생성 | 명시된 값은 보존한다. ^E15는 사용자 정상 AW 시험 서식의 값이며 ^E0으로 덮어쓰지 않는다. 대상·서식·호출 경로가 검증된 조합만 출력 지원. 중복 오류 |
| ^D, ^O | 미지정 상태 보존 | 명시된 값은 0만 허용. 정상 AW 서식에 없는 명령을 자동 추가하지 않음. 양수/다른 값·중복 오류. 현재 장치는 인쇄 전용으로 준비돼 있다는 사전조건 적용 |

위 기본값은 구현 선택이며 전용 인쇄 경로에서 해당 펌웨어로 적용됨을 실기로 확인한다. ^E0를 RFID off로 해석하지 않는다. 다른 장치 설정·글꼴 설치·센서/인쇄 방식·~R 전체 회전 등은 자동 초기화하지 않는다. 현재 장치가 검증한 텍스트 인쇄 상태로 준비되어 있다는 사전조건을 배포/실기 기록에 남긴다. 모르는 장치 설정을 지우는 공장 초기화·global 설정 명령은 추가하지 않는다.

기본값 생성은 ToBtpl/Save/Render의 공통 정규화 경로에서 수행한다. 기존 유효 설정의 순서를 보존하고, 없는 기본값만 ^P,^C,^R,^E 순서로 ^L 직전에 넣는다. ^D/^O는 미지정 상태를 유지한다. 후보를 다시 파싱하여 명시되거나 생성된 각 설정이 한 개임을 확인한다. 수치 편집은 explicit 설정을 바꾸며 기본값이 화면 뒤에서 다른 값을 덮어쓰지 않는다.

WithSetting은 허용 명령의 기존 하나를 그 위치에서 교체하고, 없으면 ^L 직전에 추가한다. 중복 원문은 Parse에서 위치가 있는 오류로 반환하며 원문을 명시적으로 수정한 뒤 다시 Parse한다. RemoveSetting은 편집 중인 템플릿에서 지정한 허용 설정을 명시적으로 제거한다. 필수 설정을 삭제한 중간 상태는 편집 가능하지만 검사·저장은 오류다. unsupported/RFID 명령을 이 API로 새로 추가할 수 없다. 기존의 금지 명령은 원문을 명시적으로 수정해 Parse로 재로드하며 조용히 제거하지 않는다.

### 1차 명령 범위와 편집 규칙

언어 전체의 명령 정의는 로컬 참고 자료 `BTPL_API_GUIDE.ko.md`를 참고한다. 아래는 이번 제품의 파서·편집·검사 범위다. 명령 정의가 문서에 있다는 것과 BT-003 Pro의 해당 펌웨어·글꼴로 출력이 검증되었다는 것을 구분한다. 기본 허용 목록 밖 명령은 미리보기에서 누락시키며 통과시키지 않고 사전검사 오류로 표시한다.

| 명령 | 1차 처리 / 수치 편집 | 근거·제약 |
| --- | --- | --- |
| `^Q`, `^W` | 라벨 길이·간격/마크 위치·너비, 원래 mm 인수. 원문의 부호와 선택 인수 보존 | 가이드 3절. ^Q/^W는 필수. 변형은 정확한 파라미터 구문 검사; 라벨 크기로 텍스트 넘침 판단 안 함 |
| `^S`, `^H` | 속도 ips·농도. 서식의 값 편집, JSON 중복 없음 | 가이드 3절·API 11절. ^S/^H는 필수. 농도 0~19의 언어 구문과 DLL setter 1~19를 구분. 속도·용지 사양의 장치 지원 확인은 실기 과제 |
| `^G` | 명시된 센서 선택값 0~2 보존·편집, 미지정 시 생성하지 않음 | 가이드 3절. 현재 시험 서식의 ^G0을 보존하며 다른 값의 실제 지원은 장치 검증과 구분 |
| `^R` | 왼쪽 여백 dot, 0~399. 미지정 ^R0 | 가이드 3절. 원래 단위 그대로 |
| `^E` | 원문 수치 보존, 미지정 ^E0. 범용 의미와 RFID 모델 문맥을 구분 | 가이드 3·12절/원문 40쪽. 사용자 최종 AW 시험의 ^E15를 보존하며 지원 여부는 실제 호출 경로까지 확인한다. 미확인 조합은 장치 검사 오류; RFID off로 해석하지 않음 |
| `^P1`, `^C1` | 행당 한 장으로 고정; 미지정 시 생성 | 4절 수량 계약. 다른 수량은 오류 |
| `^D0`, `^O0` | 명시된 값만 보존, 미지정 시 생성하지 않음. 커터·Peel/Applicator 동작 없는 값만 허용 | 가이드 3절. 양수 동작·옵션 제어는 1차 제외 |
| `^L`, `E` | 텍스트 영역 시작/종료, 각 하나 | ^LI/^LM은 1차 제외. E 뒤 다른 명령 오류 |
| `AA`~`AH`, `AI`, `AK`, `AL`, `AZ1`~`AZ9` | 텍스트 추가·삭제·복사, X/Y, x/y 배율, gap, 회전/반전, payload 편집 | 가이드 4.1·원문 25쪽 이미지. x/y/gap은 dot, 배율 1~8. 0~3 회전; Asia 4~7은 설치 글꼴 지원 확인 후 허용. font W는 이 표에 없음 |
| `AT`, `ATA`~`ATZ`, `VA`~`VZ` | 정의된 TTF/다운로드 글꼴의 X/Y·크기/배율·자간·스타일 인수 | 가이드 4.2. Vt의 239문자 상한 등 명령 자체의 규격은 검사하며 넘침 판정·축소는 하지 않음. 설치 글꼴·Unicode table·ANSI 전달 계약 확인 후 출력 활성화. 자동 다운로드·Unicode 모드 변경은 하지 않음 |
| `AW` | 사용자 선택에 따른 1차 기준 텍스트 명령. X/Y와 payload 편집, 뒤의 네 인수는 원문 그대로 잠금 | API 예제와 현재 TXT에 존재. 인수 범위·한글·실제 배출은 검증된 조합만 활성화. 일반 At 배율 1~8 검사 적용 금지. AC로 자동 대체하지 않음 |
| `^V00`~`^V99` | 텍스트 payload의 호스트 바인딩 표식으로만 사용, 전송 전에 제거 | 2절 알고리즘. 프린터 변수 선언·연산은 제외 |
| `~HE`, `RM`, `WM`, `WB`, `LP`, `PW` 및 기타 RFID 명령 | 오류. ~HE0,0도 차단 | 가이드 12절·원문 40쪽. 샘플에 있다고 허용하지 않음 |
| `~R` | 전체 서식 회전 명령은 1차 미지원 오류 | 가이드 11.2절. RFID 명령이나 DPI 설정으로 분류하지 않음 |
| 바코드·QR·이미지·도형·다운로드·메모리·RTC·^XSET·^Y·프린터 변수/DB·연속/재인쇄·즉시 제어 | 1차 미지원 오류 | 가이드의 일반 기능이며 이번 제품의 확정 범위가 아님 |

표의 텍스트 명령 인수는 쉼표 뒤 payload를 포함하므로 명령별 인수 수를 적용한다(일반 At/Vt/AW는 X/Y 뒤 네 인수, AT/ATt는 X/Y 뒤 여섯 인수). 정수·허용 enum·정의된 문법을 검사하고 숫자 overflow 및 해당 명령의 정의에 어긋나는 범위·부호를 오류로 반환한다. 미확정 인수에 일반 명령의 범위를 억지로 적용하지 않는다. 변수·일련번호·날짜 문법은 고정 payload에도 허용하지 않는다.

글꼴은 BTPL 명령에 기록하고 DLL이 한글을 별도 Windows 이미지로 대체하지 않는다. AW의 제조사 SDK 내부 GDI/raster 처리는 해당 명령의 구현 과정으로 구분하며 DLL에 별도 이미지 변환·DPI 설정을 추가하지 않는다. 장치에 설치되지 않은 글꼴을 자동 설치하는 흐름도 추가하지 않는다. 해당 프린터에서 확인된 글꼴·ABI·한글의 시험 기록을 출력 지원의 근거로 남긴다. 사전검사는 구문/데이터와 확인된 장치 조건을 검사할 수 있지만, port Open만으로 모든 글리프·용지·출력을 보증할 수는 없다.

배포 버전의 출력 허용 글꼴·명령/인수 조합은 실제 검증한 범위로 고정하고 시험 근거를 남긴다. 다운로드 글꼴 설치 여부를 자동 조회할 SDK 계약이 없으므로 파서가 해당 문법을 해석했다고 설치 확인으로 판단하지 않는다. 미확인 조합은 **장치 검사**에서 DeviceSupportUnverified 오류다. 관리 검사·참고 미리보기는 유지한다. 새로 검증한 조합을 내부 명령 표에 추가하며 업무마다 DLL을 분기하지 않는다.

편집 화면은 명령별 이름·단위와 편집 가능 여부를 표시한다. 텍스트 요소의 개수·업무 필드·위치를 고정하지 않는다. 추가/삭제/복사와 수치 변경을 공통 템플릿 API에 반영하고 같은 렌더러로 미리보기를 갱신한다. unsupported 명령은 오류 위치와 원문을 보여주며 조용히 삭제·다른 명령으로 치환하지 않는다.

미리보기는 지원 명령의 배치와 바인딩된 내용을 확인하는 용도다. 출력 영역 초과를 판단하지 않고, 실제 프린터 글꼴과 실측 크기의 일치를 보장하지 않는다. 순수 서식·바인딩 검사와 참고용 미리보기는 프린터 및 제조사 DLL이 없어도 실행되도록 SDK 로드를 연결·인쇄 시점으로 분리한다.

화면에는 **참고용 미리보기**를 표시한다. BTPL 좌표 공간에서 위치·회전·문구와 수동 줄 배치를 확인하고 zoom만 적용한다. mm 용지 인수와 dot 좌표를 DPI로 결합한 실측 용지 경계는 만들지 않는다. AW 등 크기 계약 미확정 요소는 앵커와 참고 글꼴로 표시한다. 라벨의 원래 RFID 모양을 생성하지 않는다. 실제 크기·한글 글리프·RFID 보존은 실기 확인 대상이다. 이 정확도 수준은 현재 구현 설계의 한계이며 사용자가 프린터와 픽셀 단위 일치를 확정한 것으로 기록하지 않는다.

## 7. 구현 순서와 필요한 증거

아래는 큰 단계의 흐름이다. 실제 작업은 [9개 개발 작업과 선행 관계](IMPLEMENTATION_PLAN.md)로 관리한다. USB 계약 검증을 초기에 수행하되 장치가 준비되지 않았을 때도 서식·바인딩의 오프라인 구현은 진행할 수 있다. DevExpress UI는 공통 DLL의 인수 검증 뒤 시작한다.

| 단계 | 작업 | 완료 증거 |
| --- | --- | --- |
| 1 | 제공 API와 DLL 원형·export·아키텍처 대조 | 정적 대조표·기준 해시 작성 완료. 실제 내부 선언과 ABI 실기 검증은 다음 구현 단계 |
| 2 | USB 인쇄 전용 ABI·연결·응답 계약 확인 | 제공 선언·고정 SDK 정적 분석·격리 진단 및 x86/x64 실측의 지원 범위 확정. 생산 공개 API의 PrintContractUnverified 게이트를 우회하지 않음 |
| 3 | 서식 해석, JSON 바인딩, 검사·결과 모델 구현 | DB·프린터 없는 실행 가능한 검증 |
| 4 | 공통 DLL의 한 건·일괄·취소·사용 중 처리 | 자동 검사와 필요한 실기 결과 |
| 5 | DevExpress 테스트 프로그램 구현 | 샘플 데이터 편집부터 결과 표시까지의 동작 확인 |
| 6 | phase2 연동 예시 정리 | 호출 프로그램이 만든 DataTable을 넘기는 예시 |

SDK 확인은 작은 범위에서 수행하고, 확인되지 않은 응답이나 원형을 임의로 정상 처리하지 않는다. 공통 DLL을 먼저 검증하고 그 동작을 UI에서 재사용한다. 현재 phase2 프로젝트를 수정하거나 DB를 연결하는 작업은 이 설계 단계에 포함하지 않는다.

인수 사례는 해당 계층에서 확인한다. TASK-07은 C01~C24의 DLL 관리·SDK·저장 계약과 필수 USB/ABI 검사를 확인한다. C17~C19/C24의 화면 표시·세션 전환·미저장 표시·UI 출력 차단은 TASK-08의 화면 구현 후 검사하며 미리 통과로 기록하지 않는다. 특히 C24의 파일 저장/충돌/I/O/연속 저장은 DLL 검사이고 UI 표시·버튼 차단은 Tester 검사다. UI 구현 전 UI 동작 검증을 요구하는 순환 선행 조건을 만들지 않으며, 전체 프로젝트 완료에는 후속 화면 검사도 필요하다.

관리 코드 검증에는 정상·누락 컬럼·정확한 대소문자·NULL/DBNull/빈 문자열/공백·명시적 빈 nullText·형식 오류·인코딩 오류·payload 쉼표/공백/!!·예약 문자·^V03 단독/등장 순서 변경/반복·수동 줄바꿈/빈 줄/누락 LineAdvance·복사 뒤 원본 변경·전체 건 실패·결과 순서/집계·수량 위반·RFID/unsupported 명령을 포함한다. 명령 규격 안에서 길이가 늘어도 넘침 오류·자동 축소·자르기가 없음을 확인한다.

하드웨어 완료 조건은 기준 DLL 쌍의 x86/x64 ABI, USB 식별 문자열, SetRfidPort 미호출 연결과 물리적 케이블 조건, StartLabelPrint 전용 응답, 한 행 한 장, 한글/AW 또는 사용할 글꼴, 용지/리본/헤드 오류, timeout/취소/늦은 결과, 프로세스·앱 동시 점유, 해제 후 재연결이다. TASK-02의 현재 시험은 사용자 확정에 따라 RFID 리더·앱·새 태깅·저장 데이터 전후 실측을 제외한다. 호출 경로의 RFID API·BTPL 기록 명령 미사용과 실제 저장 데이터 전후 일치는 구분하여 후자는 미검증으로 기록한다. DLL에 RFID 읽기 API를 공개하지 않으며 후속 배포의 RFID 실측 범위는 별도로 확정한다. TCP/IP·RS-232도 별도 실기로 상태를 표시한다. SDK를 fake 성공 처리한 결과는 이러한 실기의 대체 증거가 아니다.

## 8. 현재 확인 사실과 구현 전 확인 사항

| 항목 | 현재 근거 / 상태 |
| --- | --- |
| phase2 플랫폼 | Checkup·SmartCheckup 프로젝트에서 .NET Framework 4.6.1, DevExpress 20.2.6 참조 확인 |
| 기존 라벨 경로 | Layout/Object 서식과 DataRow의 Tag 컬럼 바인딩, Bixolon 출력 확인 |
| DPI | 지정 SDK 샘플의 .cs/.csproj에서 별도 DPI 설정을 찾지 못함. 최종 사용자 결정으로 설정·검사 요건에서 제외 |
| 인쇄 함수 | TASK-02에서 내부 wrapper의 x86/x64 AW 인쇄를 확인. 제품 공개 흐름은 TASK-05 |
| 연결 | 후속 TASK-02에서 USB "0"과 SetRfidPort 미호출로 x86/x64 연결·해제를 확인. 다른 포트·장치 조건은 별도 검증 |
| 응답 버퍼 | 매뉴얼의 `ReadResponseMessage`는 확인한 x86/x64 DLL에서 export되지 않음 |
| 연결 상태 | `CheckPrtConnection()`은 포트 연결 여부. 용지·리본·헤드 상태를 확인한 것으로 처리하지 않음 |
| 문자열 ABI | 고정 SDK의 ANSI949 입력·ASCII/NUL 응답·정상42-byte 분석 상한 및 격리 진단 확인. 다른 버전/환경은 재검증 |
| 출력 수량·응답 | TASK-02에서 copies1/^P1/^C1의 한 장 및 전용 응답을 확인. 코드0은 SdkResponse이며 배출 완료와 구분. 제품 통합·반복 수명은 TASK-05/06 |
| BTPL 상세 규격 | 새 가이드로 일반 명령 구문이 보완됨. SDK 폴더의 원문 25·40쪽 이미지로 글꼴 표와 ~HE를 직접 확인. 전체 63쪽 PDF는 현재 저장 위치를 확인하지 못했고, AW size10/weight1·한 장·응답은 현재 장치에서 확인, 다른 조합/모델은 미검증 |
| 실기 | TASK-02 x86/x64 합성 한글·한 장·별도 M/B 이동 확인. firmware 미확인, 저장 데이터 전후 실측은 사용자 제외, 모든 장비 오류·반복 인쇄는 미검증 |

BTPL 규격과 실기 항목은 검증 과제이며, 사용자 정책을 추가로 결정해야 하는 항목과 구분한다. 이 문서 작성 자체를 DLL 구현·인쇄 시험 완료로 간주하지 않는다.

## 9. 로컬 참고 자료

- 기준 BTPL: `../BT-003 pro SDK/JODAL_WEBFORM_5.txt` (프로젝트 루트 기준).
- 샘플: `../BT-003 pro SDK/샘플소스/HFSample/HFSample_UL/Form1.cs` 및 `BT003CtrlWrapper.cs`.
- 헤더 및 기준 DLL 후보: `../BT-003 pro SDK/라이브러리/x86`, `../BT-003 pro SDK/라이브러리/x64`.
- 제공 매뉴얼 변환본: `docs/NFC_BT003-M01-04_Programming_Manual_v1.3_markdown/NFC_BT003-M01-04_Programming_Manual_v1.3.md`.
- phase2 기존 경로(phase2 루트 기준): `UserControls/Class/LabelPrint/clsLabelPrint.cs`, `clsLabelPrintPaper.cs`, `UserControls/Frm/FrmLabelPrintManager.cs`.

SDK 샘플과 기존 제조사 문서는 변경하지 않는다. 설계 작성 단계에서는 Git 원격 연결 확인만 수행하며 push는 수행하지 않는다.

## 10. BTPL 가이드 검토

검토 대상은 로컬 참고 자료 `docs/BTPL_API_GUIDE.ko.md`다. SHA-256은 `6700c7b53292832302257860963be8aaa58a0f1ac39a7cf67757610c6e967ca7`이다. 샘플 폴더의 동명 가이드는 SHA-256 `e4cfa7807fb52a67e6e0bb2cc06b6a2d31aee9db4eac420dc1d0a6d27f31673e`로 내용이 다르다. 새 문서를 기존 샘플 문서의 동일 사본으로 간주하지 않는다. 두 원본은 수정하지 않는다.

| 검토 항목 | 결과 / 설계 반영 |
| --- | --- |
| CRLF, 파일 서식 우선, ResetForm, !!·^Vxx, API 옵션보다 서식 우선 | API Rev.1.3 설명과 일치. 2·4절에 반영 |
| ~HE0,0 | 샘플 가이드 및 BTPL 원문 40쪽 이미지의 외부 안테나 모드와 일치. 인쇄 전용 허용 명령에서 제외 |
| 일반 글꼴 표 / AW | 원문 25쪽에 W 없음. AW 존재와 인수 계약의 확정을 구분한 가이드 판단에 동의. 6절 제한으로 반영 |
| 39개 P/Invoke 선언 | 샘플 39개와 이름 집합 일치. 두 기준 DLL에 존재. 헤더 전부를 포함한 것은 아니므로 별도 대조표에서 41개 + 원형 미공개 1개 관리 |
| StartLabelPrint | 가이드도 RFID 수행 여부·응답 계약 불명확성을 표시. 존재 확인을 인쇄 성공·RFID 보존 증거로 확대하지 않음 |
| 오류표 | 원문 Hex/Dec 불일치 6행을 보존한 설명은 API PDF/변환본과 일치. raw 코드 보존 |
| 예제의 ASCII·길이 제한·이미지·RAW Serial/TCP·RFID 발행 | 일반 참고 예제다. 그 제한/경로를 제품에 복사하지 않음. SDK를 통해 연결하고 텍스트 글꼴로 출력 |
| DPI·텍스트 fit 검사 | 가이드의 일반 설명이며 사용자 최종 정책에 따라 제품의 입력·검사에 반영하지 않음 |
| 검증 스크립트와 실행 증거 | 2026-10-08 기준 docs에 `verify_btpl_guide.ps1`가 존재함을 확인. 기준 확정 작업에서는 실행하지 않음. 샘플 폴더의 `verify_btpl_examples.ps1`는 다른 11건 파서 검사용. 새 가이드의 71건/3컴파일 단위 시험을 이번 작업에서 재현했다고 주장하지 않음 |
| BTPL 원문·외부 검수 | 가이드의 원문 63쪽 전수/웹 검수 기록은 작성자의 기록이다. 이번 검토는 로컬 원문 이미지·API·소스·export와 대조한 범위만 직접 확인 |

현재 근거로 문서 계약을 보완했다. 다음은 별도 DLL 구현과 제한된 실제 장치 계약 검증이다. 가이드만으로 해결되지 않는 항목은 8절과 실기 완료 조건에 남기며 성공으로 간주하지 않는다.

## 11. 계약 추적과 구현 인수 사례

30초 제한·한 프로세스 SDK 슬롯·연결 수명 mutex·nullable 검사·참고 미리보기·LineAdvances·로컬 단일 native 세트·기본 설정 생성은 확정 사용자 정책을 만족시키기 위한 **구현 선택**이다. 사용자 정책 표에 새로운 요구사항으로 합치지 않는다. native ABI/펌웨어 사실은 문서 선택으로 확정할 수 없으며 8절 상태를 유지한다.

| 보완 항목 | 구현 계약의 위치 | 필요한 인수 사례 |
| --- | --- | --- |
| 1 오프라인 관리/장치 검사 분리 | 3절 관리 검사·미리보기 | C01, C02 |
| 2 단계별 중단·늦은 반환 | 4절 실행 단계 | C05~C13 |
| 3 미검사·집계·사유 | 3절 nullable 결과, 4절 결과 | C02~C04, C06~C13 |
| 4 실패 후 수명·소유권 | 3절 실패·정리 | C07, C08, C11, C13~C16 |
| 5 줄 배치 저장·재매핑 | 2절 편집·저장 | C17~C19, C24 |
| 6 배포·로더 | 5절 배포 | C20, C21 |
| 7 명령 위치·중복·기본값 | 6절 명령 위치 | C22, C23 |

아래는 **구현 후 검증할 기대 결과**이며 현재 DLL/장치 시험 통과 기록이 아니다. 인쇄 SDK의 성공/실패 응답은 전용 계약을 확보한 뒤 그 실제 형식으로 시험한다. S/F/U/N은 Succeeded/Failed/Unknown/NotAttempted다. 별도 표시가 없으면 입력 3행·두 검사 통과·순서대로 인쇄하는 조건이다.

| ID | 상황 | 기대 결과 |
| --- | --- | --- |
| C01 | 해석 가능한 AW, 배포 출력 조합 미검증 | offline Render는 참고 preview, candidate BTPL 반환 가능. ValidateForPrint는 DeviceSupportUnverified, NNN·인쇄 진입 0회 |
| C02 | 두 번째 값 빈 값, nullText 없음 | 관리 Rows valid=[true,false,true], NNN, batch ValidationFailed, DeviceValidation=null |
| C03 | token 사전 취소 | NNN/Cancelled, batch·row 검사=null, 인쇄 진입 0회 |
| C04 | 다른 작업/pending으로 Busy | NNN/Busy, 미검사 null, 새 요청 NativeCallPending=false·자기 SDK 호출 0회 |
| C05 | 관리/장치 검사 통과 뒤 첫 행 포트 Closed | FNN, PrintInvoked=[false,false,false], StopReason=NotConnected |
| C06 | 첫 행 정상, 둘째 서식 등록 실패 | SFN, PrintInvoked=[true,false,false], SdkFailure/FormRegistrationFailed·raw 코드 유지 |
| C07 | 첫 서식 등록이 managed 제한 초과 후 늦게 정상 반환 | FNN/Timeout, PrintInvoked 전부 false. late StartLabelPrint 0회, 소유 해제 정리, 결과 불변 |
| C08 | 둘째 인쇄 호출 managed 제한 초과 | SUN/Timeout, PrintInvoked=[true,true,false], 반환 시 pending=true. 늦은 정상 응답으로 U를 S로 변경 안 함 |
| C09 | 첫 인쇄 중 취소 후 정상 응답 | SNN/Cancelled, 첫 행 완료 증거 유지 |
| C10 | 첫 인쇄 중 취소 후 알려진 실패 응답 | FNN/SdkFailure. 실패가 취소보다 우선 |
| C11 | 첫 인쇄 중 Dispose 후 정상 응답 | SNN/Disposed. 논리 상태 Disposed, 직렬 해제 뒤 ResourcesReleased=true 또는 정리 실패 명시 |
| C12 | 세 행 완료 후 취소 | SSS/None, 집계 3. 뒤늦은 취소로 결과 변경 안 함 |
| C13 | 세 행 성공 뒤 정리/해제 실패 | SSS/None 유지, CleanupCode 실패·Faulted(폐기했으면 Disposed), 미정리 소유 자원 유지 |
| C14 | DeviceConnect 실패 뒤 부분 연결 해제·Closed 확인 | 연결 실패 결과, Disconnected, ResourcesReleased=true |
| C15 | DeviceDisconnect 뒤에도 Open | Faulted/DisconnectUnconfirmed, ResourcesReleased=false, native 동시 재사용 금지 |
| C16 | pending 상태에서 상태 조회, 이후 폐기 후 조회 | SDK 호출 없이 cached pending/정리 상태. 다른 public 사용은 ObjectDisposedException |
| C17 | 첫 literal 2줄 요소와 다음 가변 요소 저장 | 첫 노드는 BTPL 2개로 저장, 다음 인덱스 1→2, 옵션 재매핑, ^Vxx 보존. 재로드 fixed 줄 좌표 동일 |
| C18 | RemoveText(0), 끝 복사, WithText | 뒤쪽 옵션 1 감소, 삭제 옵션 폐기, 복사 벡터 명시 복사, 교체 인덱스 유지 |
| C19 | 재시작/다른 서식 로드 뒤 가변 값에 LF | 세션 옵션 없음 표시, MissingLineAdvance, 전체 NNN. 이전 서식 옵션 자동 적용 없음 |
| C20 | native 누락/아키텍처·해시·기존 모듈 불일치 | 구분된 Code, 제조사 장치 호출 0회. 관리 편집·preview 유지 |
| C21 | 제한된 로더 flags 사용 불가/필요 export 없음 | UnsupportedLoaderPlatform/NativeEntryPointMissing. 검색 보호를 낮추는 재시도 없음 |
| C22 | ^H 중복/설정이 ^L 뒤/E 뒤 명령 | DuplicateSetting/CommandPosition/프레임 오류, 인쇄 0회. 같은 값의 중복도 오류 |
| C23 | 필수 Q/W/S/H만 있고 선택 설정 없음 | P1,C1,R0,E0를 ^L 직전에 한 번씩 생성. 미지정 D/O는 생성하지 않음. 명시값 보존. 1행=1장 실기는 별도 |
| C24 | 인코딩 오류·원본 해시 변경·교체 I/O 실패·JSON만 저장 실패·연속 저장 | 저장 오류/미저장 표시. 원본 직접 truncate/삭제 우회 없음. UI 출력 차단, 파일 쌍의 원자 저장 주장 없음. 성공한 Save의 반환 객체로 다시 저장하면 자기 변경을 충돌로 오판하지 않음 |

결과 구조의 기계 검사 예시는 다음과 같다. C02/C06/C07/C08의 expected 상태를 확인하기 위한 데이터이며 실제 SDK 응답 또는 작업 저장 형식이 아니다.

```json
{
  "expectedResults": [
    { "case": "C02", "inputCount": 3, "stopReason": "ValidationFailed", "statuses": ["NotAttempted", "NotAttempted", "NotAttempted"], "printInvoked": [false, false, false], "managedValid": [true, false, true], "deviceChecked": false },
    { "case": "C06", "inputCount": 3, "stopReason": "SdkFailure", "statuses": ["Succeeded", "Failed", "NotAttempted"], "printInvoked": [true, false, false], "managedValid": [true, true, true], "deviceChecked": true },
    { "case": "C07", "inputCount": 3, "stopReason": "Timeout", "statuses": ["Failed", "NotAttempted", "NotAttempted"], "printInvoked": [false, false, false], "managedValid": [true, true, true], "deviceChecked": true },
    { "case": "C08", "inputCount": 3, "stopReason": "Timeout", "statuses": ["Succeeded", "Unknown", "NotAttempted"], "printInvoked": [true, true, false], "managedValid": [true, true, true], "deviceChecked": true }
  ]
}
```
