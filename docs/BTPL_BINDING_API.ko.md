# 가변데이터와 이전 바인딩 API

이슈 #16의 사용자 결정으로 DataTable·바인딩 JSON·일괄 렌더링과 BtplProcessor/BtplPrintRequest API는 제거 대상이다. 현재 선택 편집 기능은 [TXT 편집 계약](specs/TXT_TEMPLATE_EDITOR.md)을 따른다.

DB 조회와 업무 값을 !! 문자열로 구성하는 일은 사용 프로젝트가 맡는다. 실제 인쇄는 [프린터 공개 API](PRINTER_API.ko.md)의 Print/PrintFile에 원본 서식과 raw 가변데이터를 전달한다. 미리보기 치환은 화면 표시만 위한 것이며 저장/인쇄에 역류하지 않는다. [편집 API 안내](BTPL_TEMPLATE_API.ko.md)를 참고한다.
