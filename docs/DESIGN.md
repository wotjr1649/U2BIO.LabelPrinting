# 동기 USB MVP 설계

이슈 #16의 [확정 스펙](specs/SYNC_USB_MVP.md)이 현재 계약이다. [결정 기록](design/decisions/2026-10-12-sync-usb-mvp.md)에 사용자 선택과 제외 범위를 보존한다. 이전 #15 계약은 [역사 설계](history/DESIGN_wrapper_issue15.md)에 있다.

Bt003Printer.cs는 공개 메서드, 입력 검사, SDK 호출 순서와 자원 수명을 한 파일에서 보여 준다. PrinterModels.cs는 SDK 수준 결과만 담는다. 제조사 Wrapper 선언을 수정하거나 자체 로더·delegate 카탈로그로 감싸지 않는다. Templates의 선택 해석은 기본 인쇄 경로와 독립이다.

PrinterResult는 IsSuccess(bool), Message, NativeCode, PrintInvoked, PrintCode 다섯 속성만 제공한다. 정상 판단 기준은 IsSuccess 하나이며, Message로 SDK 단계와 이동·정리 오류를 한글로 설명한다. Status와 PrinterResultStatus enum은 제거한다. true는 요청한 SDK 처리와 필요한 배출·정리가 정상적으로 끝났다는 뜻이다. 실패와 결과 불명은 모두 false이며 실제 무출력이나 안전한 재인쇄를 뜻하지 않는다. 주 작업의 숫자와 인쇄 응답은 별도로 보존한다. 결과 생성자는 internal이고 디버거에서는 IsSuccess와 Message를 먼저 표시한다. 정리 실패는 IsSuccess=false로 기록하고 원래 숫자·인쇄 증거·설명을 보존하며 Message에 정리 오류를 추가한다. 내부 연결·위치 차단은 그대로 유지한다.

정적 gate 안에서 연결 소유자 한 명만 SDK 전역 상태를 사용한다. 인쇄·이동·finally·해제까지 직렬화한다. 부분 연결 실패는 해제·Closed 확인 후 소유권을 반환한다. 확인되지 않은 해제는 소유권을 유지하고 명시적 재시도를 허용한다.

PrintCore는 연결 확인 → 이전 배출 복귀 → ResetForm → 선택 옵션 → SetPrintFormString → 선택 변수 → StartLabelPrint(1, StringBuilder) → 선택 배출 순서다. PrintFile도 CP949 파일에서 검사한 내용을 CRLF로 정규화해 문자열로 등록하며 AW 해석은 SDK에 맡긴다. 실패하면 다음 인쇄 단계를 호출하지 않는다. finally는 서식을 정리하고 PrintFile은 그 뒤 파일을 닫는다. 정리 오류는 기존 응답을 보존하며 후속 인쇄를 차단한다.

포트별 위치 기록은 객체 수명과 독립이며, 프로그램 재실행을 위해 `C:\Bitek\BT003\position.txt`에도 저장한다. 기록 파일이 없거나 신뢰할 수 없으면 위치 불명으로 시작한다. 인쇄·이동에 진입할 때 불명으로 표시하고 명확한 정상 응답에서만 명령 이력 거리를 기록한다. 불명 상태는 호출자의 수동 정렬 확인으로만 해제한다. 명령 이력은 실제 물리 위치 확인이 아니다.

검사 책임은 세 부분이다. 실제 제품 DLL은 공개 API·원본 선언·검색 제한을 검사한다. 제품 소스를 그대로 링크한 관리 harness는 실패 순서와 수명을 검사한다. verify-build는 배포한 SDK 파일이 고정 제조사 해시와 같은지 검사한다. 어느 것도 실제 SDK 인쇄 성공을 증명하지 않는다.

Print/PrintFile의 선택 인수는 `string variableData = null`이다. 제조사 `!!` 가변데이터 문자열을 기본 검사 후 변경 없이 전달하며 변수 번호 해석은 SDK가 맡는다. 배열/params overload는 제거했고 기존 호출 앱은 새 DLL로 재빌드해야 한다. 가변데이터 전환 당시 Templates는 유지했다. 이후 선택 기능은 [TXT 원문 편집과 저장 계약](specs/TXT_TEMPLATE_EDITOR.md)으로 재설계하며 기본 인쇄 경로는 유지한다.
