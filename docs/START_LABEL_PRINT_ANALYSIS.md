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

## SDK 자체 로그의 별도 효과

일회 인쇄 경로의 상태 polling helper가 mode 0으로 logger를 호출한다. x86 logger `0x307D0`의 IAT와 원본 문자열을 해석한 결과, `wvsprintfA`는 상태 문자열을 만들고 `GetFileAttributesA`는 고정된 SDK 로그 디렉터리를 검사한다. 디렉터리가 없으면 `CreateDirectoryA` 경로를 거치고 날짜별 `.log` 파일을 `a+`로 연다. 이는 런타임 경로 조회가 아니라 **작업 루트 밖 고정 디렉터리에 대한 생성·추가 기록 효과**다.

준비 단계에서는 정확한 외부 로그 대상의 사용자 허용 전까지 실제 진단을 호출하지 않았다. 이후 사용자가 TASK-02 시험 중 SDK 자체 로그 생성·추가 기록을 명시 허용하여 x86 진단을 한 번 실행했다. 원본 DLL 패치, 로그 API hook, 보안 정책 변경은 하지 않았다. 실제 로그 내용·UID는 저장소나 Issue에 복사하지 않는다. SDK 정상 응답과 실제 미출력이라는 실행 결과는 [NATIVE_SDK_IMPLEMENTATION.md](NATIVE_SDK_IMPLEMENTATION.md)에 기록한다.
