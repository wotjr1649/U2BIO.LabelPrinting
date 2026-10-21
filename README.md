# U2BIO.LabelPrinting

BT-003 Pro의 텍스트 라벨 인쇄용 .NET Framework 4.6.1 DLL과 WinForms 서식 Tester입니다. DLL은 동기 USB 연결·인쇄·배출·해제를 제공하고, Tester는 DB 없이 TXT 서식을 편집하고 실제 출력을 확인하는 도구입니다.

## 무엇부터 읽으면 되나요?

| 독자 | 읽는 순서 |
| --- | --- |
| DLL 호출 개발자 | [설치·배포](docs/SDK_DEPLOYMENT.ko.md) → [DLL API·함수별 파라미터와 예제](docs/PRINTER_API_V2.ko.md) → 인쇄 전 [공통 복구·용지 운영](docs/PRINT_PRESENTATION.ko.md#공통-복구) |
| Tester 사용자 | [Tester 사용 순서](docs/PRINT_PRESENTATION.ko.md#tester-사용) → 용지·위치가 달라지면 같은 문서의 [운영 기준](docs/PRINT_PRESENTATION.ko.md#운영-기준) |
| 설치·배포 담당자 | [실행 환경과 DLL·Tester 배포 파일](docs/SDK_DEPLOYMENT.ko.md) |
| 프로그램에서 TXT를 편집하는 개발자 | 기본 인쇄 안내를 읽은 뒤 [선택 TXT 편집 API](docs/BTPL_TEMPLATE_API.ko.md) |
| 소스를 빌드·검증하는 유지보수자 | [개발 환경과 검증](docs/DEVELOPMENT.md) |

안내는 사용자 안내 **6개**(이 README, 현행 DLL API, 보존 API, Tester/운영, 배포, 선택 TXT 편집)와 개발 안내 **1개**입니다. 별도의 저장소 운영 파일 3개를 포함해 현재 추적하는 Markdown은 **총 10개**입니다. [기존 API 안내](docs/PRINTER_API.ko.md)는 내용 그대로 보존하며, 기본 개발자 진입점은 위 표의 DLL API입니다.

## 지원 범위와 결과 해석

- DLL은 .NET Framework 4.6.1/AnyCPU이며, 실행 프로세스와 맞는 x86 또는 x64 제조사 DLL 두 개가 필요합니다. Windows ANSI 코드 페이지 949와 CP949 인쇄 서식을 사용합니다.
- 기본 순서는 `ConnectUsb → 필요한 옵션 설정 → Print/PrintFile → Disconnect`입니다. 처음 실행하거나 용지 위치가 불명확하면 [공통 복구 절차](docs/PRINT_PRESENTATION.ko.md#공통-복구)로 정렬부터 확인합니다.
- 정상 여부는 `PrinterResult.IsSuccess`, 설명은 `Message`로 확인합니다. SDK 정상 응답은 물리 출력 완료 증거가 아니며, 실패·불명이어도 일부 출력됐을 수 있으므로 자동 재인쇄하지 않습니다.
- Tester는 같은 서식·가변데이터를 요청 장수 1~20으로 순차 시험합니다. 반복 중단은 현재 SDK 호출 반환 후 적용되며 강제 취소·대량 일괄 인쇄 API는 제공하지 않습니다.
- 미리보기는 배치·잉크 경고를 위한 진단 표시입니다. 실제 잘린 글자 모양과 출력 일치를 보장하지 않습니다. 배출 15 mm와 실측 보정은 확인한 운영 용지의 값이며 다른 용지에 일반화하지 않습니다.
- Checkup·SmartCheckup은 합성 데이터의 호출 예제입니다. 실제 업무 프로그램의 통합·운영 검증 완료를 뜻하지 않습니다.

## 개발·검증 자료

소스를 수정하거나 검증하려면 [개발 안내](docs/DEVELOPMENT.md)를 시작점으로 사용합니다. 현행 인쇄·TXT 계약은 해당 API 안내에, SDK 출처와 고정 해시는 배포 안내에 통합했습니다. 현재 사용법과 알려진 제한은 이 안내와 연결된 내부 문서에서 확인할 수 있습니다. 과거 기록을 현재 구현의 완료 증거로 해석하지 않습니다.

`tests/`(OfflineSmoke 포함)는 개발 검증에 사용하므로 소스 저장소에 유지합니다. 제품 배포에는 포함하지 않는 정책이며, 배포 대상과 [개발 검사](docs/DEVELOPMENT.md#dll-기반-빌드와-실행-검사)를 구분합니다. 개발 자료를 필독에서 제외해도 소스 추적·보존 정책은 바뀌지 않습니다.
