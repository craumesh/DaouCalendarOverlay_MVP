using System.Globalization;
using System.Text.RegularExpressions;

namespace DaouCalendarOverlay.Tests;

/// <summary>
/// 기술 문서 HTML의 구조 불변식(h2 번호가 1부터 연속, nav 링크 대상 id 존재·nav 링크 수 = h2 수,
/// id 중복 없음, 닫는 태그로 끝남)과 D1 정정 결과 문자열(기술 문서·README)을 고정한다.
/// 절 개수·테스트 개수 리터럴은 단언하지 않는다(뒤 작업이 절을 추가한다).
/// <c>v7.0.0</c> 부재는 <see cref="ChangelogTests"/>가 이미 단언한다. 저장소 파일은 읽기만 한다.
/// </summary>
public sealed class TechnicalDocumentationTests
{
    private static readonly string TechnicalDocPath =
        RepoLayout.Path("docs", "DaouCalendarOverlay_Technical_Documentation.html");

    private static readonly string ReadmePath = RepoLayout.Path("README.md");

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

    private static List<int> H2Numbers(string html) =>
        H2NumberPattern.Matches(html)
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToList();
}
