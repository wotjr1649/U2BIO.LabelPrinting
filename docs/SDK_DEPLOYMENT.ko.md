# SDK 빌드·배포

공통 DLL의 소스와 실행에 필요한 제조사 DLL을 같은 저장소에서 관리한다. 제조사 런타임의 정확한 사본은 `src/U2BIO.BT003PROLabelPrinting/Sdk/x86` 및 `Sdk/x64`에 있다. `Sdk/Interop/`는 제조사 원본 C# Wrapper이며 제조사 바이너리 폴더가 아니다.

## 빌드

Visual Studio Developer Command Prompt에서 실행한다. .NET Framework 4.6.1 targeting pack이 필요하다.

```text
MSBuild src\U2BIO.BT003PROLabelPrinting\U2BIO.BT003PROLabelPrinting.csproj /t:Rebuild /m:1 /nr:false /p:Configuration=Release /p:Bt003NativePlatform=x86
MSBuild src\U2BIO.BT003PROLabelPrinting\U2BIO.BT003PROLabelPrinting.csproj /t:Rebuild /m:1 /nr:false /p:Configuration=Release /p:Bt003NativePlatform=x64
```

각 명령의 출력은 프로젝트 아래 `bin/x86/Release/` 또는 `bin/x64/Release/`다. 관리 DLL과 선택한 제조사 DLL 두 개가 같은 폴더에 생성된다. Debug도 같은 방식이다. `Bt003NativePlatform`을 생략하면 기존 `bin/<Configuration>/`의 관리 전용 빌드이며 SDK를 자동 복사하지 않는다. x86/x64 이외의 값은 빌드 오류다.

호출 프로그램에서 `ProjectReference`를 사용하면 앱을 빌드하는 MSBuild 명령에 동일한 속성을 전달한다. 예를 들어 앱의 플랫폼이 x64면 `/p:Platform=x64 /p:Bt003NativePlatform=x64`를 지정한다. SDK 복사는 참조 프로젝트를 거쳐 앱 출력 폴더까지 전달된다. 플랫폼을 명시하지 않은 AnyCPU 실행 프로그램에 SDK 비트 수를 자동 추정하지 않는다.

컴파일된 관리 DLL을 파일 참조로 사용하는 기존 Checkup·SmartCheckup에서는 그 참조만으로 native DLL이 복사되지 않는다. 아래 파일 세 개를 앱의 실행 폴더에 배포한다. 프린터 SDK로 로드할 DLL에는 .NET 참조나 COM 등록을 추가하지 않는다.

## 실행 폴더

```text
Checkup.exe 또는 SmartCheckup.exe
U2BIO.BT003PROLabelPrinting.dll
BlackfishNfcLib.dll
HFModule.dll
```

제조사 DLL 두 개는 **실제로 실행되는 프로세스**와 같은 비트 수의 한 쌍이어야 한다. 관리 DLL은 AnyCPU이므로 실행 프로그램의 x86/x64·Prefer32Bit 설정을 기준으로 선택한다. 서로 다른 비트 수의 파일을 같은 출력 폴더에 함께 넣지 않는다. 이미 사용한 폴더에서 관리 전용 빌드로 전환해도 기존 SDK 사본을 자동 삭제하지 않으므로, 오프라인 검증에는 SDK를 넣지 않은 출력 폴더를 사용한다.

파일 버전·해시·의존성 기준은 [SDK API 대조표](SDK_API_COVERAGE.md)를 따른다. 전역 설치·Registry 등록·검색 경로 변경은 필요하지 않다. 별도 NativeHost도 1차 배포에 포함하지 않는다.

## 검증과 현재 개발 상태

```text
powershell -NoProfile -File tests/VerifySdkAssets.ps1
```

이 검사는 저장소의 네 파일이 고정 제조사 해시와 같은지만 확인하며 SDK를 로드하거나 프린터를 호출하지 않는다. 아키텍처별 복사, 기본 빌드의 SDK 미복사, 잘못된 속성 오류 및 ProjectReference 전달은 별도 MSBuild 구성 검사 대상이다.

현재 #16은 [동기 USB 스펙](specs/SYNC_USB_MVP.md)과 [공개 API](PRINTER_API.ko.md)를 따른다. node tools/verify-build.js가 x86/x64 각각 Bt003NativePlatform 배포 빌드를 수행하고 복사한 두 SDK 파일의 PE·고정 해시를 Validation --inspect-deployment로 검사한다. SDK를 로드하지 않는다. 제품 실행 중 해시 검사는 하지 않으며 DllImport 기본 로딩과 ApplicationDirectory/System32 검색 제한을 사용한다.

관리 대체 harness·제품 DLL 메타데이터 검사와 실제 P/Invoke/물리 출력 확인은 구분한다. 현재 SDK·장치·인쇄·배출 실기는 수행하지 않았다. 이전 비동기/일괄 인쇄 증거는 이전 API의 역사 기록이다.
