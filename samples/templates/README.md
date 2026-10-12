# 두 줄 테스트 템플릿

checkup-two-lines.txt(CP949), checkup-two-lines.json(UTF-8), checkup-two-lines.xml(UTF-8)은 같은 BtplTemplate으로 읽는 고정 문구 서식이다. 첫 줄은 `CK000001 홍길동 98-03-21`, 둘째 줄은 `Not Used`이며 가상 데이터다. 회색 RFID 그림은 기존 용지에 있으므로 요소에 포함하지 않는다.

용지는 이번 시험용 흰 라벨60×20mm·피치40mm·검은 마크5mm·offset7.5mm·뒷종이 폭70mm 설정이다. X80/Y120과 X80/Y160, AW10/1/0/0을 사용한다. 모든 라벨의 공통 기본값이 아니다. 두 줄의 새 실제 배치는 아직 확인하지 않았다.

```csharp
var template = BtplTemplate.Load("samples/templates/checkup-two-lines.json");
// 확장자를 txt 또는 xml로 바꿔도 같은 서식이다.
var options = Bt003ConnectionOptions.Usb(selectedUsbPort)
    .WithPresentationDistance(15);

using (var printer = new Bt003Printer())
{
    var connected = printer.Connect(options);
    if (!connected.IsSuccess) return;
    var result = printer.Print(template);
    // 인쇄 Rows와 전진 PresentationStatus, Code/CleanupCode/pending을 각각 확인한다.
}
```

자동 배출 사용에 따른 템플릿 수정은 필요 없다. E15는 보존하고 M/B를 서식에 추가하지 않는다. 배출은 위 연결 옵션으로 지정한다. 설정을 생략하면 기본0으로 전진하지 않는다. 다음 Print는 이전 전진 거리로 복귀하며, 해제/폐기 자체는 후진하지 않는다.

SDK0과 물리 완료는 다르다. 수동 이송/용지 교체/재시작/불명 위치의 복구는 [배출·복귀 API](../../docs/PRINT_PRESENTATION.ko.md)를 따른다. 실제 두 줄 출력·반복 정렬·배출은 [보류 실기](../../docs/HARDWARE_VALIDATION.ko.md)에서 확인한다.
