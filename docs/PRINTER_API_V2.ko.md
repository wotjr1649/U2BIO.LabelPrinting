# DLL API 개발자 참조서 V2

현재 `U2BIO.BT003PROLabelPrinting.dll`을 호출하는 개발자용 안내다. **V2는 문서 개정판이며 DLL 버전이나 공개 API 변경을 뜻하지 않는다.** 기준은 [Bt003Printer.cs](../src/U2BIO.BT003PROLabelPrinting/Bt003Printer.cs)와 [PrinterModels.cs](../src/U2BIO.BT003PROLabelPrinting/PrinterModels.cs)다. [V1 안내](PRINTER_API.ko.md)는 보존판으로 유지한다. 동작 설명이 다르면 현재 구현과 이 문서의 제한 사항을 확인한다.

- [실행 조건](#실행-조건): 필요한 파일·환경·책임 범위
- [빠른 시작](#빠른-시작): 연결·한 장 인쇄·결과 확인·정리
- [API 목록](#api-목록): 연결 소유권·이동·기록 변경 비교
- [함수별 상세](#함수별-상세): 선언·파라미터·조건·반환·예외·예제
- [PrinterResult](#printerresult): 다섯 속성과 상황별 결과
- [입력과 여러 장 사용](#입력과-여러-장-사용): 문자열·파일·변수·UI 호출
- [오류와 복구](#오류와-복구): 예외·연결 오류·위치 불명
- [위치 기록과 알려진 제한](#위치-기록과-알려진-제한): 저장·재실행·전원 재투입
- [제조사 API와의 대응](#제조사-api와의-대응): SDK와 facade의 차이

## 실행 조건

namespace는 `U2BIO.BT003PROLabelPrinting`이다. Windows·.NET Framework 4.6.1 대상이며 `Encoding.Default.CodePage == 949` 환경을 사용한다. 관리 DLL은 AnyCPU지만 `BlackfishNfcLib.dll`·`HFModule.dll`은 실행 프로세스와 같은 x86 또는 x64 한 쌍이어야 한다. 세 파일을 호출 프로그램 실행 폴더에 배포한다. 설치·비트수·재빌드 상세는 [배포 안내](SDK_DEPLOYMENT.ko.md)를 따른다.

`Bt003Printer`는 매개변수 없는 생성자로 만든다. 생성 자체가 연결·인쇄를 시작하지 않는다. 한 프로세스에서 하나의 객체만 SDK 연결을 소유하며 같은 물리 프린터는 한 프로그램에서만 사용한다. SDK 호출·정리는 직렬화된다. 모든 함수는 동기식이며 UI 백그라운드 실행은 호출 프로그램이 맡는다.

DLL은 USB 연결·한 장 인쇄·추가 배출·정리와 결과를 관리한다. BTPL 명령·문자 그리기·변수 치환은 제조사 SDK가 담당한다. DB 조회·업무값 매핑·장수 반복·중단 정책은 호출 프로그램이 맡는다. TXT 편집·인쇄 전 진단이 필요하면 [선택 TXT API](BTPL_TEMPLATE_API.ko.md)를 사용한다. `Print`·`PrintFile`이 해당 진단을 자동 실행하지는 않는다.

## 빠른 시작

아래 예제는 **운영자가 실제 용지 정렬을 확인했고 위치 기록도 그 상태와 일치하는 경우**에만 인쇄한다. 연결 성공만으로 이 전제를 만족하지 않는다. 최초 위치 불명·용지 교체·전원 재투입은 [오류와 복구](#오류와-복구)를 먼저 확인한다. 모든 호출 예제는 실행하면 실제 장치 동작을 요청하므로 문서 검사에서 자동 실행하지 않는다.

호출 프로그램이 보관한 `Bt003Printer`를 인수로 받으며 이 예제는 끝에 Dispose를 시도한다. 정리 실패 시 같은 객체를 버리지 않고 호출 프로그램에서 원인을 확인한 뒤 명시적으로 해제를 재시도할 수 있다. 인쇄 결과와 정리 실패를 각각 출력하여 어느 한쪽도 숨기지 않는다.

```csharp
using System;
using U2BIO.BT003PROLabelPrinting;

public static class LabelPrintExample
{
    public static void PrintOne(Bt003Printer printer, string port,
        int presentationDistanceMm, string formPath, string variableData = null)
    {
        try
        {
            PrinterResult connected = printer.ConnectUsb(port, presentationDistanceMm);
            Console.WriteLine(connected.Message);
            if (!connected.IsSuccess) return;

            PrinterResult result = printer.PrintFile(formPath, variableData);
            Console.WriteLine(result.Message);
            Console.WriteLine("SDK 정상: " + result.IsSuccess
                + ", 인쇄 호출: " + result.PrintInvoked
                + ", 인쇄 응답: " + result.PrintCode);
            // 실패·불명에서도 일부 출력됐을 수 있으므로 자동 재인쇄하지 않는다.
        }
        catch (ArgumentException error)
        {
            Console.Error.WriteLine("입력을 확인하세요: " + error.Message);
        }
        catch (NotSupportedException error)
        {
            Console.Error.WriteLine("실행 환경을 확인하세요: " + error.Message);
        }
        finally
        {
            try { printer.Dispose(); }
            catch (InvalidOperationException error)
            {
                Console.Error.WriteLine("연결 정리 실패: " + error.Message);
                // 호출 프로그램은 보관한 같은 printer로 명시적 복구를 판단한다.
            }
        }
    }
}
```

호출자는 `new Bt003Printer()`로 만든 객체·포트·해당 용지의 배출 거리·CP949 서식 경로를 전달한다. 변수 없는 서식은 `variableData`를 생략한다. 변수 서식은 필요한 모든 값을 raw `!!` 문자열로 전달한다. `using`도 가능하지만 종료 시 `Dispose()` 예외가 발생할 수 있으므로 주 작업 결과와 정리 오류를 모두 보존한다.

## API 목록

여기서 **소유권 필요**는 해당 객체가 SDK 연결을 소유해야 한다는 뜻이다. 실제 장치 준비·정렬을 보장하는 조건은 아니다. 정상 제품의 공개 메서드는 다음 9개다.

| 메서드 | 소유권 필요 | 용지 이동 | 위치 기록 변경 |
|---|---|---|---|
| [ConnectUsb](#connectusb) | 기존 소유자 없어야 함 | 별도 이동 명령 없음 | 첫 연결에서 기존 기록 읽기 |
| [GetConnectionStatus](#getconnectionstatus) | 필요 | 없음 | 없음 |
| [SetPrintingOptions](#setprintingoptions) | 필요·연결 오류 상태 차단 | 없음 | 없음 |
| [Print](#print) | 필요·연결 오류/위치 불명 차단 | 이전 배출 복귀 → 한 장 인쇄 → 선택 추가 배출 | 있음 |
| [PrintFile](#printfile) | Print와 같음 | Print와 같음 | 있음 |
| [ConfirmPrintPosition](#confirmprintposition) | 필요 | 없음 | 수동 정렬 확인을 0으로 저장 |
| [FeedAndConfirmPosition](#feedandconfirmposition) | 필요·연결 오류 상태 차단 | `~S,FEED` 전송 | 정상 응답·저장 성공 시 0 |
| [Disconnect](#disconnect) | 없어도 정상 반환 | 별도 이동 명령 없음 | 기존 기록 유지 |
| [Dispose](#dispose) | 없어도 정리 가능 | 별도 이동 명령 없음 | 기존 기록 유지 |

## 함수별 상세

이후 짧은 예제의 `printer`는 호출 프로그램이 관리하는 객체다. 인쇄 예제는 연결·실제 정렬·위치 기록 확인이 끝났고 `btpl`·`formPath`가 사용하는 용지에 맞는 서식이라는 전제다. 예제의 결과는 반드시 `IsSuccess`와 `Message`로 확인한다. 결과 코드만으로 성공을 판정하거나 실패 시 자동 재시도하지 않는다.

### ConnectUsb

```csharp
public PrinterResult ConnectUsb(string port, int presentationDistanceMm = 0)
```

USB 포트 지정 → SDK 연결 → Open 확인을 수행한다.

| 파라미터 | 자료형 | 필수·기본값 | 의미·허용 범위 |
|---|---|---|---|
| `port` | `string` | 필수 | SDK의 USB 포트 식별 문자열. null·빈 값·공백만·제어문자 금지. CP949 공통 조건 적용 |
| `presentationDistanceMm` | `int` | 기본 `0` | 정상 인쇄 응답 뒤 추가 배출할 거리(mm). `0..1000`. 0은 추가 배출 없음 |

**조건·동작:** 다른 객체 또는 이 객체가 이미 연결을 소유하면 실패하며 SDK를 호출하지 않는다. 연결 자체는 B/M/FEED를 보내지 않는다. 같은 프로세스의 첫 포트 연결에서 위치 파일을 읽으며 없거나 손상됐거나 포트가 다르면 위치 불명이다. 유효한 기록이 없어도 연결은 성공할 수 있지만 인쇄는 별도로 차단된다. 포트·배출 거리 변경은 해제 후 다시 연결한다.

**반환:** 정상 연결 확인은 `IsSuccess=true`, `NativeCode=1`. `PrintInvoked=false`, `PrintCode=null`. 연결 중 실패 후 부분 연결을 정리하며, 정리도 실패하면 원래 실패와 정리 설명을 보존한다. 원인이 해결되기 전 새 객체로 우회하지 않는다.

**예외:** 잘못된 포트는 `ArgumentException("InvalidPort")`, 잘못된 거리는 `ArgumentException("InvalidPresentationDistance")`. ANSI 949 미지원은 `NotSupportedException`, Dispose 완료 객체는 `ObjectDisposedException`. 검사 순서에 따라 입력 오류가 먼저 발생할 수 있다.

```csharp
PrinterResult result = printer.ConnectUsb(selectedUsbPort, presentationDistanceMm);
```

포트 0은 기존 한 대의 실기 확인값이다. 제조사 PDF의 `USB1` 예제를 모든 환경의 포트값으로 보장하지 않는다. DLL에는 USB 목록 감지 공개 API가 없다. 15 mm도 운영 용지의 확인값이며 DLL 기본값은 0이다.

### GetConnectionStatus

```csharp
public PrinterResult GetConnectionStatus()
```

**파라미터:** 없음. 소유 객체만 `CheckPrtConnection`을 호출한다. 소유자가 아니면 SDK 조회 없이 실패한다.

**반환:** Open이면 `IsSuccess=true`, `NativeCode=1`. Closed 응답은 실패·`NativeCode=0`. SDK 진입 전 거부 또는 응답 전 예외에서는 `NativeCode=null`일 수 있다. 인쇄 호출은 하지 않는다.

**한계·예외:** 포트 연결만 확인하며 용지·리본·헤드 상태, 물리 출력 완료, 위치 정렬을 확인하지 않는다. Open 응답이어도 기존 연결 오류 상태를 해제하지 않는다. Dispose 완료 후에는 `ObjectDisposedException`.

```csharp
PrinterResult result = printer.GetConnectionStatus();
```

### SetPrintingOptions

```csharp
public PrinterResult SetPrintingOptions(ushort speed, ushort darkness)
```

| 파라미터 | 자료형 | 필수·기본값 | 의미·허용 범위 |
|---|---|---|---|
| `speed` | `ushort` | 필수 | SDK 인쇄 속도 값. DLL은 `1..65535`만 허용하며 실제 지원 속도는 프린터에 따름 |
| `darkness` | `ushort` | 필수 | 인쇄 농도 `1..19`. 클수록 진함 |

**조건·동작:** 연결 소유권이 필요하고 연결 오류 상태에서는 거부한다. 정상 SDK 응답을 받은 옵션만 보관하여 이후 인쇄에 재적용한다. 서식의 `^S`·`^H`가 있으면 제조사 SDK의 서식 설정이 우선한다. 용지를 이동하지 않는다.

**반환·예외:** 정상 설정은 `IsSuccess=true`, `NativeCode=0`. SDK 오류·연결 미소유·연결 복구 필요는 실패 결과다. 속도 0 또는 농도 범위 밖은 `ArgumentException("InvalidPrintingOptions")`; Dispose 완료 후에는 `ObjectDisposedException`.

```csharp
PrinterResult result = printer.SetPrintingOptions(4, 12);
```

예제 속도 4의 장치 지원 여부는 해당 프린터에서 확인한다. Tester 화면의 선택 범위를 DLL 자체 제한으로 해석하지 않는다.

### Print

```csharp
public PrinterResult Print(string btpl, string variableData = null)
```

| 파라미터 | 자료형 | 필수·기본값 | 의미·허용 범위 |
|---|---|---|---|
| `btpl` | `string` | 필수 | CP949로 표현 가능한 BTPL 원문. null·빈 값·공백만·NUL 금지. 원문과 CRLF 정규화 결과 각각 4 MiB 이내 |
| `variableData` | `string` | 기본 `null` | raw `!!` 구분 문자열. null은 변수 등록 생략. 빈 전체 문자열·공백만·제어문자 금지, CP949 4 MiB 이내 |

**조건·동작:** 연결 소유권과 신뢰할 위치 기록이 필요하다. 연결 복구 필요·위치 불명이면 인쇄하지 않는다. 직전 이동 후 최소 간격을 기다리고 이전 배출이 양수이면 B로 복귀한다. `ResetForm` → 정상 옵션 재적용 → 서식·선택 변수 등록 → `StartLabelPrint(1, ...)` → 정상 응답 시 선택 M 배출 → `finally` 서식 정리 순서다. 준비 단계 실패 뒤에는 P를 호출하지 않는다.

**반환:** 인쇄·이동·정리를 합친 `PrinterResult`. 인쇄 응답은 `PrintCode`에 보존하고 후속 배출·정리 실패는 `IsSuccess=false`로 구분한다. [정상 응답 후 위치 저장 실패의 한계](#위치-기록과-알려진-제한)는 별도로 확인한다.

**예외:** 서식은 `ArgumentException("InvalidForm")`, 변수는 `ArgumentException("InvalidVariables")`; ANSI 949 미지원은 `NotSupportedException`; Dispose 완료 후에는 `ObjectDisposedException`.

```csharp
PrinterResult fixedResult = printer.Print(btplWithoutVariables);
// 아래는 별도 사용 예다. V00·V01을 사용하는 서식에 필요한 값을 매번 제공한다.
PrinterResult variableResult = printer.Print(btplWithVariables, "ITEM001!!LOT001");
```

각 예는 별도 요청이다. 첫 요청이 실패해도 다음 요청을 계속 실행하는 코드로 묶지 않는다. 기본 Print는 AW 좌표·변수 번호·라벨 방식을 검증하거나 서식에 Q/W를 추가하지 않는다.

### PrintFile

```csharp
public PrinterResult PrintFile(string path, string variableData = null)
```

| 파라미터 | 자료형 | 필수·기본값 | 의미·허용 범위 |
|---|---|---|---|
| `path` | `string` | 필수 | 읽을 수 있는 CP949 서식 파일. 상대경로는 현재 작업 디렉터리 기준. 경로도 CP949 공통 조건 적용 |
| `variableData` | `string` | 기본 `null` | Print와 같은 raw 변수 규칙 |

**조건·동작:** Print의 연결·위치 조건과 같다. 빈 파일·4 MiB 초과·잘못된 CP949를 거부하고 줄끝 정규화 후 크기도 다시 확인한다. `FileShare.Read`로 연 동일 스트림의 내용을 `SetPrintFormString`에 등록한다. SDK에 파일 경로를 넘겨 다시 읽게 하지 않는다. 쓰기·삭제 공유를 허용하지 않으며 잠금은 SDK 인쇄와 마지막 서식 정리까지 유지한다.

**반환·예외:** Print와 같은 결과다. 파일 경로·내용·읽기 오류는 `ArgumentException("InvalidFile")`로 전달하며 실제 경로·원래 예외를 메시지에 넣지 않는다. 변수 오류·ANSI 환경·Dispose 예외는 Print와 같다. 실패 후 다른 등록 방식으로 자동 재인쇄하지 않는다.

```csharp
PrinterResult result = printer.PrintFile(formPath, rawVariableData);
```

이 함수는 잠금 안에서 읽은 내용을 인쇄한다. 호출 프로그램이 그 전에 미리보기·승인을 받은 내용과 같은지까지 자동 비교하지는 않는다. 필요하면 호출자가 승인한 스냅샷을 유지하고 동일성 확인과 잠금 수명을 관리한다.

### ConfirmPrintPosition

```csharp
public PrinterResult ConfirmPrintPosition()
```

**파라미터:** 없음.

**조건·동작:** 운영자가 실제 용지를 인쇄 기준 위치로 수동 정렬한 뒤 호출한다. 연결 소유권은 확인하지만 SDK 연결·센서 상태를 조회하거나 용지를 움직이지 않는다. 위치 메모리와 파일을 0으로 기록한다. 양수였던 배출 기록도 지우므로 실제 정렬 없이 호출하면 다음 B 복귀가 생략될 수 있다.

**반환·예외:** 기록 성공은 `IsSuccess=true`, `NativeCode=null`, `PrintInvoked=false`, `PrintCode=null`. 미소유·저장 실패는 false. 연결 오류 상태를 해제하지 않으며 Dispose 완료 후에는 `ObjectDisposedException`.

```csharp
// 운영자가 실제 정렬을 확인한 시점에만 실행한다.
PrinterResult result = printer.ConfirmPrintPosition();
```

인쇄 거부를 무시하기 위한 자동 초기화로 사용하지 않는다. Tester에는 이 직접 호출 메뉴가 없다.

### FeedAndConfirmPosition

```csharp
public PrinterResult FeedAndConfirmPosition()
```

**파라미터:** 없음.

**조건·동작:** 연결 소유권이 필요하고 연결 오류 상태이면 거부한다. 프린터 대기 화면에서 사용한다. 연결 확인·최소 간격 대기 → 위치 불명 기록 → `SendData("~S,FEED\r\n")` → 정상 응답과 기록 저장 성공 시 0 확인 순서다. 이전 위치가 불명·양수여도 B 복귀 없이 FEED만 보내며 M·새 Q도 보내지 않는다. 센서·세로·가로 보정값을 별도로 전송하지 않는다.

**반환·예외:** `IsSuccess=true`는 FEED SDK 정상 응답과 기록 저장 성공이다. 실제 이송·정렬 완료의 센서 확인이 아니다. 정상 경로의 `NativeCode=0`, `PrintInvoked=false`, `PrintCode=null`. FEED 응답 비0·예외·이후 저장 실패는 false와 위치 불명이다. 진입 전 거부 시 FEED를 보내지 않으며 NativeCode는 마지막 연결 조회 값 또는 null일 수 있다. Dispose 완료 후에는 `ObjectDisposedException`.

```csharp
PrinterResult result = printer.FeedAndConfirmPosition();
```

Q 변경 뒤 새 서식을 적용하는 운영 절차와 실제 정렬은 [용지 운영](PRINT_PRESENTATION.ko.md#공통-복구)을 확인한다. 전원 재투입 뒤에는 FEED 안쪽 이동이 보고됐으며 이 함수를 검증된 전원 재투입 해결책으로 보장하지 않는다. 자동 반복 호출하지 않는다.

### Disconnect

```csharp
public PrinterResult Disconnect()
```

**파라미터:** 없음.

**동작·반환:** 소유 객체는 `DeviceDisconnect` 후 `CheckPrtConnection`의 Closed를 확인한다. 둘 다 정상이면 `IsSuccess=true`, 마지막 확인의 `NativeCode=0`. 소유자가 아니면 SDK 호출 없이 true·NativeCode=null을 반환한다. 실패 시 소유권과 연결 오류 상태를 유지하므로 원인을 확인하고 같은 객체로 명시적 재시도를 판단한다. 별도 B/M/FEED 없이 위치 기록을 보존한다.

**예외:** Dispose 완료 후에는 `ObjectDisposedException`. Disconnect 성공은 객체 Dispose와 다르므로 객체를 다시 연결해 사용할 수 있다.

```csharp
PrinterResult result = printer.Disconnect();
```

### Dispose

```csharp
public void Dispose()
```

**파라미터·반환:** 없음. 반환형은 `void`이며 `PrinterResult`가 아니다.

**동작:** 다른 스레드의 진행 중인 SDK 호출과 정리가 끝나기를 기다린 뒤 연결 해제를 시도한다. 강제 취소나 제한 시간 종료 API가 아니다. 성공하면 객체 사용을 종료하며 반복 Dispose는 아무 작업도 하지 않는다. 별도 용지 이동을 요청하지 않는다.

**예외·복구:** 해제·Closed 확인이 실패하면 `InvalidOperationException("DisconnectFailed")`. 이때 객체를 Dispose 완료로 만들지 않으므로 같은 객체를 보관하고 원인을 확인한 뒤 Disconnect·Dispose를 명시적으로 재시도할 수 있다. 성공적으로 Dispose된 객체의 다른 공개 메서드는 재사용하지 않는다.

```csharp
try { printer.Dispose(); }
catch (InvalidOperationException error)
{
    Console.Error.WriteLine(error.Message);
    // printer를 보관하고 연결 정리 실패로 처리한다. 자동 인쇄 재시도는 하지 않는다.
}
```

## PrinterResult

생성자는 internal이며 호출자는 DLL이 반환한 객체를 읽는다. 다섯 속성은 public getter/internal setter다. 결과가 없다는 뜻의 nullable 코드와 오류 코드 0을 혼동하지 않는다.

| 속성 | 자료형 | 의미 |
|---|---|---|
| `IsSuccess` | `bool` | 요청한 SDK 처리·필요한 이동·정리의 정상 여부. false는 실패 또는 결과 불명이며 무출력을 뜻하지 않음. 후속 위치 저장 한계는 아래 참조 |
| `Message` | `string` | 상태·실패 함수·이동/정리 오류를 사람이 읽는 한글 설명. 문장을 파싱해 제어하지 않음 |
| `NativeCode` | `int?` | 주 작업의 마지막 SDK 응답. SDK 미호출·응답 전 예외는 null일 수 있음. 정리 결과는 원래 값을 덮지 않음 |
| `PrintInvoked` | `bool` | StartLabelPrint 진입 직전에 true 기록. 실제 인쇄 완료 여부가 아님 |
| `PrintCode` | `int?` | 해석한 인쇄 응답. 미호출·응답 전 예외·해석 불가는 null. 후속 배출·정리 실패에도 인쇄 응답을 보존 |

일반 호출부는 `IsSuccess`와 `Message`로 처리한다. 숫자 0/1만으로 성공을 다시 정의하지 않는다. 연결 확인의 1은 Open이며 다른 함수의 오류 코드 1과 같은 의미가 아니다. `Status`, `Function`, `MovementCode`, `CleanupCode` 속성이나 암시적 bool 변환은 없다.

| 상황 | 결과 해석 |
|---|---|
| 정상 한 장 인쇄·필요한 배출·정리 | true, PrintInvoked=true, PrintCode=0. 물리 완료 확인은 아님 |
| 위치 불명·연결 미소유로 인쇄 거부 | false, PrintInvoked=false, PrintCode=null. SDK 인쇄 미호출 |
| 서식 등록 실패 | false, PrintInvoked=false. 실패 응답은 NativeCode, PrintCode=null |
| 인쇄 0 뒤 배출 응답 7 | false, NativeCode=7, PrintInvoked=true, PrintCode=0 |
| 인쇄 0 뒤 배출 응답 전 예외 | false, NativeCode=null, PrintCode=0 |
| 인쇄 0 뒤 ResetForm 정리 실패 | false, PrintCode=0과 원래 NativeCode 보존, Message에 정리 실패 추가 |
| 인쇄 101·미확인 코드·해석 불가 | false, 인쇄 시도·응답 정보 보존. 일부 출력 가능성이 있어 자동 재인쇄 금지 |
| FEED 0 뒤 위치 파일 저장 실패 | false, NativeCode=0, PrintInvoked=false, PrintCode=null. 위치는 불명 |
| 정상 P 또는 B/M 뒤 최종 위치 저장만 실패 | 현재 구현은 false로 바꾸지 않아 true가 남을 수 있음. 아래 저장 단계별 한계 참조 |

## 입력과 여러 장 사용

### 공통 입력 조건

포트·파일 경로·서식·변수 문자열은 엄격한 CP949 왕복이 가능해야 한다. null 허용은 변수 등록 생략에만 적용한다. 서식·변수·문자열 검사 대상은 각각 4 MiB(`4 * 1024 * 1024` bytes) 이내다. PrintFile의 파일 크기도 4 MiB 이내이며 CRLF 정규화 후 다시 검사한다. UTF-8 파일을 CP949 파일로 간주해 보내지 않는다.

서식의 CR/LF는 CRLF로 통일하고 마지막 CRLF를 붙인다. 변수 문자열은 줄끝·공백을 정규화하지 않는다. BTPL 전체 명령의 유효성 검사는 SDK 또는 호출자가 맡는다.

### raw 가변데이터

```csharp
PrinterResult result = printer.PrintFile(formPath, "ITEM001!!LOT001!!20261021");
```

첫 항목은 `^V00`, 다음은 `^V01` 순서다. 요소 등장 순서가 아니라 변수 번호로 매핑한다. 최고 참조 번호가 V02이면 V00부터 세 값을 요청마다 제공한다. DLL은 split/join/trim하지 않으며 `null`이 이전 변수를 지운다는 보장도 없다. 변수 없는 서식에만 생략·null을 사용한다. 빈 전체 문자열과 공백만인 값은 `InvalidVariables`다.

앞뒤·연속 `!!`, 값 사이 공백·단일 `!`는 기본 검사에서 재구성하지 않는다. 다만 빈 항목·부족 데이터의 SDK 결과는 보장하지 않는다. 값 자체에 `!!`가 들어갈 때의 이스케이프는 제공하지 않는다. DB NULL 처리·필요 항목 확인·구분자 충돌 검사는 호출 프로그램이 담당한다. 화면용 변수 치환 결과 대신 변수 표식이 남은 원문과 raw 데이터를 각각 전달한다.

### 순차 여러 장과 UI

`Print`·`PrintFile` 한 번은 한 장 요청이다. copies·일괄·강제 취소 API는 제공하지 않는다. 다음 예제는 각각 독립 요청이며 첫 false에서 중단한다. 예외가 발생해도 루프를 벗어나 후속 요청을 중단하고 자동 재인쇄하지 않는다.

```csharp
for (int i = 0; i < requestedCount; i++)
{
    PrinterResult result = printer.PrintFile(formPath, rawVariableData);
    Console.WriteLine(result.Message);
    if (!result.IsSuccess) break;
}
```

예외는 호출자가 [오류와 복구](#오류와-복구)에 따라 처리한다. 반환된 SDK 정상 수를 실제 출력 완료 장수로 표시하지 않는다. DLL은 직전 SDK 이동 시각부터 2초가 지나지 않았으면 남은 간격을 기다린다. 이 간격은 장치 완료 응답을 측정하는 기능이 아니다.

UI는 한 작업을 백그라운드에서 실행하고 끝날 때까지 중복 인쇄·편집·설정·닫기 충돌을 관리한다. 반복 중단은 현재 동기 호출 반환 후 다음 요청부터 적용한다. 병렬로 요청을 쌓거나 Task 취소만으로 장치 인쇄가 취소됐다고 표시하지 않는다. 같은 승인 원문을 여러 장에 쓰려면 요청 사이 파일 변경 확인도 호출자가 담당한다.

## 오류와 복구

| 전달 방식 | 상황 | 호출자 처리 |
|---|---|---|
| `ArgumentException` | InvalidPort·InvalidPresentationDistance·InvalidPrintingOptions·InvalidForm·InvalidFile·InvalidVariables | 입력·파일을 고친 뒤 새 요청 여부 판단. SDK 정상 결과로 변환하지 않음 |
| `NotSupportedException` | UnsupportedAnsiCodePage | 실행 환경의 ANSI 949 조건 확인 |
| `ObjectDisposedException` | Dispose 완료 객체 사용 | 객체 수명 오류 수정. 연결 소유권이 해제된 경우 새 객체 사용 |
| `InvalidOperationException` | Dispose의 DisconnectFailed | 원래 인쇄 결과와 별도로 정리 실패 기록. 같은 객체로 명시적 해제 재시도 판단 |
| `PrinterResult.IsSuccess=false` | SDK 오류·결과 불명·연결/소유권/위치 거부 | Message와 실제 출력 확인. 무출력으로 단정하거나 자동 재인쇄하지 않음 |

복구 가능한 SDK 로드·비트수·함수 누락 등은 해당 SDK 함수 이름과 함께 실패 결과로 전달된다. 입력 예외와 Dispose 예외까지 모두 결과 객체로 변환하는 계약은 아니다. 제조사 오류 번호는 호출 단계와 함께 읽으며 PDF 코드표를 공통 bool 판정식으로 사용하지 않는다.

**연결 오류와 위치 불명은 별개다.** 연결·해제·서식 정리 오류 뒤에는 Disconnect의 Closed를 확인한 후 다시 연결한다. GetConnectionStatus가 Open을 반환하거나 ConfirmPrintPosition을 호출해도 연결 오류 상태는 해제되지 않는다. 위치 불명은 실제 정렬을 확인하고 ConfirmPrintPosition 또는 명시적 FeedAndConfirmPosition 절차를 판단한다. 연결이 복구되지 않았으면 FEED도 차단된다.

Tester의 [FEED+확인], 용지 교체와 Q 변경 절차는 [공통 운영 안내](PRINT_PRESENTATION.ko.md#공통-복구)를 따른다. FEED 자체도 이동 요청이며 SDK 정상 응답은 실제 정렬 완료 보장이 아니다.

## 위치 기록과 알려진 제한

기존 정상 배출이 15 mm이고 새 연결 배출 설정이 7 mm이면 다음 요청은 `B15 → P → M7`이다. 새 배출 0이면 이전 거리는 복귀하고 새 M은 보내지 않는다. 연결 해제·Dispose는 추가 이동을 요청하지 않으며 재연결·앱 재시작도 기록을 임의 초기화하지 않는다.

기록은 메모리와 위치 파일에 저장하는 **명령 이력**이다. 파일 위치·형식은 [보존 V1의 위치 기록 설명](PRINTER_API.ko.md#내부-상태와-복구)을 참고하되, 저장 실패의 현재 동작은 아래 구분을 따른다. Tester와 호출 프로그램이 파일을 직접 수정하지 않는다.

| 저장 단계 | 현재 구현의 처리 |
|---|---|
| B/M/P 또는 FEED 진입 전 불명 기록 저장 실패 | 해당 SDK 이동·인쇄 호출을 하지 않고 false 반환. 앞서 실행한 단계가 있으면 그 영향은 남음 |
| ConfirmPrintPosition의 0 저장 실패 | false 반환 |
| FEED 정상 응답 후 0 저장 실패 | false·위치 불명 반환 |
| 정상 P 뒤 0 저장 또는 정상 B/M 뒤 거리 저장만 실패 | 반환값을 확인하지 않아 IsSuccess=true가 유지될 수 있음. 메모리는 정상 응답 거리로 갱신됐어도 디스크 기록은 갱신되지 않을 수 있음 |

따라서 true는 위치 파일 영속화나 물리 정렬 성공의 보증이 아니다. 정상 응답 후 저장 실패를 언제나 결과에서 감지해 다음 요청을 차단한다고 약속하지 않는다. 실제 구현의 저장 경계는 `PrintCore`·`Move`·`FeedAndConfirmPosition`을 참고한다. 이는 V2에서 밝히는 현재 한계이며 제품 수정 완료를 뜻하지 않는다.

전원 재투입·수동 FEED·용지 교체·당김·다른 프로그램의 사용은 자동 감지하지 않는다. 프린터 내부 원점을 저장·복원하지 않으며 전원 재투입 뒤 첫 장 상단 이상과 FEED 동작 차이의 원인은 미확정이다. 전원 유지 시 전진만 하던 FEED가 전원 후 후진→전진→후진했다는 사용자 관측과 운영상 주의점은 [알려진 제한](PRINT_PRESENTATION.ko.md#전원-재투입-후-알려진-제한), 상세 비교는 [Issue #39](https://github.com/wotjr1649/U2BIO.LabelPrinting/issues/39)를 확인한다. 해당 추가 조사는 종료했고 Issue는 OPEN이다. FEED를 확정 해결책으로 안내하거나 기록을 강제로 0으로 만들지 않는다.

현재 제공된 SDK와 확인된 명령 계약 범위에서는 내부 위치 기준의 조회·저장·복원, FEED 정렬 경로 선택 및 실제 정렬 완료를 검증하는 수단이 확보되지 않아 전원 후 자동 위치 복원을 보장하지 않는다. DLL은 B/P/M 호출 순서·배출 거리 이력·실패 중단을 관리하지만, 그 기록이나 SDK 정상 응답은 실제 위치를 증명하지 않는다. 매뉴얼의 수신 함수 `ReadResponseMessage`도 현재 배포 SDK의 x86/x64 export에 없어 사용할 수 없으며, 수신 API의 부재만으로 이번 물리 동작의 원인을 설명하지는 않는다.

이는 현재 인터페이스로 보장할 수 있는 범위에 대한 판단이며 U2BIO DLL의 확인된 호출 오류, 제조사 SDK·펌웨어의 결함 또는 영구적인 해결 불가능을 확정한 것이 아니다. 기존 비교의 한 조건에서는 전원 유지·재투입의 전체 B/P/M USB 전송이 같았는데 물리 결과가 달랐다. 이 증거도 장치 내부 상태의 동일성이나 특정 원인을 증명하지 않는다. 기존 `B15 → P → M15`를 유지하고 근거 없는 거리·좌표 보정이나 자동 FEED를 추가하지 않았다.

센서 보정은 계산한 Q가 서식에 반영됐을 때 간접 적용하고, 세로·가로 보정은 미리보기 영역 계산용이다. 이 값들로 전원 후 원점을 복원하지 않는다. [보정값의 적용 범위](PRINT_PRESENTATION.ko.md#실측-보정과-미리보기의-한계)를 따른다.

## 제조사 API와의 대응

참고 자료는 Bitek의 *NFC BT003-M01-04 API Programming Manual Rev.1.3, 2022-05-20*이다. 아래 쪽수는 PDF 물리 페이지 기준이다. PDF는 제조사 SDK 전체를 다루므로 프로토타입·반환형·범위가 다르면 현재 DLL 소스의 공개 계약을 우선 확인한다.

| DLL 공개 함수 | 내부 제조사 호출·관계 | PDF 참고 |
|---|---|---|
| ConnectUsb | SetPrinterPort(USB) → DeviceConnect → CheckPrtConnection | 3·6쪽 |
| GetConnectionStatus | CheckPrtConnection | 6쪽 |
| SetPrintingOptions | SetPrintingOption. 정상 설정은 인쇄마다 재적용 | 6쪽 |
| Print / PrintFile | ResetForm·SetPrintFormString·선택 SetPrintFormVariableData·StartLabelPrint·선택 SendData | 서식 4·5쪽, StartLabelPrint 선언 24쪽 |
| ConfirmPrintPosition | SDK 호출 없이 위치 기록만 변경 | DLL 고유 계약 |
| FeedAndConfirmPosition | CheckPrtConnection·SendData의 FEED 문자열, 위치 기록 | SendData 14쪽 |
| Disconnect / Dispose | DeviceDisconnect → CheckPrtConnection | 4·6쪽 |

내부 Wrapper의 39개 선언은 그대로 사용하지만 Wrapper 클래스는 internal이며 직접 호출용 공개 facade가 아니다. 별도 SDK 직접 호출을 섞으면 DLL의 소유권·직렬화·위치 기록을 우회할 수 있다. TCP/IP·RS-232·RFID·일반 SendData·수신 버퍼 읽기는 이 DLL의 공개 인쇄 API가 아니다. 매뉴얼에 있는 함수가 모두 현재 배포 SDK에 존재한다고 가정하지 않는다.

PDF 4쪽의 DeviceDisconnect 반환 설명과 16쪽 RFID 선언 등에는 서로 맞지 않는 부분이 있다. PDF의 예제·오류 표를 그대로 복사하거나 RFID 반환 문자열 형식을 PrinterResult에 적용하지 않는다. 과거 배열 변수·Status 기반 호출 이행은 [V1 이행 안내](PRINTER_API.ko.md#제조사-방식의-가변데이터와-이행)를 참고하고 기존 앱은 현재 DLL 참조로 재빌드한다.

관리 검사·문서 예제 컴파일은 SDK 로드·USB 전송·물리 출력 성공을 증명하지 않는다. 실제 확인 범위는 [운영 안내](PRINT_PRESENTATION.ko.md#운영-기준), 빌드·검증 방법은 [개발 안내](DEVELOPMENT.md)를 따른다.
