# 두 줄 테스트 템플릿

현재 API의 예제는 checkup-two-lines.txt(CP949)다. 이전 JSON/XML 형식은 제거했으며 BtplTemplate.Load는 TXT만 읽는다. 가상 데이터만 사용하며 회색 RFID 그림은 기존 용지에 있으므로 요소에 포함하지 않는다.

용지는 시험용 흰 라벨 60×20 mm·마크 제외 길이 35 mm·검은 마크 5 mm·뒷종이 폭 70 mm이며 `^Q35,5,9+`를 쓴다(300dpi, 흰 라벨 상단은 서식 원점보다 약 3.6 mm 아래). X80/Y94와 X80/Y134(이전 `^Q40,5,7.5+`의 Y120·Y160과 같은 인쇄 위치), AW10/1/0/0을 사용한다. 공통 기본값이 아니다. 운영 용지의 사용자 두 줄 출력·FEED 후 정렬 관측은 [운영 기록](../../docs/PRINT_PRESENTATION.ko.md#feed-헬퍼-실기-확인-2026-10-16-19)에 있다. 이 관측이 현재 로컬 수정본·복사본이나 다른 용지 전체의 검증을 뜻하지는 않는다.

```csharp
var template = BtplTemplate.Load("samples/templates/checkup-two-lines.txt");
// 선택 Templates로 TXT를 읽고 편집한 뒤 변수 표식이 남은 원문을 전달한다.
using (var printer = new Bt003Printer())
{
    if (!printer.ConnectUsb(selectedUsbPort, 15).IsSuccess) return;
    var result = printer.Print(template.ToBtpl());
    // 성공 여부는 IsSuccess로 확인한다. 실패 시 Message와 SDK 호출 증거를 확인한다.
    // 실패에도 일부 인쇄가 있었을 수 있으므로 자동으로 다시 인쇄하지 않는다.
}
```

TXT 파일을 기본 SDK 경로로 등록하려면 PrintFile(txtPath)를 사용한다. SDK 변수 서식이면 V00부터 최고 참조 번호까지 값을 매번 raw 가변데이터 문자열로 제공한다. 예를 들어 PrintFile(txtPath, "홍길동!!1122334455!!20120201")처럼 호출한다. 이 고정 문구 서식에는 별도 변수가 없다. 편집과 Save/SaveAs는 [TXT API 안내](../../docs/BTPL_TEMPLATE_API.ko.md)를 따른다.

배출 거리는 ConnectUsb 인수로 지정한다. 예시 15 mm는 이 용지와 `^Q35,5,9+`에서 블랙마크가 출구 끝에 멈춘 인출 거리이며 기본값은 0이다. 서식의 E 설정은 별개 SDK 설정으로 유지하며 배출 완료를 보장하지 않는다. M/B는 서식에 추가하지 않는다. 다음 인쇄는 이전 배출 거리를 복귀하고, 해제 자체는 후진하지 않는다.

[새 API](../../docs/PRINTER_API.ko.md), [스펙](../../docs/specs/SYNC_USB_MVP.md), [배출·복귀](../../docs/PRINT_PRESENTATION.ko.md)를 따른다. 후속 두 줄 출력·보정 관측은 [실측 기록](../../docs/PRINT_PRESENTATION.ko.md#tester-잉크-실측과-미리보기-차이-2026-10-16-24), 예전 보류 상태는 [당시 실기 기록](../../docs/history/HARDWARE_VALIDATION.ko.md)으로 구분한다. SDK 0은 물리 완료 증거가 아니다.
