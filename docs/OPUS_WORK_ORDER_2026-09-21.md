# Daou Calendar Overlay v7.0.0 — 최종 검토 결과 및 Opus 작업 지시서

- 작성일: 2026-09-21
- 대상 소스: `C:\Users\USER\Desktop\DaouCalendarOverlay_MVP` (git 저장소 아님, .NET SDK 10.0.401 설치, 프로젝트 TFM `net8.0-windows`)
- 대상 문서: `C:\Users\USER\Downloads\DaouCalendarOverlay_Technical_Documentation.html` (기술 설계 및 구현 문서 v7.0.0, 2026-09-18)
- 검토 방법: 소스 전체(약 4,200줄)와 문서 20개 절을 1:1 대조, Release 빌드 실행, 현재 PC의 설치 상태(레지스트리·LocalAppData)와 캐시 실데이터(37건) 확인, 8개 관점 리뷰(원시 지적 135건)를 코드 기준으로 재검증 후 병합. 아래 항목마다 근거 파일:라인과 검증 상태를 표기했다.

---

## 0. Opus 세션에 붙여넣을 프롬프트

```text
docs/OPUS_WORK_ORDER_2026-09-21.md 를 읽고 그 안의 작업을 수행해줘.
규칙:
1. 6절 "실행 지침"을 먼저 읽고 그대로 따른다. T0.1(git init)을 다른 어떤 작업보다 먼저 한다.
2. 우선순위 P0 → P1 → P2 → P3 순서로 진행하고, 작업 하나당 커밋 하나를 만든다.
3. 각 작업의 "완료 기준"을 모두 만족해야 완료로 표시한다. 만족하지 못한 항목은 이유와 함께 남긴다.
4. 3절의 Q1~Q8은 2026-09-21에 모두 "기본안"으로 결정됐다. 각 작업에 적힌 기본안을 그대로 적용하고, 다른 선택지를 검토하느라 시간을 쓰지 않는다.
5. 코드 변경이 문서 내용에 영향을 주면 같은 작업 안에서 README.md와 P3의 문서 정정 목록도 갱신한다.
6. 작업이 끝나면 docs/OPUS_WORK_ORDER_2026-09-21.md 의 각 작업 제목 앞에 [완료]/[부분]/[보류]를 표시하고 결과 요약 절을 맨 아래에 추가한다.
```

---

## 1. 총평

문서는 코드와 매우 잘 맞는다. 상수·경로·레지스트리 키·타이밍·코드 인용(17개 발췌)이 전부 현재 소스와 일치하고, 아키텍처가 수렴한 이유(§2)와 두 실행 모드(§5)도 정확하다. 아키텍처 선택 자체(기존 Chrome 세션 재사용, Native Messaging + Named Pipe, C#에서 HTTP 수행)는 타당하다.

보완이 필요한 축은 다섯 가지다.

1. **운영 가시성이 없다.** 로그·전역 예외 처리기가 전혀 없어 문서 §18의 장애 대응표를 현장에서 실행할 수 없고, `async void` 핸들러의 I/O 예외가 곧 프로세스 종료로 이어진다.
2. **조용한 실패 경로가 있다.** 설정 검증과 브리지 판정 규칙이 달라 http:// 또는 비-daouoffice 호스트는 저장되지만 영원히 동기화되지 않으며 화면에 사유가 없다.
3. **프로세스 스폰 모델의 비용이 문서에 없다.** 30초마다 self-contained single-file WPF EXE 전체가 기동되고, 오버레이가 꺼져 있어도 Chrome이 살아 있는 한 무한 반복된다.
4. **배포 체계가 미완이다.** 버전이 세 곳(EXE 1.0.0.0 / 확장 7.0.0 / 문서 7.0.0)에서 다르고, 제거·업그레이드 경로·코드 서명·PDB·테스트·git 이력이 없다. .NET 8 지원 종료(2026-11-10)가 50일 남았다.
5. **문서에 절차·스키마·상태표가 없다.** 시퀀스 다이어그램, 브리지 메시지 스키마, 상태 전이표, 설치/제거 절차, 지원 매트릭스, 테스트 기록, 위협 모델, 백로그가 빠져 있다.

실데이터(캐시 37건)에서 새로 드러난 사실 두 가지는 코드 수정이 필요하다. 같은 일정이 두 캘린더에 걸쳐 같은 id로 두 번 내려와 화면에 중복 표시되는 경우가 5건(13%)이고, 캐시가 마지막으로 본 달의 결과로 덮어써져 다음 기동 시 이번 달이 비어 보일 수 있다.

---

## 2. 검토 중 확인된 사실 (변경 없이 참고)

| 항목 | 결과 |
|---|---|
| `dotnet build -c Release` | 통과, 경고 0 / 오류 0 (.NET 8 타깃, SDK 10.0.401) |
| 문서 HTML | 외부 참조 없음(인라인 style 1개), 목차 앵커 20/20 일치, 코드 발췌 17개 |
| Native host 등록 | HKCU Chrome 키 존재, manifest `path`가 `bin\Release\net8.0-windows\win-x64\DaouCalendarOverlay.exe` (개발 빌드 산출물)를 가리킴. Edge 키 없음 |
| HKCU Run | 동일 개발 빌드 EXE 경로 |
| `settings.json` | 계산 속성 `IsConfigured`가 함께 직렬화됨(무해하나 불필요) |
| 캐시 실데이터 | 37건. `color`는 정수 인덱스 {1,3,6,7,13,14,17,18}. `type`은 {anniversary, company, holiday, normal}. `visibility`는 public만. 시간 일정 offset은 전부 +09:00 |
| 종일 일정 종료 | 같은 날 `23:59:00` 또는 `23:59:59.999`, 공휴일은 시작과 동일 `00:00`. 즉 **종료일 포함(inclusive)** 의미 → 현재 `OccursOnDate` 로직과 부합 |
| 음력 공휴일 | 1970년 템플릿 3건(`1970-02-06`, `02-07`, `05-12`)과 2027년 1건이 조회 범위 밖으로 섞여 내려오고, 2026년 범위로 확장된 occurrence(추석 3일)도 함께 옴 → 표시는 정상. 공휴일 RRULE은 모두 `UNTIL=20261231` |
| 중복 이벤트 | 같은 `id`가 서로 다른 `calendarId` 2개로 5건 중복(38262/60717 ×3, 38268/53886, 38267/1480365661766049793). 캘린더 14개 설정, 캐시에는 9개 등장 |
| 플랫폼 사실(웹 확인) | .NET 8 지원 종료 2026-11-10. `chrome.alarms` 30초 최소 주기는 Chrome 120+. `sendNativeMessage`는 호출마다 host 프로세스를 새로 띄우고, `connectNative`는 포트가 살아 있는 동안 유지 |

---

## 3. 사용자 결정 항목 (결정 완료)

**2026-09-21 사용자 결정: Q1~Q8 전부 기본안 채택.** 아래 표는 선택지 기록용이며, Opus는 "기본안" 열을 확정 사양으로 취급한다.

| # | 결정 | 선택지 | 기본안 |
|---|---|---|---|
| Q1 | 커스텀 도메인(비-daouoffice.com) 지원 | (a) 지원 안 함: 설정창에서 거부 (b) 지원: 확장 `host_permissions`/쿠키 필터/BuildConfig를 설정값 기반으로 | (a) |
| Q2 | 중복 이벤트 표시 정책 | (a) id 기준 1회 표시 + 소속 캘린더 목록 보존 (b) 캘린더마다 따로 표시(현행) | (a) |
| Q3 | Native host 경량화 범위 | (a) 커스텀 `Main`에서 WPF 생성 전 분기만 (b) `connectNative` 장수명 포트로 전환까지 | (a) 먼저, (b)는 측정 후 |
| Q4 | Edge/Whale 지원 | (a) Edge 레지스트리 키 추가 등록 (b) Chrome만 | (a) |
| Q5 | .NET 10 이전 시점 | (a) 이번 릴리스에서 `net10.0-windows`로 (b) 별도 릴리스 | (b), 단 시험 브랜치는 이번에 |
| Q6 | 캐시 암호화(DPAPI) | (a) 적용 (b) 옵션으로 (c) 미적용 | (c), 문서에 위험 명시 |
| Q7 | 투명도 100% 의미 | (a) 완전 불투명(alpha 255) (b) 현행 alpha 230 유지 | (b), 문서에 명시 |
| Q8 | 확장 배포 방식 | (a) unpacked 유지 (b) CRX + update_url 자체 호스팅 | (a), (b)는 백로그 |

---

## 4. 작업 지시

각 작업은 `근거`(파일:라인), `검증 상태`, `지시`, `완료 기준`으로 구성한다. 검증 상태 표기: **코드 확인** = 소스에서 직접 확인, **실데이터 확인** = 캐시/설치 상태로 확인, **웹 확인** = 플랫폼 문서로 확인, **추정** = 재현 전 가설.

### P0. 착수 전 기반

#### [완료] T0.1 git 저장소 초기화 — P0
- 근거: `.gitignore`는 있으나 `.git` 없음. 문서의 "v7.0.0"이 어떤 소스 스냅샷인지 추적 불가.
- 검증 상태: 코드 확인.
- 지시: `git init`, `.gitignore`에 `bin/ obj/ publish/ .vs/ *.user *.suo` 유지 확인 후 현재 상태를 `v7.0.0-asbuilt` 태그로 초기 커밋. 이후 모든 작업은 별도 커밋.
- 완료 기준: `git log`에 초기 커밋과 태그. `git status`가 깨끗함.
- 적용 메모(2026-09-21): 실제 작업 저장소는 `C:\Users\USER\Develop\DaouCalendarOverlay_MVP`(Desktop 복사본과 소스 동일, diff로 확인). 이미 초기 커밋 `a046a1b`가 있어 `git init` 대신 그 커밋에 `v7.0.0-asbuilt` 태그를 부여하고, docs/(작업 지시서·기술 문서 HTML)와 .claude/·CLAUDE.md를 `73ba742`로 커밋했다. `.gitignore`는 요구 항목을 이미 포함.

#### [완료] T0.2 테스트 프로젝트 골격 — P0
- 근거: 테스트 0개. UI 없는 클래스(`NativeBridgeProtocol`, `CalendarBridgeServer`의 URI/판정 로직, `MainViewModel`, `SettingsWindow`의 URL 파서, `SyncStatusService`)가 모두 테스트 가능.
- 검증 상태: 코드 확인.
- 지시: `tests/DaouCalendarOverlay.Tests` (xUnit, `net8.0-windows`) 추가. 솔루션에 포함. 첫 테스트로 `NativeBridgeProtocol.ReadFrameAsync/WriteFrameAsync` 왕복, `MainViewModel.GetVisibleRange` 42일 범위, `OccursOnDate`의 종일/시간 일정 케이스(2절 실데이터 규칙 그대로: 종료 `23:59:59.999` 포함, 공휴일 start==end)를 작성. `SettingsWindow.Extract_Click`의 파싱 부분은 정적 메서드로 추출해 테스트.
- 완료 기준: `dotnet test`가 통과. 이후 P1 작업의 완료 기준에 테스트 포함.
- 적용 메모(2026-09-21): lead가 3태스크로 분해 — T1 골격·솔루션 등록, T2 순수 로직 추출(`Models/CalendarGrid.cs`, `Services/CalendarUrlParser.cs`; 원본 대비 8,188케이스 비교 불일치 0), T3 테스트 4파일. 결과 테스트 83건 통과, 빌드 경고 0. 테스트 csproj의 `RuntimeIdentifier`/`SelfContained=false`/`ValidateExecutableReferencesMatchSelfContained=false`는 메인 csproj 전역 RID 우회이며 T2.6에서 함께 제거한다. 중간 검토(reviewer) approve, minor 5건과 P1 제약은 `docs/verification-log.md` 참조.

### P1. 기능·안정성 결함

#### [부분] T1.1 파일 로깅 + 전역 예외 처리 — P1
- 근거: 전체 소스에 로그 출력 없음. `App.xaml.cs:100-108`(초기화 실패 MessageBox 후 종료), `App.xaml.cs:179-205, 249-295`, `MainWindow.xaml.cs:393-400, 459-477, 624-629, 643-648`의 `async void` 핸들러가 파일/레지스트리 I/O를 try/catch 없이 호출. `CalendarBridgeServer.cs:97-102`, `StartupService.cs:28-31`, `NativeMessagingHost.cs:46-49, 61-62`, `App.xaml.cs:332-335`의 `catch {}`가 원인을 삼킴. 확장도 `service-worker-v700.js:144-148, 164-166`에서 오류를 버림.
- 검증 상태: 코드 확인.
- 지시:
  1. `Services/LogService.cs` 추가: `%LOCALAPPDATA%\DaouCalendarOverlay\logs\overlay-yyyyMMdd.log`, 1MB×5 롤링, 스레드 안전, 외부 패키지 없이 구현. 레벨 Info/Warn/Error.
  2. GUI 모드: `DispatcherUnhandledException`, `AppDomain.CurrentDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`을 등록해 로그 후 가능하면 계속(설정 저장 실패 등은 상태 텍스트로 안내).
  3. Native host 모드: 파이프 연결 실패·프레임 오류를 같은 로그(파일명 `host-yyyyMMdd.log`)에 기록. 요청 type과 처리 시간을 남긴다.
  4. 모든 `async void` 핸들러를 try/catch로 감싸 로그 + 상태 표시. 기존 `catch {}`에는 최소 Warn 로그.
  5. 트레이 메뉴에 "로그 폴더 열기" 추가.
  6. 확장: `getConfig`/`postResult` 실패를 `chrome.storage.session`의 `lastError`에 기록하고, 다음 `getConfig` 요청에 `lastError`를 실어 보내 앱이 로그에 남기도록.
- 완료 기준: 앱 시작·동기화 성공·인증 실패·네트워크 실패·파이프 실패가 로그에 각각 한 줄 이상 남는다. 설정 저장 중 파일을 읽기 전용으로 만들어도 앱이 종료되지 않고 상태 텍스트에 오류가 보인다. 문서 §18에 로그 경로와 대표 로그 라인 추가.
- 적용 메모(2026-09-22): LogService/LogFormatter/LogRotation 신설 + 순수 로직 단위 테스트; 전역 예외 처리기 + async void 전수 try/catch + catch{} 로그 + 트레이 "로그 폴더 열기"; 확장 7.1.0 개명 + lastError 전달 프로토콜 필드 + README/기술문서 로그 절 갱신 수동 확인 9건 대기

#### [부분] T1.2 설정/캐시 저장 직렬화 — P1
- 근거: `SettingsService.cs:35-53` temp 경로가 `settings.json.tmp` 고정. `MainWindow.xaml.cs:624-629`(투명도 450ms)와 `643-648`(위치 400ms)의 `async void` 틱이 각각 `App.SaveWindowBoundsAsync`/`SetUiOpacityAsync`를 통해 `SaveAsync`를 호출하므로 await 지점에서 겹치면 `File.Create(temp)` 공유 위반 → 처리되지 않은 예외로 종료. `CacheService.cs:35-43`은 예외 시 temp 미정리이며 `App.xaml.cs:143-147`에서 결과가 관찰되지 않음. `AppSettings.IsConfigured`(`AppSettings.cs:16-19`)가 settings.json에 직렬화됨(실데이터 확인).
- 검증 상태: 코드 확인(경쟁 조건은 추정이나 구조상 가능), IsConfigured 직렬화는 실데이터 확인.
- 지시: `SemaphoreSlim(1,1)`로 저장 직렬화, temp 파일명에 Guid 사용, 실패 시 temp 삭제, `IsConfigured`에 `[JsonIgnore]`. 저장 실패는 T1.1 로그 + 상태 텍스트.
- 완료 기준: 동시 저장 100회 테스트 통과. settings.json에 `IsConfigured` 키가 사라짐(기존 파일의 키는 무시됨을 확인).
- 적용 메모(2026-09-22): 원자적 JSON 저장기(AtomicJsonFileWriter) 도입 — SettingsService/CacheService 저장 직렬화 + IsConfigured 직렬화 제외; 저장 실패 결과 관찰 및 상태 텍스트 노출 + README·기술문서 §13/§17 갱신 수동 확인 4건 대기

#### [부분] T1.3 BaseUrl 검증 규칙 통일과 NoFetch 사유 노출 — P1 (Q1)
- 근거: `AppSettings.cs:16-19`와 `SettingsWindow.xaml.cs:63-69`는 http/https + 임의 호스트 허용. `CalendarBridgeServer.cs:167-176`(`BuildConfig`)는 https + `daouoffice.com`/서브도메인만 통과, 아니면 `NoFetch()`를 상태 변화 없이 반환 → "Chrome 브리지 연결됨 · 동기화 대기"에서 영원히 멈춤. 확장 `manifest.json:15-17` `host_permissions`는 `https://*.daouoffice.com/*`이라 apex `daouoffice.com`은 포함하지 않지만 BuildConfig는 apex를 허용(불일치).
- 검증 상태: 코드 확인.
- 지시(기본안 Q1-a): 검증 규칙을 한 곳(`AppSettings.Validate()` 또는 `Services/BaseUrlPolicy.cs`)으로 모으고 설정창·IsConfigured·BuildConfig가 모두 그것을 쓴다. 규칙: https 필수, 호스트는 `*.daouoffice.com`(apex 제외). 설정창은 위반 시 구체적 문구로 거부. BuildConfig가 NoFetch를 돌려주는 모든 경우에 사유(`NoFetchReason`)를 응답에 포함하고 `SyncStatusService`에 새 상태(`ConfigurationInvalid`)로 표시.
- 완료 기준: http:// 저장 시도 → 거부 메시지. 기존 settings.json에 http://가 있으면 시작 시 설정창이 열리며 사유 표시. 테스트: 정책 함수 단위 테스트 6케이스 이상.
- 적용 메모(2026-09-22): BaseUrlPolicy 도입 및 설정창·IsConfigured 검증 통일; BuildConfig NoFetch 사유 응답과 ConfigurationInvalid 상태 노출; T1.3 문서 반영(§10.2·§10.4·§17·§18, README) 수동 확인 6건 대기

#### [부분] T1.4 중복 이벤트 처리 — P1 (Q2)
- 근거: 실데이터 37건 중 5건이 같은 `id`로 두 `calendarId`에 걸쳐 중복. `MainViewModel.SetEvents(:96-100)`/`ApplyFilter(:201-211)`에 id 기준 dedupe 없음 → 날짜 셀 "3개 이상" 규칙이 조기 발동하고 목록 카드에 같은 일정이 두 번.
- 검증 상태: 실데이터 확인 + 코드 확인.
- 지시(기본안 Q2-a): 수신 시 `id`로 그룹화해 대표 1건을 만들고 `CalendarIds`(복수)와 `CalendarNames`를 보존. 숨김 판정은 "소속 캘린더가 모두 숨김일 때만 숨김". 색은 첫 캘린더 기준. 상세 카드의 "캘린더" 행에 복수 이름 표시. 캐시 포맷은 원본 그대로 저장(역호환), dedupe는 로드 후 수행.
- 완료 기준: 위 실데이터로 목록 카드에 중복이 사라짐. 두 캘린더 중 하나만 숨겨도 일정이 남음. 테스트 3케이스.
- 적용 메모(2026-09-22): DaouCalendarEvent에 소속 캘린더 목록 필드 추가 + Models/EventDeduplicator 신설(+단위 테스트); MainViewModel/상세 카드에 병합 적용(숨김은 전부 숨김일 때만) + 테스트·문서 갱신 수동 확인 3건 대기

#### [부분] T1.5 Named Pipe 클라이언트 보호 — P1
- 근거: 서버(`CalendarBridgeServer.cs:83-88`)는 `PipeOptions.CurrentUserOnly`지만, native host 클라이언트(`NativeMessagingHost.cs:35-41`)는 `PipeOptions.Asynchronous`만 사용. 다른 로컬 계정 프로세스가 같은 이름의 파이프를 먼저 만들면 host가 거기에 연결해 `postResult`(세션 쿠키 전량)를 보낸다. 같은 사용자 프로세스의 선점은 Chrome App-Bound Encryption을 우회해 쿠키를 얻는 경로가 된다.
- 검증 상태: 코드 확인(공격 시나리오는 추정).
- 지시: 클라이언트에 `PipeOptions.CurrentUserOnly` 추가(다른 계정 차단). 같은 계정 선점에 대해서는 연결 후 `GetNamedPipeServerProcessId`로 서버 PID를 얻어 실행 파일 경로가 자기 자신(`Environment.ProcessPath`)과 같은지 확인하고, 다르면 전송하지 않고 로그. 문서 §15 위협 모델에 "동일 사용자 프로세스는 신뢰 경계 안"임을 명시.
- 완료 기준: 다른 EXE가 파이프 이름을 선점한 상태에서 host가 쿠키를 보내지 않고 오류 응답. 단위 테스트는 경로 비교 함수만.
- 적용 메모(2026-09-22): PipePeerVerifier: 파이프 서버 프로세스 신원 확인 순수 로직 + Win32 조회; NativeHostRelay 도입: 클라이언트 CurrentUserOnly + 서버 EXE 검증 후에만 전송; 문서 갱신: §7.2 코드 발췌·§15 위협 모델·§17 파일표·§18 장애 대응 + README + D1 §15 반영 수동 확인 4건 대기

#### [부분] T1.6 stale 결과와 캐시 범위 — P1
- 근거: `ProcessResultAsync(:209-222)`는 HTTP 호출 전에만 requestId를 검사하고 `_activeRequestId`를 지운다. 호출 중 사용자가 월을 이동하면(`UpdateRequest(:53-60)`가 새 범위 설정) 이전 범위 결과가 그대로 `SyncCompleted`로 전달돼 `App.xaml.cs:136-147`에서 UI와 캐시에 반영된다. 캐시(`App.xaml.cs:143-147`)는 마지막 성공 범위를 저장하므로, 다음 달을 보다가 종료하면 다음 기동 시 이번 달이 비어 보인다.
- 검증 상태: 코드 확인.
- 지시: HTTP 완료 후 `lock`에서 `fromSnapshot/toSnapshot == _from/_to`를 재확인, 다르면 결과를 버리고 즉시 재발행(`_forceRefresh = true`). 캐시에 `RangeFrom/RangeTo`를 저장하고, 기동 시 오늘이 범위 밖이면 캐시를 표시하되 상태에 "캐시(범위 밖)"로 표기하고 즉시 강제 동기화. 또는 현재 달 범위 결과만 캐시에 쓴다(둘 중 전자 권장).
- 완료 기준: 월 이동 직후 이전 범위 응답이 UI를 덮지 않음(테스트로 `CalendarBridgeServer` 상태 전이 검증). 다음 달로 이동 후 재시작해도 이번 달 일정이 캐시로 보임.
- 적용 메모(2026-09-22): HTTP 완료 후 범위 재확인으로 stale 동기화 결과 폐기 + 재발행; 캐시에 조회 범위 저장 + 기동 시 '캐시(범위 밖)' 표기 수동 확인 3건 대기

#### [부분] T1.7 종료 지연 제거와 postResult 즉시 응답 — P1
- 근거: `client.SendAsync(:264)`에 취소 토큰 없음. `DisposeAsync(:390-398)`가 `_acceptLoop`를 기다리므로 HTTP 진행 중 종료하면 트레이 아이콘이 사라진 뒤 최대 30초 프로세스 잔류(`App.xaml.cs:384-394`). 또 `HandlePipeClientAsync(:106-130)`가 HTTP 완료까지 응답을 미루고 `maxNumberOfServerInstances: 1`이라 그 사이 `getConfig`는 2.5초 timeout(`NativeMessagingHost.cs:41`)으로 실패해 heartbeat가 끊긴다. `_leaseUntil`은 postResult 도착 시 해제(:220-221)되고 `_nextAttemptAt`은 과거라, 처리가 직렬이 아니었다면 중복 fetch가 발생하는 구조.
- 검증 상태: 코드 확인.
- 지시: (1) `_cts.Token`을 `SendAsync`에 전달. (2) `postResult`는 requestId 검증 후 즉시 ok 응답, HTTP는 백그라운드 Task로. (3) lease/`_activeRequestId` 해제를 HTTP 완료 시점으로 옮겨 중복 fetch 차단. (4) 파이프 서버 인스턴스를 2~4로 늘리거나 accept 루프를 요청 처리와 분리.
- 완료 기준: 동기화 중 "종료" 클릭 시 1초 내 프로세스 종료. 30초짜리 가짜 HTTP 응답(테스트 서버)에서 `getConfig`가 실패하지 않음.
- 적용 메모(2026-09-22): CalendarBridgeServer: postResult 즉시 ok 응답 + HTTP 백그라운드화 + lease 해제 시점 이동 + 취소 토큰 전달; 파이프 accept 루프와 요청 처리 분리(인스턴스 4) + 파이프 동시성 테스트 + 문서 §10.2/§10.3 정정 수동 확인 6건 대기

#### T1.8 자정/월 전환 갱신 — P1
- 근거: `MainViewModel.cs:32`의 `_displayMonth`는 생성 시 고정, `IsToday`(:185)는 `BuildCalendar` 시점 계산. `MainWindow.xaml.cs:543-561` 시계 타이머는 텍스트만 갱신. `App._refreshTimer(:297-306)`는 `RefreshAsync(false)`→`UpdateRequest(force:false)`가 범위/설정 불변이면 no-op이라 실질적으로 죽은 타이머(주기 동기화는 `RecordSuccess(:334-341)`의 `_nextAttemptAt`이 결정).
- 검증 상태: 코드 확인.
- 지시: 시계 틱에서 날짜 변경 감지 → `BuildCalendar()`; 월이 바뀌었고 사용자가 이번 달을 보고 있었다면 `GoToday()` + `RefreshAsync(true)`. `_refreshTimer`는 제거하거나 이 용도로 재정의하고 문서 §10.1을 "주기는 CalendarBridgeServer가 결정"으로 정정.
- 완료 기준: 시스템 시각을 자정 넘겨 바꾸면 5초 내 오늘 배지가 이동. 테스트: `MainViewModel`에 `Now` 주입 가능하게 리팩터링 후 날짜 전환 케이스.

#### T1.9 설정 상한과 입력 검증 — P1
- 근거: `SettingsWindow.xaml.cs:84-88`은 `refreshMinutes < 1`만 검사. `App.xaml.cs:301-303`의 `DispatcherTimer.Interval`은 약 35,791분(Int32.MaxValue ms) 초과 시 예외 → 설정 저장 직후 종료. 캘린더 ID는 공백·쉼표·세미콜론으로 분리(`:71-76`)하나 숫자 형식 검증 없음.
- 검증 상태: 코드 확인(상한 예외는 .NET 동작 기반 추정, 재현 후 확정).
- 지시: RefreshMinutes 1~1440 제한, 캘린더 ID는 숫자만 허용(실데이터: 5자리 또는 19자리 정수). 설정창 안내 문구 갱신.
- 완료 기준: 범위 밖 입력이 거부되고 앱이 종료되지 않음.

#### T1.10 캐시 시각 노출 — P1
- 근거: `App.xaml.cs:86-98`에서 `SetEvents(..., "캐시 HH:mm")` 직후 `_syncStatus.MarkWaiting()`이 "Chrome 백그라운드 대기"로 덮어씀. `SyncStatusService._lastSuccess`는 캐시 로드 시 설정되지 않아 `EvaluateHealth(:76-98)`의 "마지막 HH:mm"도 안 나옴.
- 검증 상태: 코드 확인.
- 지시: `SyncStatusService.MarkCacheLoaded(DateTimeOffset)` 추가, 대기/연결 대기 문구에 "· 캐시 MM-dd HH:mm" 접미. `MainWindow.SetEvents`의 `updatedAt` 파라미터(현재 미사용, `MainWindow.xaml.cs:118-123`)를 활용하거나 제거.
- 완료 기준: Chrome이 꺼진 상태로 부팅해도 상태에 캐시 시각이 보임.

### P2. 아키텍처·성능·배포 개선

#### T2.1 Native host 경량화 — P2 (Q3)
- 근거: `sendNativeMessage`(`service-worker-v700.js:36-38, 145, 160`)는 호출마다 host를 새로 띄움(웹 확인). host 모드에서도 `App` 필드 초기화(`App.xaml.cs:14-20`: `SettingsService`/`CacheService` 생성자가 `Directory.CreateDirectory`)와 WPF `Application`/`App.xaml` 리소스 로드가 끝난 뒤에야 분기(`:38-43`). self-contained + `EnableCompressionInSingleFile`(csproj:12-15)이라 기동마다 압축 해제 비용. 30초마다 1회, fetch 시 2회, 오버레이가 꺼져 있어도 2.5초 대기 후 실패를 무한 반복(`NativeMessagingHost.cs:41`).
- 검증 상태: 코드 확인 + 웹 확인. 실제 스폰당 CPU/시간은 미측정.
- 지시(기본안 Q3-a): (1) `Program.cs`에 `[STAThread] Main` 추가, `<StartupObject>` 지정, `App.xaml`의 자동 Main 비활성화. Main에서 `IsNativeInvocation(args)`이면 WPF를 만들지 않고 `NativeMessagingHost.RunAsync()`만 실행. 서비스 필드 초기화는 GUI 경로로 이동. (2) 스폰당 시간(프로세스 시작~응답)과 CPU를 before/after로 측정해 문서 성능 절에 기록. (3) Q3-b(`connectNative` 장수명 포트)는 측정치가 기준(예: 스폰당 300ms 초과)을 넘으면 별도 작업으로.
- 완료 기준: host 모드에서 `App` 생성이 일어나지 않음(로그로 확인). 측정치가 문서에 기록됨.

#### T2.2 확장 트리거·백오프·버전 협상 — P2
- 근거: `service-worker-v700.js:181-208`에서 onInstalled/onStartup/cookies.onChanged/onAlarm/action.onClicked/최상위 호출 6개가 모두 `syncOnce` → DaouOffice 페이지 로드 시 쿠키 변경마다 host 스폰. `inFlight`는 동시 실행만 막고 연속 실행은 못 막음. `getConfig` 실패 시 백오프 없음(`:144-148`). `ensureAlarm(:172-179)`은 기존 알람 주기 미갱신. 확장 버전을 앱에 전달하지 않아 EXE가 확장 파일을 덮어써도(`ChromeExtensionInstaller.cs:29-54`) 불일치를 감지 못함. `BridgeFailureKind.Extension`/`ExtensionFailure(:469-473)`/`MarkExtensionError`/`OverlaySyncState.ExtensionError`는 생성 경로가 없어 도달 불가.
- 검증 상태: 코드 확인.
- 지시: (1) `cookies.onChanged`는 5초 debounce, 최상위 `syncOnce()` 호출 제거(onStartup/onInstalled/onAlarm만). (2) host 연결 실패가 연속 3회면 알람 주기를 1→2→5분으로 늘리고 성공 시 0.5분 복귀(`chrome.alarms.create`로 재생성). (3) `ensureAlarm`은 `periodInMinutes` 불일치 시 재생성. (4) `getConfig` 요청에 `extensionVersion`(`chrome.runtime.getManifest().version`)과 `protocolVersion`을 포함, 앱은 불일치 시 `ExtensionFailure`로 "Chrome 확장 새로고침 필요 (7.0.0 → 7.1.0)"를 표시. `manifest.json` 버전을 7.1.0으로 올리고 worker 파일명 규칙(`service-worker-v710.js`)과 `ChromeExtensionInstaller` obsolete 목록 갱신.
- 완료 기준: DaouOffice 페이지를 새로고침해도 5초 내 host 스폰 1회 이하(로그로 확인). 오버레이 종료 후 5분 뒤 알람 주기가 5분(chrome://extensions 서비스 워커 콘솔로 확인). 구버전 확장 로드 시 상태에 불일치 문구.

#### T2.3 버전 체계 — P2
- 근거: `app.manifest:3` `1.0.0.0`, csproj에 `Version` 계열 속성 없음, `manifest.json:4` `7.0.0`, 문서 표지 `v7.0.0`. 앱 내 버전 표시 없음.
- 검증 상태: 코드 확인.
- 지시: csproj에 `<Version>7.1.0</Version>`, `<FileVersion>`, `<InformationalVersion>`(+git 해시), app.manifest 동기화. 트레이 툴팁·설정창 하단·로그 첫 줄에 버전 표시. `NativeBridgeProtocol`에 `ProtocolVersion = 1` 상수. `CHANGELOG.md` 시작(7.0.0 as-built, 7.1.0 이번 작업).
- 완료 기준: EXE 속성 창과 트레이 툴팁이 같은 버전을 보임.

#### T2.4 제거·업그레이드 경로 — P2
- 근거: 등록 코드만 있고(`StartupService.cs:10-32`, `NativeMessagingRegistrationService.cs:24-46`) 해제 코드·CLI·문서 절차 없음. EXE를 지우면 Chrome이 30초마다 존재하지 않는 경로를 실행 시도. 실행 중 EXE 교체는 파일 잠금으로 실패.
- 검증 상태: 코드 확인.
- 지시: `--uninstall` 인자: Run 키·NativeMessagingHosts 키(Chrome/Edge) 삭제, `%LOCALAPPDATA%\DaouCalendarOverlay` 삭제 여부 확인 창(로그·설정·캐시). 트레이 "완전 제거…" 항목. README와 문서에 설치/업그레이드(EXE 종료 → 교체 → 실행 → chrome://extensions 새로고침)/제거 절차. `IsNativeInvocation` 이전에 인자 파싱.
- 완료 기준: `--uninstall` 실행 후 레지스트리 3곳에 값 없음.

#### T2.5 브라우저 매트릭스와 "열기" 버튼 — P2 (Q4)
- 근거: `NativeMessagingRegistrationService.cs:10`은 `Software\Google\Chrome\...`만. `App.OpenDaouOffice(:207-224)`는 `UseShellExecute`로 기본 브라우저를 열어 Chrome이 기본이 아니면 재로그인해도 확장이 쿠키를 얻지 못함.
- 검증 상태: 코드 확인. Edge가 Chrome 키를 fallback으로 읽는지는 미확인.
- 지시(기본안 Q4-a): `HKCU\Software\Microsoft\Edge\NativeMessagingHosts\com.daou.calendar_overlay`에도 등록(설정 옵션 "Edge에도 등록", 기본 on). "열기"는 레지스트리 `App Paths\chrome.exe`로 Chrome을 직접 실행하고 실패 시 기본 브라우저. 문서에 지원 매트릭스(Windows 10/11 x64, Chrome 120+, Edge 시험 지원, Whale/Brave 미지원, 단일 프로필 가정, KST).
- 완료 기준: Edge에서 같은 unpacked 확장 로드 시 동기화 동작(수동 확인 기록). 기본 브라우저를 Edge로 바꿔도 "열기"가 Chrome을 띄움.

#### T2.6 빌드·배포 스크립트 정비 — P2
- 근거: `publish.ps1:14-28`은 `$ErrorActionPreference="Stop"`이 네이티브 exe 종료 코드에 적용되지 않아 실패해도 "Publish completed" 출력. csproj:11-12 전역 `RuntimeIdentifier`/`SelfContained`로 개발 빌드도 self-contained(`bin\Release\...\win-x64`에 런타임 전체 존재, 실데이터 확인). `DebugType=None`(csproj:17-18)이라 크래시 라인 정보 없음. 코드 서명 없음(SmartScreen 경고 예상, 추정).
- 검증 상태: 코드 확인 + 실데이터 확인.
- 지시: `publish.ps1`에 `if ($LASTEXITCODE -ne 0) { throw }`, csproj 중복 속성 제거(publish 프로파일 `Properties/PublishProfiles/win-x64.pubxml`로 이동), 개발 빌드는 framework-dependent, `DebugType=portable` + PDB는 `publish/symbols/`로 분리, 산출물에 버전 포함(`DaouCalendarOverlay-7.1.0.exe`). 서명은 인증서 확보 후 자리만(스크립트 파라미터).
- 완료 기준: `publish.ps1` 실패 시 비정상 종료 코드. `dotnet build`가 5초 내(런타임 복사 없음). publish EXE 정상 실행.

#### T2.7 .NET 10 이전 시험 — P2 (Q5)
- 근거: TFM `net8.0-windows`(csproj:4), 지원 종료 2026-11-10(웹 확인), 머신에는 SDK 10만.
- 검증 상태: 웹 확인.
- 지시(기본안 Q5-b): 브랜치 `net10-trial`에서 TFM을 `net10.0-windows`로 바꿔 빌드·publish·기동 시간·EXE 크기·host 모드 동작을 비교하고 결과를 `docs/net10-migration.md`에 기록. 릴리스 전환은 사용자 결정.
- 완료 기준: 비교표가 문서에 있음.

#### T2.8 시작 실패 비치명화 — P2
- 근거: `App.xaml.cs:59-108`에서 `EnsureExtracted`/`EnsureRegistered` 예외가 최상위 catch로 가 MessageBox 후 종료. GPO로 HKCU 쓰기가 막히면 캐시 표시조차 불가. 오류 문구가 원인과 무관하게 settings.json을 지목(`:103-104`).
- 검증 상태: 코드 확인.
- 지시: 두 호출을 개별 try/catch로 감싸 실패는 로그 + 상태 "Native host 등록 실패: …"로 표시하고 오버레이는 계속. 오류 문구는 실제 예외 종류별로.
- 완료 기준: 레지스트리 키를 읽기 전용으로 만든 상태에서 앱이 캐시로 기동.

#### T2.9 UX 소소한 개선 묶음 — P2
- 근거·지시(각각 한 커밋):
  1. 그리드 칩 ToolTip: `MainWindow.xaml:501-525` 칩 템플릿에 `ToolTip="{Binding Tooltip}"` 바인딩(`MainViewModel.BuildTooltip(:277-295)`은 계산되지만 미사용).
  2. 필터 포커스 키: `MainWindow.xaml.cs:344-377`의 `KeyDown`은 TextBox가 Home/PageUp/PageDown을 먼저 처리하면 안 옴. `PreviewKeyDown`에서 Ctrl 조합(예: Ctrl+Home)으로 처리하거나 필터 밖 포커스에서만 동작함을 문서화. Esc는 필터에 포커스가 있으면 필터만 비우고 창은 숨기지 않기.
  3. 상태 텍스트 ToolTip: `MainWindow.xaml:563-569` `CharacterEllipsis`로 잘린 오류 전문을 ToolTip으로.
  4. 첫 실행 창: `App.xaml.cs:66-73`에서 Owner 없음 → `WindowStartupLocation=CenterScreen`.
  5. 캘린더 표시 메뉴 이름: `GetCalendarDescriptors(:68-92)`는 범위 내 일정이 없는 캘린더를 ID로만 표시 → 마지막으로 본 이름을 settings에 `CalendarNames` 사전으로 저장.
  6. 색상 매핑: `GetEventColor(:297-304)`는 정수 인덱스를 8색 팔레트에 `% 8`로 매핑(실데이터 인덱스 1~18) → DaouOffice 웹의 색표를 채집해 인덱스→색 표로 교체, 미정의 인덱스만 해시 fallback. `int.MinValue`의 `Math.Abs` 예외 방어.
  7. 투명도(Q7 기본안 b): `CreateOpacityBrush(:597-603)` alpha 상한 230을 문서에 "최대 90%"로 명시.
  8. 초기화 버튼 3회 재구성: `FilterReset_Click(:332-342)`에서 `SelectionChanged`→`ClearFilter`→디바운스 틱 순으로 `BuildCalendar` 최대 3회 → 플래그로 1회.
  9. 시크릿/다중 프로필: `readCookiesFromAllStores(:40-73)`가 여러 store 쿠키를 한 헤더에 합침 → 기본 store(`"0"`) 우선, 다른 store는 기본이 비었을 때만.
- 완료 기준: 각 항목 수동 확인 기록.

#### T2.10 죽은 코드 정리 — P2
- 근거: `MainWindow.xaml.cs:649-661` `HasButtonAncestor` 미사용. `App.xaml:78-103` `MoreButtonStyle` 미사용. `EventChipViewModel.Tooltip`(T2.9-1에서 사용). `MainWindow.SetEvents`의 `updatedAt`(T1.10에서 사용). `BridgeSyncEventArgs.ExtensionFailure`(T2.2에서 사용). `App._refreshTimer`(T1.8에서 재정의). `NativeBridgeProtocol.Utf8(:43)` 미사용. 실행 파일 경로 획득이 두 방식(`Environment.ProcessPath` vs `Process.MainModule`, `App.xaml.cs:324-327`).
- 지시: 사용처가 생기지 않는 항목은 삭제, 경로 획득은 `Environment.ProcessPath`로 통일.
- 완료 기준: 빌드 경고 0 유지.

### P3. 문서 보완 (기술 문서 HTML + README)

문서 파일은 `docs/DaouCalendarOverlay_Technical_Documentation.html`로 저장소에 복사해 버전 관리한다(현재는 Downloads에만 존재).

#### D1 정확성 정정 목록 — P3
각 항목을 해당 절에 반영한다. 코드 변경(P1/P2)으로 사실이 바뀌는 항목은 변경 후 상태로 쓴다.

| 절 | 현재 서술 | 정정 |
|---|---|---|
| §5-A/B | GUI 시작 순서, "native 모드는 GUI 초기화를 건너뜀" | 실제 순서(`App.xaml.cs:32-99`)로, native 모드에서도 필드 초기화로 디렉터리 생성이 일어남(T2.1 후에는 사라짐) |
| §9.4 | "occurrence가 개별 event로 내려오는 동작을 전제", "음력 휴일도 서버 계산 결과를 신뢰" | 실데이터 기준으로: 반복 일정은 범위 내 occurrence + 1970년 템플릿 + 범위 밖 occurrence가 함께 옴, `id`는 occurrence·캘린더 간 중복, 종일 종료일은 포함 의미(23:59:59.999 또는 시작과 동일), 공휴일 RRULE `UNTIL=20261231`이라 2027년 공휴일은 서버 데이터 갱신 필요(앱 버그 아님) |
| §10.1 | "앱 쪽 N분 주기", "ShouldFetch가 false면 worker는 종료" | 주기는 `CalendarBridgeServer._nextAttemptAt`이 결정하고 `App._refreshTimer`는 fetch를 유발하지 않음(T1.8 반영). worker는 30초 알람으로 사실상 상시 활성. 확장 트리거 6종 나열 |
| §10.2 | lease 서술 | lease는 postResult 도착 시 해제되고 응답은 HTTP 완료까지 지연됨(T1.7 후: 즉시 응답, HTTP 완료 시 해제) |
| §10.3 | backoff | 인증 실패도 `_consecutiveFailures`를 증가시킴. 600초 상한은 도달 불가(최대 480) |
| §10.4/§18 | 상태 문자열 | 실제 문구로 교체: "시작 중…", "Chrome 백그라운드 대기", "Chrome 브리지 연결됨 · 동기화 대기", "동기화 요청 중…", "동기화 중…", "정상 · 동기화 HH:mm", "DaouOffice 재로그인 필요" 또는 브리지 오류 문구, "네트워크 오류: … · 재시도 HH:mm", "Chrome 확장 연결 대기 · 마지막 HH:mm". `ExtensionError`는 T2.2 전까지 도달 불가 상태임을 표기 |
| README "동작 확인" | "Chrome 확장 연결 대기 → Chrome 브리지 연결됨 → …" | 정상 경로는 "Chrome 백그라운드 대기 → 동기화 요청 중… → 동기화 중… → 정상 · 동기화 HH:mm" |
| §11 | 요일/공휴일 색 규칙 | 다른 달 셀은 회색(주말색 미적용), 오늘 셀은 흰색, 공휴일 판정은 `type == "holiday"`, 종일 정렬 우선 |
| §11.2 | 투명도 | 슬라이더 100%는 alpha 230(약 90%) |
| §13 | 설정 항목 | `StartWithWindows` 추가, Run 값 이름 `DaouCalendarOverlay`, single-file 네이티브 추출 디렉터리(`%TEMP%\.net\DaouCalendarOverlay`) |
| §15 | "Named Pipe CurrentUserOnly" | 서버 측만 적용(T1.5 후 클라이언트도) |
| §17 | 파일 표/트리 | `Models/AppSettings.cs`, `Models/DayCellViewModel.cs`, `GlobalUsings.cs`, `README.md`, `.gitignore`, `docs/`, `tests/` 추가 |
| §20 | 상수표 | 추가: pipe connect 2.5s, Chrome→host 64MiB / host→Chrome 1MB / pipe 8MiB, activation pipe 350ms×5회, 필터 debounce 180ms, 위치 저장 400ms, 투명도 저장 450ms, backoff 실제 상한 480s, 실패 카운터 상한 8 |
| 표지/§18 | 버전 | T2.3 후 EXE 버전과 확장 버전 병기 |
| 코드 발췌 | §9.1, §10.2, §20이 메서드 중간에서 시작 | 메서드 경계로 다시 자르기 |

#### D2 신규 절 — P3
1. **시퀀스 다이어그램**(정상 / 쿠키 없음 / 401·리디렉션 / 네트워크 오류 / 오버레이 미실행 5가지): Chrome alarm → getConfig → host 스폰 → pipe → BuildConfig → 쿠키 수집 → postResult → HTTP → SyncCompleted → UI/캐시.
2. **브리지 메시지 스키마 표**: `getConfig`, `postResult`(requestId, cookieHeader, cookieCount, cookieSource, userAgent, error, T2.2 후 extensionVersion/protocolVersion/lastError), 응답(ok, error, config{shouldFetch, requestId, baseUrl, timeMin, timeMax, includingAttendees, calendarIds, T1.3 후 noFetchReason}).
3. **상태 머신 전이표**: 상태 × 트리거 × 표시 문구 × isError × health 평가 예외.
4. **설치 / 업그레이드(v6→v7, 확장 새로고침 포함) / 제거 절차**(T2.4 연동).
5. **지원 환경 매트릭스**(T2.5 연동).
6. **로그·진단 절차**(T1.1 연동): 로그 경로, 대표 로그 라인, chrome://extensions 서비스 워커 콘솔 확인법, "native host not found" 진단 순서.
7. **테스트 체크리스트와 검증 기록**: 단위 테스트 목록 + 수동 시나리오(첫 실행, 재로그인, Chrome 종료, 월 이동, DPI 변경, 다중 모니터, 자정 전환).
8. **성능 특성**(T2.1 측정치): 스폰당 시간/CPU, 폴링 부하, 그리드 재구성 비용.
9. **위협 모델 표**: 자산(세션 쿠키, 일정 데이터), 신뢰 경계(Chrome 프로필 / 확장 / host / GUI / 디스크), 공격 벡터(파이프 선점, 확장 폴더 변조, 캐시 열람), 완화 상태.
10. **버전 이력**(CHANGELOG 연동)과 버저닝 정책(EXE·확장·프로토콜).
11. **요구사항과 비목표**: 예) 일정 편집 없음, 알림 없음, KST 고정, 단일 Chrome 프로필.
12. **settings.json 스키마**와 검증 규칙(T1.3).
13. **Daou API 응답 계약**: 2절 실데이터 규칙(필드 도메인, 종료일, 중복 id, 범위 밖 이벤트, 1970 템플릿, UNTIL).
14. **스레딩 모델**: 파이프 스레드 / DispatcherTimer / UI 마샬링 지점.
15. **확장 서비스 워커 트리거 표**(T2.2 후 기준).
16. **빌드 전제조건·릴리스 절차**(T2.6 연동).
17. **백로그**: Q3-b connectNative, Q6 DPAPI, Q8 CRX 배포, 알림 기능, 데스크톱 고정(bottom-most) 모드.
18. **용어집**, **데이터 보존·삭제 규칙**(캐시에 동료 이름·이메일·직급 평문, 삭제 방법).

---

## 5. 검토했으나 문제 없음 (재검토 불필요)

- 문서 §6.1, §7.1~7.3, §9.1~9.3, §10.2~10.4, §11.2, §12, §14.1, §16, §16.2, §20의 코드 인용과 상수는 현재 소스와 완전히 일치.
- 프레이밍(`NativeBridgeProtocol`): 길이 음수/상한 검사, EOF 처리, flush 정상.
- 스레드 경계: 파이프 스레드 이벤트는 `Dispatcher.InvokeAsync`로 마샬링, `AppSettings`는 lock 하에 Clone.
- HTTP 판정 순서(401/403 → 3xx → HTML/login → 비성공 코드 → envelope code): 정상. `AllowAutoRedirect=false`, `UseCookies=false`.
- 종일 일정 종료일 처리: 실데이터가 포함(inclusive) 의미라 `OccursOnDate` 정확.
- DPI/멀티모니터: `PerMonitorV2`, `WM_DPICHANGED`/`DisplaySettingsChanged` 시 작업 영역 클램프 정상.
- 싱글 인스턴스: `Local\` Mutex + activation pipe 정상.
- `Environment.ProcessPath`는 single-file publish에서 실제 EXE 경로를 반환하며 host manifest/Run 키에 올바르게 기록됨(설치 상태 확인).
- 확장 `key`는 공개키이며 manifest 포함이 정상. `background` 권한은 MV3에서 유효.

## 6. Opus 실행 지침

1. **순서**: T0.1 → T0.2 → P1(T1.1부터 번호순) → P2 → P3. P3의 D1은 관련 코드 작업이 끝날 때마다 해당 행을 즉시 반영해도 된다.
2. **커밋 단위**: 작업 하나 = 커밋 하나. 메시지 첫 줄에 작업 ID(`T1.3: BaseUrl 검증 규칙 통일`).
3. **빌드/테스트**: 매 커밋 전 `dotnet build -c Release`(경고 0 유지)와 `dotnet test`.
4. **금지**: 확장 ID/`key`, host name `com.daou.calendar_overlay`, 파이프 이름, LocalAppData 경로, settings.json 기존 키 이름은 변경하지 않는다. 파이프 프로토콜을 호환 불가하게 바꿔야 하면 파이프 이름을 `.v8`로 올리고 확장 버전도 올린다.
5. **확장 변경 시**: 버전을 올리고 worker 파일명을 `service-worker-v<ver>.js`로 바꾸며 `ChromeExtensionInstaller`의 obsolete 목록에 이전 파일명을 추가한다. README의 설치 절차에 "확장 새로고침" 단계를 유지한다.
6. **문서 HTML 편집**: 기존 스타일·구조를 유지하고 절 번호를 바꾸지 않는다(신규 절은 §21 이후 또는 부록). 코드 발췌는 실제 파일에서 다시 복사한다.
7. **검증 기록**: 수동 확인이 필요한 항목은 `docs/verification-log.md`에 날짜·환경·결과를 남긴다.
8. **보고**: 완료 후 이 파일 끝에 "결과 요약" 절을 추가한다(완료/부분/보류 목록, 측정치, 남은 결정).
