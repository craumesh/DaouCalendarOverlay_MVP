using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

public sealed class LogServiceTests : IDisposable
{
    private readonly string _dir;

    public LogServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "DaouLogTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        LogService.Initialize(_dir, "overlay");
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

    private string ActivePath => Path.Combine(_dir, LogRotation.GetFileName("overlay", DateTimeOffset.Now));

    /// <summary>
    /// 디렉터리 안 모든 활성 로그 파일(*.log, 보관본 .1~.4 제외)의 줄을 모은다.
    /// 자정 경계에서 파일이 갈려도 흔들리지 않게 하기 위함이다.
    /// </summary>
    private string[] ReadAllLogLines()
    {
        return Directory.GetFiles(_dir)
            .Where(f => f.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal)
            .SelectMany(File.ReadAllLines)
            .ToArray();
    }

    [Fact]
    public void Info_WritesSingleLineToDatedFile()
    {
        LogService.Info("startup", "hello");

        var files = Directory.GetFiles(_dir)
            .Where(f => f.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var file = Assert.Single(files);
        var name = Path.GetFileName(file);
        Assert.StartsWith("overlay-", name);
        Assert.Equal("overlay-".Length + 8 + ".log".Length, name.Length);

        var line = Assert.Single(ReadAllLogLines());
        Assert.Contains("[INFO] startup: hello", line);
    }

    [Fact]
    public void Error_WithException_WritesExceptionLines()
    {
        LogService.Error("sync", "동기화 실패", new InvalidOperationException("boom"));

        var lines = ReadAllLogLines();

        Assert.True(lines.Length >= 2, $"예외 줄이 있어야 한다. 실제 줄 수: {lines.Length}");
        Assert.Contains("[ERROR] sync: 동기화 실패", lines[0]);
        Assert.StartsWith("    ", lines[1]);
        Assert.Contains("boom", lines[1]);
    }

    [Fact]
    public async Task Write_HundredParallelCalls_WritesAllLines()
    {
        await Task.WhenAll(Enumerable.Range(0, 100).Select(i => Task.Run(() => LogService.Info("c", $"m{i}"))));

        var lines = ReadAllLogLines();
        Assert.Equal(100, lines.Length);

        var messages = lines
            .Select(l => l.Substring(l.LastIndexOf(": ", StringComparison.Ordinal) + 2))
            .ToList();
        Assert.Equal(100, messages.Distinct().Count());

        for (var i = 0; i < 100; i++)
            Assert.Contains($"m{i}", messages);
    }

    [Fact]
    public void Write_RotatesWhenActiveFileExceedsLimit()
    {
        var path = ActivePath;
        File.WriteAllBytes(path, new byte[1024 * 1024]);

        LogService.Info("c", "after");

        Assert.True(File.Exists(LogRotation.GetArchivePath(path, 1)), "활성 파일이 .1 로 밀려나야 한다.");
        Assert.Equal(1024 * 1024, new FileInfo(LogRotation.GetArchivePath(path, 1)).Length);
        Assert.True(new FileInfo(path).Length < 1024, "새 활성 파일에는 방금 쓴 한 줄만 있어야 한다.");
    }

    [Fact]
    public void Initialize_WithUnusableDirectory_DoesNotThrow()
    {
        var blocker = Path.Combine(_dir, "blocker.tmp");
        File.WriteAllText(blocker, "not a directory");

        LogService.Initialize(blocker, "overlay");

        Assert.False(LogService.IsInitialized);
        Assert.Null(LogService.LogDirectory);

        // 미초기화 상태에서도 호출이 예외를 던지지 않아야 한다.
        LogService.Info("c", "x");

        LogService.Initialize(_dir, "overlay");
        Assert.True(LogService.IsInitialized);
    }
}
