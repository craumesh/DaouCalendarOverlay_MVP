using DaouCalendarOverlay.Models;
using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

public sealed class OverlayDragSnapTests
{
    private const int Seed = 20261016;

    private static readonly double[] Scales = { 1.0, 1.25, 1.5, 1.75, 2.0 };

    private static readonly double[] HeightsDip =
    {
        OverlayLayoutMetrics.MiniWindowHeightDip,
        OverlayLayoutMetrics.ExpandedWindowHeightDip
    };

    // 모니터 하나: Bounds는 배치 판단용이고 Work는 스냅 기준이다.
    private readonly record struct Monitor(PixelRect Bounds, PixelRect Work);

    // 기본 배치: 왼쪽 보조 모니터와 오른쪽 주 모니터. 작업 표시줄 48 px.
    private static readonly Monitor[] TwoMonitors =
    {
        new(new PixelRect(-1920, 0, 0, 1080), new PixelRect(-1920, 0, 0, 1032)),
        new(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032))
    };

    private static readonly Monitor[] SingleMonitor =
    {
        new(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032))
    };

    private static int Px(double dip, double scale) => OverlayLayoutMetrics.DipToPx(dip, scale);

    private static long IntersectionArea(PixelRect a, PixelRect b)
    {
        var w = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        var h = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        return w <= 0 || h <= 0 ? 0 : (long)w * h;
    }

    // 하네스의 작업 영역 선택: 제안 사각형과 교차 면적이 가장 큰 모니터의 Work, 동률이면 목록 앞쪽.
    // 어느 모니터와도 겹치지 않으면 첫 모니터를 쓴다.
    private static PixelRect PickWork(PixelRect proposed, Monitor[] monitors)
    {
        var best = monitors[0];
        var bestArea = IntersectionArea(proposed, best.Bounds);
        for (var i = 1; i < monitors.Length; i++)
        {
            var area = IntersectionArea(proposed, monitors[i].Bounds);
            if (area > bestArea)
            {
                best = monitors[i];
                bestArea = area;
            }
        }

        return best.Work;
    }

    private static PixelRect StepWithPick(
        DragSnapSession session, PixelPoint cursor, int w, int h, Monitor[] monitors, int threshold)
    {
        var proposed = OverlayDragSnap.Propose(session, cursor, w, h);
        return OverlayDragSnap.Step(session, cursor, w, h, PickWork(proposed, monitors), threshold);
    }

    private static PixelPoint CenterOf(PixelRect r) => new((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);

    // 누적 lParam 모델

    [Fact]
    public void AccumulatedLParamModel_StepEqualsSnapOfStartPlusTotalDelta()
    {
        var rng = new Random(Seed);

        foreach (var scale in Scales)
        foreach (var heightDip in HeightsDip)
        {
            var w = Px(OverlayLayoutMetrics.WindowWidthDip, scale);
            var h = Px(heightDip, scale);
            var thr = Px(OverlayLayoutMetrics.SnapThresholdDip, scale);

            for (var path = 0; path < 20; path++)
            {
                var start = RandomWindowNearEdges(rng, TwoMonitors, w, h, thr);
                var startCursor = CenterOf(start);
                var session = OverlayDragSnap.Begin(start, startCursor);

                var cursor = startCursor;
                var lastWritten = start; // OS가 마지막으로 받아 간 사각형(되쓴 결과)
                var totalDx = 0;
                var totalDy = 0;

                for (var k = 0; k < 200; k++)
                {
                    var dx = rng.Next(-6, 7);
                    var dy = rng.Next(-6, 7);
                    var next = new PixelPoint(cursor.X + dx, cursor.Y + dy);

                    // OS 흉내: lParam = 직전 되쓴 사각형 + 커서 이동량. Step에는 크기만 넘긴다.
                    var lp = lastWritten.Offset(dx, dy);
                    var result = StepWithPick(session, next, lp.Width, lp.Height, TwoMonitors, thr);

                    totalDx += dx;
                    totalDy += dy;
                    var cumulative = start.Offset(totalDx, totalDy);
                    var expected = OverlaySnap.Snap(cumulative, PickWork(cumulative, TwoMonitors), thr);

                    Assert.True(
                        expected == result,
                        $"scale {scale} h {h} path {path} step {k}: expected={expected} actual={result}");

                    cursor = next;
                    lastWritten = result;
                }
            }
        }
    }

    // 작업 영역 변 근처(임계의 3배 안) 또는 내부의 무작위 시작 사각형. 모니터 안쪽에 둔다.
    private static PixelRect RandomWindowNearEdges(Random rng, Monitor[] monitors, int w, int h, int thr)
    {
        var work = monitors[rng.Next(monitors.Length)].Work;
        var span = thr * 3;
        var maxLeft = work.Right - w;
        var maxTop = work.Bottom - h;

        int Pick(int low, int high)
        {
            switch (rng.Next(3))
            {
                case 0:
                    return Math.Clamp(low + rng.Next(0, span + 1), low, high);
                case 1:
                    return Math.Clamp(high - rng.Next(0, span + 1), low, high);
                default:
                    return rng.Next(low, high + 1);
            }
        }

        var left = Pick(work.Left, maxLeft);
        var top = Pick(work.Top, maxTop);
        return new PixelRect(left, top, left + w, top + h);
    }

    // 천천히 떼기

    // (이름, 붙는 가로 변(-1 왼쪽, +1 오른쪽, 0 없음), 붙는 세로 변(-1 위, +1 아래, 0 없음))
    public static IEnumerable<object[]> AttachCases()
    {
        yield return new object[] { "top", 0, -1 };
        yield return new object[] { "bottom", 0, 1 };
        yield return new object[] { "left", -1, 0 };
        yield return new object[] { "right", 1, 0 };
        yield return new object[] { "topLeft", -1, -1 };
        yield return new object[] { "topRight", 1, -1 };
        yield return new object[] { "bottomLeft", -1, 1 };
        yield return new object[] { "bottomRight", 1, 1 };
    }

    // 창을 work의 지정한 변·모서리에서 distance px 떨어뜨려 둔다. 붙지 않는 축은 가운데에 둔다.
    private static PixelRect PlaceNearEdges(PixelRect work, int w, int h, int sideX, int sideY, int distance)
    {
        var left = sideX switch
        {
            -1 => work.Left + distance,
            1 => work.Right - w - distance,
            _ => work.Left + (work.Width - w) / 2
        };
        var top = sideY switch
        {
            -1 => work.Top + distance,
            1 => work.Bottom - h - distance,
            _ => work.Top + (work.Height - h) / 2
        };
        return new PixelRect(left, top, left + w, top + h);
    }

    [Theory]
    [MemberData(nameof(AttachCases))]
    public void SlowDetach_MovingInwardOnePixelAtATime_FollowsCursorExactlyBeyondThreshold(string name, int sideX, int sideY)
    {
        var work = SingleMonitor[0].Work;

        foreach (var scale in Scales)
        foreach (var heightDip in HeightsDip)
        {
            var w = Px(OverlayLayoutMetrics.WindowWidthDip, scale);
            var h = Px(heightDip, scale);
            var thr = Px(OverlayLayoutMetrics.SnapThresholdDip, scale);

            var start = PlaceNearEdges(work, w, h, sideX, sideY, 0);
            var startCursor = CenterOf(start);
            var session = OverlayDragSnap.Begin(start, startCursor);

            for (var k = 1; k <= 30; k++)
            {
                // 바깥 반대쪽(안쪽)으로 1 px씩.
                var dx = -sideX * k;
                var dy = -sideY * k;
                var cursor = new PixelPoint(startCursor.X + dx, startCursor.Y + dy);
                var result = OverlayDragSnap.Step(session, cursor, w, h, work, thr);
                var context = $"{name} scale {scale} h {h} k {k} thr {thr}: result={result}";

                if (sideX != 0)
                {
                    var edgeGap = sideX < 0 ? result.Left - work.Left : result.Right - work.Right;
                    if (k <= thr)
                        Assert.True(edgeGap == 0, $"가로 변 차이 {edgeGap} {context}");
                    else
                        Assert.True(result.Left == start.Left + dx, $"가로 이탈 {context}");
                }
                else
                {
                    Assert.True(result.Left == start.Left, $"가로 불변 {context}");
                }

                if (sideY != 0)
                {
                    var edgeGap = sideY < 0 ? result.Top - work.Top : result.Bottom - work.Bottom;
                    if (k <= thr)
                        Assert.True(edgeGap == 0, $"세로 변 차이 {edgeGap} {context}");
                    else
                        Assert.True(result.Top == start.Top + dy, $"세로 이탈 {context}");
                }
                else
                {
                    Assert.True(result.Top == start.Top, $"세로 불변 {context}");
                }

                Assert.Equal(w, result.Width);
                Assert.Equal(h, result.Height);
            }
        }
    }

    // 다가가기

    [Theory]
    [MemberData(nameof(AttachCases))]
    public void Approach_MovingTowardEdgeOnePixelAtATime_SnapsFromFirstStepWithinThreshold(string name, int sideX, int sideY)
    {
        var work = SingleMonitor[0].Work;

        foreach (var scale in Scales)
        foreach (var heightDip in HeightsDip)
        {
            var w = Px(OverlayLayoutMetrics.WindowWidthDip, scale);
            var h = Px(heightDip, scale);
            var thr = Px(OverlayLayoutMetrics.SnapThresholdDip, scale);
            var distance = thr + 10;

            var start = PlaceNearEdges(work, w, h, sideX, sideY, distance);
            var startCursor = CenterOf(start);
            var session = OverlayDragSnap.Begin(start, startCursor);

            for (var k = 1; k <= distance; k++)
            {
                // 변 쪽으로 1 px씩. 남은 거리는 distance - k.
                var dx = sideX * k;
                var dy = sideY * k;
                var cursor = new PixelPoint(startCursor.X + dx, startCursor.Y + dy);
                var result = OverlayDragSnap.Step(session, cursor, w, h, work, thr);
                var remaining = distance - k;
                var context = $"{name} scale {scale} h {h} k {k} thr {thr}: result={result}";

                if (sideX != 0)
                {
                    var edgeGap = sideX < 0 ? result.Left - work.Left : result.Right - work.Right;
                    if (remaining <= thr)
                        Assert.True(edgeGap == 0, $"가로 변 차이 {edgeGap} {context}");
                    else
                        Assert.True(result.Left == start.Left + dx, $"가로 커서 그대로 아님 {context}");
                }
                else
                {
                    Assert.True(result.Left == start.Left, $"가로 불변 {context}");
                }

                if (sideY != 0)
                {
                    var edgeGap = sideY < 0 ? result.Top - work.Top : result.Bottom - work.Bottom;
                    if (remaining <= thr)
                        Assert.True(edgeGap == 0, $"세로 변 차이 {edgeGap} {context}");
                    else
                        Assert.True(result.Top == start.Top + dy, $"세로 커서 그대로 아님 {context}");
                }
                else
                {
                    Assert.True(result.Top == start.Top, $"세로 불변 {context}");
                }
            }
        }
    }

    // 음수 좌표 모니터

    [Fact]
    public void NegativeCoordinateMonitor_LeftMonitorSnapsLeftToMinus1920AndRightToZero()
    {
        foreach (var scale in Scales)
        foreach (var heightDip in HeightsDip)
        {
            var w = Px(OverlayLayoutMetrics.WindowWidthDip, scale);
            var h = Px(heightDip, scale);
            var thr = Px(OverlayLayoutMetrics.SnapThresholdDip, scale);

            var start = new PixelRect(-1000, 100, -1000 + w, 100 + h);
            var startCursor = CenterOf(start);
            var session = OverlayDragSnap.Begin(start, startCursor);

            // 왼쪽 모니터의 왼쪽 변 근처로 끈다.
            var nearLeft = new PixelRect(-1920 + thr - 1, 100, -1920 + thr - 1 + w, 100 + h);
            var leftResult = StepWithPick(session, MoveTo(startCursor, start, nearLeft), w, h, TwoMonitors, thr);
            Assert.Equal(-1920, leftResult.Left);
            Assert.Equal(100, leftResult.Top);

            // 왼쪽 모니터의 오른쪽 변(= x 0) 근처로 끈다. 창은 전부 왼쪽 모니터 안에 있다.
            var nearRight = new PixelRect(-w - (thr - 1), 100, -(thr - 1), 100 + h);
            var rightResult = StepWithPick(session, MoveTo(startCursor, start, nearRight), w, h, TwoMonitors, thr);
            Assert.Equal(0, rightResult.Right);
            Assert.Equal(-w, rightResult.Left);

            // 오른쪽 모니터의 왼쪽 변(x 0)에도 붙는다.
            var onRightMonitor = new PixelRect(thr - 1, 100, thr - 1 + w, 100 + h);
            var secondResult = StepWithPick(session, MoveTo(startCursor, start, onRightMonitor), w, h, TwoMonitors, thr);
            Assert.Equal(0, secondResult.Left);

            // 작업 표시줄 위 변(Work Bottom 1032)에도 붙는다.
            var nearBottom = new PixelRect(-1000, 1032 - h + thr - 1, -1000 + w, 1032 + thr - 1);
            var bottomResult = StepWithPick(session, MoveTo(startCursor, start, nearBottom), w, h, TwoMonitors, thr);
            Assert.Equal(1032, bottomResult.Bottom);
        }
    }

    // 창을 start에서 target으로 옮기는 커서 위치.
    private static PixelPoint MoveTo(PixelPoint startCursor, PixelRect start, PixelRect target) =>
        new(startCursor.X + (target.Left - start.Left), startCursor.Y + (target.Top - start.Top));

    // Y가 어긋난 보조 모니터

    [Theory]
    [InlineData(1036)]
    [InlineData(1084)]
    public void OffsetSecondaryMonitor_TopAndBottomSnapWithZeroDifference(int workBottom)
    {
        var monitors = new[]
        {
            new Monitor(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1032)),
            new Monitor(new PixelRect(1920, 4, 3840, 1084), new PixelRect(1920, 4, 3840, workBottom))
        };
        var work = monitors[1].Work;

        foreach (var scale in Scales)
        foreach (var heightDip in HeightsDip)
        {
            var w = Px(OverlayLayoutMetrics.WindowWidthDip, scale);
            var h = Px(heightDip, scale);
            var thr = Px(OverlayLayoutMetrics.SnapThresholdDip, scale);

            var start = new PixelRect(2500, 100, 2500 + w, 100 + h);
            var startCursor = CenterOf(start);
            var session = OverlayDragSnap.Begin(start, startCursor);

            var nearTop = new PixelRect(2500, work.Top + thr - 1, 2500 + w, work.Top + thr - 1 + h);
            var topResult = StepWithPick(session, MoveTo(startCursor, start, nearTop), w, h, monitors, thr);
            Assert.Equal(4, topResult.Top);
            Assert.Equal(work.Top, topResult.Top);

            var bottomEdge = work.Bottom - (thr - 1);
            var nearBottom = new PixelRect(2500, bottomEdge - h, 2500 + w, bottomEdge);
            var bottomResult = StepWithPick(session, MoveTo(startCursor, start, nearBottom), w, h, monitors, thr);
            Assert.Equal(work.Bottom, bottomResult.Bottom);
            Assert.Equal(0, bottomResult.Bottom - work.Bottom);

            var nearLeft = new PixelRect(1920 + thr - 1, 100, 1920 + thr - 1 + w, 100 + h);
            var leftResult = StepWithPick(session, MoveTo(startCursor, start, nearLeft), w, h, monitors, thr);
            Assert.Equal(1920, leftResult.Left);
        }
    }

    // 무작위 경로

    [Fact]
    public void RandomPaths_PreserveSizeSnapOnlyToEdgesAndAreIndependentOfStepOrder()
    {
        var rng = new Random(Seed);
        var layouts = new[] { TwoMonitors, SingleMonitor };

        for (var path = 0; path < 500; path++)
        {
            var monitors = layouts[rng.Next(layouts.Length)];
            var scale = Scales[rng.Next(Scales.Length)];
            var w = Px(OverlayLayoutMetrics.WindowWidthDip, scale);
            var h = Px(HeightsDip[rng.Next(HeightsDip.Length)], scale);
            var thr = Px(OverlayLayoutMetrics.SnapThresholdDip, scale);

            var start = RandomWindowNearEdges(rng, monitors, w, h, thr);
            var startCursor = CenterOf(start);
            var session = OverlayDragSnap.Begin(start, startCursor);

            var cursors = new List<PixelPoint>();
            var results = new List<PixelRect>();
            var cursor = startCursor;
            for (var k = 0; k < 20; k++)
            {
                cursor = new PixelPoint(cursor.X + rng.Next(-40, 41), cursor.Y + rng.Next(-40, 41));
                cursors.Add(cursor);

                var proposed = OverlayDragSnap.Propose(session, cursor, w, h);
                var work = PickWork(proposed, monitors);
                var result = OverlayDragSnap.Step(session, cursor, w, h, work, thr);
                results.Add(result);

                var context = $"path {path} step {k}: proposed={proposed} result={result} work={work}";
                Assert.True(result.Width == w && result.Height == h, $"크기 보존 {context}");

                var dx = result.Left - proposed.Left;
                var dy = result.Top - proposed.Top;
                Assert.True(Math.Abs(dx) <= thr && Math.Abs(dy) <= thr, $"임계 초과 {context}");
                if (dx != 0)
                    Assert.True(result.Left == work.Left || result.Right == work.Right, $"가로 변 불일치 {context}");
                if (dy != 0)
                    Assert.True(result.Top == work.Top || result.Bottom == work.Bottom, $"세로 변 불일치 {context}");
            }

            // 걸음 순서를 섞어도 같은 커서는 같은 결과다(경로 독립).
            var order = Enumerable.Range(0, cursors.Count).OrderBy(_ => rng.Next()).ToList();
            foreach (var idx in order)
            {
                var again = StepWithPick(session, cursors[idx], w, h, monitors, thr);
                Assert.True(again == results[idx], $"경로 의존 path {path} step {idx}: {again} != {results[idx]}");
            }
        }
    }

    // 인자 검사

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-1, 100)]
    [InlineData(100, 0)]
    [InlineData(100, -1)]
    public void InvalidSize_ThrowsArgumentOutOfRange(int width, int height)
    {
        var session = OverlayDragSnap.Begin(new PixelRect(0, 0, 311, 426), new PixelPoint(10, 10));
        var work = SingleMonitor[0].Work;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OverlayDragSnap.Propose(session, new PixelPoint(20, 20), width, height));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OverlayDragSnap.Step(session, new PixelPoint(20, 20), width, height, work, 12));
    }

    [Fact]
    public void Propose_UsesStartWindowPlusCursorDelta_AndGivenSize()
    {
        var session = OverlayDragSnap.Begin(new PixelRect(100, 200, 411, 626), new PixelPoint(150, 250));

        var proposed = OverlayDragSnap.Propose(session, new PixelPoint(140, 300), 200, 48);

        Assert.Equal(new PixelRect(90, 250, 290, 298), proposed);
    }

    [Fact]
    public void ShouldLogDragEnd_SameRect_ReturnsFalse()
    {
        var start = new PixelRect(100, 200, 411, 626);

        Assert.False(OverlayDragSnap.ShouldLogDragEnd(start, new PixelRect(100, 200, 411, 626)));
    }

    [Fact]
    public void ShouldLogDragEnd_PositionOnlyDiffers_ReturnsTrue()
    {
        var start = new PixelRect(100, 200, 411, 626);

        // 같은 크기(311x426)로 위치만 옮겼다.
        Assert.True(OverlayDragSnap.ShouldLogDragEnd(start, new PixelRect(101, 200, 412, 626)));
        Assert.True(OverlayDragSnap.ShouldLogDragEnd(start, new PixelRect(100, 199, 411, 625)));
    }

    [Fact]
    public void ShouldLogDragEnd_SizeOnlyDiffers_ReturnsTrue()
    {
        var start = new PixelRect(100, 200, 411, 626);

        // Left/Top은 그대로이고 Right 또는 Bottom만 달라 크기가 바뀌었다.
        Assert.True(OverlayDragSnap.ShouldLogDragEnd(start, new PixelRect(100, 200, 412, 626)));
        Assert.True(OverlayDragSnap.ShouldLogDragEnd(start, new PixelRect(100, 200, 411, 248)));
    }
}
