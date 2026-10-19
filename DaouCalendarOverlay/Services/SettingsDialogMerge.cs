using DaouCalendarOverlay.Models;

namespace DaouCalendarOverlay.Services;

/// <summary>설정창이 돌려준 결과를 현재 설정과 합치는 순수 로직.</summary>
public static class SettingsDialogMerge
{
    /// <summary>
    /// <paramref name="dialogResult"/>의 OverlayMinimized·Left·Top에 <paramref name="current"/>의 값을 넣고
    /// <paramref name="dialogResult"/>(같은 인스턴스)를 돌려준다. <paramref name="current"/>는 바꾸지 않는다.
    /// 설정창은 열 때의 복제본을 돌려주므로, 대화 상자가 열린 동안 바뀐 최소화 상태와 위치를 되돌리지 않도록
    /// 현재 값을 다시 넣는다(설정창은 이 세 값을 다루지 않는다).
    /// </summary>
    /// <exception cref="ArgumentNullException">인자가 null일 때.</exception>
    public static AppSettings PreserveOverlayState(AppSettings dialogResult, AppSettings current)
    {
        ArgumentNullException.ThrowIfNull(dialogResult);
        ArgumentNullException.ThrowIfNull(current);

        dialogResult.OverlayMinimized = current.OverlayMinimized;
        dialogResult.Left = current.Left;
        dialogResult.Top = current.Top;
        return dialogResult;
    }
}
