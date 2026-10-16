using DaouCalendarOverlay.Models;

namespace DaouCalendarOverlay.Services;

/// <summary>
/// 창 이동 중 제안된 사각형을 작업 영역 가장자리에 붙이는 순수 로직.
/// 기준은 눈에 보이는 카드 테두리(창에서 투명 여백만큼 안쪽)이고, 축마다 따로 판정한다.
/// 스냅된 창은 투명 여백만큼 작업 영역 밖으로 나갈 수 있으며 이것이 의도다.
/// </summary>
public static class OverlaySnap
{
    /// <summary>
    /// proposedWindow: WM_MOVING이 제안한 창 사각형(px).
    /// cardInsetPx: 창과 카드 테두리 사이 투명 여백(px, 4변 동일).
    /// workArea: 대상 모니터 작업 영역(px).
    /// thresholdPx: 0 이상.
    /// 반환값: 크기는 그대로이고 평행 이동만 한 창 사각형.
    /// </summary>
    public static PixelRect Snap(PixelRect proposedWindow, int cardInsetPx, PixelRect workArea, int thresholdPx)
    {
        if (cardInsetPx < 0)
            throw new ArgumentOutOfRangeException(nameof(cardInsetPx), cardInsetPx, "카드 여백은 0 이상이어야 합니다.");
        if (thresholdPx < 0)
            throw new ArgumentOutOfRangeException(nameof(thresholdPx), thresholdPx, "스냅 임계값은 0 이상이어야 합니다.");

        var card = proposedWindow.Deflate(cardInsetPx);

        var dx = PickShift(workArea.Left - card.Left, workArea.Right - card.Right, thresholdPx);
        var dy = PickShift(workArea.Top - card.Top, workArea.Bottom - card.Bottom, thresholdPx);

        return proposedWindow.Offset(dx, dy);
    }

    /// <summary>
    /// 앞쪽(Left/Top) 후보와 뒤쪽(Right/Bottom) 후보 중 임계값 안에서 |d|가 가장 작은 것을 고른다.
    /// 같으면 앞쪽, 후보가 없으면 0.
    /// </summary>
    private static int PickShift(long toLowEdge, long toHighEdge, int threshold)
    {
        var lowOk = Math.Abs(toLowEdge) <= threshold;
        var highOk = Math.Abs(toHighEdge) <= threshold;

        if (lowOk && highOk)
            return (int)(Math.Abs(toHighEdge) < Math.Abs(toLowEdge) ? toHighEdge : toLowEdge);
        if (lowOk)
            return (int)toLowEdge;
        if (highOk)
            return (int)toHighEdge;
        return 0;
    }
}
