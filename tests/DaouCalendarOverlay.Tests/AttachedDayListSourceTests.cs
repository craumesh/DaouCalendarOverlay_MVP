using System.Xml.Linq;
using DaouCalendarOverlay.Services;

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
            Assert.Equal("9", (string?)overlay.Attribute("Margin"));
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

        // 목록 내용은 창을 바꾼 뒤 채운다(붙인 영역 높이는 내용과 무관하게 390 고정).
        var show = open.IndexOf("ShowDayEvents(", StringComparison.Ordinal);
        Assert.True(show > apply);
        Assert.Contains("new GridLength(OverlayLayoutMetrics.AttachedRegionHeightDip)", open, StringComparison.Ordinal);

        // 막대 카드와 붙인 목록 카드 사이 간격은 막대 쪽 변에만 둔다.
        Assert.Contains("new Thickness(0, OverlayLayoutMetrics.AttachGapDip, 0, 0)", open, StringComparison.Ordinal);
        Assert.Contains("new Thickness(0, 0, 0, OverlayLayoutMetrics.AttachGapDip)", open, StringComparison.Ordinal);
    }

    /// <summary>창 사각형이 곧 카드 사각형이다. 투명 여백 상수가 코드에 남아 있으면 조용히 엉뚱한 값을 낸다.</summary>
    [Fact]
    public void CodeBehind_HasNoCardInset()
    {
        // 토큰을 나눠 써서 이 파일 자신이 검색에 걸리지 않게 한다.
        Assert.Equal(0, CountOf(CodeBehind(), "Window" + "MarginDip"));
    }

    [Fact]
    public void MainWindowXaml_WindowIsCardSized()
    {
        var xaml = File.ReadAllText(RepositoryPaths.Combine("DaouCalendarOverlay", "Views", "MainWindow.xaml"));

        Assert.Contains("Width=\"311\" Height=\"426\"", xaml, StringComparison.Ordinal);

        var chrome = OpeningTag(xaml, "x:Name=\"RootChrome\"");
        Assert.DoesNotContain("Margin=", chrome, StringComparison.Ordinal);
        Assert.Contains("Padding=\"0\"", chrome, StringComparison.Ordinal);

        Assert.Contains("Margin=\"8\"", OpeningTag(xaml, "x:Name=\"ExpandedContent\""), StringComparison.Ordinal);
        Assert.Contains("Margin=\"6,4\"", OpeningTag(xaml, "x:Name=\"MiniBarContent\""), StringComparison.Ordinal);
    }

    /// <summary>
    /// 이동 루프는 OS가 누적한 lParam 위치가 아니라 드래그 시작 사각형과 커서 이동량으로 매번 다시 계산해 되쓴다.
    /// 스냅이 없는 프레임에서도 커서 기준 위치를 써야 하므로 되쓰기 비교는 lp 기준이다.
    /// 세션은 이동 루프가 끝나면 finally에서 반드시 비우고, 드래그 하나에 로그는 1줄이다.
    /// </summary>
    [Fact]
    public void MoveLoop_UsesCursorBasedSnap_AndLogsOncePerDrag()
    {
        var source = CodeBehind();
        const string wndProcSignature =
            "private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)";
        var wndProc = MethodBody(source, wndProcSignature);
        var enter = MethodBody(source, "private void OnEnterSizeMove()");
        var moving = MethodBody(source, "private bool TrySnapMoving(IntPtr lParam)");
        var exit = MethodBody(source, "private void OnExitSizeMove()");
        var cursor = MethodBody(source, "private static PixelPoint? TryGetCursorPx()");
        var loop = wndProcSignature + wndProc + enter + moving + exit + cursor;

        foreach (var token in new[]
                 {
                     "GetCursorPos", "OverlayDragSnap.Begin", "OverlayDragSnap.Propose",
                     "WmEnterSizeMove", "WmExitSizeMove", "WmDpiChanged",
                     "_dragSession = null", "snapped != lp", "finally"
                 })
            Assert.Contains(token, loop, StringComparison.Ordinal);

        // 비교 대상이 proposed면 스냅이 없는 프레임에서 OS 누적 위치가 그대로 남는다.
        Assert.DoesNotContain("snapped != proposed", loop, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(loop, "LogService.Info(\"ui.snap\""));
        Assert.True(CountOf(loop, "ReportHandlerFailure(\"ui.snap\"") >= 1);

        // WndProc가 메시지마다 해당 메서드를 부른다.
        Assert.Contains("OnEnterSizeMove();", wndProc, StringComparison.Ordinal);
        Assert.Contains("TrySnapMoving(lParam)", wndProc, StringComparison.Ordinal);
        Assert.Contains("OnExitSizeMove();", wndProc, StringComparison.Ordinal);

        // 커서는 시작과 매 WM_MOVING에서 같은 헬퍼로 읽는다.
        Assert.Contains("TryGetCursorPx()", enter, StringComparison.Ordinal);
        Assert.Contains("TryGetCursorPx()", moving, StringComparison.Ordinal);
        Assert.Contains("OverlayDragSnap.Begin", enter, StringComparison.Ordinal);
        Assert.Contains("OverlayDragSnap.Propose", moving, StringComparison.Ordinal);

        // 로그는 끝 메서드에만 있고, 세션 정리는 finally 안이다.
        Assert.Equal(1, CountOf(exit, "LogService.Info(\"ui.snap\""));
        var finallyAt = exit.IndexOf("finally", StringComparison.Ordinal);
        Assert.True(finallyAt >= 0, "OnExitSizeMove에 finally가 있어야 한다");
        Assert.True(
            exit.IndexOf("_dragSession = null", finallyAt, StringComparison.Ordinal) > finallyAt,
            "OnExitSizeMove의 _dragSession = null은 finally 뒤에 있어야 한다");

        Assert.Equal(1, CountOf(source, "private static extern bool GetCursorPos("));

        // 스냅 진입 조건은 기존과 같다.
        Assert.Contains(
            "msg == WmMoving && _snapEnabled && !_positionLocked && !_transitionInProgress &&\n                 _attached is null && lParam != IntPtr.Zero",
            wndProc.Replace("\r\n", "\n"),
            StringComparison.Ordinal);
        // WM_DPICHANGED는 기존 클램프를 유지하고, 세션이 있을 때만 기준선을 다시 잡는다.
        Assert.Contains("Dispatcher.BeginInvoke(new Action(ClampToVisibleWorkArea))", wndProc, StringComparison.Ordinal);
        Assert.Contains("OverlayDragSnap.Begin(suggested, cursor)", wndProc, StringComparison.Ordinal);

        // 경로 독립: TrySnapMoving은 lParam에서 크기만 읽고 위치(Left/Top/Right/Bottom)는 Propose에 넘기지 않는다.
        Assert.Contains("OverlayDragSnap.Propose(session, cursor, lp.Width, lp.Height)", moving, StringComparison.Ordinal);
        foreach (var position in new[] { "lp.Left", "lp.Top", "lp.Right", "lp.Bottom" })
            Assert.DoesNotContain(position, moving, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(moving, "if (snapped != lp)"));

        // 로그 형식은 spec 문자열과 같다. 스냅 켜짐은 종료 시점의 조건으로 평가한다.
        Assert.Contains(
            "LogService.Info(\"ui.snap\", $\"드래그 끝: 창=({r.Left},{r.Top},{r.Right},{r.Bottom}) 작업영역=({w.Left},{w.Top},{w.Right},{w.Bottom}) 붙은 변={OverlaySnap.DescribeEdges(edges)} 스냅={(snapOn ? \"켜짐\" : \"꺼짐\")}\");",
            exit,
            StringComparison.Ordinal);
        Assert.Contains("var snapOn = _snapEnabled && !_positionLocked && _attached is null;", exit, StringComparison.Ordinal);
        Assert.Contains("OverlaySnap.DetectEdges(r, w, 0)", exit, StringComparison.Ordinal);

        // 시작 단계의 예외도 세션을 비운다.
        var enterCatch = enter.IndexOf("catch", StringComparison.Ordinal);
        Assert.True(enterCatch >= 0, "OnEnterSizeMove에 catch가 있어야 한다");
        Assert.True(
            enter.IndexOf("_dragSession = null", enterCatch, StringComparison.Ordinal) > enterCatch,
            "OnEnterSizeMove의 catch에서 _dragSession = null이어야 한다");

        // 불변 조건 4: 예외 경로 3곳(WM_ENTERSIZEMOVE·WM_MOVING·WM_DPICHANGED) 모두에서 세션을 비운다.
        var movingCatch = moving.LastIndexOf("catch (Exception ex)", StringComparison.Ordinal);
        Assert.True(movingCatch >= 0, "TrySnapMoving에 catch (Exception ex)가 있어야 한다");
        var movingCatchTail = moving.Substring(movingCatch);
        var movingClear = movingCatchTail.IndexOf("_dragSession = null;", StringComparison.Ordinal);
        var movingReport = movingCatchTail.IndexOf("ReportHandlerFailure(\"ui.snap\", ex)", StringComparison.Ordinal);
        Assert.True(movingClear >= 0, "TrySnapMoving의 catch에서 _dragSession = null;이어야 한다");
        Assert.True(movingReport >= 0, "TrySnapMoving의 catch에서 ReportHandlerFailure(\"ui.snap\", ex)를 불러야 한다");
        Assert.True(
            movingClear < movingReport,
            "TrySnapMoving의 catch에서 _dragSession = null;은 ReportHandlerFailure보다 앞이어야 한다");

        // Propose는 폭·높이 0 이하에서 예외를 던지므로 크기 가드가 정확히 한 번 있어야 한다.
        Assert.True(
            CountOf(moving, "lp.Width > 0 && lp.Height > 0") == 1,
            "TrySnapMoving에 lp.Width > 0 && lp.Height > 0 가드가 정확히 1회 있어야 한다");

        var enterCatchEx = enter.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        Assert.True(enterCatchEx >= 0, "OnEnterSizeMove에 catch (Exception ex)가 있어야 한다");
        Assert.True(
            enter.IndexOf("_dragSession = null;", enterCatchEx, StringComparison.Ordinal) > enterCatchEx,
            "OnEnterSizeMove의 catch 뒤에 _dragSession = null;이 있어야 한다");

        var dpiBegin = wndProc.IndexOf("OverlayDragSnap.Begin(suggested, cursor)", StringComparison.Ordinal);
        Assert.True(dpiBegin >= 0, "WndProc의 WM_DPICHANGED 분기에 OverlayDragSnap.Begin(suggested, cursor)가 있어야 한다");
        var dpiCatch = wndProc.IndexOf("catch (Exception ex)", dpiBegin, StringComparison.Ordinal);
        Assert.True(dpiCatch > dpiBegin, "WM_DPICHANGED 기준선 재설정 뒤에 catch (Exception ex)가 있어야 한다");
        Assert.True(
            wndProc.IndexOf("_dragSession = null;", dpiCatch, StringComparison.Ordinal) > dpiCatch,
            "WM_DPICHANGED의 catch 뒤에 _dragSession = null;이 있어야 한다");

        // 읽지 않는 로그용 플래그 필드는 두지 않는다.
        Assert.DoesNotContain("_dragSnapUsed", source, StringComparison.Ordinal);
        Assert.Equal(1, CountOf(source, "private DragSnapSession? _dragSession;"));
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
    /// 직전 전환 메모(왕복 복원): 세 곳의 계획 계산은 PlanTransitionFor 하나만 쓰고, 메모는 창 적용 뒤 실측값으로 기록하며
    /// 중단하면 지운다. 메모는 메모리에만 두므로 설정으로 저장하는 코드가 없어야 한다.
    /// </summary>
    [Fact]
    public void Transitions_UsePlanTransitionFor_AndRecordMemo()
    {
        var source = CodeBehind();
        var collapse = MethodBody(source, "private Task BeginCollapseAsync()");
        var expand = MethodBody(source, "private Task BeginExpandAsync()");
        var expandAnimation = MethodBody(source, "private void StartExpandAnimation(int generation)");

        foreach (var body in new[] { collapse, expand, expandAnimation })
        {
            Assert.Contains("PlanTransitionFor(", body, StringComparison.Ordinal);
            Assert.DoesNotContain("OverlayLayoutPlanner.ChooseResizeAnchor(", body, StringComparison.Ordinal);
        }

        Assert.Contains("PlanTransitionFor(win, OverlayDisplayMode.MiniBar)", collapse, StringComparison.Ordinal);
        Assert.Contains("PlanTransitionFor(win, OverlayDisplayMode.Expanded)", expand, StringComparison.Ordinal);
        Assert.Contains("PlanTransitionFor(win, OverlayDisplayMode.Expanded)", expandAnimation, StringComparison.Ordinal);
        Assert.Contains("_pendingWindowBefore = win;", collapse, StringComparison.Ordinal);
        Assert.Contains("_pendingWindowBefore = win;", expand, StringComparison.Ordinal);

        var helper = MethodBody(
            source,
            "private OverlayLayoutPlanner.TransitionPlan PlanTransitionFor(PixelRect win, OverlayDisplayMode targetMode)");
        Assert.Contains("OverlayLayoutPlanner.PlanTransition(", helper, StringComparison.Ordinal);
        Assert.Contains("_lastTransition", helper, StringComparison.Ordinal);

        foreach (var signature in new[]
                 {
                     "private void ApplyCollapsedState(PixelRect? target)",
                     "private void ApplyExpandedState(PixelRect? target)",
                 })
        {
            var body = MethodBody(source, signature);
            Assert.Contains("_lastTransition = new", body, StringComparison.Ordinal);
            Assert.Contains("_pendingWindowBefore = null;", body, StringComparison.Ordinal);
            Assert.Contains("GetWindowRectPx() is PixelRect a", body, StringComparison.Ordinal);
            // 창을 적용한 다음에 실측해서 기록한다.
            Assert.True(
                body.IndexOf("ApplyWindowRectPx(", StringComparison.Ordinal) <
                body.IndexOf("_lastTransition = new", StringComparison.Ordinal),
                "메모는 창 적용 뒤에 기록해야 한다");
        }

        var abort = MethodBody(source, "private void AbortTransition()");
        Assert.Contains("_lastTransition = null;", abort, StringComparison.Ordinal);
        Assert.Contains("_pendingWindowBefore = null;", abort, StringComparison.Ordinal);

        // 메모는 메모리에만 둔다. 설정 모델에 나오지 않는다.
        Assert.Equal(1, CountOf(source, "OverlayLayoutPlanner.TransitionMemo? _lastTransition"));
        Assert.DoesNotContain(
            "_lastTransition",
            File.ReadAllText(RepositoryPaths.Combine("DaouCalendarOverlay", "Models", "AppSettings.cs")),
            StringComparison.Ordinal);
    }

    private static int IndexOrFail(string text, string token, int from = 0)
    {
        var index = text.IndexOf(token, from, StringComparison.Ordinal);
        Assert.True(index >= 0, $"찾지 못함: {token}");
        return index;
    }

    /// <summary>
    /// 접기·펼치기 공유 요소 전환: 날짜·시계 블록은 유령 요소가 사각형을 바꾸며 옮기고 나머지 내용은 페이드한다.
    /// 전환당 창 적용은 1회(접기는 ApplyCollapsedState, 펼치기는 단계 B), 완료 처리기는 모두 generation을 거치고
    /// 펼치기 단계 A 페이드아웃과 높이 애니메이션에만 있다. 모든 종료 경로는 애니메이션 해제 뒤 공유 요소 상태를 되돌린다.
    /// </summary>
    [Fact]
    public void SharedElementTransition_Invariants()
    {
        var source = CodeBehind();
        var collapse = MethodBody(source, "private void StartCollapseAnimation(int generation)");
        var expand = MethodBody(source, "private void StartExpandAnimation(int generation)");

        // 창 적용 횟수: 접기 애니메이션 안 0회, 펼치기 애니메이션 안 1회(단계 B).
        Assert.Equal(0, CountOf(collapse, "ApplyWindowRectPx("));
        Assert.Equal(1, CountOf(expand, "ApplyWindowRectPx("));

        // 완료 처리기는 모두 generation 검사를 거친다. 접기 1개(높이), 펼치기 2개(단계 A 페이드아웃, 높이).
        Assert.Equal(1, CountOf(collapse, "Completed +="));
        Assert.Equal(CountOf(collapse, "Completed +="), CountOf(collapse, "RunTransitionStep(generation"));
        Assert.Equal(2, CountOf(expand, "Completed +="));
        Assert.Equal(CountOf(expand, "Completed +="), CountOf(expand, "RunTransitionStep(generation"));
        Assert.Contains("RunTransitionStep(generation, () => ApplyCollapsedState(target))", collapse, StringComparison.Ordinal);
        Assert.Contains("RunTransitionStep(generation, () => ApplyExpandedState(null))", expand, StringComparison.Ordinal);
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

        // 해제 대상: 기존 대상 + 유령 4개 속성 + 두 층과 실제 블록 두 개의 Opacity.
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

        // 끝 상태와 중단은 애니메이션을 먼저 해제하고 그다음 공유 요소 상태를 되돌린다.
        foreach (var signature in new[]
                 {
                     "private void ApplyCollapsedState(PixelRect? target)",
                     "private void ApplyExpandedState(PixelRect? target)",
                     "private void AbortTransition()",
                 })
        {
            var body = MethodBody(source, signature);
            var clearAt = IndexOrFail(body, "ClearTransitionAnimations();");
            var resetAt = IndexOrFail(body, "ResetSharedElementState();");
            Assert.True(clearAt < resetAt, $"{signature}: ClearTransitionAnimations가 ResetSharedElementState보다 앞이어야 한다");
        }

        // 끝 상태: 창 적용은 target이 있을 때 1회, 저장 1회.
        foreach (var signature in new[]
                 {
                     "private void ApplyCollapsedState(PixelRect? target)",
                     "private void ApplyExpandedState(PixelRect? target)",
                 })
        {
            var body = MethodBody(source, signature);
            Assert.Equal(1, CountOf(body, "ApplyWindowRectPx("));
            Assert.Contains("if (target is PixelRect rect)", body, StringComparison.Ordinal);
            Assert.Equal(1, CountOf(body, "SaveOverlayLayoutAsync("));
        }

        var reset = MethodBody(source, "private void ResetSharedElementState()");
        foreach (var token in new[]
                 {
                     "TransitionLayer.Visibility = Visibility.Collapsed",
                     "ExpandedClockBlock.Opacity = 1",
                     "MiniClockBlock.Opacity = 1",
                     "ExpandedContent.Height = double.NaN",
                     "MiniBarContent.Height = double.NaN",
                     "RootChrome.Height = double.NaN",
                     "ExpandedContent.VerticalAlignment = VerticalAlignment.Stretch",
                     "MiniBarContent.VerticalAlignment = VerticalAlignment.Stretch",
                     "RootChrome.VerticalAlignment = VerticalAlignment.Stretch",
                     "ExpandedContent.IsHitTestVisible = true",
                     "MiniBarContent.IsHitTestVisible = true",
                 })
            Assert.Contains(token, reset, StringComparison.Ordinal);

        // 중단: 현재 모드 쪽 내용만 보이고 두 내용 모두 불투명이다. 메모와 대기 창도 버린다.
        var abort = MethodBody(source, "private void AbortTransition()");
        foreach (var token in new[]
                 {
                     "++_transitionGeneration;",
                     "_displayMode == OverlayDisplayMode.MiniBar",
                     "ExpandedContent.Opacity = 1;",
                     "MiniBarContent.Opacity = 1;",
                     "ExpandedContent.Visibility = ",
                     "MiniBarContent.Visibility = ",
                     "_pendingWindowRect = null;",
                     "_transitionInProgress = false;",
                 })
            Assert.Contains(token, abort, StringComparison.Ordinal);

        // 즉시 확정: 해제 뒤 끝 상태 메서드로 간다(그 안에서 Reset이 돈다).
        var finish = MethodBody(source, "private void FinishTransitionNow()");
        var finishClear = IndexOrFail(finish, "ClearTransitionAnimations();");
        Assert.True(finishClear < IndexOrFail(finish, "ApplyCollapsedState(_pendingWindowRect);"));
        Assert.True(finishClear < IndexOrFail(finish, "ApplyExpandedState(_pendingWindowRect);"));

        // 애니메이션 꺼짐이면 즉시 적용한다.
        var beginCollapse = MethodBody(source, "private Task BeginCollapseAsync()");
        var beginExpand = MethodBody(source, "private Task BeginExpandAsync()");
        Assert.Contains("SystemParameters.ClientAreaAnimation", beginCollapse, StringComparison.Ordinal);
        Assert.Contains("SystemParameters.ClientAreaAnimation", beginExpand, StringComparison.Ordinal);
        Assert.Contains("ApplyCollapsedState(plan.TargetWindow);", beginCollapse, StringComparison.Ordinal);
        Assert.Contains("ApplyExpandedState(plan.TargetWindow);", beginExpand, StringComparison.Ordinal);

        // 접기 t0: 막대 내용을 Visible로 둔 다음 UpdateLayout, 그다음 측정. 도착 사각형은 기준 모서리 이동과 Left 차이로 보정한다.
        var collapseVisible = IndexOrFail(collapse, "MiniBarContent.Visibility = Visibility.Visible;");
        var collapseLayout = IndexOrFail(collapse, "UpdateLayout();", collapseVisible);
        var collapseFrom = IndexOrFail(collapse, "RectIn(ExpandedClockBlock, TransitionLayer)", collapseLayout);
        var collapseTo = IndexOrFail(collapse, "RectIn(MiniClockBlock, TransitionLayer)", collapseLayout);
        Assert.True(collapseFrom > collapseLayout && collapseTo > collapseLayout);
        Assert.Contains("OverlayLayoutPlanner.AnchorEdgeShiftPx(win, target, _pendingAnchor) / scale", collapse, StringComparison.Ordinal);
        Assert.Contains("(target.Left - win.Left) / scale", collapse, StringComparison.Ordinal);
        Assert.DoesNotContain("OverlayLayoutPlanner.ResizeWindow(", collapse, StringComparison.Ordinal);
        Assert.DoesNotContain("OverlayLayoutPlanner.ChooseResizeAnchor(", collapse, StringComparison.Ordinal);
        Assert.True(IndexOrFail(collapse, "ShowGhostAt(from, true)") > collapseTo);
        Assert.Contains("StartGhostMorph(from, to, true)", collapse, StringComparison.Ordinal);
        Assert.Contains("OverlayLayoutMetrics.IncomingFadeBeginMilliseconds", collapse, StringComparison.Ordinal);

        // 펼치기: 단계 A와 B 어디에서도 막대 내용을 Collapsed로 바꾸지 않는다(측정 대상이 사라지지 않게).
        Assert.DoesNotContain("MiniBarContent.Visibility = Visibility.Collapsed", expand, StringComparison.Ordinal);
        // 단계 B: 창 적용 → 펼친 내용 Visible → UpdateLayout → 새 창 기준으로 출발 사각형을 다시 잰다.
        var expandApply = IndexOrFail(expand, "ApplyWindowRectPx(target);");
        var expandVisible = IndexOrFail(expand, "ExpandedContent.Visibility = Visibility.Visible;", expandApply);
        var expandLayout = IndexOrFail(expand, "UpdateLayout();", expandVisible);
        var expandFrom = IndexOrFail(expand, "RectIn(MiniClockBlock, TransitionLayer)", expandLayout);
        IndexOrFail(expand, "RectIn(ExpandedClockBlock, TransitionLayer)", expandLayout);
        Assert.True(IndexOrFail(expand, "ShowGhostAt(from, false)", expandFrom) > expandFrom);
        Assert.Contains("StartGhostMorph(from, to, false)", expand, StringComparison.Ordinal);
        Assert.Contains("_pendingWindowRect = null;", expand.Substring(expandApply), StringComparison.Ordinal);
        Assert.Contains("OverlayLayoutMetrics.IncomingFadeBeginMilliseconds", expand, StringComparison.Ordinal);
        // 단계 A도 측정 전에 레이아웃을 확정한다(붙인 목록을 뗀 직후 행 높이가 바뀌었을 수 있다).
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
        Assert.Equal("3", (string?)layer.Attribute("Grid.RowSpan"));
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
}
