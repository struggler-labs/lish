# Changelog

이 프로젝트의 주요 변경 사항을 기록합니다.
형식은 [Keep a Changelog](https://keepachangelog.com/ko/1.1.0/)를 따르고, 버전은 [Semantic Versioning](https://semver.org/lang/ko/)을 따릅니다.

릴리스 버전은 `src/Lish/Lish.csproj` 의 `<Version>` 값이며, `main` 에 새 버전이 병합되면 GitHub Actions 가 `v<버전>` 태그와 릴리스를 자동으로 만듭니다.
릴리스 노트는 이 파일의 해당 버전 항목에서 가져옵니다.

## [Unreleased]

### Added
- 단위 테스트(xUnit, `tests/Lish.Tests`, 129개): VT 파서 · OSC 7 · 문자 폭 · known_hosts · 주소 파싱 · 세션 폴더 · 세션 저장소(예전 비밀번호 삭제 마이그레이션) · 설정.
  PR 빌드 검사에서 함께 실행된다.
- `CONTRIBUTING.md`: 기여 절차, 커밋 규칙, 코드 요구 사항, 테스트 정책.

## [0.0.6] - 2026-11-01

### Security
- **릴리스 서명**: 릴리스 파일(lish.exe, zip)에 Sigstore 로 서명된 GitHub 빌드 출처 증명(SLSA provenance)을 붙이고,
  증명 번들을 `lish-v<버전>.sigstore.json` 으로 함께 올린다. 검증: `gh attestation verify lish.exe --repo struggler-labs/lish`.
- `SECURITY.md` 보안 정책 추가: GitHub 비공개 취약점 제보로 신고, 지원 버전, 처리 절차.
- CI 보안 점검: OpenSSF Scorecard, Trivy(의존성 취약점 · 비밀 값 · 설정 오류), CodeQL(C# 소스 정적 분석).
- 워크플로 강화: 모든 액션을 커밋 SHA 로 고정, 토큰 권한 최소화(쓰기 권한은 릴리스 잡에만), checkout 자격 증명 미보관.
- Dependabot 으로 NuGet · GitHub Actions 의존성 주간 업데이트. NuGet `packages.lock.json` 으로 하위 의존성까지 고정.

### Docs
- README 에 Scorecard · Build · Trivy · CodeQL 배지와 SECURITY.md 링크 추가.

## [0.0.5] - 2026-10-31

### Security
- **서버에서 자동으로 명령을 실행하지 않음**: 원격 모니터(5초마다 `/proc/stat`·`/proc/meminfo`·`df` 조회)와
  exec 채널 방식의 셸 폴더 조회(2초마다 `/proc` 전체 순회)를 없앴다. 사용자가 입력하지 않은 명령이 서버 감사 로그에 남지 않는다.
- **비밀번호 저장 기능 삭제**: 세션 편집에서 비밀번호 입력란과 저장 스위치를 없앴다. 예전 버전이 `hosts.json` 에 저장한
  비밀번호(DPAPI 암호문)는 시작할 때 지우고 파일을 다시 저장한다.
- **점프 호스트 · 프록시 · 포트 포워딩(-L / -D) 삭제**: 접근통제를 우회하는 터널이 될 수 있어 기능을 없앴다.
  세션 편집 고급 설정에는 터미널 · 메모만 남는다. 예전 세션의 해당 값은 무시되고 다음 저장 때 `hosts.json` 에서 빠진다
  (점프 호스트를 쓰던 세션은 이제 대상 호스트로 직접 접속을 시도한다).

### Added
- **좌우 2분할 화면**: 탭 우클릭 → "오른쪽에 분할로 보기" 로 두 탭을 나란히 표시. 두 탭은 탭 바에서 분할 아이콘이 붙은
  하나의 그룹으로 묶여 보인다. 그룹의 탭을 고르면 분할 화면, 다른 탭을 고르면 그 탭만 보이고 그룹은 그대로 남는다.
  활성 칸은 보라색 테두리, 칸을 클릭하면 활성 칸이 바뀐다(상태바·SFTP 가 그 칸 기준). 가운데 경계선을 끌어 폭 조절(두 번 클릭 = 반반),
  "분할 해제 (그룹 풀기)" 또는 그룹의 탭을 닫으면 원래대로.

### Changed
- **터미널 폴더 따라가기**: 셸이 프롬프트마다 보내는 OSC 7(`file://호스트/경로`)로 현재 폴더를 알아낸다. 셸 설정이 필요하다
  (bash: `PROMPT_COMMAND='printf "\e]7;file://%s%s\a" "$HOSTNAME" "$PWD"'`). 처음 알린 호스트와 다른 호스트(터미널 안에서 다시 ssh 한 서버)는 무시한다.
  SFTP 를 처음 열 때도 이 값을 쓰고, 모르면 홈 폴더에서 연다.

### Removed
- 하단 상태바의 원격 모니터(CPU · RAM · Disk).
- 세션 편집의 네트워크(점프 호스트 · 프록시) · 포트 포워딩 항목.

## [0.0.4] - 2026-10-26

### Added
- **정보 창**: 탭 바 오른쪽 설정 버튼 옆 ⓘ 버튼. 버전, 빌드 커밋 해시(복사 가능), 라이선스(GPL-3.0), 저장소 링크,
  exe 에 내장된 오픈소스 라이브러리 목록(이름·버전·라이선스, 빌드 시 실제 내장 패키지에서 자동 생성), 오픈소스 고지 전문 보기.
- `THIRD-PARTY-NOTICES.txt`: 내장 라이브러리(SSH.NET, BouncyCastle, .NET 라이브러리 — MIT)와 D2Coding(OFL-1.1) 고지. 릴리스 zip 에 포함, exe 에도 내장.

### Changed
- **우클릭 메뉴 새 디자인** (세션 트리·탭·SFTP 목록·터미널): 둥근 모서리(Windows 11), 항목 아이콘, 단축키 표시,
  둥근 선택 배경, 삭제 항목은 빨간색, 여유 있는 항목 높이.
- 세션 편집 창: **네트워크(점프 호스트·프록시)**도 고급 설정으로 이동. 기본 화면은 연결·인증만 표시.
- README 스크린샷 갱신.

## [0.0.3] - 2026-10-26

### Changed
- 세션 편집 창: **터미널 · 포트 포워딩 · 메모**를 "고급 설정"으로 접어 두고, 오른쪽 "펼치기/접기" 버튼으로 엽니다.
  창 높이가 내용에 맞춰 늘고 줄며, 기본값이 아닌 고급 설정이 있는 세션을 편집하거나 고급 설정 칸에 입력 오류가 있으면 자동으로 펼칩니다.

### Docs
- README 에 첫 화면 스크린샷 추가.

## [0.0.2] - 2026-10-26

Windows 10/11용 WinForms GUI 클라이언트 첫 공개 버전입니다.

### Added
- **탭 터미널**: xterm 호환 VT 엔진(256색/트루컬러, 대체 화면, 스크롤 영역, DEC 선문자, 마우스 모드, bracketed paste, OSC 제목),
  한글 2칸 폭·IME 조합 표시, 드래그/더블클릭 선택·복사, 우클릭 메뉴, D2Coding 글꼴 내장.
- **SSH 연결**: 키 → 비밀번호 / keyboard-interactive(OTP) 인증, 점프 호스트 체인, HTTP/SOCKS 프록시, 로컬(-L)·SOCKS(-D) 포워딩,
  OpenSSH 호환 known_hosts(해시 항목 포함), 처음 보는 서버 / 바뀐 서버 키 확인, 재접속, cp949 등 문자셋 선택.
- **세션 폴더 트리**: 폴더 안에 하위 폴더를 만들어 세션 정리. 새 폴더·이름 바꾸기(F2)·삭제(Del), 끌어 놓기로 세션/폴더 이동,
  폴더 전체 접속(하위 포함), 검색. 세션 편집 창에서는 만들어 둔 폴더 중에서 선택.
- **SFTP 사이드바**: 선택한 탭 서버의 셸 현재 폴더에서 시작, 터미널 폴더 따라가기, 업로드/다운로드/새 폴더/이름 변경/삭제,
  탐색기 ↔ 목록 끌어 놓기, 전송 진행률·속도·취소.
- **원격 모니터**: 상태바에 CPU · RAM · Disk 사용률 표시 (80%↑ 경고, 90%↑ 위험 색).
- 비밀번호 저장(Windows DPAPI 암호화), 설정 대화상자(글꼴·스크롤백·복사/붙여넣기 옵션), 창 위치·사이드바 폭 기억.
- 단일 실행 파일 배포: SSH.NET 등 의존 DLL과 글꼴을 exe 에 내장 (사내망·오프라인 실행), 포터블 모드(`lish-data` 폴더).

### Security
- SSH.NET 2026.0.0 사용 (2025.0.0 의 GHSA-mggc-4xg6-vcxf, GHSA-q939-rpr3-3284 회피).
- 호스트 키 확인 창의 Enter 기본값은 '취소' — 키 신뢰는 버튼을 직접 눌러야 합니다.

[Unreleased]: https://github.com/struggler-labs/lish/compare/v0.0.6...HEAD
[0.0.6]: https://github.com/struggler-labs/lish/compare/v0.0.5...v0.0.6
[0.0.5]: https://github.com/struggler-labs/lish/compare/v0.0.4...v0.0.5
[0.0.4]: https://github.com/struggler-labs/lish/compare/v0.0.3...v0.0.4
[0.0.3]: https://github.com/struggler-labs/lish/compare/v0.0.2...v0.0.3
[0.0.2]: https://github.com/struggler-labs/lish/releases/tag/v0.0.2
