using DaouCalendarOverlay.Models;

namespace DaouCalendarOverlay.Services;

/// <summary>창(고정 캔버스) 안 카드의 세로 배치. OffsetPx = 창 위 변 → 카드 위 변, HeightPx = 카드 높이.</summary>
public readonly record struct CardPlacement(int OffsetPx, int HeightPx)
{
    /// <summary>창 사각형 안에서 이 배치가 가리키는 카드 사각형.</summary>
    public PixelRect InWindow(PixelRect window) =>
        new(window.Left, window.Top + OffsetPx, window.Right, window.Top + OffsetPx + HeightPx);
}

/// <summary>창의 현재 배율로 환산한 캔버스 치수(물리 px).</summary>
public readonly record struct CanvasMetrics(
    int WidthPx, int SlotOffsetPx, int MiniHeightPx, int ExpandedHeightPx, int AttachRegionPx, int EdgeTolerancePx)
{
    /// <summary>
    /// 각 DIP 상수를 DipToPx(…, dpiScale)로 환산한다.
    /// dpiScale이 부적합하면 DipToPx가 ArgumentOutOfRangeException을 던진다.
    /// </summary>
    public static CanvasMetrics FromScale(double dpiScale) => new(
        OverlayLayoutMetrics.DipToPx(OverlayLayoutMetrics.WindowWidthDip, dpiScale),
        OverlayLayoutMetrics.DipToPx(OverlayLayoutMetrics.BarSlotOffsetDip, dpiScale),
        OverlayLayoutMetrics.DipToPx(OverlayLayoutMetrics.MiniCardHeightDip, dpiScale),
        OverlayLayoutMetrics.DipToPx(OverlayLayoutMetrics.ExpandedCardHeightDip, dpiScale),
        OverlayLayoutMetrics.DipToPx(OverlayLayoutMetrics.AttachedRegionHeightDip, dpiScale),
        OverlayLayoutMetrics.DipToPx(OverlayLayoutMetrics.EdgeToleranceDip, dpiScale));

    /// <summary>창 높이(px). DipToPx(828)이 아니다. 반올림 때문에 영역이 창을 1px 넘는 것을 막는다.</summary>
    public int HeightPx => SlotOffsetPx + Math.Max(MiniHeightPx + AttachRegionPx, ExpandedHeightPx);

    /// <summary>막대 칸. 막대 모드의 카드 배치.</summary>
    public CardPlacement Slot => new(SlotOffsetPx, MiniHeightPx);

    /// <summary>막대 칸 위 변에서 아래로 자라는 펼친 카드.</summary>
    public CardPlacement ExpandedDown => new(SlotOffsetPx, ExpandedHeightPx);

    /// <summary>막대 칸 아래 변에서 위로 자라는 펼친 카드.</summary>
    public CardPlacement ExpandedUp => new(SlotOffsetPx + MiniHeightPx - ExpandedHeightPx, ExpandedHeightPx);
}

/// <summary>쉬는 상태의 창 사각형과 카드 배치.</summary>
public readonly record struct RestingLayout(PixelRect Window, CardPlacement Placement);

/// <summary>
/// 고정 캔버스 안 카드 배치(접기·펼치기·시작·DPI 변경), 목록을 붙일 방향·영역, 카드 사각형의 작업 영역 클램프를 정하는 순수 로직.
/// 모든 값은 물리 px이고 Right/Bottom은 배타다. WPF·WinForms 타입을 참조하지 않는다.
/// 창은 고정 캔버스이고 카드는 창 안에서만 움직이므로 접기·펼치기·붙이기에서 창을 옮기지 않는다.
/// </summary>
public static class OverlayLayoutPlanner
{
    /// <summary>
    /// 카드 사각형에 적용한다. 크기 보존 평행 이동, 사각형이 작업 영역보다 크면 work.Left/Top.
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
    /// 막대 카드 아래에 attachPx가 들어가면 Below, 아니고 위에 들어가면 Above다.
    /// 둘 다 안 들어가면 남은 공간이 큰 쪽이고 같으면 Below다.
    /// </summary>
    public static AttachDirection ChooseAttachDirection(PixelRect bar, PixelRect work, int attachPx)
    {
        if (bar.Bottom + attachPx <= work.Bottom)
            return AttachDirection.Below;

        if (bar.Top - attachPx >= work.Top)
            return AttachDirection.Above;

        var spaceBelow = work.Bottom - bar.Bottom;
        var spaceAbove = bar.Top - work.Top;
        return spaceAbove > spaceBelow ? AttachDirection.Above : AttachDirection.Below;
    }

    /// <summary>
    /// 막대(창의 Slot)에서 펼친 카드 배치를 정한다. slot = m.Slot.InWindow(window), E = m.ExpandedHeightPx.
    /// (1) slot.Top + E &lt;= work.Bottom 이면 m.ExpandedDown.
    /// (2) 아니고 slot.Bottom - E &gt;= work.Top 이면 m.ExpandedUp.
    /// (3) 둘 다 아니면 카드 위 변 t = Math.Clamp(E &lt; work.Height ? work.Bottom - E : work.Top, slot.Bottom - E, slot.Top),
    ///     OffsetPx = t - window.Top.
    /// 결과는 항상 m.ExpandedUp.OffsetPx &lt;= OffsetPx &lt;= m.ExpandedDown.OffsetPx (카드가 막대 칸을 포함하고 창 안에 있다).
    /// </summary>
    public static CardPlacement PlanExpandedPlacement(PixelRect window, PixelRect work, CanvasMetrics m)
    {
        var slot = m.Slot.InWindow(window);
        var e = m.ExpandedHeightPx;

        if (slot.Top + e <= work.Bottom)
            return m.ExpandedDown;

        if (slot.Bottom - e >= work.Top)
            return m.ExpandedUp;

        var t = Math.Clamp(e < work.Height ? work.Bottom - e : work.Top, slot.Bottom - e, slot.Top);
        return new CardPlacement(t - window.Top, e);
    }

    /// <summary>
    /// 카드 사각형이 정해진 펼침 상태(시작·DPI 변경)에서 막대 칸을 카드 위 끝 또는 아래 끝에 둔다.
    /// |card.Bottom - work.Bottom| &lt;= m.EdgeTolerancePx 이거나 card.Top + m.MiniHeightPx &gt; work.Bottom 이면 m.ExpandedUp,
    /// 아니면 m.ExpandedDown이다.
    /// </summary>
    public static CardPlacement PlanRestingExpandedPlacement(PixelRect card, PixelRect work, CanvasMetrics m)
    {
        if (Math.Abs(card.Bottom - work.Bottom) <= m.EdgeTolerancePx)
            return m.ExpandedUp;

        if (card.Top + m.MiniHeightPx > work.Bottom)
            return m.ExpandedUp;

        return m.ExpandedDown;
    }

    /// <summary>카드 사각형과 배치에서 창 사각형을 얻는다. 창 위 변은 음수나 작업 영역 밖일 수 있다(클램프하지 않는다).</summary>
    public static PixelRect WindowForCard(PixelRect card, CardPlacement placement, int windowWidthPx, int windowHeightPx) =>
        new(card.Left, card.Top - placement.OffsetPx, card.Left + windowWidthPx, card.Top - placement.OffsetPx + windowHeightPx);

    /// <summary>
    /// 저장된 카드 왼쪽 위(px)에서 쉬는 배치를 만든다.
    /// card = (cardTopLeft, 폭 m.WidthPx, 높이 = MiniBar ? m.MiniHeightPx : m.ExpandedHeightPx)
    /// → ClampWindowToWorkArea(card, work)
    /// → placement = MiniBar ? m.Slot : PlanRestingExpandedPlacement(clamped, work, m)
    /// → Window = WindowForCard(clamped, placement, m.WidthPx, m.HeightPx).
    /// </summary>
    public static RestingLayout PlanRestingLayout(PixelPoint cardTopLeft, OverlayDisplayMode mode, PixelRect work, CanvasMetrics m)
    {
        var cardHeight = mode == OverlayDisplayMode.MiniBar ? m.MiniHeightPx : m.ExpandedHeightPx;
        var card = new PixelRect(cardTopLeft.X, cardTopLeft.Y, cardTopLeft.X + m.WidthPx, cardTopLeft.Y + cardHeight);
        var clamped = ClampWindowToWorkArea(card, work);
        var placement = mode == OverlayDisplayMode.MiniBar ? m.Slot : PlanRestingExpandedPlacement(clamped, work, m);
        return new RestingLayout(WindowForCard(clamped, placement, m.WidthPx, m.HeightPx), placement);
    }

    /// <summary>
    /// 붙인 목록 영역(창 위 변 기준).
    /// Below: (m.SlotOffsetPx + m.MiniHeightPx, m.AttachRegionPx).
    /// Above: (m.SlotOffsetPx - m.AttachRegionPx, m.AttachRegionPx).
    /// </summary>
    public static CardPlacement AttachRegion(AttachDirection direction, CanvasMetrics m) =>
        direction == AttachDirection.Below
            ? new CardPlacement(m.SlotOffsetPx + m.MiniHeightPx, m.AttachRegionPx)
            : new CardPlacement(m.SlotOffsetPx - m.AttachRegionPx, m.AttachRegionPx);
}
