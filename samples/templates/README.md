# 두 줄 테스트 템플릿

checkup-two-lines.txt(CP949), checkup-two-lines.json(UTF-8), checkup-two-lines.xml(UTF-8)은 같은 BtplTemplate으로 읽는 고정 문구 서식이다. 가상 데이터만 사용하며 회색 RFID 그림은 기존 용지에 있으므로 요소에 포함하지 않는다.

용지는 시험용 흰 라벨 60×20 mm·피치 40 mm·검은 마크 5 mm·offset 7.5 mm·뒷종이 폭 70 mm 설정이다. X80/Y120과 X80/Y160, AW10/1/0/0을 사용한다. 공통 기본값이 아니며 실제 두 줄 배치·반복 정렬은 미검증이다.

```csharp
var template = BtplTemplate.Load("samples/templates/checkup-two-lines.json");
// txt 또는 xml도 선택 Templates로 읽은 뒤 완성 BTPL 문자열로 전달한다.
using (var printer = new Bt003Printer())
{
    if (!printer.ConnectUsb(selectedUsbPort, 15).IsSuccess) return;
    var result = printer.Print(template.ToBtpl());
    // PrintCode와 마지막 MovementCode, CleanupCode를 각각 확인한다.
}
```

TXT 파일을 기본 SDK 경로로 등록하려면 PrintFile(txtPath)를 사용한다. JSON/XML 파일은 SDK에 직접 넘기지 않고 위처럼 선택 Templates로 변환한다. SDK 변수 서식이면 V00부터 최고 참조 번호까지 값을 매번 제공해야 한다. 이 고정 문구 서식에는 별도 변수가 없다.

배출 거리는 ConnectUsb 인수로 지정한다. 예시 15 mm는 시험용이며 기본값은 0이다. 서식의 E 설정은 별개 SDK 설정으로 유지하며 배출 완료를 보장하지 않는다. M/B는 서식에 추가하지 않는다. 다음 인쇄는 이전 배출 거리를 복귀하고, 해제 자체는 후진하지 않는다.

[새 API](../../docs/PRINTER_API.ko.md), [스펙](../../docs/specs/SYNC_USB_MVP.md), [배출·복귀](../../docs/PRINT_PRESENTATION.ko.md)를 따른다. 실제 두 줄 출력과 배출은 [보류 실기](../../docs/HARDWARE_VALIDATION.ko.md)에서 확인한다. SDK 0은 물리 완료 증거가 아니다.
