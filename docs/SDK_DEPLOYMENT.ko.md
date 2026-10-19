# DLL·Tester 실행 환경과 배포

대상 프로세스와 같은 비트수의 제조사 런타임을 준비한다. 소스 빌드·검사는 [개발 안내](DEVELOPMENT.md#sdk-배포용-빌드), 사용 순서는 [DLL API](PRINTER_API.ko.md)와 [Tester 안내](PRINT_PRESENTATION.ko.md#tester-사용)를 따른다. 아래는 배포 구성 정책이며 자동 패키지 생성이나 배포 완료 기록이 아니다.

## 실행 환경

.NET Framework 4.6.1을 대상으로 한 Windows 프로그램이다. 호환되는 .NET Framework 런타임과 ANSI 코드 페이지 949 환경을 사용한다. 인쇄 서식은 CP949로 준비한다.

관리 DLL은 AnyCPU다. 제조사 `BlackfishNfcLib.dll`과 `HFModule.dll`은 **실제로 실행되는 프로세스**와 같은 x86 또는 x64 한 쌍이어야 한다. 호출 프로그램의 PlatformTarget·Prefer32Bit를 확인하며 AnyCPU라는 이름으로 native 비트수를 추정하지 않는다. 서로 다른 비트수의 파일을 같은 폴더에 혼합하지 않는다.

## DLL 호출 프로그램의 실행 폴더

```text
Caller.exe
U2BIO.BT003PROLabelPrinting.dll
BlackfishNfcLib.dll
HFModule.dll
```

제품 DLL 세 파일을 호출 프로그램 실행 폴더에 둔다. `HFModule.dll`도 필수 native 의존성이다. 관리 DLL을 파일 참조한 것만으로 native 파일이 자동 복사되지는 않는다. 제조사 파일을 .NET 참조로 추가하거나 COM 등록·바이너리 포함/추출·전역 검색 경로 변경을 하지 않는다.

기존 앱의 재빌드는 [API 이행 안내](PRINTER_API.ko.md#제조사-방식의-가변데이터와-이행)를 따른다. Checkup·SmartCheckup 실제 적용은 상사 승인 대기이며 이 파일 목록이 업무 적용을 승인하거나 완료했다는 뜻은 아니다.

## Tester 실행 폴더

같은 비트수의 검증한 Tester 빌드 출력에서 다음 런타임 파일을 함께 제공한다.

| 파일 | 용도 |
| --- | --- |
| `U2BIO.BT003PROLabelPrinting.Tester.exe` | Tester 실행 |
| `U2BIO.BT003PROLabelPrinting.dll` | 공통 인쇄·TXT API |
| `BlackfishNfcLib.dll`, `HFModule.dll` | 해당 Tester 프로세스 비트수의 SDK 쌍 |
| 출력에 복사된 `DevExpress.*.v20.2.dll` | Tester가 사용하는 DevExpress 20.2.6 구성요소 |

DevExpress는 Tester에만 필요하며 제품 DLL의 의존성이 아니다. DLL용 세 파일만으로 Tester를 배포하지 않는다. 빌드 출력의 참조 DLL을 누락하지 않고 [Tester 사용 순서](PRINT_PRESENTATION.ko.md#tester-사용)로 실행한다. 사용자 설정은 사용자 로컬 앱 데이터의 `U2BIO/BT003Tester/settings.xml`에 별도로 생성되므로 개발 PC의 개인 설정을 배포하지 않는다.

## 제품 배포에서 제외하는 자료

`tests/`의 소스·검증 exe, `tools/`, AGENTS와 설계·계획·과거 근거 문서는 제품 실행 배포 대상에서 제외한다. 사용자 안내와 필요한 TXT 샘플만 선정해 제공하며 개인 수정 샘플이나 설정을 자동으로 복사하지 않는다.

검증 프로젝트와 예제는 **개발 소스 저장소에 유지**한다. 이 정책은 소스 삭제·Git 추적 해제·검사 생략이나 GitHub 비공개 전환이 아니다. GitHub 소스 다운로드 zip도 제품 배포물과 다르다. 패키징 자동화는 구현하지 않는다.

## 배치 오류 확인

- SDK 누락·비트수 불일치·함수 누락이면 결과의 `IsSuccess`와 `Message`를 확인하고 실행 프로세스 및 DLL 한 쌍을 점검한다. 인쇄 호출 이후 실패라면 자동 재인쇄하지 않는다.
- DevExpress 어셈블리 로드 오류이면 Tester 출력의 필요한 참조 DLL이 함께 있는지 확인한다. 전체 소스 빌드에는 DevExpress 20.2.6이 설치된 개발 PC가 필요하다.
- ANSI 코드 페이지 또는 파일 인코딩 오류는 지원 환경·CP949 서식을 확인한다. 화면 글꼴이나 미리보기 보정으로 인코딩 오류를 해결하지 않는다.
- 연결 오류와 용지 위치 불명은 [공통 복구](PRINT_PRESENTATION.ko.md#공통-복구)에 따라 구분한다. 파일을 올바르게 배치한 것만으로 물리 정렬이 확인되지는 않는다.

## 파일 출처와 검증 범위

고정 제조사 원본은 제품의 `Sdk/x86`과 `Sdk/x64`에 있다. 제공 SDK의 `라이브러리/x86/lib`, `라이브러리/x64/lib`에서 가져온 Blackfish/HFModule 쌍이며 두 Blackfish의 버전 표기는 2.1.0.0이다. 같은 버전 번호라도 다른 해시의 파일과 혼합하지 않는다. HFSample.exe·샘플 설정/로그·C++ 링크용 .lib는 .NET 실행 배포 대상이 아니다.

| 비트수 | 파일 | SHA-256 |
| --- | --- | --- |
| x86 | `BlackfishNfcLib.dll` | `fed09bae47835dcf78f58579e3cfb9382561799b369e54fd9fa0902e5b89dbf5` |
| x86 | `HFModule.dll` | `bb2721801bc853ead75e647608c6b36a5a3a75f344095e4e29ffc35e822319d4` |
| x64 | `BlackfishNfcLib.dll` | `64c7874980cb0a774a766e299359bc9cba5dd66fd052d758f1d4d1678d8cf72c` |
| x64 | `HFModule.dll` | `542ebfbccca1f953b4dcafb2a1faed198707d8fbcdb0653c0fc121d3c273edf5` |

`Sdk/Interop/BT003CtrlWrapper.cs`는 제조사 HFSample_UL의 원본 39개 P/Invoke 선언을 보존한다. StdCall과 원래 DLL 이름·문자열 마샬링·입출력 방향을 유지하며 LPCTSTR/LPTSTR 표기만으로 Unicode로 바꾸지 않는다. USHORT는 ushort, BYTE는 byte, Windows의 int/long은 32-bit이므로 .NET Int64로 바꾸지 않는다. StartLabelPrint는 원본 StringBuilder(100) 선언을 사용하며 SizeConst=100은 native 출력 안전성 보장이 아니다.

제품 실행은 .NET 기본 DllImport와 ApplicationDirectory/System32 검색 제한을 사용하고 실행 시 해시·기존 모듈 충돌 검사를 하지 않는다. SDK 최초 호출은 연결 시점이며 오프라인 편집·미리보기는 SDK를 로드하지 않는다. Wrapper에 RFID·보안 선언이 있어도 공개 제품 기능이나 인쇄 흐름에서 사용한다는 뜻은 아니다. 원형 미확정 export나 제공 DLL에 없는 함수를 추측하여 추가하지 않는다. 현재 인쇄·반환 처리 계약은 [DLL API](PRINTER_API.ko.md)에 있다.

`node tools/verify-build.js`는 플랫폼별 배포 파일과 고정 제조사 해시를 대조하며 SDK를 로드하지 않는다. 관리 검증과 SDK/물리 성공은 다르며, 확인된 용지·위치·배출 범위는 [운영 기준](PRINT_PRESENTATION.ko.md#운영-기준)에 있다.
