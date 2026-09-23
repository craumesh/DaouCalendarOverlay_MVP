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

## 기술 스택

- C#
- .NET 8
- WPF
- Chrome Extension
- Chrome Native Messaging
- HTTP/CalDAV 기반 일정 연동
- MVVM 구조

## 프로젝트 구조

```text
DaouCalendarOverlay_MVP/
├─ DaouCalendarOverlay.sln
├─ README.md
├─ .gitignore
├─ publish.ps1
│
└─ DaouCalendarOverlay/
   ├─ App.xaml
   ├─ App.xaml.cs
   ├─ app.manifest
   ├─ DaouCalendarOverlay.csproj
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
| `publish.ps1` | 배포용 빌드/설치 작업 |

## 동작 구조

전체적인 데이터 흐름은 다음과 같습니다.

```text
┌──────────────────────┐
│       Chrome         │
│  DaouOffice 로그인   │
└──────────┬───────────┘
           │
           │ 로그인 세션 / Cookie
           ▼
┌──────────────────────┐
│   Chrome Extension   │
└──────────┬───────────┘
           │
           │ Native Messaging
           ▼
┌──────────────────────┐
│ DaouCalendarOverlay  │
│      (WPF/.NET)      │
└──────────┬───────────┘
           │
           │ 일정 데이터 요청/동기화
           ▼
┌──────────────────────┐
│     DaouOffice       │
│      Calendar        │
└──────────────────────┘
```

이 구조에서는 WPF 애플리케이션이 다우오피스의 로그인 페이지를 직접 구현하거나 사용자의 비밀번호를 별도로 보관하는 대신, 사용자가 Chrome에서 이미 인증한 세션을 활용합니다.

동기화 주기는 앱의 타이머가 아니라 `CalendarBridgeServer`가 결정합니다. Chrome 확장이 30초마다 `getConfig`를 물어보고, 앱이 가져올 때가 됐다고 답한 회차에만 일정을 가져옵니다(성공 후 다음 시도 시각은 설정의 새로고침 주기, 실패 시 backoff로 정해집니다).

달력의 '오늘' 표시 기준은 Windows 로컬 날짜입니다. 자정이 지나면 1초 시계 틱에서 오늘 배지가 자동으로 이동하고, 이번 달을 보고 있었다면 새 달로 이동한 뒤 한 번 동기화합니다. (일정이 어느 날짜에 속하는지 판정할 때는 KST +09:00을 사용합니다.)

## 요구 사항

- Windows
- .NET 8 SDK
- Google Chrome
- 다우오피스 계정
- 다우오피스 캘린더 사용 권한

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

## 배포

저장소에 포함된 `publish.ps1`을 사용해 배포용 빌드를 생성할 수 있습니다.

PowerShell에서:

```powershell
.\publish.ps1
```

PowerShell의 실행 정책 때문에 스크립트 실행이 차단되는 경우에는 현재 사용자 범위의 정책을 확인한 후 필요한 경우 다음과 같이 일시적으로 실행할 수 있습니다.

```powershell
powershell -ExecutionPolicy Bypass -File .\publish.ps1
```

실제 배포 방식과 출력 경로는 `publish.ps1`의 현재 내용을 기준으로 확인해야 합니다.

## 최초 실행

일반적인 사용 순서는 다음과 같습니다.

1. Google Chrome을 설치합니다.
2. Chrome에서 다우오피스에 로그인합니다.
3. 프로젝트의 Chrome Extension을 설치/등록합니다. Extension 버전이 올라간 빌드로 교체했다면 `chrome://extensions`에서 해당 확장을 **새로고침**해야 새 Service Worker가 적용됩니다.
4. Native Messaging Host가 정상적으로 등록되었는지 확인합니다.
5. `DaouCalendarOverlay.exe`를 실행합니다.
6. 애플리케이션에서 캘린더 동기화 상태를 확인합니다.

DaouOffice 주소는 `https://회사이름.daouoffice.com` 형식만 허용합니다. `http://`, 다른 도메인, `daouoffice.com`(회사 이름 없는 주소)은 설정창에서 거부되며, 기존 설정에 이런 주소가 들어 있으면 앱 시작 시 설정창이 열리고 거부 사유가 표시됩니다.

Chrome Extension ID 및 Native Messaging 설정은 프로젝트의 Extension/Native Messaging 관련 파일을 기준으로 구성되어 있습니다.

### 동작 확인

애플리케이션 하단 상태 표시는 정상 경로에서 다음 순서로 바뀝니다.

```text
Chrome 백그라운드 대기 → 동기화 요청 중… → 동기화 중… → 정상 · 동기화 HH:mm
```

- 이전에 저장된 캐시가 있으면 첫 동기화 전까지 대기 문구 뒤에 `· 캐시 MM-dd HH:mm` 접미가 붙습니다(예: `Chrome 백그라운드 대기 · 캐시 09-21 08:30`). 저장된 조회 범위가 오늘을 포함하지 않으면 `· 캐시(범위 밖) · 캐시 MM-dd HH:mm` 순서로 두 접미가 함께 붙습니다. 동기화가 한 번 성공하면 접미는 사라집니다.
- 상태 텍스트에 마우스를 올리면 `마지막 갱신 yyyy-MM-dd HH:mm` 툴팁으로 마지막으로 화면에 반영된 데이터 시각을 확인할 수 있습니다.
- 95초 이상 확장 heartbeat가 없으면 `Chrome 확장 연결 대기`로 바뀝니다.

## 로컬 설정 및 인증 정보

애플리케이션의 사용자별 설정은 로컬 사용자 환경에 저장됩니다.

```text
%LOCALAPPDATA%\DaouCalendarOverlay\
```

- 자동 새로고침은 1~1440분만 허용합니다. 범위 밖 값은 설정창에서 거부되며, settings.json에 남아 있던 범위 밖 값은 앱 기동 시 자동으로 보정됩니다.
- 캘린더 ID는 숫자 1~19자리만 허용됩니다.

`settings.json`과 `calendar-cache.json`은 같은 폴더에 GUID 이름의 임시 파일(`settings.json.<guid>.tmp`)을 만들어 기록한 뒤 원자적으로 교체합니다. 같은 파일에 대한 동시 저장은 경로별로 직렬화되므로, 투명도(450ms)와 창 위치(400ms) 저장 틱이 겹쳐도 파일이 손상되거나 앱이 종료되지 않습니다.

저장이 실패하면(예: 파일이 읽기 전용이거나 다른 프로그램이 파일을 잠근 경우) 앱은 종료되지 않고 상태 표시줄에 `설정 저장 실패 · 로그 확인`(캐시는 `캐시 저장 실패 · 로그 확인`)을 표시하며, 실패한 경로와 예외를 아래 로그 파일에 남깁니다.

개발 과정에서 사용하는 계정 정보, 세션 Cookie, 토큰, 비밀번호 등의 민감한 값은 Git 저장소에 저장하지 않는 것을 원칙으로 합니다.

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
- 로그에는 인증 Cookie 값이나 토큰이 기록되지 않습니다. 쿠키는 개수(`cookieCount`)와 출처(`cookieSource`)만 남습니다.

### Chrome Extension의 고정 Key

Extension의 고정 ID를 유지하기 위해 Extension manifest에 공개용 `key` 값이 포함될 수 있습니다.

이 값은 다우오피스 계정 비밀번호나 세션 인증정보와는 다른 값이며, Extension ID를 고정하기 위한 공개 구성 요소입니다.

### 일정 캐시

일정 캐시 파일 `calendar-cache.json`에는 마지막 정상 동기화 시각, 그때 조회한 범위(`RangeFrom`/`RangeTo`), 일정 객체가 평문 JSON으로 저장됩니다. 앱을 다시 켜면 이 캐시를 먼저 표시하는데, 오늘 날짜(KST 기준)가 저장된 범위 밖이면(예: 다음 달을 보다가 종료한 경우, 또는 범위 정보가 없는 이전 버전 캐시) 캐시 일정을 그대로 보여 주되 상태 표시줄 문구 뒤에 `· 캐시(범위 밖)`를 붙여 알려 주고 즉시 강제 동기화를 수행합니다. 정상 동기화가 한 번 성공하면 이 접미는 사라집니다.

## 보안 관련 주의사항

이 프로젝트는 Chrome의 로그인 세션을 활용하기 때문에 다음 사항에 주의해야 합니다.

- 신뢰할 수 없는 Chrome Extension을 설치하지 않습니다.
- Extension의 권한 범위를 불필요하게 확대하지 않습니다.
- Native Messaging host는 Named Pipe 서버가 같은 Windows 계정이 실행한 동일 EXE일 때만 세션 쿠키를 전달합니다(다르면 전송하지 않고 오류를 반환).
- 인증 Cookie나 토큰을 로그에 출력하지 않습니다.
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

## 문제 해결

### Extension과 애플리케이션이 연결되지 않는 경우

다음 항목을 순서대로 확인합니다.

1. Chrome Extension이 정상적으로 설치되어 있는지 확인
2. Extension ID가 현재 manifest 설정과 일치하는지 확인
3. Native Messaging Host가 정상적으로 등록되어 있는지 확인
4. Native Messaging Host가 가리키는 실행 파일 경로가 실제 파일과 일치하는지 확인
5. Chrome을 완전히 재시작한 후 다시 시도
6. WPF 애플리케이션의 로그 확인 (`%LOCALAPPDATA%\DaouCalendarOverlay\logs\overlay-yyyyMMdd.log`, Native Messaging Host 쪽은 같은 폴더의 `host-yyyyMMdd.log`)
7. 실행 중인 오버레이 EXE와 Native Messaging Host manifest의 `path`가 **같은 파일**인지 확인(다르면 host가 쿠키 전송을 거부합니다. 오버레이를 재시작하면 manifest가 현재 EXE 경로로 갱신됩니다)

### 일정이 동기화되지 않는 경우

1. Chrome에서 다우오피스에 정상적으로 로그인되어 있는지 확인
2. 해당 계정에서 캘린더에 접근할 수 있는지 확인
3. Chrome Extension이 정상적으로 동작하는지 확인
4. Native Messaging 연결 상태 확인
5. 네트워크 및 다우오피스 서비스 상태 확인
6. 설정창의 DaouOffice 주소가 `https://회사이름.daouoffice.com` 형식인지 확인
7. 상태 표시가 `동기화 중…`에서 오래 멈춰 있는지 확인(조회가 오래 걸리는 동안에도 Chrome 확장 하트비트는 유지되므로, 멈춰 있다면 `%LOCALAPPDATA%\DaouCalendarOverlay\logs\overlay-yyyyMMdd.log`의 `bridge` 항목 확인)
8. 상태 문구에 `· 캐시 MM-dd HH:mm`만 계속 보이면 아직 한 번도 동기화에 성공하지 못한 상태입니다. Chrome 확장과 Native Messaging 등록을 먼저 확인하세요.

## 라이선스

현재 별도의 오픈소스 라이선스를 지정하지 않았다면 저장소의 소스 코드는 기본적으로 저작권자의 권리에 따라 보호됩니다.

오픈소스로 공개할 계획이 있다면 프로젝트 목적과 배포 범위를 고려하여 별도의 LICENSE 파일을 추가하세요.

## 상태

현재 프로젝트는 MVP 단계의 개인/내부용 도구입니다.

다우오피스의 웹 서비스 구조, 인증 방식, Chrome 정책 또는 브라우저 동작이 변경될 경우 추가 수정이 필요할 수 있습니다.
