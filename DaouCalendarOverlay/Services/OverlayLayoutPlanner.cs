using DaouCalendarOverlay.Models;

namespace DaouCalendarOverlay.Services;

/// <summary>
/// 접기·펼치기 기준 모서리, 목록을 붙일 방향, 새 창 사각형(작업 영역 클램프 포함)을 정하는 순수 로직.
/// 모든 값은 물리 px이고 Right/Bottom은 배타다. WPF·WinForms 타입을 참조하지 않는다.
/// 클램프 기준은 창이 아니라 눈에 보이는 카드다. 가장자리에 붙은 카드의 창은 투명 여백(cardInsetPx)만큼
/// 작업 영역 밖에 있으므로 창 허용 범위는 work.Deflate(-cardInsetPx)다.
/// </summary>
public static class OverlayLayoutPlanner
{
    /// <summary>
    /// 접기·펼치기의 기준 모서리를 정한다. card는 눈에 보이는 카드 사각형이다.
    /// (a) |card.Bottom - work.Bottom| &lt;= edgeTolerancePx 이거나
    /// (b) card.Top + newCardHeightPx &gt; work.Bottom (위쪽 고정으로는 안 들어감)이면 Bottom, 아니면 Top이다.
    /// </summary>
    public static VerticalAnchor ChooseResizeAnchor(PixelRect card, PixelRect work, int newCardHeightPx, int edgeTolerancePx)
    {
        if (Math.Abs(card.Bottom - work.Bottom) <= edgeTolerancePx)
            return VerticalAnchor.Bottom;

        if (card.Top + newCardHeightPx > work.Bottom)
            return VerticalAnchor.Bottom;

        return VerticalAnchor.Top;
    }

    /// <summary>
    /// 가로는 그대로 두고 anchor 쪽 변을 고정해 높이를 newHeightPx로 바꾼다.
    /// 그다음 세로를 work.Deflate(-cardInsetPx) 안으로 클램프한다.
    /// newHeightPx가 그 범위 높이 이상이면 Top은 범위의 Top이다.
    /// </summary>
    public static PixelRect ResizeWindow(PixelRect window, int newHeightPx, VerticalAnchor anchor, PixelRect work, int cardInsetPx)
    {
        if (newHeightPx <= 0)
            throw new ArgumentOutOfRangeException(nameof(newHeightPx), newHeightPx, "창 높이는 0보다 커야 합니다.");
        if (cardInsetPx < 0)
            throw new ArgumentOutOfRangeException(nameof(cardInsetPx), cardInsetPx, "투명 여백은 0 이상이어야 합니다.");

        var bounds = work.Deflate(-cardInsetPx);
        var top = anchor == VerticalAnchor.Top ? window.Top : window.Bottom - newHeightPx;

        if (newHeightPx >= bounds.Height)
            top = bounds.Top;
        else
            top = Math.Clamp(top, bounds.Top, bounds.Bottom - newHeightPx);

        return new PixelRect(window.Left, top, window.Right, top + newHeightPx);
    }

    /// <summary>
    /// 창 크기를 보존한 채 평행 이동해 카드가 작업 영역 안에 들어오게 한다.
    /// 허용 범위는 work.Deflate(-cardInsetPx)이고, 창이 범위보다 크면 범위의 Left/Top에 맞춘다.
    /// </summary>
    public static PixelRect ClampWindowToWorkArea(PixelRect window, PixelRect work, int cardInsetPx)
    {
        if (cardInsetPx < 0)
            throw new ArgumentOutOfRangeException(nameof(cardInsetPx), cardInsetPx, "투명 여백은 0 이상이어야 합니다.");

        var bounds = work.Deflate(-cardInsetPx);

        var left = window.Width >= bounds.Width
            ? bounds.Left
            : Math.Clamp(window.Left, bounds.Left, bounds.Right - window.Width);
        var top = window.Height >= bounds.Height
            ? bounds.Top
            : Math.Clamp(window.Top, bounds.Top, bounds.Bottom - window.Height);

        return window.Offset(left - window.Left, top - window.Top);
    }

    /// <summary>
    /// 막대 카드 아래에 attachPx가 들어가면 Below, 아니고 위에 들어가면 Above다.
    /// 둘 다 안 들어가면 남은 공간이 큰 쪽이고 같으면 Below다.
    /// </summary>
    public static AttachDirection ChooseAttachDirection(PixelRect card, PixelRect work, int attachPx)
    {
        if (card.Bottom + attachPx <= work.Bottom)
            return AttachDirection.Below;

        if (card.Top - attachPx >= work.Top)
            return AttachDirection.Above;

        var spaceBelow = work.Bottom - card.Bottom;
        var spaceAbove = card.Top - work.Top;
        return spaceAbove > spaceBelow ? AttachDirection.Above : AttachDirection.Below;
    }

    /// <summary>Below는 Top 고정, Above는 Bottom 고정에 대응한다.</summary>
    public static VerticalAnchor ToAnchor(AttachDirection direction) =>
        direction == AttachDirection.Below ? VerticalAnchor.Top : VerticalAnchor.Bottom;
}
