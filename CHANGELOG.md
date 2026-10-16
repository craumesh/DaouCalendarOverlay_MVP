# Changelog

이 파일은 Keep a Changelog 형식을 따르고 버전은 유의적 버전(SemVer)을 따릅니다.
WPF 앱(EXE)과 Chrome 확장은 같은 버전 번호를 씁니다. 네이티브 브리지 프로토콜 버전은 별도로 `NativeBridgeProtocol.ProtocolVersion` 으로 관리합니다(현재 v2, 파이프 이름 `DaouCalendarOverlay.NativeBridge.v8`).
릴리스 이후에는 Chrome 확장 서비스 워커를 바꾸면 확장 버전을 반드시 올립니다(worker 파일명 `service-worker-v<버전>.js`, `ChromeExtensionInstaller` obsolete 목록, 패키징 테스트를 함께 갱신).

## 7.4.0 - 2026-10-16

### Added
- 최소화 막대(미니 바): 헤더의 `—` 버튼으로 오버레이를 높이 48 DIP의 막대(창 311×48 DIP)로 접고, 막대의 `□` 버튼으로 다시 펼칩니다. 막대는 날짜·요일(`M월 d일 ddd`)과 초 단위 시각(`HH:mm:ss`) 칸, 오늘 남은 일정 2줄(옅은 배경과 둥근 모서리의 구역), 동기화 실패 상태 점과 `□`·`✕` 버튼의 세 구역으로 나뉘고 구역 사이 간격은 6 DIP입니다.
- 막대의 오늘 일정은 종일 일정이 먼저, 그다음 시작 시각 순입니다. 이미 끝난 시간 일정은 빠지고(종일 일정은 항상 남음) 남은 일정이 없으면 `남은 일정 없음`을 보입니다. 남은 일정이 3건 이상이면 4초마다 한 줄씩 위로 순환하고, 마우스를 올려 두면 멈춥니다. 숨긴 캘린더만 적용하고 검색 필터는 적용하지 않습니다.
- 막대의 일정 영역을 누르면 막대에 붙여 오늘 일정 목록을 엽니다(막대 아래, 화면 아래쪽에 자리가 없으면 위). 끝난 일정도 포함하고, 일정을 누르면 같은 자리에서 상세가 열리며, 목록과 상세를 모두 닫으면 막대가 원래 크기와 위치로 돌아옵니다. 숨긴 캘린더만 적용하고 검색 필터는 적용하지 않습니다.
- 접기·펼치기 애니메이션(공유 요소 전환): 날짜·시계 블록이 접힐 때와 펼칠 때 하나의 요소처럼 막대 자리와 펼침 자리 사이를 이동하고, 나머지 내용은 페이드합니다. 접기는 200ms, 펼치기는 320ms(막대 내용 페이드아웃 120ms 뒤 카드 확대 200ms)입니다. 전환 도중 숨기거나 단계가 실패해도 이동하던 블록이 화면에 남지 않고 날짜·시계가 보입니다. Windows의 애니메이션 효과를 끄면 중간 프레임 없이 즉시 바뀝니다. 전환 한 번마다 창 위치·크기 변경은 1회뿐입니다.
- 막대에서 펼쳤다가 다시 접으면 원래 막대 위치로 정확히 돌아옵니다. 직전 전환을 메모리에만 기억해 창이 그대로면 직전 전환 앞의 위치로 되돌리며, 아래 가장자리 근처의 막대를 펼쳤다 접을 때 다른 자리에 남던 문제를 막습니다. 이 기억은 저장하지 않습니다.
- 최소화 상태 저장(`OverlayMinimized`): 접은 채 종료하면 같은 위치에서 막대로 다시 시작합니다. 접고 펼칠 때 최소화 상태와 창 위치를 한 번에 저장합니다.
- 작업 영역 가장자리 스냅(`SnapToScreenEdges`): 오버레이를 끌어 창(= 카드) 가장자리가 작업 영역 가장자리 12 DIP 안에 들면 붙습니다. 끄는 동안 시작 위치에 커서가 움직인 만큼을 더해 매번 새로 계산하므로(커서 기준) 스냅으로 밀린 만큼 잡은 지점과 커서가 어긋나지 않습니다. 모니터 배율(100%, 125%, 150% 등)에 맞춰 px로 환산하고, 제안된 위치가 걸친 모니터의 작업 영역을 기준으로 합니다. 끌기가 끝나면 앱 로그에 창, 작업 영역, 붙은 변을 한 줄(`[INFO] ui.snap: 드래그 끝: 창=(…) 작업영역=(…) 붙은 변=… 스냅=…`) 남깁니다. 창이 실제로 움직인 드래그마다 1줄이고, 클릭만 하고 움직이지 않으면 남기지 않습니다. 설정 창에 `창을 화면 가장자리에 붙이기(스냅)` 체크박스가 생겼고 기본은 켬입니다. 위치 잠금 중에는 끌 수 없으므로 붙지 않습니다.

### Changed
- 헤더 오른쪽 버튼이 `↻` `⚙` `—` `✕` 순서가 됐습니다. `—`는 막대로 최소화하고 `✕`가 숨기기(트레이로)입니다. 이전에는 `—`가 숨기기였습니다. 버튼이 하나 늘어난 만큼 헤더 첫 줄 버튼을 22×22(간격 1)로, `이번달` 버튼을 폭 38·글자 11로 줄여 월 제목 칸을 116 DIP로 확보했습니다.
- 창 사각형이 곧 카드 사각형입니다. 카드 바깥의 투명 여백(7 DIP)을 없애 창이 펼침 311×426 DIP, 막대 311×48 DIP가 됐습니다. 막대에 붙이는 목록 영역은 390 DIP(막대와의 간격 6 DIP 포함)이고, 목록·상세 오버레이의 여백은 펼침에서 9 DIP, 붙였을 때 막대 쪽 변에만 6 DIP입니다.
- settings.json의 `Left`/`Top`은 창 왼쪽 위(= 카드 왼쪽 위)를 뜻합니다. 키와 형식은 그대로입니다. 카드보다 7 DIP 바깥을 저장한 것은 7.3.1 이하와 7.4.0 초기 빌드입니다(7.3.1(커밋 `72dbfe0`)의 `MainWindow.xaml`도 `RootChrome`의 `Margin="7"`에 창 325×440이었습니다). 이 값은 마이그레이션 없이 그대로 읽으므로 7.3.1 이하에서 올려도 처음 한 번 카드가 최대 7 DIP 어긋날 수 있고, 작업 영역 밖으로 나온 만큼은 시작할 때 안으로 옮겨집니다. 다음 저장부터는 새 의미의 값이라 어긋남이 0입니다(실제 업그레이드 확인은 아직 하지 않았습니다).
- `Esc`와 `Alt+F4`는 이전처럼 오버레이를 트레이로 숨기고 닫지 않습니다. 막대 모드의 `Esc`는 상세, 붙인 목록, 창 숨기기 순서로 닫고 검색어는 지우지 않으며, `PageUp`/`PageDown`/`Home`은 무시합니다. 막대는 오늘 일정만 보여 주기 때문입니다. 접을 때 표시 달이 오늘이 속한 달로 돌아갑니다.
- 화면 밖으로 나간 창을 작업 영역 안으로 되돌리는 클램프가 창 사각형(= 눈에 보이는 카드)을 기준으로 합니다. 스냅으로 붙은 카드가 시작·DPI 변경·디스플레이 변경 때 안쪽으로 밀리지 않습니다.
- 설정 창에서 저장해도 최소화 상태와 창 위치는 바뀌지 않습니다. 이 보존 동작을 따로 떼어(`SettingsDialogMerge`) 시험으로 고정했습니다.
- settings.json에 키 `OverlayMinimized`(기본 `false`)와 `SnapToScreenEdges`(기본 `true`)를 추가만 했습니다. 두 키가 없는 7.3.1 이하의 파일은 펼침 모드와 스냅 켬으로 읽습니다. 7.3.1 앱은 모르는 키를 무시하고 읽으며, 7.3.1이 다시 저장하면 두 키가 빠지지만 7.4.0은 기본값으로 동작합니다.
- 앱과 확장 버전을 7.4.0으로 올렸습니다. Chrome 확장은 worker 본문 로직을 바꾸지 않고 파일 이름만 `service-worker-v740.js`로 바꿨습니다(이전 `service-worker-v731.js`는 정리 대상입니다). EXE를 교체한 뒤에는 `chrome://extensions`에서 확장을 새로고침해야 합니다.
- 네이티브 브리지 프로토콜 v2와 파이프 이름 `.v8`은 그대로입니다.

## 7.3.1 - 2026-10-02

### Changed
- 대상 프레임워크를 `net10.0-windows`(.NET 10 LTS)로 바꿨습니다. .NET 8 지원이 2026-11-10에 끝나기 때문입니다. 동작 변경은 없습니다.
- 게시 EXE 크기는 75,897,256 bytes입니다(7.3.0 72,099,811 bytes 대비 +3,797,445 bytes). 원인은 .NET 10 런타임 포함입니다.
- 개발 빌드(framework-dependent)를 실행하려면 .NET 10 데스크톱 런타임이 필요합니다. `publish.ps1`이 만드는 단일 EXE는 런타임을 포함하므로 따로 설치하지 않아도 됩니다.
- Chrome 확장은 코드 변경 없이 버전만 7.3.1로 올렸습니다(`service-worker-v731.js`, 이전 `service-worker-v730.js`는 정리 대상입니다). EXE를 교체한 뒤에는 `chrome://extensions`에서 확장을 새로고침해야 하며, 새로고침하기 전에는 상태 표시줄에 `Chrome 확장 새로고침 필요 (7.3.0 → 7.3.1)`이 표시됩니다.
- 네이티브 브리지 프로토콜 v2와 파이프 이름은 그대로입니다.
- 7.3.1의 실환경 수동 확인은 2026-10-06 이 PC에서 11건(오버레이 미실행 host 왕복 H2-b, M1~M7, 7.3.0 폴더·net8 빌드 잔재 정리 D-2·D-4·D-5)을 모두 수행해 확인했습니다. M7(재부팅 뒤 자동 시작)은 17:14 부팅 1회를 관찰한 것으로, 오버레이가 부팅 25초 뒤(17:14:55)에 시작됐고 시작 프로그램 값은 7.3.1 EXE였습니다. 확인한 부팅이 1회뿐이라 이후 부팅에서도 계속 뜨는지는 관찰이 더 필요합니다. 화면 항목(M1·M2의 육안 확인, M7의 로그인과 새로고침 조작)은 사용자 보고이고 스크린샷은 없습니다. 판정 기준 두 가지는 실제와 달랐습니다: 성공 경로의 앱 로그에는 `ver=7.3.1`이 찍히지 않고(M2), 제거로 host 등록이 사라졌던 뒤에는 확장의 실패 백오프 때문에 트레이 새로고침 한 번으로 바로 연결되지 않고 최대 5분이 걸릴 수 있습니다(M6). 시각·로그 근거는 `docs/verification-log.md`의 "수동 확인 대기 (7.3.1)"에 있습니다.

## 7.3.0 - 2026-09-30

### Added
- 세션 스냅샷: 조회가 200으로 성공할 때 DaouOffice 인증 쿠키를 `chrome.storage.session`(메모리)에만 찍어 둡니다. 디스크(`chrome.storage.local`)에는 저장하지 않습니다.
- 창 닫기 복원: Chrome 창을 모두 닫으면서 Chrome이 지운 인증 쿠키를, 창이 0개일 때 스냅샷으로 되살립니다(삭제 이벤트 뒤 예약 복원, 조회 직전 복원). 창을 모두 닫아도 Chrome이 백그라운드(`background` 권한)로 남아 있으면 동기화가 이어지는 것이 목표이며, 실환경 재확인 전입니다(수동 확인 R-A).
- 만료 갱신: 조회가 401 + `ROUTE-0006`(만료)을 받으면 갱신 요청(`POST /api/portal/public/auth/refresh/login`)을 1회 보내고 조회를 1회 다시 합니다. 갱신을 시도한 뒤 5분(갱신이 거절됐으면 30분) 동안은 다시 시도하지 않고, DaouOffice 탭이 열려 있으면 페이지가 갱신하도록 최대 3분 양보합니다. getConfig 응답부터 재는 시간 예산 `SYNC_BUDGET_MS`(40초) 안에 갱신 요청(최대 20초)·대기(0.5초)·재조회(최소 5초)가 들어가지 않으면 갱신을 건너뛰어(`budget`) 결과가 앱의 조회 lease 45초 안에 닿게 합니다.
- 로그아웃 불복원: 창이 열린 상태에서 인증 쿠키가 지워지면(로그아웃 등) 스냅샷을 바로 버려, 이후 창을 닫아도 로그아웃된 세션을 되살리지 않습니다. 토큰 교체(`overwrite`)에 따른 삭제는 예외입니다.
- JS 동작 시험 `tools/worker-mock-test.js`: 가짜 chrome으로 worker의 스냅샷·복원·갱신·폐기 시나리오를 검사하며, 0단계 게이트(`.claude/scripts/gate-extra.sh`)에서 항상 실행합니다.
- 상태 문구 "DaouOffice 세션 갱신을 기다리는 중입니다": 만료 401 + 갱신 대기(`waiting_tab`, `budget`)일 때만 표시합니다(ReasonCode `session_refresh_pending`).
- 로그 필드: 앱 로그의 결과 요약 줄(`bridge.result`, `bridge.pipe`, host 로그의 요청 수신 줄)에 `refreshState=<값>`과 `refreshStatus=<갱신 요청의 HTTP 상태, 응답이 없으면 0>`이 값이 있을 때만 붙고, 확장 콘솔에 `[SESSION]`·`[REFRESH]` 줄이 남습니다. 쿠키·토큰 값은 어디에도 남기지 않습니다.

### Changed
- `cookies` 권한이 manifest에 다시 들어갑니다(7.2.0에서 제거했던 권한). 쿠키 변경 이벤트(`chrome.cookies.onChanged`)는 스냅샷·복원 판단에만 쓰이고 동기화 트리거로는 쓰지 않습니다. 값은 Chrome 밖(Native Messaging, 앱, 로그)으로 나가지 않습니다.
- 확장 worker 파일을 `service-worker-v730.js`로 교체하고 이전 `service-worker-v720.js`는 정리 대상입니다.
- 포장 테스트 가드 개정: 금지 목록에서 `chrome.cookies`·`cookies.onChanged`를 풀고, `storage.local`·`spike` 금지를 더했으며, 시간 예산 부등식과 `refreshState`·`ROUTE-0006` 리터럴이 앱과 같은지 검사하는 테스트를 추가했습니다. 툴바 아이콘 즉시 동기화(`chrome.action.onClicked`)는 7.2.0부터 있던 기능이라 유지합니다.
- 포장 테스트 가드 추가: worker에 `windows.onRemoved`·`windows.onCreated` 리스너가 들어가지 못하게 금지 목록에 더했습니다(창 닫기 판단은 이벤트 시점의 창 개수 조회로만 합니다).
- postResult 결과에 선택 필드 2개(`refreshState`, `refreshStatus`)를 더했습니다. 필드가 없으면 판정은 7.2.0과 같습니다.

### 유지(바뀌지 않음)
- 앱과 확장이 주고받는 메시지 형식의 번호(`NativeBridgeProtocol.ProtocolVersion`, 현재 2)와 파이프 이름 `DaouCalendarOverlay.NativeBridge.v8`은 7.2.0 그대로입니다. 이 번호가 다르면 앱이 확장에 조회를 맡기지 않는데, 7.3.0은 조회 결과에 없어도 되는 항목 2개(`refreshState`, `refreshStatus`)만 덧붙였으므로 번호를 바꾸지 않았습니다. 그래서 EXE를 7.3.0으로 바꾼 뒤 확장을 새로고침하기 전까지도 조회가 끊기지 않고, 상태 표시줄에 `Chrome 확장 새로고침 필요 (7.2.0 → 7.3.0)`가 표시됩니다(코드 기준, 실환경 미확인).
- 앱의 조회 lease 45초, `settings.json`, getConfig 응답은 바뀌지 않습니다. 세션 유지를 끄는 설정은 두지 않으며 세션 유지는 항상 켜져 있습니다.

### 알려진 한계
- 확장을 새로고침하면 `chrome.storage.session`이 비워지므로, 새로고침 직후 첫 조회 성공 전에 Chrome 창을 모두 닫으면 다시 로그인해야 합니다(수동 확인 R-E).
- 페이지가 로그아웃한 직후 1초 안에 마지막 창을 닫으면 창 닫기 정리로 오인해 로그아웃된 쿠키를 복원할 수 있습니다. 서버가 로그아웃 때 세션을 무효화하는지는 미확인이며, 무효화돼 있으면 다음 조회의 `ROUTE-0004`에서 스냅샷을 버립니다(수동 확인 R-B).
- `cookies` 권한이 추가되는 업그레이드에서 Chrome이 권한 재승인을 묻거나 확장을 비활성화하는지는 미확인입니다(수동 확인 R-G).

## 7.2.0 - 2026-09-29

### Added
- Chrome 확장 서비스 워커 콘솔 진단 로그(`[SYNC]`/`[FETCH]`)
- 7.1.0이 남긴 세션 쿠키 캐시 키(`daouSessionCookieCache`)를 서비스 워커 시작 때 삭제

### Changed
- Chrome 확장이 DaouOffice 캘린더 API를 직접 조회(`fetch`, `credentials: "include"`, `redirect: "manual"`, 25초 시간 초과)하고 응답 원문과 전송 정보만 앱에 전달합니다.
- 인증·API 판정은 앱 `BridgeResultClassifier`가 합니다. 401 + `ROUTE-0004`는 재로그인 필요로 판정합니다.
- 네이티브 브리지 프로토콜 2, 파이프 `.v8`. 앱은 `protocolVersion`이 2가 아닌 확장에 조회를 지시하지 않습니다.
- 상태 문구가 바뀝니다(재로그인·네트워크).
- 200이 아닌 HTML 응답은 HTTP 오류로 표시합니다.

### Removed
- 쿠키 읽기(`chrome.cookies`), 세션 쿠키 캐시(`daouSessionCookieCache`), Cookie 헤더 전달, 앱의 `HttpClient` 조회
- `cookies` 권한과 쿠키 변경 트리거

### Fixed
- Chrome에서 로그아웃한 뒤에도 캐시된 옛 쿠키로 동기화가 계속되던 문제
- 확장을 새로고침한 뒤 수동 동기화까지 실패하던 문제

### Security
- 쿠키 값이 Chrome 밖(Native Messaging, host, 파이프, 앱, 로그)으로 나가지 않습니다.

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
