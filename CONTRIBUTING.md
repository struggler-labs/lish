# LISH 기여 안내

LISH 에 관심을 가져 주셔서 감사합니다. 버그 제보, 기능 제안, 코드 기여 모두 환영합니다.

## 버그 제보 · 기능 제안

- [GitHub Issues](https://github.com/struggler-labs/lish/issues)에 올려 주세요.
- 버그는 LISH 버전(정보 창), Windows 버전, 재현 절차, 기대한 동작과 실제 동작을 적어 주세요. 화면 문제는 스크린샷이 있으면 좋습니다.
- **보안 취약점은 공개 이슈로 올리지 마세요.** [SECURITY.md](SECURITY.md)의 비공개 제보 방법을 따릅니다.
- 큰 변경(새 기능, 의존성 추가, 구조 변경)은 PR 전에 이슈로 먼저 논의해 주세요.

## 개발 환경

- Windows 10/11
- .NET SDK 8.x — `net48` 대상으로 빌드합니다. .NET Framework 4.8 참조 어셈블리는 NuGet 으로 받아지므로 별도 설치가 필요 없습니다.
- 빌드: `./build.ps1` (결과물 `dist\lish.exe`)
- 테스트: `dotnet test tests/Lish.Tests -c Release`

## 작업 흐름

1. 저장소를 fork 하거나 `main` 에서 브랜치를 만듭니다. 브랜치 이름은 `feat/…`, `fix/…`, `ci/…`, `docs/…` 처럼 종류를 앞에 붙입니다.
2. 변경하고, 빌드와 테스트가 로컬에서 통과하는지 확인합니다.
3. `main` 으로 PR 을 엽니다. 무엇을 왜 바꿨는지, 어떻게 확인했는지 적어 주세요.
4. 필수 검사가 모두 통과해야 병합됩니다: **build**(빌드 + 단위 테스트), **Trivy**, **CodeQL**.
5. `main` 은 보호 브랜치입니다. 직접 push 와 force push 는 막혀 있고, 모든 변경은 PR 로 들어갑니다.

## 커밋 규칙

- 제목은 Conventional Commits 형식을 따릅니다: `feat:`, `fix:`, `ci:`, `docs:`, `test:`, `deps:` 등. 제목은 영어로 써도 됩니다.
  - 예: `fix: keep SFTP sidebar in sync after reconnect`
- 본문에는 "무엇"보다 **"왜"** 바꿨는지를 설명합니다.
- 커밋은 서명해 주세요 (GPG 또는 SSH 서명). GitHub 에서 Verified 로 표시되어야 합니다.

## 코드 요구 사항

- 주변 코드의 스타일(이름 짓기, 주석 밀도, 한국어 UI 문자열)을 따릅니다.
- 컴파일러 경고 0 개를 유지합니다.
- 새 런타임 의존성은 논의 없이 추가하지 않습니다. NuGet 패키지를 추가한다면:
  - 라이선스가 GPL-3.0 과 호환되어야 합니다 (MIT / BSD / Apache-2.0 등).
  - 단일 exe 배포를 위해 exe 리소스로 내장되어야 합니다.
  - [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) 에 고지를 추가합니다.
- 의존성을 바꾸면 `packages.lock.json` 도 함께 갱신해 커밋합니다.
- 보안 설계 원칙을 지킵니다:
  - 서버에서 명령을 자동으로 실행하지 않습니다.
  - 비밀번호를 저장하지 않습니다.
  - 호스트 키 검사를 느슨하게 만들지 않습니다.

## 테스트 정책

- 새 기능과 버그 수정에는 자동화 테스트를 추가하거나 갱신합니다. UI 없이 검증할 수 있는 로직(파서, Core 로직 등)은 `tests/Lish.Tests` 에 테스트를 넣어 주세요.
- 버그 수정은 가능하면 그 버그를 재현하는 테스트를 먼저 추가합니다.
- UI 변경은 PR 에 변경 전후 스크린샷을 첨부합니다.

## 변경 내역 · 버전

- [CHANGELOG.md](CHANGELOG.md)([Keep a Changelog](https://keepachangelog.com/ko/1.1.0/) 형식)의 `[Unreleased]` 아래에, 또는 새 버전 항목으로 변경 내용을 적습니다.
- 릴리스 버전은 `src/Lish/Lish.csproj` 의 `<Version>` 입니다. 새 버전이 `main` 에 병합되면 GitHub Actions 가 서명된 출처 증명(provenance)과 함께 릴리스를 자동으로 만듭니다.

## 라이선스

기여한 코드는 프로젝트와 같은 [GPL-3.0](LICENSE) 으로 배포되는 데 동의한 것으로 봅니다.
