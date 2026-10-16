using DaouCalendarOverlay.Models;
using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

public sealed class OverlayLayoutPlannerTests
{
    private const int Inset = 7;
    private const int Tol = 2;
    private const int Seed = 20261015;
    private const int Cases = 500;

    private static readonly double[] Scales = { 1.0, 1.25, 1.5, 1.75, 2.0 };
    private static readonly PixelRect Work = new(0, 0, 1920, 1040);

    // 카드 사각형을 기준으로 창 사각형(카드 + 투명 여백)을 만든다.
    private static PixelRect WindowFromCard(PixelRect card, int m) => card.Deflate(-m);

    private static PixelRect Win(int left, int top, int width, int height) => new(left, top, left + width, top + height);

    // ChooseResizeAnchor

    [Theory]
    [InlineData(0, VerticalAnchor.Bottom)]
    [InlineData(2, VerticalAnchor.Bottom)]
    [InlineData(-2, VerticalAnchor.Bottom)]
    [InlineData(3, VerticalAnchor.Top)]
    public void ChooseResizeAnchor_BottomEdgeDistance_UsesTolerance(int gap, VerticalAnchor expected)
    {
        // 카드 아래 변이 work.Bottom - gap에 있고, 새 높이(48)는 위쪽 고정으로도 들어간다.
        var card = new PixelRect(100, Work.Bottom - gap - 48, 400, Work.Bottom - gap);

        var anchor = OverlayLayoutPlanner.ChooseResizeAnchor(card, Work, 48, Tol);

        Assert.Equal(expected, anchor);
    }

    [Fact]
    public void ChooseResizeAnchor_DoesNotFitWhenTopAnchored_ReturnsBottom()
    {
        // 아래 변과 거리 100px이지만 위쪽 고정으로 426px를 늘리면 work.Bottom을 넘는다.
        var card = new PixelRect(100, 900 - 48, 400, 900);

        var anchor = OverlayLayoutPlanner.ChooseResizeAnchor(card, Work, 426, Tol);

        Assert.Equal(VerticalAnchor.Bottom, anchor);
    }

    [Fact]
    public void ChooseResizeAnchor_FitsWhenTopAnchored_ReturnsTop()
    {
        var card = new PixelRect(100, 200, 400, 248);

        var anchor = OverlayLayoutPlanner.ChooseResizeAnchor(card, Work, 426, Tol);

        Assert.Equal(VerticalAnchor.Top, anchor);
    }

    // ResizeWindow

    [Fact]
    public void ResizeWindow_TopAnchor_KeepsTopAndHorizontalExtent()
    {
        var window = Win(100, 200, 325, 62);

        var result = OverlayLayoutPlanner.ResizeWindow(window, 440, VerticalAnchor.Top, Work, Inset);

        Assert.Equal(new PixelRect(100, 200, 425, 640), result);
    }

    [Fact]
    public void ResizeWindow_BottomAnchor_KeepsBottomAndHorizontalExtent()
    {
        var window = Win(100, 500, 325, 62);

        var result = OverlayLayoutPlanner.ResizeWindow(window, 440, VerticalAnchor.Bottom, Work, Inset);

        Assert.Equal(new PixelRect(100, 122, 425, 562), result);
    }

    [Fact]
    public void ResizeWindow_OutsideBounds_ClampsTopAnchorIntoBounds()
    {
        // bounds = work.Deflate(-7) = (-7,-7,1927,1047). Top 고정이면 Top 900 + 440 > 1047이므로 위로 밀린다.
        var window = Win(100, 900, 325, 62);

        var result = OverlayLayoutPlanner.ResizeWindow(window, 440, VerticalAnchor.Top, Work, Inset);

        Assert.Equal(1047, result.Bottom);
        Assert.Equal(440, result.Height);
    }

    [Fact]
    public void ResizeWindow_OutsideBounds_ClampsBottomAnchorIntoBounds()
    {
        // Bottom 고정이면 top = 100 - 440 = -340 이라 bounds.Top(-7) 밑으로 내려온다.
        var window = Win(100, 38, 325, 62);

        var result = OverlayLayoutPlanner.ResizeWindow(window, 440, VerticalAnchor.Bottom, Work, Inset);

        Assert.Equal(-Inset, result.Top);
        Assert.Equal(440, result.Height);
    }

    [Fact]
    public void ResizeWindow_HeightAtLeastBoundsHeight_PinsToBoundsTop()
    {
        var window = Win(100, 500, 325, 62);
        var boundsHeight = Work.Height + 2 * Inset;

        var result = OverlayLayoutPlanner.ResizeWindow(window, boundsHeight + 50, VerticalAnchor.Bottom, Work, Inset);

        Assert.Equal(Work.Top - Inset, result.Top);
    }

    [Fact]
    public void ResizeWindow_CardFlushToBottom_CollapseKeepsWindowBottom()
    {
        // 카드 아래 변이 work.Bottom에 붙은 창은 투명 여백만큼 작업 영역 밖에 있다.
        var window = new PixelRect(500, Work.Bottom + Inset - 440, 825, Work.Bottom + Inset);
        var anchor = OverlayLayoutPlanner.ChooseResizeAnchor(window.Deflate(Inset), Work, 48, Tol);

        var collapsed = OverlayLayoutPlanner.ResizeWindow(window, 62, anchor, Work, Inset);

        Assert.Equal(VerticalAnchor.Bottom, anchor);
        Assert.Equal(window.Bottom, collapsed.Bottom);
        Assert.Equal(62, collapsed.Height);
    }

    [Fact]
    public void ResizeWindow_InvalidArguments_Throw()
    {
        var window = Win(0, 0, 325, 62);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OverlayLayoutPlanner.ResizeWindow(window, 0, VerticalAnchor.Top, Work, Inset));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OverlayLayoutPlanner.ResizeWindow(window, -5, VerticalAnchor.Top, Work, Inset));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OverlayLayoutPlanner.ResizeWindow(window, 62, VerticalAnchor.Top, Work, -1));
    }

    // ClampWindowToWorkArea

    [Fact]
    public void ClampWindowToWorkArea_CardAlreadyInside_ReturnsSameEvenWhenWindowIsOutside()
    {
        // 카드가 work 네 변에 모두 닿는 최대 크기가 아니라, 왼쪽·위에 flush로 붙은 카드.
        var window = Win(-Inset, -Inset, 325, 440);

        var result = OverlayLayoutPlanner.ClampWindowToWorkArea(window, Work, Inset);

        Assert.Equal(window, result);
    }

    [Fact]
    public void ClampWindowToWorkArea_CardOutside_MovesOnlyUntilCardEdgeMatchesWorkEdge()
    {
        var window = Win(1800, 900, 325, 440);

        var result = OverlayLayoutPlanner.ClampWindowToWorkArea(window, Work, Inset);

        var card = result.Deflate(Inset);
        Assert.Equal(Work.Right, card.Right);
        Assert.Equal(Work.Bottom, card.Bottom);
        Assert.Equal(window.Width, result.Width);
        Assert.Equal(window.Height, result.Height);

        var upperLeft = OverlayLayoutPlanner.ClampWindowToWorkArea(Win(-500, -300, 325, 440), Work, Inset);
        Assert.Equal(Work.Left, upperLeft.Deflate(Inset).Left);
        Assert.Equal(Work.Top, upperLeft.Deflate(Inset).Top);
    }

    [Fact]
    public void ClampWindowToWorkArea_NegativeCoordinateMonitor()
    {
        var work = new PixelRect(-1920, 0, 0, 1040);

        var tooFarRight = OverlayLayoutPlanner.ClampWindowToWorkArea(Win(-100, 300, 325, 440), work, Inset);
        Assert.Equal(work.Right, tooFarRight.Deflate(Inset).Right);
        Assert.Equal(300, tooFarRight.Top);

        var inside = Win(-1000, 300, 325, 440);
        Assert.Equal(inside, OverlayLayoutPlanner.ClampWindowToWorkArea(inside, work, Inset));
    }

    [Fact]
    public void ClampWindowToWorkArea_LargerThanBounds_PinsToBoundsOrigin()
    {
        var work = new PixelRect(0, 0, 200, 300);
        var window = Win(50, 60, 325, 440);

        var result = OverlayLayoutPlanner.ClampWindowToWorkArea(window, work, Inset);

        Assert.Equal(work.Left - Inset, result.Left);
        Assert.Equal(work.Top - Inset, result.Top);
    }

    // ChooseAttachDirection

    [Fact]
    public void ChooseAttachDirection_FitsBelow_ReturnsBelow()
    {
        var card = new PixelRect(100, 100, 400, 148);

        Assert.Equal(AttachDirection.Below, OverlayLayoutPlanner.ChooseAttachDirection(card, Work, 384));
    }

    [Fact]
    public void ChooseAttachDirection_OnlyFitsAbove_ReturnsAbove()
    {
        var card = new PixelRect(100, 900 - 48, 400, 900);

        Assert.Equal(AttachDirection.Above, OverlayLayoutPlanner.ChooseAttachDirection(card, Work, 384));
    }

    [Fact]
    public void ChooseAttachDirection_FitsNeither_ReturnsLargerSide()
    {
        // 위 여백 340, 아래 여백 652 일 때 attach 700은 둘 다 안 들어간다.
        var lowerLarger = new PixelRect(100, 340, 400, 388);
        Assert.Equal(AttachDirection.Below, OverlayLayoutPlanner.ChooseAttachDirection(lowerLarger, Work, 700));

        var upperLarger = new PixelRect(100, 600, 400, 648);
        Assert.Equal(AttachDirection.Above, OverlayLayoutPlanner.ChooseAttachDirection(upperLarger, Work, 700));
    }

    [Fact]
    public void ChooseAttachDirection_Tie_ReturnsBelow()
    {
        // 위 여백 = 아래 여백 = 496, attach 600은 어느 쪽에도 안 들어간다.
        var card = new PixelRect(100, 496, 400, 544);

        Assert.Equal(AttachDirection.Below, OverlayLayoutPlanner.ChooseAttachDirection(card, Work, 600));
    }

    [Fact]
    public void ToAnchor_BelowIsTopAndAboveIsBottom()
    {
        Assert.Equal(VerticalAnchor.Top, OverlayLayoutPlanner.ToAnchor(AttachDirection.Below));
        Assert.Equal(VerticalAnchor.Bottom, OverlayLayoutPlanner.ToAnchor(AttachDirection.Above));
    }

    // 왕복 속성

    // 카드가 work 안에 있도록 무작위 위치를 고른다. 세로: 20%는 아래 변 flush, 10%는 위 변 flush.
    private static PixelRect RandomCard(Random rng, int cardWidth, int cardHeight)
    {
        var left = rng.Next(Work.Left, Work.Right - cardWidth + 1);
        var kind = rng.NextDouble();
        int top;
        if (kind < 0.20)
            top = Work.Bottom - cardHeight;
        else if (kind < 0.30)
            top = Work.Top;
        else
            top = rng.Next(Work.Top, Work.Bottom - cardHeight + 1);
        return new PixelRect(left, top, left + cardWidth, top + cardHeight);
    }

    [Fact]
    public void RoundTrip_CollapseThenExpand_RestoresOriginalWindow()
    {
        var rng = new Random(Seed);

        for (var i = 0; i < Cases; i++)
        {
            var scale = Scales[rng.Next(Scales.Length)];
            int Px(double dip) => OverlayLayoutMetrics.DipToPx(dip, scale);
            var m = Px(OverlayLayoutMetrics.WindowMarginDip);
            var expandedHeight = Px(OverlayLayoutMetrics.ExpandedWindowHeightDip);
            var width = Px(OverlayLayoutMetrics.WindowWidthDip);

            var card = RandomCard(rng, width - 2 * m, expandedHeight - 2 * m);
            var win = WindowFromCard(card, m);

            var anchor = OverlayLayoutPlanner.ChooseResizeAnchor(
                win.Deflate(m), Work, Px(OverlayLayoutMetrics.MiniCardHeightDip), Px(OverlayLayoutMetrics.EdgeToleranceDip));
            var mini = OverlayLayoutPlanner.ResizeWindow(win, Px(OverlayLayoutMetrics.MiniWindowHeightDip), anchor, Work, m);

            var anchor2 = OverlayLayoutPlanner.ChooseResizeAnchor(
                mini.Deflate(m), Work, Px(OverlayLayoutMetrics.ExpandedCardHeightDip), Px(OverlayLayoutMetrics.EdgeToleranceDip));
            var back = OverlayLayoutPlanner.ResizeWindow(mini, Px(OverlayLayoutMetrics.ExpandedWindowHeightDip), anchor2, Work, m);

            Assert.True(win == back, $"case {i} scale {scale}: win={win} mini={mini} back={back}");
        }
    }

    [Fact]
    public void RoundTrip_AttachThenDetach_RestoresBarWhenNotClamped()
    {
        var rng = new Random(Seed);
        var checkedCount = 0;

        for (var i = 0; i < Cases; i++)
        {
            var scale = Scales[rng.Next(Scales.Length)];
            int Px(double dip) => OverlayLayoutMetrics.DipToPx(dip, scale);
            var m = Px(OverlayLayoutMetrics.WindowMarginDip);
            var miniHeight = Px(OverlayLayoutMetrics.MiniWindowHeightDip);
            var width = Px(OverlayLayoutMetrics.WindowWidthDip);

            var card = RandomCard(rng, width - 2 * m, miniHeight - 2 * m);
            var mini = WindowFromCard(card, m);

            var dir = OverlayLayoutPlanner.ChooseAttachDirection(
                mini.Deflate(m), Work, Px(OverlayLayoutMetrics.AttachedRegionHeightDip));
            var anchor = OverlayLayoutPlanner.ToAnchor(dir);
            var attached = OverlayLayoutPlanner.ResizeWindow(
                mini,
                Px(OverlayLayoutMetrics.MiniWindowHeightDip + OverlayLayoutMetrics.AttachedRegionHeightDip),
                anchor, Work, m);

            var notClamped = anchor == VerticalAnchor.Top
                ? attached.Top == mini.Top
                : attached.Bottom == mini.Bottom;
            if (!notClamped)
                continue;

            checkedCount++;
            var detached = OverlayLayoutPlanner.ResizeWindow(attached, miniHeight, anchor, Work, m);
            Assert.True(mini == detached, $"case {i} scale {scale}: mini={mini} attached={attached} detached={detached}");
        }

        Assert.True(checkedCount >= 250, $"검사된 건수 {checkedCount}");
    }
}
