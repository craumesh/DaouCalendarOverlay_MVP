# Changelog

이 파일은 Keep a Changelog 형식을 따르고 버전은 유의적 버전(SemVer)을 따릅니다.
WPF 앱(EXE)과 Chrome 확장은 같은 버전 번호를 씁니다. 네이티브 브리지 프로토콜 버전은 별도로 `NativeBridgeProtocol.ProtocolVersion` 으로 관리합니다(현재 v1, 파이프 이름 `DaouCalendarOverlay.NativeBridge.v7`).

## 7.1.0 - 2026-09-23

### Added
- 파일 로깅(`%LOCALAPPDATA%\DaouCalendarOverlay\logs`, 1MB x 5 롤링)과 트레이 "로그 폴더 열기"
- 설정·캐시 저장의 원자적 쓰기(임시 파일 + 교체)
- 캐시에 마지막 동기화 시각과 조회 범위(`LastUpdated`/`RangeFrom`/`RangeTo`) 기록, 범위 밖 캐시 표기
- `tools/Measure-HostSpawn.ps1`: native host 스폰당 경과 시간·CPU 측정 스크립트(측정 결과는 기술 문서 성능 절)
- Chrome 확장 버전 불일치 감지: `getConfig`의 `extensionVersion`을 EXE 기대 버전(`ChromeExtensionInstaller.ExpectedExtensionVersion`)과 비교해 상태에 "Chrome 확장 새로고침 필요 (x → y)" 표시(`ExtensionVersionGuard`)
- 버전 체계: EXE `Version`/`FileVersion`/`InformationalVersion`(빌드 커밋 SHA 자동 포함), `NativeBridgeProtocol.ProtocolVersion = 1`, 트레이 툴팁·설정창 하단·로그 첫 줄에 버전 표시, 이 CHANGELOG 신설
- 제거 경로: `--uninstall` 명령줄 인자(`/uninstall`, `-uninstall`도 인식)와 트레이 "완전 제거…" — HKCU Run 값과 Chrome/Edge NativeMessagingHosts 키를 삭제하고 `%LOCALAPPDATA%\DaouCalendarOverlay` 삭제 여부를 확인 창으로 묻는다(`UninstallService`, `UninstallFlow`)
- README와 기술 문서에 설치·업그레이드(EXE 종료 → 교체 → 실행 → 확장 새로고침)·제거 절차
- Edge Native Messaging 등록(시험 지원): `HKCU\Software\Microsoft\Edge\NativeMessagingHosts\com.daou.calendar_overlay`에 같은 host manifest 등록, 설정 `RegisterEdge`(기본 켜짐)와 설정창 "Edge에도 Native Messaging 등록"(끄면 Edge 키만 제거)
- README와 기술 문서에 지원 환경 매트릭스(Windows 10/11 x64, Chrome 120+, Edge 시험 지원, Whale/Brave/Firefox 미지원, 단일 프로필, KST)
- README와 기술 문서에 빌드 전제조건·릴리스 절차(`publish.ps1` 파라미터, 산출물, `-Version` 일치 규칙, 코드 서명)

### Changed
- Chrome 확장 7.0.0 → 7.1.0, 서비스 워커 파일명 `service-worker-v710.js`, 확장이 `extensionVersion`을 앱에 전달
- Native host 경량화: 커스텀 `Program.Main`이 WPF `Application` 생성 전에 host 모드를 분기해, host 스폰마다 `App`·App.xaml 리소스를 만들지 않음(`StartupModeParser`)
- 확장 동기화 트리거 정리: 서비스 워커 최상위 `syncOnce()` 호출 제거, 쿠키 변경 5초 debounce, host 연결 3회 연속 실패 시 알람 주기 1 → 2 → 5분 백오프 후 성공 시 30초 복귀, 알람 주기 불일치 시 재생성
- DaouOffice 주소 검증을 https + `*.daouoffice.com` 정책 한 곳(`BaseUrlPolicy`)으로 통일
- 동기화 상태 문구를 `SyncStatusService` 한 곳에서만 생성
- 자동 새로고침 주기를 1~1440분으로 제한하고 캘린더 ID 입력을 검증
- DaouOffice "열기"(오버레이 버튼·트레이 메뉴)가 기본 브라우저 대신 `App Paths\chrome.exe`(HKCU → HKLM)로 Chrome을 직접 실행하고, 찾지 못하거나 실행에 실패하면 기본 브라우저로 연다(`BrowserLauncher`)
- 빌드·배포 정비: 개발 빌드는 framework-dependent(`DebugType=portable`), self-contained/single-file 설정은 publish 프로파일 `Properties/PublishProfiles/win-x64.pubxml`로 이동, `publish.ps1`이 `DaouCalendarOverlay-<버전>.exe` 산출물명·`publish\symbols\` PDB 분리·`-CertificateThumbprint` 서명 자리·`-DryRun`·csproj 버전과 다른 `-Version` 거부를 지원

### Fixed
- 여러 캘린더에 공유된 일정을 `id` 기준 1건으로 합치고 소속 캘린더 이름을 모두 표기
- 자정·월 전환 시 오늘 날짜 갱신 누락
- `publish.ps1`이 `dotnet` 실패(0이 아닌 종료 코드)에도 "Publish completed"를 출력하고 정상 종료하던 문제

### Security
- 네임드 파이프 `CurrentUserOnly` + 서버 프로세스 실행 파일 경로 대조 후에만 쿠키 전달
- 로그에 쿠키 값과 토큰을 남기지 않음(개수/출처만 기록)

> P2(배포·운영 정비) 작업이 진행 중입니다. 이후 P2 작업의 결과는 각 작업에서 이 7.1.0 절에 추가합니다.

## 7.0.0 - 2026-09-18

- 기술 문서 v7.0.0 as-built 기준의 최초 구현: WPF 오버레이, Chrome MV3 확장 + Native Messaging + 네임드 파이프 브리지, 단일 파일 self-contained 배포, 로컬 설정/캐시 저장.
