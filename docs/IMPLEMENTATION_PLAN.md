# 이슈16 구현 계획

기본 인쇄는 [동기 USB MVP](specs/SYNC_USB_MVP.md), 선택 편집은 [TXT 편집 계약](specs/TXT_TEMPLATE_EDITOR.md)을 따른다. 구현 순서와 실제 파일/검사 경계는 비공개 작업 계획에 기록하고 완료 증거는 이슈 체크포인트에 남긴다.

기존 작업1~6은 동기 USB API, 다섯 속성 bool 결과, 제조사 Wrapper 유지, Templates 기능 보존 통합, raw 가변데이터 문자열 API 전환이다. 현재 후속 작업의 사용자 결정이 이전 Templates 기능/API 보존 조건을 대체한다. 기본 인쇄/파일 잠금/배출/복귀와 Wrapper·PrinterModels·Bt003Printer는 유지한다.

## TXT 편집 후속 작업

7. right pane 설계/문서 검토 후 Issue를 갱신한다. 원문 보존·AW 필드 편집·WithRawText·SaveAs의 새 계약을 baseline에서 실패시키고 관리 회귀를 잠근다.
8. Templates를 두 파일로 재설계한다. 원문·줄끝·변경 필드만 보존하고 작은 AW/Q/W/S/H 모델을 제공한다. 저장 전에 모든 준비를 끝내고 임시 파일 후 명시적인 Move/Replace로 확정한다.
9. JSON/XML·DataTable·렌더 결과와 관련 Compile/참조/활성 검사를 제거하고 예제·현재 문서를 이행한다. Release x86/x64 실제 관리 DLL·기존 SDK 대체 harness·OfflineSmoke·배포 PE/해시·저장소 검사를 실행한다. right pane 코드 리뷰와 새 문맥 실행 검증을 받고 최종 근거를 기록한다.

가장 큰 위험은 원문 손실과 저장 실패 뒤 파일 기준 불일치다. 실제 임시 파일을 사용하는 관리 검사로 확인한다. 테스트는 실제 동작을 확인하며 기존 printer 행동 검사를 약화하지 않는다. UI 구현·SDK 로드·USB·물리 인쇄/배출·push/merge는 이번 범위에 없다. 한글 주석으로 원문·저장 수명과 실패 경계를 설명한다.
