# 서식 형식 안내

이슈 #16의 사용자 결정으로 서식 편집은 TXT 전용으로 축소한다. JSON/XML 변환과 BtplTemplateFormat API는 제거 대상이다. 현재 계약과 사용법은 [TXT 편집 스펙](specs/TXT_TEMPLATE_EDITOR.md)과 [편집 API 안내](BTPL_TEMPLATE_API.ko.md)를 따른다.

파일 확장자로 JSON/XML을 선택하지 않고 TXT 내용을 읽고 저장한다. 기존 형식 API를 사용한 호출부는 새 TXT API로 이행하고 재빌드해야 한다. 과거 형식 검증 기록은 현재 지원 기능으로 해석하지 않는다.
