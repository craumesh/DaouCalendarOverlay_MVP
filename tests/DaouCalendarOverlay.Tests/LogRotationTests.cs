using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

public sealed class LogRotationTests
{
    [Fact]
    public void GetFileName_UsesPrefixAndYyyyMMdd()
    {
        var timestamp = new DateTimeOffset(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal("overlay-20260922.log", LogRotation.GetFileName("overlay", timestamp));
        Assert.Equal("host-20260922.log", LogRotation.GetFileName("host", timestamp));
    }

    [Fact]
    public void GetArchivePath_AppendsDotIndex()
    {
        Assert.Equal("C:\\x\\overlay-20260922.log.1", LogRotation.GetArchivePath("C:\\x\\overlay-20260922.log", 1));
    }

    [Fact]
    public void GetArchivePath_RejectsOutOfRangeIndex()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            LogRotation.GetArchivePath("C:\\x\\overlay-20260922.log", 0);
        });

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            LogRotation.GetArchivePath("C:\\x\\overlay-20260922.log", 5);
        });
    }

    [Fact]
    public void ShouldRotate_TrueWhenTotalExceedsOneMegabyte()
    {
        Assert.True(LogRotation.ShouldRotate(1024 * 1024, 1));
    }

    [Fact]
    public void ShouldRotate_FalseWhenTotalEqualsLimit()
    {
        Assert.False(LogRotation.ShouldRotate((1024 * 1024) - 10, 10));
    }

    [Fact]
    public void BuildRotationPlan_ReturnsMovesFromOldestToNewest()
    {
        var plan = LogRotation.BuildRotationPlan("p");

        Assert.Equal(4, plan.Count);
        Assert.Equal("p.3", plan[0].Source);
        Assert.Equal("p.4", plan[0].Destination);
        Assert.Equal("p.2", plan[1].Source);
        Assert.Equal("p.3", plan[1].Destination);
        Assert.Equal("p.1", plan[2].Source);
        Assert.Equal("p.2", plan[2].Destination);
        Assert.Equal("p", plan[3].Source);
        Assert.Equal("p.1", plan[3].Destination);
    }
}
