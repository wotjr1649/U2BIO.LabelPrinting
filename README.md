# U2BIO.LabelPrinting

BT-003 Pro의 텍스트 라벨 인쇄용 .NET Framework 4.6.1 DLL과 WinForms 서식 Tester입니다. DLL은 동기 USB 연결·인쇄·배출·해제를 제공하고, Tester는 DB 없이 TXT 서식을 편집하고 실제 출력을 확인하는 도구입니다.

## 무엇부터 읽으면 되나요?

| 독자 | 읽는 순서 |
| --- | --- |
| DLL 호출 개발자 | [설치·배포](docs/SDK_DEPLOYMENT.ko.md) → [DLL API와 기본 예제](docs/PRINTER_API.ko.md) → 인쇄 전 [공통 복구·용지 운영](docs/PRINT_PRESENTATION.ko.md#공통-복구) |
| Tester 사용자 | [Tester 사용 순서](docs/PRINT_PRESENTATION.ko.md#tester-사용) → 용지·위치가 달라지면 같은 문서의 [운영 기준](docs/PRINT_PRESENTATION.ko.md#운영-기준) |
| 설치·배포 담당자 | [실행 환경과 DLL·Tester 배포 파일](docs/SDK_DEPLOYMENT.ko.md) |
| 프로그램에서 TXT를 편집하는 개발자 | 기본 인쇄 안내를 읽은 뒤 [선택 TXT 편집 API](docs/BTPL_TEMPLATE_API.ko.md) |

사용자 안내는 이 README와 위의 API·Tester/운영·배포·선택 TXT 편집 안내 **5개**입니다. 나머지는 구현이나 과거 근거를 확인할 때 읽는 개발 자료입니다.

## 지원 범위와 결과 해석

- DLL은 .NET Framework 4.6.1/AnyCPU이며, 실행 프로세스와 맞는 x86 또는 x64 제조사 DLL 두 개가 필요합니다. Windows ANSI 코드 페이지 949와 CP949 인쇄 서식을 사용합니다.
- 기본 순서는 `ConnectUsb → 필요한 옵션 설정 → Print/PrintFile → Disconnect`입니다. 처음 실행하거나 용지 위치가 불명확하면 [공통 복구 절차](docs/PRINT_PRESENTATION.ko.md#공통-복구)로 정렬부터 확인합니다.
- 정상 여부는 `PrinterResult.IsSuccess`, 설명은 `Message`로 확인합니다. SDK 정상 응답은 물리 출력 완료 증거가 아니며, 실패·불명이어도 일부 출력됐을 수 있으므로 자동 재인쇄하지 않습니다.
- Tester는 같은 서식·가변데이터를 요청 장수 1~20으로 순차 시험합니다. 반복 중단은 현재 SDK 호출 반환 후 적용되며 강제 취소·대량 일괄 인쇄 API는 제공하지 않습니다.
- 미리보기는 배치·잉크 경고를 위한 진단 표시입니다. 실제 잘린 글자 모양과 출력 일치를 보장하지 않습니다. 배출 15 mm와 실측 보정은 확인한 운영 용지의 값이며 다른 용지에 일반화하지 않습니다.
- Checkup·SmartCheckup의 실제 업무 프로그램 적용은 상사 승인 대기이며 진행하지 않았습니다. 저장소의 합성 데이터 호출 예제는 업무 적용 완료를 뜻하지 않습니다.

## 개발·검증 자료

소스를 수정하거나 검증하려면 [개발 안내](docs/DEVELOPMENT.md)를 시작점으로 사용합니다. 현재 인쇄 계약은 [SYNC_USB_MVP](docs/specs/SYNC_USB_MVP.md), 내부 구조는 [DESIGN](docs/DESIGN.md)에 있습니다. `docs/specs/`, `docs/design/decisions/`, `docs/history/`와 기준·계획 문서는 유지보수 참고이며 사용자 필독이 아닙니다.

`tests/`와 `examples/OfflineSmoke/`는 개발 검증에 사용하므로 소스 저장소에 유지합니다. 제품 배포에는 포함하지 않는 정책이며, 배포 대상과 [개발 검사](docs/DEVELOPMENT.md#dll-기반-빌드와-실행-검사)를 구분합니다. 개발 자료를 필독에서 제외해도 GitHub 추적·공개 상태는 바뀌지 않습니다.
