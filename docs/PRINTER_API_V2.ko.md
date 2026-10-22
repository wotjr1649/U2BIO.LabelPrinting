# DLL API 개발자 참조서

현재 `U2BIO.BT003PROLabelPrinting.dll`을 호출하는 개발자용 안내다. 기본 연동에 필요한 실행 조건, 9개 메서드, 결과·예외 처리, 위치 기록과 기존 호출 이행을 이 문서에서 설명한다. 배포·용지 운영·선택 TXT 편집의 상세 링크는 심화 참고다.

- [실행 조건](#실행-조건): 필요한 파일·환경·책임 범위
- [빠른 시작](#빠른-시작): 연결·한 장 인쇄·결과 확인·정리
- [PrinterResult](#printerresult): 다섯 속성과 상황별 결과
- [API 목록](#api-목록): 연결 소유권·이동·기록 변경 비교
- [함수별 상세](#함수별-상세): 선언·파라미터·조건·반환·예외·예제
- [입력과 여러 장 사용](#입력과-여러-장-사용): 문자열·파일·변수·UI 호출
- [오류와 복구](#오류와-복구): 예외·연결 오류·위치 불명
- [위치 기록과 알려진 제한](#위치-기록과-알려진-제한): 저장·재실행·전원 재투입
- [기존 호출에서 이행](#기존-호출에서-이행): 변수 인수·결과 처리·호출 앱 재빌드
- [제조사 API와의 대응](#제조사-api와의-대응): 내부 호출·제조사 문서·전원 후 제한의 근거

## 실행 조건

namespace는 `U2BIO.BT003PROLabelPrinting`이다. Windows·.NET Framework 4.6.1 대상이며 `Encoding.Default.CodePage == 949` 환경을 사용한다. 관리 DLL은 AnyCPU지만 `BlackfishNfcLib.dll`·`HFModule.dll`은 실행 프로세스와 같은 x86 또는 x64 한 쌍이어야 한다. 세 파일을 호출 프로그램 실행 폴더에 배포한다. 설치·비트수·재빌드 상세는 [배포 안내](SDK_DEPLOYMENT.ko.md)를 따른다.

`Bt003Printer`는 매개변수 없는 생성자로 만든다. 생성 자체가 연결·인쇄를 시작하지 않는다. 한 프로세스에서는 연결에 성공한 객체 하나만 SDK를 사용한다. 이 문서의 **연결 소유권**은 그 객체가 연결을 관리한다는 뜻이다. 같은 물리 프린터는 한 프로그램에서만 사용한다.

모든 함수는 동기식이다. 호출이 반환될 때까지 해당 스레드는 기다리며, DLL은 SDK 호출과 정리가 겹치지 않도록 순서대로 처리한다. UI가 멈추지 않게 백그라운드에서 호출하고 중복 요청을 막는 일은 호출 프로그램이 맡는다.

DLL은 USB 연결·한 장 인쇄·추가 배출·정리와 결과를 관리한다. BTPL 명령·문자 그리기·변수 치환은 제조사 SDK가 담당한다. DB 조회·업무값 매핑·장수 반복·중단 정책은 호출 프로그램이 맡는다. TXT 편집·인쇄 전 진단이 필요하면 [선택 TXT API](BTPL_TEMPLATE_API.ko.md)를 사용한다. `Print`·`PrintFile`이 해당 진단을 자동 실행하지는 않는다.

## 빠른 시작

기본 순서는 **입력값 준비 → 객체 생성 → 연결 → 한 장 요청 → 결과·예외 확인 → 정리**다. 아래 예제는 변수 없는 CP949 서식 파일 하나를 사용하는 경우다.

### 입력값 준비

| 입력 | 준비 방법 |
|---|---|
| `selectedUsbPort` | 설치 환경에서 연결을 확인한 SDK USB 포트 문자열을 설정값으로 제공한다. DLL에는 장치 목록 조회 API가 없으며 Windows 포트명·장치 표시 이름을 그대로 넣지 않는다. |
| `presentationDistanceMm` | 인쇄 후 추가로 배출할 거리(mm). 해당 용지에서 확인한 `0..1000` 값이며 기본값 0은 추가 배출 없음이다. |
| `formPath` | 해당 프린터·용지에 맞게 준비한 CP949 BTPL 파일의 경로다. 예제용 경로에 파일이 자동 생성되지는 않는다. |
| `variableData` | 변수 없는 서식이면 생략한다. 변수 서식이면 V00부터 최고 참조 번호까지의 값을 raw `!!` 문자열로 준비한다. |

Tester는 Windows `usbprint` 장치 목록의 번호 문자열을 포트값으로 사용한다. 기존 실기 확인 범위는 프린터 한 대의 `"0"`이며, 여러 장치의 번호 대응은 미검증이다. 호출 앱은 확인한 값을 설정으로 받아야 하며 `"0"`을 모든 PC의 기본값으로 보장하지 않는다.

### 한 장 요청과 정리

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

다음은 위 함수의 호출부다. `"0"`·15 mm·파일 경로는 예시이며 설치 환경에서 확인한 값으로 바꾼다. 위치가 불명확할 때 자동 FEED나 자동 `ConfirmPrintPosition`을 끼워 넣지 않는다.

```csharp
Bt003Printer printer = new Bt003Printer();
string selectedUsbPort = "0";
int presentationDistanceMm = 15;
string formPath = System.IO.Path.Combine(
    AppDomain.CurrentDomain.BaseDirectory, "forms", "prepared-label.txt");

LabelPrintExample.PrintOne(printer, selectedUsbPort, presentationDistanceMm, formPath);
```

이 예제는 한 번의 연결·요청 뒤 Dispose를 시도한다. 정상 Dispose 뒤 같은 객체를 다시 연결하지 않는다. 여러 장은 [순차 여러 장과 UI](#순차-여러-장과-ui)처럼 한 연결 안에서 요청한 뒤 마지막에 정리한다. `using`도 가능하지만 Dispose 예외가 주 작업 예외를 가릴 수 있으므로 두 오류를 각각 보존해야 한다.

## PrinterResult

일반 호출부는 먼저 `IsSuccess`와 `Message`를 확인한다. false 또는 예외가 발생해도 일부 출력됐을 수 있으므로 자동 재인쇄하지 않는다. 인쇄 단계의 조사에는 `PrintInvoked`와 `PrintCode`를 함께 본다.

`NativeCode`와 `PrintCode`의 `int?`는 숫자가 없을 수 있다는 뜻이다. `null`은 응답값이 없으며 숫자 0과 다르다. 0과 1의 의미는 호출한 함수에 따라 다르므로 코드 숫자만으로 성공을 다시 정의하지 않는다.

호출자는 DLL이 반환한 객체를 읽는다. 다섯 속성은 외부에서 읽기만 가능하고, 생성자와 setter는 DLL 내부용이다.

| 속성 | 자료형 | 의미 |
|---|---|---|
| `IsSuccess` | `bool` | 요청한 SDK 처리·필요한 이동·정리의 정상 여부. false는 실패 또는 결과 불명이며 무출력을 뜻하지 않음. 후속 위치 저장 한계는 아래 참조 |
| `Message` | `string` | 상태·실패 함수·이동/정리 오류를 사람이 읽는 한글 설명. 문장을 파싱해 제어하지 않음 |
| `NativeCode` | `int?` | 주 작업의 마지막 SDK 응답. SDK 미호출·응답 전 예외는 null일 수 있음. 정리 결과는 원래 값을 덮지 않음 |
| `PrintInvoked` | `bool` | StartLabelPrint 진입 직전에 true 기록. 실제 인쇄 완료 여부가 아님 |
| `PrintCode` | `int?` | 해석한 인쇄 응답. 미호출·응답 전 예외·해석 불가는 null. 후속 배출·정리 실패에도 인쇄 응답을 보존 |

연결 확인의 1은 Open이며 다른 함수의 오류 코드 1과 같은 의미가 아니다. `Status`, `Function`, `MovementCode`, `CleanupCode` 속성이나 암시적 bool 변환은 없다. 이전 호출을 옮길 때는 [이행 표](#기존-호출에서-이행)를 따른다.

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

이후 짧은 예제는 각각 독립된 사용 예다. `printer`는 호출 프로그램이 관리하는 객체이며, 인쇄 예제는 연결·실제 정렬·위치 기록 확인이 끝난 상태다. 결과는 반드시 `IsSuccess`와 `Message`로 확인한다. 예제에 쓰는 입력은 다음과 같다.

| 이름 | 호출 프로그램이 준비할 내용 |
|---|---|
| `selectedUsbPort`, `presentationDistanceMm`, `formPath` | 빠른 시작의 입력값 준비와 같음 |
| `btplWithoutVariables` | 변수 표식이 없는 BTPL 원문 문자열 |
| `btplWithVariables` | 예제에서는 V00·V01을 사용하는 BTPL 원문 문자열 |
| `rawVariableData` | 해당 서식에 필요한 모든 값의 raw `!!` 문자열. 변수 없는 서식에만 null 사용 |
| `requestedCount` | 호출 프로그램이 정한 양의 정수 장수. DLL은 이 값을 받지 않고 한 장씩 호출됨 |

### ConnectUsb

확인한 USB 포트로 연결하고 SDK 연결이 열렸는지 확인한다.

```csharp
public PrinterResult ConnectUsb(string port, int presentationDistanceMm = 0)
```

| 파라미터 | 자료형 | 필수·기본값 | 의미·허용 범위 |
|---|---|---|---|
| `port` | `string` | 필수 | SDK의 USB 포트 식별 문자열. null·빈 값·공백만·제어문자 금지. CP949 공통 조건 적용 |
| `presentationDistanceMm` | `int` | 기본 `0` | 정상 인쇄 응답 뒤 추가 배출할 거리(mm). `0..1000`. 0은 추가 배출 없음 |

**호출 조건과 동작:**

- 다른 객체 또는 이 객체가 이미 연결을 관리 중이면 실패하며 SDK를 호출하지 않는다.
- 연결 자체는 용지 복귀·배출·FEED 명령을 보내지 않는다.
- 프로세스에서 해당 포트를 처음 등록할 때 [위치 기록](#위치-기록과-알려진-제한)을 읽는다. 위치 불명이어도 연결은 성공할 수 있지만 인쇄는 별도로 차단된다.
- 포트나 배출 거리를 변경하려면 해제 후 다시 연결한다.

**반환:** 정상 연결 확인은 `IsSuccess=true`, `NativeCode=1`. `PrintInvoked=false`, `PrintCode=null`. 연결 중 실패 후 부분 연결을 정리하며, 정리도 실패하면 원래 실패와 정리 설명을 보존한다. 원인이 해결되기 전 새 객체로 우회하지 않는다.

**예외:** 잘못된 포트는 `ArgumentException("InvalidPort")`, 잘못된 거리는 `ArgumentException("InvalidPresentationDistance")`. ANSI 949 미지원은 `NotSupportedException`, Dispose 완료 객체는 `ObjectDisposedException`. 검사 순서에 따라 입력 오류가 먼저 발생할 수 있다.

```csharp
PrinterResult result = printer.ConnectUsb(selectedUsbPort, presentationDistanceMm);
```

포트 0은 기존 한 대의 실기 확인값이다. 제조사 PDF의 `USB1` 예제를 모든 환경의 포트값으로 보장하지 않는다. DLL에는 USB 목록 감지 공개 API가 없다. 15 mm도 운영 용지의 확인값이며 DLL 기본값은 0이다.

### GetConnectionStatus

현재 객체가 관리하는 SDK 연결의 열림 상태를 조회한다. 용지 정렬이나 출력 완료 조회는 아니다.

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

인쇄 속도와 농도를 설정하고 정상 응답을 받은 값을 이후 인쇄에 재적용한다.

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

BTPL 원문 문자열과 선택 가변데이터로 한 장을 요청한다.

```csharp
public PrinterResult Print(string btpl, string variableData = null)
```

| 파라미터 | 자료형 | 필수·기본값 | 의미·허용 범위 |
|---|---|---|---|
| `btpl` | `string` | 필수 | CP949로 표현 가능한 BTPL 원문. null·빈 값·공백만·NUL 금지. 원문과 CRLF 정규화 결과 각각 4 MiB 이내 |
| `variableData` | `string` | 기본 `null` | raw `!!` 구분 문자열. null은 변수 등록 생략. 빈 전체 문자열·공백만·제어문자 금지, CP949 4 MiB 이내 |

**호출 조건:** 연결에 성공한 객체와 실제 상태에 맞는 위치 기록이 필요하다. 연결 복구가 필요하거나 위치가 불명이면 인쇄하지 않는다.

**동작:** 직전 이동 후 최소 간격을 기다린다. 이전에 추가 배출한 거리가 있으면 그만큼 복귀한 뒤 한 장을 요청한다. 정상 인쇄 응답을 받으면 설정한 거리만큼 추가 배출하고 마지막에 서식을 정리한다. 준비 단계가 실패하면 인쇄를 호출하지 않는다. 제조사 함수 순서는 [API 대응 표](#제조사-api와의-대응)를 참고한다.

**반환:** 인쇄·이동·정리를 합친 `PrinterResult`. 인쇄 응답은 `PrintCode`에 보존하고 후속 배출·정리 실패는 `IsSuccess=false`로 구분한다. [정상 응답 후 위치 저장 실패의 한계](#위치-기록과-알려진-제한)는 별도로 확인한다.

**예외:** 서식은 `ArgumentException("InvalidForm")`, 변수는 `ArgumentException("InvalidVariables")`; ANSI 949 미지원은 `NotSupportedException`; Dispose 완료 후에는 `ObjectDisposedException`.

```csharp
PrinterResult fixedResult = printer.Print(btplWithoutVariables);
```

다음은 V00·V01을 사용하는 별도 서식의 예다. 필요한 값을 매번 제공한다.

```csharp
PrinterResult variableResult = printer.Print(btplWithVariables, "ITEM001!!LOT001");
```

각 예는 별도 요청이다. 첫 요청이 실패해도 다음 요청을 계속 실행하는 코드로 묶지 않는다. 기본 Print는 AW 좌표·변수 번호·라벨 방식을 검증하거나 서식에 Q/W를 추가하지 않는다.

### PrintFile

CP949 파일에서 읽은 BTPL 내용과 선택 가변데이터로 한 장을 요청한다.

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

운영자가 수동 정렬을 마쳤다는 사실을 기록한다. 용지는 움직이지 않는다.

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

물리 FEED 키에 해당하는 요청을 보내고 정상 SDK 응답 뒤 위치 기록을 갱신한다. 항상 전진하거나 일정 거리만큼 배출한다는 뜻은 아니다.

```csharp
public PrinterResult FeedAndConfirmPosition()
```

**파라미터:** 없음.

**조건·동작:** 연결 소유권이 필요하고 연결 오류 상태이면 거부한다. 프린터 대기 화면에서 사용한다. 연결 확인·최소 간격 대기 → 위치 불명 기록 → `SendData("~S,FEED\r\n")` → 정상 응답과 기록 저장 성공 시 0 확인 순서다. 이전 위치가 불명·양수여도 B 복귀 없이 FEED만 보내며 M·새 Q도 보내지 않는다. 센서·세로·가로 보정값을 별도로 전송하지 않는다.

**반환·예외:** `IsSuccess=true`는 FEED SDK 정상 응답과 기록 저장 성공이다. 실제 이송·정렬 완료의 센서 확인이 아니다. 정상 경로의 `NativeCode=0`, `PrintInvoked=false`, `PrintCode=null`. FEED 응답 비0·예외·이후 저장 실패는 false와 위치 불명이다. 진입 전 거부 시 FEED를 보내지 않으며 NativeCode는 마지막 연결 조회 값 또는 null일 수 있다. Dispose 완료 후에는 `ObjectDisposedException`.

```csharp
PrinterResult result = printer.FeedAndConfirmPosition();
```

이 함수는 새 라벨 설정을 적용하지 않는다. 마지막으로 프린터에 적용된 `^Q`의 영향을 받으므로 서식 파일의 Q만 바꾼 상태에서 새 설정으로 정렬된다고 가정하지 않는다. 대기 화면과 실제 정렬을 확인하고, 용지별 상세는 [용지 운영](PRINT_PRESENTATION.ko.md#공통-복구)을 참고한다. 전원 후에는 후진→전진→후진이 보고됐으며 이 함수를 검증된 전원 재투입 해결책으로 보장하지 않는다. 자동 반복 호출하지 않는다.

### Disconnect

연결을 해제하고 SDK의 닫힘 응답을 확인한다. 정상 해제 뒤 같은 객체를 다시 연결할 수 있다.

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

연결을 정리하고 객체 사용을 종료한다. 진행 중인 요청을 강제로 취소하지는 않는다.

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

## 입력과 여러 장 사용

### 공통 입력 조건

포트·파일 경로·서식·변수 문자열은 CP949로 인코딩한 뒤 다시 읽어도 원문이 같아야 한다. 지원하지 않는 문자를 다른 문자로 바꾸어 보내지 않고 입력 오류로 거부한다. null 허용은 변수 등록 생략에만 적용한다.

문자열 검사 대상은 각각 4 MiB(`4 * 1024 * 1024` bytes) 이내다. PrintFile의 파일 크기도 4 MiB 이내이며 CRLF 정규화 후 다시 검사한다. UTF-8 파일을 CP949 파일로 간주해 보내지 않는다.

서식의 CR/LF는 CRLF로 통일하고 마지막 CRLF를 붙인다. 변수 문자열은 줄끝·공백을 정규화하지 않는다. BTPL 전체 명령의 유효성 검사는 SDK 또는 호출자가 맡는다.

### raw 가변데이터

raw는 호출자가 만든 문자열을 DLL이 항목별로 다시 나누거나 공백을 다듬지 않고 전달한다는 뜻이다. `!!`가 항목 구분자다.

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

**연결 오류와 위치 불명은 별개다.** 다음처럼 나눠 처리하며, 하나를 복구했다고 다른 상태까지 해제된 것으로 보지 않는다.

| 상황 | 호출자가 할 일 |
|---|---|
| 연결·해제·서식 정리 오류로 연결 복구 필요 | 같은 객체로 `Disconnect`의 성공과 닫힘을 확인한 뒤 다시 연결한다. `GetConnectionStatus`의 Open이나 위치 확인만으로 오류 상태가 해제되지는 않는다. |
| 최초 실행·기록 없음 등으로 위치 불명 | 인쇄를 멈추고 실제 위치를 확인한다. 운영자가 인쇄 기준 위치로 수동 정렬을 마쳤다면 그 시점에 `ConfirmPrintPosition`을 호출하고 결과를 확인한다. 자동 초기화로 호출하지 않는다. |
| 운영자가 FEED를 선택 | 연결 복구가 끝난 대기 화면에서 `FeedAndConfirmPosition`을 명시적으로 호출한다. 반환 결과와 실제 정렬을 각각 확인한다. 전원 후 확정 해결책이 아니며 자동 반복하지 않는다. |
| 실제 위치와 유효한 기록이 일치 | 매 요청마다 FEED·위치 확인을 반복하지 않고 기존 기록으로 인쇄한다. |

처음 연결이 성공해도 위치 불명이면 `Print`·`PrintFile`은 인쇄를 호출하지 않고 실패 결과를 반환한다. 반대로 유효한 기록이 남아 있어도 수동 이송·용지 교체·전원 재투입 후의 실제 위치를 보장하지 않는다. 연결이 복구되지 않았으면 FEED도 차단된다.

Tester의 [FEED+확인], 용지 교체와 Q 변경 절차는 [공통 운영 안내](PRINT_PRESENTATION.ko.md#공통-복구)를 따른다. FEED 자체도 이동 요청이며 SDK 정상 응답은 실제 정렬 완료 보장이 아니다.

## 위치 기록과 알려진 제한

기존 정상 배출이 15 mm이고 새 연결 배출 설정이 7 mm이면 다음 요청은 `B15 → P → M7`이다. 새 배출 0이면 이전 거리는 복귀하고 새 M은 보내지 않는다. 연결 해제·Dispose는 추가 이동을 요청하지 않으며 재연결·앱 재시작도 기록을 임의 초기화하지 않는다.

### 기록의 의미와 파일

여기서 B는 기록된 거리만큼의 후진, P는 한 장 인쇄 요청, M은 추가 배출이다. 기록은 메모리와 위치 파일에 저장하는 **명령 이력**이며 센서로 측정한 실제 위치가 아니다.

| 항목 | 현재 계약 |
|---|---|
| 파일 위치 | `C:\Bitek\BT003\position.txt` |
| 저장 형식 | UTF-8 두 줄. 첫 줄은 SDK 포트 문자열, 둘째 줄은 거리(mm) 또는 `unknown` |
| 거리 `0` | 다음 인쇄 전에 추가 배출 거리를 복귀할 필요가 없다는 기록 |
| 거리 `1..1000` | 다음 인쇄 전에 복귀할 이전 배출 거리 |
| `unknown` | 위치 불명. 인쇄를 차단하고 운영자의 실제 정렬 확인이 필요 |
| 파일에 보존하는 범위 | 마지막으로 저장한 포트 한 건. 여러 포트의 기록을 담는 파일은 아님 |

프로세스에서 해당 포트를 처음 등록하는 `ConnectUsb`가 파일을 읽는다. 같은 프로세스의 재연결·새 객체는 포트별 메모리 기록을 유지하며 파일을 다시 읽어 덮지 않는다. 파일 없음·읽기 실패·두 줄 형식 오류·거리 범위 오류·`unknown`·포트 불일치는 위치 불명으로 시작한다. 포트 비교는 대소문자를 구분하지 않는다.

실행 계정에는 기록 폴더 생성과 파일 쓰기·교체 권한이 필요하다. DLL은 같은 폴더의 임시 파일에 쓴 뒤 기록 파일을 교체한다. Tester와 호출 프로그램은 이 파일을 직접 만들거나 수정해 0으로 초기화하지 않는다. 기록 0은 실제 정렬을 대신하지 않는다.

### 저장 실패의 처리

| 저장 단계 | 현재 구현의 처리 |
|---|---|
| B/M/P 또는 FEED 진입 전 불명 기록 저장 실패 | 해당 SDK 이동·인쇄 호출을 하지 않고 false 반환. 앞서 실행한 단계가 있으면 그 영향은 남음 |
| ConfirmPrintPosition의 0 저장 실패 | false 반환 |
| FEED 정상 응답 후 0 저장 실패 | false·위치 불명 반환 |
| 정상 P 뒤 0 저장 또는 정상 B/M 뒤 거리 저장만 실패 | 반환값을 확인하지 않아 IsSuccess=true가 유지될 수 있음. 메모리는 정상 응답 거리로 갱신됐어도 디스크 기록은 갱신되지 않을 수 있음 |

따라서 true는 위치 파일 영속화나 물리 정렬 성공의 보증이 아니다. 정상 응답 후 저장 실패를 언제나 결과에서 감지해 다음 요청을 차단한다고 약속하지 않는다. 실제 구현의 저장 경계는 `PrintCore`·`Move`·`FeedAndConfirmPosition`을 참고한다. 이는 현재 구현의 한계이며 제품 수정 완료를 뜻하지 않는다.

### 전원 재투입과 외부 이송

전원 재투입·수동 FEED·용지 교체·당김·다른 프로그램의 사용은 자동 감지하지 않는다. DLL은 프린터 내부 원점을 저장·복원하지 않으며 전원 후 자동 위치 복원을 보장하지 않는다. 연결 Open과 유효한 기록이 있어도 실제 정렬을 확인해야 한다. FEED를 전원 후 확정 해결책으로 안내하거나 기록을 강제로 0으로 만들지 않는다.

첫 장 상단 이상과 FEED 동작 차이는 DLL 자체의 기능 결함으로 분류하지 않고 SDK·프린터의 동작 조건으로 다룬다. 현재 제공된 SDK API와 확인된 명령 계약 범위에서는 프린터 내부 위치 기준의 조회·저장·복원 및 실제 정렬 완료를 검증하는 수단이 확보되지 않아 DLL만으로 전원 후 자동 보정을 보장하지 않는다. 물리 동작의 원인은 미확정이며 SDK/펌웨어 결함이나 영구적인 해결 불가능을 확정한 판단은 아니다. 기존 `B15 → P → M15`를 유지하고 근거 없는 거리·좌표 보정이나 자동 FEED를 추가하지 않았다. 관측과 판단 근거는 [심화 참고](#전원-후-제한의-판단-근거)에 정리했다.

센서 보정은 계산한 Q가 서식에 반영됐을 때 간접 적용하고, 세로·가로 보정은 미리보기 영역 계산용이다. 이 값들로 전원 후 원점을 복원하지 않는다. [보정값의 적용 범위](PRINT_PRESENTATION.ko.md#실측-보정과-미리보기의-한계)를 따른다.

## 기존 호출에서 이행

이전 DLL을 참조한 호출 프로그램은 현재 DLL 참조로 다시 빌드한다. 변수 없는 호출의 소스 표현이 같더라도 과거 바이너리가 배열 인수 메서드를 참조할 수 있으므로 DLL 파일만 교체하는 이행은 지원하지 않는다.

| 이전 호출·사용처 | 현재 방식 |
|---|---|
| 여러 변수 인수 또는 `string[]` 전달 | 호출 앱에서 필요한 값·NULL 정책·`!!` 충돌을 확인한 뒤 하나의 raw 문자열을 만들어 `Print(btpl, variableData)` 또는 `PrintFile(path, variableData)`에 전달 |
| `Status`·`PrinterResultStatus` 비교 | `if (!result.IsSuccess)`로 실패·불명을 처리하고 `result.Message`를 기록 |
| `Code`·`Function`·`MovementCode`·`CleanupCode` 사용 | 현재 다섯 결과 속성으로 처리 목적을 다시 매핑. 일대일 대체 속성은 없으며 Message를 파싱해 제어하지 않음 |
| bool 반환이나 결과 객체의 bool 변환 가정 | `PrinterResult`를 받아 `IsSuccess`를 명시적으로 확인 |
| 명시적 `null` 변수 인수 | 변수 등록 생략. 이전 변수 초기화 요청이 아니며 변수 없는 서식에만 사용 |

호출 앱에서 검증한 값의 배열을 `string.Join("!!", values)`로 구성하는 것은 가능하지만, 전달받은 raw 문자열을 DLL이 split/join/trim하지 않는 계약과는 별개다. 값 자체의 `!!` 이스케이프는 제공하지 않으며 공백·빈 값 정책도 호출 앱이 정한다. 현재 API에는 배열·여러 변수 인수 overload를 남기지 않았다.

## 제조사 API와의 대응

다음은 공개 DLL 함수가 내부에서 사용하는 제조사 SDK 호출과 처리 의미다. 각 함수의 입력·결과·예외·복구 조건은 이 문서의 함수별 상세에 설명되어 있다.

| DLL 공개 함수 | 내부 제조사 호출·관계 | 처리 의미 |
|---|---|---|
| ConnectUsb | SetPrinterPort(USB) → DeviceConnect → CheckPrtConnection | 포트를 지정해 연결하고 Open 응답을 확인 |
| GetConnectionStatus | CheckPrtConnection | 현재 객체의 SDK 연결 열림 상태 조회 |
| SetPrintingOptions | SetPrintingOption. 정상 설정은 인쇄마다 재적용 | 속도·농도 설정. 서식에 지정된 설정이 우선 |
| Print / PrintFile | ResetForm·SetPrintFormString·선택 SetPrintFormVariableData·StartLabelPrint·선택 SendData | 서식을 등록해 한 장 요청. 정상 인쇄 응답 뒤 선택 추가 배출 |
| ConfirmPrintPosition | SDK 호출 없이 위치 기록만 변경 | 실제 수동 정렬을 마친 뒤 기록을 0으로 저장 |
| FeedAndConfirmPosition | CheckPrtConnection·SendData의 FEED 문자열, 위치 기록 | 연결 확인·FEED 요청 뒤 정상 SDK 응답과 저장 성공 시 기록 0 |
| Disconnect / Dispose | DeviceDisconnect → CheckPrtConnection | 해제 후 Closed 응답 확인. Dispose는 성공 시 객체 수명도 종료 |

내부 Wrapper의 39개 선언은 그대로 사용하지만 Wrapper 클래스는 internal이며 직접 호출용 공개 facade가 아니다. 별도 SDK 직접 호출을 섞으면 DLL의 소유권·직렬화·위치 기록을 우회할 수 있다. TCP/IP·RS-232·RFID·일반 SendData·수신 버퍼 읽기는 이 DLL의 공개 인쇄 API가 아니다. 공개 API 목록에 없는 SDK 기능을 사용할 수 있다고 가정하지 않는다.

제조사 함수의 반환 규약은 함수마다 다르며 모든 숫자 0을 같은 성공 의미로 취급하지 않는다. 공개 결과 처리는 앞의 PrinterResult 절을 따른다. 구현 위치는 [Bt003Printer.cs](../src/U2BIO.BT003PROLabelPrinting/Bt003Printer.cs)와 [PrinterModels.cs](../src/U2BIO.BT003PROLabelPrinting/PrinterModels.cs)다.

내부 인쇄 순서는 `ResetForm` → 정상 옵션 재적용 → 서식·선택 변수 등록 → `StartLabelPrint(1, ...)` → 정상 응답 시 선택 M 배출 → `finally` 서식 정리다. 이전 배출 거리의 B 복귀는 이 준비 단계보다 먼저 수행한다. 이 내부 SDK 함수들을 호출 앱에서 별도로 중복 호출하지 않는다.

### 전원 후 제한의 판단 근거

전원 재투입 뒤 첫 장 상단 이상이 재현됐으며 입력 `^E` 생략도 개선하지 못했다. 전원 유지 시 전진만 하던 FEED가 전원 후 후진→전진→후진했다는 사용자 관측이 있다. 앞뒤 이동만으로 센서 캘리브레이션이나 특정 내부 원점 처리를 확정하지 않는다. 운영상 상세는 [알려진 제한](PRINT_PRESENTATION.ko.md#전원-재투입-후-알려진-제한)을 참고한다.

현재 제공된 SDK와 확인된 명령 계약 범위에서는 내부 위치 기준의 조회·저장·복원, FEED 정렬 경로 선택 및 실제 정렬 완료를 검증하는 수단이 확보되지 않았다. 매뉴얼의 수신 함수 `ReadResponseMessage`도 현재 배포 SDK의 x86/x64 export에 없어 사용할 수 없다. 이 함수의 부재만으로 물리 동작의 원인을 설명하지는 않는다.

기존 비교의 한 조건에서는 전원 유지·재투입의 전체 B/P/M USB 전송이 같았는데 물리 결과가 달랐다. 이는 장치 내부 상태의 동일성이나 특정 원인을 증명하지 않는다. 현재 확인 범위를 다른 프린터·펌웨어·용지 전체의 성공으로 확대하지 않는다.

관리 검사·문서 예제 컴파일은 SDK 로드·USB 전송·물리 출력 성공을 증명하지 않는다. 실제 확인 범위는 [운영 안내](PRINT_PRESENTATION.ko.md#운영-기준), 빌드·검증 방법은 [개발 안내](DEVELOPMENT.md)를 따른다.
