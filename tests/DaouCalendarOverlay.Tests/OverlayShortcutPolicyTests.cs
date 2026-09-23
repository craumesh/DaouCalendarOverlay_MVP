using System.Windows.Input;
using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

public sealed class OverlayShortcutPolicyTests
{
    private static OverlayShortcutAction Resolve(
        Key key,
        ModifierKeys modifiers = ModifierKeys.None,
        bool filterFocused = false,
        bool filterHasText = false,
        bool eventDetailsVisible = false,
        bool dayListVisible = false) =>
        OverlayShortcutPolicy.Resolve(key, modifiers, filterFocused, filterHasText, eventDetailsVisible, dayListVisible);

    [Fact]
    public void Resolve_F5_ReturnsRefresh()
    {
        Assert.Equal(OverlayShortcutAction.Refresh, Resolve(Key.F5));
        Assert.Equal(OverlayShortcutAction.Refresh, Resolve(Key.F5, ModifierKeys.Control, filterFocused: true));
        Assert.Equal(OverlayShortcutAction.Refresh, Resolve(Key.F5, ModifierKeys.Shift | ModifierKeys.Alt));
    }

    [Fact]
    public void Resolve_PageUpWithoutModifier_WhenFilterFocused_ReturnsNone()
    {
        Assert.Equal(OverlayShortcutAction.None, Resolve(Key.PageUp, filterFocused: true));
        Assert.Equal(OverlayShortcutAction.None, Resolve(Key.PageDown, filterFocused: true, filterHasText: true));
        Assert.Equal(OverlayShortcutAction.None, Resolve(Key.Home, filterFocused: true));
    }

    [Fact]
    public void Resolve_CtrlPageUp_WhenFilterFocused_ReturnsPreviousMonth()
    {
        Assert.Equal(OverlayShortcutAction.PreviousMonth, Resolve(Key.PageUp, ModifierKeys.Control, filterFocused: true));
        Assert.Equal(OverlayShortcutAction.NextMonth, Resolve(Key.PageDown, ModifierKeys.Control, filterFocused: true));
        Assert.Equal(OverlayShortcutAction.GoToday, Resolve(Key.Home, ModifierKeys.Control, filterFocused: true));
    }

    [Fact]
    public void Resolve_HomeWithoutModifier_WhenFilterNotFocused_ReturnsGoToday()
    {
        Assert.Equal(OverlayShortcutAction.GoToday, Resolve(Key.Home));
        Assert.Equal(OverlayShortcutAction.PreviousMonth, Resolve(Key.PageUp));
        Assert.Equal(OverlayShortcutAction.NextMonth, Resolve(Key.PageDown));
    }

    [Fact]
    public void Resolve_NavigationWithOtherModifiers_ReturnsNone()
    {
        Assert.Equal(OverlayShortcutAction.None, Resolve(Key.Home, ModifierKeys.Shift));
        Assert.Equal(OverlayShortcutAction.None, Resolve(Key.PageUp, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.Equal(OverlayShortcutAction.None, Resolve(Key.PageDown, ModifierKeys.Alt));
    }

    [Fact]
    public void Resolve_Escape_WhenFilterFocusedWithText_ReturnsClearFilter()
    {
        Assert.Equal(OverlayShortcutAction.ClearFilter, Resolve(Key.Escape, filterFocused: true, filterHasText: true));
    }

    [Fact]
    public void Resolve_Escape_WhenFilterFocusedWithoutText_ReturnsNone()
    {
        Assert.Equal(OverlayShortcutAction.None, Resolve(Key.Escape, filterFocused: true, filterHasText: false));
    }

    [Fact]
    public void Resolve_Escape_WhenEventDetailsVisible_ReturnsCloseEventDetails()
    {
        Assert.Equal(
            OverlayShortcutAction.CloseEventDetails,
            Resolve(Key.Escape, filterFocused: true, filterHasText: true, eventDetailsVisible: true, dayListVisible: true));
    }

    [Fact]
    public void Resolve_Escape_WhenDayListVisible_ReturnsCloseDayList()
    {
        Assert.Equal(
            OverlayShortcutAction.CloseDayList,
            Resolve(Key.Escape, filterFocused: true, filterHasText: true, dayListVisible: true));
    }

    [Fact]
    public void Resolve_Escape_WhenNothingOpen_ReturnsHideWindow()
    {
        Assert.Equal(OverlayShortcutAction.HideWindow, Resolve(Key.Escape));
        Assert.Equal(OverlayShortcutAction.HideWindow, Resolve(Key.Escape, filterHasText: true));
    }

    [Fact]
    public void Resolve_OtherKeys_ReturnNone()
    {
        Assert.Equal(OverlayShortcutAction.None, Resolve(Key.A));
        Assert.Equal(OverlayShortcutAction.None, Resolve(Key.End));
        Assert.Equal(OverlayShortcutAction.None, Resolve(Key.Enter, ModifierKeys.Control));
    }
}
