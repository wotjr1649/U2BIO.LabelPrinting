# TASK-01 빌드 검증 기록

2026-10-08에 커밋 `ac9e181d8192de86b8a0fb2ca6c779109359c6ac`의 깨끗한 작업 트리에서 검증했다. 이후 이 기록을 추가한 문서 커밋은 아래 제품 빌드의 검증 대상 커밋과 구분한다.

## 실행과 결과

```text
node tools/verify-build.js
node tools/verify-build.js --configuration Debug
```

두 명령 모두 종료 코드 0이었다. MSBuild 16.11.6.22506, compiler `3.11.0-4.26426.1 (e1e6b25d)`, .NET Framework 4.6.1 targeting pack을 사용했다. 참조 pack의 mscorlib.dll SHA-256은 `d4954270c20f362912caf146082d09c352914ddd7b7cd5116842a2707af81bbb`이다.

| 구성 | DLL | 검증 exe / 실제 프로세스 | 빌드·실행 |
| --- | --- | --- | --- |
| Release x86 | AnyCPU / v4.6.1 | x86 | 통과 |
| Release x64 | AnyCPU / v4.6.1 | x64 | 통과 |
| Debug x86 | AnyCPU / v4.6.1 | x86 | 통과 |
| Debug x64 | AnyCPU / v4.6.1 | x64 | 통과 |

네 구성의 평가된 Prefer32Bit는 false였고 framework 속성, DLL의 정확한 로드 경로, PE/CLI 비트 수, native SDK 미로드 검사를 통과했다. DevExpress/DB provider 참조가 없었다. 독립 PE 검사에서 DLL은 I386/PE32/CLI flags 0x1, x86 exe는 I386/PE32/0x3, x64 exe는 AMD64/PE32+/0x1이었다.

실행별 원본 증거는 `.work/foundation/Release-1791390525780/evidence.json`과 `.work/foundation/Debug-1791390535432/evidence.json`이다. 두 기록 모두 위 커밋, `workingTreeDirty=false`, `result=passed`를 담고 있다. 원본 로그와 바이너리는 로컬 검증 자료로 Git에서 제외하며 이 문서는 검토한 결과와 식별 해시만 남긴다.

## 산출물 SHA-256

각 실행 폴더의 x86/x64 하위 폴더에 DLL과 exe 사본이 있다. 아래 해시는 원본 evidence와 사본을 다시 대조했다. 같은 구성의 x86/x64 DLL은 동일한 AnyCPU 바이너리다.

| 구성 | 산출물 | SHA-256 |
| --- | --- | --- |
| Release | DLL, x86/x64 공통 | `fa7e363d488d1bb817bf9508c797eff5f8f6a4f6a3961c2b2908d43634e8c271` |
| Release | x86 validation exe | `2832e59ec130b345455432bd53c20868e2031d6a92ef2e016c28028070fb2175` |
| Release | x64 validation exe | `11f433ea8aa26f4fc2daa946ef36568f33dde42da491c685d50ac088e9a634a5` |
| Debug | DLL, x86/x64 공통 | `5355b6b139b01e9d661713ba9a2ebd5d2d3bf83809807c99c5fa48fea1ec4188` |
| Debug | x86 validation exe | `f88a678f3ed6eac4311669931fb874a494d1b2bfd7a8ea8bc73279c606acd246` |
| Debug | x64 validation exe | `1761d0434537a8ed2f2ca4e02a6c23db423e23f24c5f0272f7f65f6c8165f1ea` |

## 실패 조건과 범위

- DLL 구현 전 standalone x86 검증 exe가 `MANAGED_LIBRARY_MISSING`, 종료 코드 3으로 실패했다. 동일 시험 소스를 `164a8d4`에 커밋하고 시험 잠금을 적용한 뒤 DLL을 추가했다.
- x86 Release exe에 `--expect-x64`를 전달하면 종료 코드 1로 거절했다.
- `node tools/verify-build.js --configuration Invalid`는 종료 코드 1로 거절했다.
- task 전용 checkout의 로그 경로를 junction으로 checkout 밖에 연결한 경우 종료 코드 1로 거절했고 대상 폴더에 쓰지 않았다.
- 저장소 구조 검사와 `git diff --check`를 통과했다. 제품 빌드 후 문서만 수정할 때도 해당 검사를 수행한다.

DLL은 현재 어셈블리 메타데이터만 있는 기반 프로젝트다. 공개 연결·인쇄 API, BTPL 처리, BlackfishNfcLib 호출, 실제 장치·RFID 보존, DevExpress UI, DB 연결은 이번 시험에 포함하지 않았다. 프로세스 제한 초과 후 종료 처리 경로는 코드 리뷰를 수행했으며 실제 timeout을 발생시키는 시험은 수행하지 않았다. C01-C24의 장치 수용 시험은 후속 TASK에서 수행한다.
