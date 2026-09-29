namespace DaouCalendarOverlay.Tests;

/// <summary>
/// 7.2.0부터 쿠키는 Chrome 밖으로 나가지 않는다. 앱이 쿠키를 받거나 DaouOffice에 직접 요청하던 흔적
/// (쿠키 필드, HTTP 클라이언트, 브라우저 식별 문자열, Cookie 헤더 리터럴)이 앱 소스에 다시 들어오지 않도록 막는다.
/// 대상은 Services, Models 아래의 *.cs와 App.xaml.cs 원문이다(주석 포함).
/// </summary>
public sealed class CookieRemovalSourceTests
{
    private static readonly string[] ForbiddenTokens =
    {
        "CookieHeader",
        "CookieCount",
        "CookieSource",
        "HttpClient",
        "HttpMessageHandler",
        "UserAgent",
        "\"Cookie\"",
    };

    private static IReadOnlyList<string> SourceFiles()
    {
        var services = RepoLayout.Path("DaouCalendarOverlay", "Services");
        var models = RepoLayout.Path("DaouCalendarOverlay", "Models");
        var app = RepoLayout.Path("DaouCalendarOverlay", "App.xaml.cs");

        Assert.True(Directory.Exists(services), $"Services 폴더가 없습니다: {services}");
        Assert.True(Directory.Exists(models), $"Models 폴더가 없습니다: {models}");
        Assert.True(File.Exists(app), $"App.xaml.cs가 없습니다: {app}");

        return Directory.EnumerateFiles(services, "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(models, "*.cs", SearchOption.AllDirectories))
            .Append(app)
            .ToList();
    }

    /// <summary>스캔 대상이 비어 있으면 가드가 무의미하다. 브리지 서버와 판정기가 대상에 들어오는지 확인한다.</summary>
    [Fact]
    public void ScanTargets_IncludeBridgeSources()
    {
        var files = SourceFiles();

        Assert.NotEmpty(files);
        Assert.Contains(files, file => string.Equals(Path.GetFileName(file), "CalendarBridgeServer.cs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(files, file => string.Equals(Path.GetFileName(file), "BridgeResultClassifier.cs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(files, file => string.Equals(Path.GetFileName(file), "CalendarModels.cs", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AppSources_DoNotContainCookieOrHttpClientTokens()
    {
        var violations = new List<string>();

        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file);
            foreach (var token in ForbiddenTokens)
            {
                if (text.Contains(token, StringComparison.Ordinal))
                    violations.Add($"{file} ({token})");
            }
        }

        Assert.True(
            violations.Count == 0,
            "쿠키 전달·앱 HTTP 조회의 흔적이 앱 소스에 남아 있음:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }
}
