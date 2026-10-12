# 동기 USB MVP 구현 계획

이슈 #16의 [스펙](specs/SYNC_USB_MVP.md)과 [공개 API](PRINTER_API.ko.md)를 따른다. #15의 C01-C24/TASK-01..09는 [역사 계획](history/IMPLEMENTATION_PLAN_wrapper_issue15.md)에 보존한다.

1. 사용자 결정을 스펙·설계 기록으로 작성하고 오른쪽 Codex의 적대적 검토에서 구현 차단 결함을 해결한다. 실제 제품 DLL의 새 계약 검사를 먼저 작성하여 기존 제품에서 실패를 확인한다.
2. 제품을 두 파일로 구현하고 제조사 원본 Wrapper를 직접 호출한다. 관리 harness는 제품 두 소스를 Link로 컴파일하여 연결 소유권, 변수 복제, 등록 실패 중단, Unknown/cleanup 보존, 이동 실패, 파일 잠금, Dispose 대기를 확인한다.
3. 실제 Validation의 계약 검사와 Templates 검사를 연결하고 이전 보호/진단 검사는 LegacySdk로 분리한다. 오프라인 예제·현재 문서·배포 검사 경로를 이행한다.
4. Release x86/x64 빌드와 관리 검사, Bt003NativePlatform 배포 빌드·PE·원본 해시, 문서·diff 검사를 수행한다. 별도 문맥 검증과 오른쪽 Codex 최종 검토를 받으며 발견된 결함은 수정 후 관련 검사를 다시 수행한다.

수용 기준은 이슈 #16의 일곱 항목이다. SDK 실기·물리 출력·배출 정확성, 외부 앱의 고급 API 이행, 여러 프로세스 제어는 이번 완료 근거에 포함하지 않는다. push·merge·배포는 수행하지 않는다.
