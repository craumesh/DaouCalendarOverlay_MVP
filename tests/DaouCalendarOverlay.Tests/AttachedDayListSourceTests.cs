using System.Xml.Linq;

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

    [Fact]
    public void MainWindowXaml_DayOverlays_StartInExpandedPlacement()
    {
        var doc = XDocument.Load(RepositoryPaths.Combine("DaouCalendarOverlay", "Views", "MainWindow.xaml"));

        foreach (var name in new[] { "DayEventsOverlay", "EventDetailsOverlay" })
        {
            var overlay = Named(doc, name);
            Assert.Equal("1", (string?)overlay.Attribute("Grid.Row"));
            Assert.Equal("16", (string?)overlay.Attribute("Margin"));
        }

        Assert.Equal("#9A080A0E", (string?)Named(doc, "DayEventsBackdrop").Attribute("Background"));
        Assert.Equal("#9A080A0E", (string?)Named(doc, "EventDetailsBackdrop").Attribute("Background"));
        Assert.Equal("Center", (string?)Named(doc, "DayEventsCard").Attribute("VerticalAlignment"));
        Assert.Equal("Center", (string?)Named(doc, "EventDetailsCard").Attribute("VerticalAlignment"));
        Assert.Equal("0", (string?)Named(doc, "AttachAboveRow").Attribute("Height"));
        Assert.Equal("0", (string?)Named(doc, "AttachBelowRow").Attribute("Height"));
    }

    /// <summary>떼기 뒤 날짜 셀 경로가 붙인 모드 배치를 물려받지 않도록, 복원 값이 XAML 기본값과 같아야 한다.</summary>
    [Fact]
    public void RestoreOverlayPlacement_MatchesXamlDefaults()
    {
        var doc = XDocument.Load(RepositoryPaths.Combine("DaouCalendarOverlay", "Views", "MainWindow.xaml"));
        var source = CodeBehind();
        var restore = MethodBody(source, "private void RestoreOverlayPlacement()");

        foreach (var name in new[] { "DayEventsOverlay", "EventDetailsOverlay" })
        {
            var overlay = Named(doc, name);
            Assert.Contains($"Grid.SetRow({name}, {(string?)overlay.Attribute("Grid.Row")});", restore, StringComparison.Ordinal);
            Assert.Contains($"{name}.Margin = new Thickness({(string?)overlay.Attribute("Margin")});", restore, StringComparison.Ordinal);
        }

        foreach (var name in new[] { "DayEventsBackdrop", "EventDetailsBackdrop" })
            Assert.Contains($"{name}.Background = CreateBackdropBrush();", restore, StringComparison.Ordinal);

        foreach (var name in new[] { "DayEventsCard", "EventDetailsCard" })
        {
            var alignment = (string?)Named(doc, name).Attribute("VerticalAlignment");
            Assert.Contains($"{name}.VerticalAlignment = VerticalAlignment.{alignment};", restore, StringComparison.Ordinal);
        }

        var brush = MethodBody(source, "private static SolidColorBrush CreateBackdropBrush()");
        var hex = (string)Named(doc, "DayEventsBackdrop").Attribute("Background")!;
        Assert.Contains($"Color.FromArgb({ToFromArgbArguments(hex)})", brush, StringComparison.Ordinal);
        Assert.Contains("Freeze()", brush, StringComparison.Ordinal);
    }

    [Fact]
    public void CloseAttached_RestoresPreAttachRectAndExpandedPlacement()
    {
        var body = MethodBody(CodeBehind(), "private void CloseAttached()");

        Assert.Contains("ApplyWindowRectPx(pre)", body, StringComparison.Ordinal);
        Assert.Contains("RestoreOverlayPlacement();", body, StringComparison.Ordinal);
        Assert.Contains("AttachBelowRow.Height = new GridLength(0);", body, StringComparison.Ordinal);
        Assert.Contains("AttachAboveRow.Height = new GridLength(0);", body, StringComparison.Ordinal);
        Assert.Contains("_attached = null;", body, StringComparison.Ordinal);
        Assert.Contains("_preAttachWindowPx = null;", body, StringComparison.Ordinal);
        Assert.Contains("_suppressBoundsSave = false;", body, StringComparison.Ordinal);
        Assert.Contains("_returnToDayListAfterDetails = false;", body, StringComparison.Ordinal);
        // 사전 사각형 복원과 대체 계산은 서로 배타인 두 분기다. 그 밖의 창 변경은 없다.
        Assert.Equal(2, CountOf(body, "ApplyWindowRectPx("));
        Assert.True(
            body.IndexOf("if (_preAttachWindowPx is PixelRect pre)", StringComparison.Ordinal) <
            body.IndexOf("else if (GetWindowRectPx() is PixelRect win)", StringComparison.Ordinal),
            "직전 사각형이 있으면 그것으로 복원하고, 없을 때만 막대 높이로 다시 계산해야 한다");
        Assert.Contains("OverlayLayoutMetrics.DipToPx(OverlayLayoutMetrics.MiniWindowHeightDip, scale)", body, StringComparison.Ordinal);
        Assert.Contains("OverlayLayoutPlanner.ToAnchor(dir)", body, StringComparison.Ordinal);
        Assert.Contains("OverlayLayoutMetrics.DipToPx(OverlayLayoutMetrics.WindowMarginDip, scale)", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// 떼기의 마무리(억제 해제, 저장 예약, 클램프, 순환 재판정)는 조건 없이 이 순서로 돈다.
    /// 전환 중 분기를 두지 않는 대신, 전환 중에는 CloseAttached가 불리지 않도록 호출부를 고정한다(아래 시험).
    /// </summary>
    [Fact]
    public void CloseAttached_FinishesUnconditionally_InSpecOrder()
    {
        var body = MethodBody(CodeBehind(), "private void CloseAttached()");

        Assert.DoesNotContain("_transitionInProgress", body, StringComparison.Ordinal);
        var order = new[]
        {
            "RestoreOverlayPlacement();",
            "_attached = null;",
            "_preAttachWindowPx = null;",
            "_suppressBoundsSave = false;",
            "ScheduleBoundsSave();",
            "ClampToVisibleWorkArea();",
            "UpdateTickerState();",
        };
        var positions = order.Select(token => body.IndexOf(token, StringComparison.Ordinal)).ToArray();
        Assert.All(positions, p => Assert.True(p >= 0));
        for (var i = 1; i < positions.Length; i++)
            Assert.True(positions[i - 1] < positions[i], $"{order[i - 1]} 다음에 {order[i]}가 와야 한다");
    }

    /// <summary>
    /// 전환 중 CloseAttached가 억제를 풀면 최소화 상태와 펼친 창의 위치가 따로 저장될 수 있다(불변 조건 4).
    /// 그래서 CloseAttached를 부르는 곳을 4곳으로 고정하고, 어느 곳도 전환 중에는 닿지 않게 한다.
    /// </summary>
    [Fact]
    public void CloseAttached_IsNeverReachedDuringTransition()
    {
        var source = CodeBehind();

        // 호출부: HideOverlay, DetachIfAllOverlaysClosed, OnMiniBarLeaving, MiniAgenda_MouseLeftButtonUp.
        Assert.Equal(4, CountOf(source, "CloseAttached();"));

        // 펼치기는 전환 플래그를 세우기 전에 떼고, OnMiniBarLeaving은 이곳에서만 부른다.
        Assert.Equal(1, CountOf(source, "OnMiniBarLeaving();"));
        var expand = MethodBody(source, "private Task BeginExpandAsync()");
        var leave = expand.IndexOf("OnMiniBarLeaving();", StringComparison.Ordinal);
        var flag = expand.IndexOf("_transitionInProgress = true;", StringComparison.Ordinal);
        var stop = expand.IndexOf("_saveBoundsTimer?.Stop();", StringComparison.Ordinal);
        Assert.True(leave >= 0 && flag > leave && stop > flag, "떼기 → 전환 플래그 → 떼기가 예약한 저장 취소 순서여야 한다");

        // 숨기기는 전환을 먼저 끝낸 뒤 뗀다.
        var hide = MethodBody(source, "public void HideOverlay()");
        Assert.True(
            hide.IndexOf("FinishTransitionNow();", StringComparison.Ordinal) <
            hide.IndexOf("CloseAttached();", StringComparison.Ordinal));

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

    [Fact]
    public void OpenAttachedDayList_SuppressesBoundsSave_BeforeSingleWindowChange()
    {
        var open = MethodBody(CodeBehind(), "private void OpenAttachedDayList()");

        Assert.Equal(1, CountOf(open, "ApplyWindowRectPx("));
        var suppress = open.IndexOf("_suppressBoundsSave = true;", StringComparison.Ordinal);
        var capture = open.IndexOf("_preAttachWindowPx = win;", StringComparison.Ordinal);
        var apply = open.IndexOf("ApplyWindowRectPx(", StringComparison.Ordinal);
        Assert.True(suppress >= 0 && capture >= 0, "붙이기 전에 위치 저장 억제와 직전 사각형 기록이 있어야 한다");
        Assert.True(suppress < apply && capture < apply, "창을 바꾸기 전에 억제와 기록을 끝내야 한다");
        Assert.Contains("_saveBoundsTimer?.Stop();", open, StringComparison.Ordinal);
        Assert.Contains("_transitionInProgress", open, StringComparison.Ordinal);

        // 높이는 합친 DIP를 한 번에 환산한다. 나눠 반올림하면 떼기 왕복이 px 단위로 어긋날 수 있다.
        Assert.Contains(
            "OverlayLayoutMetrics.MiniWindowHeightDip + OverlayLayoutMetrics.AttachedRegionHeightDip, scale)",
            open,
            StringComparison.Ordinal);
        Assert.Contains("OverlayLayoutPlanner.ToAnchor(dir)", open, StringComparison.Ordinal);
        Assert.Contains("Grid.SetRow(DayEventsOverlay, row);", open, StringComparison.Ordinal);
        Assert.Contains("Grid.SetRow(EventDetailsOverlay, row);", open, StringComparison.Ordinal);
        Assert.Contains("DayEventsBackdrop.Background = Brushes.Transparent;", open, StringComparison.Ordinal);
        Assert.Contains("EventDetailsBackdrop.Background = Brushes.Transparent;", open, StringComparison.Ordinal);

        // 목록 내용은 창을 바꾼 뒤 채운다(붙인 영역 높이는 내용과 무관하게 384 고정).
        var show = open.IndexOf("ShowDayEvents(", StringComparison.Ordinal);
        Assert.True(show > apply);
        Assert.Contains("new GridLength(OverlayLayoutMetrics.AttachedRegionHeightDip)", open, StringComparison.Ordinal);
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
}
