# 동기 USB SDK 공개 API

이 문서는 DLL 호출 개발자용 안내와 현행 인쇄 계약이다. 실행 환경은 [배포 안내](SDK_DEPLOYMENT.ko.md), 용지·위치 복구는 [공통 운영 안내](PRINT_PRESENTATION.ko.md#공통-복구)를 먼저 확인한다. 제조사 `BT003CtrlWrapper.cs`를 직접 사용하며, 공개 함수와 SDK 호출 순서는 [Bt003Printer.cs](../src/U2BIO.BT003PROLabelPrinting/Bt003Printer.cs), 반환값은 [PrinterModels.cs](../src/U2BIO.BT003PROLabelPrinting/PrinterModels.cs)에 있다.

서식 명령, 좌표, AW 글꼴 처리와 변수 치환은 SDK가 담당한다. DLL은 입력을 검사하고 연결·등록·인쇄·배출·정리의 순서를 관리한다. 기존 비동기·취소·일괄 API는 제공하지 않는다. 선택 Templates 기능을 쓰면 변수 표식이 남은 `ToBtpl()` 원문을 `Print`에 전달한다. 화면 표시용 치환 문자열은 인쇄에 재사용하지 않는다.

바이트 그대로 보존한 제조사 Wrapper의 실제 SDK 선언·원문 변수 매핑 예제는 유효하다. 추가 제품 사용 예제와 일부 주의 주석은 이전 배열 인수·결과 속성 계약을 설명한다. 현재 DLL 호출은 이 문서의 variableData 문자열과 IsSuccess·Message 예제를 따른다.

## 공개 함수

| API | 용도 |
| --- | --- |
| ConnectUsb(string port, int presentationDistanceMm = 0) | USB 포트 지정·연결·Open 확인, 배출 0..1000 mm |
| GetConnectionStatus() | 소유 연결 확인; NativeCode 1=Open, 0=Closed |
| SetPrintingOptions(ushort speed, ushort darkness) | 양의 속도·농도 1..19; 정상 설정을 각 인쇄에 재적용 |
| Print(string btpl, string variableData = null) | 문자열 서식을 CRLF로 정규화하고 선택 변수와 함께 등록하여 한 장 인쇄 |
| PrintFile(string path, string variableData = null) | CP949 파일 내용을 CRLF로 정규화해 SetPrintFormString으로 등록하여 한 장 인쇄; 잠금은 정리까지 유지 |
| ConfirmPrintPosition() | 호출자가 실제 용지를 정렬한 뒤 위치 기록을 0으로 확인 |
| FeedAndConfirmPosition() | FEED 키와 같은 `~S,FEED`를 보내고 정상 응답이면 위치 기록을 0으로 확인; 복귀(^B)·배출(^M) 없음 |
| Disconnect() | SDK 해제·Closed 확인; 실패 결과를 반환하고 명시적 재시도 허용 |
| Dispose() | 진행 중인 작업·정리를 기다린 뒤 연결 해제; 해제 실패는 예외로 전달 |

Tester는 `FeedAndConfirmPosition`에 해당하는 [FEED+확인]만 제공한다. `ConfirmPrintPosition`은 실제 정렬을 확인할 수 있는 외부 DLL 호출자를 위해 유지한다. FEED 정상 응답과 기록 갱신은 물리 정렬 완료를 보장하지 않으며, [전원 재투입 후 알려진 제한](PRINT_PRESENTATION.ko.md#전원-재투입-후-알려진-제한)이 있다.

`Dispose` 외의 메서드는 `PrinterResult`를 반환한다. 입력 오류는 SDK 호출 전에 예외로 전달한다. API는 SDK가 반환할 때까지 기다리는 동기 방식이므로 UI의 백그라운드 실행은 호출 프로그램이 맡는다. 같은 물리 프린터는 한 프로그램에서만 사용한다.

## 기본 사용 순서

1. 사용 중인 USB 포트 식별 문자열과 용지에 맞는 추가 배출 거리를 준비한다. 블랙마크 용지의 ^Q 형식, 배출 거리, 인쇄 영역과 새 용지 보정은 [운영 기준](PRINT_PRESENTATION.ko.md#운영-기준)을 따른다.
2. `ConnectUsb`로 연결하고 결과를 확인한다.
3. 처음 실행하거나 용지 위치가 불명확하면 [공통 복구](PRINT_PRESENTATION.ko.md#공통-복구)에 따라 실제 정렬과 위치 확인을 한다. 신뢰할 위치 기록이 있으면 매 요청마다 FEED하지 않는다.
4. 필요한 경우 `SetPrintingOptions`로 속도와 농도를 설정한다.
5. 서식에 맞게 `Print` 또는 `PrintFile`을 호출하고 결과를 확인한다.
6. 사용을 마치면 `Disconnect` 또는 `using` 종료의 `Dispose`로 연결을 해제한다.

다음은 변수 두 개를 사용하는 파일 서식의 예다. `selectedUsbPort`, `presentationDistanceMm`, `formPath`는 호출 프로그램이 준비한다. 이 예제는 용지 위치 확인이 끝난 상태를 전제로 하며 실행하면 실제 SDK 연결과 인쇄를 시도한다. 최초 위치 불명 복구를 자동으로 생략하거나 실패 뒤 재인쇄하지 않는다.

```csharp
using (var printer = new Bt003Printer())
{
    var connected = printer.ConnectUsb(selectedUsbPort, presentationDistanceMm);
    if (!connected.IsSuccess)
    {
        Console.WriteLine(connected.Message);
        return;
    }

    var options = printer.SetPrintingOptions(4, 12);
    if (!options.IsSuccess)
    {
        Console.WriteLine(options.Message);
        return;
    }

    // 서식이 V00/V01을 참조하면 모든 값을 요청마다 제공한다.
    var result = printer.PrintFile(formPath, "FIRST LINE!!SECOND LINE");
    if (!result.IsSuccess)
    {
        Console.WriteLine(result.Message);
        return;
    }
    Console.WriteLine("SDK 처리가 정상적으로 끝났습니다.");
    Console.WriteLine("주 작업 SDK 응답: " + result.NativeCode);
    Console.WriteLine("인쇄 호출 시도: " + result.PrintInvoked + ", 인쇄 응답: " + result.PrintCode);

    // 실패·불명이어도 일부 출력되었을 수 있으므로 결과를 확인한 뒤 사람이 판단한다.
}
```

추가 배출 거리는 기본값이 0이며, 0이면 DLL이 인쇄 뒤 별도 `^M`을 보내지 않는다. 양수이면 정상 인쇄 응답 뒤 그 거리만큼 배출하고 다음 요청 전에 `^B`로 복귀한다. 거리는 사용하는 용지에 맞춰 정하며 DLL이 임의의 거리나 좌표를 적용하지 않는다.

`SetPrintingOptions`는 정상 설정만 보관하여 인쇄할 때마다 다시 적용한다. 서식에 `^S`와 `^H`가 있으면 SDK의 서식 옵션 우선순위를 따른다. `GetConnectionStatus`의 `NativeCode=1`은 포트가 열렸다는 의미이며 용지·리본·헤드 준비나 인쇄 완료를 확인한 결과가 아니다.

## 파일·문자열·변수 입력

입력에 맞춰 다음 호출 중 하나를 선택한다. `btpl`은 호출자가 준비한 문자열이며, `formPath`는 CP949 파일 경로다.

```csharp
// 변수 없는 문자열 서식
var result = printer.Print(btpl);
```

```csharp
// 변수 없는 파일 서식
var result = printer.PrintFile(formPath);
```

```csharp
// V00, V01을 사용하는 파일 서식
var result = printer.PrintFile(formPath, "FIRST LINE!!SECOND LINE");
```

각 예는 사용할 서식에 맞춰 선택하는 별도 호출이다. `variableData`는 SDK 항목 구분자 `!!`로 연결한 원문 문자열이다. SDK는 첫 항목부터 V00, V01 순서로 번호에 매핑하며 출력 요소 등장 순서와 무관하다. 사용하지 않는 추가 데이터는 SDK가 생략한다. DLL은 분리·재조합·trim·번호 해석을 하지 않는다. 최고 참조 번호까지 필요한 데이터를 매 요청 모두 제공하는 것은 호출자 책임이다. `ResetForm`이 이전 변수를 지운다고 가정하지 않으므로 non-null 요청마다 다시 등록한다.

인수 생략 또는 명시적 `null`은 `SetPrintFormVariableData` 등록 생략이다. 변수 참조가 없는 서식에서만 사용한다. 빈 전체 문자열·공백만인 문자열·NUL·제어문자는 `InvalidVariables` 예외이며 null로 바꾸지 않는다. 앞뒤 공백·단일 `!`·앞뒤/연속 `!!`는 기본 검사에 맞으면 원문 전달하지만 빈 항목·부족 데이터의 native 의미와 출력 성공은 보장하지 않는다.

기본 ANSI 코드 페이지(`Encoding.Default.CodePage`)가 949인 환경만 지원한다. 포트·경로·문자열·변수는 엄격한 CP949 왕복과 NUL 검사를 거친다. 서식, 구분자를 포함한 가변데이터와 파일은 각각 4 MiB 이내여야 한다. `Print`와 `PrintFile`은 서식의 줄끝을 CRLF로 정규화하고 마지막 CRLF를 붙인 뒤 크기를 다시 검사한다. 가변데이터는 줄끝을 바꾸지 않는다.

`PrintFile`은 상대경로를 현재 작업 디렉터리 기준으로 절대경로화한다. 같은 `FileStream`으로 크기를 확인하고 내용을 읽으며, `FileShare.Read`로 열어 쓰기·삭제 공유를 허용하지 않는다. 파일 핸들은 SDK 인쇄와 마지막 서식 정리가 끝날 때까지 보유한다. 파일 오류, SDK 실패 또는 서식 정리 실패로 반환해도 핸들을 닫는다.

SDK에는 파일 경로 대신 검사한 내용을 `SetPrintFormString`으로 등록한다. SDK가 파일을 다시 여는 경로를 사용하지 않으며 AW 처리는 계속 SDK가 담당한다. 실패 뒤 다른 등록 방식으로 자동 재인쇄하지 않는다.

## 제조사 방식의 가변데이터와 이행

```csharp
string variableData = "홍길동!!1122334455!!20120201!!Blackfish BT-003";
var result = printer.PrintFile(formPath, variableData);
```

SDK는 V00=홍길동, V01=1122334455, V02=20120201, V03=Blackfish BT-003으로 매핑한다. TXT가 V00/V02/V01 순서이면 그 번호에 해당하는 값을 사용하고 제4항목을 사용하지 않는다. V03만 있는 다른 TXT에도 같은 데이터를 제공할 수 있다. 실제 치환·장치 출력은 SDK가 수행하며 이 예제는 관리 검증의 물리 관측을 뜻하지 않는다.

기존 여러 인수 호출은 하나의 문자열로 바꾼다. 기존 string[] 호출은 호출 프로그램에서 `string.Join("!!", values)` 등으로 데이터를 준비한다. 값 자체에 `!!`가 있을 때의 이스케이프 규칙은 이 API가 제공하지 않는다. 과거 배열 overload는 남기지 않는다.

모든 기존 호출 앱은 새 DLL 참조로 재빌드해야 한다. 변수 없는 `Print(btpl)`·`PrintFile(path)`의 소스 표현은 같아도 과거 바이너리는 `(string, string[])` 메서드를 참조하므로 DLL만 교체할 수 없다. 기존 명시적 null은 입력 오류에서 등록 생략으로 변경된다.

## SDK 호출 흐름

연결은 `SetPrinterPort(1, port, "") → DeviceConnect → CheckPrtConnection` 순서다. 인쇄는 다음 순서이며 실패한 준비 단계 이후에는 인쇄를 호출하지 않는다.

1. 연결 상태를 확인하고 이전 정상 배출 거리가 있으면 `^B`로 복귀한다.
2. `ResetForm`으로 이전 서식을 정리한다.
3. 성공한 설정이 있으면 `SetPrintingOption`으로 다시 적용한다.
4. `SetPrintFormString`으로 이번 서식을 등록한다.
5. 변수 값이 있으면 `SetPrintFormVariableData`로 등록한다.
6. 제조사 `StartLabelPrint(1, new StringBuilder(100))`로 한 장을 요청한다.
7. 인쇄 응답이 0이고 추가 배출 거리가 양수이면 `SendData`로 `^M`을 보낸다.
8. `finally`에서 `ResetForm`으로 서식을 정리하고, 파일 인쇄이면 그 뒤 파일을 닫는다.

이 순서는 `PrintCore`에 직접 보인다. 실패한 SDK 함수는 한글 `Message`에서 확인한다. `NativeCode`는 다음 주 작업 호출 전에 비우므로 응답 전 예외에 이전 숫자가 남지 않는다. 마지막 정리 오류는 원래 숫자와 설명을 덮지 않고 `Message`에 추가한다.

## 반환 결과 읽기

| 필드 | 의미 |
| --- | --- |
| IsSuccess (bool) | 요청한 SDK 처리와 필요한 배출·정리가 정상적으로 끝나면 true. 실패와 결과 불명은 false |
| Message | 상태·SDK 함수·이동/정리 오류를 사람이 읽는 한글 설명 |
| NativeCode | 주 작업의 마지막 SDK 응답. 미호출·응답 전 예외이면 null; 연결 확인은 1=Open, 0=Closed. 후속 서식 정리는 이 값을 덮지 않음 |
| PrintInvoked | StartLabelPrint 호출 진입 직전에 기록한 값. 실제 출력 완료 여부가 아님 |
| PrintCode | 해석한 인쇄 응답. 미호출·잘못된 응답·응답 전 예외이면 null |

공개 속성은 이 다섯 개뿐이다. 정상 여부는 `IsSuccess` 하나로 판단하고 `Message`로 이유를 읽는다. 성공 여부의 판단은 DLL에서 끝내므로 호출자가 SDK 숫자를 bool로 변환할 필요가 없다. Status와 PrinterResultStatus enum은 제공하지 않는다. 호출자가 결과를 직접 만들지 않도록 생성자는 internal이다. 디버거에서는 속성을 펼치기 전에 IsSuccess와 Message를 먼저 표시한다.

```csharp
var result = printer.PrintFile(formPath);
if (!result.IsSuccess)
{
    Console.WriteLine(result.Message);
}
```

일반 호출부는 IsSuccess와 Message만 확인하면 된다. `Message`는 사람의 확인용이며 프로그램이 문장을 파싱해 자동 복구하지 않는다. 오류 조사에는 `PrintInvoked`와 `PrintCode`를 함께 확인하되 별도의 성공 기준으로 사용하지 않는다. 이전 Status 비교는 `if (!result.IsSuccess)`로 바꾸고 호출 프로그램을 재빌드한다. Code, Function, MovementCode, CleanupCode 사용처도 현재 계약으로 이행한다. bool 반환 overload나 결과 객체의 암시적 bool 변환은 제공하지 않는다.

| 상황 | 결과의 의미 |
| --- | --- |
| 정상 인쇄·필요한 배출·서식 정리 완료 | IsSuccess=true. SDK 정상 종료이며 물리 출력 완료 확인은 아님 |
| 서식 등록 실패 | IsSuccess=false, PrintInvoked=false이며 StartLabelPrint를 호출하지 않음 |
| 정상 인쇄 응답 뒤 배출 실패 | IsSuccess=false, PrintInvoked=true, PrintCode=0을 보존 |
| 인쇄 응답 101·미확인 코드·잘못된 응답 | IsSuccess=false. 결과 불명을 Message에 설명하고 실제 일부 출력 가능성 때문에 자동 재인쇄하지 않음 |
| 정상 인쇄 응답 뒤 서식 정리 실패 | IsSuccess=false, PrintCode=0과 원래 NativeCode·설명은 보존; Message에 ResetForm 오류 추가 |
| 불명 응답 뒤 서식 정리도 실패 | IsSuccess=false와 원래 응답·불명 설명을 유지하고 Message에 정리 오류 추가 |

예를 들어 인쇄 0 뒤 배출 응답 7은 IsSuccess=false, NativeCode=7, PrintCode=0이며 Message가 인쇄 응답 정상과 SendData 배출 실패를 함께 설명한다. 같은 배출이 응답 전 예외를 내면 IsSuccess=false, NativeCode=null, PrintCode=0이다. 성공한 사전 복귀의 0을 마지막 배출 예외의 숫자로 남기지 않는다.

SDK 코드 0과 `SendData` 코드 0은 물리 출력·이동 완료 증거가 아니다. IsSuccess=false는 무출력을 뜻하지 않으므로 같은 요청을 자동 재인쇄하지 않는다.

## 내부 상태와 복구

기존 상태 이름을 그대로 사용하며 역할은 다음과 같다.

| 상태 | 역할 |
| --- | --- |
| Gate | 프로세스의 SDK 호출, 서식 정리와 연결 해제를 직렬화하는 잠금 |
| owner | SDK 연결을 사용하는 객체. 닫힘을 확인하지 못하면 소유권 유지 |
| faulted | 연결·해제·서식 정리 실패 후 인쇄와 옵션 재사용을 차단하는 상태 |
| Positions[port] | null=위치 불명, 0=복귀 불필요, 양수=다음 인쇄 전 복귀 거리(mm) |
| hasOptions | 정상 설정한 속도·농도를 이후 인쇄에서 다시 적용할지 표시 |
| disposed | 연결 해제를 마치고 객체 사용을 종료했는지 표시 |

`Positions`는 명령 이력이며 센서로 확인한 위치가 아니다. 인쇄·이동 전에 불명으로 기록하고 정상 응답에서만 갱신한다. 같은 프로세스의 재연결이나 새 객체도 기록을 유지한다. 같은 기록을 `C:\Bitek\BT003\position.txt`(첫 줄 포트, 둘째 줄 mm 또는 `unknown`)에 저장하므로, 프로그램을 다시 실행해도 첫 `ConnectUsb`가 기록을 읽어 다음 인쇄 전에 직전 배출 거리만큼 복귀한다. 파일이 없거나 손상됐거나 다른 포트의 기록이면 위치 불명으로 시작한다. SDK 진입 전에 이 파일을 쓰지 못하면 SDK를 호출하지 않고 IsSuccess=false를 반환한다. DLL은 수동 FEED·용지 교체를 감지하지 않으므로 운영자가 정렬 후 `ConfirmPrintPosition`을 호출하거나, 대기 화면에서 `FeedAndConfirmPosition`으로 FEED와 확인을 함께 한다. 정지 위치는 DLL의 M/B만 담당하며 프린터 LCD 정지 위치 설정에 의존하지 않는다.

Message가 용지 위치 불명을 안내하면 실제 위치를 확인하고 수동으로 정렬한 뒤 `ConfirmPrintPosition`을 호출하거나, 대기 화면에서 아래 `FeedAndConfirmPosition`을 호출한다. `ConfirmPrintPosition`은 기록을 0으로 바꿀 뿐 센서를 조회하거나 용지를 이동하지 않는다. 실제 정렬 없이 호출하면 이전 양수 배출 이력이 지워져 다음 인쇄의 복귀가 생략될 수 있다. 이미 지운 기록은 재연결·재시작으로 복원되지 않는다. 이 오조작을 막기 위해 Tester에는 직접 확인 메뉴가 없으며 [FEED+확인]을 사용한다. `FeedAndConfirmPosition`은 실제 FEED를 수행한다.

`FeedAndConfirmPosition`은 FEED 키 + `ConfirmPrintPosition` 절차를 DLL이 대신한다. 연결 확인과 2초 간격 대기 뒤 위치를 불명으로 저장하고 `SendData("~S,FEED")`를 보낸 다음, 응답 0과 기록 저장 성공에서만 위치를 0으로 확인한다. 배출 상태여도 복귀하지 않으며, 응답 실패·예외·저장 실패이면위치를 불명으로 남긴다. 프린터는 마지막으로 받은 ^Q로 마크를 맞추므로 ^Q를 바꾼 직후에는 새 서식으로 한 장 인쇄한 뒤 호출하고, LCD 메뉴가 열린 상태에서는 호출하지 않는다. 응답 0도 정렬 완료 증거가 아니다. 운영 기준 블랙마크 용지의 x64·x86 실기에서 배출 상태와 위치 불명 상태의 FEED 후 출력이 수동 절차와 같다는 사용자 관측이 있다. 확인 범위와 한계는 [운영 기준](PRINT_PRESENTATION.ko.md#운영-기준)을 따른다.

Message가 SDK 연결 복구를 안내하면 `Disconnect`로 닫힘을 확인한 뒤 재연결한다. `GetConnectionStatus`가 Open을 반환하거나 `ConfirmPrintPosition`을 호출해도 faulted는 해제되지 않으며, faulted 연결에서 `FeedAndConfirmPosition`은 FEED를 보내지 않는다. 연결 상태와 용지 위치에 모두 문제가 있으면 각각 복구해야 한다.

`Disconnect`는 `DeviceDisconnect`의 정상 응답과 `CheckPrtConnection`의 Closed를 확인한다. 확인되지 않은 해제는 소유권을 유지하므로 다른 객체로 우회하지 않는다. 소유자가 아닌 객체의 조회·옵션·인쇄는 실패하고, 해제와 `Dispose`는 SDK를 호출하지 않는다.

프린터 전원 재투입은 자동 감지하지 않는다. `position.txt`와 메모리의 배출 기록은 SDK 명령 이력이며 전원 재투입 후 실제 위치나 장치 내부 원점을 저장·복원한 값이 아니다. 유효한 기록과 연결 Open만으로 전원 재투입 뒤 첫 장 위치를 보장할 수 없다. 첫 장 상단 이상과 FEED 안쪽 이동은 미해결 제한으로 남아 있으며, 추가 조사는 종료했다. `FeedAndConfirmPosition`은 기록된 거리만큼 `^B`로 복귀하거나 새 `^Q`를 전송하지 않고 `~S,FEED`만 보낸다. FEED의 정상 응답 뒤 기록 0은 실제 정렬을 센서로 확인한 결과가 아니므로 전원 재투입 복구 성공으로 해석하지 않는다. 상세 관측은 [운영 안내](PRINT_PRESENTATION.ko.md#전원-재투입-후-알려진-제한)를 따른다.

## 예외와 사용 종료

입력 오류는 `ArgumentException`으로 전달하며 메시지는 InvalidFile, InvalidForm, InvalidVariables 등 고정 식별 코드다. 원문 서식·변수·파일 경로와 파일 처리의 원래 예외·InnerException을 포함하지 않는다. 코드 페이지가 949가 아니면 `NotSupportedException`의 UnsupportedAnsiCodePage로 거부한다.

SDK 호출 중 DLL 누락·형식/비트수 불일치·함수 누락은 IsSuccess=false와 해당 SDK 함수 이름·고정 한글 설명으로 전달한다. 원래 예외 메시지를 결과에 복사하지 않는다. 인쇄 호출 이후 예외도 false이며 PrintInvoked/PrintCode에 호출·응답 정보를 보존한다. 마지막 서식 정리 오류는 앞서 설명한 규칙으로 보존한다. 실패한 연결 뒤 추가 해제도 실패하면 원래 연결 NativeCode와 설명을 유지하고 Message에 해제 오류를 추가한다. 입력 오류·미지원 코드 페이지·해제된 객체 사용과 Dispose 실패 등의 예외까지 false로 반환하는 계약은 아니다.

`Dispose`는 진행 중인 SDK 호출과 `finally`가 끝날 때까지 기다린다. 해제를 확인하지 못하면 `InvalidOperationException("DisconnectFailed")`을 던지고 객체를 종료 상태로 바꾸지 않는다. `using` 종료에서도 이 예외가 발생할 수 있으므로 호출 프로그램에서 처리하고 명시적 해제를 재시도할 수 있다. 정상 종료된 객체의 `Dispose`는 반복 호출해도 작업하지 않으며, 다른 공개 함수는 `ObjectDisposedException`을 던진다.

## 검증과 배포 범위

관리 DLL은 .NET Framework 4.6.1/AnyCPU다. 호출 프로그램의 비트수와 맞는 `BlackfishNfcLib.dll`, `HFModule.dll`을 실행 폴더에 배포한다. 배포 파일의 고정 해시는 verify-build에서 검사하고 제품 DLL은 실행 시 해시를 검사하지 않는다.

관리 harness는 제품 두 소스를 연결하여 SDK 대체 호출로 순서·실패·잠금 수명을 검사한다. 실제 제품 DLL의 계약 검사는 공개 API와 선언을 확인하며 SDK를 로드하지 않는다. [오프라인 검증 프로그램](../tests/OfflineSmoke/Program.cs)도 실제 프린터 연결과 인쇄를 실행하지 않는다. 문서의 사용 예제는 SDK를 실행하는 코드이므로 자동 검사 대상으로 실행하지 않는다.

제조사 StringBuilder 출력 계약을 사용하며 SDK 자체 출력 버퍼 결함 방어는 범위에서 제외한다. 관리 검사 통과는 실제 인쇄 성공을 의미하지 않는다. 사용자 승인 실기에는 공개 DLL의 x86·x64 파일/문자열 인쇄에서 ASCII·한글 변수와 위치·배출 정상 관측이 있다. 다른 용지·펌웨어·서식 전체의 성공을 보장하는 결과는 아니다. FEED·미리보기 관측과 한계는 [운영 기준](PRINT_PRESENTATION.ko.md#운영-기준)을 따른다. 이 API 안내나 관리 검사만으로 다른 환경의 장치 성공을 판정하지 않는다.

## 구현과 책임의 경계

기본 제품 코드는 `Bt003Printer.cs`와 `PrinterModels.cs` 두 파일이며, 제조사 Wrapper의 원본 39개 선언을 직접 사용한다. 기본 Print/PrintFile은 선택 Templates의 해석·검사기를 실행하지 않는다. BTPL의 명령·좌표·변수 번호 판정은 SDK 또는 호출 프로그램 책임이다. 자체 로더·함수 검색·worker·강제 취소·시간 초과 성공 변환·일괄 API를 추가하지 않는다.

`Bt003Printer`는 IDisposable인 sealed 클래스다. 모든 SDK 호출과 finally·해제는 정적 Gate 안에서 직렬화하며 연결 소유자 하나만 SDK 전역 상태를 사용한다. 연결 시도 이후에는 DeviceDisconnect와 CheckPrtConnection의 Closed 확인까지 소유권을 유지한다. 연결 실패 뒤 정리도 실패하면 원래 응답을 보존하고 해제 오류를 추가한다. 다른 객체나 새 연결로 미정리 상태를 우회하지 않는다. 이 보장은 프로세스 내부이며 교차 프로세스 제어는 제공하지 않는다.

원본 DllImport의 ApplicationDirectory/System32 검색 제한을 유지하며 실행 시 고정 해시·기존 모듈 충돌 검사는 하지 않는다. 파일의 부모 디렉터리 잠금·Win32 최종 경로 검사는 제공하지 않는다. 원래 Path/FileStream/디코딩 예외나 InnerException을 외부로 복사하지 않는다. 공개 결과 속성의 getter는 public, setter와 생성자는 internal이며 새 결과는 false에서 정상 경로만 true로 설정한다.

`^M`과 `^B`는 SDK 진입 전에 위치를 불명으로 저장한다. 0 반환에서만 명령 이력 거리로 갱신하며 비0·예외·저장 실패에서는 다음 자동 인쇄를 차단한다. 인쇄 준비·응답·정리 실패를 자동 재시도로 복구하지 않는다. SDK 함수마다 반환 규약이 다르므로 DeviceDisconnect의 int나 void 함수의 반환을 임의로 일반적인 0 성공으로 해석하지 않는다. 제품의 정상·Closed 확인과 고정 실패 처리는 이 문서의 흐름을 따른다.
