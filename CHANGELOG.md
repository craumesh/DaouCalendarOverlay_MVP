# Changelog

이 파일은 Keep a Changelog 형식을 따르고 버전은 유의적 버전(SemVer)을 따릅니다.
WPF 앱(EXE)과 Chrome 확장은 같은 버전 번호를 씁니다. 네이티브 브리지 프로토콜 버전은 별도로 `NativeBridgeProtocol.ProtocolVersion` 으로 관리합니다(현재 v1, 파이프 이름 `DaouCalendarOverlay.NativeBridge.v7`).
릴리스 이후에는 Chrome 확장 서비스 워커를 바꾸면 확장 버전을 반드시 올립니다(worker 파일명 `service-worker-v<버전>.js`, `ChromeExtensionInstaller` obsolete 목록, 패키징 테스트를 함께 갱신).

## 7.1.0 - 2026-09-23

### Added
- 파일 로깅(`%LOCALAPPDATA%\DaouCalendarOverlay\logs`, 1MB x 5 롤링)과 트레이 "로그 폴더 열기"
- 설정·캐시 저장의 원자적 쓰기(임시 파일 + 교체)
- 캐시에 마지막 동기화 시각과 조회 범위(`LastUpdated`/`RangeFrom`/`RangeTo`) 기록, 범위 밖 캐시 표기
- `tools/Measure-HostSpawn.ps1`: native host 스폰당 경과 시간·CPU 측정 스크립트(측정 결과는 기술 문서 성능 절)
- Chrome 확장 버전 불일치 감지: `getConfig`의 `extensionVersion`을 EXE 기대 버전(`ChromeExtensionInstaller.ExpectedExtensionVersion`)과 비교해 상태에 "Chrome 확장 새로고침 필요 (x → y)" 표시(`ExtensionVersionGuard`). 확장이 버전을 보내지 않으면 x는 "7.0.0 이하", 재로그인 필요 상태와 재로그인 배너는 덮어쓰지 않으며, 불일치 표시 중 heartbeat가 끊기면 "Chrome 확장 연결 대기"로 바뀜
- 버전 체계: EXE `Version`/`FileVersion`/`InformationalVersion`(빌드 커밋 SHA 자동 포함), `NativeBridgeProtocol.ProtocolVersion = 1`, 트레이 툴팁·설정창 하단·로그 첫 줄에 버전 표시, 이 CHANGELOG 신설
- 제거 경로: `--uninstall` 명령줄 인자(`/uninstall`, `-uninstall`도 인식)와 트레이 "완전 제거…" — HKCU Run 값과 Chrome/Edge NativeMessagingHosts 키를 삭제하고 `%LOCALAPPDATA%\DaouCalendarOverlay` 삭제 여부를 확인 창으로 묻는다(`UninstallService`, `UninstallFlow`)
- README와 기술 문서에 설치·업그레이드(EXE 종료 → 교체 → 실행 → 확장 새로고침)·제거 절차
- Edge Native Messaging 등록(시험 지원): `HKCU\Software\Microsoft\Edge\NativeMessagingHosts\com.daou.calendar_overlay`에 같은 host manifest 등록, 설정 `RegisterEdge`(기본 켜짐)와 설정창 "Edge에도 Native Messaging 등록"(끄면 Edge 키만 제거)
- README와 기술 문서에 지원 환경 매트릭스(Windows 10/11 x64, Chrome 120+, Edge 시험 지원, Whale/Brave/Firefox 미지원, 단일 프로필, KST)
- README와 기술 문서에 빌드 전제조건·릴리스 절차(`publish.ps1` 파라미터, 산출물, `-Version` 일치 규칙, 코드 서명)
- .NET 10 이전 시험: `tools/net10-trial.ps1`(TFM을 임시로 `net10.0-windows`로 바꿔 빌드·publish·EXE 크기·테스트·host 모드 왕복을 비교한 뒤 원복)과 측정 기록 `docs/net10-migration.md`. 현재 TFM은 `net8.0-windows` 유지(`TargetFrameworkGuardTests`로 고정), 전환은 별도 릴리스에서 결정
- 오버레이 UX: 달력 일정 칩 ToolTip(제목·캘린더·시간), 오류 상태 텍스트 ToolTip에 잘린 문구 전문 + 마지막 갱신 시각, 검색창 포커스와 무관하게 달을 옮기는 `Ctrl+PageUp`/`Ctrl+PageDown`/`Ctrl+Home` 단축키
- 캘린더 표시 이름 기억: settings.json 새 키 `CalendarNames`(캘린더 ID → 마지막으로 확인한 이름, 최대 200개, 없으면 빈 사전)에 동기화로 확인된 이름을 바뀐 경우에만 저장해, 조회 범위에 일정이 없는 캘린더도 컨텍스트 메뉴 "캘린더 표시"에서 ID 대신 이름으로 표시(`CalendarNameStore`)

### Changed
- Chrome 확장 7.0.0 → 7.1.0, 서비스 워커 파일명 `service-worker-v710.js`, 확장이 `getConfig`에 `extensionVersion`과 `protocolVersion`(현재 1)을 실어 앱에 전달. 앱은 `protocolVersion`을 로그 요약(`protocolVersion=1`)에 남기고, 이 필드가 없는 구버전 확장도 호환으로 처리
- Native host 경량화: 커스텀 `Program.Main`이 WPF `Application` 생성 전에 host 모드를 분기해, host 스폰마다 `App`·App.xaml 리소스를 만들지 않음(`StartupModeParser`)
- 확장 동기화 트리거 정리: 서비스 워커 최상위 `syncOnce()` 호출 제거, 쿠키 변경 5초 debounce, host 연결 3회 연속 실패 시 알람 주기 1 → 2 → 5분 백오프 후 성공 시 30초 복귀, 알람 주기 불일치 시 재생성
- DaouOffice 주소 검증을 https + `*.daouoffice.com` 정책 한 곳(`BaseUrlPolicy`)으로 통일
- 동기화 상태 문구를 `SyncStatusService` 한 곳에서만 생성
- 자동 새로고침 주기를 1~1440분으로 제한하고 캘린더 ID 입력을 검증
- DaouOffice "열기"(오버레이 버튼·트레이 메뉴)가 기본 브라우저 대신 `App Paths\chrome.exe`(HKCU → HKLM)로 Chrome을 직접 실행하고, 찾지 못하거나 실행에 실패하면 기본 브라우저로 연다(`BrowserLauncher`)
- 빌드·배포 정비: 개발 빌드는 framework-dependent(`DebugType=portable`), self-contained/single-file 설정은 publish 프로파일 `Properties/PublishProfiles/win-x64.pubxml`로 이동, `publish.ps1`이 `DaouCalendarOverlay-<버전>.exe` 산출물명·`publish\symbols\` PDB 분리·`-CertificateThumbprint` 서명 자리·`-DryRun`·csproj 버전과 다른 `-Version` 거부를 지원
- 키보드 단축키를 `PreviewKeyDown` + `OverlayShortcutPolicy`로 판정: 검색창 포커스 중 PageUp/PageDown/Home은 텍스트 편집에 양보하고, Esc는 검색창에서 검색어만 비우며 창을 숨기지 않음. 첫 실행 설정 창은 화면 중앙에 표시
- 일정 색 매핑을 `EventColorPalette`(색 인덱스 1~18 → 색 표, 표에 없는 값은 팔레트 나머지 연산, 숫자가 아니면 캘린더 ID 해시)로 분리. 실제 DaouOffice 색표 확보 전까지 기존 8색과 같은 색을 유지
- Chrome 확장 쿠키 수집이 기본 쿠키 스토어(`"0"`)를 우선하고, 비었을 때만 암시적 스토어 → 쿠키가 있는 첫 스토어 하나를 사용(확장 버전 7.1.0 유지)
- 투명도 슬라이더 100%가 alpha 상한 230(최대 약 90%)임을 README와 기술 문서에 명시

### Fixed
- 여러 캘린더에 공유된 일정을 `id` 기준 1건으로 합치고 소속 캘린더 이름을 모두 표기
- 자정·월 전환 시 오늘 날짜 갱신 누락
- `publish.ps1`이 `dotnet` 실패(0이 아닌 종료 코드)에도 "Publish completed"를 출력하고 정상 종료하던 문제
- 기동 시 Chrome 확장 파일 추출·Native Messaging 등록 실패(GPO·ACL로 HKCU 쓰기 차단 등)가 앱을 종료시키던 문제: 이제 로그(`startup.extension`/`startup.nativehost`)와 상태 문구("Native host 등록 실패: … · 로그 확인")로만 알리고 캐시 표시를 계속하며, 초기화 오류 문구가 원인과 무관하게 settings.json을 지목하던 것을 예외 종류별 사유와 로그 폴더 안내로 바꿈(`StartupFailureReasons`)
- 필터 초기화 버튼이 콤보 변경 → 초기화 → 검색어 debounce 순으로 달력을 최대 3회 재구성하던 문제(변경 이벤트 억제로 1회)
- 일정 색 값이 `-2147483648`(`int.MinValue`)이면 `Math.Abs`가 `OverflowException`을 던지던 경로
- 시크릿 창·다른 프로필이 함께 열려 있을 때 여러 쿠키 스토어의 쿠키를 한 `Cookie` 헤더로 합쳐 인증이 깨질 수 있던 문제

- 명령줄 `--uninstall`은 오버레이가 실행 중이면 아무것도 지우지 않고 종료 코드 2로 중단한다(실행 중인 인스턴스가 설정 파일과 host 등록을 되살리는 문제 방지). 트레이 "완전 제거…"는 영향 없음. Run 키 경로와 값 이름은 `UninstallService` 한 곳에서 정의한다
### Security
- 네임드 파이프 `CurrentUserOnly` + 서버 프로세스 실행 파일 경로 대조 후에만 쿠키 전달
- 로그에 쿠키 값과 토큰을 남기지 않음(개수/출처만 기록)

> P2(배포·운영 정비) 작업이 진행 중입니다. 이후 P2 작업의 결과는 각 작업에서 이 7.1.0 절에 추가합니다.

## 7.0.0 - 2026-09-18

- 기술 문서 v7.0.0 as-built 기준의 최초 구현: WPF 오버레이, Chrome MV3 확장 + Native Messaging + 네임드 파이프 브리지, 단일 파일 self-contained 배포, 로컬 설정/캐시 저장.
