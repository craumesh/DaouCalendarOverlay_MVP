using DaouCalendarOverlay.Models;
using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

public sealed class OverlayLayoutPlannerTests
{
    private const int Tol = 2;
    private const int Seed = 20261015;
    private const int Cases = 500;

    private static readonly double[] Scales = { 1.0, 1.25, 1.5, 1.75, 2.0 };
    private static readonly PixelRect Work = new(0, 0, 1920, 1040);

    private static PixelRect Win(int left, int top, int width, int height) => new(left, top, left + width, top + height);

    // ChooseResizeAnchor

    [Theory]
    [InlineData(0, VerticalAnchor.Bottom)]
    [InlineData(2, VerticalAnchor.Bottom)]
    [InlineData(-2, VerticalAnchor.Bottom)]
    [InlineData(3, VerticalAnchor.Top)]
    public void ChooseResizeAnchor_BottomEdgeDistance_UsesTolerance(int gap, VerticalAnchor expected)
    {
        // 창 아래 변이 work.Bottom - gap에 있고, 새 높이(48)는 위쪽 고정으로도 들어간다.
        var window = new PixelRect(100, Work.Bottom - gap - 48, 400, Work.Bottom - gap);

        var anchor = OverlayLayoutPlanner.ChooseResizeAnchor(window, Work, 48, Tol);

        Assert.Equal(expected, anchor);
    }

    [Fact]
    public void ChooseResizeAnchor_DoesNotFitWhenTopAnchored_ReturnsBottom()
    {
        // 아래 변과 거리 100px이지만 위쪽 고정으로 426px를 늘리면 work.Bottom을 넘는다.
        var window = new PixelRect(100, 900 - 48, 400, 900);

        var anchor = OverlayLayoutPlanner.ChooseResizeAnchor(window, Work, 426, Tol);

        Assert.Equal(VerticalAnchor.Bottom, anchor);
    }

    [Fact]
    public void ChooseResizeAnchor_FitsWhenTopAnchored_ReturnsTop()
    {
        var window = new PixelRect(100, 200, 400, 248);

        var anchor = OverlayLayoutPlanner.ChooseResizeAnchor(window, Work, 426, Tol);

        Assert.Equal(VerticalAnchor.Top, anchor);
    }

    // ResizeWindow

    [Fact]
    public void ResizeWindow_TopAnchor_KeepsTopAndHorizontalExtent()
    {
        var window = Win(100, 200, 311, 48);

        var result = OverlayLayoutPlanner.ResizeWindow(window, 426, VerticalAnchor.Top, Work);

        Assert.Equal(new PixelRect(100, 200, 411, 626), result);
    }

    [Fact]
    public void ResizeWindow_BottomAnchor_KeepsBottomAndHorizontalExtent()
    {
        var window = Win(100, 500, 311, 48);

        var result = OverlayLayoutPlanner.ResizeWindow(window, 426, VerticalAnchor.Bottom, Work);

        Assert.Equal(new PixelRect(100, 122, 411, 548), result);
    }

    [Fact]
    public void ResizeWindow_OutsideBounds_ClampsTopAnchorIntoBounds()
    {
        // bounds = work = (0,0,1920,1040). Top 고정이면 Top 900 + 426 > 1040이므로 위로 밀린다.
        var window = Win(100, 900, 311, 48);

        var result = OverlayLayoutPlanner.ResizeWindow(window, 426, VerticalAnchor.Top, Work);

        Assert.Equal(1040, result.Bottom);
        Assert.Equal(426, result.Height);
    }

    [Fact]
    public void ResizeWindow_OutsideBounds_ClampsBottomAnchorIntoBounds()
    {
        // Bottom 고정이면 top = 86 - 426 = -340 이라 work.Top(0) 밑으로 내려온다.
        var window = Win(100, 38, 311, 48);

        var result = OverlayLayoutPlanner.ResizeWindow(window, 426, VerticalAnchor.Bottom, Work);

        Assert.Equal(Work.Top, result.Top);
        Assert.Equal(426, result.Height);
    }

    [Fact]
    public void ResizeWindow_HeightAtLeastBoundsHeight_PinsToBoundsTop()
    {
        var window = Win(100, 500, 311, 48);

        var result = OverlayLayoutPlanner.ResizeWindow(window, Work.Height + 50, VerticalAnchor.Bottom, Work);

        Assert.Equal(Work.Top, result.Top);
    }

    [Fact]
    public void ResizeWindow_FlushToBottom_CollapseKeepsWindowBottom()
    {
        // 아래 변이 work.Bottom에 붙은 창을 접으면 아래 변이 그대로다.
        var window = new PixelRect(500, Work.Bottom - 426, 811, Work.Bottom);
        var anchor = OverlayLayoutPlanner.ChooseResizeAnchor(window, Work, 48, Tol);

        var collapsed = OverlayLayoutPlanner.ResizeWindow(window, 48, anchor, Work);

        Assert.Equal(VerticalAnchor.Bottom, anchor);
        Assert.Equal(window.Bottom, collapsed.Bottom);
        Assert.Equal(48, collapsed.Height);
    }

    [Fact]
    public void ResizeWindow_InvalidArguments_Throw()
    {
        var window = Win(0, 0, 311, 48);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OverlayLayoutPlanner.ResizeWindow(window, 0, VerticalAnchor.Top, Work));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OverlayLayoutPlanner.ResizeWindow(window, -5, VerticalAnchor.Top, Work));
        // 예전 inset 인자 검사 자리. 남은 높이 검사를 Bottom 기준·하한 극값으로도 확인한다.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OverlayLayoutPlanner.ResizeWindow(window, int.MinValue, VerticalAnchor.Bottom, Work));
    }

    // ClampWindowToWorkArea

    [Fact]
    public void ClampWindowToWorkArea_WindowAlreadyInside_ReturnsSame()
    {
        // 왼쪽·위에 flush로 붙은 창.
        var window = Win(0, 0, 311, 426);

        var result = OverlayLayoutPlanner.ClampWindowToWorkArea(window, Work);

        Assert.Equal(window, result);
    }

    [Fact]
    public void ClampWindowToWorkArea_WindowOutside_MovesOnlyUntilEdgeMatchesWorkEdge()
    {
        var window = Win(1800, 900, 311, 426);

        var result = OverlayLayoutPlanner.ClampWindowToWorkArea(window, Work);

        Assert.Equal(Work.Right, result.Right);
        Assert.Equal(Work.Bottom, result.Bottom);
        Assert.Equal(window.Width, result.Width);
        Assert.Equal(window.Height, result.Height);

        var upperLeft = OverlayLayoutPlanner.ClampWindowToWorkArea(Win(-500, -300, 311, 426), Work);
        Assert.Equal(Work.Left, upperLeft.Left);
        Assert.Equal(Work.Top, upperLeft.Top);
    }

    [Fact]
    public void ClampWindowToWorkArea_NegativeCoordinateMonitor()
    {
        var work = new PixelRect(-1920, 0, 0, 1040);

        var tooFarRight = OverlayLayoutPlanner.ClampWindowToWorkArea(Win(-100, 300, 311, 426), work);
        Assert.Equal(work.Right, tooFarRight.Right);
        Assert.Equal(300, tooFarRight.Top);

        var inside = Win(-1000, 300, 311, 426);
        Assert.Equal(inside, OverlayLayoutPlanner.ClampWindowToWorkArea(inside, work));
    }

    [Fact]
    public void ClampWindowToWorkArea_LargerThanBounds_PinsToBoundsOrigin()
    {
        var work = new PixelRect(0, 0, 200, 300);
        var window = Win(50, 60, 311, 426);

        var result = OverlayLayoutPlanner.ClampWindowToWorkArea(window, work);

        Assert.Equal(work.Left, result.Left);
        Assert.Equal(work.Top, result.Top);
    }

    // ChooseAttachDirection

    [Fact]
    public void ChooseAttachDirection_FitsBelow_ReturnsBelow()
    {
        var window = new PixelRect(100, 100, 400, 148);

        Assert.Equal(AttachDirection.Below, OverlayLayoutPlanner.ChooseAttachDirection(window, Work, 390));
    }

    [Fact]
    public void ChooseAttachDirection_OnlyFitsAbove_ReturnsAbove()
    {
        var window = new PixelRect(100, 900 - 48, 400, 900);

        Assert.Equal(AttachDirection.Above, OverlayLayoutPlanner.ChooseAttachDirection(window, Work, 390));
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
        var window = new PixelRect(100, 496, 400, 544);

        Assert.Equal(AttachDirection.Below, OverlayLayoutPlanner.ChooseAttachDirection(window, Work, 600));
    }

    [Fact]
    public void ToAnchor_BelowIsTopAndAboveIsBottom()
    {
        Assert.Equal(VerticalAnchor.Top, OverlayLayoutPlanner.ToAnchor(AttachDirection.Below));
        Assert.Equal(VerticalAnchor.Bottom, OverlayLayoutPlanner.ToAnchor(AttachDirection.Above));
    }

    // 왕복 속성

    // 창이 work 안에 있도록 무작위 위치를 고른다. 세로: 20%는 아래 변 flush, 10%는 위 변 flush.
    private static PixelRect RandomWindow(Random rng, int width, int height)
    {
        var left = rng.Next(Work.Left, Work.Right - width + 1);
        var kind = rng.NextDouble();
        int top;
        if (kind < 0.20)
            top = Work.Bottom - height;
        else if (kind < 0.30)
            top = Work.Top;
        else
            top = rng.Next(Work.Top, Work.Bottom - height + 1);
        return new PixelRect(left, top, left + width, top + height);
    }

    [Fact]
    public void RoundTrip_CollapseThenExpand_RestoresOriginalWindow()
    {
        var rng = new Random(Seed);

        for (var i = 0; i < Cases; i++)
        {
            var scale = Scales[rng.Next(Scales.Length)];
            int Px(double dip) => OverlayLayoutMetrics.DipToPx(dip, scale);
            var expandedHeight = Px(OverlayLayoutMetrics.ExpandedWindowHeightDip);
            var width = Px(OverlayLayoutMetrics.WindowWidthDip);

            var win = RandomWindow(rng, width, expandedHeight);

            var anchor = OverlayLayoutPlanner.ChooseResizeAnchor(
                win, Work, Px(OverlayLayoutMetrics.MiniWindowHeightDip), Px(OverlayLayoutMetrics.EdgeToleranceDip));
            var mini = OverlayLayoutPlanner.ResizeWindow(win, Px(OverlayLayoutMetrics.MiniWindowHeightDip), anchor, Work);

            var anchor2 = OverlayLayoutPlanner.ChooseResizeAnchor(
                mini, Work, Px(OverlayLayoutMetrics.ExpandedWindowHeightDip), Px(OverlayLayoutMetrics.EdgeToleranceDip));
            var back = OverlayLayoutPlanner.ResizeWindow(mini, Px(OverlayLayoutMetrics.ExpandedWindowHeightDip), anchor2, Work);

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
            var miniHeight = Px(OverlayLayoutMetrics.MiniWindowHeightDip);
            var width = Px(OverlayLayoutMetrics.WindowWidthDip);

            var mini = RandomWindow(rng, width, miniHeight);

            var dir = OverlayLayoutPlanner.ChooseAttachDirection(
                mini, Work, Px(OverlayLayoutMetrics.AttachedRegionHeightDip));
            var anchor = OverlayLayoutPlanner.ToAnchor(dir);
            var attached = OverlayLayoutPlanner.ResizeWindow(
                mini,
                Px(OverlayLayoutMetrics.MiniWindowHeightDip + OverlayLayoutMetrics.AttachedRegionHeightDip),
                anchor, Work);

            var notClamped = anchor == VerticalAnchor.Top
                ? attached.Top == mini.Top
                : attached.Bottom == mini.Bottom;
            if (!notClamped)
                continue;

            checkedCount++;
            var detached = OverlayLayoutPlanner.ResizeWindow(attached, miniHeight, anchor, Work);
            Assert.True(mini == detached, $"case {i} scale {scale}: mini={mini} attached={attached} detached={detached}");
        }

        Assert.True(checkedCount >= 250, $"검사된 건수 {checkedCount}");
    }

    // PlanTransition / TransitionMemo

    private static readonly PixelRect TaskbarWork = new(0, 0, 1920, 1032);

    // 막대를 작업 영역 안 임의 위치에 둔다. 세로: 20%는 아래 변 flush, 10%는 위 변 flush.
    private static PixelRect RandomBar(Random rng, PixelRect work, int width, int height)
    {
        var left = rng.Next(work.Left, work.Right - width + 1);
        var kind = rng.NextDouble();
        int top;
        if (kind < 0.20)
            top = work.Bottom - height;
        else if (kind < 0.30)
            top = work.Top;
        else
            top = rng.Next(work.Top, work.Bottom - height + 1);
        return new PixelRect(left, top, left + width, top + height);
    }

    [Fact]
    public void RoundTrip_FromMiniBar_ExpandThenCollapse_RestoresOriginalBar()
    {
        var rng = new Random(Seed);
        var total = 0;

        foreach (var scale in Scales)
        {
            int Px(double dip) => OverlayLayoutMetrics.DipToPx(dip, scale);
            var barWidth = Px(OverlayLayoutMetrics.WindowWidthDip);
            var barHeight = Px(OverlayLayoutMetrics.MiniWindowHeightDip);
            var expandedHeight = Px(OverlayLayoutMetrics.ExpandedWindowHeightDip);
            var tol = Px(OverlayLayoutMetrics.EdgeToleranceDip);

            for (var i = 0; i < Cases; i++)
            {
                var bar = RandomBar(rng, TaskbarWork, barWidth, barHeight);

                var plan1 = OverlayLayoutPlanner.PlanTransition(bar, TaskbarWork, expandedHeight, tol, null);
                var after = plan1.TargetWindow;
                var memo = new OverlayLayoutPlanner.TransitionMemo(bar, after, TaskbarWork, plan1.Anchor);
                var plan2 = OverlayLayoutPlanner.PlanTransition(after, TaskbarWork, barHeight, tol, memo);

                Assert.False(plan1.RestoredFromMemo);
                Assert.True(
                    plan2.TargetWindow == bar && plan2.RestoredFromMemo,
                    $"case {i} scale {scale}: bar={bar} after={after} back={plan2.TargetWindow} restored={plan2.RestoredFromMemo}");
                total++;
            }
        }

        Assert.Equal(Cases * Scales.Length, total);
    }

    [Fact]
    public void RoundTrip_NearBottomBar_FailsWithoutMemo_RestoresWithMemo()
    {
        // 배율 1.0, 막대 아래 변이 work.Bottom보다 50 px 위. 허용 오차(2) 밖이라 접을 때 Top이 고른다.
        var bar = new PixelRect(100, 934, 411, 982);
        Assert.Equal(TaskbarWork.Bottom - 50, bar.Bottom);

        var plan1 = OverlayLayoutPlanner.PlanTransition(bar, TaskbarWork, 426, Tol, null);

        // 메모 없이 접으면 원래 막대와 다르다(현재 결함 재현).
        var withoutMemo = OverlayLayoutPlanner.PlanTransition(plan1.TargetWindow, TaskbarWork, 48, Tol, null);
        Assert.False(withoutMemo.RestoredFromMemo);
        Assert.NotEqual(bar, withoutMemo.TargetWindow);

        // 메모를 쓰면 같다.
        var memo = new OverlayLayoutPlanner.TransitionMemo(bar, plan1.TargetWindow, TaskbarWork, plan1.Anchor);
        var withMemo = OverlayLayoutPlanner.PlanTransition(plan1.TargetWindow, TaskbarWork, 48, Tol, memo);
        Assert.True(withMemo.RestoredFromMemo);
        Assert.Equal(bar, withMemo.TargetWindow);
    }

    [Theory]
    [InlineData("windowMoved")]
    [InlineData("workChanged")]
    [InlineData("beforeHeightMismatch")]
    [InlineData("beforeWidthMismatch")]
    public void PlanTransition_InvalidMemo_IsIgnored(string kind)
    {
        var bar = new PixelRect(100, 300, 411, 348);
        var after = OverlayLayoutPlanner.PlanTransition(bar, TaskbarWork, 426, Tol, null).TargetWindow;
        var memo = new OverlayLayoutPlanner.TransitionMemo(bar, after, TaskbarWork, VerticalAnchor.Top);

        var window = after;
        var work = TaskbarWork;
        var newHeight = 48;
        switch (kind)
        {
            case "windowMoved":
                window = after.Offset(1, 0);
                break;
            case "workChanged":
                work = new PixelRect(0, 0, 1920, 1040);
                break;
            case "beforeHeightMismatch":
                newHeight = 50;
                break;
            case "beforeWidthMismatch":
                memo = memo with { WindowBefore = new PixelRect(bar.Left, bar.Top, bar.Left + 300, bar.Bottom) };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        var plan = OverlayLayoutPlanner.PlanTransition(window, work, newHeight, Tol, memo);

        var expectedAnchor = OverlayLayoutPlanner.ChooseResizeAnchor(window, work, newHeight, Tol);
        var expectedWindow = OverlayLayoutPlanner.ResizeWindow(window, newHeight, expectedAnchor, work);
        Assert.False(plan.RestoredFromMemo);
        Assert.Equal(expectedAnchor, plan.Anchor);
        Assert.Equal(expectedWindow, plan.TargetWindow);
    }

    [Fact]
    public void PlanTransition_NonPositiveHeight_Throws()
    {
        var window = Win(100, 300, 311, 48);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OverlayLayoutPlanner.PlanTransition(window, TaskbarWork, 0, Tol, null));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OverlayLayoutPlanner.PlanTransition(window, TaskbarWork, -1, Tol, null));
    }

    // AnchorEdgeShiftPx

    [Fact]
    public void AnchorEdgeShiftPx_NoClamp_ReturnsZero()
    {
        var topBar = Win(100, 300, 311, 48);
        var topTarget = OverlayLayoutPlanner.ResizeWindow(topBar, 426, VerticalAnchor.Top, TaskbarWork);
        Assert.Equal(0, OverlayLayoutPlanner.AnchorEdgeShiftPx(topBar, topTarget, VerticalAnchor.Top));

        var bottomBar = Win(100, 500, 311, 48);
        var bottomTarget = OverlayLayoutPlanner.ResizeWindow(bottomBar, 426, VerticalAnchor.Bottom, TaskbarWork);
        Assert.Equal(0, OverlayLayoutPlanner.AnchorEdgeShiftPx(bottomBar, bottomTarget, VerticalAnchor.Bottom));
    }

    [Fact]
    public void AnchorEdgeShiftPx_BottomAnchorClampedAtTop_IsPositive()
    {
        // Bottom 고정이면 Top이 -340이 되어 work.Top(0)으로 밀리므로 아래 변이 340 px 내려간다.
        var bar = Win(100, 38, 311, 48);
        var target = OverlayLayoutPlanner.ResizeWindow(bar, 426, VerticalAnchor.Bottom, TaskbarWork);

        Assert.Equal(0, target.Top);
        Assert.Equal(340, OverlayLayoutPlanner.AnchorEdgeShiftPx(bar, target, VerticalAnchor.Bottom));
    }

    [Fact]
    public void AnchorEdgeShiftPx_TopAnchorClampedAtBottom_IsNegative()
    {
        // Top 고정이면 Bottom이 1326이 되어 work.Bottom(1032)에 맞게 위로 밀리므로 위 변이 294 px 올라간다.
        var bar = Win(100, 900, 311, 48);
        var target = OverlayLayoutPlanner.ResizeWindow(bar, 426, VerticalAnchor.Top, TaskbarWork);

        Assert.Equal(TaskbarWork.Bottom, target.Bottom);
        Assert.Equal(-294, OverlayLayoutPlanner.AnchorEdgeShiftPx(bar, target, VerticalAnchor.Top));
    }

    [Fact]
    public void AnchorEdgeShiftPx_MemoRestore_ReportsAnchorEdgeMovement()
    {
        var bar = new PixelRect(100, 934, 411, 982);
        var plan1 = OverlayLayoutPlanner.PlanTransition(bar, TaskbarWork, 426, Tol, null);
        var memo = new OverlayLayoutPlanner.TransitionMemo(bar, plan1.TargetWindow, TaskbarWork, plan1.Anchor);
        var plan2 = OverlayLayoutPlanner.PlanTransition(plan1.TargetWindow, TaskbarWork, 48, Tol, memo);

        // 복원 대상의 Bottom은 원래 막대의 Bottom이고, 접기 전 창의 Bottom과 같다.
        Assert.Equal(0, OverlayLayoutPlanner.AnchorEdgeShiftPx(plan1.TargetWindow, plan2.TargetWindow, plan1.Anchor));
    }
}
