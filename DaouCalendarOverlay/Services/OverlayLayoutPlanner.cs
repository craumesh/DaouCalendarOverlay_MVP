using DaouCalendarOverlay.Models;

namespace DaouCalendarOverlay.Services;

/// <summary>
/// 접기·펼치기 기준 모서리, 목록을 붙일 방향, 새 창 사각형(작업 영역 클램프 포함)을 정하는 순수 로직.
/// 모든 값은 물리 px이고 Right/Bottom은 배타다. WPF·WinForms 타입을 참조하지 않는다.
/// 창 사각형이 곧 카드 사각형이므로 클램프 기준은 작업 영역 그대로다.
/// </summary>
public static class OverlayLayoutPlanner
{
    /// <summary>
    /// 접기·펼치기의 기준 모서리를 정한다.
    /// (a) |window.Bottom - work.Bottom| &lt;= edgeTolerancePx 이거나
    /// (b) window.Top + newHeightPx &gt; work.Bottom (위쪽 고정으로는 안 들어감)이면 Bottom, 아니면 Top이다.
    /// </summary>
    public static VerticalAnchor ChooseResizeAnchor(PixelRect window, PixelRect work, int newHeightPx, int edgeTolerancePx)
    {
        if (Math.Abs(window.Bottom - work.Bottom) <= edgeTolerancePx)
            return VerticalAnchor.Bottom;

        if (window.Top + newHeightPx > work.Bottom)
            return VerticalAnchor.Bottom;

        return VerticalAnchor.Top;
    }

    /// <summary>
    /// 가로는 그대로 두고 anchor 쪽 변을 고정해 높이를 newHeightPx로 바꾼다.
    /// 그다음 세로를 work 안으로 클램프한다.
    /// newHeightPx가 work 높이 이상이면 Top은 work.Top이다.
    /// </summary>
    public static PixelRect ResizeWindow(PixelRect window, int newHeightPx, VerticalAnchor anchor, PixelRect work)
    {
        if (newHeightPx <= 0)
            throw new ArgumentOutOfRangeException(nameof(newHeightPx), newHeightPx, "창 높이는 0보다 커야 합니다.");

        var top = anchor == VerticalAnchor.Top ? window.Top : window.Bottom - newHeightPx;

        if (newHeightPx >= work.Height)
            top = work.Top;
        else
            top = Math.Clamp(top, work.Top, work.Bottom - newHeightPx);

        return new PixelRect(window.Left, top, window.Right, top + newHeightPx);
    }

    /// <summary>
    /// 창 크기를 보존한 채 평행 이동해 창이 작업 영역 안에 들어오게 한다.
    /// 창이 작업 영역보다 크면 work의 Left/Top에 맞춘다.
    /// </summary>
    public static PixelRect ClampWindowToWorkArea(PixelRect window, PixelRect work)
    {
        var left = window.Width >= work.Width
            ? work.Left
            : Math.Clamp(window.Left, work.Left, work.Right - window.Width);
        var top = window.Height >= work.Height
            ? work.Top
            : Math.Clamp(window.Top, work.Top, work.Bottom - window.Height);

        return window.Offset(left - window.Left, top - window.Top);
    }

    /// <summary>
    /// 막대 아래에 attachPx가 들어가면 Below, 아니고 위에 들어가면 Above다.
    /// 둘 다 안 들어가면 남은 공간이 큰 쪽이고 같으면 Below다.
    /// </summary>
    public static AttachDirection ChooseAttachDirection(PixelRect window, PixelRect work, int attachPx)
    {
        if (window.Bottom + attachPx <= work.Bottom)
            return AttachDirection.Below;

        if (window.Top - attachPx >= work.Top)
            return AttachDirection.Above;

        var spaceBelow = work.Bottom - window.Bottom;
        var spaceAbove = window.Top - work.Top;
        return spaceAbove > spaceBelow ? AttachDirection.Above : AttachDirection.Below;
    }

    /// <summary>직전 전환 기록. WindowBefore: 전환 시작 때 창, WindowAfter: 전환 뒤 실제 창.</summary>
    public readonly record struct TransitionMemo(PixelRect WindowBefore, PixelRect WindowAfter, PixelRect WorkArea, VerticalAnchor Anchor);

    /// <summary>전환 계획. RestoredFromMemo가 true이면 TargetWindow는 메모의 WindowBefore다.</summary>
    public readonly record struct TransitionPlan(PixelRect TargetWindow, VerticalAnchor Anchor, bool RestoredFromMemo);

    /// <summary>
    /// 창 높이를 newHeightPx로 바꾸는 전환을 계획한다. newHeightPx가 0 이하이면 ArgumentOutOfRangeException.
    /// (1) last가 있고 window == last.WindowAfter, work == last.WorkArea,
    ///     last.WindowBefore.Height == newHeightPx, last.WindowBefore.Width == window.Width를 모두 만족하면
    ///     직전 전환 앞의 사각형으로 정확히 되돌린다. 높이 검사는 다른 배율에서 만든 메모로
    ///     잘못된 크기를 복원하는 일을 막으므로 빼지 않는다.
    /// (2) 그 외에는 ChooseResizeAnchor와 ResizeWindow로 계산한다.
    /// </summary>
    public static TransitionPlan PlanTransition(PixelRect window, PixelRect work, int newHeightPx, int edgeTolerancePx, TransitionMemo? last)
    {
        if (newHeightPx <= 0)
            throw new ArgumentOutOfRangeException(nameof(newHeightPx), newHeightPx, "창 높이는 0보다 커야 합니다.");

        if (last is TransitionMemo memo
            && window == memo.WindowAfter
            && work == memo.WorkArea
            && memo.WindowBefore.Height == newHeightPx
            && memo.WindowBefore.Width == window.Width)
        {
            return new TransitionPlan(memo.WindowBefore, memo.Anchor, true);
        }

        var anchor = ChooseResizeAnchor(window, work, newHeightPx, edgeTolerancePx);
        return new TransitionPlan(ResizeWindow(window, newHeightPx, anchor, work), anchor, false);
    }

    /// <summary>
    /// 기준 모서리 쪽 변이 current에서 target으로 옮겨진 거리(px).
    /// Top이면 target.Top - current.Top, Bottom이면 target.Bottom - current.Bottom이다.
    /// 클램프나 메모 복원이 없으면 0이다.
    /// </summary>
    public static int AnchorEdgeShiftPx(PixelRect current, PixelRect target, VerticalAnchor anchor) =>
        anchor == VerticalAnchor.Top ? target.Top - current.Top : target.Bottom - current.Bottom;

    /// <summary>Below는 Top 고정, Above는 Bottom 고정에 대응한다.</summary>
    public static VerticalAnchor ToAnchor(AttachDirection direction) =>
        direction == AttachDirection.Below ? VerticalAnchor.Top : VerticalAnchor.Bottom;
}
