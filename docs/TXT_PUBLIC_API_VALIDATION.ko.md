# 연결 상태 수정과 TXT 공개 API 검증 — 2026-10-12

추적: [#14](https://github.com/wotjr1649/U2BIO.LabelPrinting/issues/14), 넓은 실기 범위는 [#11](https://github.com/wotjr1649/U2BIO.LabelPrinting/issues/11)이다. 제품 revision은 `90b3f1c418e271437280f37edd06ae9f262d50d9`다. 사용자 솔루션 메타데이터와 세 샘플의 E15 제거 변경은 보존했으며 작업 커밋에 포함하지 않는다.

## 연결 결함과 관리 검사

첫 행 연결 확인에서 예외가 발생하면 서식 초기화 전에 중단되어 이전 Connected/Open이 남던 결함을 공유 Status 경로에서 수정했다. Faulted/Unknown·nativeCode=null·CleanupCode=NativeCallFailed를 보존한다. 다른 인스턴스 점유 및 새 Print를 거부하고 명시 Disconnect 후 재연결을 허용한다. 성공하는 유휴 상태 조회로 Faulted를 자동 복구하지 않는다. timeout/Dispose 처리와 소유권 계약은 유지한다.

`56e0768`의 새 회귀는 수정 전 x86에서 실패했다. 첫 잠금 명령은 DEVFLOW_ROOT 누락으로 저장소 검사에서 멈췄으므로 그 실행은 재현 증거가 아니다. 설치된 플러그인 경로를 지정한 재실행이 실제 TXT 회귀에 도달해 실패했다. x64도 고정된 수정 전 DLL 스냅샷과 동일 회귀 실행 파일의 별도 관리 시험에서 실패를 확인했다. 수정 후 독립 verifier의 `node tools/verify-build.js --scope txt`는 Release x86/x64·.NET Framework4.6.1·관리 DLL AnyCPU에서 통과했다. native SDK는 로드하지 않았다.

독립 검증 증거는 `.work/foundation/Release-1791772045880/evidence.json`, x64 실패 대조는 `.work/public-txt14/red-x64/evidence.json`이다. 제품 DLL SHA-256은 `046b2085db6e680d5e4a27014657af30f7f8d7acc4b96a6cc2be637a680b25a9`다. dirty 기록은 보존한 사용자 변경을 포함하므로 HEAD의 모든 파일이 깨끗했던 검사로 설명하지 않는다. 독립 코드 리뷰에서 추가 제품 결함은 보고되지 않았다.

## 실제 TXT 시험 조건

USB 대상은 명시 `0`이다. 사용자 확인으로 두 케이블과 흰 라벨60×20mm·피치40mm·뒷종이70mm·Black-Mark5mm·마크 앞뒤7.5mm를 사용했다. Windows에서 COM3와 BT-003 Pro가 나타났으나 COM3를 Usb 인수로 자동 지정하지 않는다. 펌웨어는 미확인이다. 고정 x64 SDK 쌍을 해시 검사 후 작업 폴더에서 사용했다. RFID·DB API와 원문 직접 인쇄는 호출하지 않았다. 사용자가 이번 시험의 SDK 로그 추가·현재 USB1 캐시 갱신을 허용했다. 원문 로그·UID·응답 문자열은 검증 결과에 저장하지 않는다.

TXT는 CP949이고 첫 AW는 X80/Y120, 두 번째는 X80/Y160, 두 행 모두 size10/weight1/rotation0이다. 사용자가 제거한 E15를 샘플에 되살리지 않았다. 공개 BtplTemplate.Load → ValidateForPrint → Print 경로를 사용하며 ToBtpl의 기존 정규화는 누락된 E에 E0을 넣는다. 배출15mm는 호출자의 연결 옵션이다.

| 시험 | 공개 API/SDK 결과 | 사용자 물리 관측 | 판정 |
| --- | --- | --- | --- |
| TXT·배출15mm·한 장 요청 | Connect/상태/사전검사 정상, StartLabelPrint1회·응답0, 자동 전진 명령0, CleanupNone | 배출 정상, 흰 라벨과 뒷종이 모두 잉크 없음, FEED 정상 | 인쇄 실패. 응답0을 실제 성공으로 판정하지 않음 |
| 같은 TXT·배출0mm·비교 한 장 | StartLabelPrint1회·응답0, SendData 이송 없음, CleanupNone | 확인 대기 | 물리 성공 미판정 |

첫 시험 뒤 두 번째 인쇄는 중단했다. Disconnect → Closed/ResourcesReleased → 재연결 → 최종 Disconnect → Dispose를 정상 확인했으며 해제/폐기 자체는 이송하지 않았다. 첫 결과의15mm 복귀 기록을 연결 해제·재연결 중 보존했다. 첫 시험 증거는 `.work/public-txt14/x64-atomic/result.json`이다. 프로세스 종료 후 물리 위치와 메모리 기록의 연결은 자동 복원하지 않으며 사용자가 FEED로 다음 비교의 기준 위치를 준비했다.

첫 제어 도구의 stdin 사용 불가와 파일 잠금 실패는 Print 명령 전달 전에 발생했다. 시험 프로세스 종료를 확인한 뒤 새 도구에서 완성된 제어 파일을 원자적으로 전달했다. 도구는 두 개 명령만 전달하고, 인쇄 실패 뒤 추가 Print를 하지 않는다. VM 관리 자체 검사와 별도 verifier가 제어 상한·잘못된 입력·읽기 실패 중단을 확인했다. 이 VM 시험은 native/물리 인쇄의 증거가 아니다.

## 남은 범위

TXT 문구 미출력의 원인은 미확정이며 자동 전진0 비교 관측으로 다음 가설을 구분한다. 다음 출력 전 B15 복귀·반복 위치, x86 공개 API 실기, 실제 오류/취소/timeout·다건·다른 transport·장치/용지/글꼴은 아직 미검증이다. #11 전체를 이번 일부 시험으로 완료하지 않는다. JSON/XML 구현과 기본 전체 검사 명령은 유지하되 이번 형식별 테스트·검수·검증은 사용자 지시에 따라 DEFERRED다. 이전 검사 기록은 과거 증거로 유지한다.

## 비교 시험의 정리와 분석 한계

배출0mm 비교도 입력 대기5분 뒤 두 번째 Print 없이 정상 Disconnect·Closed/ResourcesReleased·재연결·최종 해제·Dispose를 확인했다. 증거는 `.work/public-txt14/x64-nomove/result.json`이다. 실제 문구 여부는 여전히 사용자 관측 대기이며 종료 코드0은 연결/명령/정리 결과일 뿐 물리 인쇄 성공이 아니다.

호스트는 `Microsoft Windows NT 10.0.26200.0`이다. 실제 장치 사전검사는 .NET Framework의 ANSI code page949 요구를 통과했다. 이번 실제 실행은 x64이고 x86은 관리 검사까지만 확인했다.

SDK 정적 분석에서 원문 E의 첫 값이 object+0x1E8에 저장되고 모델0x33의 전송 행은 E0,0으로 재작성됨을 다시 확인했다. 따라서 E15가 모든 내부 처리에서 무효라고 단정하지 않는다. 확인한 AW 영역에서 그 필드의 직접 읽기와 미출력의 연결은 찾지 못했으며 간접/전체 데이터 흐름은 미확정이다. E 삭제의 원인 판정이나 샘플 복원을 근거 없이 진행하지 않았다. 다음 판별은 배출0mm의 물리 관측을 먼저 반영하고 필요한 경우 두 번째 AW 제거 등 한 변수만 바꾼 시험으로 좁힌다.
