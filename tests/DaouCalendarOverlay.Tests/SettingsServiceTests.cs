using DaouCalendarOverlay.Models;
using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _dir;

    public SettingsServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "dco-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
            // 임시 디렉터리 정리 실패는 무시한다.
        }
    }

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    [Fact]
    public async Task SaveAsync_DoesNotWriteIsConfiguredKey()
    {
        var service = new SettingsService(_dir);

        var saved = await service.SaveAsync(new AppSettings
        {
            BaseUrl = "https://x.daouoffice.com",
            CalendarIds = new List<string> { "12345" }
        });

        Assert.True(saved);

        var text = File.ReadAllText(SettingsPath);
        Assert.DoesNotContain("IsConfigured", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("BaseUrl", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_IgnoresLegacyIsConfiguredKey()
    {
        File.WriteAllText(
            SettingsPath,
            "{\"BaseUrl\":\"https://x.daouoffice.com\",\"CalendarIds\":[\"123\"],\"RefreshMinutes\":9,\"IsConfigured\":true}");

        var loaded = await new SettingsService(_dir).LoadAsync();

        Assert.Equal("https://x.daouoffice.com", loaded.BaseUrl);
        Assert.Equal(9, loaded.RefreshMinutes);
        Assert.Equal(new[] { "123" }, loaded.CalendarIds);
    }

    [Fact]
    public async Task SaveAsync_HundredConcurrentSaves_AllSucceed()
    {
        var service = new SettingsService(_dir);

        var results = await Task.WhenAll(Enumerable.Range(1, 100)
            .Select(i => service.SaveAsync(new AppSettings
            {
                BaseUrl = "https://x.daouoffice.com",
                CalendarIds = new List<string> { "12345" },
                RefreshMinutes = i
            })));

        Assert.Equal(100, results.Length);
        Assert.All(results, r => Assert.True(r));

        var loaded = await service.LoadAsync();
        Assert.Equal("https://x.daouoffice.com", loaded.BaseUrl);
        Assert.InRange(loaded.RefreshMinutes, 1, 100);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public async Task LoadAsync_ReturnsDefaults_WhenFileMissing()
    {
        var loaded = await new SettingsService(_dir).LoadAsync();

        Assert.Equal("", loaded.BaseUrl);
        Assert.Equal(5, loaded.RefreshMinutes);
        Assert.Empty(loaded.CalendarIds);
    }
}
