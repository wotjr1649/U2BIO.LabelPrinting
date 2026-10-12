# 제조사 SDK 런타임

이 폴더는 공통 모듈의 빌드·배포에 필요한 제조사 DLL 원본 사본을 보관한다. `Interop/BT003CtrlWrapper.cs`는 HFSample_UL의 원본 선언이며 namespace와39개 API·마샬링을 보존한다. 자체 정책은 이 파일에 넣지 않는다. 제조사 DLL을 .NET 참조로 추가하거나 수정하지 않는다.

| 폴더 | 파일 | 용도 |
| --- | --- | --- |
| `x86/` | `BlackfishNfcLib.dll`, `HFModule.dll` | 32비트 호출 프로그램 |
| `x64/` | `BlackfishNfcLib.dll`, `HFModule.dll` | 64비트 호출 프로그램 |

`BlackfishNfcLib.dll`은 내부적으로 `HFModule.dll`을 사용한다. `HFModule.dll`은 샘플 프로그램이 아니라 필수 native 의존성이다. `HFSample.exe`, 샘플의 빌드 산출물·설정·로그와 C++ 링크용 `.lib`는 .NET 실행 배포에 필요하지 않다. 제조사 문서·헤더·샘플은 별도 참고 자료이며 SDK 전체의 수정된 폴더를 실행 배포에 포함하지 않는다.

두 Blackfish 파일의 버전은 2.1.0.0이다. 각 사본의 SHA-256은 [SDK API 대조표](../../../docs/SDK_API_COVERAGE.md)의 고정 쌍과 같다. 출처는 제공 SDK의 `라이브러리/x86/lib`와 `라이브러리/x64/lib`다. 원본 파일은 변경하지 않았다.

Visual Studio 프로젝트에 네 DLL을 표시하되 기본 빌드에는 복사하지 않는다. 실제 앱 배포를 준비할 때 `Bt003NativePlatform=x86` 또는 `x64`를 지정하면 해당 두 파일만 관리 DLL과 같은 출력 폴더에 복사한다. 관리 DLL 자체는 AnyCPU다. [빌드·배포 방법](../../../docs/SDK_DEPLOYMENT.ko.md)을 따른다.

사본 준비는 제품 연결·인쇄 API 구현 완료나 실기 지원 확대를 뜻하지 않는다. SDK 로드는 연결 시점으로 지연하고, 오프라인 템플릿 편집·검사는 SDK 없이 동작한다. 임의 버전 교체·다른 해시의 파일 혼합은 지원하지 않는다.

현재 #16 제품은 제조사 원본 StartLabelPrint의 StringBuilder 선언을 사용한다. 자체 로더·실행 시 해시·포인터 bridge는 제거했으며 고정 해시와 PE 검사는 Validation에서만 수행한다. [동기 MVP 스펙](../../../docs/specs/SYNC_USB_MVP.md)을 따른다.
