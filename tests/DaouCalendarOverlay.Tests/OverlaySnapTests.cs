using DaouCalendarOverlay.Models;
using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

public sealed class OverlaySnapTests
{
    private const int Inset = 7;
    private const int Threshold = 12;
    private const int WinW = 325;
    private const int WinH = 440;

    private static readonly PixelRect Work = new(0, 0, 1920, 1040);

    private static PixelRect Win(int left, int top) => new(left, top, left + WinW, top + WinH);

    private static PixelRect SnapDefault(PixelRect window, PixelRect? work = null) =>
        OverlaySnap.Snap(window, Inset, work ?? Work, Threshold);

    // 가로 축

    [Theory]
    [InlineData(12, true)]
    [InlineData(13, false)]
    [InlineData(0, true)]
    [InlineData(-12, true)]
    [InlineData(-13, false)]
    public void Snap_LeftEdge_SnapsOnlyWithinThreshold(int cardLeftOffset, bool snapped)
    {
        // 카드 왼쪽 = work.Left + cardLeftOffset (창 Left = 카드 Left - inset)
        var proposed = Win(cardLeftOffset - Inset, 300);

        var result = SnapDefault(proposed);

        Assert.Equal(snapped ? Work.Left - Inset : proposed.Left, result.Left);
        Assert.Equal(proposed.Top, result.Top);
    }

    [Theory]
    [InlineData(12, true)]
    [InlineData(13, false)]
    public void Snap_RightEdge_SnapsOnlyWithinThreshold(int gapToRight, bool snapped)
    {
        // 카드 오른쪽 = work.Right - gapToRight
        var cardRight = Work.Right - gapToRight;
        var proposed = Win(cardRight + Inset - WinW, 300);

        var result = SnapDefault(proposed);

        Assert.Equal(snapped ? Work.Right + Inset - WinW : proposed.Left, result.Left);
        if (snapped)
            Assert.Equal(Work.Right, result.Deflate(Inset).Right);
    }

    // 세로 축

    [Theory]
    [InlineData(12, true)]
    [InlineData(13, false)]
    public void Snap_TopEdge_SnapsOnlyWithinThreshold(int cardTopOffset, bool snapped)
    {
        var proposed = Win(500, cardTopOffset - Inset);

        var result = SnapDefault(proposed);

        Assert.Equal(snapped ? Work.Top - Inset : proposed.Top, result.Top);
        Assert.Equal(proposed.Left, result.Left);
    }

    [Theory]
    [InlineData(12, true)]
    [InlineData(13, false)]
    public void Snap_BottomEdge_SnapsOnlyWithinThreshold(int gapToBottom, bool snapped)
    {
        var cardBottom = Work.Bottom - gapToBottom;
        var proposed = Win(500, cardBottom + Inset - WinH);

        var result = SnapDefault(proposed);

        Assert.Equal(snapped ? Work.Bottom + Inset - WinH : proposed.Top, result.Top);
        if (snapped)
            Assert.Equal(Work.Bottom, result.Deflate(Inset).Bottom);
    }

    [Fact]
    public void Snap_Corner_SnapsBothAxes()
    {
        // 카드가 왼쪽 위 모서리에서 각각 10px, 11px 떨어짐
        var proposed = Win(10 - Inset, 11 - Inset);

        var result = SnapDefault(proposed);

        Assert.Equal(Work.Left - Inset, result.Left);
        Assert.Equal(Work.Top - Inset, result.Top);
    }

    [Fact]
    public void Snap_BottomRightCorner_SnapsBothAxes()
    {
        var proposed = Win(Work.Right - WinW + 5, Work.Bottom - WinH + 3);

        var result = SnapDefault(proposed);

        var card = result.Deflate(Inset);
        Assert.Equal(Work.Right, card.Right);
        Assert.Equal(Work.Bottom, card.Bottom);
    }

    [Fact]
    public void Snap_NegativeCoordinateMonitor_SnapsLeftAndRight()
    {
        var work = new PixelRect(-1920, 0, 0, 1040);

        var left = SnapDefault(Win(-1920 + 4 - Inset, 200), work);
        Assert.Equal(-1920 - Inset, left.Left);

        var right = SnapDefault(Win(0 - 6 + Inset - WinW, 200), work);
        Assert.Equal(0, right.Deflate(Inset).Right);
        Assert.Equal(Inset - WinW, right.Left);
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
        // 카드 폭 311, 작업 영역 폭 321 -> 여유 10px
        var work = new PixelRect(0, 0, 321, 1040);

        // 카드 Left=2: dL=-2, dR=+8 -> Left
        var nearLeft = SnapDefault(Win(2 - Inset, 300), work);
        Assert.Equal(0 - Inset, nearLeft.Left);

        // 카드 Left=8: dL=-8, dR=+2 -> Right
        var nearRight = SnapDefault(Win(8 - Inset, 300), work);
        Assert.Equal(work.Right, nearRight.Deflate(Inset).Right);
    }

    [Fact]
    public void Snap_BothSidesEqualDistance_PicksLeft()
    {
        var work = new PixelRect(0, 0, 321, 1040);

        // 카드 Left=5: dL=-5, dR=+5 -> 동률이면 Left
        var result = SnapDefault(Win(5 - Inset, 300), work);

        Assert.Equal(work.Left - Inset, result.Left);
    }

    [Fact]
    public void Snap_VerticalBothSidesInThreshold_PicksCloserThenTop()
    {
        // 카드 높이 426, 작업 영역 높이 436 -> 여유 10px
        var work = new PixelRect(0, 0, 1920, 436);

        var nearTop = SnapDefault(Win(500, 2 - Inset), work);
        Assert.Equal(work.Top - Inset, nearTop.Top);

        var nearBottom = SnapDefault(Win(500, 8 - Inset), work);
        Assert.Equal(work.Bottom, nearBottom.Deflate(Inset).Bottom);

        var tie = SnapDefault(Win(500, 5 - Inset), work);
        Assert.Equal(work.Top - Inset, tie.Top);
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
        Assert.Throws<ArgumentOutOfRangeException>(() => OverlaySnap.Snap(Win(0, 0), Inset, Work, -1));
    }

    [Fact]
    public void Snap_NegativeInset_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OverlaySnap.Snap(Win(0, 0), -1, Work, Threshold));
    }

    // 속성 시험

    [Fact]
    public void Snap_RandomProposals_PreserveSizeBoundMoveAndExactEdges()
    {
        var rng = new Random(20261015);
        double[] scales = { 1.0, 1.25, 1.5, 1.75, 2.0 };
        int[] heightsDip = { 440, 62 };

        for (var i = 0; i < 1000; i++)
        {
            var scale = scales[rng.Next(scales.Length)];
            var inset = OverlayLayoutMetrics.DipToPx(7, scale);
            var threshold = OverlayLayoutMetrics.DipToPx(12, scale);
            var winW = OverlayLayoutMetrics.DipToPx(325, scale);
            var winH = OverlayLayoutMetrics.DipToPx(heightsDip[rng.Next(heightsDip.Length)], scale);

            var workLeft = rng.Next(-4000, 2000);
            var workTop = rng.Next(-1000, 1000);
            var work = new PixelRect(
                workLeft,
                workTop,
                workLeft + rng.Next(800, 3841),
                workTop + rng.Next(600, 2161));

            var cardW = winW - 2 * inset;
            var cardH = winH - 2 * inset;

            var cardLeft = PickCoordinate(rng, work.Left, work.Right - cardW, work.Left, work.Right - cardW, threshold);
            var cardTop = PickCoordinate(rng, work.Top, work.Bottom - cardH, work.Top, work.Bottom - cardH, threshold);
            var proposed = new PixelRect(cardLeft - inset, cardTop - inset, cardLeft - inset + winW, cardTop - inset + winH);

            var result = OverlaySnap.Snap(proposed, inset, work, threshold);

            Assert.Equal(proposed.Width, result.Width);
            Assert.Equal(proposed.Height, result.Height);

            var dx = result.Left - proposed.Left;
            var dy = result.Top - proposed.Top;
            Assert.True(Math.Abs(dx) <= threshold, $"case {i}: |dx|={Math.Abs(dx)} > {threshold}");
            Assert.True(Math.Abs(dy) <= threshold, $"case {i}: |dy|={Math.Abs(dy)} > {threshold}");

            var card = result.Deflate(inset);
            if (dx != 0)
                Assert.True(card.Left == work.Left || card.Right == work.Right, $"case {i}: 가로 변 불일치");
            if (dy != 0)
                Assert.True(card.Top == work.Top || card.Bottom == work.Bottom, $"case {i}: 세로 변 불일치");
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
