# 검증 기록 (verification-log)

작업 지시서 `docs/OPUS_WORK_ORDER_2026-09-21.md` 6절 #7에 따라 수동/독립 검증 결과를 남긴다. 각 항목은 날짜·환경·결과를 포함한다.

## 환경

| 항목 | 값 |
|---|---|
| OS | Windows 11 Pro 10.0.26200 |
| .NET SDK | 10.0.401 (프로젝트 TFM net8.0-windows) |
| 저장소 | `C:\Users\USER\Develop\DaouCalendarOverlay_MVP` (main) |
| 기준 커밋 | `a046a1b` = 태그 `v7.0.0-asbuilt` |

## P0

| 날짜 | 작업 | 검증 방법 | 결과 |
|---|---|---|---|
| 2026-09-21 | T0.1 | `git log --decorate`, `git status` | 태그 `v7.0.0-asbuilt`가 `a046a1b`에 부여됨. docs/·.claude/·CLAUDE.md 커밋 `73ba742`. 작업 트리 깨끗함. |
| 2026-09-21 | T0.1 빌드 기준선 | `dotnet build -c Release` | 경고 0 / 오류 0, 3.9초 |
| 2026-09-21 | T0.2-T1 테스트 골격 | verifier(Sonnet) 독립 실행: sln list, build, test, csproj 대조, 금지 파일 diff | PASS. 테스트 1건(프로토콜 상수 가드) 통과. 메인 csproj·publish.ps1 무변경. |
| 2026-09-21 | T0.2-T2 순수 로직 추출 | senior-implementer 동등성 하니스(원본 본문 복사본 vs 새 `CalendarGrid`/`CalendarUrlParser`, 스크래치패드) + verifier 정적 대조 | 8,188케이스(월 2,412×2 + 이벤트 55종×60일 + URL 22종) 불일치 0. verifier PASS: diff가 spec 범위에 한정, 의미 동일. |
| 2026-09-21 | T0.2-T3 테스트 작성 | verifier: build, test 전체/필터, `--list-tests` 이름 전수 대조, 금지 API grep, 의미 검토, 인코딩 | PASS. 83건 통과(CalendarGrid 51, NativeBridgeProtocol 11, CalendarUrlParser 15, MainViewModel 5, 상수 가드 1). 2026-09-01=화, 2026-11-01=일 요일 재확인. |
| 2026-09-21 | P0 중간 검토 | reviewer(Fable): 설계 일치·불변 조건·구조·테스트 품질·csproj 설정·P1 제약 | **approve**. blocker/major 없음. minor 5건(아래). |

### P0 중간 검토 minor (P1 이후 이월)

1. `tests/DaouCalendarOverlay.Tests/DaouCalendarOverlay.Tests.csproj` RID/SelfContained/ValidateExecutableReferencesMatchSelfContained 3속성은 메인 csproj 전역 RID 우회 → T2.6에서 메인 csproj 정리와 함께 제거.
2. `MainViewModelTests.cs`의 `RunSta` 헬퍼 미사용 → T1.8 테스트 재작성 시 삭제.
3. `MainViewModelTests.cs`가 `DateTime.Today`를 직접 읽음(자정 경계 flaky 가능) → T1.8에서 고정 시계 주입으로 교체.
4. `CalendarGridTests.OccursOnDate_AllDay_UsesEventOwnOffsetDateNotInstant`는 9/22 false 단언이 없어 종일(이벤트 오프셋 벽시계)/시간(KST 인스턴트) 비대칭을 완전히 고정하지 못함 → 후속 보강 시 추가.
5. `DaouCalendarOverlay.sln`은 `dotnet sln add`가 BOM+CRLF+탭으로 재작성하고 x64/x86 구성을 추가함(무해).

### P1 착수 제약 (중간 검토에서 P1 lead에게 전달)

1. `CalendarGrid`는 시계를 갖지 않는다. T1.8의 `Now` 주입은 `MainViewModel` 생성자 파라미터(`Func<DateTime>` 또는 `TimeProvider`)로 하고 `DateTime.Today` 3곳(`MainViewModel.cs` 생성자·`GoToday`·`BuildCalendar`)을 모두 교체. `CalendarGrid` 시그니처 유지.
2. "오늘"의 기준(로컬 vs KST)을 T1.8에서 결정하고 문서화. KST 고정이면 `CalendarGrid.GetToday(DateTimeOffset now) => now.ToOffset(KstOffset).Date` 권장.
3. T1.3: 스킴/호스트 정책은 `Services/BaseUrlPolicy`에만. `CalendarUrlParser`에 https 검증을 넣지 않는다. `Parse_AcceptsHttpScheme` 유지.
4. T1.4: dedupe는 `SetEvents` 입력 단계의 순수 함수로 분리. `OccursOnDate`·정렬 계약 불변. 숨김 판정 "모두 숨김일 때만"은 `_hiddenCalendarIds.Contains` 2곳에 동일 적용.
5. T1.6: 캐시 `RangeFrom/RangeTo`는 `CalendarGrid.GetVisibleRange(month)` 반환값 그대로 저장. 범위 밖 판정도 `KstOffset` 기준.
6. T2.6: 메인 csproj RID/SelfContained를 pubxml로 옮길 때 테스트 csproj 3속성 함께 제거. 완료 기준에 포함.
7. InternalsVisibleTo 금지 유지. 테스트 필요 시 순수 로직을 public 정적 클래스로 추출.
8. `ProtocolConstantsTests`는 6절 금지 상수 가드. 깨지면 테스트가 아니라 변경을 되돌린다.
9. 모든 P1 태스크 완료 기준에 테스트 포함. STA 헬퍼 없이 순수 로직만 대상.

## P1

| 날짜 | 작업 | 검증 방법 | 결과 |
|---|---|---|---|
| 2026-09-22 | T1.1 | verifier(Sonnet) 독립 실행: build 경고 0, test 실패 0, spec 항목 대조 (구현 T1.1a opus 1회, T1.1b opus 1회, T1.1c opus 1회) | PASS. dotnet build (Release) 경고 0/오류 0. dotnet test (Release) 총 98건 전부 통과(기존 5개 클래스 83건: CalendarGridTests 51/CalendarUrlParserTests 15/NativeBridgeProtocolTests 11/MainViewModelTests 5/ProtocolConstantsTests 1 = 변경 없음, 신규 15건: LogFormatterTests 4/LogRotationTests 6/LogServiceTests 5 전부 통과). --filter "FullyQualifiedName~Log" 3회 반복 실행 매번 15/15 통과(플레이크 없음). rg로 LogService.Initialize/LogDirectory/DefaultLogDirectory 시그니처, LogRotation.MaxFileBytes/MaxFiles 상수 각 1건 확인. "LogService." 호출이 LogService.cs 밖에 0건(호출부 배선 없음, spec 준수). csproj에 PackageReference 0건·git diff 없음(패키지 미추가). git status --porcelain은 신규 6개 파일 + 이 태스크 시작 전부터 있던 무관한 6개 파일(.claude/agents/*.md, feature.js, CLAUDE.md, 라우팅 정책 변경)만 표시, 대상 외 변경 없음. 3개 소스 파일과 3개 테스트 파일 모두 BOM 없는 UTF-8. InternalsVisibleTo 추가 없음. 소스 코드를 직접 읽어 LogFormatter/LogRotation/LogService의 모든 동작 규약(개행 치환 순서, 예외 들여쓰기, 롤링 계획, lock 범위, try/catch로 예외 삼킴 등)이 spec과 일치함을 확인. / T1.1b 독립 검증 완료. dotnet build -c Release: 경고 0/오류 0 (clean rebuild 포함 재확인). dotnet test -c Release: 통과 103/실패 0/건너뜀 0 (기존 98 baseline 대비 정확히 +5, 기존 5개 테스트 클래스 무변경 확인). BridgeLogSummaryTests 5건 개별 필터 실행으로 전부 통과 확인. ProtocolConstantsTests.FrozenIdentifiers_MustNotChange 통과. App.xaml.cs/MainWindow.xaml.cs/CalendarBridgeServer.cs/StartupService.cs/NativeMessagingHost.cs/BridgeLogSummary.cs 전문을 읽어 spec 2(a)~(j), 3, 4, 5, 6절의 모든 항목(전역 예외 처리기 3종 등록+SetObserved, ReportError/SaveSettingsAsync 헬퍼, OpenLogFolder, BridgeServer_SyncCompleted 로그·분기별 Warn, OpenDaouOffice/ConfigureRefreshTimer/CreateTrayIcon 예외 안전화, ExitApplication 가드, MainWindow 15개 async void 핸들러 전수 try/catch(가드·부수효과 순서 보존), AcceptLoopAsync/HandlePipeClientAsync/ProcessResultAsync/HTTP catch 3곳 로그, StartupService.Apply catch, NativeMessagingHost RunAsync 로그+Console.Write 미사용)이 코드에 정확히 반영됨을 확인. 금지 위반(InternalsVisibleTo, csproj 변경, NoWarn/pragma, PipeName/HostName/ExtensionId/LocalAppData 상수, SettingsService.cs/CacheService.cs/SyncStatusService.cs/NativeBridgeProtocol.cs) 전부 0건. git status --porcelain 결과 소스 변경은 T1.1b files 7개 ∪ T1.1a files 6개 안에 전부 포함되며, .claude/agents/*.md·CLAUDE.md·feature.js는 이번 태스크 이전부터 있던 무관한 변경(대화 시작 시점 gitStatus 스냅샷과 동일)으로 이번 구현자가 만든 변경이 아님을 확인. manual_checks 5건은 지시대로 판정 제외. / dotnet build 0 warnings/0 errors; dotnet test 107 passed/0 failed (baseline 103 + 4 new ChromeExtensionPackagingTests, exact +4 as required); all 4 new test methods present and green; verified service-worker rename (v710 present, v700 absent from tracked tree), manifest version 7.1.0 + service_worker + key preserved, csproj EmbeddedResource Include/LogicalName both updated, ChromeExtensionInstaller Files+obsolete list updated, NativeBridgeProtocol.cs adds only LastError/ExtensionVersion (pipe name v7 unchanged), CalendarBridgeServer.cs inserts only the two specified lines while T1.1b's bridge.pipe/bridge.result logs remain intact, service-worker-v710.js diff against original v700 shows only the intended additive changes (LAST_ERROR_KEY, recordLastError/readLastError/clearLastError, syncOnce lastError plumbing, postResult catch), README.md and tech doc HTML changes match spec exactly (log section, existing-item text extension, §18 log/diagnosis block with correct representative log lines and cookie-safety note, extension 7.0.0->0/7.1.0->1, no new <h2>21., §17 rows for LogService/LogFormatter/LogRotation/BridgeLogSummary plus the required backlog rows AppSettings/DayCellViewModel/CalendarGrid/CalendarUrlParser/GlobalUsings/README/.gitignore/docs//tests all present, HTML tag balance intact tr/td/table/thead/tbody/section/div all equal open/close), §10.3 backoff correction text present, manifest/csproj excerpts in HTML updated to 7.1.0/v710. No forbidden patterns found: 0 CookieHeader-in-log matches, 0 InternalsVisibleTo, 0 NoWarn/pragma suppression. docs/OPUS_WORK_ORDER_2026-09-21.md and docs/verification-log.md have 0 changes as required. 수동 미확인 9건 |

### 수동 확인 대기 (P1)

- T1.1: 앱을 실제로 실행해 `%LOCALAPPDATA%\DaouCalendarOverlay\logs\overlay-yyyyMMdd.log`에 '앱 시작' 라인이 남는지 확인 — 미확인
- T1.1: Chrome 연동 상태에서 동기화 성공 / DaouOffice 로그아웃 후 인증 실패 / 네트워크 차단 후 네트워크 실패 / 오버레이 미실행 상태의 host 파이프 실패가 각각 로그에 1줄 이상 남는지 확인(host 실패는 host-yyyyMMdd.log) — 미확인
- T1.1: settings.json을 읽기 전용으로 만든 뒤 트레이 설정 저장을 시도했을 때 앱이 종료되지 않고 상태 텍스트에 '설정 저장 실패: …'가 보이는지 확인 — 미확인
- T1.1: 트레이 우클릭 메뉴에 '로그 폴더 열기'가 보이고 클릭 시 로그 폴더가 열리는지 확인 — 미확인
- T1.1: 로그 파일 전체에 대해 쿠키 값(JSESSIONID 등)이 기록되지 않았는지 육안 확인 — 미확인
- T1.1: chrome://extensions에서 `%LOCALAPPDATA%\DaouCalendarOverlay\ChromeExtension`을 새로고침해 확장 버전이 7.1.0으로 표시되고 서비스 워커가 service-worker-v710.js로 로드되는지 확인 — 미확인
- T1.1: 오버레이를 종료한 상태로 두어 getConfig가 실패하게 만든 뒤 오버레이를 다시 켜면 다음 getConfig에 lastError가 실려 오고 앱 로그에 `[WARN] extension: 확장 보고 오류 ver=7.1.0: …` 한 줄이 남는지 확인 — 미확인
- T1.1: 확장 폴더에 이전 service-worker-v700.js가 남아 있던 환경에서 앱을 실행하면 해당 파일이 삭제되는지 확인(obsolete 목록 동작) — 미확인
- T1.1: 기술 문서 §17 표/트리를 브라우저로 열어 추가한 행이 기존 스타일과 동일하게 렌더링되고 표 열 수가 어긋나지 않는지 확인 — 미확인
