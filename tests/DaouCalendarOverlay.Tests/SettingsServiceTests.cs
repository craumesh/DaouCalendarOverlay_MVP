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

    /// <summary>RegisterEdge 도입 전 settings.json에는 키가 없다. 이때 Edge 등록은 켜진 상태로 로드돼야 한다.</summary>
    [Fact]
    public async Task LoadAsync_FileWithoutRegisterEdge_DefaultsToTrue()
    {
        File.WriteAllText(
            SettingsPath,
            "{\"BaseUrl\":\"https://x.daouoffice.com\",\"CalendarIds\":[\"123\"]}");

        var loaded = await new SettingsService(_dir).LoadAsync();

        Assert.Equal("https://x.daouoffice.com", loaded.BaseUrl);
        Assert.True(loaded.RegisterEdge);
    }

    [Fact]
    public async Task SaveAsync_WritesRegisterEdgeKey()
    {
        var service = new SettingsService(_dir);

        var saved = await service.SaveAsync(new AppSettings
        {
            BaseUrl = "https://x.daouoffice.com",
            CalendarIds = new List<string> { "12345" },
            RegisterEdge = false
        });

        Assert.True(saved);

        var text = File.ReadAllText(SettingsPath);
        Assert.Contains("\"RegisterEdge\"", text, StringComparison.Ordinal);

        var loaded = await service.LoadAsync();
        Assert.False(loaded.RegisterEdge);
    }

    [Fact]
    public async Task SaveAsync_LoadAsync_RoundTripsCalendarNames()
    {
        var service = new SettingsService(_dir);
        var settings = new AppSettings
        {
            BaseUrl = "https://x.daouoffice.com",
            CalendarIds = new List<string> { "123", "456" }
        };
        settings.CalendarNames["123"] = "팀 캘린더";
        settings.CalendarNames["456"] = "내 캘린더";

        Assert.True(await service.SaveAsync(settings));

        var text = File.ReadAllText(SettingsPath);
        Assert.Contains("\"CalendarNames\"", text, StringComparison.Ordinal);

        var loaded = await service.LoadAsync();
        Assert.Equal(2, loaded.CalendarNames.Count);
        Assert.Equal("팀 캘린더", loaded.CalendarNames["123"]);
        Assert.Equal("내 캘린더", loaded.CalendarNames["456"]);
    }

    /// <summary>CalendarNames 도입 전 settings.json에는 키가 없다. 이때 빈 사전으로 로드되고 다른 값은 그대로다.</summary>
    [Fact]
    public async Task LoadAsync_FileWithoutCalendarNames_DefaultsToEmptyDictionary()
    {
        File.WriteAllText(
            SettingsPath,
            "{\"BaseUrl\":\"https://x.daouoffice.com\",\"CalendarIds\":[\"123\"],\"RefreshMinutes\":7}");

        var loaded = await new SettingsService(_dir).LoadAsync();

        Assert.Equal("https://x.daouoffice.com", loaded.BaseUrl);
        Assert.Equal(7, loaded.RefreshMinutes);
        Assert.NotNull(loaded.CalendarNames);
        Assert.Empty(loaded.CalendarNames);
        Assert.True(loaded.IsConfigured);
    }

    /// <summary>
    /// 7.3.1이 쓴 settings.json(OverlayMinimized·SnapToScreenEdges 키 없음)은 펼침 모드, 스냅 켬으로 읽고
    /// 기존 키 값은 그대로 유지한다.
    /// </summary>
    [Fact]
    public async Task LoadAsync_FileWithoutOverlayKeys_DefaultsToExpandedAndSnapOn()
    {
        File.WriteAllText(
            SettingsPath,
            """
            {
              "BaseUrl": "https://x.daouoffice.com",
              "CalendarIds": [ "123" ],
              "HiddenCalendarIds": [],
              "RefreshMinutes": 7,
              "StartWithWindows": false,
              "AlwaysOnTop": true,
              "PositionLocked": true,
              "RegisterEdge": false,
              "UiOpacity": 0.6,
              "Left": 1200.5,
              "Top": 300,
              "CalendarNames": { "123": "팀 캘린더" }
            }
            """);

        var loaded = await new SettingsService(_dir).LoadAsync();

        Assert.False(loaded.OverlayMinimized);
        Assert.True(loaded.SnapToScreenEdges);

        // 옛 파일을 기본값으로 대체한 것이 아니라 실제로 읽었는지 기존 키로 확인한다.
        Assert.Equal("https://x.daouoffice.com", loaded.BaseUrl);
        Assert.Equal(new[] { "123" }, loaded.CalendarIds);
        Assert.Equal(7, loaded.RefreshMinutes);
        Assert.False(loaded.StartWithWindows);
        Assert.True(loaded.AlwaysOnTop);
        Assert.True(loaded.PositionLocked);
        Assert.False(loaded.RegisterEdge);
        Assert.Equal(0.6, loaded.UiOpacity);
        Assert.Equal(1200.5, loaded.Left);
        Assert.Equal(300.0, loaded.Top);
        Assert.Equal("팀 캘린더", loaded.CalendarNames["123"]);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task SaveThenLoad_RoundTripsOverlayKeys(bool overlayMinimized, bool snapToScreenEdges)
    {
        var service = new SettingsService(_dir);

        var saved = await service.SaveAsync(new AppSettings
        {
            BaseUrl = "https://x.daouoffice.com",
            CalendarIds = new List<string> { "12345" },
            OverlayMinimized = overlayMinimized,
            SnapToScreenEdges = snapToScreenEdges,
            Left = 100,
            Top = 200
        });

        Assert.True(saved);

        var loaded = await new SettingsService(_dir).LoadAsync();
        Assert.Equal(overlayMinimized, loaded.OverlayMinimized);
        Assert.Equal(snapToScreenEdges, loaded.SnapToScreenEdges);
        Assert.Equal(100.0, loaded.Left);
        Assert.Equal(200.0, loaded.Top);
    }

    /// <summary>새 키도 기존 키와 같이 PascalCase 속성 이름 그대로 저장된다(명명 정책 없음).</summary>
    [Fact]
    public async Task SaveAsync_WritesOverlayKeysInPascalCase()
    {
        var service = new SettingsService(_dir);

        Assert.True(await service.SaveAsync(new AppSettings
        {
            BaseUrl = "https://x.daouoffice.com",
            CalendarIds = new List<string> { "12345" },
            OverlayMinimized = true,
            SnapToScreenEdges = false
        }));

        var text = File.ReadAllText(SettingsPath);
        Assert.Contains("\"OverlayMinimized\": true", text, StringComparison.Ordinal);
        Assert.Contains("\"SnapToScreenEdges\": false", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"overlayMinimized\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"snapToScreenEdges\"", text, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(text);
        Assert.Equal(JsonValueKind.True, document.RootElement.GetProperty("OverlayMinimized").ValueKind);
        Assert.Equal(JsonValueKind.False, document.RootElement.GetProperty("SnapToScreenEdges").ValueKind);
    }

    /// <summary>
    /// 하위 호환: 7.3.1의 AppSettings(새 키 없음)처럼 모델에 없는 키는 System.Text.Json 기본 동작으로 무시된다.
    /// 7.4.0이 쓴 파일을 새 키를 모르는 모델로 읽어도 실패하지 않고 기존 키를 그대로 읽는지 확인한다.
    /// </summary>
    [Fact]
    public async Task SavedFile_ReadByModelWithoutOverlayKeys_IgnoresNewKeys()
    {
        var service = new SettingsService(_dir);
        Assert.True(await service.SaveAsync(new AppSettings
        {
            BaseUrl = "https://x.daouoffice.com",
            CalendarIds = new List<string> { "12345" },
            RefreshMinutes = 9,
            OverlayMinimized = true,
            SnapToScreenEdges = false
        }));

        var legacy = JsonSerializer.Deserialize<LegacySettings731>(File.ReadAllText(SettingsPath));

        Assert.NotNull(legacy);
        Assert.Equal("https://x.daouoffice.com", legacy!.BaseUrl);
        Assert.Equal(new[] { "12345" }, legacy.CalendarIds);
        Assert.Equal(9, legacy.RefreshMinutes);
    }

    /// <summary>7.3.1 AppSettings의 키 일부를 흉내 낸 모델(새 키를 모른다).</summary>
    private sealed class LegacySettings731
    {
        public string BaseUrl { get; set; } = "";
        public List<string> CalendarIds { get; set; } = new();
        public int RefreshMinutes { get; set; } = 5;
    }
}
