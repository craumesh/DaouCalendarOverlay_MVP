using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

public sealed class EventColorPaletteTests
{
    [Fact]
    public void Palette_HasEightColors()
    {
        Assert.Equal(8, EventColorPalette.Palette.Count);
    }

    [Fact]
    public void Resolve_KnownIndex_ReturnsMappedPaletteColor()
    {
        Assert.Equal(EventColorPalette.Palette[1], EventColorPalette.Resolve("1", "cal-1"));
    }

    [Fact]
    public void Resolve_IntMinValue_DoesNotThrow()
    {
        var color = EventColorPalette.Resolve("-2147483648", "cal-1");

        Assert.Contains(color, EventColorPalette.Palette);
    }

    [Fact]
    public void Resolve_IndexOutsideTable_FallsBackToPalette()
    {
        var color = EventColorPalette.Resolve("1000", null);

        Assert.Contains(color, EventColorPalette.Palette);
    }

    [Fact]
    public void Resolve_NonNumericColor_IsStableForSameCalendarId()
    {
        // 문자열 해시는 프로세스마다 달라지므로 특정 색을 단정하지 않는다.
        var first = EventColorPalette.Resolve("blue", "cal-42");
        var second = EventColorPalette.Resolve("blue", "cal-42");

        Assert.Equal(first, second);
        Assert.Contains(first, EventColorPalette.Palette);
    }

    [Fact]
    public void IndexColors_CoversObservedRange()
    {
        for (var i = 1; i <= 18; i++)
            Assert.True(EventColorPalette.IndexColors.ContainsKey(i), $"index {i} missing");
    }
}
