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
        Assert.Contains("public const int ProtocolVersion = 2;", html, StringComparison.Ordinal);
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
        Assert.Contains("네트워크 오류: DaouOffice에 연결하지 못했습니다", html, StringComparison.Ordinal);
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
        Assert.Contains("FETCH_TIMEOUT_MS", html, StringComparison.Ordinal);
        Assert.Contains("MAX_BODY_CHARS", html, StringComparison.Ordinal);
        Assert.Contains("FetchLeaseSeconds", html, StringComparison.Ordinal);
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
        Assert.Contains("outcome", section, StringComparison.Ordinal);
        Assert.Contains("bodyLength", section, StringComparison.Ordinal);
        Assert.Contains("protocol_mismatch", section, StringComparison.Ordinal);
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
        Assert.Contains("fetchCalendar", section, StringComparison.Ordinal);
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
        Assert.Contains("ROUTE-0004", section, StringComparison.Ordinal);
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
        Assert.Contains("[FETCH]", section, StringComparison.Ordinal);
        Assert.Contains("verdict=", section, StringComparison.Ordinal);
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
        Assert.Contains("DaouCalendarOverlay.NativeBridge.v8", section, StringComparison.Ordinal);
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
        Assert.Contains("DaouCalendarOverlay.NativeBridge.v8", versioning, StringComparison.Ordinal);

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

    [Fact]
    public void TechnicalDoc_RetentionThreatAndGlossaryReflectV730()
    {
        var html = File.ReadAllText(TechnicalDocPath);

        // 7.3.0(세션 유지·만료 갱신) 사실을 절마다 고정한다. 정본 서술은 각 절에 있고 여기서는 핵심 문자열만 본다.
        string Section(string id)
        {
            var match = Regex.Match(html, $"<section id=\"{id}\"[^>]*>([\\s\\S]*?)</section>");
            Assert.True(match.Success, $"기술 문서에서 <section id=\"{id}\">를 찾지 못했다.");
            return match.Groups[1].Value;
        }

        // §34: 새 자동 검사와 수동 확인 R-A~R-G
        var testing = Section("testing");
        Assert.Contains("worker-mock-test.js", testing, StringComparison.Ordinal);
        foreach (var item in new[] { "R-A", "R-B", "R-C", "R-D", "R-E", "R-F", "R-G" })
            Assert.Contains(item, testing, StringComparison.Ordinal);

        // §35: 세션 스냅샷과 갱신 기록은 메모리 저장소다
        var retention = Section("data-retention");
        Assert.Contains("daouSessionSnapshot", retention, StringComparison.Ordinal);
        Assert.Contains("storage.session", retention, StringComparison.Ordinal);

        // §36: 위협 모델
        var threat = Section("threat-model");
        Assert.Contains("storage.session", threat, StringComparison.Ordinal);
        Assert.Contains("CurrentUserOnly", threat, StringComparison.Ordinal);
        Assert.Contains("GetNamedPipeServerProcessId", threat, StringComparison.Ordinal);

        // §37: 버전 이력과 프로토콜 유지
        var versioning = Section("versioning");
        Assert.Contains("7.3.1", versioning, StringComparison.Ordinal);
        Assert.Contains("DaouCalendarOverlay.NativeBridge.v8", versioning, StringComparison.Ordinal);

        // §39: 용어
        var glossary = Section("glossary");
        Assert.Contains("daouSessionSnapshot", glossary, StringComparison.Ordinal);
        Assert.Contains("ROUTE-0006", glossary, StringComparison.Ordinal);
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

    /// <summary>§1 요약(문서 표지 뒤 첫 section)이 7.2.0 구조(확장이 직접 조회, 앱은 판정·파싱·표시)를 설명하는지 고정한다.</summary>
    [Fact]
    public void TechnicalDoc_SummaryDescribesDirectFetch()
    {
        var html = File.ReadAllText(TechnicalDocPath);

        var start = html.IndexOf("<section", StringComparison.Ordinal);
        Assert.True(start >= 0, "기술 문서에서 첫 <section을 찾지 못했다.");
        const string closing = "</section>";
        var end = html.IndexOf(closing, start, StringComparison.Ordinal);
        Assert.True(end > start, "기술 문서의 첫 section 닫는 태그를 찾지 못했다.");
        var summary = html.Substring(start, end + closing.Length - start);

        Assert.Contains("<h2>1. ", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("API 조회·파싱·표시는 Windows 오버레이 앱", summary, StringComparison.Ordinal);
        Assert.Contains("확장", summary, StringComparison.Ordinal);
        Assert.Contains("직접 조회", summary, StringComparison.Ordinal);
    }

    /// <summary>7.3.0: §1 요약이 세션 유지(storage.session, cookies 권한 복귀)를 말하고, §6이 새 worker 발췌를 싣고 옛 "cookies 권한 제거" 서술을 남기지 않는지 고정한다.</summary>
    [Fact]
    public void TechnicalDoc_SummaryAndWorkerSectionDescribeSessionKeepAlive()
    {
        var html = File.ReadAllText(TechnicalDocPath);

        var start = html.IndexOf("<section", StringComparison.Ordinal);
        Assert.True(start >= 0, "기술 문서에서 첫 <section을 찾지 못했다.");
        const string closing = "</section>";
        var end = html.IndexOf(closing, start, StringComparison.Ordinal);
        Assert.True(end > start, "기술 문서의 첫 section 닫는 태그를 찾지 못했다.");
        var summary = html.Substring(start, end + closing.Length - start);

        Assert.Contains("storage.session", summary, StringComparison.Ordinal);
        Assert.Contains("cookies", summary, StringComparison.Ordinal);

        Assert.Contains("function fetchCalendar(config, timeoutMs = FETCH_TIMEOUT_MS)", html, StringComparison.Ordinal);
        Assert.Contains("async function onCookieChanged(changeInfo)", html, StringComparison.Ordinal);
        Assert.DoesNotContain("cookies 권한은 7.2.0에서 제거했다", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// 7.3.1: §16.2의 Installer 발췌가 v731 worker를 <c>Files</c>에, v720·v730 worker를 obsolete 목록에 싣고,
    /// §10이 <c>cookies.onChanged</c> 트리거와 스냅샷 저장소 키를 말하며, §13·§15·§17이 세션 유지 사실을 반영하는지 고정한다.
    /// 발췌는 HTML 이스케이프를 풀어 실제 소스 형태로 비교한다.
    /// </summary>
    [Fact]
    public void TechnicalDoc_InstallerExcerptListsV731Worker()
    {
        var html = File.ReadAllText(TechnicalDocPath);

        var build = System.Net.WebUtility.HtmlDecode(GetSectionHtml(html, "build"));
        var filesStart = build.IndexOf("private static readonly (string ResourceName, string FileName)[] Files", StringComparison.Ordinal);
        Assert.True(filesStart >= 0, "§16.2 발췌에서 Files 배열을 찾지 못했다.");
        var obsoleteStart = build.IndexOf("foreach (var obsolete in new[]", filesStart, StringComparison.Ordinal);
        Assert.True(obsoleteStart > filesStart, "§16.2 발췌에서 obsolete 목록을 찾지 못했다.");
        var filesExcerpt = build.Substring(filesStart, obsoleteStart - filesStart);
        var obsoleteEnd = build.IndexOf("})", obsoleteStart, StringComparison.Ordinal);
        Assert.True(obsoleteEnd > obsoleteStart, "§16.2 발췌에서 obsolete 목록의 끝을 찾지 못했다.");
        var obsoleteExcerpt = build.Substring(obsoleteStart, obsoleteEnd - obsoleteStart);

        Assert.Contains(
            "(\"DaouCalendarOverlay.ChromeExtension.service-worker-v731.js\", \"service-worker-v731.js\")",
            filesExcerpt,
            StringComparison.Ordinal);
        Assert.DoesNotContain("service-worker-v730.js", filesExcerpt, StringComparison.Ordinal);
        Assert.DoesNotContain("service-worker-v720.js", filesExcerpt, StringComparison.Ordinal);
        Assert.Contains("\"service-worker-v720.js\"", obsoleteExcerpt, StringComparison.Ordinal);
        Assert.Contains("\"service-worker-v730.js\"", obsoleteExcerpt, StringComparison.Ordinal);
        Assert.Contains("public const string ExpectedExtensionVersion = \"7.3.1\";", build, StringComparison.Ordinal);

        var sync = GetSectionHtml(html, "sync");
        Assert.Contains("cookies.onChanged", sync, StringComparison.Ordinal);
        Assert.Contains("daouSessionSnapshot", sync, StringComparison.Ordinal);
        Assert.Contains("daouRefreshLast", sync, StringComparison.Ordinal);
        Assert.Contains("daouRefreshTabWait", sync, StringComparison.Ordinal);
        Assert.Contains("daouSessionCookieCache", sync, StringComparison.Ordinal);

        // §10.4: 7.3.0 판정 문구(세션 갱신 대기)와 기대 버전이 현재 소스와 같아야 한다.
        Assert.Contains(BridgeResultClassifier.SessionRefreshPendingMessage, sync, StringComparison.Ordinal);
        Assert.Contains("session_refresh_pending", sync, StringComparison.Ordinal);
        Assert.Contains("인증 문구 4종", sync, StringComparison.Ordinal);
        Assert.DoesNotContain("인증 문구 3종", sync, StringComparison.Ordinal);
        Assert.DoesNotContain("기대 버전은 7.2.0", sync, StringComparison.Ordinal);
        Assert.DoesNotContain("기대 버전은 7.3.0", sync, StringComparison.Ordinal);
        Assert.Contains("기대 버전은 7.3.1", sync, StringComparison.Ordinal);
        Assert.DoesNotContain("7.2.0 worker의", sync, StringComparison.Ordinal);
        Assert.Contains("SYNC_BUDGET_MS = 40000", sync, StringComparison.Ordinal);
        // §10.2 버전 불일치 예시는 현재 기대 버전(ExpectedExtensionVersion)을 쓴다.
        Assert.Contains("Chrome 확장 새로고침 필요 (7.1.0 → " + ChromeExtensionInstaller.ExpectedExtensionVersion + ")", sync, StringComparison.Ordinal);
        Assert.DoesNotContain("(7.1.0 → 7.2.0)", sync, StringComparison.Ordinal);
        // §6.1은 7.3.0에서 권한 표가 되어 쿠키 변경 트리거를 뺀 이유를 더 싣지 않는다. 이유는 §29에 있다.
        Assert.DoesNotContain("이유는 §6.1", sync, StringComparison.Ordinal);
        Assert.DoesNotContain("§6.1에 있다", sync, StringComparison.Ordinal);

        var storage = GetSectionHtml(html, "storage");
        Assert.Contains("daouSessionSnapshot", storage, StringComparison.Ordinal);
        Assert.Contains("DaouCalendarOverlay-7.3.1.exe", storage, StringComparison.Ordinal);
        Assert.DoesNotContain("DaouCalendarOverlay-7.3.0.exe", storage, StringComparison.Ordinal);
        Assert.DoesNotContain("DaouCalendarOverlay-7.2.0.exe", storage, StringComparison.Ordinal);

        var security = GetSectionHtml(html, "security");
        Assert.Contains("(§36)", security, StringComparison.Ordinal);
        Assert.Contains("overwrite", security, StringComparison.Ordinal);
        Assert.DoesNotContain("확장에 <code class=\"inline\">cookies</code> 권한이 없고", security, StringComparison.Ordinal);

        var files = GetSectionHtml(html, "files");
        Assert.Contains("service-worker-v731.js", files, StringComparison.Ordinal);
        Assert.DoesNotContain("service-worker-v730.js", files, StringComparison.Ordinal);
        Assert.DoesNotContain("service-worker-v720.js", files, StringComparison.Ordinal);
        Assert.Contains("worker-mock-test.js", files, StringComparison.Ordinal);
        Assert.Contains("check-html.js", files, StringComparison.Ordinal);
    }

    /// <summary>
    /// 7.3.0: §20 상수표가 worker 시간 상수(예산·갱신·쿨다운·탭 양보·복원)를 싣고, §22·§23이 7.3.0 기준으로 서술하며,
    /// 7.3.1: §24.5 체크리스트의 현재값이 v731 worker인지 고정한다. 프로토콜·lease 상수는 바뀌지 않았다는 서술도 확인한다.
    /// </summary>
    [Fact]
    public void TechnicalDoc_ConstantsAndReleaseChecklistReflectV731()
    {
        var html = File.ReadAllText(TechnicalDocPath);

        Assert.Contains("SYNC_BUDGET_MS", html, StringComparison.Ordinal);
        Assert.Contains("REFRESH_TIMEOUT_MS", html, StringComparison.Ordinal);

        var appendix = GetSectionHtml(html, "appendix");
        foreach (var constant in new[]
                 {
                     "SYNC_BUDGET_MS", "REFRESH_TIMEOUT_MS", "REFRESH_SETTLE_MS", "RETRY_MIN_MS",
                     "REFRESH_COOLDOWN_MS", "REFRESH_REJECTED_COOLDOWN_MS", "TAB_IDLE_OVERRIDE_MS",
                     "RESTORE_DELAY_MS", "RESNAPSHOT_DELAY_MS",
                 })
        {
            Assert.Contains(constant, appendix, StringComparison.Ordinal);
        }

        Assert.Contains("FetchLeaseSeconds", appendix, StringComparison.Ordinal);
        Assert.Contains("ResultLeaseSeconds", appendix, StringComparison.Ordinal);
        Assert.Contains("기대 확장 버전</td><td>7.3.1", appendix, StringComparison.Ordinal);
        Assert.DoesNotContain("service-worker-v730.js", appendix, StringComparison.Ordinal);
        Assert.DoesNotContain("service-worker-v720.js", appendix, StringComparison.Ordinal);

        var browserMatrix = GetSectionHtml(html, "browser-matrix");
        Assert.Contains("현재 빌드(v7.3.1)", browserMatrix, StringComparison.Ordinal);
        Assert.DoesNotContain("v7.2.0", browserMatrix, StringComparison.Ordinal);
        Assert.Contains("R-E", browserMatrix, StringComparison.Ordinal);

        var release = GetSectionHtml(html, "release");
        Assert.Contains("service-worker-v731.js", release, StringComparison.Ordinal);
        Assert.Contains("DaouCalendarOverlay.ChromeExtension.service-worker-v731.js", release, StringComparison.Ordinal);
        Assert.Contains("worker-mock-test.js", release, StringComparison.Ordinal);
        Assert.DoesNotContain("-Version 7.2.0", release, StringComparison.Ordinal);
        Assert.DoesNotContain("-Version 7.3.0", release, StringComparison.Ordinal);
    }

    /// <summary>
    /// 7.3.0: 동작 계약의 정본 절(§26 스키마, §27 시퀀스, §28 상태, §29 트리거, §32 API 계약, §33 진단)이 세션 갱신을 서술하는지 고정한다.
    /// refreshState 값 9개와 만료 코드는 리터럴이 아니라 앱 상수(<see cref="BridgeRefreshStates"/>, <see cref="BridgeResultClassifier.ExpiredApiCode"/>)에서 읽는다.
    /// 옛 "cookies 권한 없이" 서술은 문서 어디에도 남지 않아야 한다.
    /// </summary>
    [Fact]
    public void TechnicalDoc_CanonicalSectionsDescribeSessionRefresh()
    {
        var html = File.ReadAllText(TechnicalDocPath);

        var schema = GetSectionHtml(html, "bridge-schema");
        Assert.Contains("refreshState", schema, StringComparison.Ordinal);
        Assert.Contains("refreshStatus", schema, StringComparison.Ordinal);
        var refreshStates = typeof(BridgeRefreshStates)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();
        Assert.Equal(9, refreshStates.Count);
        foreach (var state in refreshStates)
            Assert.Contains($"<code>{state}</code>", schema, StringComparison.Ordinal);
        Assert.Contains("ProtocolVersion", schema, StringComparison.Ordinal);

        var sequence = GetSectionHtml(html, "sequence");
        Assert.Contains("refreshSession", sequence, StringComparison.Ordinal);
        Assert.Contains("restoreBeforeFetch", sequence, StringComparison.Ordinal);
        Assert.Contains(BridgeResultClassifier.ExpiredApiCode, sequence, StringComparison.Ordinal);

        var stateMachine = GetSectionHtml(html, "state-machine");
        Assert.Contains("DaouOffice 세션 갱신을 기다리는 중입니다", stateMachine, StringComparison.Ordinal);

        var triggers = GetSectionHtml(html, "extension-triggers");
        Assert.Contains("cookies.onChanged", triggers, StringComparison.Ordinal);
        Assert.DoesNotContain("chrome.cookies.onChanged</code> 리스너와 5초 debounce", triggers, StringComparison.Ordinal);

        var api = GetSectionHtml(html, "api-contract");
        Assert.Contains("ROUTE-0006", api, StringComparison.Ordinal);
        Assert.Contains("ROUTE-0004", api, StringComparison.Ordinal);
        Assert.Contains("/api/portal/public/auth/refresh/login", api, StringComparison.Ordinal);

        var diagnostics = GetSectionHtml(html, "diagnostics");
        Assert.Contains("[REFRESH]", diagnostics, StringComparison.Ordinal);
        Assert.Contains("refreshState=", diagnostics, StringComparison.Ordinal);
        Assert.Contains("[SESSION]", diagnostics, StringComparison.Ordinal);

        Assert.DoesNotContain("cookies 권한 없이", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// 7.3.0 최종 검토 지적(2026-10-01): 옛 worker 파일 이름 <c>service-worker-v720.js</c>는 obsolete 목록·옛 파일 정리·
    /// 버전 변경 이력 설명 문맥에서만 나올 수 있다. 현재 worker를 가리키는 곳(발췌 설명, 상수표 근거 등)에 남으면 실패한다.
    /// 허용 문맥은 등장 위치 앞 160자·뒤 80자(태그를 벗기고 HTML 이스케이프를 푼 본문)에서 <see cref="V720AllowedContext"/>가 일치하는 경우다.
    /// </summary>
    [Fact]
    public void TechnicalDoc_V720WorkerNameAppearsOnlyInObsoleteOrHistoryContext()
    {
        const string name = "service-worker-v720.js";
        var html = File.ReadAllText(TechnicalDocPath);
        var text = System.Net.WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", string.Empty));

        var offenders = new List<string>();
        var count = 0;
        var index = text.IndexOf(name, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            var from = Math.Max(0, index - 160);
            var to = Math.Min(text.Length, index + name.Length + 80);
            var context = text.Substring(from, to - from);
            var before = text.Substring(Math.Max(0, index - 600), index - Math.Max(0, index - 600));
            if (!V720AllowedContext.IsMatch(context) && !ObsoleteArrayPrefix.IsMatch(before))
            {
                var show = Math.Max(0, index - 60);
                offenders.Add(text.Substring(show, Math.Min(text.Length - show, 140)));
            }
            index = text.IndexOf(name, index + name.Length, StringComparison.Ordinal);
        }

        // 옛 이름이 obsolete 목록(§16.2 발췌)에 있어야 하므로 한 번도 안 나오면 오히려 문서가 잘못된 것이다.
        Assert.True(count > 0, "문서에 obsolete 목록의 service-worker-v720.js가 없다.");
        Assert.True(
            offenders.Count == 0,
            "service-worker-v720.js가 허용 문맥(obsolete 배열·목록, 옛 파일 정리, 직전 worker, 7.3.0 추가, 변경 이력) 밖에 있다: "
            + string.Join(" || ", offenders));
    }

    // §16.2 발췌의 obsolete 배열 안: foreach (var obsolete in new[] { 뒤로 닫는 }가 나오기 전이다.
    private static readonly Regex ObsoleteArrayPrefix = new(
        @"foreach \(var obsolete in new\[\]\s*\{[^}]*$",
        RegexOptions.CultureInvariant);

    private static readonly Regex V720AllowedContext = new(
        @"obsolete|옛 (worker )?파일|직전 worker|7\.3\.0에서 (추가|넣었)|Chrome 확장 7\.2\.0\(service-worker-v720\.js\)",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// §16의 csproj 발췌가 실제 <c>DaouCalendarOverlay.csproj</c>의 Version·AssemblyVersion·FileVersion과
    /// worker EmbeddedResource(Include, LogicalName)와 같은지 고정한다(7.3.0 최종 검토에서 7.2.0 발췌가 남아 있었다).
    /// </summary>
    [Fact]
    public void TechnicalDoc_CsprojExcerptMatchesActualCsproj()
    {
        var html = File.ReadAllText(TechnicalDocPath);
        var build = System.Net.WebUtility.HtmlDecode(GetSectionHtml(html, "build"));
        var csproj = File.ReadAllText(RepoLayout.Path("DaouCalendarOverlay", "DaouCalendarOverlay.csproj"));

        var excerptStart = build.IndexOf("<Project Sdk=", StringComparison.Ordinal);
        Assert.True(excerptStart >= 0, "§16 발췌에서 <Project Sdk=를 찾지 못했다.");
        var excerptEnd = build.IndexOf("</Project>", excerptStart, StringComparison.Ordinal);
        Assert.True(excerptEnd > excerptStart, "§16 발췌에서 </Project>를 찾지 못했다.");
        var excerpt = build.Substring(excerptStart, excerptEnd - excerptStart);

        foreach (var element in new[] { "Version", "AssemblyVersion", "FileVersion", "InformationalVersion" })
        {
            var pattern = new Regex($"<{element}>([^<]*)</{element}>", RegexOptions.CultureInvariant);
            var actual = pattern.Match(csproj);
            var documented = pattern.Match(excerpt);
            Assert.True(actual.Success, $"csproj에서 <{element}>를 찾지 못했다.");
            Assert.True(documented.Success, $"§16 csproj 발췌에서 <{element}>를 찾지 못했다.");
            Assert.Equal(actual.Groups[1].Value, documented.Groups[1].Value);
        }

        var resourcePattern = new Regex(
            "<EmbeddedResource Include=\"([^\"]+)\" LogicalName=\"([^\"]+)\"",
            RegexOptions.CultureInvariant);
        var actualResources = resourcePattern.Matches(csproj).Select(m => (m.Groups[1].Value, m.Groups[2].Value)).ToList();
        var documentedResources = resourcePattern.Matches(excerpt).Select(m => (m.Groups[1].Value, m.Groups[2].Value)).ToList();
        Assert.NotEmpty(actualResources);
        Assert.Equal(actualResources, documentedResources);
    }

    /// <summary>
    /// 7.3.0 최종 검토 지적(2026-10-01): §16의 publish 결과 예시가 "현재 publish\DaouCalendarOverlay-7.2.0.exe"로 남아 있었다.
    /// 문서에 나오는 EXE 파일 이름 예시(DaouCalendarOverlay-x.y.z.exe)는 모두 현재 버전을 가리키므로 csproj의 Version과 같아야 한다.
    /// (옛 버전 EXE 이름을 역사 서술로 적어야 하면 이 테스트에 허용 문맥을 더한다.)
    /// </summary>
    [Fact]
    public void TechnicalDoc_PublishExeExamplesMatchCsprojVersion()
    {
        var html = File.ReadAllText(TechnicalDocPath);
        var csproj = File.ReadAllText(RepoLayout.Path("DaouCalendarOverlay", "DaouCalendarOverlay.csproj"));
        var version = Regex.Match(csproj, "<Version>([^<]*)</Version>", RegexOptions.CultureInvariant);
        Assert.True(version.Success, "csproj에서 <Version>을 찾지 못했다.");

        var examples = Regex.Matches(html, @"DaouCalendarOverlay-(\d+\.\d+\.\d+)\.exe", RegexOptions.CultureInvariant);
        Assert.NotEmpty(examples);
        Assert.All(examples, match => Assert.Equal(version.Groups[1].Value, match.Groups[1].Value));

        var build = GetSectionHtml(html, "build");
        Assert.Contains($@"publish\DaouCalendarOverlay-{version.Groups[1].Value}.exe", build, StringComparison.Ordinal);
    }

    /// <summary>
    /// 7.3.0 최종 검토 지적(2026-10-01): 7.2.0 시절의 "worker는 쿠키 값을 읽지도 넘기지도 않는다"와
    /// "cookies 권한은 7.2.0에서 제거" 서술은 7.3.0의 현재 사실이 아니다. 태그를 벗긴 본문에도 없어야 한다.
    /// 7.3.0 사실(조회 경로는 쿠키를 읽지 않고, cookies 권한으로 메모리 스냅샷에만 값을 둔다)은 §8이 싣는다.
    /// </summary>
    [Fact]
    public void TechnicalDoc_NoStaleV720CookieStatements_AndAuthSectionDescribesSnapshot()
    {
        var html = File.ReadAllText(TechnicalDocPath);
        var text = System.Net.WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", string.Empty));

        foreach (var stale in new[] { "worker는 쿠키 값을 읽지도 넘기지도 않는다", "cookies 권한은 7.2.0에서 제거" })
        {
            Assert.DoesNotContain(stale, html, StringComparison.Ordinal);
            Assert.DoesNotContain(stale, text, StringComparison.Ordinal);
        }

        var auth = System.Net.WebUtility.HtmlDecode(
            Regex.Replace(GetSectionHtml(html, "auth"), "<[^>]+>", string.Empty));
        Assert.Contains("cookies 권한", auth, StringComparison.Ordinal);
        Assert.Contains("storage.session", auth, StringComparison.Ordinal);
        Assert.Contains("앱으로", auth, StringComparison.Ordinal);
        Assert.Contains("폐기 세대", auth, StringComparison.Ordinal);
        Assert.Contains("§6.4", auth, StringComparison.Ordinal);
        Assert.Contains("§36", auth, StringComparison.Ordinal);
    }

    /// <summary>
    /// 7.3.0 최종 검토 지적(2026-10-01): <c>postRefresh</c>는 <c>redirect: "manual"</c>이라 3xx도 opaqueredirect(status 0)다.
    /// refreshStatus 0은 "응답 없음"뿐 아니라 "리디렉션(opaqueredirect)"도 뜻한다고 §26·§27·§32·§33이 적어야 한다.
    /// </summary>
    [Fact]
    public void TechnicalDoc_RefreshStatusZeroMeansNoResponseOrRedirect()
    {
        var html = File.ReadAllText(TechnicalDocPath);

        foreach (var id in new[] { "bridge-schema", "sequence", "api-contract", "diagnostics" })
        {
            var section = System.Net.WebUtility.HtmlDecode(
                Regex.Replace(GetSectionHtml(html, id), "<[^>]+>", string.Empty));
            Assert.Contains("응답 없음 또는 리디렉션", section, StringComparison.Ordinal);
            Assert.Contains("opaqueredirect", section, StringComparison.Ordinal);
        }

        var glossary = System.Net.WebUtility.HtmlDecode(
            Regex.Replace(GetSectionHtml(html, "glossary"), "<[^>]+>", string.Empty));
        Assert.Contains("응답이 없거나 리디렉션(opaqueredirect)이면 0이다", glossary, StringComparison.Ordinal);
    }

    /// <summary>
    /// 7.3.0 2차 검토 지적(2026-10-01): §34.4가 7.2.0 시점의 구성과 총 건수에서 멈춰 있었다.
    /// verification-log.md의 "### 수동 확인 대기" 절마다 "- "로 시작하는 줄을 세어 합계가 §34.4의 총 건수와 같고,
    /// 7.3.0 절이 §34.4 목록에 들어 있으며 §34.5가 7.3.0 절 등록을 계획이 아닌 사실로 적는지 고정한다.
    /// </summary>
    [Fact]
    public void TechnicalDoc_VerificationLogSummaryMatchesActualLog()
    {
        var html = File.ReadAllText(TechnicalDocPath);
        var testing = System.Net.WebUtility.HtmlDecode(
            Regex.Replace(GetSectionHtml(html, "testing"), "<[^>]+>", string.Empty));

        var lines = File.ReadAllLines(RepoLayout.Path("docs", "verification-log.md"));
        var total = 0;
        var inPending = false;
        var pendingSections = 0;
        foreach (var line in lines)
        {
            if (line.StartsWith("## ", StringComparison.Ordinal) || line.StartsWith("### ", StringComparison.Ordinal))
            {
                inPending = line.StartsWith("### 수동 확인 대기", StringComparison.Ordinal);
                if (inPending)
                {
                    pendingSections++;
                }

                continue;
            }

            if (inPending && line.StartsWith("- ", StringComparison.Ordinal))
            {
                total++;
            }
        }

        Assert.True(pendingSections >= 5, "verification-log.md에서 수동 확인 대기 절을 5개 이상 찾지 못했다.");
        Assert.Contains($"총 {total}건이다(계산)", testing, StringComparison.Ordinal);
        Assert.Contains("수동 확인 대기 (7.3.0)", testing, StringComparison.Ordinal);
        Assert.Contains("R-A~R-G 7", testing, StringComparison.Ordinal);
        Assert.DoesNotContain("7.3.0 절을 더해", testing, StringComparison.Ordinal);
        Assert.DoesNotContain("결과를 적을 계획이다", testing, StringComparison.Ordinal);
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
