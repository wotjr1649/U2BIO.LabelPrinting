# SDK 가변데이터 문자열을 공개 인수로 사용

상태: 사용자 승인 및 right pane 설계 READY. [동기 USB 스펙](../../specs/SYNC_USB_MVP.md)의 후속 결정이다.

제조사 SetPrintFormVariableData는 !!로 구분한 문자열을 받고 V00부터 번호로 매핑한다. 이전 params string[] API는 완성 문자열의 !!를 거부해 제조사 예제를 다시 나누도록 요구했다. 사용자는 제조사 Wrapper 기반의 작은 API와 쉬운 유지보수를 위해 문자열 방식으로 통일하도록 승인했다. Templates는 유지한다.

Print/PrintFile의 두 번째 인수는 string variableData=null이다. 배열 복사·항목별 합산·재조합과 배열용 구분자 충돌 규칙을 제거한다. null은 변수 없는 서식의 등록 생략이다. non-null 원문은 ANSI949/CP949·NUL·제어문자·4 MiB 기본 검사 후 SDK에 그대로 전달한다. 자체 변수 번호 파서·빈 항목 보정·이스케이프를 추가하지 않는다.

배열을 유지하면 제조사 입력을 분해해야 한다. raw overload를 함께 제공하면 호출·검증·null 선택의 계약이 늘어난다. 단일 문자열이 선택된 목적에 맞으므로 overload는 남기지 않는다. 기존 호출 앱은 한 인수 호출만 사용하는 경우에도 새 DLL로 재빌드해야 한다.

부족 데이터·빈 항목·ResetForm의 변수 초기화는 제조사 상세 계약과 실물 증거가 부족하다. 매 요청 필요한 값을 모두 제공하는 호출자 책임을 유지하고 원문 전달을 물리 출력 성공으로 표현하지 않는다. 파일 읽기 잠금·SDK 순서·실패 중단·owner/faulted/Positions·결과 객체는 변경하지 않는다. Wrapper의 실제 선언·원문은 보존하고 과거 제품 예제와 현재 사용법을 API 문서에서 구분한다.
