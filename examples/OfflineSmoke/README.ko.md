# Checkup·SmartCheckup 호출 예시

이 예시는 현재 동기 USB API를 기존 호출 프로그램에 붙이는 방법이다. 기존 업무 프로그램 소스나 DB 연결은 변경하지 않는다. 제품 DLL에는 DataTable·업무 열 이름·NULL 정책을 넣지 않는다.

`CheckupExamples.cs`의 두 함수는 호출 프로그램이 조회한 DataRow를 받는다. 실제 DB 열 이름은 각 함수의 열 선택만 바꾸면 된다.

| 예시 | V00 | V01 | V02 | 인쇄 경로 |
|---|---|---|---|---|
| Checkup | ExamNo | DisplayName | BirthText | 읽은 서식 원문을 Print |
| SmartCheckup | VisitCode | DisplayName | Department | 배포 후 고정된 파일을 PrintFile |

NULL은 빈 문자열로 변환하며 빈 항목·앞뒤 공백은 보존한다. 날짜/숫자의 업무별 출력 형식은 호출 전에 결정한다. 필수 열 누락과 값 내부의 `!!`는 오류로 알린다. `!!`는 SDK 구분자이며 이스케이프 규약이 없으므로 값 안에 넣지 않는다. `A!!!!C!!`는 4개 항목이다. 합쳐진 raw 문자열을 다시 split/join/trim하지 않고 전달한다.

`PrintOne`은 실제 장치를 사용하는 호출 순서 예시다. 운영자가 인쇄를 요청할 때만 호출하고, 기본 오프라인 Main에서는 호출하지 않는다. 연결 후 운영자가 명시적으로 요청한 경우에만 FEED를 수행한다. 물리 FEED 키로 정렬했다면 그 직후 ConfirmPrintPosition을 호출한다. 위치 불명 실패를 만나면 자동 재시도하지 말고 운영자가 정렬 방법을 선택하게 한다. `operatorRequestedFeed`를 항상 true로 고정하면 매번 빈 라벨을 이송하므로 일반 인쇄 설정으로 쓰지 않는다.

배출 15 mm는 검증된 운영 블랙마크 용지에만 해당한다. 다른 용지는 운영 기준의 보정 절차를 따른다. 실제 UI는 동기 호출을 백그라운드에서 실행하고 중복 요청과 작업 중 종료를 제어해야 한다.

IsSuccess는 SDK 작업 결과이며 실제 출력 완료나 무출력을 보장하지 않는다. NativeCode는 주 작업의 마지막 응답, PrintInvoked는 인쇄 호출 진입 여부, PrintCode는 인쇄 응답이다. PrintCode=0이면서 IsSuccess=false이면 배출·정리 실패일 수 있으므로 자동으로 다시 인쇄하지 않는다. Message를 사용자에게 표시하고 결과를 보존한다. Disconnect 결과와 Dispose 예외도 별도로 처리한다. 예시는 콘솔에 표시하므로 실제 프로그램에서는 기존 알림/로그 UI에 연결한다. 입력·파일 예외도 호출 프로그램의 기존 오류 처리에서 알린다.

두 서식은 같은 DLL과 같은 변수 전달 계약을 사용하며 위치·표시 순서만 다르다. 미리보기 GetPreviewText의 결과는 표시용이다. 치환된 미리보기를 저장하거나 인쇄 원문으로 되돌려 넣지 않는다. PrintFile 예시는 배포 후 변경하지 않는 파일을 전제로 한다. 외부 편집을 허용하는 경우에는 PrintCheckup처럼 검증한 원문 문자열을 Print하거나 Tester의 확인 후 파일 잠금 절차를 참고한다.

검증은 기존 OfflineSmoke에 포함된다. 합성 DataTable로 NULL·빈 항목·공백·누락 열·구분자 충돌, 다른 두 서식의 미리보기, 미연결 Print/PrintFile 결과를 검사한다. x86/x64 빌드와 오프라인 실행은 기존 verify-build가 수행한다. 실제 연결·FEED·인쇄 함수는 자동 실행하지 않는다. 이 검증은 실제 업무 연동 완료나 SDK의 장치 출력 성공을 뜻하지 않는다.

호출 프로그램은 .NET Framework 4.6.1 이상에서 관리 DLL을 참조하고 실제 프로세스 비트수와 맞는 BlackfishNfcLib.dll 및 HFModule.dll을 실행 파일 폴더에 배치한다. AnyCPU 호출자는 실행 비트수를 명확히 고정해야 한다. 제조사 파일을 관리 DLL에 포함하거나 COM 등록하지 않는다. 예제의 System.Data 참조는 호출 프로그램에만 추가한다.
