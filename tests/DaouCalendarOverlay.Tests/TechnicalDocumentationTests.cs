using System.Globalization;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DaouCalendarOverlay.Models;
using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

/// <summary>
/// 기술 문서 HTML의 구조 불변식(h2 번호가 1부터 연속, nav 링크 대상 id 존재·nav 링크 수 = h2 수,
/// id 중복 없음, 닫는 태그로 끝남)과 D1 정정 결과 문자열(기술 문서·README), 신규 절(§25~)의
/// id·h2 제목·nav 링크 존재, 기존 §21~§24에 보강한 소절 제목을 고정한다.
/// 절 개수·테스트 개수 리터럴은 단언하지 않는다(뒤 작업이 절을 추가한다).
/// <c>v7.0.0</c> 부재는 <see cref="ChangelogTests"/>가 이미 단언한다. 저장소 파일은 읽기만 한다.
/// </summary>
public sealed class TechnicalDocumentationTests
{
    private static readonly string TechnicalDocPath =
        RepoLayout.Path("docs", "DaouCalendarOverlay_Technical_Documentation.html");

    private static readonly string ReadmePath = RepoLayout.Path("README.md");

    private static readonly string WorkOrderPath = RepoLayout.Path("docs", "OPUS_WORK_ORDER_2026-09-21.md");

    private static readonly Regex H2NumberPattern = new(@"<h2>(\d+)\.", RegexOptions.CultureInvariant);
    private static readonly Regex NavBlockPattern = new(@"<nav>(.*?)</nav>", RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex NavLinkPattern = new("<a href=\"#([^\"]+)\">(\\d+)\\.", RegexOptions.CultureInvariant);
    private static readonly Regex IdPattern = new("\\sid=\"([^\"]+)\"", RegexOptions.CultureInvariant);

    [Fact]
    public void TechnicalDoc_H2NumbersAreContiguousFromOne()
    {
        var html = File.ReadAllText(TechnicalDocPath);

        var numbers = H2Numbers(html);

        Assert.NotEmpty(numbers);
        Assert.Equal(Enumerable.Range(1, numbers.Count), numbers);
    }

    [Fact]
    public void TechnicalDoc_NavLinksMatchH2Sections()
    {
        var html = File.ReadAllText(TechnicalDocPath);

        var nav = NavBlockPattern.Match(html);
        Assert.True(nav.Success, "<nav> 블록을 찾지 못했다.");

        var links = NavLinkPattern.Matches(nav.Groups[1].Value);
        Assert.NotEmpty(links);

        var linkNumbers = new List<int>();
        foreach (Match link in links)
        {
            var target = link.Groups[1].Value;
            Assert.Contains($" id=\"{target}\"", html, StringComparison.Ordinal);
            linkNumbers.Add(int.Parse(link.Groups[2].Value, CultureInfo.InvariantCulture));
        }

        Assert.Equal(Enumerable.Range(1, linkNumbers.Count), linkNumbers);
        Assert.Equal(H2Numbers(html).Count, links.Count);
    }

    [Fact]
    public void TechnicalDoc_IdsAreUnique()
    {
        var html = File.ReadAllText(TechnicalDocPath);

        var ids = IdPattern.Matches(html).Select(match => match.Groups[1].Value).ToList();
        var duplicates = ids
            .GroupBy(id => id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.NotEmpty(ids);
        // 실패하면 xUnit이 중복 id 목록을 메시지에 출력한다.
        Assert.Empty(duplicates);
    }

    [Fact]
    public void TechnicalDoc_EndsWithClosingTags()
    {
        var html = File.ReadAllText(TechnicalDocPath);

        Assert.EndsWith("</main></div></body></html>", html.TrimEnd(), StringComparison.Ordinal);
    }

    [Fact]
    public void TechnicalDoc_D1_ProcessAndApiSectionsCorrected()
    {
        var html = File.ReadAllText(TechnicalDocPath);

        // §5: 제거 모드 선행 판정이 들어간 현재 Program.cs 발췌
        Assert.Contains("StartupModeParser.IsUninstallInvocation(args)", html, StringComparison.Ordinal);
        Assert.Contains("UninstallFlow.RunInteractive()", html, StringComparison.Ordinal);
        // §7.1: NativeBridgeProtocol 발췌에 ProtocolVersion 포함
        Assert.Contains("public const int ProtocolVersion = 1;", html, StringComparison.Ordinal);
        // §9.1: 일요일 시작 42칸 그리드 기준 예시 날짜와 메서드 경계 발췌
        Assert.Contains("2026-08-30T00:00:00.000+09:00", html, StringComparison.Ordinal);
        Assert.Contains("2026-10-10T23:59:59.999+09:00", html, StringComparison.Ordinal);
        Assert.Contains("private void Extract_Click(object sender, RoutedEventArgs e)", html, StringComparison.Ordinal);
        Assert.Contains("public static CalendarUrlParseResult Parse(string? rawUrl)", html, StringComparison.Ordinal);
        // §9.3: 현재 ProcessResultAsync 발췌
        Assert.Contains("DiscardIfRangeChanged(settingsSnapshot, fromSnapshot, toSnapshot)", html, StringComparison.Ordinal);
        // §9.4: 실데이터 규칙
        Assert.Contains("UNTIL=20261231", html, StringComparison.Ordinal);
        // §4: Named Pipe 클라이언트
        Assert.Contains("NativeHostRelay", html, StringComparison.Ordinal);

        Assert.DoesNotContain("모든 Cookie Store", html, StringComparison.Ordinal);
        Assert.DoesNotContain("2026-09-01T00:00:00.000+09:00", html, StringComparison.Ordinal);
        Assert.DoesNotContain("개별 event로 내려오는 동작을 전제로", html, StringComparison.Ordinal);
        Assert.DoesNotContain("음력 휴일도 서버 계산 결과를 신뢰", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnicalDoc_D1_SyncUiOperationsSectionsCorrected()
    {
        var html = File.ReadAllText(TechnicalDocPath);

        // §10.2: BuildConfig~ReleaseActiveRequest 연속 구간(종료 가드 포함)
        Assert.Contains("if (_cts.IsCancellationRequested)", html, StringComparison.Ordinal);
        Assert.Contains("private void ReleaseActiveRequest(string requestId)", html, StringComparison.Ordinal);
        // §10.4: SyncStatusService 발췌를 파일 끝까지, 시작 이슈 접미와 NetworkError/GeneralError 변형
        Assert.Contains("StatusChanged?.Invoke(this, new SyncStatusChangedEventArgs", html, StringComparison.Ordinal);
        Assert.Contains("Native host 등록 실패", html, StringComparison.Ordinal);
        Assert.Contains("확장 파일 설치 실패", html, StringComparison.Ordinal);
        Assert.Contains("DaouOffice 응답 시간 초과", html, StringComparison.Ordinal);
        Assert.Contains("네트워크 요청이 취소되었습니다.", html, StringComparison.Ordinal);
        Assert.Contains("저장 실패 · 로그 확인", html, StringComparison.Ordinal);
        // §14.3: 트레이 메뉴 전체
        Assert.Contains("로그 폴더 열기", html, StringComparison.Ordinal);
        Assert.Contains("완전 제거…", html, StringComparison.Ordinal);
        // §10.1: 자정 전환 시 강제 동기화
        Assert.Contains("ClockTimer_Tick", html, StringComparison.Ordinal);
        Assert.Contains("RefreshTodayIfChanged", html, StringComparison.Ordinal);
        // §11: 날짜 숫자 색 우선순위
        Assert.Contains("DayForeground", html, StringComparison.Ordinal);
        // §18: 연결 대기 문구의 마지막 성공 시각 접미
        Assert.Contains("Chrome 확장 연결 대기[ · 마지막 HH:mm]", html, StringComparison.Ordinal);

        Assert.DoesNotContain("재구성에만", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<code class=\"inline\">SyncStatusService</code> 한 곳에서만 만든다", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<td>네트워크 오류 · 재시도 HH:mm</td>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<td>Chrome 확장 연결 대기</td>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnicalDoc_D1_StorageFilesAppendixCorrected()
    {
        var html = File.ReadAllText(TechnicalDocPath);

        // §17: 파일 표·트리 누락분
        Assert.Contains("Services/SettingsValidation.cs", html, StringComparison.Ordinal);
        Assert.Contains(".gitattributes", html, StringComparison.Ordinal);
        Assert.Contains("BridgeRangeGuard.cs", html, StringComparison.Ordinal);
        Assert.Contains("CacheRangePolicy.cs", html, StringComparison.Ordinal);
        Assert.Contains("EventDeduplicator.cs", html, StringComparison.Ordinal);
        // §20: 데이터 모델 발췌를 타입 경계로, 상수표 추가 행
        Assert.Contains("public sealed class DaouCalendarPerson", html, StringComparison.Ordinal);
        Assert.Contains("public sealed class DaouCalendarEvent", html, StringComparison.Ordinal);
        Assert.Contains("ResultLeaseSeconds", html, StringComparison.Ordinal);
        Assert.Contains("PipeServerInstances", html, StringComparison.Ordinal);
        Assert.Contains("COOKIE_DEBOUNCE_MS", html, StringComparison.Ordinal);
        Assert.Contains("TrayTextMaxLength", html, StringComparison.Ordinal);
        Assert.Contains("OverlayRunningExitCode", html, StringComparison.Ordinal);
        // §15: BaseUrl 규칙
        Assert.Contains("*.daouoffice.com", html, StringComparison.Ordinal);

        Assert.DoesNotContain("daouoffice.com/subdomain", html, StringComparison.Ordinal);
        // §23: 쿠키는 한 store만 쓴다
        Assert.DoesNotContain("쿠키 스토어의 쿠키를 한 헤더로 합친다", html, StringComparison.Ordinal);

        // 표지: 날짜 pill이 옛 문서 날짜가 아니다. 새 날짜 값은 단언하지 않는다(T3.5가 바꾼다).
        // 문서 전체가 아니라 표지 meta 구간만 본다(버전 이력 절이 옛 날짜를 쓸 수 있다).
        const string metaStart = "<div class=\"meta\">";
        var start = html.IndexOf(metaStart, StringComparison.Ordinal);
        Assert.True(start >= 0, "표지 <div class=\"meta\"> 블록을 찾지 못했다.");
        var end = html.IndexOf("</div>", start, StringComparison.Ordinal);
        Assert.True(end > start, "표지 meta 블록의 닫는 </div>를 찾지 못했다.");
        var meta = html.Substring(start, end - start);
        Assert.DoesNotContain("2026-09-18", meta, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_D1_StartupOrderAndStructureCorrected()
    {
        var readme = File.ReadAllText(ReadmePath);

        // 프로젝트 구조
        Assert.Contains("GlobalUsings.cs", readme, StringComparison.Ordinal);
        Assert.Contains("tests/", readme, StringComparison.Ordinal);
        Assert.Contains("docs/", readme, StringComparison.Ordinal);

        Assert.DoesNotContain("HTTP/CalDAV", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("이번 달을 보고 있었다면 새 달로 이동한 뒤 한 번 동기화합니다", readme, StringComparison.Ordinal);

        // 최초 실행: 확장 폴더는 EXE를 실행해야 추출되므로 EXE 실행이 확장 로드보다 먼저 나온다.
        var start = readme.IndexOf("## 최초 실행", StringComparison.Ordinal);
        Assert.True(start >= 0, "README에서 '## 최초 실행' 절을 찾지 못했다.");
        var end = readme.IndexOf("### 동작 확인", start, StringComparison.Ordinal);
        Assert.True(end > start, "README에서 '## 최초 실행' 뒤의 '### 동작 확인'을 찾지 못했다.");
        var firstRun = readme.Substring(start, end - start);

        var exeIndex = firstRun.IndexOf(".exe", StringComparison.Ordinal);
        var extensionsIndex = firstRun.IndexOf("chrome://extensions", StringComparison.Ordinal);
        Assert.True(exeIndex >= 0, "'## 최초 실행' 절에 .exe가 없다.");
        Assert.True(extensionsIndex >= 0, "'## 최초 실행' 절에 chrome://extensions가 없다.");
        Assert.True(exeIndex < extensionsIndex, "'## 최초 실행' 절에서 EXE 실행이 chrome://extensions보다 먼저 나와야 한다.");
    }

    [Fact]
    public void TechnicalDoc_HasRequirementsSection()
    {
        var html = File.ReadAllText(TechnicalDocPath);

        Assert.Contains("id=\"requirements\"", html, StringComparison.Ordinal);
        Assert.Contains("<h2>25. 요구사항과 비목표</h2>", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"#requirements\">25. 요구사항·비목표</a>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnicalDoc_HasBridgeSchemaSection()
    {
        var html = File.ReadAllText(TechnicalDocPath);

        Assert.Contains("id=\"bridge-schema\"", html, StringComparison.Ordinal);
        Assert.Contains("<h2>26. 브리지 메시지 스키마</h2>", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"#bridge-schema\">26. 브리지 메시지 스키마</a>", html, StringComparison.Ordinal);

        var section = GetSectionHtml(html, "bridge-schema");
        Assert.Contains("protocolVersion", section, StringComparison.Ordinal);
        Assert.Contains("extensionVersion", section, StringComparison.Ordinal);
        Assert.Contains("lastError", section, StringComparison.Ordinal);
        Assert.Contains("cookieSource", section, StringComparison.Ordinal);
        Assert.Contains("noFetchReason", section, StringComparison.Ordinal);
        Assert.Contains("lease_active", section, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnicalDoc_HasSequenceSection()
    {
        var html = File.ReadAllText(TechnicalDocPath);

        Assert.Contains("id=\"sequence\"", html, StringComparison.Ordinal);
        Assert.Contains("<h2>27. 동기화 시퀀스</h2>", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"#sequence\">27. 동기화 시퀀스</a>", html, StringComparison.Ordinal);

        var section = GetSectionHtml(html, "sequence");
        Assert.Contains("postResult", section, StringComparison.Ordinal);
        Assert.Contains("recordHostFailure", section, StringComparison.Ordinal);
        Assert.Contains("DiscardIfRangeChanged", section, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnicalDoc_HasStateMachineSection()
    {
        var html = File.ReadAllText(TechnicalDocPath);

        Assert.Contains("id=\"state-machine\"", html, StringComparison.Ordinal);
        Assert.Contains("<h2>28. 상태 머신 전이표</h2>", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"#state-machine\">28. 상태 전이표</a>", html, StringComparison.Ordinal);

        var section = GetSectionHtml(html, "state-machine");
        Assert.Contains("Chrome 브리지 연결됨 · 동기화 대기", section, StringComparison.Ordinal);
        Assert.Contains("Chrome 확장 연결 대기", section, StringComparison.Ordinal);
        Assert.Contains("Chrome 확장 새로고침 필요 (", section, StringComparison.Ordinal);
        Assert.Contains("7.0.0 이하", section, StringComparison.Ordinal);
        Assert.Contains("EvaluateHealth", section, StringComparison.Ordinal);
        Assert.Contains("ConfigurationInvalid", section, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnicalDoc_HasExtensionTriggersSection()
    {
        var html = File.ReadAllText(TechnicalDocPath);

        Assert.Contains("id=\"extension-triggers\"", html, StringComparison.Ordinal);
        Assert.Contains("<h2>29. 확장 서비스 워커 트리거</h2>", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"#extension-triggers\">29. 확장 트리거</a>", html, StringComparison.Ordinal);

        var section = GetSectionHtml(html, "extension-triggers");
        Assert.Contains("daou-calendar-overlay-sync", section, StringComparison.Ordinal);
        Assert.Contains("daouBridgeBackoff", section, StringComparison.Ordinal);
        Assert.Contains("cookies.onChanged", section, StringComparison.Ordinal);
        Assert.Contains("ensureAlarm", section, StringComparison.Ordinal);
        Assert.Contains("inFlight", section, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnicalDoc_HasThreadingSection()
    {
        var html = File.ReadAllText(TechnicalDocPath);

        Assert.Contains("id=\"threading\"", html, StringComparison.Ordinal);
        Assert.Contains("<h2>30. 스레딩 모델</h2>", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"#threading\">30. 스레딩 모델</a>", html, StringComparison.Ordinal);

        var section = GetSectionHtml(html, "threading");
        Assert.Contains("Dispatcher.InvokeAsync", section, StringComparison.Ordinal);
        Assert.Contains("_healthTimer", section, StringComparison.Ordinal);
        Assert.Contains("DisposeAsync", section, StringComparison.Ordinal);
        Assert.Contains("AtomicJsonFileWriter", section, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnicalDoc_HasSettingsSchemaSection()
    {
        var html = File.ReadAllText(TechnicalDocPath);

        Assert.Contains("id=\"settings-schema\"", html, StringComparison.Ordinal);
        Assert.Contains("<h2>31. settings.json 스키마와 검증</h2>", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"#settings-schema\">31. settings.json 스키마</a>", html, StringComparison.Ordinal);

        // settings.json 키 = AppSettings의 public 인스턴스 속성 중 [JsonIgnore]가 아닌 것.
        // 모델에 키가 추가·변경되면 §31 키 표도 바뀌어야 하므로 개수가 아니라 이름 전부를 확인한다.
        var section = GetSectionHtml(html, "settings-schema");
        var keys = typeof(AppSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetCustomAttribute<JsonIgnoreAttribute>() is null)
            .Select(property => property.Name)
            .ToList();

        Assert.NotEmpty(keys);
        foreach (var key in keys)
            Assert.Contains($"<code>{key}</code>", section, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnicalDoc_HasApiContractSection()
    {
        var html = File.ReadAllText(TechnicalDocPath);

        Assert.Contains("id=\"api-contract\"", html, StringComparison.Ordinal);
        Assert.Contains("<h2>32. Daou API 응답 계약</h2>", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"#api-contract\">32. Daou API 계약</a>", html, StringComparison.Ordinal);

        var section = GetSectionHtml(html, "api-contract");
        Assert.Contains("/gw/api/calendar/event", section, StringComparison.Ordinal);
        Assert.Contains("includingAttendees=true", section, StringComparison.Ordinal);
        Assert.Contains("UNTIL=20261231", section, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnicalDoc_HasDiagnosticsSection()
    {
        var html = File.ReadAllText(TechnicalDocPath);

        Assert.Contains("id=\"diagnostics\"", html, StringComparison.Ordinal);
        Assert.Contains("<h2>33. 로그·진단 절차</h2>", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"#diagnostics\">33. 로그·진단</a>", html, StringComparison.Ordinal);

        var section = GetSectionHtml(html, "diagnostics");
        Assert.Contains("com.daou.calendar_overlay", section, StringComparison.Ordinal);
        Assert.Contains("gkchgbpcbljkgabjcgjelacfkphcmhmi", section, StringComparison.Ordinal);
        Assert.Contains("daou-calendar-overlay-sync", section, StringComparison.Ordinal);
        Assert.Contains("host-", section, StringComparison.Ordinal);
        Assert.Contains("overlay-", section, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnicalDoc_HasTestingSection()
    {
        var html = File.ReadAllText(TechnicalDocPath);

        Assert.Contains("id=\"testing\"", html, StringComparison.Ordinal);
        Assert.Contains("<h2>34. 테스트 체크리스트와 검증 기록</h2>", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"#testing\">34. 테스트·검증 기록</a>", html, StringComparison.Ordinal);

        // 수동 확인 항목은 모두 미확인이다. 이 절은 어떤 항목도 확인을 마친 것으로 적지 않는다.
        var section = GetSectionHtml(html, "testing");
        Assert.Contains("미확인", section, StringComparison.Ordinal);
        Assert.DoesNotContain("검증 완료", section, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnicalDoc_HasDataRetentionSection()
    {
        var html = File.ReadAllText(TechnicalDocPath);

        Assert.Contains("id=\"data-retention\"", html, StringComparison.Ordinal);
        Assert.Contains("<h2>35. 데이터 보존·삭제 규칙</h2>", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"#data-retention\">35. 데이터 보존·삭제</a>", html, StringComparison.Ordinal);

        // 캐시·설정·로그 위치와 삭제의 정본(§35)이 다루는 대상과 제거 진입점, 비영속 흔적, 제거되지 않는 추출 폴더.
        var section = GetSectionHtml(html, "data-retention");
        Assert.Contains("calendar-cache.json", section, StringComparison.Ordinal);
        Assert.Contains("settings.json", section, StringComparison.Ordinal);
        Assert.Contains("--uninstall", section, StringComparison.Ordinal);
        Assert.Contains("DaouCalendarOverlay.NativeBridge.v7", section, StringComparison.Ordinal);
        Assert.Contains(@"%TEMP%\.net\DaouCalendarOverlay", section, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnicalDoc_LegacySections_HaveP3Supplements()
    {
        var html = File.ReadAllText(TechnicalDocPath);

        // §21~§24는 새 절로 만들지 않고 기존 절 안에 소절로 보강한다(각 소절이 자기 절 구간 안에 있어야 한다).
        var perf = GetSectionHtml(html, "perf");
        Assert.Contains("<h3>폴링 부하</h3>", perf, StringComparison.Ordinal);
        Assert.Contains("<h3>그리드 재구성 비용</h3>", perf, StringComparison.Ordinal);

        var lifecycle = GetSectionHtml(html, "lifecycle");
        Assert.Contains("<h3>6.x에서 7.x로 업그레이드</h3>", lifecycle, StringComparison.Ordinal);
        Assert.Contains("<h3>다운그레이드</h3>", lifecycle, StringComparison.Ordinal);

        var browserMatrix = GetSectionHtml(html, "browser-matrix");
        Assert.Contains("<h3>추가 전제 조건</h3>", browserMatrix, StringComparison.Ordinal);

        var release = GetSectionHtml(html, "release");
        Assert.Contains("<h3>24.5 확장 버전 상향 체크리스트</h3>", release, StringComparison.Ordinal);
        Assert.Contains("<h3>24.6 태그와 검증 기록</h3>", release, StringComparison.Ordinal);
        Assert.Contains("<h3>24.7 .NET 8 지원 종료 대비</h3>", release, StringComparison.Ordinal);

        Assert.DoesNotContain("v7.0.0", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnicalDoc_HasThreatModelAndVersioningSections()
    {
        var html = File.ReadAllText(TechnicalDocPath);

        Assert.Contains("id=\"threat-model\"", html, StringComparison.Ordinal);
        Assert.Contains("<h2>36. 위협 모델</h2>", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"#threat-model\">36. 위협 모델</a>", html, StringComparison.Ordinal);
        Assert.Contains("id=\"versioning\"", html, StringComparison.Ordinal);
        Assert.Contains("<h2>37. 버전 이력과 버저닝 정책</h2>", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"#versioning\">37. 버전 이력·정책</a>", html, StringComparison.Ordinal);
        Assert.True(
            html.IndexOf("id=\"threat-model\"", StringComparison.Ordinal) < html.IndexOf("id=\"versioning\"", StringComparison.Ordinal),
            "§36(threat-model)이 §37(versioning)보다 앞에 있어야 한다.");

        // 보안 보호 장치의 정본(§36): 파이프 양쪽 CurrentUserOnly와 서버 PID 확인(fail-closed).
        var threatModel = GetSectionHtml(html, "threat-model");
        Assert.Contains("CurrentUserOnly", threatModel, StringComparison.Ordinal);
        Assert.Contains("GetNamedPipeServerProcessId", threatModel, StringComparison.Ordinal);

        // 버저닝 정책(§37): 파이프 이름은 EXE·확장 버전이 아니라 프로토콜 호환 단위로 바뀐다.
        var versioning = GetSectionHtml(html, "versioning");
        Assert.Contains("DaouCalendarOverlay.NativeBridge.v7", versioning, StringComparison.Ordinal);

        Assert.DoesNotContain("v7.0.0", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnicalDoc_HasBacklogAndGlossarySections()
    {
        var html = File.ReadAllText(TechnicalDocPath);

        Assert.Contains("id=\"backlog\"", html, StringComparison.Ordinal);
        Assert.Contains("<h2>38. 백로그</h2>", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"#backlog\">38. 백로그</a>", html, StringComparison.Ordinal);
        Assert.Contains("id=\"glossary\"", html, StringComparison.Ordinal);
        Assert.Contains("<h2>39. 용어집</h2>", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"#glossary\">39. 용어집</a>", html, StringComparison.Ordinal);

        var versioningIndex = html.IndexOf("id=\"versioning\"", StringComparison.Ordinal);
        var backlogIndex = html.IndexOf("id=\"backlog\"", StringComparison.Ordinal);
        var glossaryIndex = html.IndexOf("id=\"glossary\"", StringComparison.Ordinal);
        Assert.True(versioningIndex < backlogIndex, "§37(versioning)이 §38(backlog)보다 앞에 있어야 한다.");
        Assert.True(backlogIndex < glossaryIndex, "§38(backlog)이 §39(glossary)보다 앞에 있어야 한다.");

        // 백로그의 정본(§38): 결정으로 보류된 Q3-b·Q6·Q8 항목.
        var backlog = GetSectionHtml(html, "backlog");
        Assert.Contains("connectNative", backlog, StringComparison.Ordinal);
        Assert.Contains("DPAPI", backlog, StringComparison.Ordinal);
        Assert.Contains("update_url", backlog, StringComparison.Ordinal);

        // 용어집(§39): 브리지 lease 사유 코드와 확장 세션 쿠키 캐시 키.
        var glossary = GetSectionHtml(html, "glossary");
        Assert.Contains("lease_active", glossary, StringComparison.Ordinal);
        Assert.Contains("daouSessionCookieCache", glossary, StringComparison.Ordinal);

        Assert.DoesNotContain("v7.0.0", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnicalDoc_SectionReferencesResolveToExistingHeadings()
    {
        var html = File.ReadAllText(TechnicalDocPath);

        // 코드 발췌와 스타일 블록 안의 문자는 절 참조가 아니므로 빼고 본다.
        var prose = Regex.Replace(html, "<style>[\\s\\S]*?</style>", "");
        prose = Regex.Replace(prose, "<pre>[\\s\\S]*?</pre>", "");

        var h2Numbers = new HashSet<int>(
            Regex.Matches(html, @"<h2>(\d+)\.")
                .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)));
        var h3Numbers = new HashSet<string>(
            Regex.Matches(html, @"<h3[^>]*>(\d+\.\d+)[ <·]").Select(match => match.Groups[1].Value),
            StringComparer.Ordinal);

        var unresolved = new List<string>();
        foreach (Match reference in Regex.Matches(prose, @"§(\d+)(?:\.(\d+))?"))
        {
            var section = int.Parse(reference.Groups[1].Value, CultureInfo.InvariantCulture);
            if (!h2Numbers.Contains(section))
            {
                unresolved.Add(reference.Value);
                continue;
            }

            if (reference.Groups[2].Success &&
                !h3Numbers.Contains($"{reference.Groups[1].Value}.{reference.Groups[2].Value}"))
                unresolved.Add(reference.Value);
        }

        Assert.True(unresolved.Count == 0, "해석되지 않는 절 참조: " + string.Join(", ", unresolved.Distinct()));
    }

    [Theory]
    [InlineData("sync", "(§28)")]
    [InlineData("sync", "(§26)")]
    [InlineData("sync", "(§29)")]
    [InlineData("native", "(§26)")]
    [InlineData("chrome", "(§29)")]
    public void TechnicalDoc_BehaviorSummariesReferenceCanonicalSections(string sectionId, string reference)
    {
        var html = File.ReadAllText(TechnicalDocPath);

        // 동작 계약의 정본(§26 브리지 스키마, §28 상태 전이, §29 확장 트리거)을 요약하는 기존 절은 정본 절을 가리킨다.
        var match = Regex.Match(html, $"<section id=\"{sectionId}\"[^>]*>([\\s\\S]*?)</section>");
        Assert.True(match.Success, $"기술 문서에서 <section id=\"{sectionId}\">를 찾지 못했다.");
        Assert.Contains(reference, match.Groups[1].Value, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("storage", "(§31)")]
    [InlineData("storage", "(§35)")]
    [InlineData("lifecycle", "(§35)")]
    [InlineData("security", "(§36)")]
    [InlineData("limitations", "(§38)")]
    public void TechnicalDoc_DataSecuritySummariesReferenceCanonicalSections(string sectionId, string reference)
    {
        var html = File.ReadAllText(TechnicalDocPath);

        // 데이터·보안·운영 계약의 정본(§31 settings.json 스키마, §35 데이터 보존·삭제, §36 위협 모델, §38 백로그)을
        // 요약하는 기존 절은 정본 절을 가리킨다.
        var match = Regex.Match(html, $"<section id=\"{sectionId}\"[^>]*>([\\s\\S]*?)</section>");
        Assert.True(match.Success, $"기술 문서에서 <section id=\"{sectionId}\">를 찾지 못했다.");
        Assert.Contains(reference, match.Groups[1].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnicalDoc_CoverPillAndFooterShareDocumentDate()
    {
        var html = File.ReadAllText(TechnicalDocPath);

        var pill = Regex.Match(html, "<span class=\"pill\">문서 기준일 (\\d{4}-\\d{2}-\\d{2})</span>");
        Assert.True(pill.Success, "표지 meta의 '문서 기준일' pill을 찾지 못했다.");

        var footer = Regex.Match(html, "<div class=\"footer\">([^<]*)</div>");
        Assert.True(footer.Success, "footer를 찾지 못했다.");

        var footerDate = Regex.Match(footer.Groups[1].Value, "문서 기준일 (\\d{4}-\\d{2}-\\d{2})");
        Assert.True(footerDate.Success, "footer에서 '문서 기준일'을 찾지 못했다.");

        Assert.Equal(pill.Groups[1].Value, footerDate.Groups[1].Value);
        Assert.True(
            DateTime.TryParseExact(
                pill.Groups[1].Value,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _),
            $"문서 기준일 '{pill.Groups[1].Value}'가 yyyy-MM-dd 형식이 아니다.");

        Assert.Contains($"v{AppVersion.Display}", footer.Groups[1].Value, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("요구사항과 비목표")]
    [InlineData("상태 머신 전이표")]
    [InlineData("로그·진단 절차")]
    [InlineData("위협 모델")]
    [InlineData("백로그")]
    public void Readme_ReferencesTechnicalDocSectionsByTitle(string title)
    {
        var readme = File.ReadAllText(ReadmePath);
        var html = File.ReadAllText(TechnicalDocPath);

        Assert.Contains($"\"{title}\" 절", readme, StringComparison.Ordinal);
        Assert.Contains("docs/DaouCalendarOverlay_Technical_Documentation.html", readme, StringComparison.Ordinal);
        Assert.Contains($". {title}</h2>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkOrder_EndsWithResultSummarySection()
    {
        var lines = File.ReadAllText(WorkOrderPath)
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .ToList();

        var instructionsIndex = lines.FindIndex(line => line == "## 6. Opus 실행 지침");
        var summaryIndex = lines.FindIndex(line => line == "## 결과 요약");

        Assert.True(instructionsIndex >= 0, "'## 6. Opus 실행 지침' 줄을 찾지 못했다.");
        Assert.True(summaryIndex >= 0, "'## 결과 요약' 줄을 찾지 못했다.");
        Assert.True(instructionsIndex < summaryIndex, "'결과 요약'이 '6. Opus 실행 지침' 앞에 있다.");

        var lastHeadingLine = lines.LastOrDefault(line => line.StartsWith("## ", StringComparison.Ordinal));
        Assert.Equal("## 결과 요약", lastHeadingLine);

        var afterSummary = lines.Skip(summaryIndex + 1).ToList();
        Assert.Contains("### 1. 작업 상태", afterSummary);
        Assert.Contains("### 2. 측정치", afterSummary);
        Assert.Contains("### 4. 남은 결정", afterSummary);
    }

    /// <summary><c>&lt;section id="{id}"&gt;</c>부터 그 뒤 첫 <c>&lt;/section&gt;</c>까지(포함)를 돌려준다.</summary>
    private static string GetSectionHtml(string html, string id)
    {
        var marker = $"<section id=\"{id}\">";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"기술 문서에서 {marker}를 찾지 못했다.");

        const string closing = "</section>";
        var end = html.IndexOf(closing, start, StringComparison.Ordinal);
        Assert.True(end > start, $"기술 문서에서 {marker}의 닫는 {closing}을 찾지 못했다.");

        return html.Substring(start, end + closing.Length - start);
    }

    private static List<int> H2Numbers(string html) =>
        H2NumberPattern.Matches(html)
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToList();
}
