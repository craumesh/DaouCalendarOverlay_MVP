using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

// 이 파일의 테스트는 실제 레지스트리·파일시스템·브라우저에 접근하지 않는다.
// 값 정규화, URL 판정, 후보 선택 같은 순수 함수만 검증하고, 파일 존재 확인은 가짜 집합으로 주입한다.
public sealed class BrowserLauncherTests
{
    private static Func<string, bool> FakeFiles(params string[] existing)
    {
        var set = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        return set.Contains;
    }

    [Fact]
    public void NormalizeChromePath_NullOrWhitespace_ReturnsNull()
    {
        Assert.Null(BrowserLauncher.NormalizeChromePath(null));
        Assert.Null(BrowserLauncher.NormalizeChromePath(""));
        Assert.Null(BrowserLauncher.NormalizeChromePath("   "));
    }

    [Fact]
    public void NormalizeChromePath_StripsSurroundingQuotes()
    {
        var normalized = BrowserLauncher.NormalizeChromePath("\"C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe\"");

        Assert.Equal(@"C:\Program Files\Google\Chrome\Application\chrome.exe", normalized);
    }

    [Fact]
    public void NormalizeChromePath_ExpandsEnvironmentVariables()
    {
        var expectedRoot = Environment.ExpandEnvironmentVariables("%SystemRoot%");

        var normalized = BrowserLauncher.NormalizeChromePath("%SystemRoot%\\x.exe");

        Assert.NotNull(normalized);
        Assert.Equal(expectedRoot + @"\x.exe", normalized);
        Assert.DoesNotContain("%", normalized, StringComparison.Ordinal);
    }

    [Fact]
    public void IsLaunchableUrl_HttpsUrl_IsTrue()
    {
        Assert.True(BrowserLauncher.IsLaunchableUrl("https://company.daouoffice.com"));
    }

    [Fact]
    public void IsLaunchableUrl_NonHttpOrArgument_IsFalse()
    {
        Assert.False(BrowserLauncher.IsLaunchableUrl(null));
        Assert.False(BrowserLauncher.IsLaunchableUrl(""));
        Assert.False(BrowserLauncher.IsLaunchableUrl("--incognito"));
        Assert.False(BrowserLauncher.IsLaunchableUrl("file:///C:/x.html"));
        Assert.False(BrowserLauncher.IsLaunchableUrl("company.daouoffice.com"));
    }

    [Fact]
    public void SelectChromePath_DefaultValueExists_ReturnsIt()
    {
        var keys = new (object? DefaultValue, object? PathValue)[]
        {
            (@"C:\Chrome\chrome.exe", null)
        };

        var selected = BrowserLauncher.SelectChromePath(keys, FakeFiles(@"C:\Chrome\chrome.exe"));

        Assert.Equal(@"C:\Chrome\chrome.exe", selected);
    }

    [Fact]
    public void SelectChromePath_FirstKeyEmpty_FallsBackToSecond()
    {
        var keys = new (object? DefaultValue, object? PathValue)[]
        {
            (null, null),
            (@"C:\Machine\chrome.exe", null)
        };

        var selected = BrowserLauncher.SelectChromePath(keys, FakeFiles(@"C:\Machine\chrome.exe"));

        Assert.Equal(@"C:\Machine\chrome.exe", selected);
    }

    [Fact]
    public void SelectChromePath_DefaultMissing_UsesPathValue()
    {
        var keys = new (object? DefaultValue, object? PathValue)[]
        {
            (null, @"C:\Chrome\Application")
        };

        var selected = BrowserLauncher.SelectChromePath(keys, FakeFiles(@"C:\Chrome\Application\chrome.exe"));

        Assert.Equal(@"C:\Chrome\Application\chrome.exe", selected);
    }

    [Fact]
    public void SelectChromePath_QuotedDefaultValue_IsNormalized()
    {
        var keys = new (object? DefaultValue, object? PathValue)[]
        {
            ("\"C:\\Chrome\\chrome.exe\"", null)
        };

        var selected = BrowserLauncher.SelectChromePath(keys, FakeFiles(@"C:\Chrome\chrome.exe"));

        Assert.Equal(@"C:\Chrome\chrome.exe", selected);
    }

    [Fact]
    public void SelectChromePath_NoCandidateExists_ReturnsNull()
    {
        var keys = new (object? DefaultValue, object? PathValue)[]
        {
            (@"C:\Chrome\chrome.exe", @"C:\Chrome\Application"),
            (@"C:\Machine\chrome.exe", @"C:\Machine")
        };

        var selected = BrowserLauncher.SelectChromePath(keys, _ => false);

        Assert.Null(selected);
    }

    [Fact]
    public void SelectChromePath_NonStringValues_AreIgnored()
    {
        var keys = new (object? DefaultValue, object? PathValue)[]
        {
            (42, new byte[] { 1 })
        };

        Assert.Null(BrowserLauncher.SelectChromePath(keys, _ => true));
        Assert.Null(BrowserLauncher.SelectChromePath(null, _ => true));
    }
}
