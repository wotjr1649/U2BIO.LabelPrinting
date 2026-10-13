# 동기 USB MVP 구현 계획

이슈 #16의 [스펙](specs/SYNC_USB_MVP.md)과 [공개 API](PRINTER_API.ko.md)를 따른다. #15의 C01-C24/TASK-01..09는 [역사 계획](history/IMPLEMENTATION_PLAN_wrapper_issue15.md)에 보존한다.

1. 사용자 결정을 스펙·설계 기록으로 작성하고 오른쪽 Codex의 적대적 검토에서 구현 차단 결함을 해결한다. 실제 제품 DLL의 새 계약 검사를 먼저 작성하여 기존 제품에서 실패를 확인한다.
2. 제품을 두 파일로 구현하고 제조사 원본 Wrapper를 직접 호출한다. 관리 harness는 제품 두 소스를 Link로 컴파일하여 연결 소유권, 변수 복제, 등록 실패 중단, 불명 응답/정리 오류의 정보 보존, 이동 실패, 파일 잠금, Dispose 대기를 확인한다.
3. 실제 Validation의 계약 검사와 Templates 검사를 연결하고 이전 보호/진단 검사는 LegacySdk로 분리한다. 오프라인 예제·현재 문서·배포 검사 경로를 이행한다.
4. Release x86/x64 빌드와 관리 검사, Bt003NativePlatform 배포 빌드·PE·원본 해시, 문서·diff 검사를 수행한다. 별도 문맥 검증과 오른쪽 Codex 최종 검토를 받으며 발견된 결함은 수정 후 관련 검사를 다시 수행한다.

2026-10-14 후속 작업은 다섯 속성의 정상 판단 기준을 bool IsSuccess로 통일하는 공개 계약 이행이다. 사용자가 승인한 모델은 right pane 설계 검토에서 READY다. IsSuccess/Message/NativeCode/PrintInvoked/PrintCode의 정확한 집합·자료형, Status와 PrinterResultStatus enum 제거, internal 생성자/setter, bool 기본값 false와 DebuggerDisplay를 검사한다. 새 계약 검사를 기존 제품에서 먼저 실패시키고 제품 두 파일을 수정한다. 기존 harness와 OfflineSmoke는 bool 분기로 이행한다. 정리 예외가 없는 불명 응답도 false인지 확인하고, 정상/실패/불명 인쇄 뒤 정리 오류, 배출 실패/예외, 연결 실패+해제 실패에서 숫자·설명·인쇄 증거·후속 호출 차단을 보존한다. README·현재 API/배출 문서와 tools/verify-repository.js의 필수 계약 문자열도 이행하며 과거 기록은 보존한다.

기존 전체 저장소 오류 18건은 참고 패키지의 작업 보관 위치 이동, Validation 작업 파일의 LF 정리와 제조사 Wrapper 예제 경로의 파일별 허용 설정으로 해소했다. 예제 경로는 현재 머신에 존재할 필요가 없는 참고 자료이며 제품 실행 입력과 구분한다. Wrapper 바이트와 제품 코드는 유지했고 전체 verify-build의 Release x86/x64 관리·배포 파일 검사와 저장소 검사가 통과했다. 판정 범위는 현재 제품이 사용하는 SDK 함수 10개와 bool 결과 계약이며 전체 SDK 기능의 실기 완료를 요구하지 않는다. SDK·장치 실행은 추가 수행하지 않는다.

수용 기준은 이슈 #16의 일곱 항목이다. SDK 실기·물리 출력·배출 정확성, 외부 앱의 고급 API 이행, 여러 프로세스 제어는 이번 완료 근거에 포함하지 않는다. push·merge·배포는 수행하지 않는다.
