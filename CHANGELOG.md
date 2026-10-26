# Changelog

이 프로젝트의 주요 변경 사항을 기록합니다.
형식은 [Keep a Changelog](https://keepachangelog.com/ko/1.1.0/)를 따르고, 버전은 [Semantic Versioning](https://semver.org/lang/ko/)을 따릅니다.

릴리스 버전은 `src/Lish/Lish.csproj` 의 `<Version>` 값이며, `main` 에 새 버전이 병합되면 GitHub Actions 가 `v<버전>` 태그와 릴리스를 자동으로 만듭니다.
릴리스 노트는 이 파일의 해당 버전 항목에서 가져옵니다.

## [Unreleased]

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

[Unreleased]: https://github.com/struggler-labs/lish/compare/v0.0.3...HEAD
[0.0.3]: https://github.com/struggler-labs/lish/compare/v0.0.2...v0.0.3
[0.0.2]: https://github.com/struggler-labs/lish/releases/tag/v0.0.2
