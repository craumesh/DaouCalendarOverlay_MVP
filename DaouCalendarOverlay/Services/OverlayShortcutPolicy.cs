using System.Windows.Input;

namespace DaouCalendarOverlay.Services;

/// <summary>오버레이 키보드 단축키가 실행할 동작.</summary>
public enum OverlayShortcutAction
{
    None,
    PreviousMonth,
    NextMonth,
    GoToday,
    Refresh,
    ClearFilter,
    CloseEventDetails,
    CloseDayList,
    HideWindow
}

/// <summary>
/// 오버레이 창 <c>PreviewKeyDown</c>에서 키·수정자·포커스 상태를 동작으로 바꾸는 순수 로직.
/// 검색창에 포커스가 있으면 PageUp/PageDown/Home은 텍스트 편집에 양보하고, Ctrl 조합은 항상 달력 이동으로 처리한다.
/// </summary>
public static class OverlayShortcutPolicy
{
    public static OverlayShortcutAction Resolve(
        Key key,
        ModifierKeys modifiers,
        bool filterFocused,
        bool filterHasText,
        bool eventDetailsVisible,
        bool dayListVisible,
        bool miniBarMode = false)
    {
        if (miniBarMode)
        {
            // 막대 모드: 달 이동 키는 막지 않으면 보이지 않는 달이 바뀌므로 None.
            // 그 밖의 키는 검색창 상태를 무시(포커스·검색어 없음으로 강제)하고 기존 규칙을 그대로 쓴다.
            if (key is Key.PageUp or Key.PageDown or Key.Home)
                return OverlayShortcutAction.None;

            return Resolve(key, modifiers, false, false, eventDetailsVisible, dayListVisible);
        }

        if (key == Key.F5)
            return OverlayShortcutAction.Refresh;

        if (key == Key.Escape)
        {
            if (eventDetailsVisible)
                return OverlayShortcutAction.CloseEventDetails;
            if (dayListVisible)
                return OverlayShortcutAction.CloseDayList;
            if (filterFocused && filterHasText)
                return OverlayShortcutAction.ClearFilter;
            if (filterFocused)
                return OverlayShortcutAction.None;
            return OverlayShortcutAction.HideWindow;
        }

        var navigation = key switch
        {
            Key.PageUp => OverlayShortcutAction.PreviousMonth,
            Key.PageDown => OverlayShortcutAction.NextMonth,
            Key.Home => OverlayShortcutAction.GoToday,
            _ => OverlayShortcutAction.None
        };

        if (navigation == OverlayShortcutAction.None)
            return OverlayShortcutAction.None;

        if (modifiers == ModifierKeys.Control)
            return navigation;

        if (modifiers == ModifierKeys.None && !filterFocused)
            return navigation;

        return OverlayShortcutAction.None;
    }
}
