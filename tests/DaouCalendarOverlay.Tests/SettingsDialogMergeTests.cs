using DaouCalendarOverlay.Models;
using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

public sealed class SettingsDialogMergeTests
{
    private static AppSettings Current() => new()
    {
        BaseUrl = "https://current.example.com",
        RefreshMinutes = 5,
        UiOpacity = 1.0,
        OverlayMinimized = true,
        Left = 123.5,
        Top = -4
    };

    private static AppSettings Dialog() => new()
    {
        BaseUrl = "https://dialog.example.com",
        RefreshMinutes = 15,
        UiOpacity = 0.6,
        OverlayMinimized = false,
        Left = 10,
        Top = 20
    };

    [Fact]
    public void PreserveOverlayState_TakesMinimizedLeftTopFromCurrent()
    {
        var result = SettingsDialogMerge.PreserveOverlayState(Dialog(), Current());

        Assert.True(result.OverlayMinimized);
        Assert.Equal(123.5, result.Left);
        Assert.Equal(-4, result.Top);
    }

    [Fact]
    public void PreserveOverlayState_NullLeftTopInCurrent_StaysNull()
    {
        var current = Current();
        current.Left = null;
        current.Top = null;

        var result = SettingsDialogMerge.PreserveOverlayState(Dialog(), current);

        Assert.Null(result.Left);
        Assert.Null(result.Top);
        Assert.True(result.OverlayMinimized);
    }

    [Fact]
    public void PreserveOverlayState_KeepsOtherDialogValues()
    {
        var result = SettingsDialogMerge.PreserveOverlayState(Dialog(), Current());

        Assert.Equal(15, result.RefreshMinutes);
        Assert.Equal("https://dialog.example.com", result.BaseUrl);
        Assert.Equal(0.6, result.UiOpacity);
    }

    [Fact]
    public void PreserveOverlayState_ReturnsSameInstanceAsDialogResult()
    {
        var dialog = Dialog();

        var result = SettingsDialogMerge.PreserveOverlayState(dialog, Current());

        Assert.Same(dialog, result);
    }

    [Fact]
    public void PreserveOverlayState_DoesNotModifyCurrent()
    {
        var current = Current();

        SettingsDialogMerge.PreserveOverlayState(Dialog(), current);

        Assert.True(current.OverlayMinimized);
        Assert.Equal(123.5, current.Left);
        Assert.Equal(-4, current.Top);
        Assert.Equal(5, current.RefreshMinutes);
        Assert.Equal("https://current.example.com", current.BaseUrl);
        Assert.Equal(1.0, current.UiOpacity);
    }

    [Fact]
    public void PreserveOverlayState_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => SettingsDialogMerge.PreserveOverlayState(null!, Current()));
        Assert.Throws<ArgumentNullException>(() => SettingsDialogMerge.PreserveOverlayState(Dialog(), null!));
    }
}
