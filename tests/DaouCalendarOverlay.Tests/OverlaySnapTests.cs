using DaouCalendarOverlay.Models;
using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

public sealed class OverlaySnapTests
{
    private const int Threshold = 12;
    private const int WinW = 311;
    private const int WinH = 426;

    private static readonly PixelRect Work = new(0, 0, 1920, 1032);

    private static PixelRect Win(int left, int top) => new(left, top, left + WinW, top + WinH);

    private static PixelRect SnapDefault(PixelRect window, PixelRect? work = null) =>
        OverlaySnap.Snap(window, work ?? Work, Threshold);

    // 가로 축

    [Theory]
    [InlineData(12, true)]
    [InlineData(13, false)]
    [InlineData(0, true)]
    [InlineData(-12, true)]
    [InlineData(-13, false)]
    public void Snap_LeftEdge_SnapsOnlyWithinThreshold(int leftOffset, bool snapped)
    {
        // 창 왼쪽 = work.Left + leftOffset (창 = 카드)
        var proposed = Win(leftOffset, 300);

        var result = SnapDefault(proposed);

        Assert.Equal(snapped ? Work.Left : proposed.Left, result.Left);
        Assert.Equal(proposed.Top, result.Top);
    }

    [Theory]
    [InlineData(12, true)]
    [InlineData(13, false)]
    public void Snap_RightEdge_SnapsOnlyWithinThreshold(int gapToRight, bool snapped)
    {
        var right = Work.Right - gapToRight;
        var proposed = Win(right - WinW, 300);

        var result = SnapDefault(proposed);

        Assert.Equal(snapped ? Work.Right - WinW : proposed.Left, result.Left);
        if (snapped)
            Assert.Equal(Work.Right, result.Right);
    }

    // 세로 축

    [Theory]
    [InlineData(12, true)]
    [InlineData(13, false)]
    public void Snap_TopEdge_SnapsOnlyWithinThreshold(int topOffset, bool snapped)
    {
        var proposed = Win(500, topOffset);

        var result = SnapDefault(proposed);

        Assert.Equal(snapped ? Work.Top : proposed.Top, result.Top);
        Assert.Equal(proposed.Left, result.Left);
    }

    [Theory]
    [InlineData(12, true)]
    [InlineData(13, false)]
    public void Snap_BottomEdge_SnapsOnlyWithinThreshold(int gapToBottom, bool snapped)
    {
        var bottom = Work.Bottom - gapToBottom;
        var proposed = Win(500, bottom - WinH);

        var result = SnapDefault(proposed);

        Assert.Equal(snapped ? Work.Bottom - WinH : proposed.Top, result.Top);
        if (snapped)
            Assert.Equal(Work.Bottom, result.Bottom);
    }

    [Fact]
    public void Snap_Corner_SnapsBothAxes()
    {
        // 창이 왼쪽 위 모서리에서 각각 10px, 11px 떨어짐
        var result = SnapDefault(Win(10, 11));

        Assert.Equal(Work.Left, result.Left);
        Assert.Equal(Work.Top, result.Top);
    }

    [Fact]
    public void Snap_BottomRightCorner_SnapsBothAxes()
    {
        var result = SnapDefault(Win(Work.Right - WinW + 5, Work.Bottom - WinH + 3));

        Assert.Equal(Work.Right, result.Right);
        Assert.Equal(Work.Bottom, result.Bottom);
    }

    [Fact]
    public void Snap_TopRightAndBottomLeftCorners_SnapBothAxes()
    {
        var topRight = SnapDefault(Win(Work.Right - WinW - 4, 6));
        Assert.Equal(Work.Right, topRight.Right);
        Assert.Equal(Work.Top, topRight.Top);

        var bottomLeft = SnapDefault(Win(9, Work.Bottom - WinH - 2));
        Assert.Equal(Work.Left, bottomLeft.Left);
        Assert.Equal(Work.Bottom, bottomLeft.Bottom);
    }

    [Fact]
    public void Snap_NegativeCoordinateMonitor_SnapsLeftAndRight()
    {
        var work = new PixelRect(-1920, 0, 0, 1032);

        var left = SnapDefault(Win(-1920 + 4, 200), work);
        Assert.Equal(-1920, left.Left);

        var right = SnapDefault(Win(0 - 6 - WinW, 200), work);
        Assert.Equal(0, right.Right);
        Assert.Equal(-WinW, right.Left);
    }

    [Fact]
    public void Snap_NoCandidate_ReturnsSameRect()
    {
        var proposed = Win(700, 300);

        Assert.Equal(proposed, SnapDefault(proposed));
    }

    // 양쪽 모두 임계 안

    [Fact]
    public void Snap_BothSidesInThreshold_PicksCloserSide()
    {
        // 창 폭 311, 작업 영역 폭 321 -> 여유 10px
        var work = new PixelRect(0, 0, 321, 1032);

        // Left=2: dL=-2, dR=+8 -> Left
        var nearLeft = SnapDefault(Win(2, 300), work);
        Assert.Equal(0, nearLeft.Left);

        // Left=8: dL=-8, dR=+2 -> Right
        var nearRight = SnapDefault(Win(8, 300), work);
        Assert.Equal(work.Right, nearRight.Right);
    }

    [Fact]
    public void Snap_BothSidesEqualDistance_PicksLeft()
    {
        var work = new PixelRect(0, 0, 321, 1032);

        // Left=5: dL=-5, dR=+5 -> 동률이면 Left
        var result = SnapDefault(Win(5, 300), work);

        Assert.Equal(work.Left, result.Left);
    }

    [Fact]
    public void Snap_VerticalBothSidesInThreshold_PicksCloserThenTop()
    {
        // 창 높이 426, 작업 영역 높이 436 -> 여유 10px
        var work = new PixelRect(0, 0, 1920, 436);

        var nearTop = SnapDefault(Win(500, 2), work);
        Assert.Equal(work.Top, nearTop.Top);

        var nearBottom = SnapDefault(Win(500, 8), work);
        Assert.Equal(work.Bottom, nearBottom.Bottom);

        var tie = SnapDefault(Win(500, 5), work);
        Assert.Equal(work.Top, tie.Top);
    }

    // DipToPx

    [Theory]
    [InlineData(7, 1.25, 9)]
    [InlineData(7, 1.5, 10)]
    [InlineData(12, 1.5, 18)]
    [InlineData(7, 2.0, 14)]
    [InlineData(440, 1.0, 440)]
    public void DipToPx_ReturnsExpectedPixels(double dip, double scale, int expected)
    {
        Assert.Equal(expected, OverlayLayoutMetrics.DipToPx(dip, scale));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void DipToPx_InvalidScale_Throws(double scale)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OverlayLayoutMetrics.DipToPx(7, scale));
    }

    // 인자 검증

    [Fact]
    public void Snap_NegativeThreshold_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OverlaySnap.Snap(Win(0, 0), Work, -1));
    }

    // DetectEdges

    [Fact]
    public void DetectEdges_ExactMatch_ReportsEachEdge()
    {
        Assert.Equal(SnapEdges.Left | SnapEdges.Top, OverlaySnap.DetectEdges(Win(Work.Left, Work.Top), Work, 0));
        Assert.Equal(
            SnapEdges.Right | SnapEdges.Bottom,
            OverlaySnap.DetectEdges(Win(Work.Right - WinW, Work.Bottom - WinH), Work, 0));
        Assert.Equal(SnapEdges.Top, OverlaySnap.DetectEdges(Win(500, Work.Top), Work, 0));
        Assert.Equal(SnapEdges.Left, OverlaySnap.DetectEdges(Win(Work.Left, 300), Work, 0));
        Assert.Equal(SnapEdges.None, OverlaySnap.DetectEdges(Win(500, 300), Work, 0));
    }

    [Fact]
    public void DetectEdges_ComparesExclusiveRightAndBottomAsIs()
    {
        // Right/Bottom이 배타 좌표(work.Right/work.Bottom)와 같을 때만 정확히 일치다.
        var flush = new PixelRect(Work.Right - WinW, Work.Bottom - WinH, Work.Right, Work.Bottom);
        Assert.Equal(SnapEdges.Right | SnapEdges.Bottom, OverlaySnap.DetectEdges(flush, Work, 0));
        Assert.Equal(SnapEdges.None, OverlaySnap.DetectEdges(flush.Offset(-1, -1), Work, 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(12)]
    public void DetectEdges_ToleranceBoundary_IsInclusive(int tol)
    {
        // 각 변이 tol만큼 떨어지면 붙음, tol + 1이면 안 붙음. 바깥쪽(음의 방향)도 같다.
        Assert.Equal(SnapEdges.Left, OverlaySnap.DetectEdges(Win(Work.Left + tol, 300), Work, tol));
        Assert.Equal(SnapEdges.None, OverlaySnap.DetectEdges(Win(Work.Left + tol + 1, 300), Work, tol));
        Assert.Equal(SnapEdges.Left, OverlaySnap.DetectEdges(Win(Work.Left - tol, 300), Work, tol));
        Assert.Equal(SnapEdges.None, OverlaySnap.DetectEdges(Win(Work.Left - tol - 1, 300), Work, tol));

        Assert.Equal(SnapEdges.Top, OverlaySnap.DetectEdges(Win(500, Work.Top + tol), Work, tol));
        Assert.Equal(SnapEdges.None, OverlaySnap.DetectEdges(Win(500, Work.Top + tol + 1), Work, tol));

        Assert.Equal(SnapEdges.Right, OverlaySnap.DetectEdges(Win(Work.Right - WinW - tol, 300), Work, tol));
        Assert.Equal(SnapEdges.None, OverlaySnap.DetectEdges(Win(Work.Right - WinW - tol - 1, 300), Work, tol));

        Assert.Equal(SnapEdges.Bottom, OverlaySnap.DetectEdges(Win(500, Work.Bottom - WinH - tol), Work, tol));
        Assert.Equal(SnapEdges.None, OverlaySnap.DetectEdges(Win(500, Work.Bottom - WinH - tol - 1), Work, tol));
    }

    [Fact]
    public void DetectEdges_NegativeTolerance_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OverlaySnap.DetectEdges(Win(0, 0), Work, -1));
    }

    // DescribeEdges

    [Fact]
    public void DescribeEdges_None_ReturnsNone()
    {
        Assert.Equal("없음", OverlaySnap.DescribeEdges(SnapEdges.None));
    }

    [Fact]
    public void DescribeEdges_TopRight_JoinsWithMiddleDot()
    {
        Assert.Equal("위·오른쪽", OverlaySnap.DescribeEdges(SnapEdges.Top | SnapEdges.Right));
        Assert.Equal("왼쪽", OverlaySnap.DescribeEdges(SnapEdges.Left));
    }

    [Fact]
    public void DescribeEdges_AllEdges_KeepsLeftTopRightBottomOrder()
    {
        var all = SnapEdges.Bottom | SnapEdges.Right | SnapEdges.Top | SnapEdges.Left;

        Assert.Equal("왼쪽·위·오른쪽·아래", OverlaySnap.DescribeEdges(all));
    }

    // 속성 시험

    [Fact]
    public void Snap_RandomProposals_PreserveSizeBoundMoveAndExactEdges()
    {
        var rng = new Random(20261015);
        double[] scales = { 1.0, 1.25, 1.5, 1.75, 2.0 };
        double[] heightsDip =
        {
            OverlayLayoutMetrics.ExpandedCardHeightDip,
            OverlayLayoutMetrics.MiniCardHeightDip
        };

        // 반복 수는 기존 1000건 그대로다(spec의 최소 500건 이상).
        for (var i = 0; i < 1000; i++)
        {
            var scale = scales[rng.Next(scales.Length)];
            var threshold = OverlayLayoutMetrics.DipToPx(OverlayLayoutMetrics.SnapThresholdDip, scale);
            var winW = OverlayLayoutMetrics.DipToPx(OverlayLayoutMetrics.WindowWidthDip, scale);
            var winH = OverlayLayoutMetrics.DipToPx(heightsDip[rng.Next(heightsDip.Length)], scale);

            var workLeft = rng.Next(-4000, 2000);
            var workTop = rng.Next(-1000, 1000);
            var work = new PixelRect(
                workLeft,
                workTop,
                workLeft + rng.Next(800, 3841),
                workTop + rng.Next(600, 2161));

            var left = PickCoordinate(rng, work.Left, work.Right - winW, work.Left, work.Right - winW, threshold);
            var top = PickCoordinate(rng, work.Top, work.Bottom - winH, work.Top, work.Bottom - winH, threshold);
            var proposed = new PixelRect(left, top, left + winW, top + winH);

            var result = OverlaySnap.Snap(proposed, work, threshold);

            Assert.Equal(proposed.Width, result.Width);
            Assert.Equal(proposed.Height, result.Height);

            var dx = result.Left - proposed.Left;
            var dy = result.Top - proposed.Top;
            Assert.True(Math.Abs(dx) <= threshold, $"case {i}: |dx|={Math.Abs(dx)} > {threshold}");
            Assert.True(Math.Abs(dy) <= threshold, $"case {i}: |dy|={Math.Abs(dy)} > {threshold}");

            if (dx != 0)
                Assert.True(result.Left == work.Left || result.Right == work.Right, $"case {i}: 가로 변 불일치");
            if (dy != 0)
                Assert.True(result.Top == work.Top || result.Bottom == work.Bottom, $"case {i}: 세로 변 불일치");
        }
    }

    // 작업 영역 변 근처 +-(threshold*3) 또는 내부 무작위
    private static int PickCoordinate(Random rng, int lowEdge, int highEdge, int lowBase, int highBase, int threshold)
    {
        var span = threshold * 3;
        switch (rng.Next(3))
        {
            case 0:
                return lowBase + rng.Next(-span, span + 1);
            case 1:
                return highBase + rng.Next(-span, span + 1);
            default:
                return rng.Next(lowEdge, Math.Max(lowEdge + 1, highEdge));
        }
    }
}
