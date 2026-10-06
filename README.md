# DaouCalendarOverlay

다우오피스(DaouOffice) 캘린더를 Windows 데스크톱에서 확인할 수 있도록 만든 WPF 기반 오버레이 애플리케이션입니다.

Chrome에서 로그인한 다우오피스 세션을 활용하고, Chrome Extension과 Native Messaging을 통해 데스크톱 애플리케이션과 연결합니다. 별도의 브라우저 로그인 화면을 애플리케이션에 구현하지 않고 기존 Chrome 세션을 재사용하는 것이 핵심 구조입니다.

> 이 프로젝트는 특정 조직의 다우오피스 환경을 대상으로 한 개인/내부용 프로젝트입니다. 다우오피스의 공식 API 또는 공식 클라이언트가 아니며, 실제 사용 환경의 정책 및 서비스 변경에 따라 동작이 달라질 수 있습니다.

## 주요 기능

- Windows 데스크톱 오버레이 형태의 캘린더 UI
- 다우오피스 캘린더 일정 조회
- Chrome에 로그인된 다우오피스 세션 재사용
- Chrome Extension ↔ Native Messaging ↔ WPF 애플리케이션 연동
- 로컬 환경에서 일정 데이터 동기화
- 사용자별 로컬 설정 저장
- Publish 스크립트를 통한 배포용 빌드 지원
- 여러 캘린더에 공유된 일정은 `id` 기준 1건으로 합쳐 표시(소속 캘린더 이름은 모두 표기)
- 합쳐진 일정은 소속 캘린더를 모두 숨겨야 화면에서 사라짐

## 키보드 단축키

오버레이 창에 포커스가 있을 때 동작합니다.

| 키 | 동작 |
|---|---|
| `F5` | 강제 새로고침 |
| `PageUp` / `PageDown` / `Home` | 이전 달 / 다음 달 / 오늘. 검색창에 포커스가 있으면 텍스트 편집에 양보하므로 동작하지 않습니다. |
| `Ctrl+PageUp` / `Ctrl+PageDown` / `Ctrl+Home` | 이전 달 / 다음 달 / 오늘. 검색창 포커스와 관계없이 항상 동작합니다. |
| `Esc` | 열린 것부터 순서대로 닫습니다: 일정 상세 → 날짜 일정 목록 → (검색창에 포커스가 있고 검색어가 있으면) 검색어 비우기(`초기화` 버튼과 같이 검색 대상도 제목으로 되돌림) → 창 숨기기. 검색창에 포커스가 있고 검색어가 비어 있으면 창을 숨기지 않습니다. |

- 필터의 `초기화` 버튼(또는 검색창에서 `Esc`)은 검색 대상과 검색어를 되돌리는 동안 변경 이벤트를 억제하므로 달력을 한 번만 다시 그립니다.

## 기술 스택

- C#
- .NET 10 (LTS)
- WPF
- Chrome Extension
- Chrome Native Messaging
- HTTP(DaouOffice 캘린더 REST API, GET) 기반 일정 조회
- MVVM 구조

## 프로젝트 구조

```text
DaouCalendarOverlay_MVP/
├─ DaouCalendarOverlay.sln
├─ README.md
├─ CHANGELOG.md
├─ .gitignore
├─ .gitattributes
├─ publish.ps1
├─ tools/
├─ docs/
├─ tests/
│
└─ DaouCalendarOverlay/
   ├─ Program.cs
   ├─ App.xaml
   ├─ App.xaml.cs
   ├─ app.manifest
   ├─ GlobalUsings.cs
   ├─ DaouCalendarOverlay.csproj
   ├─ Properties/PublishProfiles/win-x64.pubxml
   │
   ├─ Models/
   ├─ ViewModels/
   ├─ Views/
   ├─ Services/
   ├─ ChromeExtension/
   └─ Assets/
```

### 주요 구성 요소

| 경로 | 역할 |
|---|---|
| `Models/` | 캘린더 일정 및 애플리케이션 데이터 모델 |
| `ViewModels/` | WPF 화면 상태 및 MVVM 로직 |
| `Views/` | WPF UI |
| `Services/` | 다우오피스 연동, 동기화, 설정 등 핵심 서비스 |
| `ChromeExtension/` | Chrome Extension 및 Native Messaging 관련 파일 |
| `Assets/` | 아이콘 및 기타 리소스 |
| `publish.ps1` | 릴리스 publish 스크립트(버전 산출물명, PDB 분리, 서명 파라미터) |
| `DaouCalendarOverlay/Properties/PublishProfiles/win-x64.pubxml` | 릴리스 publish 전용 설정(win-x64 self-contained single-file, portable PDB) |
| `tools/` | host 스폰 비용 측정 스크립트, .NET 10 비교 측정 스크립트(`net10-trial.ps1`) |
| `docs/` | 기술 설계 문서(`DaouCalendarOverlay_Technical_Documentation.html`), 검증 기록(`verification-log.md`), .NET 10 이전 시험 기록(`net10-migration.md`), 작업 지시서(`OPUS_WORK_ORDER_2026-09-21.md`) |
| `tests/` | xUnit 테스트 프로젝트(`tests/DaouCalendarOverlay.Tests`) |
| `CHANGELOG.md` | 버전별 변경 이력 |

## 동작 구조

전체적인 데이터 흐름은 다음과 같습니다.

```text
┌──────────────────────┐
│       Chrome         │
│  DaouOffice 로그인   │
└──────────┬───────────┘
           │
           │ 브라우저 세션으로 직접 조회
           ▼
┌──────────────────────┐   fetch   ┌──────────────────────┐
│   Chrome Extension   │ ────────▶ │     DaouOffice       │
│                      │ ◀──────── │      Calendar        │
└──────────┬───────────┘   응답    └──────────────────────┘
           │
           │ Native Messaging(응답 원문과 전송 정보)
           ▼
┌──────────────────────┐
│ DaouCalendarOverlay  │
│      (WPF/.NET)      │
│ 결과 판정·일정 표시  │
└──────────────────────┘
```

이 구조에서는 WPF 애플리케이션이 다우오피스의 로그인 페이지를 직접 구현하거나 사용자의 비밀번호를 별도로 보관하는 대신, 사용자가 Chrome에서 이미 인증한 세션을 활용합니다. Chrome 확장이 그 로그인 세션으로 다우오피스를 직접 조회(`fetch`)하고, 응답 원문과 전송 정보를 Native Messaging으로 앱에 넘기면 앱이 성공·재로그인 필요·오류를 판정합니다. 쿠키는 Chrome 밖으로 나가지 않습니다. host는 쿠키 없이 확장의 요청(`getConfig`, `postResult`)과 앱의 응답을 중계합니다.

동기화 주기는 앱의 타이머가 아니라 `CalendarBridgeServer`가 결정합니다. Chrome 확장이 30초마다 `getConfig`를 물어보고, 앱이 가져올 때가 됐다고 답한 회차에만 일정을 가져옵니다(성공 후 다음 시도 시각은 설정의 새로고침 주기, 실패 시 backoff로 정해집니다). 확장 알람은 기본 30초 주기이며, 앱이 꺼져 있는 등의 이유로 native host 연결이 연속 3회 실패하면 1분 → 2분 → 5분으로 늘어났다가 연결에 성공하면 30초로 복귀합니다.

달력의 '오늘' 표시 기준은 Windows 로컬 날짜입니다. 자정이 지나면 1초 시계 틱에서 오늘 배지가 이동하고(이전 오늘이 속한 달을 보고 있었고 달이 바뀌었으면 새 달로 이동), 날짜가 바뀔 때마다 한 번 강제 동기화를 요청합니다. (일정이 어느 날짜에 속하는지 판정할 때는 KST +09:00을 사용합니다.)

Chrome이 Native Messaging host로 같은 EXE를 실행할 때는 `Program.Main`이 실행 인자를 보고 WPF 창/리소스를 만들지 않은 채 stdin↔Named Pipe 중계만 수행한 뒤 종료합니다. 이때의 기록은 `host-yyyyMMdd.log`에 남습니다.

## 요구 사항

- Windows
- .NET 10 SDK (`net10.0-windows` 타깃 빌드에는 SDK 10 이상이 필요합니다)
- Google Chrome 120 이상 (권장)
- Microsoft Edge (시험 지원, 선택)
- 다우오피스 계정
- 다우오피스 캘린더 사용 권한

지원 환경 요약: OS는 Windows 10/11 x64만 지원합니다. 브라우저는 Chrome 120 이상이 정식 지원이고, Edge(Chromium)는 같은 확장을 별도로 로드하는 조건의 시험 지원입니다. Whale, Brave, Firefox는 지원하지 않습니다.
브라우저 프로필은 하나만 쓰는 환경을 가정하며(다중 프로필·시크릿 창 동시 사용은 보장하지 않음), 일정의 날짜 판정은 KST(+09:00) 기준입니다.
자세한 표는 기술 문서의 "지원 환경 매트릭스" 절을 참고하세요.

개발/빌드 환경에서는 Visual Studio 또는 .NET CLI를 사용할 수 있습니다.

## 빌드

### Visual Studio

1. `DaouCalendarOverlay.sln`을 Visual Studio에서 엽니다.
2. `Debug` 또는 `Release` 구성을 선택합니다.
3. 프로젝트를 빌드합니다.

### .NET CLI

프로젝트 루트에서:

```powershell
dotnet restore
dotnet build
```

Release 빌드:

```powershell
dotnet build -c Release
```

개발 빌드(`dotnet build`)는 framework-dependent입니다. 출력 폴더(`DaouCalendarOverlay\bin\Release\net10.0-windows\`)에는 .NET 런타임이 복사되지 않으므로, 이 EXE를 실행하려면 PC에 .NET 10 데스크톱 런타임(또는 .NET 10 SDK)이 설치되어 있어야 합니다. 런타임을 포함한 자체 포함(self-contained) 단일 EXE는 아래 `publish.ps1`로만 만듭니다(publish 설정은 `DaouCalendarOverlay/Properties/PublishProfiles/win-x64.pubxml`에만 있습니다). 개발 빌드의 PDB는 `portable` 형식으로 EXE 옆에 생성됩니다.

## 배포

저장소에 포함된 `publish.ps1`을 사용해 배포용 빌드(win-x64 self-contained 단일 EXE)를 생성할 수 있습니다. self-contained/single-file 설정은 publish 프로파일(`win-x64.pubxml`)에서 가져오므로 개발 빌드에는 영향을 주지 않습니다.

PowerShell에서:

```powershell
.\publish.ps1
```

PowerShell의 실행 정책 때문에 스크립트 실행이 차단되는 경우에는 현재 사용자 범위의 정책을 확인한 후 필요한 경우 다음과 같이 일시적으로 실행할 수 있습니다.

```powershell
powershell -ExecutionPolicy Bypass -File .\publish.ps1
```

파라미터:

| 파라미터 | 설명 |
|---|---|
| `-Version` | 생략하면 csproj의 `<Version>`을 씁니다. 지정하면 csproj `<Version>`과 같아야 하며, 다르면 `Version mismatch` 오류로 스크립트가 중단됩니다. 버전을 바꾸는 옵션이 아니라 의도한 버전인지 확인하는 옵션입니다. 버전을 올리려면 먼저 `DaouCalendarOverlay.csproj`의 `Version`/`AssemblyVersion`/`FileVersion`과 `app.manifest`의 `assemblyIdentity version`을 수정하세요(EXE 속성 창의 파일 버전과 트레이·설정창·로그에 보이는 버전을 일치시키기 위함). |
| `-OutputDir` | 출력 폴더(기본: 저장소 루트의 `publish`). 실행할 때마다 기존 폴더를 통째로 지우고 다시 만들므로 다른 파일이 있는 폴더를 지정하지 마세요. |
| `-CertificateThumbprint` / `-TimestampUrl` | 코드 서명 인증서의 SHA1 지문과 타임스탬프 서버(기본 `http://timestamp.digicert.com`). 지문을 주면 `signtool`로 최종 EXE를 서명하고, 비워 두면 서명을 건너뜁니다. |
| `-DryRun` | 빌드하지 않고 버전·출력 경로·산출물 이름 등 계획만 출력합니다(파일을 만들거나 지우지 않음). 버전 검사는 DryRun에서도 수행됩니다. |

예:

```powershell
.\publish.ps1 -DryRun
.\publish.ps1 -Version 7.3.1
.\publish.ps1 -CertificateThumbprint <인증서 SHA1 지문>
```

산출물:

```text
publish\DaouCalendarOverlay-<버전>.exe   (배포할 단일 EXE, 예: DaouCalendarOverlay-7.3.1.exe)
publish\symbols\*.pdb                    (크래시 분석용 PDB, 배포하지 않고 EXE 버전별로 보관)
```

- `dotnet restore`·`dotnet publish`·`signtool` 중 하나라도 실패하면 스크립트는 비정상 종료 코드로 끝나며 `Publish completed`가 출력되지 않습니다(이전 스크립트는 실패해도 `Publish completed`를 출력했습니다).
- 코드 서명 인증서를 아직 확보하지 않아 기본 산출물은 미서명입니다. 인터넷이나 메신저로 받은 EXE를 처음 실행할 때 Windows SmartScreen 경고가 표시될 수 있습니다.
- 빌드 전제조건과 릴리스 순서는 기술 문서의 "빌드 전제조건·릴리스 절차" 절을 참고하세요.

## 최초 실행

일반적인 사용 순서는 다음과 같습니다.

1. Google Chrome을 설치합니다.
2. Chrome에서 다우오피스에 로그인합니다.
3. `DaouCalendarOverlay-<버전>.exe`를 실행합니다. 실행하면 먼저 확장 파일이 `%LOCALAPPDATA%\DaouCalendarOverlay\ChromeExtension`에 추출되고 Native Messaging Host가 등록된 뒤 첫 실행 설정창이 열립니다. DaouOffice 주소와 캘린더 ID를 입력해 저장하면 추출·등록을 한 번 더 수행합니다. 앱은 기동할 때마다 이 두 작업을 다시 수행하며, 실패하면 상태 표시에 `Native host 등록 실패` 또는 `확장 파일 설치 실패`가 들어간 문구가 표시됩니다(아래 "문제 해결" 참고).
4. Chrome에서 `chrome://extensions`를 열고 **개발자 모드**를 켠 뒤 **압축해제된 확장 프로그램을 로드합니다**로 위 폴더를 선택합니다. 트레이 메뉴의 **"Chrome 확장 폴더 열기"** 로 이 폴더를 바로 열 수 있습니다. Extension 버전이 올라간 빌드로 교체했다면 `chrome://extensions`에서 해당 확장을 **새로고침**해야 새 Service Worker가 적용됩니다.
5. 애플리케이션 하단 상태 표시에서 동기화 상태를 확인합니다(아래 "동작 확인" 참고).
6. (Edge를 쓰는 경우) 설정창의 **"Edge에도 Native Messaging 등록"** 이 켜져 있어야 하며(기본 켜짐), Edge에서도 `edge://extensions`를 열고 개발자 모드를 켠 뒤 같은 unpacked 확장(`%LOCALAPPDATA%\DaouCalendarOverlay\ChromeExtension`)을 로드해야 합니다. Edge 지원은 시험 지원입니다.

DaouOffice 주소는 `https://회사이름.daouoffice.com` 형식만 허용합니다. `http://`, 다른 도메인, `daouoffice.com`(회사 이름 없는 주소)은 설정창에서 거부되며, 기존 설정에 이런 주소가 들어 있으면 앱 시작 시 설정창이 열리고 거부 사유가 표시됩니다.

Chrome Extension ID 및 Native Messaging 설정은 프로젝트의 Extension/Native Messaging 관련 파일을 기준으로 구성되어 있습니다.

### 동작 확인

애플리케이션 하단 상태 표시는 정상 경로에서 다음 순서로 바뀝니다.

```text
Chrome 백그라운드 대기 → 동기화 요청 중… → 동기화 중… → 정상 · 동기화 HH:mm
```

- 앱이 기동 직후 곧바로 동기화를 요청하므로 첫 단계 `Chrome 백그라운드 대기`는 거의 보이지 않고 바로 `동기화 요청 중…`이 보일 수 있습니다.
- 이전에 저장된 캐시가 있으면 첫 동기화 전까지 대기 문구 뒤에 `· 캐시 MM-dd HH:mm` 접미가 붙습니다(예: `Chrome 백그라운드 대기 · 캐시 09-21 08:30`). 저장된 조회 범위가 오늘을 포함하지 않으면 `· 캐시(범위 밖) · 캐시 MM-dd HH:mm` 순서로 두 접미가 함께 붙습니다. 동기화가 한 번 성공하면 접미는 사라집니다.
- 상태 텍스트에 마우스를 올리면 `마지막 갱신 yyyy-MM-dd HH:mm` 툴팁으로 마지막으로 화면에 반영된 데이터 시각을 확인할 수 있습니다.
- 확장 heartbeat를 한 번도 받지 못했거나 95초 넘게 받지 못하면 health 평가(20초 주기)에서 `Chrome 확장 연결 대기`로 바뀝니다(재로그인 필요·설정 오류 상태에서는 그 문구를 유지합니다). 동기화에 성공한 적이 있으면 `Chrome 확장 연결 대기 · 마지막 HH:mm`처럼 마지막 성공 시각이 붙습니다.

## 설치 · 업그레이드 · 제거

### 설치

1. `publish.ps1`이 만든 `DaouCalendarOverlay-<버전>.exe`(현재 `DaouCalendarOverlay-7.3.1.exe`)를 둘 폴더에 복사한 뒤 실행합니다.
2. 처음 실행하면 설정창이 열립니다. DaouOffice 주소(`https://회사이름.daouoffice.com`)와 캘린더 ID를 입력하고 저장합니다.
3. Chrome에서 `chrome://extensions`를 열고 **개발자 모드**를 켠 뒤 **압축해제된 확장 프로그램을 로드합니다**로 `%LOCALAPPDATA%\DaouCalendarOverlay\ChromeExtension` 폴더를 선택합니다. 트레이 메뉴의 **"Chrome 확장 폴더 열기"** 로 이 폴더를 바로 열 수 있습니다.

앱은 기동할 때마다 확장 파일을 위 폴더에 추출하고, Native Messaging manifest(`%LOCALAPPDATA%\DaouCalendarOverlay\NativeMessaging\com.daou.calendar_overlay.json`)의 `path`를 **현재 실행 중인 EXE 경로로 다시 기록**합니다. EXE를 다른 폴더로 옮겼다면 새 위치에서 한 번 실행하면 등록이 갱신됩니다.

### 업그레이드

1. 트레이 아이콘 우클릭 → **종료**로 앱을 끕니다. 실행 중이면 EXE 파일이 잠겨 있어 교체가 실패합니다.
2. EXE 파일을 새 버전으로 교체합니다.
3. 새 EXE를 실행합니다. 이때 확장 파일이 새 버전으로 추출되고 Native Messaging manifest의 `path`가 새 EXE 경로로 갱신됩니다.
4. `chrome://extensions`에서 확장을 **새로고침**합니다. 새로고침하지 않으면 이전 Service Worker가 계속 동작하며, 확장 버전이 바뀐 업그레이드라면 상태 표시줄에 `Chrome 확장 새로고침 필요 (이전 버전 → 새 버전)`이 표시됩니다. 7.0.0 확장은 버전을 앱에 보내지 않으므로 7.0.0에서 올린 경우에는 `Chrome 확장 새로고침 필요 (7.0.0 이하 → 7.3.1)`으로 표시됩니다.

7.2.0: EXE 실행 후 chrome://extensions에서 확장 새로고침 필수. 7.2.0은 네이티브 브리지 프로토콜을 v2로 올렸기 때문에, 새로고침하기 전의 7.1.0 확장에는 앱이 조회를 지시하지 않습니다. 이때는 `Chrome 확장 새로고침 필요 (7.1.0 → 7.2.0)`가 표시되고 캐시 표시만 유지될 것으로 예상합니다(실환경 미확인).

7.3.0: EXE 실행 후 chrome://extensions에서 확장 새로고침 필수. 7.3.0은 앱과 확장이 주고받는 메시지 형식의 번호(프로토콜 v2)를 7.2.0 그대로 두었기 때문에, 확장을 새로고침하기 전 7.2.0 확장이 남아 있는 동안에도 앱이 조회를 계속 맡기고 상태 표시줄에 `Chrome 확장 새로고침 필요 (7.2.0 → 7.3.0)`가 표시될 것으로 예상합니다(실환경 미확인).
- 7.3.0 manifest에 `cookies` 권한이 다시 추가됩니다. 2026-10-01 이 PC에서 확장을 새로고침했을 때는 권한 재승인 요청이나 비활성화 표시를 보지 못했습니다(사용자 보고, 수동 확인 R-G). 다른 환경에서 확장이 비활성화되면 chrome://extensions에서 다시 켜세요.
- 확장을 새로고침하면 세션 스냅샷(`chrome.storage.session`)이 비워집니다. 새로고침 직후 첫 동기화가 성공하기 전에 Chrome 창을 모두 닫으면 다시 로그인해야 합니다(알려진 한계, 수동 확인 R-E).
- 창을 모두 닫아도 Chrome이 백그라운드(`background` 권한)로 남아 있으면 동기화가 이어지도록 만들었습니다. 2026-10-01 이 PC에서 창을 모두 닫은 채 약 30분 동안 조회가 이어지고, 토큰이 만료된 시점에 자동 갱신 뒤 조회가 성공한 것을 확인했습니다(수동 확인 R-A). 창이 열린 상태에서 로그아웃하면 스냅샷을 버려 그 세션을 되살리지 않습니다. 단 로그아웃 직후 1초 안에 마지막 창을 닫으면 창 닫기로 오인해 복원할 수 있으며, 이 경우는 실환경에서 확인하기 전입니다(수동 확인 R-B).
- DaouOffice 탭이 열려 있을 때 토큰이 만료되면 확장은 갱신 요청을 보내지 않고 페이지가 갱신하도록 양보합니다. 2026-10-01 이 PC에서 탭을 연 채 만료를 맞아 상태 문구 `DaouOffice 세션 갱신을 기다리는 중입니다`가 보이고, 확장이 갱신 요청을 보내지 않은 채 DaouOffice 페이지가 스스로 갱신해 이후 조회가 성공한 것을 확인했습니다(수동 확인 R-D, 양보까지). 양보한 지 3분이 넘으면 확장이 넘겨받는 경로는 실환경에서 확인하기 전입니다.
- 세션 유지를 끄는 설정은 없으며 항상 켜져 있습니다. `settings.json`은 바뀌지 않습니다.
- 7.3.0 실환경 수동 확인 상태(2026-10-01~02, 이 PC): R-A·R-C·R-D·R-G·R-H 확인(R-D는 양보까지, R-H는 2026-10-02 1회 부팅만 확인), R-B 일부 확인, R-F 미확인, R-E 알려진 한계입니다. 시각과 로그 근거는 `docs/verification-log.md`의 7.3.0 절에 있습니다.

7.3.1: EXE 실행 후 chrome://extensions에서 확장 새로고침 필수. 7.3.1은 확장 코드를 바꾸지 않고 버전 번호만 7.3.1로 올렸습니다. EXE를 교체한 뒤 새로고침하기 전까지는 상태 표시줄에 `Chrome 확장 새로고침 필요 (7.3.0 → 7.3.1)`가 표시됩니다.
- 대상 프레임워크가 .NET 10(`net10.0-windows`)입니다. 게시 EXE는 런타임을 포함하므로 사용자 PC에 따로 설치할 것이 없습니다.
- 앱과 확장이 주고받는 메시지 형식의 번호(프로토콜 v2)는 그대로입니다.
- 7.3.1 실환경 수동 확인 상태(2026-10-06, 이 PC): 오버레이 미실행 host 왕복(H2-b), M1~M7, D-2·D-4·D-5 11건을 모두 확인했습니다. M7(재부팅 뒤 자동 시작)은 부팅 1회 관찰이라 이후 부팅에서도 계속 뜨는지는 관찰이 더 필요합니다. M1·M2의 육안 확인 항목과 M7의 로그인·새로고침 조작은 사용자 보고(스크린샷 없음)이고, Edge에서의 동작은 확인 범위 밖입니다. 제거 뒤 다시 실행하면 확장이 바로 붙지 않고 최대 5분 걸릴 수 있습니다(확장의 실패 백오프). 시각과 로그 근거는 `docs/verification-log.md`의 "수동 확인 대기 (7.3.1)" 절에 있습니다.

설정·캐시·로그는 `%LOCALAPPDATA%\DaouCalendarOverlay`에 있으므로 EXE를 교체해도 유지됩니다.

### 제거

제거 방법은 두 가지입니다. 어느 쪽이든 확인 창 2단계(등록 해제 확인 → 사용자 데이터 폴더 삭제 여부)를 거친 뒤 결과 창에 실제로 지운 항목과 실패한 항목을 보여 줍니다(명령줄은 그 전에 오버레이 실행 여부를 먼저 확인합니다). 이미 없는 항목은 건너뜁니다.

- 트레이 아이콘 우클릭 → **완전 제거…**: 결과 창을 닫으면 앱이 종료됩니다. 사용자 데이터 삭제를 선택했다면 폴더는 앱이 종료된 직후 삭제됩니다.
- 명령줄: `DaouCalendarOverlay-<버전>.exe --uninstall` (예: `DaouCalendarOverlay-7.3.1.exe --uninstall`. `/uninstall`, `-uninstall`도 인식). 시작하자마자 오버레이가 실행 중인지 확인합니다. 실행 중이면 확인 창을 띄우지 않고 **아무것도 지우지 않은 채** "오버레이를 먼저 종료하거나 트레이의 **완전 제거…** 를 사용하라"는 안내 창을 보여 주고 종료 코드 2로 끝납니다(실행 중인 오버레이는 제거 뒤에도 동기화·창 위치·설정 저장 때 `settings.json`을 다시 만들고, 설정 저장 때 Native Messaging Host 등록을 되살리기 때문입니다). 이 경우 트레이 아이콘 우클릭 → **종료**로 오버레이를 끈 뒤 다시 실행하거나 트레이의 **완전 제거…** 를 쓰세요. 종료 코드는 완료·취소 0, 실패한 항목이 있으면 1, 오버레이 실행 중이라 중단하면 2입니다.

| 항목 | 위치 |
|---|---|
| 시작 프로그램 등록 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 의 `DaouCalendarOverlay` 값 |
| Chrome Native Messaging Host | `HKCU\Software\Google\Chrome\NativeMessagingHosts\com.daou.calendar_overlay` |
| Edge Native Messaging Host | `HKCU\Software\Microsoft\Edge\NativeMessagingHosts\com.daou.calendar_overlay` |
| (선택) 사용자 데이터 | `%LOCALAPPDATA%\DaouCalendarOverlay` (settings.json, calendar-cache.json, logs, ChromeExtension, NativeMessaging manifest 포함) |

제거 후에도 남는 것:

- Chrome 확장은 자동으로 제거되지 않습니다. `chrome://extensions`(Edge는 `edge://extensions`)에서 직접 제거하세요.
- EXE 파일은 자동으로 삭제되지 않습니다. 제거가 끝난 뒤 수동으로 삭제하세요. 제거 후 EXE를 다시 실행하면 등록이 다시 만들어집니다.

> **주의:** 등록을 먼저 해제하지 않고 EXE만 지우면 Native Messaging Host 등록이 남아 Chrome이 존재하지 않는 경로의 EXE를 계속 실행하려 하고, 시작 프로그램 등록도 남아 Windows 로그인 때마다 없는 파일을 실행하려 합니다. EXE를 지우기 전에 반드시 위 방법으로 제거하세요.

## 로컬 설정 및 인증 정보

애플리케이션의 사용자별 설정은 로컬 사용자 환경에 저장됩니다.

```text
%LOCALAPPDATA%\DaouCalendarOverlay\
```

- 자동 새로고침은 1~1440분만 허용합니다. 범위 밖 값은 설정창에서 거부되며, settings.json에 남아 있던 범위 밖 값은 앱 기동 시 자동으로 보정됩니다.
- 캘린더 ID는 숫자 1~19자리만 허용됩니다.
- `RegisterEdge`(설정창 "Edge에도 Native Messaging 등록")는 기본 켜짐이며, 끄고 저장하면 Edge의 NativeMessagingHosts 키(`HKCU\Software\Microsoft\Edge\NativeMessagingHosts\com.daou.calendar_overlay`)를 제거합니다. 기존 settings.json에 이 키가 없으면 켜짐으로 간주합니다.
- 투명도 슬라이더(`UiOpacity`) 100%는 완전 불투명이 아니라 최대 약 90%(배경 alpha 상한 230)입니다. 100%에서도 창 뒤 배경이 약간 비쳐 보이는 것이 정상 동작입니다.
- 캘린더 표시 이름은 settings.json의 `CalendarNames`(캘린더 ID → 마지막으로 확인한 이름)에 기억되어, 조회 범위에 일정이 없는 캘린더도 컨텍스트 메뉴 "캘린더 표시"에서 ID 대신 이름으로 보입니다. 이름은 동기화로 새 이름이 확인될 때만 저장되며, 기존 settings.json에 이 키가 없으면 빈 사전으로 시작합니다.

`settings.json`과 `calendar-cache.json`은 같은 폴더에 GUID 이름의 임시 파일(`settings.json.<guid>.tmp`)을 만들어 기록한 뒤 원자적으로 교체합니다. 같은 파일에 대한 동시 저장은 경로별로 직렬화되므로, 투명도(450ms)와 창 위치(400ms) 저장 틱이 겹쳐도 파일이 손상되거나 앱이 종료되지 않습니다.

저장이 실패하면(예: 파일이 읽기 전용이거나 다른 프로그램이 파일을 잠근 경우) 앱은 종료되지 않고 상태 표시줄에 `설정 저장 실패 · 로그 확인`(캐시는 `캐시 저장 실패 · 로그 확인`)을 표시하며, 실패한 경로와 예외를 아래 로그 파일에 남깁니다.

개발 과정에서 사용하는 계정 정보, 세션 Cookie, 토큰, 비밀번호 등의 민감한 값은 Git 저장소에 저장하지 않는 것을 원칙으로 합니다. 앱은 쿠키를 받지 않으며 쿠키는 Chrome 밖으로 나가지 않습니다.

다음과 같은 파일이나 값은 저장소에 커밋하지 마세요.

```text
.env
.env.*
secrets.json
appsettings.Local.json
비밀번호
세션 Cookie
Bearer Token
API Key
개인 인증서/Private Key
```

`.gitignore`에는 일반적인 로컬 설정 및 비밀값 파일을 제외하도록 구성되어 있습니다.

### 로그

애플리케이션은 동작 기록을 다음 위치에 파일로 남깁니다.

```text
%LOCALAPPDATA%\DaouCalendarOverlay\logs\overlay-yyyyMMdd.log   (오버레이 GUI 모드)
%LOCALAPPDATA%\DaouCalendarOverlay\logs\host-yyyyMMdd.log      (Chrome이 실행하는 Native Messaging Host 모드)
```

- 날짜별 파일로 기록하며, 파일 하나가 1MB를 넘으면 롤링하여 최대 5개(`overlay-yyyyMMdd.log`, `.1` ~ `.4`)까지 보관합니다.
- 트레이 아이콘 우클릭 메뉴의 **"로그 폴더 열기"** 로 위 폴더를 바로 열 수 있습니다.
- 로그에 응답 본문과 쿠키가 남지 않습니다. 결과 줄에는 status·contentType·bodyLength 같은 전송 정보와 판정 코드만 남습니다. 다만 API 오류 응답의 `message` 필드는 상태 문구로 쓰이므로 로그에 남을 수 있습니다.

### Chrome Extension의 고정 Key

Extension의 고정 ID를 유지하기 위해 Extension manifest에 공개용 `key` 값이 포함될 수 있습니다.

이 값은 다우오피스 계정 비밀번호나 세션 인증정보와는 다른 값이며, Extension ID를 고정하기 위한 공개 구성 요소입니다.

### 일정 캐시

일정 캐시 파일 `calendar-cache.json`에는 마지막 정상 동기화 시각, 그때 조회한 범위(`RangeFrom`/`RangeTo`), 일정 객체가 평문 JSON으로 저장됩니다. 앱을 다시 켜면 이 캐시를 먼저 표시하는데, 오늘 날짜(KST 기준)가 저장된 범위 밖이면(예: 다음 달을 보다가 종료한 경우, 또는 범위 정보가 없는 이전 버전 캐시) 캐시 일정을 그대로 보여 주되 상태 표시줄 문구 뒤에 `· 캐시(범위 밖)`를 붙여 알려 주고 즉시 강제 동기화를 수행합니다. 정상 동기화가 한 번 성공하면 이 접미는 사라집니다.

## 보안 관련 주의사항

이 프로젝트는 Chrome의 로그인 세션을 활용하기 때문에 다음 사항에 주의해야 합니다.

- 신뢰할 수 없는 Chrome Extension을 설치하지 않습니다.
- Extension의 권한 범위를 불필요하게 확대하지 않습니다.
- Native Messaging host는 Named Pipe 서버가 같은 Windows 계정이 실행한 동일 EXE일 때만 요청과 조회 결과를 전달합니다(다르면 전송하지 않고 오류를 반환). 쿠키는 Chrome 밖으로 나가지 않으며, host가 중계하는 요청과 조회 결과에는 쿠키가 없습니다.
- 응답 본문, 쿠키, 토큰을 로그에 출력하지 않습니다(로그에 응답 본문과 쿠키가 남지 않습니다).
- 디버깅 로그에 개인정보가 남지 않도록 합니다.
- GitHub 저장소를 Public으로 변경하기 전에 전체 소스와 Git history를 확인합니다.
- 과거 커밋에 비밀번호나 토큰이 들어갔다면 현재 파일에서 삭제하는 것만으로 충분하지 않을 수 있으므로 해당 자격 증명을 폐기/재발급하고 Git history도 정리해야 합니다.

## Git 사용

이미 생성된 프로젝트를 처음 Git 저장소로 만드는 경우:

```powershell
git init
git add .
git status
git commit -m "Initial commit"
```

GitHub에 빈 Repository를 만든 후:

```powershell
git branch -M main
git remote add origin https://github.com/<YOUR_USERNAME>/DaouCalendarOverlay_MVP.git
git push -u origin main
```

이후 변경 사항은:

```powershell
git add .
git commit -m "Describe your change"
git push
```

형태로 반영합니다.

## 개발 메모

이 프로젝트의 핵심 설계는 다음과 같습니다.

1. WPF 애플리케이션이 데스크톱 UI와 일정 표시를 담당합니다.
2. Chrome Extension은 Chrome 환경과 데스크톱 애플리케이션 사이의 연결 지점입니다.
3. Native Messaging을 통해 Extension과 WPF 애플리케이션이 통신합니다.
4. 다우오피스의 기존 브라우저 로그인 세션을 활용하여 별도의 애플리케이션 로그인 절차를 최소화합니다.
5. 사용자별 로컬 설정은 애플리케이션 데이터 영역에 저장합니다.

설계와 동작 계약의 세부는 기술 문서 [docs/DaouCalendarOverlay_Technical_Documentation.html](docs/DaouCalendarOverlay_Technical_Documentation.html)에 있습니다. 제품 범위는 "요구사항과 비목표" 절, 상태 표시 문구가 바뀌는 조건은 "상태 머신 전이표" 절, 로그 위치와 장애 진단 순서는 "로그·진단 절차" 절, 보안 가정과 남은 위험은 "위협 모델" 절, 아직 하지 않은 작업은 "백로그" 절을 참고하세요.

### .NET 10 이전 검토

- .NET 8 지원이 2026-11-10에 끝나므로 7.3.1부터 `net10.0-windows`(.NET 10 LTS)를 대상으로 합니다. 7.3.0까지는 `net8.0-windows`였습니다.
- 2026-09-23 시험 측정 결과(빌드·publish·EXE 크기·테스트·host 모드 왕복 비교)는 [docs/net10-migration.md](docs/net10-migration.md)를 참고하세요.

## 문제 해결

### Extension과 애플리케이션이 연결되지 않는 경우

다음 항목을 순서대로 확인합니다.

1. Chrome Extension이 정상적으로 설치되어 있는지 확인
2. Extension ID가 현재 manifest 설정과 일치하는지 확인
3. Native Messaging Host가 정상적으로 등록되어 있는지 확인
4. Native Messaging Host가 가리키는 실행 파일 경로가 실제 파일과 일치하는지 확인
5. Chrome을 완전히 재시작한 후 다시 시도
6. WPF 애플리케이션의 로그 확인 (`%LOCALAPPDATA%\DaouCalendarOverlay\logs\overlay-yyyyMMdd.log`, Native Messaging Host 쪽은 같은 폴더의 `host-yyyyMMdd.log`)
7. 실행 중인 오버레이 EXE와 Native Messaging Host manifest의 `path`가 **같은 파일**인지 확인(다르면 host가 전송을 거부합니다. 오버레이를 재시작하면 manifest가 현재 EXE 경로로 갱신됩니다)

### 일정이 동기화되지 않는 경우

1. Chrome에서 다우오피스에 정상적으로 로그인되어 있는지 확인
2. 해당 계정에서 캘린더에 접근할 수 있는지 확인
3. Chrome Extension이 정상적으로 동작하는지 확인
4. Native Messaging 연결 상태 확인
5. 네트워크 및 다우오피스 서비스 상태 확인
6. 설정창의 DaouOffice 주소가 `https://회사이름.daouoffice.com` 형식인지 확인
7. 상태 표시가 `동기화 중…`에서 오래 멈춰 있는지 확인(조회는 Chrome 확장 worker가 하고 최대 25초(`FETCH_TIMEOUT_MS`)이므로 95초 연결 대기 임계를 넘지 않는다. 멈춰 있다면 `%LOCALAPPDATA%\DaouCalendarOverlay\logs\overlay-yyyyMMdd.log`의 `bridge` 항목 확인)
8. 상태 문구에 `· 캐시 MM-dd HH:mm`만 계속 보이면 아직 한 번도 동기화에 성공하지 못한 상태입니다. Chrome 확장과 Native Messaging 등록을 먼저 확인하세요.
9. 상태에 `Chrome 확장 새로고침 필요 (…)`가 보이면 Chrome에 로드된 확장 버전이 EXE에 포함된 버전과 다른 것입니다(예: `Chrome 확장 새로고침 필요 (7.0.0 이하 → 7.3.1)`. 버전을 보내지 않는 7.0.0 확장은 `7.0.0 이하`로 표시). `chrome://extensions`에서 확장을 새로고침하세요. 새로고침하면 확장이 곧바로 `getConfig`를 보내고(`onInstalled` 트리거. 새로고침 때 이 이벤트가 오는지는 실환경 미확인) 그 뒤로는 30초 알람마다 보내므로, 문구가 `Chrome 브리지 연결됨 · 동기화 대기`로 바뀝니다. 재로그인이 필요한 상태(`DaouOffice 재로그인 필요`, 세션 만료 안내 등)에서는 재로그인 안내가 우선이라 이 문구가 나오지 않고, 재로그인해 동기화가 성공한 뒤에 표시됩니다.

### "열기"가 Chrome이 아닌 브라우저로 열리는 경우

DaouOffice "열기" 버튼(트레이 "DaouOffice 열기" 포함)은 기본 브라우저가 무엇이든 Chrome을 직접 실행합니다. Chrome을 찾지 못하면(`App Paths` 레지스트리에 `chrome.exe`가 없음) 기본 브라우저로 엽니다.
이 경우 기본 브라우저에 확장이 설치돼 있지 않으면 그 브라우저에서 로그인해도 동기화에 반영되지 않으므로, 확장이 설치된 Chrome을 직접 열어 다우오피스에 로그인하세요.

### "Native host 등록 실패" 또는 "확장 파일 설치 실패" 상태가 표시되는 경우

오버레이는 기동할 때와 설정을 저장할 때 Chrome 확장 파일을 `%LOCALAPPDATA%\DaouCalendarOverlay\ChromeExtension`에 추출하고 HKCU에 Native Messaging Host를 등록합니다. 이 두 작업이 실패해도(그룹 정책·ACL로 HKCU 쓰기가 막힌 경우, 폴더 권한·디스크 문제 등) 더 이상 앱을 종료시키지 않고, 로그와 상태 표시줄 문구로만 알립니다.

- 상태 표시줄: `Native host 등록 실패: 쓰기 권한이 없습니다(정책 또는 ACL 제한) · 로그 확인`, `확장 파일 설치 실패: … · 로그 확인`처럼 실패한 구성 요소와 예외 종류별 사유가 표시됩니다.
- 로그: `%LOCALAPPDATA%\DaouCalendarOverlay\logs\overlay-yyyyMMdd.log`에 카테고리 `startup.extension`(확장 파일 추출) / `startup.nativehost`(Native Messaging 등록)로 오류와 예외 정보가 남습니다.
- 이 상태에서도 저장된 캐시 일정은 계속 표시되지만, 새 동기화는 Native Messaging 등록이 복구될 때까지 되지 않습니다.
- 이후 대기 문구 끝에 ` · Native host 등록 실패`(또는 ` · 확장 파일 설치 실패`) 접미가 붙어 있습니다. 원인을 해소한 뒤 트레이 → **설정**을 열어 저장하거나 오버레이를 다시 시작해 재시도가 성공하면 이 접미는 사라집니다.

## 버전

| 구성 요소 | 버전 |
|---|---|
| EXE (`DaouCalendarOverlay.exe`) | 7.3.1 (파일 버전 7.3.1.0) |
| Chrome 확장 | 7.3.1 |
| 네이티브 브리지 프로토콜 | v2 (파이프 이름은 `DaouCalendarOverlay.NativeBridge.v8`) |

EXE와 Chrome 확장은 같은 버전 번호를 쓰고, 네이티브 브리지 프로토콜 버전은 `NativeBridgeProtocol.ProtocolVersion`으로 따로 관리합니다. 7.2.0에서 호환 불가 변경(postResult 형식 교체)으로 프로토콜을 v2로, 파이프 이름을 `.v8`로 올렸습니다. 7.3.0에서는 이 번호를 바꾸지 않았습니다. 프로토콜 버전은 앱과 확장이 주고받는 메시지 형식의 번호이고, 번호가 다르면 앱은 확장에 조회를 맡기지 않습니다(7.2.0으로 올릴 때 7.1.0 확장이 그랬습니다). 7.3.0은 확장이 앱에 보내는 조회 결과(postResult)에 `refreshState`, `refreshStatus` 두 항목을 덧붙이기만 했고, 이 두 항목이 없는 결과는 앱이 7.2.0과 똑같이 처리합니다. 그래서 번호(v2)와 파이프 이름을 그대로 두었고, EXE만 7.3.0으로 바꾸고 확장을 아직 새로고침하지 않은 동안에도 조회가 끊기지 않습니다(코드 기준, 실환경 미확인). 7.3.1에서는 대상 프레임워크만 .NET 10으로 바꾸고 확장 코드는 그대로 두어 프로토콜 번호(v2)와 파이프 이름(`.v8`)도 바꾸지 않았으며, 확장은 버전 번호만 7.3.1로 올렸습니다.

실행 중인 버전은 다음 네 곳에서 확인할 수 있으며, 네 곳이 항상 같은 버전을 가리켜야 합니다.

1. 트레이 아이콘 툴팁: `Daou Calendar Overlay 7.3.1`
2. 설정 창 하단: `버전 7.3.1 · 프로토콜 v2`
3. 로그 파일 첫 줄: `DaouCalendarOverlay 7.3.1 (7.3.1+<커밋 SHA>) mode=overlay pid=…` (Native Messaging Host 모드는 `host-yyyyMMdd.log`에 `mode=host`)
4. EXE 속성 창 > 자세히: 파일 버전 7.3.1.0

- 확장을 새 버전으로 교체했다면 `chrome://extensions`에서 해당 확장을 **새로고침**해야 새 Service Worker가 적용됩니다. 새로고침하지 않으면 상태 표시줄에 `Chrome 확장 새로고침 필요 (…)`가 표시됩니다.
- 빌드 커밋 해시: git 저장소에서 빌드하면 .NET SDK(Source Link)가 `InformationalVersion`에 커밋 SHA를 자동으로 붙여 `7.3.1+<커밋 SHA>`가 됩니다. 이 값은 로그 첫 줄과 EXE 속성 창의 제품 버전에서 보입니다. 트레이 툴팁과 설정 창에 표시하는 버전은 `7.3.1`으로 유지됩니다. 별도의 빌드 옵션은 필요 없으며, git 저장소 밖(소스 압축본 등)에서 빌드하면 해시 없이 `7.3.1`입니다.
- 버전별 변경 이력은 [`CHANGELOG.md`](CHANGELOG.md)를 참고하세요.

## 라이선스

현재 별도의 오픈소스 라이선스를 지정하지 않았다면 저장소의 소스 코드는 기본적으로 저작권자의 권리에 따라 보호됩니다.

오픈소스로 공개할 계획이 있다면 프로젝트 목적과 배포 범위를 고려하여 별도의 LICENSE 파일을 추가하세요.

## 상태

현재 프로젝트는 MVP 단계의 개인/내부용 도구입니다.

다우오피스의 웹 서비스 구조, 인증 방식, Chrome 정책 또는 브라우저 동작이 변경될 경우 추가 수정이 필요할 수 있습니다.
