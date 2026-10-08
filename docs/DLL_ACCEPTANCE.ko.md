# TASK-07 DLL 인수·배포 상태

2026-10-09 기준이다. 제품 소스 `3050c1f`, TASK-06 완료 문서 `7de27b0`을 기반으로 검사·미실행을 구분한다. 사용자가 실기 검증을 별도 보류로 지시하여 TASK-07은 소프트웨어 인수로 정리하고 실제 SDK/장치 인수는 [별도 실기 #11](HARDWARE_VALIDATION.ko.md)에 남긴다. 실제 출력 금지 기간에 제조사 SDK를 실행하거나 용지를 이송하지 않았다.

## 공통 실행 근거

`node tools/verify-build.js`와 `node tools/verify-build.js --configuration Debug`는 Release/Debug × x86/x64 모두 통과했다. 정확한 revision·조건별 결과·해시는 [TASK-06 검증 기록](BATCH_PRINTING_EVIDENCE.md)에 있다. 이 실행의 `nativeSDK=not-loaded`는 합성 제어 흐름의 근거이며 실제 프린터 출력 성공의 근거가 아니다.

아래 표의 **통과**는 표시한 관리/합성 계약만 의미한다. **부분**은 유사 사례나 일부 계층만 검사한 경우다. 실제 제품 USB 실기는 전체적으로 미실행이며, 미실행·부분을 완전 통과로 집계하지 않는다. S/F/U/N은 Succeeded/Failed/Unknown/NotAttempted다. 표의 검사는 `tests/U2BIO.BT003PROLabelPrinting.Validation/`에 있다.

| ID | 기대 계약 | 실제 실행 근거·결과 | 판정·남은 확인 |
| --- | --- | --- | --- |
| C01 | offline preview 가능, 미확인 장치 조합은 NNN | 추가 합성 검사에서 AW size1 세 행 NNN/DeviceSupportUnverified, offline Render 가능/인쇄0회 | 관리·합성 통과 |
| C02 | 중간 빈 값에서 valid=[true,false,true], NNN | PrinterBatchTests에서 같은 결과, DeviceValidation=null/SDK0회 | 관리·합성 통과 |
| C03 | 사전 취소 NNN/미검사 null | PrinterBatchTests, 연결 전 사전 취소도 Cancelled | 관리·합성 통과 |
| C04 | Busy NNN/자기 SDK0회/pending=false | 동시 요청 및 OtherOwner에서 Busy, 미검사 null | 관리·합성 통과 |
| C05 | 첫 행 Closed FNN/인쇄 미진입 | row-closed, PrintNumber=0/NotConnected | 합성 통과 |
| C06 | 둘째 등록 실패 SFN/raw code | form-second-fail, SFN/코드9/인쇄1회 | 합성 통과 |
| C07 | late 등록 timeout FNN/late 인쇄0 | form-timeout, FNN/미진입/자원 해제/반환 결과 불변 | 합성 통과 |
| C08 | 둘째 print timeout SUN/pending=true | second-timeout, SUN/인쇄2회, late 응답 뒤 U 유지 | 합성 통과 |
| C09 | print 중 취소 후 정상 SNN | cancel-success, SNN/Cancelled | 합성 통과 |
| C10 | print 중 취소 후 실패 FNN 우선 | cancel-fail, 코드9/FNN/SdkFailure | 합성 통과 |
| C11 | print 중 Dispose 후 SNN/해제 | dispose, SNN/Disposed/ResourcesReleased | 합성 통과 |
| C12 | 마지막 완료 뒤 취소 SSS/None | last-cancel, SSS/집계3/None | 합성 통과 |
| C13 | 정리 실패 뒤 SSS 유지 | reset-cleanup-fail, SSS/None/FormResetFailed/Faulted/미해제 | 합성 통과 |
| C14 | Connect 실패 후 Closed/부분 해제 | PrinterConnectionTests의 connect-fail/partial cleanup | 합성 통과 |
| C15 | Disconnect 후 Open이면 Faulted/미해제 | stay-open, DisconnectUnconfirmed/소유 유지 | 합성 통과 |
| C16 | pending/Disposed 조회는 cached | PrinterConnectionTests와 PrinterPrintingTests의 pending·Dispose·상태 조회 및 표준 예외 | 관리·합성 통과 |
| C17 | literal 줄 확장·다음 요소 인덱스/옵션/변수 보존 | 기존 검사와 정확한 2줄 추가 검사: 인덱스1→2·옵션/^Vxx 보존·저장/재로드 Y=[120,160,200] | 관리 통과 |
| C18 | 삭제/끝 복사/교체의 옵션 유지 | BtplTemplateTests의 RemoveText/WithRemovedText/WithLineAdvance/WithText | 관리 통과 |
| C19 | 세션 간 가변 LF 옵션 없음은 오류/NNN | 새 프린터 객체·재로드한 서식의 요청에서 MissingLineAdvance/NNN/DeviceValidation=null/SDK0회 | 관리·합성 통과: OS 프로세스 재시작은 별도 |
| C20 | native 누락/비트/해시/모듈 충돌 구분 | NativeTests의 missing/architecture/version 및 system-module 경로 분류; 고정 쌍 PE 검사 | 부분: 실제 vendor 모듈 선점 충돌·runtime 로드 |
| C21 | loader flag/export 오류와 fallback 금지 | NativeTests의 UnsupportedLoaderPlatform 분류, 고정 쌍41 export 확인 | 부분: 실제 flag 불가·GetProcAddress 누락 실패 주입 |
| C22 | 중복·명령 위치·프레임 오류 거절 | BtplTemplateTests의 DuplicateSetting/CommandPosition/프레임 오류 | 관리 통과 |
| C23 | P1/C1/R0/E0 기본 생성·명시값 보존 | BtplTemplateTests의 기본값·중복 없음·보존; 합성 StartLabelPrint(1) | 관리·합성 통과: 실제 한 행 한 장은 미실행 |
| C24 | strict 저장·충돌·I/O·연속 저장·UI 차단 | BtplTemplateTests/BindingTests의 strict 인코딩·해시 충돌·파일 잠금·연속 Save | 부분: Tester 미저장 표시·UI 출력 차단은 TASK-08 |

관리/합성 판정 21개, 부분 판정 3개다. 부분 항목이 없는 C01~C24 전체 제품 인수 완료라고 선언하지 않는다. 실제 출력·모든 장비 오류·시간 제한·다중 앱 점유·해제 후 재연결의 제품 실기는 추가 근거가 필요하다.

C01/C17/C19의 추가 검사는 로컬 `.work/devflow/i7/contracts/Program.cs`로 Release x86/x64에서 실행했다. 제품 DLL과 기존 합성 Fixture만 사용하고 실제 SDK는 미로드다. 검사 프로그램은 task 전용 서식 두 개만 저장/재로드하며 새 프린터 객체에서 이전 요청의 줄 간격 옵션이 전달되지 않음을 확인한다. OS 프로세스 재시작이나 UI 세션 전환을 실행한 것으로 주장하지 않는다. 이 추가 검사는 제품 소스 변경이 아니다.

## 연결 방식별 지원

| 방식 | 공개 연결 API | 자동 검사 | TASK-02 과거 실기 | 현재 제품 실기·인쇄 |
| --- | --- | --- | --- | --- |
| USB | 명시 USB 식별 문자열, 자동 선택 없음 | 정규화·같은 worker·소유권·실패/정리 합성 검사 통과 | 고정 x86/x64 쌍 연결·AW10/weight1·ANSI949 한글·한 장, M15/B15 제어 확인 | 새 Bt003Printer 경로 실기는 미실행. 코드상 확인된 프로필만 허용 |
| TCP/IP | IP/port 명시 | 검증·native 인수 매핑·대상 식별 합성 검사 통과 | 미실행 | 미실행, 인쇄 DeviceSupportUnverified |
| RS-232 | COM 번호 명시 | COM 정규화·native 인수 매핑·대상 식별 합성 검사 통과 | 미실행 | 미실행, 인쇄 DeviceSupportUnverified |

USB 인터페이스와 COM 인터페이스를 혼동하지 않는다. TASK-02의 물리 케이블 조건 및 SDK 진단 이력은 [Native SDK 구현 기록](NATIVE_SDK_IMPLEMENTATION.md)을 따른다. 과거 진단 결과를 새 공통 API의 실제 검증으로 바꾸지 않는다. 자동 배출15mm/원점 복귀는 제품 기본 기능으로 넣지 않았다.

## 배포 검사와 한계

SDK 사본은 `src/U2BIO.BT003PROLabelPrinting/Sdk/x86` 및 `Sdk/x64`다. BlackfishNfcLib.dll과 제조사 의존 HFModule.dll을 실제 앱 프로세스와 같은 비트 수로 함께 배포한다. 관리 DLL은 AnyCPU다. [배포 명령·파일 배치](SDK_DEPLOYMENT.ko.md)를 따른다.

2026-10-09 read-only `NativeSdk.InspectPair` 검사에서 양쪽 사본의 PE 아키텍처·고정 해시·41 exact export·import를 확인했다. 이 검사는 파일 핸들로 읽고 vendor DLL을 로드하지 않는다. TASK-05의 별도 packaging 검사는 두 플랫폼 선택 복사, 관리 전용 빌드의 SDK 미복사, 잘못된 platform 속성 거절, ProjectReference 전달과 네 DLL 고정 해시를 확인했다.

`VerifySdkAssets.ps1` 직접 실행은 호스트 정책으로 거절되어 미실행이다. 허용된 CertUtil/MSBuild 검사와 현재 PE/해시 검사는 별도 근거이며 그 스크립트를 실행한 것으로 보고하지 않는다. 두 쌍의 고정 해시는 [SDK API 대조표](SDK_API_COVERAGE.md)에 있다.

새 제품 경로의 실제 LoadLibrary/GetProcAddress/ABI/SDK 함수 정상 실행, 실제 device 오류·timeout·취소·재연결은 미실행이다. 제조사 모듈의 정상 로드가 DllMain/의존 초기화를 실행하므로 오프라인 파일 검사와 동일하지 않다. 다른 SDK/ANSI 환경/글꼴 조합은 미검증이며 native fatal 장애 격리를 보장하지 않는다. RFID 저장값 전후 비교는 사용자 제외 정책에 따라 미검증으로 남기고 RFID API/기록 명령 미사용과 구분한다.

## DB·DevExpress 없는 독립 호출

[OfflineSmoke 소스](../examples/OfflineSmoke/Program.cs)와 [프로젝트](../examples/OfflineSmoke/OfflineSmoke.csproj)는 관리 DLL의 공개 API만 참조한다. 솔루션에 UI 프로젝트를 추가하지 않았다. 합성 두 줄의 Parse/Validate/Render, 연결 없는 Print의 NotConnected, 사전 취소 PrintAsync의 Cancelled 및 native SDK 파일/모듈 부재를 확인한다. Connect를 호출하지 않아 장치·인쇄·이송·DB가 실행되지 않는다. 예시 용지값은 DLL 기본값이 아니다.

```text
MSBuild examples\OfflineSmoke\OfflineSmoke.csproj /t:Rebuild /m:1 /nr:false /p:Platform=x86 /p:Configuration=Release
examples\OfflineSmoke\bin\x86\Release\U2BIO.LabelPrinting.OfflineSmoke.exe --expect-x86
MSBuild examples\OfflineSmoke\OfflineSmoke.csproj /t:Rebuild /m:1 /nr:false /p:Platform=x64 /p:Configuration=Release
examples\OfflineSmoke\bin\x64\Release\U2BIO.LabelPrinting.OfflineSmoke.exe --expect-x64
```

두 비트 수의 빌드·실행은 모두 종료0이며 `nativeSDK=not-loaded; no DB/UI/device`를 확인했다. 이 예제에는 `Bt003NativePlatform`을 지정하지 않는다. DB/DevExpress provider를 참조하지 않으며 일반 Framework 타입만 사용한다.

독립 verifier/reviewer는 `7de27b0..34d4fe7`을 검토하고 위 두 비트 수의 예제 빌드·실행과 Release 전체 관리 검사를 재실행해 모두 종료0을 확인했다. 전체 검사 증거는 로컬 `.work/foundation/Release-1791476324048/evidence.json`이며 기존 사용자 솔루션 변경을 보존했다. 당시의 18개 관리/합성·6개 부분 집계와 상대 링크를 대조했다. 이후 C01/C17/C19의 추가 관리/합성 검사를 위에 구분해 기록했다. 완료 조건 표와 본 증거를 연결하는 후속 변경은 문서 전용이다.

## 다음 완료 조건

TASK-07 조건1·4·5·6의 기존 증거에 조건2의 실기 이관, 조건3의 파일 배포/정적 검사와 runtime 미검증 구분, 조건7의 소프트웨어 개발 선행 정리를 적용한다. TASK-05의 새 USB 정상/오류 실기도 #11로 보류한다. 원래 조건과 이관 근거는 [실기 검증 보류·이관](HARDWARE_VALIDATION.ko.md)에 보존한다. 이 소프트웨어 인수 기준으로 TASK-08/09를 진행할 수 있으며 실제 장치 지원·운영 인수는 보류한다.

C24 전체 사례의 UI 부분은 TASK-08에서 검사한다. TASK-07은 이미 실행한 DLL 저장/충돌/I/O/연속 저장 계약을 확인하며 화면 구현 전 UI 버튼/표시 검사를 선행 조건으로 요구하지 않는다. 표의 C24 부분 판정은 전체 사례의 UI 검증이 아직 남았다는 뜻이다. 실제 USB/runtime ABI 검사 의무는 #11에 보존하며 표의 C20/C21 부분 판정을 유지한다.
