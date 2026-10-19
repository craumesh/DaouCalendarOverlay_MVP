using DaouCalendarOverlay.Models;

namespace DaouCalendarOverlay.Services;

/// <summary>창이 붙어 있는 작업 영역 변. 여러 변이 동시에 붙을 수 있다.</summary>
[Flags]
public enum SnapEdges
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8
}

/// <summary>
/// 창 이동 중 제안된 사각형을 작업 영역 가장자리에 붙이는 순수 로직.
/// 창 사각형이 곧 카드 테두리의 바깥 사각형이고, 축마다 따로 판정한다.
/// </summary>
public static class OverlaySnap
{
    /// <summary>
    /// proposedWindow: WM_MOVING이 제안한 창 사각형(px).
    /// workArea: 대상 모니터 작업 영역(px).
    /// thresholdPx: 0 이상.
    /// 반환값: 크기는 그대로이고 평행 이동만 한 창 사각형.
    /// </summary>
    public static PixelRect Snap(PixelRect proposedWindow, PixelRect workArea, int thresholdPx)
    {
        if (thresholdPx < 0)
            throw new ArgumentOutOfRangeException(nameof(thresholdPx), thresholdPx, "스냅 임계값은 0 이상이어야 합니다.");

        var dx = PickShift(workArea.Left - proposedWindow.Left, workArea.Right - proposedWindow.Right, thresholdPx);
        var dy = PickShift(workArea.Top - proposedWindow.Top, workArea.Bottom - proposedWindow.Bottom, thresholdPx);

        return proposedWindow.Offset(dx, dy);
    }

    /// <summary>
    /// 창의 각 변이 작업 영역의 같은 쪽 변과 tolerancePx 이내이면 그 변을 돌려준다.
    /// Right/Bottom은 배타 좌표 그대로 비교한다. tolerancePx가 음수이면 ArgumentOutOfRangeException.
    /// </summary>
    public static SnapEdges DetectEdges(PixelRect window, PixelRect workArea, int tolerancePx)
    {
        if (tolerancePx < 0)
            throw new ArgumentOutOfRangeException(nameof(tolerancePx), tolerancePx, "허용 오차는 0 이상이어야 합니다.");

        var edges = SnapEdges.None;
        if (Math.Abs((long)window.Left - workArea.Left) <= tolerancePx)
            edges |= SnapEdges.Left;
        if (Math.Abs((long)window.Top - workArea.Top) <= tolerancePx)
            edges |= SnapEdges.Top;
        if (Math.Abs((long)window.Right - workArea.Right) <= tolerancePx)
            edges |= SnapEdges.Right;
        if (Math.Abs((long)window.Bottom - workArea.Bottom) <= tolerancePx)
            edges |= SnapEdges.Bottom;
        return edges;
    }

    /// <summary>
    /// 붙은 변을 "왼쪽·위·오른쪽·아래" 순서로 '·'(U+00B7)로 이어 표기한다. 없으면 "없음".
    /// </summary>
    public static string DescribeEdges(SnapEdges edges)
    {
        var names = new List<string>(4);
        if (edges.HasFlag(SnapEdges.Left))
            names.Add("왼쪽");
        if (edges.HasFlag(SnapEdges.Top))
            names.Add("위");
        if (edges.HasFlag(SnapEdges.Right))
            names.Add("오른쪽");
        if (edges.HasFlag(SnapEdges.Bottom))
            names.Add("아래");

        return names.Count == 0 ? "없음" : string.Join('·', names);
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
