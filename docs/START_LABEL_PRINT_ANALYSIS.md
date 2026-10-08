# StartLabelPrint 정적 분석 결과

2026-10-08, SDK_API_COVERAGE.md에 고정한 x86/x64 BlackfishNfcLib.dll의 SHA-256을 대조한 뒤 설치된 Microsoft dumpbin 14.12.25835.0으로 분석했다. 원본 SDK는 수정하지 않았으며 DLL을 로드하거나 장치·인쇄·RFID 함수를 실행하지 않았다. 전체 disassembly는 Git에서 제외한 로컬 작업 폴더에만 보존한다.

## 관측한 구현

| 대상 | x86 RVA | x64 RVA | 관측 내용 |
| --- | --- | --- | --- |
| StartLabelPrint 진입 | `0x12060` | `0x184E0` | 첫 인수의 16-bit copies와 두 번째 출력 포인터를 사용한다. x86 종료는 `ret 8`이다 |
| 최종 출력 복사 | `0x1264A`–`0x1268B` | `0x18B21`–`0x18B61` | 소스 한 바이트를 대상에 쓰고 두 포인터를 증가시킨다. NUL을 복사한 뒤 종료하며 이 루프에 대상 길이 비교가 없다 |
| 결과 문자열 구성 | `0xE200` | `0x13D60` | 내부 바이트 배열을 내부 길이 필드에 따라 반복 처리한 문자열과 결과 정수를 조합한다 |
| DeviceConnect 진입 | `0x13560` | `0x19B90` | 내부 연결 호출 뒤 반환 레지스터를 0으로 설정한다. 반환 0만으로 실제 포트 연결을 증명하지 않는다 |

일부 오류 분기에서 형식 상수 `Unknown,%d`와 결과 정수 102 또는 103을 확인했다. x86/x64 최종 결과 구성 경로의 `%02X`, `:`, `%s,%d` 상수도 원본 바이너리에서 대조했다. 이는 코드의 형식 상수와 분기에 대한 관측이며 실제 장치 응답이나 코드별 물리적 완료 의미의 검증이 아니다. 기존 RFID 응답 파서를 인쇄 전용 파서에 적용하지 않는다.

## 정상 상태의 응답 상한 후보

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

이는 **해시를 고정한 정상 경로의 진단 후보 상한**이다. formatter에 길이 clamp가 없고 최종 복사에도 대상 길이 비교가 없으므로 상태 손상·native 내부 오류까지 포함한 제조사 보편 안전 계약이라고 선언하지 않는다.

## 확인된 것과 남은 경계

- 관측한 출력 복사는 1-byte 단위이며 종료 NUL도 대상에 쓴다. 일반적인 출력 인코딩 계약과 모든 예외·오류 경로까지 확인한 것은 아니다.
- native 함수에 대상 길이 인수가 없고 관측한 복사 루프에도 대상 상한 검사가 없다. 샘플의 `SizeConst=100`은 native 쓰기 상한을 설정하지 않는다.
- 내부 정상 길이 설정 경로와 42-byte 후보를 위와 같이 확인했다. 손상된 상태를 포함하는 보편 상한은 검증하지 않았다. 따라서 100 bytes 또는 임의의 큰 버퍼를 안전 상한으로 선언하지 않는다.
- RFID를 변경하지 않는 모든 내부 경로, 성공·실패 응답의 전체 문법, 정상 반환과 실제 라벨 출력 완료의 관계는 아직 확인하지 않았다.
- DeviceConnect=0과 별도로 CheckPrtConnection=1을 요구하는 기존 연결 판정은 유지한다. 연결 성공은 인쇄 준비·인쇄 완료의 증거가 아니다.

분석 명령은 다음 형태이며 제조사 DLL의 명령을 실행하지 않는다.

```text
dumpbin /ERRORREPORT:NONE /EXPORTS <BlackfishNfcLib.dll>
dumpbin /ERRORREPORT:NONE /DISASM:NOBYTES /OUT:<task-output.txt> <BlackfishNfcLib.dll>
```

## 다음 검사

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

준비 단계에서는 정확한 외부 로그 대상의 사용자 허용 전까지 실제 진단을 호출하지 않았다. 이후 사용자가 TASK-02 시험 중 SDK 자체 로그 생성·추가 기록을 명시 허용하여 x86 진단을 실행했다. 원본 DLL 패치, 로그 API hook, 보안 정책 변경은 하지 않았다. 실제 로그 내용·UID는 저장소나 Issue에 복사하지 않는다. 시험별 SDK 응답과 실제 출력 관측은 [NATIVE_SDK_IMPLEMENTATION.md](NATIVE_SDK_IMPLEMENTATION.md)에 기록한다.

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
