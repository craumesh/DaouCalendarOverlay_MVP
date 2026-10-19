using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using DaouCalendarOverlay.Models;
using DaouCalendarOverlay.Services;
using WpfBorder = System.Windows.Controls.Border;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfDropShadowEffect = System.Windows.Media.Effects.DropShadowEffect;
using WpfFrameworkElement = System.Windows.FrameworkElement;
using WpfGrid = System.Windows.Controls.Grid;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;
using WpfSize = System.Windows.Size;
using WpfThickness = System.Windows.Thickness;
using WpfUIElement = System.Windows.UIElement;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;
using WpfVisibility = System.Windows.Visibility;
using WpfVisualTreeHelper = System.Windows.Media.VisualTreeHelper;

namespace DaouCalendarOverlay.Tests;

/// <summary>
/// 막대에 붙여 여는 오늘 일정 목록(MainWindow 코드비하인드)의 계약을 원문으로 고정한다.
/// MainWindow를 띄우는 UI 시험이 없으므로, 떼기 뒤 펼침 배치가 XAML 기본값과 같은지,
/// 날짜 셀과 막대 경로가 서로 다른 필터를 쓰는지, 목록에서 상세로 바꿀 때 떼기가 일어나지 않는지를 원문으로 확인한다.
/// </summary>
public sealed class AttachedDayListSourceTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string CodeBehind() =>
        File.ReadAllText(RepositoryPaths.Combine("DaouCalendarOverlay", "Views", "MainWindow.xaml.cs"));

    private static XElement Named(XDocument doc, string name) =>
        doc.Descendants().Single(e => (string?)e.Attribute(X + "Name") == name);

    /// <summary>
    /// signature로 시작하는 메서드 본문을 돌려준다. 블록 본문이면 짝이 맞는 중괄호까지, 식 본문이면 첫 세미콜론까지다.
    /// </summary>
    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"메서드를 찾지 못함: {signature}");
        Assert.True(
            source.IndexOf(signature, start + signature.Length, StringComparison.Ordinal) < 0,
            $"같은 시그니처가 두 번 이상 있음: {signature}");

        var afterSignature = start + signature.Length;
        var brace = source.IndexOf('{', afterSignature);
        var arrow = source.IndexOf("=>", afterSignature, StringComparison.Ordinal);
        if (arrow >= 0 && (brace < 0 || arrow < brace))
        {
            var end = source.IndexOf(';', arrow);
            Assert.True(end > arrow, $"식 본문의 끝을 찾지 못함: {signature}");
            return source.Substring(arrow, end - arrow + 1);
        }

        Assert.True(brace >= 0, $"본문을 찾지 못함: {signature}");
        var depth = 0;
        for (var i = brace; i < source.Length; i++)
        {
            if (source[i] == '{')
                depth++;
            else if (source[i] == '}' && --depth == 0)
                return source.Substring(brace, i - brace + 1);
        }

        throw new InvalidOperationException($"본문의 끝을 찾지 못함: {signature}");
    }

    private static int CountOf(string text, string token)
    {
        var count = 0;
        for (var i = text.IndexOf(token, StringComparison.Ordinal); i >= 0; i = text.IndexOf(token, i + token.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    // "#AARRGGBB"를 코드의 Color.FromArgb 인자 형식 "0xAA, 0xRR, 0xGG, 0xBB"로 바꾼다.
    private static string ToFromArgbArguments(string hex)
    {
        Assert.Matches("^#[0-9A-Fa-f]{8}$", hex);
        var parts = Enumerable.Range(0, 4).Select(i => "0x" + hex.Substring(1 + i * 2, 2).ToUpperInvariant());
        return string.Join(", ", parts);
    }

    /// <summary>
    /// 두 오버레이(목록·상세)는 쉬는 상태에서 카드 사각형과 같은 Margin·Height로 놓이고 창 밖으로 그려지지 않는다(ClipToBounds).
    /// 어두운 배경판은 기존 오버레이 Margin 9를 물려받아 화면 위치가 7.4.0b와 같다.
    /// </summary>
    [Fact]
    public void MainWindowXaml_DayOverlays_MatchCardRect()
    {
        var doc = XDocument.Load(RepositoryPaths.Combine("DaouCalendarOverlay", "Views", "MainWindow.xaml"));

        foreach (var name in new[] { "DayEventsOverlay", "EventDetailsOverlay" })
        {
            var overlay = Named(doc, name);
            Assert.Equal("True", (string?)overlay.Attribute("ClipToBounds"));
            Assert.Equal("Top", (string?)overlay.Attribute("VerticalAlignment"));
            Assert.Equal("0,390,0,0", (string?)overlay.Attribute("Margin"));
            Assert.Equal("426", (string?)overlay.Attribute("Height"));
            Assert.Null(overlay.Attribute("Grid.Row"));
            Assert.Null(overlay.Attribute("Grid.RowSpan"));
        }

        foreach (var name in new[] { "DayEventsBackdrop", "EventDetailsBackdrop" })
        {
            var backdrop = Named(doc, name);
            Assert.Equal("9", (string?)backdrop.Attribute("Margin"));
            Assert.Equal("#9A080A0E", (string?)backdrop.Attribute("Background"));
        }

        Assert.Equal("Center", (string?)Named(doc, "DayEventsCard").Attribute("VerticalAlignment"));
        Assert.Equal("Center", (string?)Named(doc, "EventDetailsCard").Attribute("VerticalAlignment"));

        // 붙인 상태에서 그림자를 없애고 떼면 되돌리므로 두 효과에 이름이 있어야 한다.
        foreach (var name in new[] { "DayEventsCardShadow", "EventDetailsCardShadow" })
        {
            var shadow = Named(doc, name);
            Assert.Equal("DropShadowEffect", shadow.Name.LocalName);
            Assert.Equal("28", (string?)shadow.Attribute("BlurRadius"));
            Assert.Equal("5", (string?)shadow.Attribute("ShadowDepth"));
            Assert.Equal("0.45", (string?)shadow.Attribute("Opacity"));
        }

        var layout = Named(doc, "WindowLayout");
        var layer = Named(doc, "TransitionLayer");
        Assert.Same(layer, layout.Elements().Last());
        Assert.Null(layer.Attribute("Grid.Row"));
        Assert.Null(layer.Attribute("Grid.RowSpan"));
    }

    /// <summary>떼기 뒤 날짜 셀 경로가 붙인 모드 배치를 물려받지 않도록, 복원 값이 카드 사각형과 XAML 기본값에 맞아야 한다.</summary>
    [Fact]
    public void RestoreOverlayPlacement_MatchesXamlDefaults()
    {
        var doc = XDocument.Load(RepositoryPaths.Combine("DaouCalendarOverlay", "Views", "MainWindow.xaml"));
        var source = CodeBehind();
        var restore = MethodBody(source, "private void RestoreOverlayPlacement()");

        foreach (var name in new[] { "DayEventsOverlay", "EventDetailsOverlay" })
        {
            Assert.Contains($"{name}.Margin = RootChrome.Margin;", restore, StringComparison.Ordinal);
            Assert.Contains($"{name}.Height = RootChrome.Height;", restore, StringComparison.Ordinal);
        }

        foreach (var name in new[] { "DayEventsBackdrop", "EventDetailsBackdrop" })
            Assert.Contains($"{name}.Background = CreateBackdropBrush();", restore, StringComparison.Ordinal);

        foreach (var name in new[] { "DayEventsCard", "EventDetailsCard" })
        {
            var alignment = (string?)Named(doc, name).Attribute("VerticalAlignment");
            Assert.Contains($"{name}.VerticalAlignment = VerticalAlignment.{alignment};", restore, StringComparison.Ordinal);
        }

        Assert.Contains("DayEventsCard.Effect = DayEventsCardShadow;", restore, StringComparison.Ordinal);
        Assert.Contains("EventDetailsCard.Effect = EventDetailsCardShadow;", restore, StringComparison.Ordinal);

        var brush = MethodBody(source, "private static SolidColorBrush CreateBackdropBrush()");
        var hex = (string)Named(doc, "DayEventsBackdrop").Attribute("Background")!;
        Assert.Contains($"Color.FromArgb({ToFromArgbArguments(hex)})", brush, StringComparison.Ordinal);
        Assert.Contains("Freeze()", brush, StringComparison.Ordinal);
    }

    /// <summary>
    /// 떼기의 마무리(억제 해제, 순환 재판정)는 조건 없이 이 순서로 돈다. 창을 옮기거나 클램프하지 않는다.
    /// 저장 예약은 붙인 동안 창이 옮겨졌을 때만 한다(CloseAttached_SavesOnlyWhenWindowMovedWhileAttached가 고정한다).
    /// 전환 중 분기를 두지 않는 대신, 전환 중에는 CloseAttached가 불리지 않도록 호출부를 고정한다(아래 시험).
    /// </summary>
    [Fact]
    public void CloseAttached_FinishesUnconditionally_InSpecOrder()
    {
        var body = MethodBody(CodeBehind(), "private void CloseAttached()");

        foreach (var forbidden in new[] { "_transitionInProgress", "ApplyWindowRectPx(", "ClampToVisibleWorkArea", "_preAttach" })
            Assert.DoesNotContain(forbidden, body, StringComparison.Ordinal);

        var order = new[]
        {
            "_returnToDayListAfterDetails = false;",
            "DayEventsOverlay.Visibility = Visibility.Collapsed;",
            "EventDetailsOverlay.Visibility = Visibility.Collapsed;",
            "RestoreOverlayPlacement();",
            "_attached = null;",
            "_suppressBoundsSave = false;",
            "UpdateTickerState();",
        };
        var positions = order.Select(token => body.IndexOf(token, StringComparison.Ordinal)).ToArray();
        Assert.All(positions, p => Assert.True(p >= 0));
        for (var i = 1; i < positions.Length; i++)
            Assert.True(positions[i - 1] < positions[i], $"{order[i - 1]} 다음에 {order[i]}가 와야 한다");
    }

    /// <summary>
    /// 붙인 동안 화면 설정 변경(ClampToVisibleWorkArea 등)으로 창이 옮겨지면 저장이 막혀 있어 설정의 Left/Top이 옛 값으로 남는다.
    /// 그래서 붙일 때 창 사각형(물리 px)을 기억하고, 떼기에서 현재 창 사각형이 다를 때만 저장을 예약한다.
    /// 평소 떼기(창이 그대로)는 창 이동 0회·저장 0회다.
    /// </summary>
    [Fact]
    public void CloseAttached_SavesOnlyWhenWindowMovedWhileAttached()
    {
        var source = CodeBehind();
        Assert.Equal(1, CountOf(source, "private PixelRect? _attachedWindowPx;"));

        var open = MethodBody(source, "private void OpenAttachedDayList()");
        Assert.Equal(1, CountOf(open, "_attachedWindowPx = win;"));
        Assert.True(
            open.IndexOf("GetWindowRectPx() is not PixelRect win", StringComparison.Ordinal) >= 0,
            "OpenAttachedDayList가 기억하는 사각형은 가드에서 읽은 창 사각형이어야 한다");

        var close = MethodBody(source, "private void CloseAttached()");
        Assert.Equal(1, CountOf(close, "ScheduleBoundsSave();"));
        Assert.Equal(1, CountOf(close, "_attachedWindowPx = null;"));
        Assert.DoesNotContain("ApplyWindowRectPx(", close, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveWindowBoundsAsync", close, StringComparison.Ordinal);

        // 저장 예약은 억제를 푼 뒤에, 그리고 기억한 사각형이 있고 현재 창 사각형과 다를 때만 한다.
        var unsuppress = close.IndexOf("_suppressBoundsSave = false;", StringComparison.Ordinal);
        var condition = close.IndexOf("now != before", StringComparison.Ordinal);
        var schedule = close.IndexOf("ScheduleBoundsSave();", StringComparison.Ordinal);
        Assert.True(unsuppress >= 0 && condition > unsuppress && schedule > condition, "억제 해제 → 비교 → 저장 예약 순서여야 한다");
        var ifLine = close.LastIndexOf("if (", schedule, StringComparison.Ordinal);
        var guard = close.Substring(ifLine, schedule - ifLine);
        Assert.Contains("windowAtAttach is PixelRect before", guard, StringComparison.Ordinal);
        Assert.Contains("GetWindowRectPx() is PixelRect now", guard, StringComparison.Ordinal);
        Assert.Contains("now != before", guard, StringComparison.Ordinal);

        // 붙이지 않은 채 떼면(_attached == null) 아무것도 하지 않는다.
        Assert.True(
            close.IndexOf("if (_attached is null)", StringComparison.Ordinal) < unsuppress,
            "붙어 있지 않으면 먼저 반환해야 한다");
    }

    /// <summary>
    /// 끌기 중 배율이 바뀌면(모니터 사이로 끌 때) 작업 영역 클램프를 하지 않는다. 클램프하면 모니터 경계에서 카드가 밀려나
    /// 밀려난 거리가 끌기 세션에 남고, 높은 배율에서 낮은 배율로 천천히 끌어서는 넘어갈 수 없다. 클램프는 EndCardDrag의 1회다.
    /// 새 창은 이전 창의 중심을 보존한다(PlanDragDpiReplacement). 이전 창은 cardBefore와 이전 배율(_placementScale)의 배치로 얻고,
    /// 그 창이 가장 많이 걸친 모니터(배율을 바꾼 모니터)는 반 px 반올림 방향과 왕복 예측에만 쓴다. 중심 보존 창이 그 모니터를 벗어날 때만
    /// WPF가 적용한 OS 제안 창의 왼쪽 위를 쓴다.
    /// </summary>
    [Fact]
    public void ReplaceAfterDpiChange_DuringDrag_DoesNotClamp()
    {
        var source = CodeBehind();
        var replace = MethodBody(source, "private void ReplaceAfterDpiChange(");

        var branchStart = replace.IndexOf("if (_dragSession is not null)", StringComparison.Ordinal);
        var restStart = replace.IndexOf("if ((cardBefore ?? GetCardRectPx()) is not PixelRect card)", StringComparison.Ordinal);
        Assert.True(branchStart >= 0, "_dragSession 분기가 있어야 한다");
        Assert.True(restStart > branchStart, "끌기 중 분기는 쉬는 배치 계산보다 앞에 있어야 한다");
        var branch = replace.Substring(branchStart, restStart - branchStart);

        foreach (var forbidden in new[] { "PlanRestingLayout", "ClampWindowToWorkArea", "ClampToVisibleWorkArea", "PlanExpandedPlacement" })
            Assert.DoesNotContain(forbidden, branch, StringComparison.Ordinal);

        var ordered = new[]
        {
            "cardBefore ?? GetCardRectPx()",
            "CanvasMetrics.FromScale(_placementScale)",
            "OverlayLayoutPlanner.WindowForCard(dragBefore, _placement, previousMetrics.WidthPx, previousMetrics.HeightPx)",
            "var monitor = GetWorkAreaPx(windowBefore);",
            "OverlayLayoutPlanner.PlanDragDpiReplacement(dragBefore, _placement, previousMetrics, _displayMode, m, toward)",
            "if (GetWorkAreaPx(dragLayout.Window) != monitor && GetWindowRectPx() is PixelRect suggested)",
            "ApplyRestingLayout(dragLayout, scale);",
            "_dragSession = OverlayDragSnap.Begin(dragCard, dragCursor);",
            "return;",
        };
        var at = -1;
        foreach (var item in ordered)
        {
            var next = branch.IndexOf(item, at + 1, StringComparison.Ordinal);
            Assert.True(next > at, $"끌기 중 분기에서 '{item}'이 순서대로 있어야 한다");
            at = next;
        }

        // 작업 영역은 모니터 식별(반올림 방향·왕복 예측)에만 쓰고 창 사각형을 그 안으로 옮기지 않는다.
        Assert.Equal(2, CountOf(branch, "GetWorkAreaPx("));
        Assert.Equal(1, CountOf(branch, "ApplyRestingLayout("));

        // 끌기 중이 아닐 때의 재배치는 그대로 카드 왼쪽 위를 보존하는 PlanRestingLayout(클램프 포함)이다.
        var rest = replace.Substring(restStart);
        Assert.Contains("OverlayLayoutPlanner.PlanRestingLayout(", rest, StringComparison.Ordinal);
        Assert.DoesNotContain("PlanDragDpiReplacement", rest, StringComparison.Ordinal);

        // 끌기 끝의 클램프 1회는 EndCardDrag가 맡는다.
        var end = MethodBody(source, "private void EndCardDrag(");
        Assert.Equal(1, CountOf(end, "ClampToVisibleWorkArea();"));

        // 이전 배율은 카드 배치와 함께 ApplyCardPlacement만 정한다.
        Assert.Equal(2, CountOf(source, "_placementScale = ")); // 선언 초기값 1 + ApplyCardPlacement 1
        Assert.Contains("_placementScale = scale;", MethodBody(source, "private void ApplyCardPlacement(CardPlacement placement, double scale)"), StringComparison.Ordinal);

        // 순수 함수는 작업 영역을 받지 않고 클램프하지 않는다.
        var planner = File.ReadAllText(RepositoryPaths.Combine("DaouCalendarOverlay", "Services", "OverlayLayoutPlanner.cs"));
        var planBody = MethodBody(planner, "public static RestingLayout PlanDragDpiReplacement(");
        Assert.DoesNotContain("ClampWindowToWorkArea", planBody, StringComparison.Ordinal);
        Assert.DoesNotContain("work", planBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// 전환 중 CloseAttached가 억제를 풀면 최소화 상태와 펼친 창의 위치가 따로 저장될 수 있다(불변 조건 4).
    /// 그래서 CloseAttached를 부르는 곳을 5곳으로 고정하고, 어느 곳도 전환 중에는 닿지 않게 한다.
    /// </summary>
    [Fact]
    public void CloseAttached_IsNeverReachedDuringTransition()
    {
        var source = CodeBehind();

        // 호출부: HideOverlay, DetachIfAllOverlaysClosed, OnMiniBarLeaving, MiniAgenda_MouseLeftButtonUp, ReplaceAfterDpiChange.
        Assert.Equal(5, CountOf(source, "CloseAttached();"));

        // 펼치기는 전환 플래그를 세우기 전에 떼고, OnMiniBarLeaving은 이곳에서만 부른다.
        Assert.Equal(1, CountOf(source, "OnMiniBarLeaving();"));
        var expand = MethodBody(source, "private Task BeginExpandAsync()");
        var leave = expand.IndexOf("OnMiniBarLeaving();", StringComparison.Ordinal);
        var flag = expand.IndexOf("_transitionInProgress = true;", StringComparison.Ordinal);
        var stop = expand.IndexOf("_saveBoundsTimer?.Stop();", StringComparison.Ordinal);
        Assert.True(leave >= 0 && flag > leave && stop > flag, "떼기 → 전환 플래그 → 떼기가 예약한 저장 취소 순서여야 한다");

        // 숨기기는 전환을 먼저 끝내고, 끌기를 끝낸 뒤 뗀다.
        var hide = MethodBody(source, "public void HideOverlay()");
        var hideFinish = hide.IndexOf("FinishTransitionNow();", StringComparison.Ordinal);
        var hideEnd = hide.IndexOf("EndCardDrag(cancel: false);", StringComparison.Ordinal);
        var hideClose = hide.IndexOf("CloseAttached();", StringComparison.Ordinal);
        Assert.True(
            hideFinish >= 0 && hideEnd > hideFinish && hideClose > hideEnd,
            "HideOverlay는 FinishTransitionNow → EndCardDrag → CloseAttached 순서여야 한다");

        // 클릭 처리기와 붙이기 모두 전환 중이면 아무것도 하지 않는다. 그러면 전환 중 _attached는 항상 null이라
        // DetachIfAllOverlaysClosed도 전환 중에는 CloseAttached에 닿지 않는다.
        var click = MethodBody(source, "private void MiniAgenda_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)");
        Assert.True(
            click.IndexOf("if (_transitionInProgress)", StringComparison.Ordinal) <
            click.IndexOf("CloseAttached();", StringComparison.Ordinal));
        var open = MethodBody(source, "private void OpenAttachedDayList()");
        Assert.StartsWith(
            "if (_transitionInProgress ||",
            open.TrimStart('{').TrimStart().Split('\n').SkipWhile(l => l.TrimStart().StartsWith("//", StringComparison.Ordinal)).First().Trim(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void DayCellPath_UsesSearchFilteredChips_AttachedPath_UsesUnfilteredChips()
    {
        var source = CodeBehind();
        var dayCell = MethodBody(source, "private void ShowDayEvents(DateTime date)");
        Assert.Contains("_vm.GetEventChipsForDate(date)", dayCell, StringComparison.Ordinal);
        Assert.DoesNotContain("Unfiltered", dayCell, StringComparison.Ordinal);

        var open = MethodBody(source, "private void OpenAttachedDayList()");
        Assert.Contains("_vm.GetUnfilteredEventChipsForDate(", open, StringComparison.Ordinal);
        Assert.DoesNotContain("GetEventChipsForDate(", open, StringComparison.Ordinal);
    }

    /// <summary>붙이기는 창을 옮기지 않는다. 오버레이 Margin·Height, 배경판, 카드 정렬, 그림자만 바꾼다.</summary>
    [Fact]
    public void OpenAttachedDayList_NeverMovesWindow_RemovesShadows()
    {
        var open = MethodBody(CodeBehind(), "private void OpenAttachedDayList()");

        Assert.Equal(0, CountOf(open, "ApplyWindowRectPx("));
        Assert.Equal(0, CountOf(open, "SetWindowPos("));
        foreach (var forbidden in new[] { "Grid.SetRow", "GridLength", "ResizeWindow", "ToAnchor", "_preAttachWindowPx", "AttachBelowRow", "AttachAboveRow" })
            Assert.DoesNotContain(forbidden, open, StringComparison.Ordinal);

        // 전환 중이거나 끌기 중이면 아무것도 하지 않는다.
        Assert.Contains("_transitionInProgress || _dragSession is not null", open, StringComparison.Ordinal);

        var suppress = open.IndexOf("_suppressBoundsSave = true;", StringComparison.Ordinal);
        Assert.True(suppress >= 0, "붙이기 전에 위치 저장 억제가 있어야 한다");
        Assert.Contains("_saveBoundsTimer?.Stop();", open, StringComparison.Ordinal);
        Assert.Contains("OverlayLayoutPlanner.AttachRegion(", open, StringComparison.Ordinal);
        Assert.Contains("OverlayLayoutPlanner.ChooseAttachDirection(bar, GetWorkAreaPx(bar), m.AttachRegionPx)", open, StringComparison.Ordinal);

        // px → DIP는 px / scale 대입이다.
        Assert.Contains("region.OffsetPx / scale", open, StringComparison.Ordinal);
        Assert.Contains("region.HeightPx / scale - OverlayLayoutMetrics.AttachGapDip", open, StringComparison.Ordinal);

        // 막대 카드와 붙인 목록 카드 사이 간격은 막대 쪽 변(Below는 위)에만 둔다.
        Assert.Contains("new Thickness(0, regionTop + OverlayLayoutMetrics.AttachGapDip, 0, 0)", open, StringComparison.Ordinal);
        Assert.Contains("new Thickness(0, regionTop, 0, 0)", open, StringComparison.Ordinal);

        // 알파가 0이 아닌 픽셀은 카드와 목록 카드뿐이어야 하므로 배경판은 투명, 그림자는 없다.
        Assert.Equal(1, CountOf(open, "DayEventsBackdrop.Background = Brushes.Transparent;"));
        Assert.Equal(1, CountOf(open, "EventDetailsBackdrop.Background = Brushes.Transparent;"));
        Assert.Equal(2, CountOf(open, "Brushes.Transparent"));
        Assert.Contains("DayEventsCard.Effect = null;", open, StringComparison.Ordinal);
        Assert.Contains("EventDetailsCard.Effect = null;", open, StringComparison.Ordinal);

        // 목록 내용은 배치를 끝낸 뒤 채운다.
        var show = open.IndexOf("ShowDayEvents(", StringComparison.Ordinal);
        Assert.True(show > open.IndexOf("EventDetailsCard.Effect = null;", StringComparison.Ordinal));
        Assert.True(show > suppress);
    }

    /// <summary>창 사각형이 곧 카드 사각형이다. 투명 여백 상수가 코드에 남아 있으면 조용히 엉뚱한 값을 낸다.</summary>
    [Fact]
    public void CodeBehind_HasNoCardInset()
    {
        // 토큰을 나눠 써서 이 파일 자신이 검색에 걸리지 않게 한다.
        Assert.Equal(0, CountOf(CodeBehind(), "Window" + "MarginDip"));
    }

    [Fact]
    public void MainWindowXaml_IsFixedCanvas()
    {
        var path = RepositoryPaths.Combine("DaouCalendarOverlay", "Views", "MainWindow.xaml");
        var xaml = File.ReadAllText(path);
        var doc = XDocument.Load(path);

        Assert.Contains("Width=\"311\" Height=\"828\"", xaml, StringComparison.Ordinal);
        var window = doc.Root!;
        Assert.Equal(
            OverlayLayoutMetrics.WindowWidthDip,
            double.Parse((string)window.Attribute("Width")!, CultureInfo.InvariantCulture));
        Assert.Equal(
            OverlayLayoutMetrics.WindowHeightDip,
            double.Parse((string)window.Attribute("Height")!, CultureInfo.InvariantCulture));
        Assert.Equal("None", (string?)window.Attribute("WindowStyle"));
        Assert.Equal("True", (string?)window.Attribute("AllowsTransparency"));
        Assert.Equal("Transparent", (string?)window.Attribute("Background"));
        Assert.Equal("NoResize", (string?)window.Attribute("ResizeMode"));
        Assert.Equal("False", (string?)window.Attribute("ShowInTaskbar"));

        // WindowLayout은 행이 없고 배경도 없다(알파 0 영역은 클릭이 통과해야 한다).
        var layout = Named(doc, "WindowLayout");
        Assert.DoesNotContain(layout.Elements(), e => e.Name.LocalName == "Grid.RowDefinitions");
        Assert.Null(layout.Attribute("Background"));
        Assert.Equal(0, CountOf(xaml, "AttachAboveRow") + CountOf(xaml, "AttachBelowRow") + CountOf(xaml, "x:Name=\"CardRow\""));

        var chrome = Named(doc, "RootChrome");
        Assert.Equal("Top", (string?)chrome.Attribute("VerticalAlignment"));
        Assert.Equal("0,390,0,0", (string?)chrome.Attribute("Margin"));
        Assert.Equal("426", (string?)chrome.Attribute("Height"));
        Assert.Equal("0", (string?)chrome.Attribute("Padding"));
        Assert.Null(chrome.Attribute("Grid.Row"));
        Assert.Equal(390.0, OverlayLayoutMetrics.BarSlotOffsetDip);
        Assert.Equal(426.0, OverlayLayoutMetrics.ExpandedCardHeightDip);

        // 보정 애니메이션 동안 내용 Margin이 음수가 되므로 카드 안쪽에서 잘라 내야 한다.
        var host = Named(doc, "CardContentHost");
        Assert.Equal("True", (string?)host.Attribute("ClipToBounds"));
        Assert.Same(chrome, host.Parent);
        Assert.Equal("8", (string?)Named(doc, "ExpandedContent").Attribute("Margin"));
        Assert.Equal("6,4", (string?)Named(doc, "MiniBarContent").Attribute("Margin"));
        Assert.Same(host, Named(doc, "ExpandedContent").Parent);
        Assert.Same(host, Named(doc, "MiniBarContent").Parent);
    }

    /// <summary>접기·펼치기·붙이기·떼기 경로는 창을 옮기지도 키우지도 않는다(SetWindowPos 0회).</summary>
    [Fact]
    public void TransitionsAndAttach_NeverMoveWindow()
    {
        var source = CodeBehind();
        var signatures = new[]
        {
            "private Task BeginCollapseAsync()",
            "private Task BeginExpandAsync()",
            "private void StartCollapseAnimation(int generation)",
            "private void StartExpandAnimation(int generation)",
            "private void ApplyCollapsedState(CardPlacement target)",
            "private void ApplyExpandedState(CardPlacement target)",
            "private void FinishTransitionNow()",
            "private void AbortTransition()",
            "private void RunTransitionStep(int generation, Action step)",
            "private void OpenAttachedDayList()",
            "private void CloseAttached()",
            "private void RestoreOverlayPlacement()",
        };
        var unqualifiedWindowPosition = new Regex(@"(?<![\w.])(Left|Top)\s*=(?!=)");

        foreach (var signature in signatures)
        {
            var body = MethodBody(source, signature);
            Assert.Equal(0, CountOf(body, "ApplyWindowRectPx("));
            Assert.Equal(0, CountOf(body, "SetWindowPos("));
            Assert.False(unqualifiedWindowPosition.IsMatch(body), $"{signature}: 한정자 없는 Left/Top 대입이 있다");
        }
    }

    /// <summary>창 크기는 XAML의 고정값이다. 코드에서 창 크기를 상수로 대입하거나 내용 크기에 맞추지 않는다.</summary>
    [Fact]
    public void CodeBehind_NeverAssignsWindowSize()
    {
        var source = CodeBehind();

        Assert.False(
            Regex.IsMatch(source, @"(?<![\w.])(Height|Width)\s*=\s*OverlayLayoutMetrics"),
            "코드비하인드가 창 Height/Width에 OverlayLayoutMetrics 상수를 대입한다");
        Assert.Equal(0, CountOf(source, "SizeToContent"));
    }

    /// <summary>
    /// ApplyWindowRectPx를 부르는 곳은 끌기·클램프·쉬는 배치뿐이다. 출현은 정의 1회와 호출 4회로 총 5회이고,
    /// 네 허용 메서드가 모두 있으며 각 본문에 1회 이상, 합 4회다.
    /// </summary>
    [Fact]
    public void ApplyWindowRectPx_CalledOnlyFromAllowedMethods()
    {
        var source = CodeBehind();
        var allowed = new[]
        {
            "private void ContinueCardDrag(",
            "private void EndCardDrag(",
            "private void ClampToVisibleWorkArea(",
            "private void ApplyRestingLayout(",
        };

        var counts = allowed.Select(signature =>
        {
            Assert.True(source.Contains(signature, StringComparison.Ordinal), $"허용 메서드가 없다: {signature}");
            return CountOf(MethodBody(source, signature), "ApplyWindowRectPx(");
        }).ToArray();

        Assert.All(counts, count => Assert.True(count >= 1, "허용 메서드 본문에 ApplyWindowRectPx 호출이 1회 이상 있어야 한다"));
        Assert.Equal(4, counts.Sum());
        Assert.Equal(1, CountOf(source, "private void ApplyWindowRectPx("));
        Assert.Equal(5, CountOf(source, "ApplyWindowRectPx("));
        Assert.Equal(1, CountOf(source, "private static extern bool SetWindowPos("));
        Assert.Equal(1, CountOf(source, "SetWindowPos(hwnd"));
    }

    /// <summary>
    /// 저장 좌표는 보이는 카드의 왼쪽 위(DIP = 카드 px ÷ 창 배율)다. WPF Left/Top(창 왼쪽 위)은 카드보다 막대 칸 오프셋만큼 위라
    /// 그대로 저장하면 settings.json의 Left/Top 의미(7.4.0b와 같은 카드 왼쪽 위)가 깨진다.
    /// </summary>
    [Fact]
    public void Save_UsesCardTopLeft()
    {
        var source = CodeBehind();
        foreach (var forbidden in new[]
                 {
                     "SaveWindowBoundsAsync(Left, Top)",
                     "SaveOverlayLayoutAsync(true, Left, Top)",
                     "SaveOverlayLayoutAsync(false, Left, Top)",
                     "SaveOverlayLayoutAsync(minimized, Left, Top)",
                 })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        }

        // 카드 왼쪽 위(DIP)는 카드 px ÷ GetDpiScale()로만 만든다.
        var topLeft = MethodBody(source, "private (double Left, double Top)? GetCardTopLeftDip()");
        Assert.Contains("GetCardRectPx()", topLeft, StringComparison.Ordinal);
        Assert.Contains("GetDpiScale()", topLeft, StringComparison.Ordinal);
        Assert.Contains("card.Left / scale", topLeft, StringComparison.Ordinal);
        Assert.Contains("card.Top / scale", topLeft, StringComparison.Ordinal);

        var tick = MethodBody(source, "private async void SaveBoundsTimer_Tick(object? sender, EventArgs e)");
        Assert.Equal(1, CountOf(tick, "GetCardTopLeftDip()"));
        Assert.Equal(1, CountOf(tick, "SaveWindowBoundsAsync("));
        Assert.Contains("SaveWindowBoundsAsync(tl.Left, tl.Top)", tick, StringComparison.Ordinal);
        Assert.Contains("LogService.Warn(\"ui.saveBounds\"", tick, StringComparison.Ordinal);
        Assert.True(
            tick.IndexOf("GetCardTopLeftDip()", StringComparison.Ordinal) < tick.IndexOf("SaveWindowBoundsAsync(", StringComparison.Ordinal),
            "저장 타이머: 카드 왼쪽 위를 저장 호출보다 먼저 읽어야 한다");

        foreach (var (signature, minimized) in new[]
                 {
                     ("private void ApplyCollapsedState(CardPlacement target)", "true"),
                     ("private void ApplyExpandedState(CardPlacement target)", "false"),
                 })
        {
            var body = MethodBody(source, signature);
            Assert.Equal(1, CountOf(body, "GetCardTopLeftDip()"));
            Assert.Equal(1, CountOf(body, "SaveOverlayLayoutAsync("));
            Assert.Contains($"SaveOverlayLayoutAsync({minimized}, tl.Left, tl.Top)", body, StringComparison.Ordinal);
            Assert.Contains("RunGuardedAsync(\"ui.saveOverlayLayout\"", body, StringComparison.Ordinal);
            Assert.Contains("LogService.Warn(\"ui.saveOverlayLayout\"", body, StringComparison.Ordinal);
            // 카드 왼쪽 위는 끝 배치를 적용한 뒤에 읽는다(접은 막대·펼친 카드의 위치를 저장).
            var place = IndexOrFail(body, "ApplyCardPlacement(");
            var read = IndexOrFail(body, "GetCardTopLeftDip()", place);
            Assert.True(read > place, $"{signature}: GetCardTopLeftDip가 ApplyCardPlacement 뒤여야 한다");
        }
    }

    /// <summary>
    /// 클램프는 보이는 카드 사각형을 기준으로 하고, 모니터도 카드가 가장 많이 걸친 모니터(GetWorkAreaPx)로 고른다(I12).
    /// 창 핸들 기준(Screen.FromHandle)은 828 DIP 캔버스가 가장 많이 걸친 모니터라 카드 모니터와 다를 수 있다.
    /// 끌기 중에는 클램프하지 않는다(I11).
    /// </summary>
    [Fact]
    public void Clamp_IsCardBased()
    {
        var source = CodeBehind();
        var clamp = MethodBody(source, "private void ClampToVisibleWorkArea()");
        Assert.Contains("_dragSession is not null", clamp, StringComparison.Ordinal);
        Assert.Contains("_transitionInProgress", clamp, StringComparison.Ordinal);
        Assert.Contains("_placement.InWindow(", clamp, StringComparison.Ordinal);
        Assert.Contains("GetWorkAreaPx(", clamp, StringComparison.Ordinal);
        Assert.Contains("OverlayLayoutPlanner.ClampWindowToWorkArea(card, GetWorkAreaPx(card))", clamp, StringComparison.Ordinal);
        Assert.Contains("ApplyWindowRectPx(win.Offset(clamped.Left - card.Left, clamped.Top - card.Top))", clamp, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(clamp, "ApplyWindowRectPx("));
        Assert.Contains("if (clamped == card)", clamp, StringComparison.Ordinal);

        Assert.DoesNotContain("Forms.Screen.FromHandle", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Screen.FromHandle", source, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(source, "Screen.FromRectangle"));
        var workArea = MethodBody(source, "private static PixelRect GetWorkAreaPx(PixelRect rect)");
        Assert.Equal(1, CountOf(workArea, "Screen.FromRectangle"));
    }

    /// <summary>
    /// 시작 배치는 WndProc 훅을 단 뒤 SourceInitialized에서 동기로 한다(창이 보이기 전이라 깜빡이지 않는다).
    /// 복원은 저장된 카드 왼쪽 위(DIP)에서 DipToPx로 px를 직접 만들어 PlanRestingLayout을 거친다(R5: WPF Top 대입만으로 정하면 소수 배율에서 1px씩 밀린다).
    /// </summary>
    [Fact]
    public void InitialPlacement_RunsAfterHook()
    {
        var source = CodeBehind();
        var initialized = MethodBody(source, "private void MainWindow_SourceInitialized(object? sender, EventArgs e)");
        var hook = IndexOrFail(initialized, "AddHook(WndProc)");
        var place = IndexOrFail(initialized, "ApplyInitialPlacement();");
        var clamp = IndexOrFail(initialized, "BeginInvoke(new Action(ClampToVisibleWorkArea))");
        Assert.True(hook < place, "AddHook(WndProc)가 ApplyInitialPlacement보다 먼저여야 한다");
        Assert.True(place < clamp, "ApplyInitialPlacement가 클램프 예약보다 먼저여야 한다");
        Assert.Equal(1, CountOf(source, "ApplyInitialPlacement();"));

        var initial = MethodBody(source, "private void ApplyInitialPlacement()");
        Assert.Contains("PlanRestingLayout(", initial, StringComparison.Ordinal);
        Assert.Contains("DipToPx(_savedCardTopLeftDip.Left, scale)", initial, StringComparison.Ordinal);
        Assert.Contains("DipToPx(_savedCardTopLeftDip.Top, scale)", initial, StringComparison.Ordinal);
        Assert.Contains("CanvasMetrics.FromScale(scale)", initial, StringComparison.Ordinal);
        Assert.Contains("GetWorkAreaPx(", initial, StringComparison.Ordinal);
        Assert.Contains("ApplyRestingLayout(", initial, StringComparison.Ordinal);
        Assert.Contains("창 캔버스 높이 부족", initial, StringComparison.Ordinal);
        Assert.Contains("ReportHandlerFailure(\"ui.layout\", ex)", initial, StringComparison.Ordinal);
        // 실패하면 _placement를 XAML 초기 배치(막대 칸 위 변 = BarSlotOffsetDip)와 같은 px 배치로 맞춘다. 기본값 (0, 0)이 남으면
        // 카드 판정·저장이 창 왼쪽 위를 카드로 보아 다음 시작에서 막대 칸만큼 밀린다.
        var failure = IndexOrFail(initial, "ReportHandlerFailure(\"ui.layout\", ex)");
        var fallbackGuard = IndexOrFail(initial, "_placement == default", failure);
        var fallbackPlace = IndexOrFail(initial, "m.Slot : m.ExpandedDown", fallbackGuard);
        // 폴백 조건은 배율이 유한하고 0보다 클 때뿐이고, 폴백 자체의 예외는 경고로만 남는다(SourceInitialized 밖으로 나가지 않는다).
        var outerCatch = IndexOrFail(initial, "catch (Exception ex)");
        var fallbackTry = IndexOrFail(initial, "try", outerCatch);
        Assert.True(fallbackTry < fallbackGuard, "폴백은 바깥 catch 안의 try로 감싸야 한다");
        IndexOrFail(initial, "double.IsFinite(scale) && scale > 0", fallbackTry);
        var fallbackCatch = IndexOrFail(initial, "catch (Exception fallbackEx)", fallbackPlace);
        IndexOrFail(initial, "LogService.Warn(\"ui.layout\"", fallbackCatch);
        Assert.Equal(1, CountOf(initial, "ReportHandlerFailure("));
        Assert.Equal(0, CountOf(initial, "ApplyWindowRectPx("));
        Assert.DoesNotContain("Top =", initial, StringComparison.Ordinal);
        Assert.DoesNotContain("Left =", initial, StringComparison.Ordinal);

        // 쉬는 배치: 카드 배치를 먼저 적용하고, 창은 다를 때만 1회 옮긴다.
        var resting = MethodBody(source, "private void ApplyRestingLayout(RestingLayout layout, double scale)");
        var card = IndexOrFail(resting, "ApplyCardPlacement(layout.Placement, scale);");
        var differs = IndexOrFail(resting, "GetWindowRectPx() != layout.Window", card);
        var move = IndexOrFail(resting, "ApplyWindowRectPx(layout.Window);", differs);
        Assert.True(card < differs && differs < move);
        Assert.Equal(1, CountOf(resting, "ApplyWindowRectPx("));

        Assert.Equal(1, CountOf(source, "private (double Left, double Top) _savedCardTopLeftDip;"));
    }

    /// <summary>
    /// ApplySettings는 전환·붙임·끌기 중에는 위치를 건드리지 않는다(I11).
    /// 핸들이 없으면(생성자 경로) HWND가 올바른 모니터에 생기도록 카드 기준 임시 창 위치(Top = 카드 위 - 막대 칸 오프셋)만 둔다.
    /// 핸들이 있으면 카드 px를 DipToPx로 만들고 배치는 그대로 둔 채 창만 옮긴다.
    /// </summary>
    [Fact]
    public void ApplySettings_SkipsDuringDrag()
    {
        var source = CodeBehind();
        var apply = MethodBody(source, "public void ApplySettings(AppSettings settings)");
        Assert.Contains("_transitionInProgress || _attached is not null || _dragSession is not null", apply, StringComparison.Ordinal);
        Assert.Contains("_dragSession is not null", apply, StringComparison.Ordinal);
        Assert.Contains("BarSlotOffsetDip", apply, StringComparison.Ordinal);
        Assert.Contains("Top = cardTop - OverlayLayoutMetrics.BarSlotOffsetDip;", apply, StringComparison.Ordinal);
        Assert.Contains("Left = cardLeft;", apply, StringComparison.Ordinal);
        Assert.Contains("_savedCardTopLeftDip = (cardLeft, cardTop);", apply, StringComparison.Ordinal);
        Assert.Contains("SystemParameters.WorkArea.Right - OverlayLayoutMetrics.WindowWidthDip - 20", apply, StringComparison.Ordinal);
        Assert.Contains("SystemParameters.WorkArea.Top + 20", apply, StringComparison.Ordinal);
        Assert.Contains("if (_hwndSource is null)", apply, StringComparison.Ordinal);
        Assert.Contains("OverlayLayoutPlanner.WindowForCard(card, _placement, win.Width, win.Height)", apply, StringComparison.Ordinal);
        Assert.Contains("ApplyRestingLayout(", apply, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.BeginInvoke(new Action(ClampToVisibleWorkArea))", apply, StringComparison.Ordinal);
        Assert.Equal(0, CountOf(apply, "ApplyWindowRectPx("));

        // WPF Left/Top 대입(줄 맨 앞의 한정자 없는 대입문)은 핸들 없는 분기 안에만 있다(1회씩).
        var leftAssign = System.Text.RegularExpressions.Regex.Matches(apply, @"(?m)^[ \t]*Left[ \t]*=[^=]");
        var topAssign = System.Text.RegularExpressions.Regex.Matches(apply, @"(?m)^[ \t]*Top[ \t]*=[^=]");
        Assert.Single(leftAssign);
        Assert.Single(topAssign);
        var noHandle = IndexOrFail(apply, "if (_hwndSource is null)");
        var withHandle = IndexOrFail(apply, "else if (GetWindowRectPx() is PixelRect win)", noHandle);
        Assert.True(noHandle < leftAssign[0].Index && leftAssign[0].Index < withHandle, "WPF Left 대입은 핸들 없는 분기 안에만 있어야 한다");
        Assert.True(noHandle < topAssign[0].Index && topAssign[0].Index < withHandle, "WPF Top 대입은 핸들 없는 분기 안에만 있어야 한다");
    }

    /// <summary>
    /// 위치 저장 타이머가 대기 중이면(끌기·클램프 뒤 400 ms 안) 메모리 설정값(Left/Top)이 현재 카드보다 오래됐다.
    /// 그때 설정창 저장·항상 위 전환으로 ApplySettings가 불려도 창을 오래된 위치로 옮기지 않는다.
    /// 타이머 Tick은 첫 줄에서 타이머를 멈추고 나서 카드 위치를 읽으므로, 저장이 시작된 뒤에는 IsEnabled가 false다.
    /// 타이머를 저장 없이 멈추는 붙이기·전환 중단 경로는 대기 중인 저장을 버리지 않는다.
    /// </summary>
    [Fact]
    public void ApplySettings_SkipsWhileBoundsSavePending()
    {
        var source = CodeBehind();
        var apply = MethodBody(source, "public void ApplySettings(AppSettings settings)");
        var noHandle = IndexOrFail(apply, "if (_hwndSource is null)");
        var pending = IndexOrFail(apply, "else if (_saveBoundsTimer is { IsEnabled: true })", noHandle);
        var withHandle = IndexOrFail(apply, "else if (GetWindowRectPx() is PixelRect win)", pending);
        Assert.True(noHandle < pending && pending < withHandle);
        Assert.Equal(1, CountOf(apply, "_saveBoundsTimer is { IsEnabled: true }"));

        // 대기 분기는 위치를 다시 적용하지 않는다(창 호출, 배치 적용, Left/Top 대입이 없다).
        var pendingBranch = apply.Substring(pending, withHandle - pending);
        foreach (var forbidden in new[] { "ApplyRestingLayout(", "ApplyWindowRectPx(", "ApplyCardPlacement(", "Left =", "Top =" })
            Assert.DoesNotContain(forbidden, pendingBranch, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(apply, "ApplyRestingLayout("));
        Assert.True(IndexOrFail(apply, "ApplyRestingLayout(") > withHandle, "ApplyRestingLayout은 핸들 있는 분기 안에만 있어야 한다");

        var tick = MethodBody(source, "private async void SaveBoundsTimer_Tick(object? sender, EventArgs e)");
        var stop = IndexOrFail(tick, "_saveBoundsTimer?.Stop();");
        var read = IndexOrFail(tick, "GetCardTopLeftDip()");
        Assert.True(stop < read, "Tick은 카드 위치를 읽기 전에 타이머를 멈춰야 한다");

        // 붙이기: 대기 중인 저장을 먼저 기록한 뒤 억제·정지한다.
        var open = MethodBody(source, "private void OpenAttachedDayList()");
        var flush = IndexOrFail(open, "if (_saveBoundsTimer is { IsEnabled: true })");
        var flushCall = IndexOrFail(open, "SaveBoundsTimer_Tick(_saveBoundsTimer, EventArgs.Empty);", flush);
        Assert.True(flushCall < IndexOrFail(open, "_suppressBoundsSave = true;"));
        Assert.True(flushCall < IndexOrFail(open, "_saveBoundsTimer?.Stop();"));

        // 전환 중단: 억제를 푼 뒤 현재 카드 위치 저장을 다시 예약한다.
        var abort = MethodBody(source, "private void AbortTransition()");
        var unsuppress = IndexOrFail(abort, "_suppressBoundsSave = false;");
        IndexOrFail(abort, "ScheduleBoundsSave();", unsuppress);
    }

    /// <summary>지운 전환 메모·창 크기 변경 계산의 흔적이 코드에 남아 있지 않다.</summary>
    [Fact]
    public void Removed_SymbolsAbsent()
    {
        var source = CodeBehind();
        foreach (var token in new[] { "_lastTransition", "_preAttachWindowPx", "PlanTransitionFor" })
            Assert.DoesNotContain(token, source, StringComparison.Ordinal);

        // 옛 상수 이름은 조각을 이어 만든다. 저장소 전체에서 옛 이름 0건을 확인하는 git grep(T2-A7)에 이 시험 자체가 걸리지 않게 한다.
        var removedServiceTokens = new[] { "TransitionMemo", "Mini" + "WindowHeightDip", "Expanded" + "WindowHeightDip" };
        Assert.Equal("MiniCardHeightDip".Replace("Card", "Window", StringComparison.Ordinal), removedServiceTokens[1]);
        Assert.Equal("ExpandedCardHeightDip".Replace("Card", "Window", StringComparison.Ordinal), removedServiceTokens[2]);

        var services = Directory.GetFiles(RepositoryPaths.Combine("DaouCalendarOverlay", "Services"), "*.cs", SearchOption.AllDirectories);
        Assert.NotEmpty(services);
        foreach (var file in services)
        {
            var text = File.ReadAllText(file);
            foreach (var token in removedServiceTokens)
                Assert.False(text.Contains(token, StringComparison.Ordinal), $"{Path.GetFileName(file)}에 {token}가 남아 있다");
        }
    }

    /// <summary>
    /// 끌기는 마우스 캡처로 직접 구현한다. 시작에서 캡처와 세션을 만들고, 이동마다 GetCursorPos(물리 px)로 카드 위치를 다시 계산하며,
    /// 끝에서는 세션을 먼저 비운 뒤 캡처를 풀어(R9 재진입) 끝 클램프와 로그가 1회만 일어나게 한다.
    /// </summary>
    [Fact]
    public void CardDrag_UsesCaptureAndCursorBasedSnap()
    {
        var source = CodeBehind();

        var down = MethodBody(source, "private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)");
        Assert.Contains("BeginCardDrag()", down, StringComparison.Ordinal);
        Assert.Contains("_dragSession is not null", down, StringComparison.Ordinal);

        var begin = MethodBody(source, "private bool BeginCardDrag(");
        Assert.Contains("CaptureMouse()", begin, StringComparison.Ordinal);
        Assert.Contains("OverlayDragSnap.Begin(", begin, StringComparison.Ordinal);
        Assert.Contains("ReportHandlerFailure(\"ui.snap\", ex)", begin, StringComparison.Ordinal);

        var cont = MethodBody(source, "private void ContinueCardDrag(");
        Assert.Contains("TryGetCursorPx()", cont, StringComparison.Ordinal);
        Assert.Contains("OverlayDragSnap.Step(", cont, StringComparison.Ordinal);
        Assert.Contains("WindowForCard(", cont, StringComparison.Ordinal);
        Assert.DoesNotContain("GetPosition(", cont, StringComparison.Ordinal);
        Assert.DoesNotContain("ClampToVisibleWorkArea", cont, StringComparison.Ordinal);
        var contCatch = cont.LastIndexOf("catch (Exception ex)", StringComparison.Ordinal);
        Assert.True(contCatch >= 0, "ContinueCardDrag에 catch (Exception ex)가 있어야 한다");
        var contTail = cont.Substring(contCatch);
        var contClear = contTail.IndexOf("_dragSession = null;", StringComparison.Ordinal);
        var contRelease = contTail.IndexOf("ReleaseMouseCapture();", StringComparison.Ordinal);
        var contReport = contTail.IndexOf("ReportHandlerFailure(\"ui.snap\", ex)", StringComparison.Ordinal);
        Assert.True(
            contClear >= 0 && contRelease > contClear && contReport > contRelease,
            "ContinueCardDrag의 catch는 세션 비우기 → 캡처 해제 → 보고 순서여야 한다");
        Assert.Equal(0, CountOf(cont, "LogService.Info(\"ui.snap\""));

        var replace = MethodBody(source, "private void ReplaceAfterDpiChange(");
        var replaceCatch = replace.LastIndexOf("catch (Exception ex)", StringComparison.Ordinal);
        Assert.True(replaceCatch >= 0, "ReplaceAfterDpiChange에 catch (Exception ex)가 있어야 한다");
        var replaceTail = replace.Substring(replaceCatch);
        var replaceClear = replaceTail.IndexOf("_dragSession = null;", StringComparison.Ordinal);
        var replaceRelease = replaceTail.IndexOf("ReleaseMouseCapture();", StringComparison.Ordinal);
        var replaceReport = replaceTail.IndexOf("ReportHandlerFailure(\"ui.snap\", ex)", StringComparison.Ordinal);
        Assert.True(
            replaceClear >= 0 && replaceRelease > replaceClear && replaceReport > replaceRelease,
            "ReplaceAfterDpiChange의 catch는 세션 비우기 → 캡처 해제 → 보고 순서여야 한다");

        var end = MethodBody(source, "private void EndCardDrag(");
        var clear = end.IndexOf("_dragSession = null;", StringComparison.Ordinal);
        var release = end.IndexOf("ReleaseMouseCapture()", StringComparison.Ordinal);
        Assert.True(clear >= 0 && release > clear, "EndCardDrag에서 _dragSession = null;이 ReleaseMouseCapture()보다 먼저여야 한다");
        Assert.Contains("ShouldLogDragEnd(", end, StringComparison.Ordinal);
        Assert.Contains("드래그 끝: 카드=(", end, StringComparison.Ordinal);
        Assert.Contains("ClampToVisibleWorkArea();", end, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(end, "LogService.Info(\"ui.snap\""));
        Assert.Contains("var snapOn = _snapEnabled && !_positionLocked && _attached is null;", end, StringComparison.Ordinal);
        Assert.Contains("OverlaySnap.DetectEdges(f, w, 0)", end, StringComparison.Ordinal);
        Assert.Contains("ReportHandlerFailure(\"ui.snap\", ex)", end, StringComparison.Ordinal);

        var up = MethodBody(source, "private void Window_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)");
        Assert.Contains("EndCardDrag(false);", up, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true;", up, StringComparison.Ordinal);

        var lost = MethodBody(source, "private void Window_LostMouseCapture(object sender, MouseEventArgs e)");
        Assert.Contains("ReferenceEquals(e.OriginalSource, this)", lost, StringComparison.Ordinal);
        Assert.Contains("_dragSession is not null", lost, StringComparison.Ordinal);
        Assert.Contains("EndCardDrag(false);", lost, StringComparison.Ordinal);

        // Esc는 PreviewKeyDown 본문의 첫 if에서 처리하고 숨기기로 이어지지 않는다.
        var keyDown = MethodBody(source, "private async void Window_PreviewKeyDown(object sender, WpfKeyEventArgs e)");
        var firstIf = keyDown.IndexOf("if (", StringComparison.Ordinal);
        Assert.True(firstIf >= 0);
        var firstIfLine = keyDown.Substring(firstIf, keyDown.IndexOf('\n', firstIf) - firstIf);
        Assert.Contains("_dragSession is not null", firstIfLine, StringComparison.Ordinal);
        Assert.Contains("Key.Escape", firstIfLine, StringComparison.Ordinal);
        Assert.True(
            keyDown.IndexOf("EndCardDrag(true);", StringComparison.Ordinal) <
            keyDown.IndexOf("OverlayShortcutPolicy.Resolve(", StringComparison.Ordinal),
            "Esc 끌기 취소는 단축키 해석보다 먼저여야 한다");

        Assert.Equal(1, CountOf(source, "private static extern bool GetWindowRect"));
        Assert.Equal(1, CountOf(source, "private static extern bool SetWindowPos"));
        Assert.Equal(1, CountOf(source, "private static extern bool GetCursorPos"));
        Assert.Equal(1, CountOf(source, "private DragSnapSession? _dragSession;"));
        Assert.DoesNotContain("_dragSnapUsed", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// DragMove와 WM_MOVING 이동 루프는 없어졌다. WndProc는 WM_GETMINMAXINFO(최대 추적 크기)와 WM_DPICHANGED(BeginInvoke 재배치)만 다루고,
    /// XAML에는 끌기 이벤트 세 개가 연결돼 있다.
    /// </summary>
    [Fact]
    public void MoveLoop_Removed()
    {
        var source = CodeBehind();
        foreach (var removed in new[]
                 {
                     "DragMove(", "WmMoving", "WmEnterSizeMove", "WmExitSizeMove",
                     "TrySnapMoving", "OnEnterSizeMove", "OnExitSizeMove",
                 })
        {
            Assert.DoesNotContain(removed, source, StringComparison.Ordinal);
        }

        var wndProc = MethodBody(source, "private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)");
        Assert.Contains("0x0024", wndProc, StringComparison.Ordinal);
        Assert.Contains("MaxTrackSizePx", wndProc, StringComparison.Ordinal);
        Assert.Contains("ReplaceAfterDpiChange", wndProc, StringComparison.Ordinal);
        Assert.Contains("0x02E0", wndProc, StringComparison.Ordinal);
        Assert.Contains("GetCardRectPx()", wndProc, StringComparison.Ordinal);
        Assert.DoesNotContain("handled = true", wndProc, StringComparison.Ordinal);
        Assert.Contains("ReportHandlerFailure(\"ui.layout\", ex)", wndProc, StringComparison.Ordinal);
        Assert.Contains("private const int MaxTrackSizePx = 16384;", source, StringComparison.Ordinal);

        // MINMAXINFO 필드 순서는 Win32 정의와 같다.
        var info = MethodBody(source, "private struct MinMaxInfo");
        var order = new[] { "PtReserved", "PtMaxSize", "PtMaxPosition", "PtMinTrackSize", "PtMaxTrackSize" }
            .Select(name => info.IndexOf(name, StringComparison.Ordinal))
            .ToArray();
        Assert.All(order, index => Assert.True(index >= 0));
        Assert.Equal(order.OrderBy(i => i).ToArray(), order);

        var xaml = File.ReadAllText(RepositoryPaths.Combine("DaouCalendarOverlay", "Views", "MainWindow.xaml"));
        Assert.Contains("PreviewMouseMove=\"Window_PreviewMouseMove\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PreviewMouseLeftButtonUp=\"Window_PreviewMouseLeftButtonUp\"", xaml, StringComparison.Ordinal);
        Assert.Contains("LostMouseCapture=\"Window_LostMouseCapture\"", xaml, StringComparison.Ordinal);
    }

    // marker가 있는 여는 태그를 다음 '>'까지 돌려준다.
    private static string OpeningTag(string xaml, string marker)
    {
        var start = xaml.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"XAML에서 찾지 못함: {marker}");
        var end = xaml.IndexOf('>', start);
        Assert.True(end > start, $"여는 태그의 끝을 찾지 못함: {marker}");
        return xaml.Substring(start, end - start + 1);
    }

    /// <summary>목록에서 상세로 바꾸는 순간 두 오버레이가 모두 Collapsed인 구간이 없어야 붙인 목록이 떼어지지 않는다.</summary>
    [Fact]
    public void DayListEvent_Click_ShowsDetailsBeforeCollapsingList_WithoutDetachPath()
    {
        var body = MethodBody(CodeBehind(), "private void DayListEvent_Click(object sender, RoutedEventArgs e)");

        var show = body.IndexOf("ShowEventDetails(", StringComparison.Ordinal);
        var collapse = body.IndexOf("DayEventsOverlay.Visibility = Visibility.Collapsed;", StringComparison.Ordinal);
        Assert.True(show >= 0 && collapse >= 0);
        Assert.True(show < collapse, "상세를 먼저 Visible로 만든 뒤 목록을 숨겨야 한다");
        Assert.DoesNotContain("HideDayEvents(", body, StringComparison.Ordinal);
        Assert.DoesNotContain("CloseAttached(", body, StringComparison.Ordinal);
        Assert.DoesNotContain("DetachIfAllOverlaysClosed(", body, StringComparison.Ordinal);
    }

    [Fact]
    public void HideDayEventsAndHideEventDetails_DetachOnlyAtEnd_WhenBothCollapsed()
    {
        var source = CodeBehind();
        foreach (var signature in new[] { "private void HideDayEvents()", "private void HideEventDetails()" })
        {
            var body = MethodBody(source, signature).TrimEnd('}').TrimEnd();
            Assert.EndsWith("DetachIfAllOverlaysClosed();", body, StringComparison.Ordinal);
        }

        var detach = MethodBody(source, "private void DetachIfAllOverlaysClosed()");
        Assert.Contains("_attached is not null", detach, StringComparison.Ordinal);
        Assert.Contains("DayEventsOverlay.Visibility == Visibility.Collapsed", detach, StringComparison.Ordinal);
        Assert.Contains("EventDetailsOverlay.Visibility == Visibility.Collapsed", detach, StringComparison.Ordinal);
        Assert.Contains("CloseAttached();", detach, StringComparison.Ordinal);

        // 상세에서 목록으로 돌아갈 때는 목록을 Visible로 만든 뒤에 검사하므로 떼지 않는다.
        var hideDetails = MethodBody(source, "private void HideEventDetails()");
        Assert.True(
            hideDetails.IndexOf("DayEventsOverlay.Visibility = Visibility.Visible;", StringComparison.Ordinal) <
            hideDetails.IndexOf("DetachIfAllOverlaysClosed();", StringComparison.Ordinal));
    }

    [Fact]
    public void ExpandStart_DetachesBeforeReadingWindowRect()
    {
        var source = CodeBehind();
        var leaving = MethodBody(source, "private void OnMiniBarLeaving()");
        Assert.Contains("CloseAttached();", leaving, StringComparison.Ordinal);

        var expand = MethodBody(source, "private Task BeginExpandAsync()");
        var leave = expand.IndexOf("OnMiniBarLeaving();", StringComparison.Ordinal);
        var reread = expand.IndexOf("GetWindowRectPx() is not PixelRect win", StringComparison.Ordinal);
        Assert.True(leave >= 0 && reread > leave, "떼기 뒤에 창 사각형을 다시 읽어야 한다");

        var hide = MethodBody(source, "public void HideOverlay()");
        var finish = hide.IndexOf("FinishTransitionNow();", StringComparison.Ordinal);
        var close = hide.IndexOf("CloseAttached();", StringComparison.Ordinal);
        var hideCall = hide.IndexOf("Hide();", close < 0 ? 0 : close, StringComparison.Ordinal);
        Assert.True(finish >= 0 && close > finish && hideCall > close);
    }

    /// <summary>
    /// 전환의 목표는 카드 배치(CardPlacement)다. 접기는 막대 칸(CanvasMetrics.Slot), 펼치기는 PlanExpandedPlacement가 정한다.
    /// 목표는 _pendingPlacement에 두고, 중단하면 전환 전 배치(_placement)로 되돌린다. 창 사각형 기록은 없다.
    /// 쉬는 상태의 배치는 ApplyCardPlacement만 바꾼다.
    /// </summary>
    [Fact]
    public void Transitions_PlanCardPlacement()
    {
        var source = CodeBehind();
        var collapse = MethodBody(source, "private Task BeginCollapseAsync()");
        var expand = MethodBody(source, "private Task BeginExpandAsync()");

        Assert.Contains("CanvasMetrics.FromScale(", collapse, StringComparison.Ordinal);
        Assert.Contains(".Slot;", collapse, StringComparison.Ordinal);
        Assert.Contains("_pendingPlacement =", collapse, StringComparison.Ordinal);
        Assert.Contains("PlanExpandedPlacement(", expand, StringComparison.Ordinal);
        Assert.Contains("GetWorkAreaPx(m.Slot.InWindow(win))", expand, StringComparison.Ordinal);
        Assert.Contains("_pendingPlacement =", expand, StringComparison.Ordinal);
        foreach (var body in new[] { collapse, expand })
        {
            Assert.Contains("_transitionInProgress || _dragSession is not null || GetWindowRectPx() is null", body, StringComparison.Ordinal);
            Assert.DoesNotContain("ApplyWindowRectPx(", body, StringComparison.Ordinal);
        }

        var abort = MethodBody(source, "private void AbortTransition()");
        Assert.Contains("ApplyCardPlacement(_placement", abort, StringComparison.Ordinal);
        Assert.Contains("_pendingPlacement = null", abort, StringComparison.Ordinal);

        var apply = MethodBody(source, "private void ApplyCardPlacement(CardPlacement placement, double scale)");
        Assert.Contains("placement.OffsetPx / scale", apply, StringComparison.Ordinal);
        Assert.Contains("placement.HeightPx / scale", apply, StringComparison.Ordinal);
        Assert.Contains("_placement = placement;", apply, StringComparison.Ordinal);
        Assert.Contains("if (_attached is null)", apply, StringComparison.Ordinal);
        Assert.Contains("RestoreOverlayPlacement();", apply, StringComparison.Ordinal);

        var cardRect = MethodBody(source, "private PixelRect? GetCardRectPx()");
        Assert.Contains("_placement.InWindow(win)", cardRect, StringComparison.Ordinal);

        // _placement는 ApplyCardPlacement만 바꾼다(대입 1곳).
        Assert.Equal(1, CountOf(source, "_placement = "));
        Assert.Equal(1, CountOf(source, "private CardPlacement _placement;"));
        Assert.Equal(1, CountOf(source, "private CardPlacement? _pendingPlacement;"));

        // 상태는 메모리에만 둔다. 설정 모델에 나오지 않는다.
        Assert.DoesNotContain(
            "_placement",
            File.ReadAllText(RepositoryPaths.Combine("DaouCalendarOverlay", "Models", "AppSettings.cs")),
            StringComparison.Ordinal);

        // 첫 배치: SourceInitialized는 카드 배치를 직접 정하지 않고 ApplyInitialPlacement에 맡긴다(InitialPlacement_RunsAfterHook).
        var initialized = MethodBody(source, "private void MainWindow_SourceInitialized(object? sender, EventArgs e)");
        Assert.Equal(0, CountOf(initialized, "ApplyCardPlacement("));
        Assert.Equal(1, CountOf(initialized, "ApplyInitialPlacement();"));

        // 초기 모드: 창 Height를 대입하지 않고 카드 Margin·Height만 정한다.
        var initial = MethodBody(source, "private void ApplyInitialDisplayMode(bool minimized)");
        Assert.Contains("RootChrome.Margin = new Thickness(0, OverlayLayoutMetrics.BarSlotOffsetDip, 0, 0);", initial, StringComparison.Ordinal);
        Assert.Contains("OverlayLayoutMetrics.MiniCardHeightDip", initial, StringComparison.Ordinal);
        Assert.Contains("OverlayLayoutMetrics.ExpandedCardHeightDip", initial, StringComparison.Ordinal);
        Assert.Equal(0, CountOf(initial, "ApplyWindowRectPx("));
    }

    /// <summary>
    /// 전환 중 내용이 화면에서 제자리에 있으려면(카드 위 변 + 내용 Margin 위쪽 = 일정) 카드 Margin, 카드 Height,
    /// 두 내용 Margin 보정이 같은 시간·같은 곡선으로 같은 호출에서 시작해야 하고 시작값·끝값 방향이 맞아야 한다.
    /// 접기: 카드 위 변 fromTop → toTop(d 아래로), 펼친 내용 8 → 8 - d, 막대 내용 4 + d → 4.
    /// 펼치기: 카드 위 변 s → t(d 위로), 펼친 내용 8 - d → 8, 막대 내용 4 → 4 + d.
    /// </summary>
    [Fact]
    public void CardAndContentMarginAnimations_ShareOneCurve_AndKeepContentInPlace()
    {
        var source = CodeBehind();
        var height = MethodBody(source, "private static DoubleAnimation CreateHeightAnimation(double from, double to)");
        var thickness = MethodBody(source, "private static ThicknessAnimation CreateThicknessAnimation(Thickness from, Thickness to)");
        foreach (var body in new[] { height, thickness })
        {
            Assert.Contains("Ms(OverlayLayoutMetrics.HeightAnimationMilliseconds)", body, StringComparison.Ordinal);
            Assert.Contains("new CubicEase { EasingMode = EasingMode.EaseInOut }", body, StringComparison.Ordinal);
            Assert.DoesNotContain("BeginTime", body, StringComparison.Ordinal);
        }

        var collapse = MethodBody(source, "private void StartCollapseAnimation(int generation)");
        var expand = MethodBody(source, "private void StartExpandAnimation(int generation)");

        foreach (var (body, tokens) in new[]
                 {
                     (collapse, new[]
                     {
                         "var shrink = CreateHeightAnimation(fromH, toH);",
                         "var slide = CreateThicknessAnimation(new Thickness(0, fromTop, 0, 0), new Thickness(0, toTop, 0, 0));",
                         "var expandedShift = CreateThicknessAnimation(new Thickness(8, 8, 8, 8), new Thickness(8, 8 - d, 8, 8));",
                         "var miniShift = CreateThicknessAnimation(new Thickness(6, 4 + d, 6, 4), new Thickness(6, 4, 6, 4));",
                         "RootChrome.BeginAnimation(HeightProperty, shrink);",
                     }),
                     (expand, new[]
                     {
                         "var grow = CreateHeightAnimation(miniH, cardHeight);",
                         "var slide = CreateThicknessAnimation(new Thickness(0, s, 0, 0), new Thickness(0, t, 0, 0));",
                         "var expandedShift = CreateThicknessAnimation(new Thickness(8, 8 - d, 8, 8), new Thickness(8, 8, 8, 8));",
                         "var miniShift = CreateThicknessAnimation(new Thickness(6, 4, 6, 4), new Thickness(6, 4 + d, 6, 4));",
                         "RootChrome.BeginAnimation(HeightProperty, grow);",
                     }),
                 })
        {
            foreach (var token in tokens)
                Assert.Equal(1, CountOf(body, token));
            Assert.Equal(3, CountOf(body, "CreateThicknessAnimation("));
            Assert.Equal(1, CountOf(body, "RootChrome.BeginAnimation(MarginProperty, slide);"));
            Assert.Equal(1, CountOf(body, "ExpandedContent.BeginAnimation(MarginProperty, expandedShift);"));
            Assert.Equal(1, CountOf(body, "MiniBarContent.BeginAnimation(MarginProperty, miniShift);"));
            // 보정 애니메이션에 지연을 두면 카드 이동과 어긋나 내용이 흔들린다.
            Assert.DoesNotContain("slide.BeginTime", body, StringComparison.Ordinal);
            Assert.DoesNotContain("expandedShift.BeginTime", body, StringComparison.Ordinal);
            Assert.DoesNotContain("miniShift.BeginTime", body, StringComparison.Ordinal);
        }

        // 접기 시작값의 내용 Margin은 애니메이션 시작 전 로컬 값과 같아야 첫 프레임에 튀지 않는다.
        Assert.Contains("MiniBarContent.Margin = new Thickness(6, 4 + d, 6, 4);", collapse, StringComparison.Ordinal);
        // 막대 내용 높이는 쉬는 Margin(위 4·아래 4)으로 계산해야 하므로 보정 Margin 대입보다 앞이다.
        Assert.True(
            IndexOrFail(collapse, "MiniBarContent.Height = MiniBarContentHeightDip();") <
            IndexOrFail(collapse, "MiniBarContent.Margin = new Thickness(6, 4 + d, 6, 4);"));
        // 펼치기 단계 B의 시작값도 로컬 값과 같다. 펼친 내용 높이는 끝 카드 높이 - 테두리 2 - 쉬는 Margin 16이다.
        Assert.Contains("ExpandedContent.Margin = new Thickness(8, 8 - d, 8, 8);", expand, StringComparison.Ordinal);
        Assert.Contains("MiniBarContent.Margin = new Thickness(6, 4, 6, 4);", expand, StringComparison.Ordinal);
        Assert.Contains("var cardHeight = target.HeightPx / scale;", expand, StringComparison.Ordinal);
        Assert.Contains("var miniH = _placement.HeightPx / scale;", expand, StringComparison.Ordinal);
    }

    private static int IndexOrFail(string text, string token, int from = 0)
    {
        var index = text.IndexOf(token, from, StringComparison.Ordinal);
        Assert.True(index >= 0, $"찾지 못함: {token}");
        return index;
    }

    /// <summary>
    /// 접기·펼치기 공유 요소 전환: 날짜·시계 블록은 유령 요소가 사각형을 바꾸며 옮기고 나머지 내용은 페이드한다.
    /// 창은 움직이지 않고 카드 Margin·Height와 내용 Margin 보정만 같은 200 ms 곡선으로 애니메이션한다.
    /// 완료 처리기는 모두 generation을 거치고 펼치기 단계 A 페이드아웃과 높이 애니메이션에만 있다.
    /// 모든 종료 경로는 애니메이션 해제 뒤 공유 요소 상태를 되돌린다.
    /// </summary>
    [Fact]
    public void SharedElementTransition_Invariants()
    {
        var source = CodeBehind();
        var collapse = MethodBody(source, "private void StartCollapseAnimation(int generation)");
        var expand = MethodBody(source, "private void StartExpandAnimation(int generation)");

        // 두 애니메이션 모두 창을 부르지 않고 Thickness 애니메이션으로 카드와 내용 Margin을 옮긴다.
        Assert.Equal(0, CountOf(collapse, "ApplyWindowRectPx("));
        Assert.Equal(0, CountOf(expand, "ApplyWindowRectPx("));
        Assert.Contains("ThicknessAnimation", collapse, StringComparison.Ordinal);
        Assert.Contains("ThicknessAnimation", expand, StringComparison.Ordinal);

        // d 부호: 접기는 d = toTop - fromTop, 펼치기는 d = s - t (모두 0 이상). px → DIP는 px / scale이다.
        Assert.Contains("var d = toTop - fromTop;", collapse, StringComparison.Ordinal);
        Assert.Contains("var fromTop = _placement.OffsetPx / scale;", collapse, StringComparison.Ordinal);
        Assert.Contains("var fromH = _placement.HeightPx / scale;", collapse, StringComparison.Ordinal);
        Assert.Contains("var toTop = target.OffsetPx / scale;", collapse, StringComparison.Ordinal);
        Assert.Contains("var toH = target.HeightPx / scale;", collapse, StringComparison.Ordinal);
        Assert.Contains("var d = s - t;", expand, StringComparison.Ordinal);
        Assert.Contains("var s = _placement.OffsetPx / scale;", expand, StringComparison.Ordinal);
        Assert.Contains("var t = target.OffsetPx / scale;", expand, StringComparison.Ordinal);
        Assert.Contains("new Thickness(6, 4 + d, 6, 4)", collapse, StringComparison.Ordinal);
        Assert.Contains("new Thickness(8, 8 - d, 8, 8)", collapse, StringComparison.Ordinal);
        Assert.Contains("new Thickness(8, 8 - d, 8, 8)", expand, StringComparison.Ordinal);
        Assert.Contains("new Thickness(6, 4 + d, 6, 4)", expand, StringComparison.Ordinal);

        // 완료 처리기는 모두 generation 검사를 거친다. 접기 1개(높이), 펼치기 2개(단계 A 페이드아웃, 높이).
        Assert.Equal(1, CountOf(collapse, "Completed +="));
        Assert.Equal(CountOf(collapse, "Completed +="), CountOf(collapse, "RunTransitionStep(generation"));
        Assert.Equal(2, CountOf(expand, "Completed +="));
        Assert.Equal(CountOf(expand, "Completed +="), CountOf(expand, "RunTransitionStep(generation"));
        Assert.Contains("RunTransitionStep(generation, () => ApplyCollapsedState(target))", collapse, StringComparison.Ordinal);
        Assert.Contains("RunTransitionStep(generation, () => ApplyExpandedState(target))", expand, StringComparison.Ordinal);
        Assert.Contains("shrink.Completed +=", collapse, StringComparison.Ordinal);
        Assert.Contains("grow.Completed +=", expand, StringComparison.Ordinal);
        Assert.Contains("fadeOut.Completed +=", expand, StringComparison.Ordinal);

        // 끝 상태 메서드에 페이드인 경로가 남아 있지 않다(추가 완료 처리기 금지).
        Assert.DoesNotContain("private void FadeIn(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("fadeIn:", source, StringComparison.Ordinal);
        Assert.DoesNotContain("bool fadeIn", source, StringComparison.Ordinal);

        // 유령 이동은 완료 처리기 없이 Left/Top/Width/Height와 두 층의 교차 페이드만 건다.
        var morph = MethodBody(source, "private void StartGhostMorph(Rect from, Rect to, bool toMini)");
        Assert.Equal(0, CountOf(morph, "Completed"));
        foreach (var token in new[]
                 {
                     "SharedClockGhost.BeginAnimation(Canvas.LeftProperty,",
                     "SharedClockGhost.BeginAnimation(Canvas.TopProperty,",
                     "SharedClockGhost.BeginAnimation(WidthProperty,",
                     "SharedClockGhost.BeginAnimation(HeightProperty,",
                     "OverlayLayoutMetrics.HeightAnimationMilliseconds",
                     "EasingMode.EaseInOut",
                     "GhostExpandedClock",
                     "GhostMiniClock",
                 })
            Assert.Contains(token, morph, StringComparison.Ordinal);

        var show = MethodBody(source, "private void ShowGhostAt(Rect r, bool expandedLayerVisible)");
        foreach (var token in new[]
                 {
                     "Canvas.SetLeft(SharedClockGhost, r.X)",
                     "Canvas.SetTop(SharedClockGhost, r.Y)",
                     "SharedClockGhost.Width = r.Width",
                     "SharedClockGhost.Height = r.Height",
                     "GhostExpandedClock.Opacity = expandedLayerVisible ? 1 : 0",
                     "GhostMiniClock.Opacity = expandedLayerVisible ? 0 : 1",
                     "TransitionLayer.Visibility = Visibility.Visible",
                 })
            Assert.Contains(token, show, StringComparison.Ordinal);

        var rectIn = MethodBody(source, "private static Rect RectIn(FrameworkElement e, Visual root)");
        Assert.Contains("TranslatePoint(", rectIn, StringComparison.Ordinal);
        Assert.Contains("ActualWidth", rectIn, StringComparison.Ordinal);
        Assert.Contains("ActualHeight", rectIn, StringComparison.Ordinal);

        // 해제 대상: 유령 4개 속성 + 두 층과 실제 블록 두 개의 Opacity + 카드·내용 Height/Margin.
        var clear = MethodBody(source, "private void ClearTransitionAnimations()");
        foreach (var token in new[]
                 {
                     "SharedClockGhost.BeginAnimation(Canvas.LeftProperty, null)",
                     "SharedClockGhost.BeginAnimation(Canvas.TopProperty, null)",
                     "SharedClockGhost.BeginAnimation(WidthProperty, null)",
                     "SharedClockGhost.BeginAnimation(HeightProperty, null)",
                     "GhostExpandedClock.BeginAnimation(OpacityProperty, null)",
                     "GhostMiniClock.BeginAnimation(OpacityProperty, null)",
                     "ExpandedClockBlock.BeginAnimation(OpacityProperty, null)",
                     "MiniClockBlock.BeginAnimation(OpacityProperty, null)",
                     "ExpandedContent.BeginAnimation(OpacityProperty, null)",
                     "MiniBarContent.BeginAnimation(OpacityProperty, null)",
                     "RootChrome.BeginAnimation(OpacityProperty, null)",
                     "RootChrome.BeginAnimation(HeightProperty, null)",
                 })
            Assert.Contains(token, clear, StringComparison.Ordinal);
        foreach (var target in new[] { "RootChrome", "ExpandedContent", "MiniBarContent" })
            Assert.Equal(1, CountOf(clear, $"{target}.BeginAnimation(MarginProperty, null)"));

        // 끝 상태와 중단은 애니메이션을 먼저 해제하고 그다음 공유 요소 상태를 되돌린다.
        foreach (var signature in new[]
                 {
                     "private void ApplyCollapsedState(CardPlacement target)",
                     "private void ApplyExpandedState(CardPlacement target)",
                     "private void AbortTransition()",
                 })
        {
            var body = MethodBody(source, signature);
            var clearAt = IndexOrFail(body, "ClearTransitionAnimations();");
            var resetAt = IndexOrFail(body, "ResetSharedElementState();");
            Assert.True(clearAt < resetAt, $"{signature}: ClearTransitionAnimations가 ResetSharedElementState보다 앞이어야 한다");
        }

        // 끝 상태: 카드 배치 1회 → 저장 1회(앞이 먼저). 창은 부르지 않는다.
        foreach (var signature in new[]
                 {
                     "private void ApplyCollapsedState(CardPlacement target)",
                     "private void ApplyExpandedState(CardPlacement target)",
                 })
        {
            var body = MethodBody(source, signature);
            Assert.Equal(1, CountOf(body, "ApplyCardPlacement("));
            Assert.Equal(1, CountOf(body, "SaveOverlayLayoutAsync("));
            Assert.True(
                body.IndexOf("ApplyCardPlacement(", StringComparison.Ordinal) <
                body.IndexOf("SaveOverlayLayoutAsync(", StringComparison.Ordinal),
                $"{signature}: 카드 배치가 저장보다 앞이어야 한다");
            Assert.Contains("ApplyCardPlacement(target, GetDpiScale());", body, StringComparison.Ordinal);
            Assert.Contains("_pendingPlacement = null;", body, StringComparison.Ordinal);
            Assert.Equal(0, CountOf(body, "ApplyWindowRectPx("));
            Assert.DoesNotContain("if (target is PixelRect", body, StringComparison.Ordinal);
        }

        var reset = MethodBody(source, "private void ResetSharedElementState()");
        foreach (var token in new[]
                 {
                     "TransitionLayer.Visibility = Visibility.Collapsed",
                     "ExpandedClockBlock.Opacity = 1",
                     "MiniClockBlock.Opacity = 1",
                     "ExpandedContent.Height = double.NaN",
                     "MiniBarContent.Height = double.NaN",
                     "ExpandedContent.VerticalAlignment = VerticalAlignment.Stretch",
                     "MiniBarContent.VerticalAlignment = VerticalAlignment.Stretch",
                     "ExpandedContent.IsHitTestVisible = true",
                     "MiniBarContent.IsHitTestVisible = true",
                     "ExpandedContent.Margin = new Thickness(8)",
                     "MiniBarContent.Margin = new Thickness(6, 4, 6, 4)",
                 })
            Assert.Contains(token, reset, StringComparison.Ordinal);
        // 카드 Margin·Height는 ApplyCardPlacement가 맡는다.
        Assert.DoesNotContain("RootChrome.", reset, StringComparison.Ordinal);

        // 중단: 현재 모드 쪽 내용만 보이고 두 내용 모두 불투명이다. 카드는 전환 전 배치로 돌아가고 대기 목표는 버린다.
        var abort = MethodBody(source, "private void AbortTransition()");
        foreach (var token in new[]
                 {
                     "++_transitionGeneration;",
                     "_displayMode == OverlayDisplayMode.MiniBar",
                     "ExpandedContent.Opacity = 1;",
                     "MiniBarContent.Opacity = 1;",
                     "ExpandedContent.Visibility = ",
                     "MiniBarContent.Visibility = ",
                     "ApplyCardPlacement(_placement, GetDpiScale());",
                     "_pendingPlacement = null;",
                     "_transitionInProgress = false;",
                 })
            Assert.Contains(token, abort, StringComparison.Ordinal);

        // 즉시 확정: 해제 뒤 끝 상태 메서드로 간다(그 안에서 Reset이 돈다).
        var finish = MethodBody(source, "private void FinishTransitionNow()");
        var finishClear = IndexOrFail(finish, "ClearTransitionAnimations();");
        Assert.True(finishClear < IndexOrFail(finish, "ApplyCollapsedState(_pendingPlacement ?? _placement);"));
        Assert.True(finishClear < IndexOrFail(finish, "ApplyExpandedState(_pendingPlacement ?? _placement);"));

        // 애니메이션 꺼짐이면 즉시 적용한다.
        var beginCollapse = MethodBody(source, "private Task BeginCollapseAsync()");
        var beginExpand = MethodBody(source, "private Task BeginExpandAsync()");
        Assert.Contains("SystemParameters.ClientAreaAnimation", beginCollapse, StringComparison.Ordinal);
        Assert.Contains("SystemParameters.ClientAreaAnimation", beginExpand, StringComparison.Ordinal);
        Assert.Contains("ApplyCollapsedState(target);", beginCollapse, StringComparison.Ordinal);
        Assert.Contains("ApplyExpandedState(target);", beginExpand, StringComparison.Ordinal);

        // 접기 t0: 막대 내용을 Visible로 둔 다음 UpdateLayout, 그다음 측정. 창이 그대로라 도착 사각형을 보정하지 않는다.
        var collapseVisible = IndexOrFail(collapse, "MiniBarContent.Visibility = Visibility.Visible;");
        var collapseLayout = IndexOrFail(collapse, "UpdateLayout();", collapseVisible);
        var collapseFrom = IndexOrFail(collapse, "RectIn(ExpandedClockBlock, TransitionLayer)", collapseLayout);
        var collapseTo = IndexOrFail(collapse, "RectIn(MiniClockBlock, TransitionLayer)", collapseLayout);
        Assert.True(collapseFrom > collapseLayout && collapseTo > collapseLayout);
        foreach (var removed in new[] { "AnchorEdgeShiftPx", "ResizeWindow(", "ChooseResizeAnchor(", "target.Left - win.Left" })
            Assert.DoesNotContain(removed, collapse, StringComparison.Ordinal);
        Assert.True(IndexOrFail(collapse, "ShowGhostAt(from, true)") > collapseTo);
        Assert.Contains("StartGhostMorph(from, to, true)", collapse, StringComparison.Ordinal);
        Assert.Contains("OverlayLayoutMetrics.IncomingFadeBeginMilliseconds", collapse, StringComparison.Ordinal);
        Assert.DoesNotContain("RootChrome.VerticalAlignment", collapse, StringComparison.Ordinal);

        // 펼치기: 단계 A와 B 어디에서도 막대 내용을 Collapsed로 바꾸지 않는다(측정 대상이 사라지지 않게).
        Assert.DoesNotContain("MiniBarContent.Visibility = Visibility.Collapsed", expand, StringComparison.Ordinal);
        Assert.DoesNotContain("RootChrome.VerticalAlignment", expand, StringComparison.Ordinal);
        // 단계 B: 펼친 내용 Visible → UpdateLayout → 출발·도착 사각형 측정.
        var stageB = IndexOrFail(expand, "var d = s - t;");
        var expandVisible = IndexOrFail(expand, "ExpandedContent.Visibility = Visibility.Visible;", stageB);
        var expandLayout = IndexOrFail(expand, "UpdateLayout();", expandVisible);
        var expandFrom = IndexOrFail(expand, "RectIn(MiniClockBlock, TransitionLayer)", expandLayout);
        IndexOrFail(expand, "RectIn(ExpandedClockBlock, TransitionLayer)", expandLayout);
        Assert.True(IndexOrFail(expand, "ShowGhostAt(from, false)", expandFrom) > expandFrom);
        Assert.Contains("StartGhostMorph(from, to, false)", expand, StringComparison.Ordinal);
        Assert.Contains("OverlayLayoutMetrics.IncomingFadeBeginMilliseconds", expand, StringComparison.Ordinal);
        // 단계 A도 측정 전에 레이아웃을 확정한다(붙인 목록을 뗀 직후 오버레이 배치가 바뀌었을 수 있다).
        var phaseALayout = IndexOrFail(expand, "UpdateLayout();");
        Assert.True(phaseALayout < IndexOrFail(expand, "RectIn(MiniClockBlock, TransitionLayer)"));
        Assert.True(phaseALayout < IndexOrFail(expand, "fadeOut.Completed +="));

        Assert.Equal(80, OverlayLayoutMetrics.IncomingFadeBeginMilliseconds);
        Assert.Equal(
            OverlayLayoutMetrics.HeightAnimationMilliseconds - OverlayLayoutMetrics.ContentFadeMilliseconds,
            OverlayLayoutMetrics.IncomingFadeBeginMilliseconds);

        // XAML: 이름은 각 1회, 전환 층은 WindowLayout의 마지막 자식이고 입력을 받지 않으며 처음에는 접혀 있다.
        var xamlPath = RepositoryPaths.Combine("DaouCalendarOverlay", "Views", "MainWindow.xaml");
        var xaml = File.ReadAllText(xamlPath);
        foreach (var name in new[] { "TransitionLayer", "SharedClockGhost", "ExpandedClockBlock", "MiniClockBlock", "GhostExpandedClock", "GhostMiniClock" })
            Assert.Equal(1, CountOf(xaml, $"x:Name=\"{name}\""));

        var doc = XDocument.Load(xamlPath);
        var layer = Named(doc, "TransitionLayer");
        Assert.Equal("Canvas", layer.Name.LocalName);
        Assert.Same(layer, Named(doc, "WindowLayout").Elements().Last());
        Assert.Null(layer.Attribute("Grid.Row"));
        Assert.Null(layer.Attribute("Grid.RowSpan"));
        Assert.Equal("False", (string?)layer.Attribute("IsHitTestVisible"));
        Assert.Equal("Collapsed", (string?)layer.Attribute("Visibility"));

        var ghost = Named(doc, "SharedClockGhost");
        Assert.Same(layer, ghost.Parent);
        Assert.Equal("#0BFFFFFF", (string?)ghost.Attribute("Background"));
        Assert.Equal("7", (string?)ghost.Attribute("CornerRadius"));

        // 실제 블록은 해당 TextBlock을 담고, 유령 층은 같은 TextBlock을 ElementName으로 따라간다.
        var expandedBlock = Named(doc, "ExpandedClockBlock");
        var miniBlock = Named(doc, "MiniClockBlock");
        string Bindings(XElement e) => string.Join("|", e.Descendants().Select(d => (string?)d.Attribute("Text") ?? ""));
        string Names(XElement e) => string.Join("|", e.Descendants().Select(d => (string?)d.Attribute(X + "Name") ?? ""));
        foreach (var name in new[] { "TodayDateText", "TodayWeekdayText", "CurrentTimeText" })
        {
            Assert.Contains(name, Names(expandedBlock), StringComparison.Ordinal);
            Assert.Contains($"{{Binding Text, ElementName={name}}}", Bindings(Named(doc, "GhostExpandedClock")), StringComparison.Ordinal);
        }

        foreach (var name in new[] { "MiniDateText", "MiniTimeText" })
        {
            Assert.Contains(name, Names(miniBlock), StringComparison.Ordinal);
            Assert.Contains($"{{Binding Text, ElementName={name}}}", Bindings(Named(doc, "GhostMiniClock")), StringComparison.Ordinal);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // 고정 캔버스 실측. MainWindow를 띄우는 UI 시험이 없으므로 MainWindow.xaml의 WindowLayout을 원문 속성 그대로 읽고
    // 내용 자식(ExpandedContent, MiniBarContent, 두 오버레이 카드의 내용, TransitionLayer)만 비워 STA에서 311 DIP 폭으로 배치한다.
    // 전환은 코드비하인드와 같은 from→to 값을 같은 진행률 e로 보간한 프레임으로 재현한다. 다섯 애니메이션이 같은 곡선(200 ms,
    // CubicEase InOut, 지연 없음)이고 from→to 리터럴이 아래와 같다는 사실은 CardAndContentMarginAnimations_ShareOneCurve_AndKeepContentInPlace와
    // SharedElementTransition_Invariants가 원문으로 고정한다. 여기서는 그 값들이 WPF 레이아웃에서 실제로 내용을 제자리에 두고,
    // 카드 밖으로 나간 내용이 그려지지도 클릭을 받지도 않으며(CardContentHost 클립), 끝 상태가 마지막 프레임과 같음을 잰다.
    // ---------------------------------------------------------------------------------------------

    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    // 진행률 e. 0과 1은 첫·마지막 프레임이고, 나머지는 d·e가 .5 경계에 걸리지 않는 값이다(레이아웃 반올림 동률 회피).
    private static readonly double[] Progress = { 0, 0.13, 0.4, 0.6, 0.87, 1 };

    private const double LayoutEpsilon = 0.01;

    private sealed record CanvasSkeleton(
        WpfGrid Root,
        WpfBorder Card,
        WpfGrid Host,
        WpfGrid Expanded,
        WpfGrid Mini,
        WpfGrid DayOverlay,
        WpfBorder DayBackdrop,
        WpfBorder DayCard,
        WpfGrid DetailsOverlay,
        WpfBorder DetailsBackdrop,
        WpfBorder DetailsCard);

    private static CanvasSkeleton LoadCanvasSkeleton()
    {
        var doc = XDocument.Load(RepositoryPaths.Combine("DaouCalendarOverlay", "Views", "MainWindow.xaml"));
        var fragment = new XElement(Named(doc, "WindowLayout"));
        XElement In(string name) => fragment.Descendants().Single(e => (string?)e.Attribute(X + "Name") == name);

        In("ExpandedContent").RemoveNodes();
        In("MiniBarContent").RemoveNodes();
        In("TransitionLayer").RemoveNodes();
        foreach (var card in new[] { In("DayEventsCard"), In("EventDetailsCard") })
            card.Elements().Where(e => e.Name != Wpf + "Border.Effect").Remove();

        // 코드비하인드 처리기와 리소스가 없으므로 이벤트 속성과 태그 확장 값을 뺀다. 배치 속성은 원문 그대로다.
        foreach (var attribute in fragment.DescendantsAndSelf().Attributes()
                     .Where(a => a.Name.Namespace == XNamespace.None
                                 && (a.Value.StartsWith('{') || a.Name.LocalName.StartsWith("Mouse", StringComparison.Ordinal)))
                     .ToList())
        {
            attribute.Remove();
        }

        fragment.SetAttributeValue("xmlns", Wpf.NamespaceName);
        fragment.SetAttributeValue(XNamespace.Xmlns + "x", X.NamespaceName);

        var root = (WpfGrid)XamlReader.Parse(fragment.ToString());
        // Window의 UseLayoutRounding="True"는 상속 속성이다. 조각 루트에 같은 값을 둔다.
        root.UseLayoutRounding = true;
        T Find<T>(string name) => (T)root.FindName(name);
        return new CanvasSkeleton(
            root,
            Find<WpfBorder>("RootChrome"),
            Find<WpfGrid>("CardContentHost"),
            Find<WpfGrid>("ExpandedContent"),
            Find<WpfGrid>("MiniBarContent"),
            Find<WpfGrid>("DayEventsOverlay"),
            Find<WpfBorder>("DayEventsBackdrop"),
            Find<WpfBorder>("DayEventsCard"),
            Find<WpfGrid>("EventDetailsOverlay"),
            Find<WpfBorder>("EventDetailsBackdrop"),
            Find<WpfBorder>("EventDetailsCard"));
    }

    // ApplyCardPlacement의 XAML 대입과 같다(px / scale).
    private static void PlaceCard(CanvasSkeleton s, CardPlacement placement, double scale)
    {
        s.Card.Margin = new WpfThickness(0, placement.OffsetPx / scale, 0, 0);
        s.Card.Height = placement.HeightPx / scale;
    }

    // ResetSharedElementState의 내용 쪽 대입과 같다.
    private static void ResetContent(CanvasSkeleton s)
    {
        s.Expanded.Height = double.NaN;
        s.Expanded.VerticalAlignment = WpfVerticalAlignment.Stretch;
        s.Expanded.Margin = new WpfThickness(8);
        s.Mini.Height = double.NaN;
        s.Mini.VerticalAlignment = WpfVerticalAlignment.Stretch;
        s.Mini.Margin = new WpfThickness(6, 4, 6, 4);
    }

    // MiniBarContentHeightDip와 같은 식(쉬는 Margin 기준).
    private static double MiniContentHeight(CanvasSkeleton s) =>
        OverlayLayoutMetrics.MiniCardHeightDip
        - s.Card.BorderThickness.Top - s.Card.BorderThickness.Bottom
        - s.Mini.Margin.Top - s.Mini.Margin.Bottom;

    // 히트 테스트가 내용에 닿도록 시험에서만 내용 Grid에 배경을 준다(원문 내용에는 배경 있는 자식이 있다).
    private static void MakeContentHitTestable(CanvasSkeleton s)
    {
        s.Expanded.Background = WpfBrushes.Red;
        s.Mini.Background = WpfBrushes.Blue;
    }

    private static double Lerp(double from, double to, double e) => from + (to - from) * e;

    private static void LayoutCanvas(WpfFrameworkElement root, double height = OverlayLayoutMetrics.WindowHeightDip)
    {
        root.Measure(new WpfSize(OverlayLayoutMetrics.WindowWidthDip, height));
        root.Arrange(new WpfRect(0, 0, OverlayLayoutMetrics.WindowWidthDip, height));
        root.UpdateLayout();
    }

    private static WpfRect RectInRoot(WpfFrameworkElement element, WpfUIElement root)
    {
        var origin = element.TranslatePoint(new WpfPoint(0, 0), root);
        return new WpfRect(origin.X, origin.Y, element.ActualWidth, element.ActualHeight);
    }

    private static void AssertNear(double expected, double actual, string what) =>
        Assert.True(Math.Abs(expected - actual) <= LayoutEpsilon, $"{what}: 기대 {expected}, 실제 {actual}");

    // 중간 프레임의 카드 위 변·높이는 레이아웃 반올림으로 장치 px(여기서는 96 DPI라 1 DIP)에 맞춰진다. 차이는 반 px 이내다.
    // 첫·마지막 프레임 값은 정수 px라 정확히 같다. 내용 위치는 이 허용 오차 없이 AssertNear로 정확히 판정한다.
    private static void AssertCardFrame(double expectedTop, double expectedHeight, WpfRect card, string frame)
    {
        const double halfPx = 0.5 + LayoutEpsilon;
        Assert.True(Math.Abs(expectedTop - card.Top) <= halfPx, $"{frame} 카드 위 변: 기대 {expectedTop}, 실제 {card.Top}");
        Assert.True(Math.Abs(expectedHeight - card.Height) <= halfPx, $"{frame} 카드 높이: 기대 {expectedHeight}, 실제 {card.Height}");
        Assert.True(
            Math.Abs(expectedTop + expectedHeight - card.Bottom) <= halfPx,
            $"{frame} 카드 아래 변: 기대 {expectedTop + expectedHeight}, 실제 {card.Bottom}");
    }

    private static void AssertContains(WpfRect outer, WpfRect inner, string what) =>
        Assert.True(
            inner.Left >= outer.Left - LayoutEpsilon && inner.Top >= outer.Top - LayoutEpsilon
            && inner.Right <= outer.Right + LayoutEpsilon && inner.Bottom <= outer.Bottom + LayoutEpsilon,
            $"{what}: {inner}가 {outer} 밖으로 나간다");

    // 카드 위·아래 변 바로 바깥이면서 클립 전 내용 사각형 안인 점은 아무것도 맞지 않아야 한다(내용이 카드 밖에서 그려지거나 클릭을 받지 않음).
    // 카드 안이면서 내용 안인 점은 카드의 하위 요소(겹친 다른 내용 포함)에 맞아야 한다. 바깥 점 검사 횟수를 돌려준다.
    private static int AssertContentClippedToCard(CanvasSkeleton s, WpfGrid content, WpfRect card, string frame)
    {
        var unclipped = RectInRoot(content, s.Root);
        var x = card.Left + card.Width / 2;
        var outside = 0;
        foreach (var y in new[] { card.Top - 0.5, card.Bottom + 0.5 })
        {
            if (y < 0 || y >= s.Root.ActualHeight || y <= unclipped.Top || y >= unclipped.Bottom)
                continue;
            var hit = WpfVisualTreeHelper.HitTest(s.Root, new WpfPoint(x, y));
            Assert.True(hit is null, $"{frame}: 카드 밖 ({x},{y})에서 {hit?.VisualHit}가 맞았다. 카드={card}, 내용={unclipped}");
            outside++;
        }

        var insideTop = Math.Max(card.Top, unclipped.Top) + 1;
        var insideBottom = Math.Min(card.Bottom, unclipped.Bottom) - 1;
        if (insideBottom > insideTop)
        {
            var hit = WpfVisualTreeHelper.HitTest(s.Root, new WpfPoint(x, (insideTop + insideBottom) / 2));
            Assert.True(
                hit?.VisualHit is System.Windows.Media.Visual v && (ReferenceEquals(v, s.Card) || v.IsDescendantOf(s.Card)),
                $"{frame}: 카드 안 내용 점에서 카드 밖 요소 {hit?.VisualHit}가 맞았다");
        }

        return outside;
    }

    [Theory]
    [InlineData(390)] // 아래로 자란 카드(ExpandedDown)
    [InlineData(12)]  // 위로 자란 카드(ExpandedUp)
    [InlineData(364)] // 대체 규칙(아래 맞춤)으로 정한 중간 배치
    public void CanvasLayout_Collapse_ContentStaysInPlace_ClippedToCard_EndsAtSlot(int fromOffsetPx)
    {
        RunStaLayout(() =>
        {
            const double scale = 1.0;
            var m = CanvasMetrics.FromScale(scale);
            var from = new CardPlacement(fromOffsetPx, m.ExpandedHeightPx);
            var to = m.Slot;
            var s = LoadCanvasSkeleton();
            MakeContentHitTestable(s);
            var border = s.Card.BorderThickness.Top;

            // 쉬는 펼침 상태(ApplyExpandedState 끝과 같음).
            PlaceCard(s, from, scale);
            s.Expanded.Visibility = WpfVisibility.Visible;
            s.Mini.Visibility = WpfVisibility.Collapsed;
            LayoutCanvas(s.Root);
            var expandedRest = RectInRoot(s.Expanded, s.Root);
            AssertNear(from.OffsetPx + border + 8, expandedRest.Top, "쉬는 펼친 내용 위 변");

            // StartCollapseAnimation 준비 1~3과 같은 대입.
            var fromTop = from.OffsetPx / scale;
            var fromH = from.HeightPx / scale;
            var toTop = to.OffsetPx / scale;
            var toH = to.HeightPx / scale;
            var d = toTop - fromTop;
            Assert.True(d >= 0, $"접기 d = {d}");
            s.Expanded.Height = s.Expanded.ActualHeight;
            s.Expanded.VerticalAlignment = WpfVerticalAlignment.Top;
            var miniHeight = MiniContentHeight(s);
            s.Mini.Height = miniHeight;
            s.Mini.VerticalAlignment = WpfVerticalAlignment.Top;
            s.Mini.Margin = new WpfThickness(6, 4 + d, 6, 4);
            s.Mini.Visibility = WpfVisibility.Visible;

            var outsideChecks = 0;
            foreach (var e in Progress)
            {
                s.Card.Height = Lerp(fromH, toH, e);
                s.Card.Margin = new WpfThickness(0, Lerp(fromTop, toTop, e), 0, 0);
                s.Expanded.Margin = new WpfThickness(8, Lerp(8, 8 - d, e), 8, 8);
                s.Mini.Margin = new WpfThickness(6, Lerp(4 + d, 4, e), 6, 4);
                LayoutCanvas(s.Root);

                var frame = $"접기 from={fromOffsetPx} e={e}";
                var card = RectInRoot(s.Card, s.Root);
                AssertCardFrame(Lerp(fromTop, toTop, e), Lerp(fromH, toH, e), card, frame);
                var expanded = RectInRoot(s.Expanded, s.Root);
                AssertNear(expandedRest.Top, expanded.Top, $"{frame} 펼친 내용 위 변(제자리)");
                AssertNear(expandedRest.Height, expanded.Height, $"{frame} 펼친 내용 높이");
                AssertNear(toTop + border + 4, RectInRoot(s.Mini, s.Root).Top, $"{frame} 막대 내용 위 변(끝 위치)");
                outsideChecks += AssertContentClippedToCard(s, s.Expanded, card, frame);
            }

            if (d > 0)
                Assert.True(outsideChecks > 0, "카드 밖 클립 검사가 한 번도 돌지 않았다");

            // 끝 상태(ApplyCollapsedState: ResetSharedElementState → 가시성 → ApplyCardPlacement). 마지막 프레임과 같아야 한다.
            ResetContent(s);
            s.Expanded.Visibility = WpfVisibility.Collapsed;
            s.Mini.Visibility = WpfVisibility.Visible;
            PlaceCard(s, to, scale);
            LayoutCanvas(s.Root);
            var miniEnd = RectInRoot(s.Mini, s.Root);
            AssertNear(toTop + border + 4, miniEnd.Top, "끝 막대 내용 위 변");
            AssertNear(miniHeight, miniEnd.Height, "끝 막대 내용 높이(애니메이션 고정 높이와 같음)");
            var cardEnd = to.InWindow(new PixelRect(0, 0, 311, m.HeightPx));
            Assert.Equal(new WpfRect(cardEnd.Left, cardEnd.Top, cardEnd.Width, cardEnd.Height), RectInRoot(s.Card, s.Root));
        });
    }

    [Theory]
    [InlineData(390)] // 아래로 펼침(ExpandedDown)
    [InlineData(12)]  // 위로 펼침(ExpandedUp)
    [InlineData(364)] // 대체 규칙(아래 맞춤)으로 정한 중간 배치
    public void CanvasLayout_Expand_ContentStaysInPlace_ClippedToCard_EndsAtTarget(int targetOffsetPx)
    {
        RunStaLayout(() =>
        {
            const double scale = 1.0;
            var m = CanvasMetrics.FromScale(scale);
            var from = m.Slot;
            var target = new CardPlacement(targetOffsetPx, m.ExpandedHeightPx);
            var s = LoadCanvasSkeleton();
            MakeContentHitTestable(s);
            var border = s.Card.BorderThickness.Top;

            // 쉬는 막대 상태(ApplyCollapsedState 끝과 같음).
            PlaceCard(s, from, scale);
            s.Expanded.Visibility = WpfVisibility.Collapsed;
            s.Mini.Visibility = WpfVisibility.Visible;
            LayoutCanvas(s.Root);
            var miniRest = RectInRoot(s.Mini, s.Root);
            AssertNear(from.OffsetPx + border + 4, miniRest.Top, "쉬는 막대 내용 위 변");

            // StartExpandAnimation 단계 B 준비와 같은 대입.
            var miniH = from.HeightPx / scale;
            var sTop = from.OffsetPx / scale;
            var tTop = target.OffsetPx / scale;
            var d = sTop - tTop;
            Assert.True(d >= 0, $"펼치기 d = {d}");
            var cardHeight = target.HeightPx / scale;
            s.Mini.Height = MiniContentHeight(s);
            s.Mini.VerticalAlignment = WpfVerticalAlignment.Top;
            s.Mini.Margin = new WpfThickness(6, 4, 6, 4);
            var expandedHeight = cardHeight - s.Card.BorderThickness.Top - s.Card.BorderThickness.Bottom - 16;
            s.Expanded.Height = expandedHeight;
            s.Expanded.VerticalAlignment = WpfVerticalAlignment.Top;
            s.Expanded.Margin = new WpfThickness(8, 8 - d, 8, 8);
            s.Expanded.Visibility = WpfVisibility.Visible;

            var outsideChecks = 0;
            foreach (var e in Progress)
            {
                s.Card.Height = Lerp(miniH, cardHeight, e);
                s.Card.Margin = new WpfThickness(0, Lerp(sTop, tTop, e), 0, 0);
                s.Expanded.Margin = new WpfThickness(8, Lerp(8 - d, 8, e), 8, 8);
                s.Mini.Margin = new WpfThickness(6, Lerp(4, 4 + d, e), 6, 4);
                LayoutCanvas(s.Root);

                var frame = $"펼치기 target={targetOffsetPx} e={e}";
                var card = RectInRoot(s.Card, s.Root);
                AssertCardFrame(Lerp(sTop, tTop, e), Lerp(miniH, cardHeight, e), card, frame);
                AssertNear(tTop + border + 8, RectInRoot(s.Expanded, s.Root).Top, $"{frame} 펼친 내용 위 변(끝 위치)");
                AssertNear(miniRest.Top, RectInRoot(s.Mini, s.Root).Top, $"{frame} 막대 내용 위 변(제자리)");
                outsideChecks += AssertContentClippedToCard(s, s.Expanded, card, frame);
            }

            Assert.True(outsideChecks > 0, "카드 밖 클립 검사가 한 번도 돌지 않았다");

            // 끝 상태(ApplyExpandedState). 마지막 프레임과 같아야 한다.
            ResetContent(s);
            s.Mini.Visibility = WpfVisibility.Collapsed;
            s.Expanded.Visibility = WpfVisibility.Visible;
            PlaceCard(s, target, scale);
            LayoutCanvas(s.Root);
            var expandedEnd = RectInRoot(s.Expanded, s.Root);
            AssertNear(tTop + border + 8, expandedEnd.Top, "끝 펼친 내용 위 변");
            AssertNear(expandedHeight, expandedEnd.Height, "끝 펼친 내용 높이(애니메이션 고정 높이와 같음)");
            var cardEnd = target.InWindow(new PixelRect(0, 0, 311, m.HeightPx));
            Assert.Equal(new WpfRect(cardEnd.Left, cardEnd.Top, cardEnd.Width, cardEnd.Height), RectInRoot(s.Card, s.Root));
        });
    }

    [Theory]
    [InlineData(390)]
    [InlineData(12)]
    [InlineData(364)]
    public void CanvasLayout_ExpandedOverlays_CoverCard_KeepCardScreenPosition_ClipShadow(int offsetPx)
    {
        RunStaLayout(() =>
        {
            const double scale = 1.0;
            var m = CanvasMetrics.FromScale(scale);
            var s = LoadCanvasSkeleton();
            PlaceCard(s, new CardPlacement(offsetPx, m.ExpandedHeightPx), scale);
            // RestoreOverlayPlacement와 같은 대입.
            foreach (var overlay in new[] { s.DayOverlay, s.DetailsOverlay })
            {
                overlay.Margin = s.Card.Margin;
                overlay.Height = s.Card.Height;
                overlay.Visibility = WpfVisibility.Visible;
            }

            s.DayCard.Height = 200;
            s.DetailsCard.Height = 1000; // MaxHeight 380으로 잘린다.
            LayoutCanvas(s.Root);

            var card = RectInRoot(s.Card, s.Root);
            Assert.Equal(card, RectInRoot(s.DayOverlay, s.Root));
            Assert.Equal(card, RectInRoot(s.DetailsOverlay, s.Root));
            var inset = new WpfRect(card.X + 9, card.Y + 9, card.Width - 18, card.Height - 18);
            Assert.Equal(inset, RectInRoot(s.DayBackdrop, s.Root));
            Assert.Equal(inset, RectInRoot(s.DetailsBackdrop, s.Root));

            // 7.4.0b와 같은 화면 위치: 가로 13 DIP, 세로 카드 위 변 + 213 − h/2.
            Assert.Equal(new WpfRect(13, offsetPx + 213 - 100, 285, 200), RectInRoot(s.DayCard, s.Root));
            Assert.Equal(new WpfRect(13, offsetPx + 213 - 190, 285, 380), RectInRoot(s.DetailsCard, s.Root));

            // 그림자는 이름 붙은 원래 효과이고, 오버레이의 레이아웃 클립(= 카드 사각형) 밖에는 그려지지 않는다.
            Assert.Same(s.Root.FindName("DayEventsCardShadow"), s.DayCard.Effect);
            Assert.Same(s.Root.FindName("EventDetailsCardShadow"), s.DetailsCard.Effect);
            Assert.IsType<WpfDropShadowEffect>(s.DayCard.Effect);
            foreach (var overlay in new[] { s.DayOverlay, s.DetailsOverlay })
            {
                var clip = WpfVisualTreeHelper.GetClip(overlay);
                Assert.NotNull(clip);
                Assert.Equal(new WpfRect(0, 0, card.Width, card.Height), clip.Bounds);
            }
        });
    }

    [Theory]
    [InlineData(AttachDirection.Below, 1.0)]
    [InlineData(AttachDirection.Above, 1.0)]
    [InlineData(AttachDirection.Below, 1.25)]
    [InlineData(AttachDirection.Above, 1.25)]
    [InlineData(AttachDirection.Below, 1.5)]
    [InlineData(AttachDirection.Above, 1.5)]
    public void CanvasLayout_AttachedOverlays_InRegion_GapFromBar_InsideCanvas(AttachDirection dir, double scale)
    {
        RunStaLayout(() =>
        {
            var m = CanvasMetrics.FromScale(scale);
            var s = LoadCanvasSkeleton();
            // 96 DPI 반올림을 끄고 배율 환산 DIP 값을 그대로 잰다.
            s.Root.UseLayoutRounding = false;
            PlaceCard(s, m.Slot, scale);
            s.Mini.Visibility = WpfVisibility.Visible;
            s.Expanded.Visibility = WpfVisibility.Collapsed;

            // OpenAttachedDayList와 같은 대입.
            var region = OverlayLayoutPlanner.AttachRegion(dir, m);
            var regionTop = region.OffsetPx / scale;
            var regionHeight = region.HeightPx / scale - OverlayLayoutMetrics.AttachGapDip;
            var margin = dir == AttachDirection.Below
                ? new WpfThickness(0, regionTop + OverlayLayoutMetrics.AttachGapDip, 0, 0)
                : new WpfThickness(0, regionTop, 0, 0);
            var cardAlignment = dir == AttachDirection.Below ? WpfVerticalAlignment.Top : WpfVerticalAlignment.Bottom;
            foreach (var (overlay, backdrop, listCard) in new[]
                     {
                         (s.DayOverlay, s.DayBackdrop, s.DayCard),
                         (s.DetailsOverlay, s.DetailsBackdrop, s.DetailsCard),
                     })
            {
                overlay.Margin = margin;
                overlay.Height = regionHeight;
                overlay.Visibility = WpfVisibility.Visible;
                backdrop.Background = WpfBrushes.Transparent;
                listCard.VerticalAlignment = cardAlignment;
                listCard.Effect = null;
                listCard.Height = 1000; // MaxHeight(370, 380)로 잘린 최악 높이
            }

            var canvasHeight = m.HeightPx / scale;
            LayoutCanvas(s.Root, canvasHeight);

            var bar = RectInRoot(s.Card, s.Root);
            var regionRect = new WpfRect(0, regionTop, OverlayLayoutMetrics.WindowWidthDip, region.HeightPx / scale);
            var canvas = new WpfRect(0, 0, OverlayLayoutMetrics.WindowWidthDip, canvasHeight);
            AssertContains(canvas, regionRect, "붙임 영역");
            foreach (var (overlay, listCard) in new[] { (s.DayOverlay, s.DayCard), (s.DetailsOverlay, s.DetailsCard) })
            {
                var o = RectInRoot(overlay, s.Root);
                var c = RectInRoot(listCard, s.Root);
                AssertContains(regionRect, o, $"{overlay.Name}");
                AssertContains(o, c, $"{listCard.Name}");
                AssertNear(13, c.Left, $"{listCard.Name} 왼쪽");
                var gap = dir == AttachDirection.Below ? c.Top - bar.Bottom : bar.Top - c.Bottom;
                AssertNear(OverlayLayoutMetrics.AttachGapDip, gap, $"{listCard.Name}와 막대 사이 간격");
                Assert.Null(overlay.Background);
                Assert.Null(listCard.Effect);
            }
        });
    }

    private static void RunStaLayout(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
