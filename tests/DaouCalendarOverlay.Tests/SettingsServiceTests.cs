using System.Text.Json;
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

    /// <summary>
    /// SaveAsync가 산 인스턴스를 그대로 직렬화하면, 호출 스레드가 다음 루프에서 계속
    /// HiddenCalendarIds를 변형하는 동안 게이트 대기 후 스레드풀에서 진행되는 직렬화가
    /// "열거 중 컬렉션 수정" 예외로 실패할 수 있다(SaveAsync 진입 즉시 뜬 스냅샷을 직렬화하면 안전하다).
    /// </summary>
    [Fact]
    public async Task SaveAsync_WhileCallerKeepsMutatingHiddenCalendarIds_AllSavesSucceedAndFileStaysValidJson()
    {
        var service = new SettingsService(_dir);
        var settings = new AppSettings
        {
            BaseUrl = "https://x.daouoffice.com",
            CalendarIds = new List<string> { "12345" }
        };
        for (var seed = 0; seed < 50000; seed++)
            settings.HiddenCalendarIds.Add("seed" + seed);

        // 호출 스레드 하나에서만 SaveAsync를 요청하고 곧바로 리스트를 변형한다(Clone은 그 호출
        // 스레드에서 동기적으로 일어나야 하므로, 여러 스레드가 동시에 Clone을 호출하게 만들면
        // 안전하지 않다 — 이는 실제 UI 스레드 사용 패턴과도 다르다).
        // 각 SaveAsync 호출 자체는 게이트 대기 후 스레드풀에서 실제 직렬화를 계속 진행하므로, 다음
        // 저장을 큐잉하기 전 이 스레드에서 짧게 계속 변형해(리스트도 넉넉히 크게 둬 직렬화 시간을
        // 벌린다) 그 백그라운드 작업과 실제로 겹치게 한다. 큐가 무한정 쌓이지 않도록 라운드 수는 제한한다.
        const int Rounds = 25;
        var saveTasks = new List<Task<bool>>(Rounds);
        var i = 0;
        for (var round = 0; round < Rounds; round++)
        {
            saveTasks.Add(service.SaveAsync(settings));

            var spinUntil = DateTime.UtcNow.AddMilliseconds(3);
            while (DateTime.UtcNow < spinUntil)
            {
                settings.HiddenCalendarIds.Add("h" + i++);
                if (settings.HiddenCalendarIds.Count > 50100)
                    settings.HiddenCalendarIds.RemoveRange(50000, 50);
            }
        }

        var results = await Task.WhenAll(saveTasks).WaitAsync(TimeSpan.FromSeconds(20));

        Assert.All(results, r => Assert.True(r));

        var text = await File.ReadAllTextAsync(SettingsPath);
        var loaded = JsonSerializer.Deserialize<AppSettings>(text);
        Assert.NotNull(loaded);
        Assert.Equal("https://x.daouoffice.com", loaded!.BaseUrl);
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
