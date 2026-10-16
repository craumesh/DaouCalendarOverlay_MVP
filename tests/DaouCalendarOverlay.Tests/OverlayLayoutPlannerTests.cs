using DaouCalendarOverlay.Models;
using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

public sealed class OverlayLayoutPlannerTests
{
    private static readonly PixelRect Work = new(0, 0, 1920, 1040);

    private static PixelRect Win(int left, int top, int width, int height) => new(left, top, left + width, top + height);

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

    // 고정 캔버스: CanvasMetrics

    [Theory]
    [InlineData(1.0, 311, 390, 48, 426, 390, 2, 828, 12)]
    [InlineData(1.25, 389, 488, 60, 532, 488, 2, 1036, 16)]
    [InlineData(1.5, 466, 585, 72, 639, 585, 3, 1242, 18)]
    public void CanvasMetrics_FromScale_MatchesTable(
        double scale, int width, int slotOffset, int mini, int expanded, int attachRegion, int edgeTol, int height, int upOffset)
    {
        var m = CanvasMetrics.FromScale(scale);

        Assert.Equal(width, m.WidthPx);
        Assert.Equal(slotOffset, m.SlotOffsetPx);
        Assert.Equal(mini, m.MiniHeightPx);
        Assert.Equal(expanded, m.ExpandedHeightPx);
        Assert.Equal(attachRegion, m.AttachRegionPx);
        Assert.Equal(edgeTol, m.EdgeTolerancePx);
        Assert.Equal(height, m.HeightPx);
        Assert.Equal(upOffset, m.ExpandedUp.OffsetPx);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void CanvasMetrics_FromScale_RejectsInvalidScale(double scale)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CanvasMetrics.FromScale(scale));
    }

    [Fact]
    public void Metrics_BarSlotOffset_IsMaxOfExpandedDeltaAndAttachRegion()
    {
        var expected = Math.Max(
            OverlayLayoutMetrics.ExpandedCardHeightDip - OverlayLayoutMetrics.MiniCardHeightDip,
            OverlayLayoutMetrics.AttachedRegionHeightDip);
        Assert.Equal(OverlayLayoutMetrics.BarSlotOffsetDip, expected);
        Assert.Equal(828, OverlayLayoutMetrics.WindowHeightDip);
    }

    [Fact]
    public void CanvasMetrics_RegionsFitInsideCanvas_AllScales()
    {
        for (var scale = 1.0; scale <= 3.0; scale += 0.25)
        {
            var m = CanvasMetrics.FromScale(scale);

            Assert.True(m.ExpandedUp.OffsetPx >= 0, $"scale {scale}: ExpandedUp.OffsetPx={m.ExpandedUp.OffsetPx}");
            Assert.True(m.SlotOffsetPx - m.AttachRegionPx >= 0, $"scale {scale}: Above 영역이 창 위로 나감");
            Assert.True(
                m.ExpandedDown.OffsetPx + m.ExpandedHeightPx <= m.HeightPx,
                $"scale {scale}: ExpandedDown이 창 아래로 나감");
            Assert.True(
                m.SlotOffsetPx + m.MiniHeightPx + m.AttachRegionPx <= m.HeightPx,
                $"scale {scale}: Below 영역이 창 아래로 나감");
        }
    }

    // PlanExpandedPlacement

    private static readonly PixelRect Work1032 = new(0, 0, 1920, 1032);

    // 100% 캔버스에서 막대 칸 위 변이 slotTop인 창.
    private static PixelRect WindowWithSlotTop(int left, int slotTop, CanvasMetrics m) =>
        new(left, slotTop - m.SlotOffsetPx, left + m.WidthPx, slotTop - m.SlotOffsetPx + m.HeightPx);

    [Fact]
    public void PlanExpandedPlacement_Cases()
    {
        var m = CanvasMetrics.FromScale(1.0);

        // 아래로 자람: 막대 칸 위 변 100(창 위 변 -290).
        var down = WindowWithSlotTop(100, 100, m);
        Assert.Equal(-290, down.Top);
        var downPlacement = OverlayLayoutPlanner.PlanExpandedPlacement(down, Work1032, m);
        Assert.Equal(390, downPlacement.OffsetPx);
        var downCard = downPlacement.InWindow(down);
        Assert.Equal(100, downCard.Top);
        Assert.Equal(526, downCard.Bottom);

        // 위로 자람: 막대 칸 아래 변 1032(창 위 변 594).
        var up = WindowWithSlotTop(100, 984, m);
        Assert.Equal(594, up.Top);
        var upPlacement = OverlayLayoutPlanner.PlanExpandedPlacement(up, Work1032, m);
        Assert.Equal(12, upPlacement.OffsetPx);
        var upCard = upPlacement.InWindow(up);
        Assert.Equal(606, upCard.Top);
        Assert.Equal(1032, upCard.Bottom);

        // 대체 규칙(아래 맞춤): work 아래 변 700, 막대 칸 위 변 300(창 -90).
        var shortWork = new PixelRect(0, 0, 1920, 700);
        var fit = WindowWithSlotTop(100, 300, m);
        Assert.Equal(-90, fit.Top);
        var fitPlacement = OverlayLayoutPlanner.PlanExpandedPlacement(fit, shortWork, m);
        Assert.Equal(364, fitPlacement.OffsetPx);
        var fitCard = fitPlacement.InWindow(fit);
        Assert.Equal(274, fitCard.Top);
        Assert.Equal(700, fitCard.Bottom);

        // 대체 규칙(카드가 work보다 큼): work 높이 400.
        var tinyWork = new PixelRect(0, 0, 1920, 400);
        var big = WindowWithSlotTop(100, 100, m);
        Assert.Equal(-290, big.Top);
        var bigPlacement = OverlayLayoutPlanner.PlanExpandedPlacement(big, tinyWork, m);
        Assert.Equal(290, bigPlacement.OffsetPx);
        var bigCard = bigPlacement.InWindow(big);
        Assert.Equal(0, bigCard.Top);
        Assert.Equal(426, bigCard.Bottom);

        // 음수 좌표 모니터.
        var negWork = new PixelRect(-1920, -200, 0, 880);
        var neg = WindowWithSlotTop(-1500, -150, m);
        Assert.Equal(-540, neg.Top);
        Assert.Equal(390, OverlayLayoutPlanner.PlanExpandedPlacement(neg, negWork, m).OffsetPx);
    }

    [Fact]
    public void PlanExpandedPlacement_Property_CardContainsSlotAndStaysInCanvas()
    {
        var rng = new Random(7404);
        double[] scales = { 1.0, 1.25, 1.5 };

        for (var i = 0; i < 10_000; i++)
        {
            var m = CanvasMetrics.FromScale(scales[i % scales.Length]);
            var workLeft = rng.Next(-3840, 3841);
            var workTop = rng.Next(-1200, 1201);
            var work = new PixelRect(
                workLeft, workTop, workLeft + rng.Next(800, 3841), workTop + rng.Next(300, 2161));
            var slotTop = rng.Next(work.Top - 200, work.Bottom + 201);
            var window = WindowWithSlotTop(rng.Next(-3840, 3841), slotTop, m);
            var slot = m.Slot.InWindow(window);
            var e = m.ExpandedHeightPx;

            var result = OverlayLayoutPlanner.PlanExpandedPlacement(window, work, m);

            var context = $"case {i}: work={work} window={window} result={result}";
            Assert.True(result.OffsetPx >= m.ExpandedUp.OffsetPx && result.OffsetPx <= m.ExpandedDown.OffsetPx, context);
            Assert.True(result.HeightPx == e, context);

            var card = result.InWindow(window);
            Assert.True(card.Top <= slot.Top && card.Bottom >= slot.Bottom, context);
            Assert.True(card.Top >= window.Top && card.Bottom <= window.Bottom, context);

            if (slot.Top + e <= work.Bottom)
                Assert.True(result == m.ExpandedDown, context);
            else if (slot.Bottom - e >= work.Top)
                Assert.True(result == m.ExpandedUp, context);
        }
    }

    // PlanRestingExpandedPlacement

    [Fact]
    public void PlanRestingExpandedPlacement_BottomEdgeTolerance()
    {
        var m = CanvasMetrics.FromScale(1.0);

        PixelRect CardWithBottom(int bottom) => new(100, bottom - 426, 411, bottom);

        Assert.Equal(m.ExpandedUp, OverlayLayoutPlanner.PlanRestingExpandedPlacement(CardWithBottom(1032), Work1032, m));
        Assert.Equal(m.ExpandedUp, OverlayLayoutPlanner.PlanRestingExpandedPlacement(CardWithBottom(1030), Work1032, m));
        Assert.Equal(m.ExpandedDown, OverlayLayoutPlanner.PlanRestingExpandedPlacement(CardWithBottom(1029), Work1032, m));

        // card.Top + 48 > work.Bottom (카드가 작업 영역 밖으로 걸침)
        var overhang = new PixelRect(100, 990, 411, 990 + 426);
        Assert.True(overhang.Top + m.MiniHeightPx > Work1032.Bottom);
        Assert.Equal(m.ExpandedUp, OverlayLayoutPlanner.PlanRestingExpandedPlacement(overhang, Work1032, m));
    }

    // PlanRestingLayout

    [Fact]
    public void PlanRestingLayout_Cases()
    {
        var m = CanvasMetrics.FromScale(1.0);

        var bar = OverlayLayoutPlanner.PlanRestingLayout(new PixelPoint(100, 984), OverlayDisplayMode.MiniBar, Work1032, m);
        Assert.Equal(m.Slot, bar.Placement);
        Assert.Equal(594, bar.Window.Top);
        Assert.Equal(311, bar.Window.Width);
        Assert.Equal(828, bar.Window.Height);

        var up = OverlayLayoutPlanner.PlanRestingLayout(new PixelPoint(100, 606), OverlayDisplayMode.Expanded, Work1032, m);
        Assert.Equal(m.ExpandedUp, up.Placement);
        Assert.Equal(594, up.Window.Top);
        Assert.Equal(311, up.Window.Width);
        Assert.Equal(828, up.Window.Height);

        var down = OverlayLayoutPlanner.PlanRestingLayout(new PixelPoint(100, 20), OverlayDisplayMode.Expanded, Work1032, m);
        Assert.Equal(m.ExpandedDown, down.Placement);
        Assert.Equal(-370, down.Window.Top);
        Assert.Equal(311, down.Window.Width);
        Assert.Equal(828, down.Window.Height);

        var far = OverlayLayoutPlanner.PlanRestingLayout(new PixelPoint(5000, 5000), OverlayDisplayMode.MiniBar, Work1032, m);
        Assert.Equal(m.Slot, far.Placement);
        Assert.Equal(1609, far.Window.Left);
        Assert.Equal(594, far.Window.Top);
        Assert.Equal(311, far.Window.Width);
        Assert.Equal(828, far.Window.Height);
        Assert.Equal(new PixelRect(1609, 984, 1920, 1032), far.Placement.InWindow(far.Window));
    }

    // 저장·복원 왕복

    [Fact]
    public void SaveRestore_RoundTrip_ExactPx()
    {
        foreach (var scale in new[] { 1.0, 1.25, 1.5, 1.75 })
        {
            foreach (var px in new[] { -1920, -1, 0, 1, 987, 3839 })
            {
                Assert.Equal(px, OverlayLayoutMetrics.DipToPx(px / scale, scale));
            }
        }
    }

    private static readonly PixelRect[] RoundTripWorks =
    {
        new(0, 0, 1920, 1032),
        new(1920, 4, 3840, 1084),
        new(-1920, -200, 0, 880),
    };

    public static IEnumerable<object[]> RoundTripCases()
    {
        foreach (var scale in new[] { 1.0, 1.25, 1.5, 1.75 })
            foreach (var mode in new[] { OverlayDisplayMode.MiniBar, OverlayDisplayMode.Expanded })
                for (var work = 0; work < RoundTripWorks.Length; work++)
                    yield return new object[] { scale, mode, work };
    }

    /// <summary>
    /// 저장 → 복원 왕복(같은 배율). 저장 값은 쉬는 배치의 카드 px 왼쪽 위 ÷ 배율(MainWindow.GetCardTopLeftDip와 같은 식)이고,
    /// 복원은 DipToPx로 px를 직접 만든 뒤 PlanRestingLayout을 다시 거친다(MainWindow.ApplyInitialPlacement와 같은 경로).
    /// 결과 창 사각형과 카드 배치가 px까지 같아야 재시작마다 위치가 밀리지 않는다(R5).
    /// 카드 왼쪽 위는 작업 영역의 네 변에 0px로 붙은 위치, 아래 가장자리 허용 오차 경계, 안쪽 위치, 밖으로 나간 위치(클램프)를 포함한다.
    /// </summary>
    [Theory]
    [MemberData(nameof(RoundTripCases))]
    public void PlanRestingLayout_SaveRestore_RoundTrip_SameScale(double scale, OverlayDisplayMode mode, int workIndex)
    {
        var work = RoundTripWorks[workIndex];
        var m = CanvasMetrics.FromScale(scale);
        var cardHeight = mode == OverlayDisplayMode.MiniBar ? m.MiniHeightPx : m.ExpandedHeightPx;
        var maxLeft = work.Right - m.WidthPx;
        var maxTop = work.Bottom - cardHeight;
        var xs = new[] { work.Left, work.Left + 1, work.Left + 333, maxLeft - 1, maxLeft, work.Left - 50, work.Right + 7 };
        var ys = new[]
        {
            work.Top, work.Top + 1, work.Top + 287, maxTop - m.EdgeTolerancePx - 1, maxTop - m.EdgeTolerancePx,
            maxTop - 1, maxTop, work.Top - 999, work.Bottom + 3,
        };

        var cases = 0;
        foreach (var x in xs)
        {
            foreach (var y in ys)
            {
                var layout = OverlayLayoutPlanner.PlanRestingLayout(new PixelPoint(x, y), mode, work, m);
                var card = layout.Placement.InWindow(layout.Window);

                // 쉬는 배치는 카드를 작업 영역 안에 두고 창 크기는 캔버스 크기다.
                Assert.Equal(m.WidthPx, layout.Window.Width);
                Assert.Equal(m.HeightPx, layout.Window.Height);
                Assert.Equal(cardHeight, card.Height);
                Assert.True(card.Left >= work.Left && card.Right <= work.Right && card.Top >= work.Top && card.Bottom <= work.Bottom,
                    $"카드 {card}가 작업 영역 {work} 밖이다(scale={scale}, mode={mode}, tl=({x},{y}))");

                // 저장: 카드 px ÷ 배율(DIP, double 그대로).
                var savedLeft = card.Left / scale;
                var savedTop = card.Top / scale;

                // 복원: DipToPx로 px를 직접 만든다.
                var restored = new PixelPoint(
                    OverlayLayoutMetrics.DipToPx(savedLeft, scale),
                    OverlayLayoutMetrics.DipToPx(savedTop, scale));
                Assert.Equal(new PixelPoint(card.Left, card.Top), restored);

                var again = OverlayLayoutPlanner.PlanRestingLayout(restored, mode, work, m);
                Assert.Equal(layout, again);

                // 두 번째 왕복도 같다(재시작을 거듭해도 밀리지 않는다).
                var card2 = again.Placement.InWindow(again.Window);
                var third = OverlayLayoutPlanner.PlanRestingLayout(
                    new PixelPoint(
                        OverlayLayoutMetrics.DipToPx(card2.Left / scale, scale),
                        OverlayLayoutMetrics.DipToPx(card2.Top / scale, scale)),
                    mode, work, m);
                Assert.Equal(layout, third);
                cases++;
            }
        }

        Assert.Equal(xs.Length * ys.Length, cases);
    }

    /// <summary>
    /// 설정창 저장 경로(MainWindow.ApplySettings의 핸들 있는 분기)와 같은 식. 저장 값 = 카드 px ÷ 배율이면,
    /// DipToPx로 만든 카드 px에 현재 배치를 그대로 두고 WindowForCard로 얻은 창이 원래 창과 px까지 같다(창 호출 0회, 위치 변화 0px).
    /// 배치는 막대 칸, 위·아래로 자란 펼침, 규칙 (3)의 중간 오프셋을 모두 포함한다. 창 위 변이 음수인 경우도 넣는다.
    /// </summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    public void SettingsReapply_SameScale_KeepsWindowExactly(double scale)
    {
        var m = CanvasMetrics.FromScale(scale);
        var placements = new List<CardPlacement> { m.Slot, m.ExpandedUp, m.ExpandedDown };
        for (var offset = m.ExpandedUp.OffsetPx + 1; offset < m.ExpandedDown.OffsetPx; offset += 7)
            placements.Add(new CardPlacement(offset, m.ExpandedHeightPx));

        foreach (var placement in placements)
        {
            foreach (var (left, top) in new[] { (0, 0), (1609, 594), (1920, -386), (-1920, -200), (-311, 4), (3529, 1), (987, -1) })
            {
                var win = new PixelRect(left, top, left + m.WidthPx, top + m.HeightPx);
                var card = placement.InWindow(win);
                var savedLeft = card.Left / scale;
                var savedTop = card.Top / scale;

                var x = OverlayLayoutMetrics.DipToPx(savedLeft, scale);
                var y = OverlayLayoutMetrics.DipToPx(savedTop, scale);
                var cardPx = new PixelRect(x, y, x + win.Width, y + placement.HeightPx);
                Assert.Equal(card, cardPx);
                Assert.Equal(win, OverlayLayoutPlanner.WindowForCard(cardPx, placement, win.Width, win.Height));
            }
        }
    }

    /// <summary>
    /// 7.4.0b(창 = 카드)는 WPF Left/Top(창 왼쪽 위 px ÷ 배율)을 저장했다. 같은 값을 카드 왼쪽 위로 읽으면
    /// 작업 영역 안에 있던 카드는 같은 px에 복원되고, 창은 그 카드에서 막대 칸 오프셋(막대) 또는 펼침 배치 오프셋만큼 위에 놓인다.
    /// </summary>
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    public void PlanRestingLayout_Reads740bValuesAsCardTopLeft(double scale)
    {
        var m = CanvasMetrics.FromScale(scale);
        var work = new PixelRect(0, 0, 1920, 1032);
        foreach (var mode in new[] { OverlayDisplayMode.MiniBar, OverlayDisplayMode.Expanded })
        {
            var cardHeight = mode == OverlayDisplayMode.MiniBar ? m.MiniHeightPx : m.ExpandedHeightPx;
            foreach (var (left, top) in new[] { (1000, 20), (0, 0), (1920 - m.WidthPx, 1032 - cardHeight), (700, 200) })
            {
                // 7.4.0b 창 사각형 = 카드 사각형. 저장 값은 WPF Left/Top = 창 px ÷ 배율.
                var saved740b = (Left: left / scale, Top: top / scale);
                var tl = new PixelPoint(OverlayLayoutMetrics.DipToPx(saved740b.Left, scale), OverlayLayoutMetrics.DipToPx(saved740b.Top, scale));
                var layout = OverlayLayoutPlanner.PlanRestingLayout(tl, mode, work, m);
                var card = layout.Placement.InWindow(layout.Window);

                Assert.Equal(new PixelRect(left, top, left + m.WidthPx, top + cardHeight), card);
                Assert.Equal(top - layout.Placement.OffsetPx, layout.Window.Top);
                if (mode == OverlayDisplayMode.MiniBar)
                    Assert.Equal(m.Slot, layout.Placement);
            }
        }
    }

    // AttachRegion

    [Fact]
    public void AttachRegion_Cases()
    {
        var m100 = CanvasMetrics.FromScale(1.0);
        Assert.Equal(new CardPlacement(438, 390), OverlayLayoutPlanner.AttachRegion(AttachDirection.Below, m100));
        Assert.Equal(new CardPlacement(0, 390), OverlayLayoutPlanner.AttachRegion(AttachDirection.Above, m100));

        var m125 = CanvasMetrics.FromScale(1.25);
        var below = OverlayLayoutPlanner.AttachRegion(AttachDirection.Below, m125);
        Assert.Equal(new CardPlacement(548, 488), below);
        Assert.Equal(new CardPlacement(0, 488), OverlayLayoutPlanner.AttachRegion(AttachDirection.Above, m125));
        Assert.Equal(m125.HeightPx, below.OffsetPx + below.HeightPx);
        Assert.Equal(1036, m125.HeightPx);
    }

    // WindowForCard

    [Fact]
    public void WindowForCard_AllowsNegativeWindowTop()
    {
        var m = CanvasMetrics.FromScale(1.0);

        var window = OverlayLayoutPlanner.WindowForCard(new PixelRect(0, 0, 311, 48), m.Slot, 311, 828);

        Assert.Equal(new PixelRect(0, -390, 311, 438), window);
    }
}
