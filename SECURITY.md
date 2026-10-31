# 보안 정책

LISH 의 보안 취약점을 발견하셨다면 아래 절차로 비공개 제보해 주세요.

## 지원 버전

보안 수정은 **최신 릴리스에만** 반영합니다. 이전 버전은 따로 패치하지 않으니
[Releases](https://github.com/struggler-labs/lish/releases)에서 최신 버전으로 올려 주세요.

| 버전 | 지원 |
|---|---|
| 최신 릴리스 | O |
| 그 이전 버전 | X |

## 취약점 제보 방법

- **공개 이슈로 올리지 마세요.** 수정 전에 내용이 알려지면 사용자가 위험해집니다.
- GitHub 비공개 취약점 제보를 이용합니다:
  [Security Advisory 작성](https://github.com/struggler-labs/lish/security/advisories/new)

제보에 다음 내용을 넣어 주세요.

- 사용한 LISH 버전 (정보(ⓘ) 창 또는 릴리스 태그)과 Windows 버전
- 재현 절차 (가능하면 서버 설정, 입력 값, 스크린샷 포함)
- 예상되는 영향 (예: 호스트 키 검증 우회, 로컬 파일 덮어쓰기, 자격 증명 노출)

## 처리 절차

- **7일 이내**에 제보 접수를 확인해 답변합니다.
- **30일 이내**에 수정 또는 완화 방안을 내는 것을 목표로 합니다.
- 수정 릴리스와 함께 GitHub Security Advisory 로 공개하며, 원하시면 제보자를 크레딧에 표기합니다.

## 범위

다음은 제보 대상입니다.

- 호스트 키 검증 (`known_hosts` 처리, 키 변경 차단 우회 등)
- 자격 증명 처리 (비밀번호·키 암호·OTP 입력값의 노출 또는 잔존. LISH 는 비밀번호를 저장하지 않습니다)
- SFTP 경로 처리 (경로 조작으로 의도하지 않은 로컬/원격 위치에 쓰기·읽기)
- 터미널 이스케이프 시퀀스 파서 (악의적인 서버 출력으로 인한 충돌, 명령 주입, 클립보드·창 제목 악용 등)

다음은 범위 밖입니다.

- SSH 서버 자체의 취약점 → 해당 서버 프로젝트에 제보해 주세요.
- 이미 상위 프로젝트에서 수정된 서드파티 라이브러리(SSH.NET, BouncyCastle 등) 취약점 → 해당 라이브러리에 제보해 주세요.
  단, **LISH 가 취약한 버전을 내장해 배포하고 있다면** 이쪽으로도 알려 주세요.

## 릴리스 파일 검증

릴리스에는 `SHA256SUMS.txt` 와 GitHub 빌드 출처 증명(build provenance)이 함께 제공됩니다.

```
gh attestation verify lish.exe --repo struggler-labs/lish
```

PowerShell 에서 `Get-FileHash lish.exe -Algorithm SHA256` 결과를 `SHA256SUMS.txt` 와 비교해도 됩니다.
