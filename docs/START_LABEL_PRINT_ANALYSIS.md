현재 #16의 계약은 [동기 USB 스펙](specs/SYNC_USB_MVP.md)이다. 아래 분석은 포인터 보호와 자체 로더가 있던 이전 구현의 역사 기록이다. 제품은 원본 StringBuilder/DllImport로 전환했고 출력 버퍼 결함 방어와 실행 시 고정 SDK 해시는 MVP에서 제외했다. 아래 과거 보호를 현재 제품 기능이나 새 실기 성공으로 해석하지 않는다.

# StartLabelPrint 정적 분석 결과

## 2026-10-12: 원본 StringBuilder 전환 재검토

이번 재검토는 #15의 `add38f4` 작업본을 기준으로 한다. 아래 기존 분석·실기 이력과 구분하며, 제조사 DLL을 로드하거나 SDK·장치를 실행하지 않았다. 제품 소스와 기존 검사는 변경하지 않았다.

제품의 x86/x64 BlackfishNfcLib.dll·HFModule.dll 네 파일의 SHA-256이 SdkRuntime에 고정한 값과 일치함을 다시 확인했다. Microsoft dumpbin 14.12.25835.0으로 두 Blackfish 파일을 새로 disassemble하여 다음을 재관측했다. 로컬 출력은 `.work/output-review-20261012/`에 두고 배포·커밋하지 않는다.

| 확인 대상 | 이번 관측 | 전환 판단 |
| --- | --- | --- |
| x86 최종 복사 RVA `0x1264A`–`0x1268B` | 한 바이트를 출력 포인터에 쓰고 source/destination을 증가시킨 뒤, 그 바이트가 NUL인지 비교한다 | 이 루프에는 대상 길이 비교가 없다 |
| x64 최종 복사 RVA `0x18B21`–`0x18B61` | x86과 같은 바이트 복사·포인터 증가·NUL 종료 흐름이다 | 이 루프에도 대상 길이 비교가 없다 |
| formatter x86 `0xE200`, x64 `0x13D60` | 내부 UID 길이 필드까지 반복하는 분기를 재확인했다 | 이번에 모든 setter·오류 경로의 출력 상한을 새로 증명한 것은 아니다 |
| 원본 Wrapper·매뉴얼 부록 | `void StartLabelPrint(ushort, StringBuilder)`와 LPStr/SizeConst=100 선언이며 출력 길이 인수가 없다 | 선언 채택과 출력 쓰기 상한 보장은 다른 사항이다 |
| 제품 PrintResponse | 42-byte 뒤 PAGE_NOACCESS, 지정 범위 내 NUL/ASCII/응답 문법 검사, finally에서 관리·native 버퍼 지우기를 수행한다 | 응답 코드·길이·prefix 종류만 반환하고 UID 문자열은 보존하지 않는다 |

42 bytes는 아래 과거 분석에서 고정 SDK의 정상 상태에 대해 얻은 값이다. 이번에는 네 파일의 동일성과 최종 복사·formatter 루프를 재확인했으며, 정상 길이 분석을 모든 native 상태의 보편 상한으로 확대하지 않았다.

Microsoft의 [문자열 마샬링 설명](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/default-marshalling-for-strings#fixed-length-string-buffers)은 StringBuilder 사용 시 callee가 버퍼 용량을 넘지 않아야 한다는 조건을 둔다. [interop 설명](https://learn.microsoft.com/en-us/dotnet/standard/native-interop/best-practices#dllimport-specific-guidance)에 따르면 StringBuilder 마샬링에는 별도 native 버퍼와 관리 복사가 있으며 반환은 첫 NUL까지 처리한다. 이 문서에서 PAGE_NOACCESS나 버퍼 지우기를 보장하는 계약은 확인되지 않았다. 공개 Reference Source의 AsAny/StringBuilder 구현도 참고했지만, 이것을 해당 LPStr P/Invoke stub이나 설치된 .NET Framework 4.6.1 대상 실행 환경의 관측 결과로 사용하지 않는다.

`new StringBuilder(100)`, 반환 후 Length/ASCII 검사, `Clear()`만으로는 기존 쓰기 경계와 모든 중간 버퍼 지우기를 대체할 근거가 되지 않는다. 반환 후 문법 검사는 계속 필요하지만 호출 중의 초과 쓰기를 막는 검사는 아니다. NUL이 없거나 native가 정상 분석 범위를 넘겨 쓰는 경우를 관리 콜백 검사 성공으로 해결했다고 표시하지 않는다.

**판정: StringBuilder 제품 전환 근거는 여전히 부족하다.** 원본 Wrapper는 보존하고 StartLabelPrint 한 곳의 기존 pointer bridge·PrintResponse를 유지한다. SDK 기능 재구현, 별도 격리 engine, 추가 원형 또는 광범위한 시험을 만들지 않는다. #15 수용 기준5는 미완료이며, 이 정적 검토를 제품 출력 전환이나 실제 P/Invoke 성공으로 처리하지 않는다.

전환 재개에는 길이·종료·오류 경로의 확인뿐 아니라 기존 출력 보호와 지우기를 대체할 구체적인 근거가 필요하다. 공식 정상 출력 상한만 확보하거나 정상 응답 시험을 늘리는 것은 PAGE_NOACCESS·중간 버퍼 지우기의 대체를 증명하지 않는다. 장치 미실행은 현재 사용자 범위이며 이 검토를 위해 장치 시험을 요구하지 않는다.

이번 변경은 이 판단과 계획 기록뿐이다. 문서·저장소 검사와 최종 diff를 확인하며, 변경하지 않은 제품의 빌드·관리 검사는 반복하지 않는다. 아래 2026-10-08 이후 내용은 과거 분석·실기 이력이다.

## 기존 분석·실기 이력

2026-10-08, SDK_API_COVERAGE.md에 고정한 x86/x64 BlackfishNfcLib.dll의 SHA-256을 대조한 뒤 설치된 Microsoft dumpbin 14.12.25835.0으로 분석했다. 원본 SDK는 수정하지 않았으며 DLL을 로드하거나 장치·인쇄·RFID 함수를 실행하지 않았다. 전체 disassembly는 Git에서 제외한 로컬 작업 폴더에만 보존한다.

## 관측한 구현

| 대상 | x86 RVA | x64 RVA | 관측 내용 |
| --- | --- | --- | --- |
| StartLabelPrint 진입 | `0x12060` | `0x184E0` | 첫 인수의 16-bit copies와 두 번째 출력 포인터를 사용한다. x86 종료는 `ret 8`이다 |
| 최종 출력 복사 | `0x1264A`–`0x1268B` | `0x18B21`–`0x18B61` | 소스 한 바이트를 대상에 쓰고 두 포인터를 증가시킨다. NUL을 복사한 뒤 종료하며 이 루프에 대상 길이 비교가 없다 |
| 결과 문자열 구성 | `0xE200` | `0x13D60` | 내부 바이트 배열을 내부 길이 필드에 따라 반복 처리한 문자열과 결과 정수를 조합한다 |
| DeviceConnect 진입 | `0x13560` | `0x19B90` | 내부 연결 호출 뒤 반환 레지스터를 0으로 설정한다. 반환 0만으로 실제 포트 연결을 증명하지 않는다 |

일부 오류 분기에서 형식 상수 `Unknown,%d`와 결과 정수 102 또는 103을 확인했다. x86/x64 최종 결과 구성 경로의 `%02X`, `:`, `%s,%d` 상수도 원본 바이너리에서 대조했다. 이는 코드의 형식 상수와 분기에 대한 관측이며 실제 장치 응답이나 코드별 물리적 완료 의미의 검증이 아니다. 기존 RFID 응답 파서를 인쇄 전용 파서에 적용하지 않는다.

## 고정 SDK 정상 상태의 응답 상한

Blackfish뿐 아니라 고정된 HFModule 쌍의 해시를 확인하고 UID 길이의 저장 경로와 concrete getter dispatch를 추적했다. 별도 검토자가 추가 setter와 초기화를 대조했다. 아래 주소는 모듈 기준 RVA다.

| 경로 | x86 RVA | x64 RVA | 관측한 길이 |
| --- | --- | --- | --- |
| StartLabelPrint 길이 초기화 | `0x120E1` | `0x18576` | 0 |
| 고정 getter | `0x32415`, `0x3637D` | `0x4027E`, `0x45435` | 7 또는 8 |
| 동적 getter 실패 기본값 | `0x34D92` | `0x43700` | 10 |
| tag 선택 setter | `0x1DEA7`, `0x1DEDD`, `0x1DF0F` | `0x269AC`, `0x269F0`, `0x26A31` | 4, 7 또는 10 |
| 다른 subclass setter | `0x20E37`, `0x20E70` | `0x2A9C3`, `0x2AA0B` | 4 또는 7 |
| HFModule FullAnticolSelect 성공 출력 | `0x38F6`, `0x392D`, `0x3956` | `0x4605`, `0x462C`, `0x4644` | 4, 7 또는 10 |

formatter는 각 바이트를 zero-extend하여 두 자리 uppercase hex로 구성한다. 정상 상태에서 가장 긴 prefix는 10바이트 UID의 29 ASCII bytes다. 여기에 쉼표 1, signed int32 decimal 최대 표시 길이 11, 종료 NUL 1을 더하면 **42 bytes**다. `Unknown` 오류 prefix는 이보다 짧다.

이는 **해시를 고정한 SDK의 정상 경로에 한정한 42-byte 분석 상한**이다. 제조사 추가 문서를 기다리는 대신 아래 격리 진단과 실기·경계값 검사로 이 범위의 응답 계약을 채택한다. formatter에 길이 clamp가 없고 최종 복사에도 대상 길이 비교가 없으므로 상태 손상·native 내부 오류까지 포함한 보편적 in-process 안전 보장은 아니다.

## 확인된 것과 남은 경계

- 관측한 출력 복사는 1-byte 단위이며 종료 NUL도 대상에 쓴다. 일반적인 출력 인코딩 계약과 모든 예외·오류 경로까지 확인한 것은 아니다.
- native 함수에 대상 길이 인수가 없고 관측한 복사 루프에도 대상 상한 검사가 없다. 샘플의 `SizeConst=100`은 native 쓰기 상한을 설정하지 않는다.
- 내부 정상 길이 설정 경로와 42-byte 후보를 위와 같이 확인했다. 손상된 상태를 포함하는 보편 상한은 검증하지 않았다. 따라서 100 bytes 또는 임의의 큰 버퍼를 안전 상한으로 선언하지 않는다.
- 호출 경로의 RFID API·BTPL 기록 명령 미사용은 검토했으나 저장 데이터 전후 일치는 시험하지 않았다. 실제 응답0·101과 formatter 문법을 구분하고, 모든 오류의 물리 효과 또는 정상 반환과 배출 완료의 일치는 주장하지 않는다.
- DeviceConnect=0과 별도로 CheckPrtConnection=1을 요구하는 기존 연결 판정은 유지한다. 연결 성공은 인쇄 준비·인쇄 완료의 증거가 아니다.

분석 명령은 다음 형태이며 제조사 DLL의 명령을 실행하지 않는다.

```text
dumpbin /ERRORREPORT:NONE /EXPORTS <BlackfishNfcLib.dll>
dumpbin /ERRORREPORT:NONE /DISASM:NOBYTES /OUT:<task-output.txt> <BlackfishNfcLib.dll>
```

## 채택한 진단 경계

`NativeLabelResponse`는 정확히 42 bytes 내 NUL과 printable ASCII, canonical signed int32, 빈/Unknown/지원 길이 UID prefix를 검사한다. 결과에는 코드·길이·prefix 종류만 남기고 실제 UID를 보존하지 않는다. 이 파서는 반환 이후 검사이며 native 쓰기 보호를 대신하지 않는다.

일회 진단에는 별도 child process의 42-byte 출력 영역 바로 뒤에 PAGE_NOACCESS를 둔다. [VirtualAlloc](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualalloc)으로 할당한 두 페이지 중 두 번째를 [VirtualProtect](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualprotect)로 보호하고, 반환 후 지우고 [VirtualFree](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualfree)의 MEM_RELEASE로 해제한다. 초과 쓰기가 child를 종료해도 실제 출력 여부는 Unknown이며 재호출하지 않는다.

현재 GetFunction의 PrintContractUnverified 차단은 유지한다. 공유 인쇄 helper의 reachable 경로에서 확인된 RFID write 호출은 없지만, 실제 RFID 전후 일치와 전체 vendor behavior의 증거를 대신하지 않는다. 상태 helper의 반환 0은 내부 정상 상태이며 물리적 출력 완료를 뜻한다고 해석하지 않는다. vendor 내부 연결 재시도 경로가 있으므로 프로그램의 재출력 금지와 SDK 내부 동작을 구분한다.

## 로그 두 기록과 실제 전송 경로

최초 실기 후 합성 문구만을 기준으로 SDK 로그 구조와 x86의 추가 하위 경로를 확인했다. private 원문 로그와 다른 라벨 값은 출력하거나 복사하지 않았다.

- `SetPrintFormString` RVA `0xF7C0`은 `0xF7CE`에서 API 인수 로깅 wrapper `0x30BF0`을 호출한 뒤 서식을 저장한다. 이 기록은 인쇄 실행이 아니다.
- 공유 helper `0x90D0`은 `0x95E0`에서 서식을 가공하고 객체 `+0x1F4`에 저장한다. `0xA510`의 `0xA564`는 그 가공된 서식을 별도로 기록한다. 입력 서식과 가공된 서식이 각각 로그에 나타나도 두 번의 StartLabelPrint를 의미하지 않는다.
- `0xA583`은 가공된 서식을 큰 함수 `0xAB90`에 전달한다. 이 함수는 GDI/raster 처리와 실제 printer transport를 포함한다. 상위 helper만 보고 전송이 없다고 추론해서는 안 된다.
- `0xD6A2`에서 `E\r\n`을 검색한다. 검색 helper `0x15620`은 미발견 시 -1, 발견 시 index를 반환하며 `0xD6A7`–`0xD6A9`는 index=0일 때만 현재 버퍼 전송 경로로 진행한다.
- 버퍼가 1024 bytes보다 크면 `0xD73D`에서 조각을 전송하고, 남은 길이가 있으면 `0xD78B`에서 전송한다. 두 call site는 서로 다른 라벨 매수나 인쇄 요청을 뜻하지 않는다. 해당 call 직후에는 transport 반환 레지스터를 검사하는 분기가 없다.

이번 관리 코드는 StartLabelPrint(1)을 한 번 호출했고 x86만 실행했다. 자체 합성 문구의 로그 두 기록은 원문과 가공 서식으로 구분되며, 가공 서식에는 E가 한 번 있다. 그러나 가공 로그의 문자 바이트 수를 GDI 처리 후 native 전송 버퍼 길이와 동일하다고 가정하지 않는다. SDK 내부 재시도와 실제 출력 매수는 이 정적 분석만으로 확정하지 않는다. 정상 응답 0은 물리적 완료뿐 아니라 이 하위 전송 반환값의 성공 증명으로도 사용하지 않는다.

## Black-Mark 서식의 내부 용지 해석 한계

추가 분석은 같은 고정 바이너리와 BTPL 원문 10쪽을 기준으로 했다. 원문은 Black-Mark의 x를 label length, y를 마크 폭, z를 마크와 서식 시작점의 관계로 설명한다. 그림의 반복 위치 관계만으로 특정 SDK의 pitch·센서·소수 offset 지원을 확정하지 않는다. 현재 실측 용지의 반복 주기 40 mm와 흰 라벨 길이 20 mm를 구분한다.

- x86 `0x3A0098`의 literal은 원본 PE bytes에서 `^Q`로 대조했다. preprocessing `0x95E0`의 `0x9A6F`–`0x9B23`은 세 개의 comma-separated field를 처리한다. x64 대응은 `0xE72A`–`0xE80B`다.
- x86 `0x9AB5`에서 두 번째 field를 정수로 읽은 후, 0이면 `0x9AD3`에서 세 번째 field를 읽어 내부 mode 2로 저장한다. 0이 아니면 `0x9ABF`가 `0x9AF3`으로 이동하며 두 번째 field만 다시 저장하고 mode 1을 설정한다. 후보 `^Q40,5,7.5+`는 후자이며 이 metadata 분기에서 세 번째 field를 읽지 않는다. mode 1을 문서의 Black-Mark enum과 같다고 단정하지 않는다.
- 숫자 helper `0x2CFA68` → `0x2CE014`는 부호와 정수 digit을 누적한다. x64는 `0x3D680C` → `0x3D4A48`이다. 소수점을 해석하는 경로는 관측되지 않았다. trailing 기호의 정확한 오류 동작까지 확정한 것은 아니다.
- 원문 세 번째 field가 중간 가공 서식에 남을 가능성과 내부 geometry에 반영되는지는 별개다. 이후 큰 `0xAB90`에서 생성한 buffer를 transport에 넘기므로, 서식 로그에 offset이 보인다는 사실만으로 실제 프린터에 동일 값이 전달됐다고 판단하지 않는다.
- `^G0` literal의 해당 처리 경로와 모델 지원은 아직 확인하지 않았다. 프린터의 센서 설정이 반영되었거나 후보가 인쇄 원점을 보정한다고 선언하지 않는다.

`7600257` 작성 시점에는 시험 후보의 관리 전달만 검사했고 보정 실기를 실행하지 않았다. 공통 DLL에 용지 규격을 고정하지 않았다. 이후 원문 명령 전달 경로의 추가 분석과 단일 실기는 아래와 같이 구분한다.

### 후속 확인: 내부 metadata와 원문 전송은 별도 경로

Q-present 분기의 metadata 관측을 offset 전송 누락으로 확대하지 않는다. x86 `0x9898`–`0x98A4`에서 Q가 발견되면 `0x9956`으로 이동하여 인수 substring을 읽는다. 이 경로는 원문 Q 행을 대체하거나 삭제하지 않는다. 반대로 Q가 없을 때만 `0x98AA`–`0x9951`에서 기본 Q 문자열을 만든다. `0xA44C`–`0xA49E`는 기본값 prefix와 남은 원문을 결합하여 결과 문자열에 저장한다.

큰 출력 함수의 두 번째 행 반복은 `0xBEED`부터 시작한다. FONT/AUID/AW 처리를 지나 `0xC155`에서 AW가 아닌 행은 `0xD644`로 이동한다. `0xD644`–`0xD690`은 해당 원문 행에 CRLF를 붙여 전송 buffer에 append한다. 고정 후보의 Q/W/G/AC/E는 이 일반 행 경로를 따른다. `0xD697`–`0xD6A9`에서 E 행을 만나면 누적 buffer를 전송한다. 따라서 내부 metadata에서 z를 사용하지 않는 것과 원문의 z 문자열이 전송 buffer에 남는 것은 양립한다. 이 확인은 프린터가 소수 offset·G0를 지원하거나 용지 원점을 맞췄다는 실기 증거가 아니다.

USB 전송 helper `0x66B0`은 객체 `+0x214`의 vtable `+0x0C`를 호출한다. 원본 PE의 vtable `0x3A1160`을 읽어 `0x1CFB0`으로 대조했고, 이 함수의 IAT `0x32F590`은 WriteFile이다. 연결 검사 `0x1D2B0`은 handle 필드 `+0x0C`의 nonzero 여부를 반환한다. Open=1을 프린터의 용지·센서·수신 준비 상태로 확대하지 않는다.

인쇄 helper `0xA510`은 전송 후 응답 문자열의 `total print`를 기다린다(`0xA631`–`0xA6A9`, literal `0x3A0188`). 이를 못 찾으면 ERROR 응답 검사 뒤 연결 상태를 확인하고 연결이 남아 있는 경우 `0xA724`에서 101을 반환한다. Rev.1.3 오류표의 101은 Response timeout이다. 이 오류로 WriteFile 실패·문법 오류·센서 오류 중 하나를 단독 원인으로 확정할 수 없으며, 실제 출력 여부는 별도로 관측한다.

### USB 연결 캐시의 별도 효과

SetPrinterPort의 USB 분기 `0xF459`는 `0x42B0`을 호출하고 이 함수의 `0x434C`은 USB 경로 탐색 `0x4580`으로 이어진다. `0x4580`은 HKLM의 usbprint 열거와 USB printer interface class를 읽고, 사용자별 `Software\BitekTechnology\AXBT003` 캐시도 갱신한다. `0x4744`–`0x477A`는 이전 USBn 값을 삭제한다. 삭제 wrapper `0x259A0`의 IAT `0x32F02C`는 원본 PE import에서 RegDeleteValueA로 대조했다. `0x4C89`/`0x4D24`는 `0x5E30`을 통해 새 USBn 값을 저장하며 `0x26B30` 계열은 RegSetValueExA를 사용한다.

이것은 출력용 용지 설정을 저장하는 SetLabelSetting과 다른 연결 캐시 효과다. 현재 캐시 USB1과 USB print 열거 각각 1개, vendor VID/PID·interface class·활성 instance의 일치를 관측했다. 실제 경로·instance 식별자를 출력하거나 저장하지 않았다. SDK 로그 허용을 Registry 갱신 허용으로 확대하지 않는다. 이후 사용자가 TASK-02 동안 현재 USB1 캐시 갱신을 명시 허용하여 범위 확인 후 단일 AW 진단을 실행했다. 다른 USBn 값이나 장치로 허용을 확대하지 않으며 캐시 수동 수정·SDK 패치로 우회하지 않는다. 실제 실행 결과는 구현·검증 문서를 따른다.

## SDK 자체 로그의 별도 효과

일회 인쇄 경로의 상태 polling helper가 mode 0으로 logger를 호출한다. x86 logger `0x307D0`의 IAT와 원본 문자열을 해석한 결과, `wvsprintfA`는 상태 문자열을 만들고 `GetFileAttributesA`는 고정된 SDK 로그 디렉터리를 검사한다. 디렉터리가 없으면 `CreateDirectoryA` 경로를 거치고 날짜별 `.log` 파일을 `a+`로 연다. 이는 런타임 경로 조회가 아니라 **작업 루트 밖 고정 디렉터리에 대한 생성·추가 기록 효과**다.

준비 단계에서는 정확한 외부 로그 대상의 사용자 허용 전까지 실제 진단을 호출하지 않았다. 이후 사용자가 TASK-02 시험 중 SDK 자체 로그 생성·추가 기록을 명시 허용하여 x86 진단을 실행했다. 원본 DLL 패치, 로그 API hook, 보안 정책 변경은 하지 않았다. 실제 로그 내용·UID는 저장소나 Issue에 복사하지 않는다. 시험별 SDK 응답과 실제 출력 관측은 [NATIVE_SDK_IMPLEMENTATION.md](history/NATIVE_SDK_IMPLEMENTATION.md)에 기록한다.

## 문구 출력 확인 후 정지·배출 분석

Y120 시험에서 사용자는 흰 라벨 안의 ASCII 문구 출력을 확인했으며 사진에서도 식별된다. 다만 직접 당겨서 확인해야 했으므로 문구 위치와 인쇄 후 배출을 별도 문제로 다룬다. 다음은 동일한 고정 x86 바이너리의 정적 관측이며 추가 실기는 아니다.

- `^E` 전처리의 모델 값 `0x33` 분기는 원문 명령을 `^E0,0`으로 재작성한다(`0xA07C`–`0xA0A1`, format literal `0x3A0148`). 따라서 일반 프린터의 `^Ex` 설명만으로 현재 SDK에서 x가 그대로 전달된다고 가정하지 않는다.
- 재작성 뒤에도 원래 인수를 metadata로 읽는다. 두 인수일 때 첫째는 객체 `+0x120`, 둘째는 `+0x124`에 저장한다(`0xA175`, `0xA19B`). 설정 저장 문자열에는 `LabelStop`, `CuttingPos`, `CuttingWaitTime`이 있다. 이름만으로 모든 필드의 단위와 제조사 모델 계약을 확정하지 않는다.
- `+0x124`는 `0x3440` worker의 GetTickCount 경과 비교와 WaitForSingleObject의 timeout으로 쓰인다(`0x34D5`–`0x3503`). 원본 PE import의 `0x32F5DC`·`0x32F5D8`을 각각 대조했으므로 이 경로의 둘째 인수는 대기 ms이며 배출 거리 mm가 아니다.
- worker는 `+0x120 - +0x11C`를 계산하고 양수면 `^M%d`, 음수면 `^B%d` 문자열을 만든다(`0x349B`–`0x34AD`, `0x35A0`–`0x35ED`). 원본 PE literal `0x39F808`·`0x39F810`을 대조했다. worker 시작 `0x33D0`의 호출은 `StartPrintWithRfid` export `0x12800` 내부 `0x13427`에 있다. `StartLabelPrint` export `0x12060`은 출력 helper `0x90D0`을 호출하며 이 worker를 직접 시작하지 않는다. RFID 인쇄의 후처리를 현재 인쇄 전용 호출에 있는 기능으로 간주하지 않는다.

이 분석으로 `^E0,20`의 20을 단순 배출 거리로 바꾸거나 RFID 인쇄 함수로 교체하는 보완은 채택하지 않는다. 제공 BTPL 가이드의 `~S,FEED`는 Feed 키와 같은 이송 명령이지만 현재 장치·SDK 경로의 실제 적용과 추가 용지 소비는 아직 검증하지 않았다. 배출 보완의 다음 확인은 인쇄 전용 경로에서 사용할 정지/명시 이송 동작이며, 인쇄 응답 성공을 이송 성공으로 대체하지 않는다. 용지 규격·Y 좌표·배출량을 공통 DLL에 하드코딩하지 않는다.

## 사용자 정상 서식과 현재 샘플의 직접전송 경로

사용자는 Q40,5,7.5+·W70·G0·E7·S5·H19·P1·C1·L 및 AC/AW80,120,1,1,0,0,U2BIO TEST2 서식을 제공하고 HFSample_UL에서 끝까지 정상 출력된다고 보고했다. 앞선 7.5 인수 제외 시험의 물리 결과를 이 새 조합의 결과로 대체하지 않는다.

현재 참조 파일을 다시 읽은 결과, Form1의 WritingProc는 원래 RFID 발급 루프와 달리 SendData(m_printScript)를 호출한다. PreparePrintScript는 줄바꿈을 CRLF로 정규화하고 AW와 비ASCII 입력을 거부한다. 따라서 현재 소스에서 AC 직접전송은 설명되지만 AW의 정상 실행은 이 소스만으로 확인되지 않는다. 실제 실행 EXE와 소스의 빌드 일치 여부는 별도이며 원본 참조 파일을 수정하지 않는다.

고정 x86 SendData export `0xF860`은 입력 NUL까지 길이를 세고 원문 pointer·길이를 전송 helper `0x66B0`에 전달한다(`0xF890`–`0xF89D`). `0xF8A2`는 하위 반환값을 버리고 0을 반환한다. 이 함수에는 StartLabelPrint의 `0x95E0` 전처리·AW raster 변환·`^E` 재작성이 없다. 따라서 직접전송에서는 ^E7과 Q의 세 번째 인수가 원문에 남지만, 프린터가 이를 정확히 실행했다는 판정은 물리적 관측이 필요하다. SendData0은 전송 성공 자체나 인쇄 완료를 증명하지 않는다.

반면 기존 StartLabelPrint 경로의 모델0x33 전처리는 ^E7도 ^E0,0으로 바꾸며, 인쇄 전용 함수는 앞서 분석한 RFID용 위치 worker를 직접 시작하지 않는다. 현재 성공 프로그램과 기존 진단은 같은 서식을 보내도 동일한 명령 처리가 아니다. 이는 확인된 차이이며 배출 차이의 유력한 원인이다. E0→E7뿐 아니라 속도·농도·AW 인수·생략 옵션·호출 경로까지 바뀌었으므로 E7 하나가 모든 증상을 해결했다고 단정하지 않는다.

속도 S2→S5는 인쇄 속도, 농도 H10→H19는 열전사 진하기에 관련된다. AW10,2→AW1,1은 SDK raster의 크기·weight 인수에 영향을 주며 AC의 배율1,1과 같은 의미가 아니다. 이전 R0/D0/O0를 뺀 것도 동일한 상태를 보장하지 않는다. 수정된 진단은 제공 서식을 그대로 유지하고 AC 원문 직접전송을 별도 선택하게 한다. 직접전송 결과에는 항상 completion=unknown을 기록하며 LABEL 완료 응답·제품 계약의 성공으로 대체하지 않는다.

### 후속 확정: AW 사용 및 속도·농도 영향 평가

사용자는 인쇄 속도·농도의 영향이 크지 않았다고 보고했고 AW를 사용할 것으로 확정했다. 이 시험 조건에서는 S/H를 배출 문제의 주요 원인 가설에서 제외한다. 모든 용지에서 영향이 없다는 결론으로 확대하지 않으며 제공 서식의 S5/H19는 그대로 둔다.

이후 기본 시험은 기존 blackmark60x20의 AW,80,120,1,1,0,0,U2BIO TEST2를 SDK 경로로 처리한다. AC 직접전송은 과거 비교 근거이며 제품 방식이나 AW의 대체 성공 경로로 채택하지 않는다. 집중 확인 대상은 SDK의 AW 처리, ^E 재작성, 인쇄 후 정지·배출이다. 현재 AW 선택은 인수·한글·완료 응답·완전 배출의 모든 검증 완료를 뜻하지 않는다. 새 물리 시험이나 임의 배출 명령은 이 결정 기록에서 실행하지 않았다.

### 후속 해결 보고: R0·E15의 최종 AW 서식

사용자는 속도/농도·AW 인수·Q/W/G를 유지하고 R0을 명시한 E15 서식으로 문제가 해결됐다고 확인했다. 해당 시험 라벨의 출력·배출 해결은 이 보고를 따른다. R은 가로 여백이고 E는 정지 위치와 관련되므로 이 결과는 배출 정지 위치 보정의 기여를 뒷받침한다. E15를 모든 라벨의 기본값으로 삼거나 API 경로 차이를 무시하지 않는다. 제공 서식의 정확한 CRLF 전달 검증, SDK 내부 E 재작성, 사용자 실행 프로그램의 물리 결과는 별도 근거다. 같은 서식의 불필요한 재출력은 하지 않았다.

### 정정: AC 정상 시험과 AW 검증 목표의 구분

후속 답변에서 사용자는 원래 AC로 정상 시험한 뒤 이 저장소에서 AW를 검증하기 위해 서식을 AW로 바꿨다고 설명했다. 앞선 해결 보고를 최종 AW의 물리 출력 완료로 해석한 판정을 정정한다. AC의 배출 설정을 AW 목표 서식에 채택할 수는 있지만 AW 장치 검증을 대체하지 않는다.

지정한 BT003_PRINT_ONLY_BLACK_MARK_70x20_NATIVE.txt를 제한된 명령 필드만 읽어 대조한 결과, 현재 파일은 최종 AW80,120,1,1,0,0,U2BIO TEST2·R0/E15·Q40,5,7.5+ 서식과 일치한다. raw payload나 로그를 저장하지 않았다. Microsoft 서명 유효한 ILDasm4.8.3928로 기존 EXE의 WritingProc만 정적으로 대조했으며 실행하지 않았다.

| 샘플 실행 파일 폴더 | WritingProc의 호출 |
| --- | --- |
| bin/Debug | StartPrintWithRfid |
| bin/Release | StartPrintWithRfid |
| bin/x64/Debug | SendData |
| bin/x64/Release | StartPrintWithRfid |

x64 Debug의 PreparePrintScript에는 AW 정규식 일치 시 ArgumentException을 던지는 분기도 있다. 현재 AW 파일과 해당 EXE의 제한을 정상 AW 직접전송 근거로 혼합하지 않는다. 현재 소스의 SendData 흐름만으로 다른 EXE가 같은 흐름이라고 판단한 설명도 정정한다. 샘플 프로세스는 조회 시 실행 중이 아니어서 실제 과거 로드 경로는 확인하지 못했다.

실행 폴더의 x86/x64 제조사 DLL 쌍은 현재 기준 핀과 일치했다. 프로젝트 루트와 lib/x64의 일부 복사본은 같은 버전 표기지만 해시가 달랐다. 이를 원인으로 단정하거나 다른 DLL을 자동 선택하지 않는다. 고정 x64 SendData0x15650은 원문 길이·pointer를 helper0xAA80에 전달하고0을 반환하므로 이 함수도 실제 완료 증거가 아니다.

## 샘플·매뉴얼·x64 SDK 심층 대조: wrapper 인쇄 유지

사용자는 원문 전체 직접 전송 대신 wrapper 사용을 지시했다. 제조사 API를 내부 delegate로 래핑하는 현재 구조를 유지하며, 제품 경로를 SendData 원문 전송으로 바꾸지 않는다. 참조 샘플·제조사 DLL·변환 매뉴얼은 수정하거나 실행하지 않았다. 이번 작업은 정적 분석이며 추가 인쇄는 없다.

### 샘플의 연결·수량·전송과 현재 wrapper의 차이

현재 HFSample_UL의 Form1.cs 79/85행은 SetPrinterPort(1,"0","") → DeviceConnect이며 현재 DLL의 USB0 연결 지정과 같다. COM3 또는 용지 DPI를 별도로 정하지 않는다. WritingProc의 211~224행은 UI 수량 m_reqCount만큼 SendData(m_printScript)를 호출하고 오류면 중단한다. 기본 수량은 Form1.Designer.cs 705행의1이다. 수량2를 넣으면 두 번 전송하지만 이는 오류 retry가 아니다. 이번 DLL 진단에는 해당 루프가 없다.

BT003CtrlWrapper.cs는 DllImport 선언을 모아 둔 thin wrapper다. 선언이 있다고 모든 함수를 인쇄 흐름에서 실행하는 것은 아니다. 현재 Form1의 PreparePrintScript 248~253행은 AW와 비ASCII를 거부한다. 따라서 현재 소스의 직접전송 AC 정상 결과를 AW의 wrapper 인쇄 정상 결과로 사용할 수 없다. 실제 과거 EXE별 호출은 앞의 IL 대조표를 따른다.

| 처리 | 현재 샘플 소스 | 현재 DLL AW 진단 |
| --- | --- | --- |
| 연결 | SDK SetPrinterPort·DeviceConnect | 같은 SDK API를 내부 래핑, 단계별 검사 추가 |
| 서식 준비 | 파일 읽기·CRLF 정리·AW/비ASCII 거부 | 검증 프로젝트의 고정 합성 AW 서식을 전달 |
| 인쇄 호출 | wrapper.SendData, UI 수량만큼 | ResetForm·SetPrintFormString·StartLabelPrint(copies1), 한 번 |
| AW 처리 | 현재 소스에서 사전 차단 | SDK의 Windows GDI 렌더링 경로 |
| E 처리 | 원문 직접 전송에서는 SDK 전처리 없음 | SDK 모델 분기에 따라 재작성 |
| 성공 표시 | SendData0으로 UI 완료 카운트 증가 | native0을 기록하되 물리 완료는 별도 관측 |

매뉴얼 Rev.1.3의6/7절은 문자열 서식의 CRLF, 파일 서식 우선, ResetForm 후 서식 재지정을 명시한다. 현재 ResetForm → SetPrintFormString 순서는 이전 파일 서식의 우선 적용 가능성을 제거하며 이 순서가 E 재작성의 원인은 아니다. StartLabelPrint는4장의 C# 선언에 있지만 별도의 상세 API 설명은 없다.30절은 RFID 인코딩을 시작하는 StartPrintingWithRfid를 설명하므로 기존 RFID 데이터 위에 인쇄만 하는 경로에 복사하지 않는다.

### x64에서 확인한 E 재작성과 후처리 차이

현재 기준 SHA-256의 x64 DLL을 로드하지 않고 PE literal·import·pdata와 기존 disassembly를 대조했다. 주소는 image base0x180000000을 뺀 RVA다.

- 0xEF10은 내부 모델 값0x33을 검사하고, 0xEF19~0xEF5F는 인수0으로 `^E%d,0\r\n`을 생성하여 기존 E 행을 대체한다. literal0x51B180을 원본 PE bytes에서 확인했다. 현재 사용자 로그의 E0,0과 일치한다.
- 원래 E의 첫 인수는 0xF037~0xF044에서 객체+0x1E8에 저장되고, 두 번째 인수는+0x1EC에 저장된다. 원문 전체를 버린 것은 아니지만 프린터용 서식의 E15 보존 계약은 충족하지 않는다.
- 위치 worker 시작 helper0x6430은 worker0x64C0을 넘긴다. 이 helper의 직접 call site는0x19A37 하나이며 StartPrintWithRfid export 범위0x18CF0~0x19B27 안이다. StartLabelPrint 범위0x184E0~0x18BA8에는 이 호출이 없다. worker는0x664B/0x666C에서 M/B 이동 문자열을 구성한다. x86에서 확인한 인쇄 전용/발행 후처리 차이가 x64에도 존재한다. 모든 간접 경로·펌웨어 동작이 같다는 보장으로 확대하지 않는다.

이는 SDK 내부의 모델별 처리다. wrapper에 원문 E15를 재등록하거나 ResetForm을 반복해도 동일 전처리가 다시 적용되므로 원인 해결이 아니다. 위치 worker를 얻으려고 RFID 발행 API로 교체하거나 SDK 패치·함수 주소 직접 호출을 하지 않는다.

### AW의1은 AC의1배가 아니다

x64 AW 처리의0x11E83~0x11EC1은 comma token3을 정수로 읽어 font helper0x132A0의 size 인수에 전달한다. PE pdata에서 helper 범위0x132A0~0x133ED를 대조했다.0x13329~0x1333C는 `size * 340 / 72`의 정수 결과를 LOGFONT 높이에 넣고0x133BB에서 CreateFontIndirectA를 호출한다. IAT0x43D520을 원본 import로 확인했다. x86의0xDA98~0xDAA7에도 같은 계산이 있다.

| AW size | SDK LOGFONT 높이 계산 결과 |
| --- | --- |
| 1 | 4 |
| 10 | 47 |
| 12 | 56 |

이는 해당 SDK의 내부 글꼴 높이 값이며 실제 mm나 모든 글꼴의 검은 픽셀 높이를 보장하지 않는다. AC의1,1은 글꼴 배율이고 AW의 size/weight와 같지 않다. weight token4의1은400,2는600으로 처리된다. 이번 AW1,1은 매우 작은 높이·보통 weight를 요청한다. 앞선 x86 AW10,2에서 문구가 보였던 결과와 대비되며 문구 없음/희미함의 유력한 별도 원인이다. 그러나 현재 라벨 관측 없이 글자 누락 원인을 이것만으로 확정하지 않는다.

제조사 매뉴얼의 AW 예시는 size10/12를 사용한다. 다음 판별 시험 후보는 Q/W/G/R/E·X/Y·weight·수량을 유지하고 size만1→10으로 바꾸는 AW80,120,10,1,0,0의 합성 한 장이다. 사용자가 지정한 서식을 자동 보정하거나 공통 DLL에 size10을 기본값으로 넣지 않는다. 후보는 아직 실행하지 않았다. 글자 크기 시험과 배출 보완을 동시에 바꾸지 않는다.

### 판정과 다음 보완 범위

두 로그는 중복 로깅이며 관리 코드 retry2의 근거가 아니다. 배출 문제의 확인된 경로 차이는 SDK E 재작성 및 위치 후처리 차이이고, 문구 가시성에는 AW 크기 계약 차이가 있다. 연결 API·CRLF·고정 수량은 해당 입력에서 일치하며 새 DPI·Gap 방식·속도/농도 변경을 원인 해결로 추가하지 않는다.

wrapper의 AW 인쇄 경로는 유지한다. 배출 위치 제어는 별도 계약으로 정리해야 한다. 현재41개 API와 Rev.1.3에는 전용 Stop/Eject 위치 setter가 확인되지 않았으며,27절의 문자열 제어 API 존재를 미검증 배출 완료 보장으로 확대하지 않는다. 원문 전체 직접 전송이나 임의 M/B15 하드코딩 대신, 인쇄 전용 SDK가 지원하는 후처리 방법을 확인하고 출력 성공과 배출 성공을 분리하여 검증해야 한다. SDK0·샘플 UI 완료 카운트만으로 TASK-02를 닫지 않는다.

### 15:50 x64 AW 시험: 두 로그와 E 재작성

사용자가 제공한 합성 시험 로그는 15:50:49.244의 API 매개변수 기록과 15:50:49.256의 가공 서식 기록이다. 첫 기록은 SetPrintFormString에 준 원문이며 자체 인쇄 실행이 아니다. 둘째 기록에는 SDK가 앞에 구성한 P1·세로 원점0·R0과 E0,0이 나타난다. 이는 위에서 확인한 원문/가공 서식의 이중 로깅 구조와 일치하며, 기록 수를 인쇄 호출 수로 해석하지 않는다. raw 로그는 문서나 Issue에 복사하지 않는다.

현재 RunLabelProbe는 ResetForm → SetPrintFormString → 성공 시 StartLabelPrint(1)을 한 번 호출한다. supervisor도 label-diagnostic에서는 지정한 아키텍처 하나만 실행한다. 반복 호출이나 자동 retry 설정은 없고, SetRfidCondition의 retry 인수도 이 경로에서 호출하지 않는다. 기존 관리 검사는 정상 경로에서 Print callback 한 번과 copies=1을 검증한다. 이번 실행 evidence의 x64 결과도 하나다. PrintInvoked는 bool이므로 그 필드만으로 호출 횟수를 증명하지 않고 코드·관리 검사·supervisor 실행 기록을 함께 근거로 사용한다.

첫 기록 전체가 무시된 것은 아니다. Q/W/G/S/H와 AW 행은 가공 기록에 남았지만 E15는 E0,0으로 바뀌었다. 관리 DLL은 E15를 그대로 전달했으며 이 변경은 제조사 SDK의 전처리에서 발생한다. 가공 로그도 AW의 이후 raster 변환을 거친 최종 전송 bytes 전체는 아니다. 현재 x64 시험의 물리 결과는 비정상/불명으로 보고되었으므로 SDK0이나 두 로그만으로 정상 출력, 완전 배출, 실제 장수를 판정하지 않는다. 배출 문제는 E 재작성과 인쇄 전용 경로의 후처리 차이를 우선 확인하고, 문구 문제는 AC 배율과 다른 AW 크기 인수를 별도로 확인한다. 추가 출력·x86 재시도는 하지 않았다.
