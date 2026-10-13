# 동기 USB MVP 구현 계획

이슈 #16의 [스펙](specs/SYNC_USB_MVP.md)과 [공개 API](PRINTER_API.ko.md)를 따른다. #15의 C01-C24/TASK-01..09는 [역사 계획](history/IMPLEMENTATION_PLAN_wrapper_issue15.md)에 보존한다.

1. 사용자 결정을 스펙·설계 기록으로 작성하고 오른쪽 Codex의 적대적 검토에서 구현 차단 결함을 해결한다. 실제 제품 DLL의 새 계약 검사를 먼저 작성하여 기존 제품에서 실패를 확인한다.
2. 제품을 두 파일로 구현하고 제조사 원본 Wrapper를 직접 호출한다. 관리 harness는 제품 두 소스를 Link로 컴파일하여 연결 소유권, 변수 복제, 등록 실패 중단, Unknown/cleanup 보존, 이동 실패, 파일 잠금, Dispose 대기를 확인한다.
3. 실제 Validation의 계약 검사와 Templates 검사를 연결하고 이전 보호/진단 검사는 LegacySdk로 분리한다. 오프라인 예제·현재 문서·배포 검사 경로를 이행한다.
4. Release x86/x64 빌드와 관리 검사, Bt003NativePlatform 배포 빌드·PE·원본 해시, 문서·diff 검사를 수행한다. 별도 문맥 검증과 오른쪽 Codex 최종 검토를 받으며 발견된 결함은 수정 후 관련 검사를 다시 수행한다.

2026-10-14 후속 작업은 PrinterResult를 다섯 속성으로 줄이는 공개 계약 이행이다. 사용자가 승인한 Status 단일 판단·한글 Message 설계는 right pane의 적대적 검토에서 READY다. 정확한 속성 집합·자료형·internal 생성자/setter·DebuggerDisplay 계약 검사를 먼저 기존 제품에서 실패시키고 제품 두 파일을 수정한다. 기존 harness의 결과 접근과 OfflineSmoke를 Status 비교로 이행하고 인쇄 정상/실패/Unknown에 정리 오류가 겹치는 경우, 배출 실패/예외, 연결 실패+해제 실패의 숫자·설명·후속 호출 차단을 확인한다. README·현재 API/배출 문서와 tools/verify-repository.js의 필수 계약 문자열도 이행하며 과거 기록은 보존한다.

전체 저장소 문서 검사에는 수정 전부터 Wrapper와 별도 참고 자료의 오류 18건이 있다. Wrapper 바이트를 유지하면서 새 실패가 없는지 대조하고 직접 실행한 x86/x64 관리·배포 파일 검증과 전체 검사 실패를 구분해 기록한다. 전체 문서 검사 통과 조건을 완료로 표시하지 않으며 장치 실행은 수행하지 않는다.

수용 기준은 이슈 #16의 일곱 항목이다. SDK 실기·물리 출력·배출 정확성, 외부 앱의 고급 API 이행, 여러 프로세스 제어는 이번 완료 근거에 포함하지 않는다. push·merge·배포는 수행하지 않는다.
