using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

public sealed class LogFormatterTests
{
    private static readonly TimeSpan Kst = TimeSpan.FromHours(9);

    [Fact]
    public void FormatLine_ProducesTimestampLevelCategoryMessage()
    {
        var line = LogFormatter.FormatLine(
            new DateTimeOffset(2026, 9, 22, 13, 4, 5, 123, Kst),
            "INFO",
            "startup",
            "overlay 시작",
            null);

        Assert.Equal("2026-09-22 13:04:05.123 [INFO] startup: overlay 시작", line);
    }

    [Fact]
    public void FormatLine_CollapsesNewlinesIntoSingleLine()
    {
        var line = LogFormatter.FormatLine(
            new DateTimeOffset(2026, 9, 22, 13, 4, 5, 123, Kst),
            "INFO",
            "bridge",
            "a\r\nb\nc\rd",
            null);

        Assert.DoesNotContain("\n", line);
        Assert.DoesNotContain("\r", line);
        Assert.Contains("a b c d", line);
    }

    [Fact]
    public void FormatLine_WithException_AppendsIndentedExceptionLines()
    {
        var line = LogFormatter.FormatLine(
            new DateTimeOffset(2026, 9, 22, 13, 4, 5, 123, Kst),
            "ERROR",
            "sync",
            "동기화 실패",
            new InvalidOperationException("boom"));

        Assert.Contains(Environment.NewLine, line);

        var lines = line.Split(Environment.NewLine);
        Assert.True(lines.Length >= 2, "예외 줄이 이어 붙어야 한다.");
        Assert.StartsWith("    ", lines[1]);
        Assert.Contains("boom", lines[1]);
    }

    [Fact]
    public void FormatLine_EmptyCategory_FallsBackToGeneral()
    {
        var line = LogFormatter.FormatLine(
            new DateTimeOffset(2026, 9, 22, 13, 4, 5, 123, Kst),
            "WARN",
            "  ",
            "메시지",
            null);

        Assert.Contains("[WARN] general:", line);
    }
}
