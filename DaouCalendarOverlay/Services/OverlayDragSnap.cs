using DaouCalendarOverlay.Models;

namespace DaouCalendarOverlay.Services;

/// <summary>드래그를 시작한 순간의 창 사각형과 커서 위치. 이동 루프 동안 바뀌지 않는다.</summary>
public readonly record struct DragSnapSession(PixelRect StartWindow, PixelPoint StartCursor);

/// <summary>
/// 창 드래그 중 커서 이동량으로 제안 사각형을 계산하고 작업 영역에 붙이는 순수 로직.
/// OS가 WM_MOVING에 실어 주는 lParam 위치는 이전 되쓰기 결과가 누적된 값이라 쓰지 않는다.
/// 결과는 (세션, 커서, 크기, 작업 영역, 임계값)만의 함수이므로 이동 경로에 의존하지 않는다.
/// 모든 값은 물리 px이고 Right/Bottom은 배타다. WPF·WinForms 타입을 참조하지 않는다.
/// </summary>
public static class OverlayDragSnap
{
    /// <summary>이동 루프가 시작될 때의 창 사각형과 커서 위치로 세션을 만든다.</summary>
    public static DragSnapSession Begin(PixelRect window, PixelPoint cursor) => new(window, cursor);

    /// <summary>
    /// 시작 사각형의 Left/Top에 (cursor - StartCursor)를 더하고 크기는 widthPx x heightPx로 한다.
    /// 크기를 인자로 받는 이유는 이동 중 창 크기가 바뀔 수 있어 lParam의 현재 크기를 쓰기 위해서다.
    /// widthPx 또는 heightPx가 0 이하이면 ArgumentOutOfRangeException.
    /// </summary>
    public static PixelRect Propose(DragSnapSession session, PixelPoint cursor, int widthPx, int heightPx)
    {
        if (widthPx <= 0)
            throw new ArgumentOutOfRangeException(nameof(widthPx), widthPx, "창 폭은 0보다 커야 합니다.");
        if (heightPx <= 0)
            throw new ArgumentOutOfRangeException(nameof(heightPx), heightPx, "창 높이는 0보다 커야 합니다.");

        var left = session.StartWindow.Left + (cursor.X - session.StartCursor.X);
        var top = session.StartWindow.Top + (cursor.Y - session.StartCursor.Y);
        return new PixelRect(left, top, left + widthPx, top + heightPx);
    }

    /// <summary>
    /// 드래그 끝 로그를 남길지 판정한다. 시작 사각형과 최종 사각형이 같으면(창이 움직이지 않은 클릭) false,
    /// 위치나 크기가 하나라도 다르면 true다. 빈 영역을 클릭해도 DragMove가 이동 루프를 열기 때문에 필요하다.
    /// </summary>
    public static bool ShouldLogDragEnd(PixelRect start, PixelRect final) => start != final;

    /// <summary>Snap(Propose(...), workArea, thresholdPx)와 같다.</summary>
    public static PixelRect Step(
        DragSnapSession session,
        PixelPoint cursor,
        int widthPx,
        int heightPx,
        PixelRect workArea,
        int thresholdPx) =>
        OverlaySnap.Snap(Propose(session, cursor, widthPx, heightPx), workArea, thresholdPx);
}
