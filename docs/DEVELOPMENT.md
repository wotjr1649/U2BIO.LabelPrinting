# 저장소 개발 환경과 검증

소스를 빌드·검증하는 유지보수자용 안내다. 사용자 안내 5개는 [README](../README.md)의 읽는 순서를 따른다. 현행 인쇄 계약은 [DLL API](PRINTER_API.ko.md), 원문 편집·저장 계약은 [TXT API](BTPL_TEMPLATE_API.ko.md), SDK 출처·고정 해시와 실행 파일은 [배포 안내](SDK_DEPLOYMENT.ko.md)에 통합되어 있다.

## 빌드 준비와 저장소 검사

Visual Studio/MSBuild, .NET Framework 4.6.1 targeting pack, Node.js가 필요하다. 전체 솔루션에는 DevExpress 20.2.6이 설치된 개발 PC가 필요하지만 공통 DLL은 DevExpress·DB에 의존하지 않는다. Tester는 C# 7.3, x86/x64, Prefer32Bit=false이며 Any CPU 솔루션 구성은 실행 프로젝트를 x86으로 매핑한다. DevExpress 20.2.6.0 GAC 참조와 licenses.licx를 유지하고 필요한 참조 DLL을 실행 폴더에 복사한다.

```text
node tools/verify-repository.js
git diff --check
```

정상 출력은 doctor의 `ok`와 `Repository checks passed`다. Node.js와 devflow는 개발 검사 도구이며 제품 런타임 의존성이 아니다. `DEVFLOW_ROOT`를 설치된 devflow 플러그인 루트로 프로세스 환경에 지정한다. 지정하지 않으면 검사기는 기존 기본 경로의 devflow 0.3.0을 찾는다. 개인 절대 경로를 공용 설정에 기록하지 않는다.

저장소 검사는 추적 파일과 Git에서 무시하지 않는 새 파일 중 현존 Markdown이 안내 6개·운영 3개의 정확한 집합인지 확인한다. 로컬 파일·한국어 절 링크, 숨은 문자·개인 경로·지침과 private 경로 등은 기존 devflow-doctor로 검사한다. 새 문서가 필요하면 문서 구성 정책을 먼저 변경한다. 과거 자료를 다른 현재 폴더로 옮겨 문서 수만 맞추지 않는다.

## DLL 기반 빌드와 실행 검사

Issue #39의 B 이후 시간 민감도 시험은 `BT003_POST_B_PAUSE_DIAGNOSTIC` symbol로 만든 격리 빌드에서만 제공한다. 그 빌드에서도 AppContext switch `U2BIO.BT003PROLabelPrinting.DiagnosticPostBPause1000`을 명시적으로 true로 해야 B Move 정상 반환 뒤 첫 ResetForm 전에 1000 ms를 한 번 기다린다. 미정의/false와 정상 제품 빌드에는 이 대기가 없다. 기존 `PrintStageTiming` 진단을 함께 켜고 소스 revision·symbol·설정·실행 DLL hash와 실제 SDK 구간 시각으로 조건을 확인한다. 이 시험은 모터 완료 보증이나 제품용 sleep 해법이 아니며, SDK/실기 검증을 대신하지 않는다. 산출물은 기존 배포·실행 폴더와 분리한다.

```text
node tools/verify-build.js
node tools/verify-build.js --configuration Debug
node tools/verify-build.js --scope txt
```

기본 명령은 Release x86/x64를 재빌드한다. DLL은 AnyCPU, 검증 exe와 Tester는 x86/x64다. `--scope txt`도 현재 TXT·공통 프린터 검사를 실행하며 harness·OfflineSmoke를 생략하지 않는다. 범위와 구성을 함께 지정할 때는 `node tools/verify-build.js --configuration Debug --scope txt`처럼 범위를 마지막에 둔다.

| 검사 | 확인하는 내용 |
| --- | --- |
| 실제 제품 DLL의 Validation | Framework·PE·비트수·공개 API·원본 Wrapper 39개 선언·검색 제한·TXT 편집/저장 |
| 제품 두 소스를 Link한 SdkHarness | SDK 대체 호출로 순서·실패 중단·결과·소유권·파일 잠금·배출/복귀·정리 수명 |
| OfflineSmoke | 독립 호출자의 TXT 편집/저장·변수 표시·미연결 API·합성 DataTable 예시 |
| Tester `--self-check --expect-x86` 또는 `--expect-x64` | 화면·SDK 초기화 전 설정·서식·입력·표시·파일 인쇄 검사 |
| 배포 파일 대조 | 검증한 관리 DLL 동일성과 플랫폼별 제조사 DLL 두 파일의 고정 해시 |

검사는 제조사 SDK를 로드하거나 UI·장치 인쇄·이송을 실행하지 않는다. 성공 출력은 각 검사 PASS와 `Build checks passed`다. 실제 SDK 응답·물리 완료·사용자 관측은 별도 근거다. SDK 대체 harness는 제품 두 소스를 수정 사본 없이 링크하며, ResetForm이 이전 변수를 지우지 않는 경우도 검사한다. 실제 제품 DLL의 P/Invoke·USB 성공으로 해석하지 않는다.

검사기는 vswhere로 MSBuild를 찾고 평가된 프로젝트 설정·컴파일러·targeting pack·revision·dirty 상태와 산출물 해시를 기록한다. 각 하위 프로세스는 120초·출력 2 MiB 제한이며 초과는 실패다. compiler 공유와 MSBuild node reuse를 끄고 종료를 확인한다. 실행별 명령·로그·관리 DLL/검증 exe 사본은 `.work/foundation/`에 남긴다. dirty 결과를 같은 커밋 자체의 검증으로 보고하지 않는다.

## 인쇄 단계 시각 진단

같은 관측 전용 compile symbol은 `DiagnosticPostPObservation` AppContext switch를 명시적으로 true로 하면 P 정상 SDK 응답 뒤·위치 0 기록 전·M15 전에 멈추는 진단도 제공한다. B 관측·post-B 1000 ms 대기와 동시에 켤 수 없으며 복귀/배출 15 mm·요청 1장·owned 관측창 조건은 같다. 계속 선택은 기존 M15만 한 번 보내고 P를 다시 호출하지 않는다. 취소·Stop·닫기·관측 예외는 이미 호출한 `PrintInvoked=true`·`PrintCode=0`을 보존하되 실패·위치 미확인으로 반환하고 M/자동 재인쇄를 막는다. P 실패·불명에는 관측창/M을 호출하지 않는다. 같은 라벨의 글자·경계가 보이는 경우만 M 전 위치를 관측하며 보이지 않으면 미관측으로 기록한다. 관측 대기가 재현을 바꿀 수 있으므로 정상 결과만으로 제품 지연 보완을 확정하지 않는다. 이 기능 역시 기본 off이며 정상 제품 빌드에는 포함되지 않는다.

Issue #39의 단계 분리 관측은 별도 `BT003_POST_B_OBSERVATION_DIAGNOSTIC` 빌드와 명시적인 `U2BIO.BT003PROLabelPrinting.DiagnosticPostBObservation=true`에서만 켠다. 정상 빌드에는 관측 callback·친구 어셈블리·UI 코드가 없다. 복귀/배출 15 mm와 요청 1장으로 제한하며 post-B 1000 ms switch는 꺼야 한다. 같은 요청의 Gate·PrintFile 잠금을 유지한 채 B 정상 반환 직후 owned 관측창에서 멈추고, 운영자의 계속 선택 뒤에만 기존 서식 등록·인쇄·배출을 보낸다. 취소·관측 예외는 인쇄/배출 없이 위치를 미확인으로 남겨 재시도를 막는다. B는 이미 보낸 상태이므로 자동 원상 복귀는 하지 않는다. 설정 선행 전송·새 이동·고정 대기를 섞지 않으며 관측 시간 자체의 영향과 SDK 반환/실제 이동 완료를 구분한다. 기본은 off이고, 실제 비교는 확인된 정상 정지 상태에서 사용자 관측으로만 진행한다.

전원 재투입 후 첫 출력의 차이를 조사할 때만 `U2BIO.BT003PROLabelPrinting.PrintStageTiming` AppContext switch를 켤 수 있다. 기본은 미정의/false이며, SDK 호출 순서·거리·대기·위치 기록과 공개 API 서명은 바꾸지 않는다. 실행 exe의 `.exe.config`에 아래 runtime 항목을 병합하고 새 프로세스로 시작한다. 기존 설정을 덮어쓰지 않으며, 제품 기본 설정에는 켜지 않는다. 종료 후 false로 바꾸거나 이 override만 제거한다. 호스트 설정은 [.NET Framework AppContextSwitchOverrides](https://learn.microsoft.com/en-us/dotnet/framework/configure-apps/file-schema/runtime/appcontextswitchoverrides-element)를 따른다.

```xml
<configuration>
  <runtime>
    <AppContextSwitchOverrides value="U2BIO.BT003PROLabelPrinting.PrintStageTiming=true" />
  </runtime>
</configuration>
```

요청 시작에 switch를 한 번 고정하고, B/M의 SendData 및 StartLabelPrint 바로 전후 UTC 시각만 지역 변수에 둔다. 서식·변수·경로·포트·응답 원문을 담거나 별도 파일·listener에 기록하지 않는다. cleanup 뒤 기존 Message 전체에 최대 200자의 `[SDK-UTC B0015=진입/반환;P=진입/반환;M0015=진입/반환]` 접미사를 한 번 붙인다. B/M 뒤 네 자리 숫자는 요청 거리이며, 시각은 `yyyyMMddTHHmmss.fffffffZ`, 미기록은 하이픈 24개다. 사전 저장 실패로 SDK를 호출하지 않은 단계는 양쪽이 미기록이고, SDK가 예외를 던진 단계는 진입만 남는다. 시각 취득·표시의 복구 가능한 실패에는 접미사를 생략하고 원래 결과를 보존한다. Tester는 기존 Message를 표시하며 접미사를 파싱해 제어하지 않는다.

이 시각은 SDK 호출 경계이고 실제 이송·설정 적용 완료가 아니다. UTC 소수 일곱 자리가 실제 시계 분해능을 뜻하지 않는다. 영상에는 같은 PC 시각 기준을 함께 담고 전원 유지/재투입, 앱 유지/재시작을 구분한다. 시계 보정·영상 대응 오차가 크거나 함수 반환 뒤 이동이 계속되면 구간 판정 불가다. 요청이나 cleanup이 끝나지 않거나 프로세스가 종료되면 접미사를 회수할 수 없다. 시각 진단의 관리 검증은 cold 정렬 성공을 증명하지 않으며, 원인이 확인되기 전 B/print/M의 순서·거리를 바꾸는 근거로 쓰지 않는다.

## SDK 배포용 빌드

Visual Studio Developer Command Prompt에서 실행한다.

```text
MSBuild src\U2BIO.BT003PROLabelPrinting\U2BIO.BT003PROLabelPrinting.csproj /t:Rebuild /m:1 /nr:false /p:Configuration=Release /p:Bt003NativePlatform=x86
MSBuild src\U2BIO.BT003PROLabelPrinting\U2BIO.BT003PROLabelPrinting.csproj /t:Rebuild /m:1 /nr:false /p:Configuration=Release /p:Bt003NativePlatform=x64
```

출력은 제품 프로젝트의 `bin/x86/Release/` 또는 `bin/x64/Release/`이며 관리 DLL과 해당 제조사 두 파일이다. `Bt003NativePlatform`을 생략하면 `bin/<Configuration>/`에 관리 DLL만 빌드하며 기존 출력의 SDK를 지우지 않는다. 오프라인 검사에는 SDK가 없는 별도 출력 폴더를 쓴다.

ProjectReference 호출 앱도 `/p:Platform=x64 /p:Bt003NativePlatform=x64`처럼 실행 비트수를 명시하면 SDK 복사가 앱 출력으로 전달된다. 파일 참조 앱은 직접 배포한다. Tester는 제품 재빌드 중복을 피하려고 `Sdk/<Platform>` 두 파일을 직접 링크하며 Bt003NativePlatform과 Platform이 다르면 빌드 오류로 거부한다. DLL/Tester별 실행 파일과 제품 배포 제외 목록은 [배포 안내](SDK_DEPLOYMENT.ko.md)를 따른다.

## Tester 유지보수

사용자 조작은 [Tester 사용](PRINT_PRESENTATION.ko.md#tester-사용), 용지·보정은 [운영 기준](PRINT_PRESENTATION.ko.md#운영-기준)을 따른다. FrmMain은 Bt003Printer 하나와 TemplateDocument 하나를 관리하고, DLL의 공개 인쇄·TXT API를 사용한다. private 위치 상태나 Message 문장을 파싱해 제어하지 않는다. UI는 DB 없이 파일 선택·편집 상태·그리기·확인·장치 작업과 결과 표시를 맡는다. Tester는 단일 [FEED+확인] 버튼으로 FeedAndConfirmPosition만 호출하고 ConfirmPrintPosition 직접 호출은 노출하지 않는다. 위치 파일의 기록·복원은 DLL이 담당한다. 상태 줄은 마지막 정렬 요청의 처리 결과·SDK 응답·시각이며 실제 위치 감지나 인쇄 가능 판정이 아니다. 시작 및 연결·해제 요청 후에는 표시를 초기화하고 인쇄·상태 조회는 갱신하지 않는다.

### 문서 상태와 인쇄

- 편집 성공 시에만 새 immutable 객체를 채택한다. 수정 여부는 마지막 열기/저장 원문과 현재 ToBtpl의 비교다. 표준 숫자를 되돌리면 수정이 없어질 수 있으나 선행 0 표기를 잃으면 수정으로 남는다. 실패는 객체·경로·저장 기준을 바꾸지 않는다.
- Save/SaveAs 성공은 반환 객체를 채택한다. SourceChanged이면 다시 읽기·다른 이름 저장·편집 유지 중 선택하며 원본 삭제 때는 다시 읽기를 제외한다. 같은 경로 SaveAs는 Save로 안내하고 명시적 확인 후에만 overwrite=true를 쓴다.
- 열기·다시 읽기·닫기는 되돌리기 기록을 비우고 저장·SaveAs는 유지한다. 최대 100단계 원문 기록이며 현재 객체의 WithRawText로 복원해 현재 경로·인코딩·저장 해시를 유지한다. 처음 연 원문은 저장으로 바꾸지 않는다. 복원 거부 시 오류를 보이고 상태·기록을 유지한다. 라벨 치수·보정·가변데이터는 원문 기록 대상이 아니다.
- 열기·닫기·종료·저장·인쇄·되돌리기 전에는 그리드 편집기를 확정한다. 검증 거부로 편집기가 남으면 동작을 멈춘다. 변경을 버리기 전에 저장/버리기/취소를 묻고 취소·저장 실패는 전환·종료를 멈춘다. 닫기는 용지 칸을 기준값으로 복원하고 가변데이터는 세션 값으로 유지한다. 새 서식 만들기는 제공하지 않는다.
- AW X 오류는 DLL 진단으로 인쇄를 차단하고, 라벨 방식 불일치·지정 없음·판정 불가는 기본 아니요인 사용자 확인을 받는다. 편집본은 검사한 ToBtpl 원문을 고정해 Print한다. 저장 파일은 UI에서 읽어 확인한 원문과 작업 스레드에서 다시 읽은 내용이 같을 때만 PrintFile한다. 확인 창 동안에는 잠그지 않고, 매 장 파일 비교부터 PrintFile 반환까지 FileShare.Read 잠금을 유지한다.
- 요청 장수는 기본 1·정수 1~20이며 재시작 시 1이다. 시작 때 원문/파일 확인 내용·raw 가변데이터·장수를 고정하고 매 장 한 장 API를 순차 호출한다. ^P/^C나 SDK copies는 바꾸지 않는다. IsSuccess=true에서만 다음 장을 요청하며 PrintCode=0 뒤 배출/정리 실패·예외·불명도 즉시 후속 요청을 멈춘다. 자동 재인쇄하지 않는다.
- 전체 반복 동안 편집·메뉴·설정·장치를 잠근다. 반복 중단·창 닫기는 현재 호출 반환 뒤 다음 장부터 중단한다. 각 결과 i/N과 요청/SDK 정상/실패 또는 불명/미시도 수를 기록하되 물리 완료 장수로 표시하지 않는다.
- SDK 작업은 async/await Task.Run으로 실행한다. 종료 시 진행 작업이 끝난 뒤 UI 스레드에서 Disconnect 결과와 Dispose 예외를 처리한다. 동기 SDK 반환이 늦으면 종료도 기다린다. 실패 후 자동 정렬 확인·재연결로 오류를 숨기지 않는다.

### 설정과 용지 동기화

설정은 사용자 로컬 앱 데이터의 `U2BIO/BT003Tester/settings.xml` 버전 1 XML이며 x86/x64가 공유한다. 같은 폴더 임시 파일로 쓰고 교체한다. 포트·배출은 다음 연결에 적용하고 연결 당시 값을 상태 줄에 표시한다.

| 값 | 기본값과 범위 |
| --- | --- |
| Port | 0, 빈 값·제어문자 거부 |
| PresentationDistanceMm | 15, 정수 0..1000; DLL ConnectUsb 기본 0과 구분 |
| RecentFiles | 최대 10개, Windows 대소문자 무시 중복 제거·최신 우선 |
| Dpi | 300, 203/300 |
| SensorOffsetMm | 1.5, −1000..1000·소수 셋째 자리까지 |
| VerticalOffsetMm | 3.6, 0..1000·소수 셋째 자리까지 |
| HorizontalOffsetMm | −1.0, −1000..1000·소수 셋째 자리까지 |
| LabelMediaMode | BlackMark, BlackMark/Gap만 허용 |

기본 보정과 특정 PC의 실측 후보값을 혼동하지 않는다. 이전 TemplateFolder는 유무와 무관하게 무시하고 읽기만 해서는 다시 쓰지 않는다. 새 설정 항목이 없는 이전 파일은 해당 값만 기본값으로 읽는다. 손상·유효하지 않은 값이면 경고 후 기본값으로 시작하되 설정 화면의 명시적 확인 전까지 기존 파일을 보존한다. 그동안 최근 목록은 메모리에서만 갱신하고 확인 저장 때 함께 쓴다. 파일이 없으면 기본값으로 만든다.

USB 목록은 Windows usbprint 등록 번호와 FriendlyName(없으면 DeviceDesc)을 읽고 장치 인스턴스 ID는 표시·설정·로그에 남기지 않는다. SDK에는 목록 조회 API가 없다. 번호와 SDK 포트 대응은 프린터 한 대·포트 0만 물리 확인했으며 여러 대의 대응을 보장하지 않는다. 저장 포트 미감지와 감지 대수를 표시하고 번호·이름 목록을 다시 감지할 수 있다.

용지 칸은 열기/다시 읽기에서 기준값 후 원문 W와 Q를 매핑한다. Q의 z에서 센서 보정을 빼 흰 부분으로 표시한다. BtplBlackMarkMedia와 DLL WithSetting으로 같은 원문 설정을 재현할 때만 연결한다. 두 인수·방향 −·0.5 mm 단위 외 위치·누락·중복은 임의 복구하지 않고 원문 명령 편집으로 안내한다. 받침지 폭 변경은 W만, 마크 치수·간격은 Q만 반영한다. 라벨 폭/높이는 서식에 넣지 않으므로 숫자를 각각 검증하고 조합이 유효해질 때까지 연결·영역 판정을 멈춰 순차 복구를 허용한다. dpi·보정·라벨 치수는 TXT에 저장하지 않는다. 저장/SaveAs는 용지 칸을 유지하고 설정 변경은 열린 Q/W를 자동 수정하지 않는다. Q 변경 후에는 새 서식 인쇄 뒤 대기 화면에서 [FEED+확인]으로 정렬하도록 안내한다.

### 미리보기와 자체 검사

미리보기는 계산한 LabelArea/SafeArea와 잉크 경고다. SdkTextRenderer는 SDK 글꼴 설정 또는 기본 돋움체, LOGFONT 양수 높이 `size * 340 / 72`, 굵기 0·1=400/2=600/3=700, DEFAULT_CHARSET, FF_ROMAN/VARIABLE_PITCH, 엄격 CP949 TextOutA, TA_TOP, 폭 32픽셀 배수의 1bpp 그림으로 잉크 반열림 범위를 구한다. 실제 실현 글꼴과 대체 여부를 표시한다. SDK FONT 명령·자동 줄바꿈과 정렬·회전의 잉크 판정은 재현하지 않는다. 라벨 밖 문구도 진단용으로 표시하므로 실제 잘린 획 모양은 [운영 안내의 한계](PRINT_PRESENTATION.ko.md#실측-보정과-미리보기의-한계)를 따른다.

그림 한 변 8000 dot·1600만 픽셀, 갱신 중 캐시와 신규 비트/그림 합계 32 MB 한도를 유지한다. 사용하지 않는 캐시와 임시 GDI 자원을 해제하고 실패·한도 초과는 판정 불가로 표시한다. 빈 잉크는 경고하지 않고 정렬/회전은 판정 제외다. 캔버스 클리핑과 선택 표시는 경고 판정·좌표를 바꾸지 않는다. 눈금은 화면 배율에 맞춰 숫자 간격을 늘리고 경계 주석은 겹침을 피하며 선택 줄은 Y 기준선·셀·잉크 범위를 표시한다. 오류는 그리드 입력 칸과 공통 오류 줄로 나누고, 지속 경고는 별도 줄과 툴팁에 둔다.

Tester self-check는 성공 0·실패 1·잘못된 인수 2로 종료한다. 임시 파일과 합성 USB 목록으로 설정 호환·손상 보존·TXT 저장/경합·수정/복원·용지 연결·잉크 경계/한도·GDI 자원·AW 차단·라벨 방식 확인·PrintFile 잠금·장수/폴더 선택을 검사한다. 컴파일된 Tester의 생성자·메서드·async 상태 머신 IL에서 ConfirmPrintPosition 참조가 없고 FeedAndConfirmPosition 참조가 남는지도 검사한다. 이는 메서드 참조 회귀이며 실제 클릭 도달성 검사는 아니다. 실제 사용자 설정·레지스트리·운영 서식은 건드리지 않는다. UI 배치·F12/ESC·확인창과 물리 정렬은 별도 관측이 필요하다.

## 호출 예제와 샘플

[OfflineSmoke](../tests/OfflineSmoke/Program.cs)는 공개 API를 독립 호출자 관점에서 검사하는 오프라인 검증 프로그램이며 [CheckupExamples](../tests/OfflineSmoke/CheckupExamples.cs)는 호출 프로그램이 준비한 DataRow를 받는 두 합성 예시다. DLL에 DB 조회·업무 열 이름·NULL 정책을 넣지 않는다. Checkup은 ExamNo/DisplayName/BirthText를 V00/V01/V02로 구성해 Print하고, SmartCheckup은 VisitCode/DisplayName/Department를 구성해 PrintFile한다. NULL은 빈 문자열, 항목 공백과 빈 칸은 보존하고 필수 열 누락·값 내부 !!는 거부한다. 날짜·숫자 형식은 호출 전에 정한다.

PrintOne은 실제 연결/인쇄를 시도하므로 오프라인 Main이 호출하지 않는다. FEED는 운영자의 명시적 요청에서만 수행하고 항상 true로 고정해 빈 라벨을 이송하지 않는다. PrintFile 예제는 배포 후 고정된 파일을 전제로 하며 외부 편집을 허용한다면 검사한 원문 Print 또는 Tester의 확인·잠금 순서를 따른다. 실제 업무 프로그램 적용은 상사 승인 대기로 진행하지 않았다.

[samples/templates/checkup-two-lines.txt](../samples/templates/checkup-two-lines.txt)는 CP949 고정 두 줄의 가상 예제다. AW X80/Y94와 X80/Y134, 크기10·굵기1·정렬/방향0/0이며 회색 그림은 용지에 있으므로 서식 요소가 아니다. 운영 용지에서만 확인한 Q35,5,9+와 배출15를 공통 기본값으로 적용하지 않는다. 사용자 수정본·복사본의 검증을 뜻하지 않는다.

`samples/templates/checkup-variable.txt`는 기존 가변데이터 서식을 원문 그대로 제공하는 추가 샘플이다. 이번 추가 작업에서는 내용 변경·서식 진단·실기 검증을 수행하지 않았다. OfflineSmoke의 세 항목 호출 예시와 대응하는 서식으로 검증한 것은 아니다.

## 문서 구성과 과거 자료

현행 Markdown은 README·API·Tester/운영·배포·TXT·개발 안내 6개와 AGENTS·GitHub 이슈/PR 템플릿 3개다. 현재 유효한 계약은 이 안내가 소유하며 과거 설계·계획·결정·날짜별 시험 상세는 [정리 전 커밋 c185afe](https://github.com/wotjr1649/U2BIO.LabelPrinting/tree/c185afee7cf7091de7838c62d5b475fe2d148c12)에서 확인한다. 이력·태그를 다시 쓰거나 공개를 차단한 것이 아니므로 예전 자료는 계속 열람할 수 있다.

```text
git show c185afee7cf7091de7838c62d5b475fe2d148c12:docs/specs/SYNC_USB_MVP.md
git show v0.1.1-design:docs/BASELINE.md
```

기준 문서 네 해시는 현재 문서가 아니라 v0.1.1-design의 BASELINE과 해당 태그의 네 문서 원문(LF 정규화)을 대조한다. 신규 clone에는 기준 태그도 가져온다. 현재 안내를 고쳤다는 이유로 기준 태그·해시를 갱신하지 않는다. 과거의 미구현/미검증·구 API와 당시 실행 결과를 현재 제품 전체의 성공으로 해석하지 않는다.

변경은 연결된 Issue와 전용 브랜치에서 진행한다. Issue I/O와 장부는 설치된 devflow-state를 사용하고 공개 기록에는 검토한 요약·합성 증거만 남긴다. push-on-request와 SDK/실기 승인 경계는 유지한다. tests/(OfflineSmoke 포함) 코드는 개발 소스·전체 검증에 남기며 제품 배포에서만 제외한다. 빌드 산출물·계획·검토 보고서·제조사 참고 자료는 Git에서 제외하고 사용자 서식·개인 설정·위치 기록·다른 작업 폴더와 SDK 원본은 보존한다.
