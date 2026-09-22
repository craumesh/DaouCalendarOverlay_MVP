using System.Text.Json;
using DaouCalendarOverlay.Models;
using DaouCalendarOverlay.Services;

// LogService 는 프로세스 전역 정적 상태(로그 디렉터리 1개)를 갖는다.
// AtomicJsonFileWriter 의 실패 경로는 그 전역 로거에 기록하므로, 테스트 클래스가 병렬로 돌면
// LogServiceTests 가 자기 디렉터리에서 세는 줄 수에 다른 클래스의 로그가 섞여 들어간다.
// 클래스 간 병렬 실행만 끄고(클래스 안의 Task.WhenAll 동시성 테스트는 그대로 유지) 결정적으로 만든다.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace DaouCalendarOverlay.Tests;

public sealed class AtomicJsonFileWriterTests : IDisposable
{
    private readonly string _dir;

    public AtomicJsonFileWriterTests()
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

    private static JsonSerializerOptions Options => new() { WriteIndented = true };

    [Fact]
    public void CreateTempPath_IsInSameDirectoryAndUnique()
    {
        var finalPath = Path.Combine(_dir, "settings.json");

        var first = AtomicJsonFileWriter.CreateTempPath(finalPath);
        var second = AtomicJsonFileWriter.CreateTempPath(finalPath);

        Assert.NotEqual(first, second);
        Assert.Equal(Path.GetDirectoryName(finalPath), Path.GetDirectoryName(first));
        Assert.Equal(Path.GetDirectoryName(finalPath), Path.GetDirectoryName(second));
        Assert.EndsWith(".tmp", first, StringComparison.Ordinal);
        Assert.EndsWith(".tmp", second, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsync_WritesJsonAndLeavesNoTempFile()
    {
        var path = Path.Combine(_dir, "settings.json");
        var settings = new AppSettings { BaseUrl = "https://x.daouoffice.com", RefreshMinutes = 7 };

        var ok = await AtomicJsonFileWriter.WriteAsync(path, settings, Options, "test");

        Assert.True(ok);
        Assert.True(File.Exists(path));

        var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
        Assert.NotNull(loaded);
        Assert.Equal(7, loaded!.RefreshMinutes);
        Assert.Equal("https://x.daouoffice.com", loaded.BaseUrl);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public async Task WriteAsync_HundredConcurrentWrites_AllSucceedAndFileStaysValid()
    {
        var path = Path.Combine(_dir, "settings.json");

        var results = await Task.WhenAll(Enumerable.Range(1, 100)
            .Select(i => AtomicJsonFileWriter.WriteAsync(
                path,
                new AppSettings { RefreshMinutes = i },
                new JsonSerializerOptions { WriteIndented = true },
                "test")));

        Assert.Equal(100, results.Length);
        Assert.All(results, r => Assert.True(r));

        var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
        Assert.NotNull(loaded);
        Assert.InRange(loaded!.RefreshMinutes, 1, 100);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public async Task WriteAsync_ReturnsFalseAndCleansTemp_WhenTargetPathIsDirectory()
    {
        // 대상 경로가 이미 디렉터리이면 File.Move 가 실패한다.
        var path = Path.Combine(_dir, "settings.json");
        Directory.CreateDirectory(path);

        var ok = await AtomicJsonFileWriter.WriteAsync(path, new AppSettings(), Options, "test");

        Assert.False(ok);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        Assert.True(Directory.Exists(path), "실패해도 기존 대상은 그대로 남아야 한다.");
    }
}
