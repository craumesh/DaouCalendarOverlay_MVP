using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Interop;
using DaouCalendarOverlay.Models;
using DaouCalendarOverlay.Services;
using DaouCalendarOverlay.ViewModels;
using WpfApplication = System.Windows.Application;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfButton = System.Windows.Controls.Button;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using WpfSelector = System.Windows.Controls.Primitives.Selector;
using WpfTextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;
using WpfThumb = System.Windows.Controls.Primitives.Thumb;
using Forms = System.Windows.Forms;

namespace DaouCalendarOverlay.Views;

public partial class MainWindow : Window
{
    private static readonly CultureInfo KoreanCulture = CultureInfo.GetCultureInfo("ko-KR");

    private readonly MainViewModel _vm = new();
    private bool _allowClose;
    private bool _returnToDayListAfterDetails;
    private System.Windows.Threading.DispatcherTimer? _saveBoundsTimer;
    private System.Windows.Threading.DispatcherTimer? _saveOpacityTimer;
    private System.Windows.Threading.DispatcherTimer? _filterDebounceTimer;
    private System.Windows.Threading.DispatcherTimer? _clockTimer;
    private bool _applyingSettings;
    private bool _suppressFilterApply;
    private bool _opacityUiReady;
    private bool _positionLocked;
    private bool _snapEnabled = true;
    private HwndSource? _hwndSource;
    private OverlayDisplayMode _displayMode = OverlayDisplayMode.Expanded;
    private bool _transitionInProgress;
    private int _transitionGeneration;
    private bool _suppressBoundsSave;
    private AttachDirection? _attached;
    private System.Windows.Threading.DispatcherTimer? _miniTickerTimer;
    private IReadOnlyList<DaouCalendarEvent> _todayVisible = Array.Empty<DaouCalendarEvent>();
    private IReadOnlyList<DaouCalendarEvent> _miniRemaining = Array.Empty<DaouCalendarEvent>();
    private IReadOnlyList<string> _miniKeys = Array.Empty<string>();
    private int _miniOffset;
    private bool _miniHover;
    private bool _tickerAnimating;
    private bool _miniDirty;
    private int _tickerGeneration;
    // 전환 애니메이션 중에만 의미가 있다. 아직 적용하지 않은 목표 창은 적용 뒤 null로 돌린다.
    private OverlayDisplayMode _pendingMode = OverlayDisplayMode.Expanded;
    private PixelRect? _pendingWindowRect;
    private VerticalAnchor _pendingAnchor = VerticalAnchor.Top;
    // 목록을 붙이기 직전의 막대 창 사각형(px). 떼기에서 이 값으로 정확히 되돌린다.
    private PixelRect? _preAttachWindowPx;

    private static readonly SolidColorBrush MiniTimeBrush = CreateFrozenBrush(0xB8, 0xBD, 0xC8);
    private static readonly SolidColorBrush MiniTitleBrush = CreateFrozenBrush(0xE7, 0xEA, 0xF0);

    public MainWindow(AppSettings settings)
    {
        InitializeComponent();
        DataContext = _vm;
        ApplySettings(settings);
        ApplyInitialDisplayMode(settings.OverlayMinimized);
        _opacityUiReady = true;
        StartClock();

        Closing += (_, e) =>
        {
            if (_allowClose)
                return;

            e.Cancel = true;
            HideOverlay();
        };

        SourceInitialized += MainWindow_SourceInitialized;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
                UpdateTickerState();
            else
            {
                // 숨겨지는 순간 늦게 오는 Completed가 상태를 덮지 않도록 끝 상태를 먼저 확정한다.
                if (_transitionInProgress)
                    FinishTransitionNow();
                _miniTickerTimer?.Stop();
            }
        };
        Closed += (_, _) =>
        {
            _clockTimer?.Stop();
            _miniTickerTimer?.Stop();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
            if (_hwndSource is not null)
                _hwndSource.RemoveHook(WndProc);
        };
        LocationChanged += (_, _) => ScheduleBoundsSave();
    }

    public void ApplySettings(AppSettings settings)
    {
        _applyingSettings = true;
        try
        {
            Topmost = settings.AlwaysOnTop;
            _positionLocked = settings.PositionLocked;
            _snapEnabled = settings.SnapToScreenEdges;
            _vm.SetConfiguredCalendars(settings.CalendarIds);
            _vm.SetKnownCalendarNames(settings.CalendarNames);
            _vm.SetHiddenCalendars(settings.HiddenCalendarIds);

            var opacity = Math.Clamp(settings.UiOpacity, 0.0, 1.0);
            Opacity = 1.0;
            ApplySurfaceOpacity(opacity);
            if (OpacitySlider is not null)
                OpacitySlider.Value = opacity;
            UpdateOpacityLabel(opacity);

            // 전환 중이거나 목록이 붙어 있을 때 저장된 막대 위치를 다시 대입하면 창이 튄다.
            if (!_transitionInProgress && _attached is null)
            {
                if (settings.Left is double left && settings.Top is double top &&
                    double.IsFinite(left) && double.IsFinite(top))
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = left;
                    Top = top;
                }
                else
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = SystemParameters.WorkArea.Right - Width - 20;
                    Top = SystemParameters.WorkArea.Top + 20;
                }
            }
        }
        finally
        {
            _applyingSettings = false;
        }

        ReloadTodayVisible();

        if (IsLoaded)
            Dispatcher.BeginInvoke(new Action(ClampToVisibleWorkArea));
    }

    public void SetHiddenCalendars(IEnumerable<string> hiddenCalendarIds)
    {
        _vm.SetHiddenCalendars(hiddenCalendarIds);
        ReloadTodayVisible();
    }

    public IReadOnlyList<CalendarDescriptor> GetCalendarDescriptors() => _vm.GetCalendarDescriptors();

    public void SetKnownCalendarNames(IReadOnlyDictionary<string, string>? names) => _vm.SetKnownCalendarNames(names);

    public bool IsCalendarVisible(string calendarId) => _vm.IsCalendarVisible(calendarId);

    public void SetPositionLocked(bool locked) => _positionLocked = locked;

    public void SetEvents(IEnumerable<DaouCalendarEvent> events, DateTimeOffset updatedAt, string status)
    {
        _vm.SetEvents(events);
        _vm.SetLastUpdated(updatedAt == default ? null : updatedAt);
        _vm.SetStatus(status, false);
        ReloadTodayVisible();
    }

    public void SetStatus(string text, bool isError) => _vm.SetStatus(text, isError);
    public void SetLoginRequired(bool required) => _vm.SetLoginRequired(required);
    public (DateTimeOffset From, DateTimeOffset To) GetVisibleRange() => _vm.GetVisibleRange();

    public void AllowCloseAndClose()
    {
        _allowClose = true;
        Close();
    }

    /// <summary>
    /// 실제 조작 컨트롤과 날짜 셀을 제외한 빈 영역은 창 드래그 영역으로 사용한다.
    /// 날짜 셀은 클릭해서 해당 날짜의 전체 일정 목록을 여는 데 사용한다.
    /// </summary>
    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_transitionInProgress ||
            _positionLocked ||
            EventDetailsOverlay.Visibility == Visibility.Visible ||
            DayEventsOverlay.Visibility == Visibility.Visible)
            return;

        if (e.LeftButton != MouseButtonState.Pressed || IsInteractiveSource(e.OriginalSource))
            return;

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // 마우스 버튼 상태가 DragMove 직전에 바뀐 경우 무시한다.
        }
    }

    /// <summary>비동기 이벤트 핸들러에서 새어 나온 예외를 로그 + 상태 텍스트로 처리한다.</summary>
    private static void ReportHandlerFailure(string category, Exception ex)
    {
        if (WpfApplication.Current is App app)
            app.ReportError(category, "UI 동작 실패", ex);
        else
            LogService.Error(category, "UI 동작 실패", ex);
    }

    private static async Task RunGuardedAsync(string category, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            ReportHandlerFailure(category, ex);
        }
    }

    private async void PrevMonth_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _vm.MoveMonth(-1);
            await ((App)WpfApplication.Current).RefreshAsync(false);
        }
        catch (Exception ex)
        {
            ReportHandlerFailure("ui.prevMonth", ex);
        }
    }

    private async void NextMonth_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _vm.MoveMonth(1);
            await ((App)WpfApplication.Current).RefreshAsync(false);
        }
        catch (Exception ex)
        {
            ReportHandlerFailure("ui.nextMonth", ex);
        }
    }

    private async void Today_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _vm.GoToday();
            await ((App)WpfApplication.Current).RefreshAsync(false);
        }
        catch (Exception ex)
        {
            ReportHandlerFailure("ui.today", ex);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) =>
        await RunGuardedAsync("ui.refresh", () => ((App)WpfApplication.Current).RefreshAsync(true));

    private async void Settings_Click(object sender, RoutedEventArgs e) =>
        await RunGuardedAsync("ui.settings", () => ((App)WpfApplication.Current).OpenSettingsAsync());

    private void Login_Click(object sender, RoutedEventArgs e) =>
        ((App)WpfApplication.Current).OpenDaouOffice();

    private void Hide_Click(object sender, RoutedEventArgs e) => HideOverlay();

    /// <summary>
    /// 오버레이를 트레이로 숨긴다. ×, Esc, 우클릭 메뉴, Alt+F4(Closing), 트레이 토글이 모두 이 메서드를 거친다.
    /// 펼침 모드에서 열린 목록·상세는 그대로 둔 채 숨긴다.
    /// </summary>
    public void HideOverlay()
    {
        if (_transitionInProgress)
            FinishTransitionNow();

        if (_displayMode == OverlayDisplayMode.MiniBar && _attached is not null)
            CloseAttached();

        Hide();
    }

    /// <summary>
    /// 막대에 오늘 일정 목록을 붙여 연다. 창 위치·크기 변경은 ApplyWindowRectPx 1회뿐이다.
    /// 붙인 동안은 위치 저장을 막고(_suppressBoundsSave), 드래그는 오버레이가 보이는 동안 이미 막혀 있다.
    /// </summary>
    private void OpenAttachedDayList()
    {
        // 전환 마무리는 위치 저장 억제를 풀므로, 전환 중에는 열지 않는다(붙인 상태의 위치가 저장되지 않게).
        if (_transitionInProgress || GetWindowRectPx() is not PixelRect win)
            return;

        var scale = GetDpiScale();
        var inset = OverlayLayoutMetrics.DipToPx(OverlayLayoutMetrics.WindowMarginDip, scale);
        var work = GetWorkAreaPx(win);
        var dir = OverlayLayoutPlanner.ChooseAttachDirection(
            win.Deflate(inset),
            work,
            OverlayLayoutMetrics.DipToPx(OverlayLayoutMetrics.AttachedRegionHeightDip, scale));

        _preAttachWindowPx = win;
        _suppressBoundsSave = true;
        _saveBoundsTimer?.Stop();
        if (_tickerAnimating)
            StopMiniTicker();
        else
            _miniTickerTimer?.Stop();
        _attached = dir;

        var row = dir == AttachDirection.Below ? 2 : 0;
        var attachRow = dir == AttachDirection.Below ? AttachBelowRow : AttachAboveRow;
        attachRow.Height = new GridLength(OverlayLayoutMetrics.AttachedRegionHeightDip);
        Grid.SetRow(DayEventsOverlay, row);
        Grid.SetRow(EventDetailsOverlay, row);
        var margin = new Thickness(OverlayLayoutMetrics.WindowMarginDip, 0, OverlayLayoutMetrics.WindowMarginDip, 0);
        DayEventsOverlay.Margin = margin;
        EventDetailsOverlay.Margin = margin;
        // 알파 0 픽셀은 레이어드 창에서 클릭이 통과한다.
        DayEventsBackdrop.Background = Brushes.Transparent;
        EventDetailsBackdrop.Background = Brushes.Transparent;
        var cardAlignment = dir == AttachDirection.Below ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        DayEventsCard.VerticalAlignment = cardAlignment;
        EventDetailsCard.VerticalAlignment = cardAlignment;

        // 높이는 막대 창과 붙인 영역을 합친 DIP를 한 번에 환산한다(떼기 왕복이 px 단위로 맞게).
        ApplyWindowRectPx(OverlayLayoutPlanner.ResizeWindow(
            win,
            OverlayLayoutMetrics.DipToPx(
                OverlayLayoutMetrics.MiniWindowHeightDip + OverlayLayoutMetrics.AttachedRegionHeightDip, scale),
            OverlayLayoutPlanner.ToAnchor(dir),
            work,
            inset));

        // 끝난 일정을 포함하고 숨긴 캘린더만 제외한다. 검색 필터는 적용하지 않는다.
        ShowDayEvents(DateTime.Today, _vm.GetUnfilteredEventChipsForDate(DateTime.Today));
    }

    /// <summary>
    /// 막대에 붙은 목록을 떼고 오버레이 배치를 펼침 모드 값으로 되돌린다.
    /// 창은 붙이기 직전 사각형으로 ApplyWindowRectPx 1회에 복원한다(붙인 동안은 드래그가 막혀 위치가 바뀌지 않는다).
    /// 전환 중에는 불리지 않는다. 펼치기는 전환 플래그를 세우기 전에 OnMiniBarLeaving에서 먼저 떼고,
    /// HideOverlay는 FinishTransitionNow 뒤에 부르며, 전환 중에는 목록을 붙이지 않는다.
    /// </summary>
    private void CloseAttached()
    {
        if (_attached is not AttachDirection dir)
            return;

        _returnToDayListAfterDetails = false;
        DayEventsOverlay.Visibility = Visibility.Collapsed;
        EventDetailsOverlay.Visibility = Visibility.Collapsed;

        if (_preAttachWindowPx is PixelRect pre)
        {
            ApplyWindowRectPx(pre);
        }
        else if (GetWindowRectPx() is PixelRect win)
        {
            var scale = GetDpiScale();
            ApplyWindowRectPx(OverlayLayoutPlanner.ResizeWindow(
                win,
                OverlayLayoutMetrics.DipToPx(OverlayLayoutMetrics.MiniWindowHeightDip, scale),
                OverlayLayoutPlanner.ToAnchor(dir),
                GetWorkAreaPx(win),
                OverlayLayoutMetrics.DipToPx(OverlayLayoutMetrics.WindowMarginDip, scale)));
        }

        AttachBelowRow.Height = new GridLength(0);
        AttachAboveRow.Height = new GridLength(0);
        RestoreOverlayPlacement();

        _attached = null;
        _preAttachWindowPx = null;
        _suppressBoundsSave = false;
        ScheduleBoundsSave();
        ClampToVisibleWorkArea();
        UpdateTickerState();
    }

    // 목록·상세 오버레이를 펼침 모드의 배치(Row 1, 여백 16, 어두운 배경판, 가운데 정렬)로 되돌린다.
    private void RestoreOverlayPlacement()
    {
        Grid.SetRow(DayEventsOverlay, 1);
        Grid.SetRow(EventDetailsOverlay, 1);
        DayEventsOverlay.Margin = new Thickness(16);
        EventDetailsOverlay.Margin = new Thickness(16);
        DayEventsBackdrop.Background = CreateBackdropBrush();
        EventDetailsBackdrop.Background = CreateBackdropBrush();
        DayEventsCard.VerticalAlignment = VerticalAlignment.Center;
        EventDetailsCard.VerticalAlignment = VerticalAlignment.Center;
    }

    private static SolidColorBrush CreateBackdropBrush()
    {
        var brush = new SolidColorBrush(Color.FromArgb(0x9A, 0x08, 0x0A, 0x0E));
        brush.Freeze();
        return brush;
    }

    // 목록과 상세가 모두 닫히면 붙인 목록을 뗀다. 목록과 상세를 오가는 중에는 한쪽이 보이므로 떼지 않는다.
    private void DetachIfAllOverlaysClosed()
    {
        if (_attached is not null &&
            DayEventsOverlay.Visibility == Visibility.Collapsed &&
            EventDetailsOverlay.Visibility == Visibility.Collapsed)
            CloseAttached();
    }

    // 막대 모드 진입·이탈 시점 훅. 막대 일정 갱신과 붙인 목록 정리가 여기에 연결된다.
    private void OnMiniBarEntered() => ReloadTodayVisible();

    private void OnMiniBarLeaving()
    {
        // 붙인 목록은 펼치기 시작 전에 즉시 뗀다. 창 사각형은 이 호출 뒤에 다시 읽어야 한다.
        if (_attached is not null)
            CloseAttached();

        StopMiniTicker();
        // 막대가 접히면 일정 영역에서 MouseLeave가 오지 않을 수 있으므로 호버 상태를 직접 푼다.
        _miniHover = false;
    }

    private void MiniAgenda_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (_transitionInProgress)
                return;

            if (_attached is not null)
                CloseAttached();
            else
                OpenAttachedDayList();

            e.Handled = true;
        }
        catch (Exception ex)
        {
            ReportHandlerFailure("ui.miniAgenda", ex);
        }
    }

    private void MiniAgenda_MouseEnter(object sender, MouseEventArgs e)
    {
        // 진행 중인 슬라이드는 그대로 끝내고 다음 순환만 멈춘다.
        _miniHover = true;
        _miniTickerTimer?.Stop();
    }

    private void MiniAgenda_MouseLeave(object sender, MouseEventArgs e)
    {
        _miniHover = false;
        // 멈췄다가 다시 시작하므로 4초를 처음부터 센다.
        _miniTickerTimer?.Stop();
        UpdateTickerState();
    }

    // 막대 일정의 원본은 숨긴 캘린더만 적용한 오늘 일정이다. 검색 필터는 적용하지 않는다.
    private void ReloadTodayVisible()
    {
        _todayVisible = _vm.GetVisibleUnfilteredEventsForDate(DateTime.Today);
        RefreshMiniAgenda(DateTimeOffset.Now, force: true);
    }

    private void RefreshMiniAgenda(DateTimeOffset now, bool force = false)
    {
        var remaining = MiniBarAgenda.SelectRemaining(_todayVisible, now);
        var keys = remaining.Select(MiniBarAgenda.KeyOf).ToList();
        if (!force && keys.SequenceEqual(_miniKeys, StringComparer.Ordinal))
            return;

        // 슬라이드 중에는 행을 건드리지 않고 Completed에서 반영한다.
        if (_tickerAnimating)
        {
            _miniDirty = true;
            return;
        }

        _miniOffset = MiniBarAgenda.ReconcileOffset(_miniKeys, _miniOffset, keys);
        _miniKeys = keys;
        _miniRemaining = remaining;
        RebuildMiniRows();
        UpdateTickerState();
    }

    private void RebuildMiniRows()
    {
        MiniAgendaRows.Children.Clear();
        var count = _miniRemaining.Count;
        MiniAgendaEmptyText.Text = MiniBarAgenda.NoRemainingText;
        MiniAgendaEmptyText.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // 순환할 때는 들어올 한 줄을 더해 3줄을 만든다. 넘치는 줄은 일정 영역이 잘라낸다.
        var indexes = MiniBarAgenda.ShouldRotate(count)
            ? MiniBarAgenda.GetWindow(count, _miniOffset, 3)
            : MiniBarAgenda.GetWindow(count, 0, MiniBarAgenda.VisibleLineCount);
        foreach (var index in indexes)
            MiniAgendaRows.Children.Add(CreateMiniRow(MainViewModel.CreateEventChip(_miniRemaining[index])));

        MiniAgendaShift.Y = 0;
    }

    private static UIElement CreateMiniRow(EventChipViewModel chip)
    {
        var row = new Grid { Height = OverlayLayoutMetrics.MiniBarLineHeightDip };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var accent = new System.Windows.Shapes.Rectangle
        {
            Width = 3,
            Height = 12,
            RadiusX = 1.5,
            RadiusY = 1.5,
            Margin = new Thickness(0, 0, 5, 0),
            Fill = chip.AccentBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        var time = new TextBlock
        {
            Text = chip.Text,
            Width = 34,
            FontSize = 11,
            Foreground = MiniTimeBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        var title = new TextBlock
        {
            Text = chip.Title,
            FontSize = 11.5,
            Foreground = MiniTitleBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetColumn(accent, 0);
        Grid.SetColumn(time, 1);
        Grid.SetColumn(title, 2);
        row.Children.Add(accent);
        row.Children.Add(time);
        row.Children.Add(title);
        return row;
    }

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    // 순환 조건이 맞으면 타이머를 켠다. 이미 돌고 있으면 건드리지 않는다(매초 재시작하면 4초가 리셋된다).
    private void UpdateTickerState()
    {
        var shouldRun = _displayMode == OverlayDisplayMode.MiniBar &&
                        !_transitionInProgress &&
                        _attached is null &&
                        !_miniHover &&
                        IsVisible &&
                        MiniBarAgenda.ShouldRotate(_miniRemaining.Count);
        if (!shouldRun)
        {
            _miniTickerTimer?.Stop();
            return;
        }

        if (_miniTickerTimer is null)
        {
            _miniTickerTimer = new System.Windows.Threading.DispatcherTimer();
            _miniTickerTimer.Tick += MiniTickerTimer_Tick;
        }

        if (_miniTickerTimer.IsEnabled)
            return;

        _miniTickerTimer.Interval = TimeSpan.FromSeconds(OverlayLayoutMetrics.TickerIntervalSeconds);
        _miniTickerTimer.Start();
    }

    private void MiniTickerTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            var count = _miniRemaining.Count;
            if (!MiniBarAgenda.ShouldRotate(count))
            {
                _miniTickerTimer?.Stop();
                return;
            }

            if (_tickerAnimating)
                return;

            if (!SystemParameters.ClientAreaAnimation)
            {
                _miniOffset = MiniBarAgenda.NextOffset(count, _miniOffset);
                RebuildMiniRows();
                return;
            }

            _tickerAnimating = true;
            var generation = ++_tickerGeneration;
            var slide = new System.Windows.Media.Animation.DoubleAnimation(
                0,
                -OverlayLayoutMetrics.MiniBarLineHeightDip,
                TimeSpan.FromMilliseconds(OverlayLayoutMetrics.TickerSlideMilliseconds))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                }
            };
            slide.Completed += (_, _) => OnMiniSlideCompleted(generation);
            MiniAgendaShift.BeginAnimation(TranslateTransform.YProperty, slide);
        }
        catch (Exception ex)
        {
            _tickerAnimating = false;
            LogService.Warn("miniTicker", "막대 일정 순환 실패", ex);
        }
    }

    private void OnMiniSlideCompleted(int generation)
    {
        if (generation != _tickerGeneration)
            return;

        // HoldEnd 값이 남으면 이후 Y 대입이 무시되므로 애니메이션부터 해제한다.
        MiniAgendaShift.BeginAnimation(TranslateTransform.YProperty, null);
        MiniAgendaShift.Y = 0;
        _miniOffset = MiniBarAgenda.NextOffset(_miniRemaining.Count, _miniOffset);
        _tickerAnimating = false;

        if (_miniDirty)
        {
            _miniDirty = false;
            RefreshMiniAgenda(DateTimeOffset.Now, force: true);
        }
        else
        {
            RebuildMiniRows();
        }
    }

    // 막대를 떠나거나 닫을 때 순환과 슬라이드를 모두 정리한다. 늦게 오는 Completed는 generation으로 무시된다.
    private void StopMiniTicker()
    {
        _miniTickerTimer?.Stop();
        ++_tickerGeneration;
        MiniAgendaShift.BeginAnimation(TranslateTransform.YProperty, null);
        MiniAgendaShift.Y = 0;
        _tickerAnimating = false;
        _miniDirty = false;
    }

    /// <summary>생성자에서 한 번만 부른다. 저장된 최소화 상태를 애니메이션 없이 적용한다.</summary>
    private void ApplyInitialDisplayMode(bool minimized)
    {
        if (!minimized)
            return;

        Height = OverlayLayoutMetrics.MiniWindowHeightDip;
        RootChrome.Height = OverlayLayoutMetrics.MiniCardHeightDip;
        RootChrome.Padding = new Thickness(8, 4, 8, 4);
        ExpandedContent.Visibility = Visibility.Collapsed;
        MiniBarContent.Visibility = Visibility.Visible;
        _displayMode = OverlayDisplayMode.MiniBar;
    }

    private void ToggleDisplayMode_Click(object sender, RoutedEventArgs e)
    {
        _ = RunGuardedAsync("ui.toggleDisplayMode", () =>
            _displayMode == OverlayDisplayMode.Expanded ? BeginCollapseAsync() : BeginExpandAsync());
    }

    // 전환 한 번마다 창 위치·크기 변경은 ApplyWindowRectPx 1회뿐이다.
    // 전환 버튼은 창이 화면에 있을 때만 눌리므로 창 핸들이 항상 있다. 핸들이 없으면(창 생성 전)
    // 아무 상태도 바꾸지 않고 끝낸다. 그래야 모드, 화면, 저장값이 서로 어긋나지 않는다.
    private Task BeginCollapseAsync()
    {
        if (_transitionInProgress || GetWindowRectPx() is not PixelRect win)
            return Task.CompletedTask;

        _transitionInProgress = true;
        ++_transitionGeneration;
        _suppressBoundsSave = true;
        _saveBoundsTimer?.Stop();

        try
        {
            _returnToDayListAfterDetails = false;
            DayEventsOverlay.Visibility = Visibility.Collapsed;
            EventDetailsOverlay.Visibility = Visibility.Collapsed;
            // 검색창 포커스를 떼어 보이지 않는 검색어가 키를 받지 않게 한다. 포커스는 ApplyCollapsedState가 창에 다시 둔다.
            Keyboard.ClearFocus();

            // 막대는 오늘 일정만 보여 주므로 오늘이 속한 달로 돌린다. 새로고침은 전환을 막지 않는다.
            var today = DateTime.Today;
            if (_vm.DisplayMonth.Year != today.Year || _vm.DisplayMonth.Month != today.Month)
            {
                _vm.GoToday();
                _ = RunGuardedAsync("ui.collapseRefresh", () => ((App)WpfApplication.Current).RefreshAsync(false));
            }

            var scale = GetDpiScale();
            var inset = OverlayLayoutMetrics.DipToPx(OverlayLayoutMetrics.WindowMarginDip, scale);
            var work = GetWorkAreaPx(win);
            var anchor = OverlayLayoutPlanner.ChooseResizeAnchor(
                win.Deflate(inset),
                work,
                OverlayLayoutMetrics.DipToPx(OverlayLayoutMetrics.MiniCardHeightDip, scale),
                OverlayLayoutMetrics.DipToPx(OverlayLayoutMetrics.EdgeToleranceDip, scale));
            var target = OverlayLayoutPlanner.ResizeWindow(
                win,
                OverlayLayoutMetrics.DipToPx(OverlayLayoutMetrics.MiniWindowHeightDip, scale),
                anchor,
                work,
                inset);

            _pendingMode = OverlayDisplayMode.MiniBar;
            _pendingWindowRect = target;
            _pendingAnchor = anchor;

            if (SystemParameters.ClientAreaAnimation)
                StartCollapseAnimation(_transitionGeneration);
            else
                ApplyCollapsedState(target);
        }
        catch
        {
            // 정상 경로의 플래그 해제는 ApplyCollapsedState가 맡는다. 예외로 끝나면 여기서 풀어
            // 드래그와 위치 저장이 영구히 막히지 않게 한다. 예외는 RunGuardedAsync가 보고한다.
            AbortTransition();
            throw;
        }

        return Task.CompletedTask;
    }

    private Task BeginExpandAsync()
    {
        if (_transitionInProgress || GetWindowRectPx() is null)
            return Task.CompletedTask;

        // 붙인 목록은 전환 플래그를 세우기 전에 먼저 뗀다. 그래야 CloseAttached가 전환 밖에서 끝나고,
        // 떼기가 예약한 위치 저장은 바로 아래 Stop으로 취소되어 저장은 ApplyExpandedState의 한 번뿐이다(저장 원자성).
        OnMiniBarLeaving();

        _transitionInProgress = true;
        ++_transitionGeneration;
        _suppressBoundsSave = true;
        _saveBoundsTimer?.Stop();

        try
        {
            // 붙인 목록을 떼면 창 사각형이 바뀌므로 정리 뒤에 다시 읽는다.
            if (GetWindowRectPx() is not PixelRect win)
            {
                AbortTransition();
                return Task.CompletedTask;
            }

            var scale = GetDpiScale();
            var inset = OverlayLayoutMetrics.DipToPx(OverlayLayoutMetrics.WindowMarginDip, scale);
            var work = GetWorkAreaPx(win);
            var anchor = OverlayLayoutPlanner.ChooseResizeAnchor(
                win.Deflate(inset),
                work,
                OverlayLayoutMetrics.DipToPx(OverlayLayoutMetrics.ExpandedCardHeightDip, scale),
                OverlayLayoutMetrics.DipToPx(OverlayLayoutMetrics.EdgeToleranceDip, scale));
            var target = OverlayLayoutPlanner.ResizeWindow(
                win,
                OverlayLayoutMetrics.DipToPx(OverlayLayoutMetrics.ExpandedWindowHeightDip, scale),
                anchor,
                work,
                inset);

            _pendingMode = OverlayDisplayMode.Expanded;
            _pendingWindowRect = target;
            _pendingAnchor = anchor;

            if (SystemParameters.ClientAreaAnimation)
                StartExpandAnimation(_transitionGeneration);
            else
                ApplyExpandedState(target);
        }
        catch
        {
            // 정상 경로의 플래그 해제는 ApplyExpandedState가 맡는다. 예외로 끝날 때만 여기서 푼다.
            AbortTransition();
            throw;
        }

        return Task.CompletedTask;
    }

    // 접기의 끝 상태를 한 번에 적용하고 저장한다. 애니메이션 경로도 마지막에 이 메서드를 쓴다.
    // target이 null이면 창은 이미 적용된 것이다. fadeIn이면 막대 내용을 0에서 1로 띄운다.
    private void ApplyCollapsedState(PixelRect? target, bool fadeIn = false)
    {
        ExpandedContent.Visibility = Visibility.Collapsed;
        ExpandedContent.Opacity = 1;
        RootChrome.Height = OverlayLayoutMetrics.MiniCardHeightDip;
        RootChrome.VerticalAlignment = VerticalAlignment.Stretch;
        RootChrome.Padding = new Thickness(8, 4, 8, 4);
        if (target is PixelRect rect)
            ApplyWindowRectPx(rect);
        _pendingWindowRect = null;
        if (fadeIn)
        {
            FadeIn(MiniBarContent);
        }
        else
        {
            MiniBarContent.Opacity = 1;
            MiniBarContent.Visibility = Visibility.Visible;
        }

        _displayMode = OverlayDisplayMode.MiniBar;
        _suppressBoundsSave = false;
        EnsureKeyboardFocus();

        var left = Left;
        var top = Top;
        _ = RunGuardedAsync("ui.saveOverlayLayout", () =>
            ((App)WpfApplication.Current).SaveOverlayLayoutAsync(true, left, top));

        OnMiniBarEntered();
        _transitionInProgress = false;
        // OnMiniBarEntered 시점에는 전환 중이라 순환이 켜지지 않으므로 전환이 끝난 뒤 다시 판정한다.
        UpdateTickerState();
    }

    // 펼치기의 끝 상태를 한 번에 적용하고 저장한다. 애니메이션 경로도 마지막에 이 메서드를 쓴다.
    // target이 null이면 창은 이미 적용된 것이다. fadeIn이면 펼친 내용을 0에서 1로 띄운다.
    private void ApplyExpandedState(PixelRect? target, bool fadeIn = false)
    {
        MiniBarContent.Visibility = Visibility.Collapsed;
        MiniBarContent.Opacity = 1;
        RootChrome.Padding = new Thickness(8);
        RootChrome.Height = double.NaN;
        RootChrome.VerticalAlignment = VerticalAlignment.Stretch;
        if (target is PixelRect rect)
            ApplyWindowRectPx(rect);
        _pendingWindowRect = null;
        if (fadeIn)
        {
            FadeIn(ExpandedContent);
        }
        else
        {
            ExpandedContent.Opacity = 1;
            ExpandedContent.Visibility = Visibility.Visible;
        }
        _displayMode = OverlayDisplayMode.Expanded;
        _suppressBoundsSave = false;
        EnsureKeyboardFocus();

        var left = Left;
        var top = Top;
        _ = RunGuardedAsync("ui.saveOverlayLayout", () =>
            ((App)WpfApplication.Current).SaveOverlayLayoutAsync(false, left, top));

        _transitionInProgress = false;
    }

    private static Duration Ms(int milliseconds) => new(TimeSpan.FromMilliseconds(milliseconds));

    private static DoubleAnimation CreateFade(double from, double to) =>
        new(from, to, Ms(OverlayLayoutMetrics.ContentFadeMilliseconds));

    private static DoubleAnimation CreateHeightAnimation(double from, double to) =>
        new(from, to, Ms(OverlayLayoutMetrics.HeightAnimationMilliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

    // Completed가 늦게 와도 시작 때의 generation과 다르면 상태를 바꾸지 않는다.
    // 단계에서 예외가 나면 전환을 중단하고 플래그를 풀어 드래그와 위치 저장이 영구히 막히지 않게 한다.
    private void RunTransitionStep(int generation, Action step)
    {
        if (generation != _transitionGeneration)
            return;

        try
        {
            step();
        }
        catch (Exception ex)
        {
            LogService.Warn("transition", "전환 애니메이션 단계 실패", ex);
            AbortTransition();
        }
    }

    // 끝나면 HoldEnd 값을 해제한 뒤 로컬 값을 1로 둔다.
    private void FadeIn(UIElement element)
    {
        var generation = _transitionGeneration;
        element.BeginAnimation(OpacityProperty, null);
        element.Opacity = 0;
        element.Visibility = Visibility.Visible;
        var fade = CreateFade(0, 1);
        fade.Completed += (_, _) => RunTransitionStep(generation, () =>
        {
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = 1;
        });
        element.BeginAnimation(OpacityProperty, fade);
    }

    private void ClearTransitionAnimations()
    {
        ExpandedContent.BeginAnimation(OpacityProperty, null);
        MiniBarContent.BeginAnimation(OpacityProperty, null);
        RootChrome.BeginAnimation(OpacityProperty, null);
        RootChrome.BeginAnimation(HeightProperty, null);
    }

    // 접기: 펼친 내용 페이드아웃, 카드 높이 축소, 창 1회 변경, 막대 내용 페이드인 순이다.
    private void StartCollapseAnimation(int generation)
    {
        var target = _pendingWindowRect;
        var fadeOut = CreateFade(1, 0);
        fadeOut.Completed += (_, _) => RunTransitionStep(generation, () =>
        {
            ExpandedContent.BeginAnimation(OpacityProperty, null);
            ExpandedContent.Opacity = 1;
            ExpandedContent.Visibility = Visibility.Collapsed;

            // 내용이 접히면 자동 높이가 줄어드는 프레임이 생기므로 현재 높이를 먼저 로컬 값으로 고정한다.
            var from = RootChrome.ActualHeight;
            RootChrome.Height = from;
            RootChrome.VerticalAlignment = _pendingAnchor == VerticalAnchor.Bottom
                ? VerticalAlignment.Bottom
                : VerticalAlignment.Top;

            var shrink = CreateHeightAnimation(from, OverlayLayoutMetrics.MiniCardHeightDip);
            shrink.Completed += (_, _) => RunTransitionStep(generation, () =>
            {
                RootChrome.BeginAnimation(HeightProperty, null);
                ApplyCollapsedState(target, fadeIn: true);
            });
            RootChrome.BeginAnimation(HeightProperty, shrink);
        });
        ExpandedContent.BeginAnimation(OpacityProperty, fadeOut);
    }

    // 펼치기: 막대 내용 페이드아웃, 창 1회 변경(카드는 창보다 커질 수 없으므로 먼저 키운다),
    // 카드 높이 확대, 펼친 내용 페이드인 순이다.
    private void StartExpandAnimation(int generation)
    {
        var fadeOut = CreateFade(1, 0);
        fadeOut.Completed += (_, _) => RunTransitionStep(generation, () =>
        {
            MiniBarContent.BeginAnimation(OpacityProperty, null);
            MiniBarContent.Opacity = 1;
            MiniBarContent.Visibility = Visibility.Collapsed;

            var scale = GetDpiScale();
            var inset = OverlayLayoutMetrics.DipToPx(OverlayLayoutMetrics.WindowMarginDip, scale);
            var target = _pendingWindowRect ?? throw new InvalidOperationException("펼치기 목표 창이 없습니다.");
            if (GetWindowRectPx() is PixelRect win)
            {
                var work = GetWorkAreaPx(win);
                var anchor = OverlayLayoutPlanner.ChooseResizeAnchor(
                    win.Deflate(inset),
                    work,
                    OverlayLayoutMetrics.DipToPx(OverlayLayoutMetrics.ExpandedCardHeightDip, scale),
                    OverlayLayoutMetrics.DipToPx(OverlayLayoutMetrics.EdgeToleranceDip, scale));
                target = OverlayLayoutPlanner.ResizeWindow(
                    win,
                    OverlayLayoutMetrics.DipToPx(OverlayLayoutMetrics.ExpandedWindowHeightDip, scale),
                    anchor,
                    work,
                    inset);
                _pendingAnchor = anchor;
            }

            _pendingWindowRect = target;
            RootChrome.Height = OverlayLayoutMetrics.MiniCardHeightDip;
            RootChrome.VerticalAlignment = _pendingAnchor == VerticalAnchor.Bottom
                ? VerticalAlignment.Bottom
                : VerticalAlignment.Top;
            RootChrome.Padding = new Thickness(8);
            ApplyWindowRectPx(target);
            _pendingWindowRect = null;

            var to = target.Height / scale - 2 * OverlayLayoutMetrics.WindowMarginDip;
            var grow = CreateHeightAnimation(OverlayLayoutMetrics.MiniCardHeightDip, to);
            grow.Completed += (_, _) => RunTransitionStep(generation, () =>
            {
                RootChrome.BeginAnimation(HeightProperty, null);
                ApplyExpandedState(null, fadeIn: true);
            });
            RootChrome.BeginAnimation(HeightProperty, grow);
        });
        MiniBarContent.BeginAnimation(OpacityProperty, fadeOut);
    }

    /// <summary>
    /// 진행 중인 전환을 즉시 끝 상태로 확정한다. 숨기기, 자정, 창이 숨겨질 때 부른다.
    /// 아직 적용하지 않은 목표 창이 있으면 1회 적용하고 저장까지 끝낸다. 늦게 오는 Completed는 generation으로 무효가 된다.
    /// </summary>
    private void FinishTransitionNow()
    {
        if (!_transitionInProgress)
            return;

        ++_transitionGeneration;
        try
        {
            ClearTransitionAnimations();
            if (_pendingMode == OverlayDisplayMode.MiniBar)
                ApplyCollapsedState(_pendingWindowRect);
            else
                ApplyExpandedState(_pendingWindowRect);
        }
        catch (Exception ex)
        {
            LogService.Warn("transition", "전환 즉시 확정 실패", ex);
        }
        finally
        {
            _pendingWindowRect = null;
            _suppressBoundsSave = false;
            _transitionInProgress = false;
        }
    }

    // 전환을 되돌리지 않고 멈춘다. 늦은 Completed를 무효화하고 애니메이션 값을 해제한다.
    private void AbortTransition()
    {
        ++_transitionGeneration;
        ClearTransitionAnimations();
        ExpandedContent.Opacity = 1;
        MiniBarContent.Opacity = 1;
        _pendingWindowRect = null;
        _suppressBoundsSave = false;
        _transitionInProgress = false;
    }

    // 전환으로 포커스를 가진 요소가 접히면 포커스가 사라져 PreviewKeyDown이 오지 않는다(Esc·F5 불능).
    // 마무리 때 포커스를 창 안의 보이는 요소에 둔다. 창 자체를 우선하고, 안 되면 막대의 펼치기 버튼에 둔다.
    private void EnsureKeyboardFocus()
    {
        if (HasVisibleFocusInWindow())
            return;

        Keyboard.Focus(this);
        if (HasVisibleFocusInWindow())
            return;

        if (_displayMode == OverlayDisplayMode.MiniBar)
            MiniExpandButton.Focus();
        else
            MinimizeToggleButton.Focus();
    }

    private bool HasVisibleFocusInWindow() =>
        Keyboard.FocusedElement is UIElement { IsVisible: true } focused &&
        (ReferenceEquals(focused, this) || IsAncestorOf(focused));

    private double GetDpiScale() => VisualTreeHelper.GetDpi(this).DpiScaleY;

    private PixelRect? GetWindowRectPx()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect))
            return null;

        return new PixelRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
    }

    // 작업 영역은 창이 걸친 모니터 기준의 물리 px이다.
    private static PixelRect GetWorkAreaPx(PixelRect rect)
    {
        var work = Forms.Screen.FromRectangle(
            new System.Drawing.Rectangle(rect.Left, rect.Top, rect.Width, rect.Height)).WorkingArea;
        return new PixelRect(work.Left, work.Top, work.Right, work.Bottom);
    }

    // 창 위치·크기는 이 메서드로만 바꾼다. WPF Left/Top/Height를 따로 대입하면 두 번 바뀌어 깜빡인다.
    private void ApplyWindowRectPx(PixelRect rect)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        SetWindowPos(hwnd, IntPtr.Zero, rect.Left, rect.Top, rect.Width, rect.Height, SwpNoZOrder | SwpNoActivate);
    }

    private void DayCell_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: DateTime date } cell)
            return;

        ShowDayEvents(date);
        e.Handled = true;
    }

    // 날짜 셀 경로: 검색 필터를 적용한 목록을 연다.
    private void ShowDayEvents(DateTime date) => ShowDayEvents(date, _vm.GetEventChipsForDate(date));

    // 목록 본문: 호출자가 고른 칩(날짜 셀은 검색 필터 적용, 막대는 숨긴 캘린더만 제외)을 그대로 보인다.
    private void ShowDayEvents(DateTime date, IReadOnlyList<EventChipViewModel> chips)
    {
        DayEventsDateText.Text = date.ToString("M월 d일 dddd", KoreanCulture);
        DayEventsCountText.Text = chips.Count == 0 ? "일정 없음" : $"일정 {chips.Count}개";
        DayEventsItemsControl.ItemsSource = chips;
        DayEventsItemsControl.Visibility = chips.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        DayEventsEmptyText.Visibility = chips.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DayEventsOverlay.Visibility = Visibility.Visible;
    }

    private void DayListEvent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { Tag: DaouCalendarEvent calendarEvent })
            return;

        // 상세를 먼저 열고 목록을 숨긴다. HideDayEvents를 거치지 않으므로 붙인 목록의 떼기 검사가 일어나지 않는다.
        ShowEventDetails(calendarEvent, returnToDayList: true);
        DayEventsOverlay.Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    private void CloseDayEvents_Click(object sender, RoutedEventArgs e) => HideDayEvents();

    private void DayEventsBackdrop_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // MouseDown 시점에 오버레이를 제거하면 같은 클릭의 MouseUp이 아래 날짜 셀까지
        // 이어져 다른 날짜 목록이 즉시 열릴 수 있다. MouseUp까지 배경을 유지한 뒤 닫는다.
        HideDayEvents();
        e.Handled = true;
    }

    private void HideDayEvents()
    {
        DayEventsOverlay.Visibility = Visibility.Collapsed;
        DayEventsItemsControl.ItemsSource = null;
        DetachIfAllOverlaysClosed();
    }

    private void ShowEventDetails(DaouCalendarEvent calendarEvent, bool returnToDayList)
    {
        _returnToDayListAfterDetails = returnToDayList;
        DetailCloseButton.Content = returnToDayList ? "목록으로" : "닫기";

        DetailTitleText.Text = calendarEvent.Summary;
        DetailTimeText.Text = calendarEvent.IsAllDay
            ? $"{calendarEvent.StartTime:yyyy년 M월 d일} · 종일"
            : $"{calendarEvent.StartTime:yyyy년 M월 d일 HH:mm} ~ {calendarEvent.EndTime:HH:mm}";
        DetailCalendarText.Text = calendarEvent.CalendarDisplayName;
        DetailOwnerText.Text = calendarEvent.OwnerOrCreatorDisplay;
        DetailAudienceText.Text = calendarEvent.AttendeeDisplay;
        DetailVisibilityText.Text = calendarEvent.VisibilityDisplay;

        if (string.IsNullOrWhiteSpace(calendarEvent.Location))
        {
            DetailLocationText.Text = string.Empty;
            DetailLocationRow.Visibility = Visibility.Collapsed;
        }
        else
        {
            DetailLocationText.Text = calendarEvent.Location;
            DetailLocationRow.Visibility = Visibility.Visible;
        }

        DetailDescriptionText.Text = string.IsNullOrWhiteSpace(calendarEvent.Description)
            ? "추가 설명 없음"
            : calendarEvent.Description;
        DetailDescriptionText.Foreground = string.IsNullOrWhiteSpace(calendarEvent.Description)
            ? new SolidColorBrush(Color.FromRgb(0x7C, 0x81, 0x8B))
            : new SolidColorBrush(Color.FromRgb(0xE0, 0xE3, 0xE8));

        EventDetailsOverlay.Visibility = Visibility.Visible;
    }

    private void CloseEventDetails_Click(object sender, RoutedEventArgs e) => HideEventDetails();

    private void EventDetailsBackdrop_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // MouseDown 시점에 오버레이를 닫으면 이어지는 MouseUp이 아래 날짜 셀에 전달되어
        // 의도치 않게 다른 날짜의 일정 목록이 열릴 수 있다. MouseUp까지 오버레이를 유지한 뒤 닫는다.
        HideEventDetails();
        e.Handled = true;
    }

    private void HideEventDetails()
    {
        EventDetailsOverlay.Visibility = Visibility.Collapsed;

        if (_returnToDayListAfterDetails)
        {
            _returnToDayListAfterDetails = false;
            DayEventsOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            _returnToDayListAfterDetails = false;
        }

        DetachIfAllOverlaysClosed();
    }

    private void FilterCriteria_Changed(object sender, RoutedEventArgs e)
    {
        // 초기화 버튼이 콤보/텍스트를 되돌리는 동안에는 적용하지 않는다(재구성은 ResetFilter에서 1회).
        if (_suppressFilterApply)
            return;

        if (FilterFieldCombo is null || FilterTextBox is null)
            return;

        // 드롭다운 변경은 즉시 적용하고, 검색어 입력은 짧게 debounce 한다.
        // 매 키 입력마다 42개 날짜 셀 전체를 재구성하면 체감상 버벅임이 발생한다.
        if (sender is ComboBox)
        {
            _filterDebounceTimer?.Stop();
            ApplyCurrentFilter();
            return;
        }

        _filterDebounceTimer ??= new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(180)
        };
        _filterDebounceTimer.Stop();
        _filterDebounceTimer.Tick -= FilterDebounceTimer_Tick;
        _filterDebounceTimer.Tick += FilterDebounceTimer_Tick;
        _filterDebounceTimer.Start();
    }

    private void FilterDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _filterDebounceTimer?.Stop();
        ApplyCurrentFilter();
    }

    private void ApplyCurrentFilter()
    {
        if (FilterFieldCombo is null || FilterTextBox is null)
            return;

        var field = FilterFieldCombo.SelectedValue as string ?? "Title";
        _vm.SetFilter(field, FilterTextBox.Text);
    }

    private void FilterReset_Click(object sender, RoutedEventArgs e) => ResetFilter();

    // 콤보/텍스트 변경 이벤트를 억제한 채 되돌리고, 달력 재구성은 ClearFilter 한 번으로 끝낸다.
    private void ResetFilter()
    {
        if (FilterFieldCombo is null || FilterTextBox is null)
            return;

        _filterDebounceTimer?.Stop();
        _suppressFilterApply = true;
        try
        {
            FilterFieldCombo.SelectedIndex = 0;
            FilterTextBox.Clear();
        }
        finally
        {
            _suppressFilterApply = false;
        }

        _filterDebounceTimer?.Stop();
        _vm.ClearFilter();
        FilterTextBox.Focus();
    }

    // PreviewKeyDown은 검색창(TextBox)이 키를 먼저 소비하기 전에 받는다.
    // 판정은 OverlayShortcutPolicy에 두고, None이면 텍스트 입력을 삼키지 않도록 Handled를 건드리지 않는다.
    private async void Window_PreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        try
        {
            var action = OverlayShortcutPolicy.Resolve(
                e.Key,
                Keyboard.Modifiers,
                FilterTextBox?.IsKeyboardFocusWithin == true,
                !string.IsNullOrEmpty(FilterTextBox?.Text),
                EventDetailsOverlay.Visibility == Visibility.Visible,
                DayEventsOverlay.Visibility == Visibility.Visible,
                miniBarMode: _displayMode == OverlayDisplayMode.MiniBar);

            if (action == OverlayShortcutAction.None)
                return;

            e.Handled = true;
            switch (action)
            {
                case OverlayShortcutAction.PreviousMonth:
                    _vm.MoveMonth(-1);
                    await ((App)WpfApplication.Current).RefreshAsync(false);
                    break;
                case OverlayShortcutAction.NextMonth:
                    _vm.MoveMonth(1);
                    await ((App)WpfApplication.Current).RefreshAsync(false);
                    break;
                case OverlayShortcutAction.GoToday:
                    _vm.GoToday();
                    await ((App)WpfApplication.Current).RefreshAsync(false);
                    break;
                case OverlayShortcutAction.Refresh:
                    await ((App)WpfApplication.Current).RefreshAsync(true);
                    break;
                case OverlayShortcutAction.ClearFilter:
                    ResetFilter();
                    break;
                case OverlayShortcutAction.CloseEventDetails:
                    HideEventDetails();
                    break;
                case OverlayShortcutAction.CloseDayList:
                    HideDayEvents();
                    break;
                case OverlayShortcutAction.HideWindow:
                    HideOverlay();
                    break;
            }
        }
        catch (Exception ex)
        {
            ReportHandlerFailure("ui.keyDown", ex);
        }
    }

    private async void ContextToday_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _vm.GoToday();
            await ((App)WpfApplication.Current).RefreshAsync(false);
        }
        catch (Exception ex)
        {
            ReportHandlerFailure("ui.contextToday", ex);
        }
    }

    private async void ContextRefresh_Click(object sender, RoutedEventArgs e) =>
        await RunGuardedAsync("ui.contextRefresh", () => ((App)WpfApplication.Current).RefreshAsync(true));

    private async void ContextSettings_Click(object sender, RoutedEventArgs e) =>
        await RunGuardedAsync("ui.contextSettings", () => ((App)WpfApplication.Current).OpenSettingsAsync());

    private void ContextHide_Click(object sender, RoutedEventArgs e) => HideOverlay();

    private async void ContextTopmost_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not WpfMenuItem item)
                return;

            Topmost = item.IsChecked;
            await ((App)WpfApplication.Current).SetAlwaysOnTopAsync(Topmost);
        }
        catch (Exception ex)
        {
            ReportHandlerFailure("ui.contextTopmost", ex);
        }
    }

    private void ContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfContextMenu menu)
            return;

        foreach (var item in menu.Items.OfType<WpfMenuItem>())
        {
            var tag = item.Tag as string;
            if (string.Equals(tag, "topmost", StringComparison.Ordinal))
                item.IsChecked = Topmost;
            else if (string.Equals(tag, "positionLock", StringComparison.Ordinal))
                item.IsChecked = _positionLocked;
            else if (string.Equals(tag, "calendars", StringComparison.Ordinal))
                PopulateCalendarVisibilityMenu(item);
        }
    }

    private void PopulateCalendarVisibilityMenu(WpfMenuItem menu)
    {
        menu.Items.Clear();
        var calendars = _vm.GetCalendarDescriptors();
        if (calendars.Count == 0)
        {
            menu.Items.Add(new WpfMenuItem
            {
                Header = "동기화 후 캘린더 목록이 표시됩니다.",
                IsEnabled = false
            });
            return;
        }

        var allVisible = calendars.All(c => _vm.IsCalendarVisible(c.Id));
        var showAll = new WpfMenuItem
        {
            Header = "모두 표시",
            IsCheckable = true,
            IsChecked = allVisible
        };
        showAll.Click += ContextShowAllCalendars_Click;
        menu.Items.Add(showAll);
        menu.Items.Add(new Separator());

        foreach (var calendar in calendars)
        {
            var item = new WpfMenuItem
            {
                Header = calendar.Name,
                ToolTip = calendar.Id,
                Tag = calendar.Id,
                IsCheckable = true,
                IsChecked = _vm.IsCalendarVisible(calendar.Id)
            };
            item.Click += ContextCalendarVisibility_Click;
            menu.Items.Add(item);
        }
    }

    private async void ContextPositionLock_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not WpfMenuItem item)
                return;

            _positionLocked = item.IsChecked;
            await ((App)WpfApplication.Current).SetPositionLockedAsync(_positionLocked);
        }
        catch (Exception ex)
        {
            ReportHandlerFailure("ui.contextPositionLock", ex);
        }
    }

    private async void ContextCalendarVisibility_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not WpfMenuItem { Tag: string calendarId } item)
                return;

            await ((App)WpfApplication.Current).SetCalendarVisibilityAsync(calendarId, item.IsChecked);
        }
        catch (Exception ex)
        {
            ReportHandlerFailure("ui.contextCalendarVisibility", ex);
        }
    }

    private async void ContextShowAllCalendars_Click(object sender, RoutedEventArgs e) =>
        await RunGuardedAsync("ui.contextShowAllCalendars", () => ((App)WpfApplication.Current).ShowAllCalendarsAsync());

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource?.AddHook(WndProc);
        Dispatcher.BeginInvoke(new Action(ClampToVisibleWorkArea));
    }

    private void SystemEvents_DisplaySettingsChanged(object? sender, EventArgs e) =>
        _ = Dispatcher.BeginInvoke(new Action(ClampToVisibleWorkArea));

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmDpiChanged = 0x02E0;
        const int WmMoving = 0x0216;
        if (msg == WmDpiChanged)
            _ = Dispatcher.BeginInvoke(new Action(ClampToVisibleWorkArea));
        else if (msg == WmMoving && _snapEnabled && !_positionLocked && !_transitionInProgress &&
                 _attached is null && lParam != IntPtr.Zero)
        {
            try
            {
                var r = Marshal.PtrToStructure<NativeRect>(lParam);
                var proposed = new PixelRect(r.Left, r.Top, r.Right, r.Bottom);
                // 배율은 모니터를 넘나들며 바뀔 수 있으므로 메시지마다 다시 읽는다.
                // OverlaySnap은 4변 동일 inset만 받으므로 가로·세로 배율이 다를 수 있어도 DpiScaleX 하나로 inset과 임계값을 환산한다.
                var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
                // 작업 영역은 현재 창이 아니라 제안 사각형이 걸친 모니터 기준이다.
                var work = GetWorkAreaPx(proposed);
                var snapped = OverlaySnap.Snap(
                    proposed,
                    OverlayLayoutMetrics.DipToPx(OverlayLayoutMetrics.WindowMarginDip, scale),
                    work,
                    OverlayLayoutMetrics.DipToPx(OverlayLayoutMetrics.SnapThresholdDip, scale));
                if (snapped != proposed)
                {
                    var native = new NativeRect
                    {
                        Left = snapped.Left,
                        Top = snapped.Top,
                        Right = snapped.Right,
                        Bottom = snapped.Bottom
                    };
                    Marshal.StructureToPtr(native, lParam, false);
                }

                handled = true;
                return (IntPtr)1;
            }
            catch (Exception ex)
            {
                // 스냅만 건너뛰고 이동은 막지 않는다.
                handled = false;
                ReportHandlerFailure("ui.snap", ex);
            }
        }

        return IntPtr.Zero;
    }

    private void ClampToVisibleWorkArea()
    {
        // 전환 중에는 창이 이미 한 번 바뀌므로 여기서 또 바꾸지 않는다.
        if (_transitionInProgress)
            return;

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || GetWindowRectPx() is not PixelRect rect)
            return;

        var screenWork = Forms.Screen.FromHandle(hwnd).WorkingArea;
        var work = new PixelRect(screenWork.Left, screenWork.Top, screenWork.Right, screenWork.Bottom);

        // 눈에 보이는 카드 기준으로 클램프한다. 스냅으로 붙은 창의 투명 여백은 작업 영역 밖이어도 밀지 않는다.
        var inset = OverlayLayoutMetrics.DipToPx(OverlayLayoutMetrics.WindowMarginDip, GetDpiScale());
        var clamped = OverlayLayoutPlanner.ClampWindowToWorkArea(rect, work, inset);
        if (clamped == rect)
            return;

        ApplyWindowRectPx(clamped);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private void StartClock()
    {
        UpdateClock();
        _clockTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick -= ClockTimer_Tick;
        _clockTimer.Tick += ClockTimer_Tick;
        _clockTimer.Start();
    }

    // 자정을 넘기면 오늘 배지를 옮기고, 이번 달을 보고 있었다면 새 달로 이동한 뒤 한 번 강제 동기화한다.
    private void ClockTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            UpdateClock();
            if (!_vm.RefreshTodayIfChanged())
            {
                if (_displayMode == OverlayDisplayMode.MiniBar && !_transitionInProgress)
                {
                    RefreshMiniAgenda(DateTimeOffset.Now);
                    UpdateTickerState();
                }

                return;
            }

            if (_transitionInProgress)
                FinishTransitionNow();

            ReloadTodayVisible();
            _ = ((App)WpfApplication.Current).RefreshAsync(true);
        }
        catch (Exception ex)
        {
            LogService.Warn("clock", "자정 전환 처리 실패", ex);
        }
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        TodayDateText.Text = now.ToString("yyyy년 M월 d일", KoreanCulture);
        TodayWeekdayText.Text = now.ToString("dddd", KoreanCulture);
        CurrentTimeText.Text = now.ToString("HH:mm:ss");
        MiniDateText.Text = now.ToString("M월 d일 ddd", KoreanCulture);
        MiniTimeText.Text = now.ToString("HH:mm:ss");
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_opacityUiReady || _applyingSettings)
            return;

        var opacity = Math.Clamp(e.NewValue, 0.0, 1.0);
        ApplySurfaceOpacity(opacity);
        UpdateOpacityLabel(opacity);
        ScheduleOpacitySave();
    }

    private void ApplySurfaceOpacity(double opacity)
    {
        opacity = Math.Clamp(opacity, 0.0, 1.0);

        var surfaceBrush = CreateOpacityBrush(0x1A, 0x1C, 0x21, opacity);

        // 배경은 RootChrome 한 겹에만 적용한다.
        // RootChrome과 내부 BackgroundLayer에 같은 반투명 브러시를 겹치면
        // 콘텐츠 영역만 두 번 칠해져 padding 영역보다 더 진하게 보인다.
        if (RootChrome is not null)
            RootChrome.Background = surfaceBrush;
        if (BackgroundLayer is not null)
        {
            BackgroundLayer.Opacity = 1.0;
            BackgroundLayer.Background = Brushes.Transparent;
        }

        // 텍스트는 선명하게 유지하고 입력 컨트롤의 배경만 바깥 배경과 같이 투명하게 만든다.
        if (FilterFieldCombo is not null)
            FilterFieldCombo.Background = CreateOpacityBrush(0x17, 0x1A, 0x20, opacity);
        if (FilterTextBox is not null)
            FilterTextBox.Background = CreateOpacityBrush(0x13, 0x16, 0x1B, opacity);
    }

    private static SolidColorBrush CreateOpacityBrush(byte r, byte g, byte b, double opacity)
    {
        var alpha = (byte)Math.Clamp((int)Math.Round(opacity * 230.0), 0, 230);
        var brush = new SolidColorBrush(Color.FromArgb(alpha, r, g, b));
        brush.Freeze();
        return brush;
    }

    private void UpdateOpacityLabel(double opacity)
    {
        if (OpacityPercentText is not null)
            OpacityPercentText.Text = $"{Math.Round(opacity * 100):0}%";
    }

    private void ScheduleOpacitySave()
    {
        _saveOpacityTimer ??= new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(450)
        };

        _saveOpacityTimer.Stop();
        _saveOpacityTimer.Tick -= SaveOpacityTimer_Tick;
        _saveOpacityTimer.Tick += SaveOpacityTimer_Tick;
        _saveOpacityTimer.Start();
    }

    private async void SaveOpacityTimer_Tick(object? sender, EventArgs e)
    {
        _saveOpacityTimer?.Stop();
        await RunGuardedAsync("ui.saveOpacity", () => ((App)WpfApplication.Current).SetUiOpacityAsync(OpacitySlider.Value));
    }

    private void ScheduleBoundsSave()
    {
        // 전환 중 위치는 전환이 끝날 때 최소화 상태와 함께 한 번에 저장한다.
        if (_suppressBoundsSave)
            return;

        _saveBoundsTimer ??= new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };

        _saveBoundsTimer.Stop();
        _saveBoundsTimer.Tick -= SaveBoundsTimer_Tick;
        _saveBoundsTimer.Tick += SaveBoundsTimer_Tick;
        _saveBoundsTimer.Start();
    }

    private async void SaveBoundsTimer_Tick(object? sender, EventArgs e)
    {
        _saveBoundsTimer?.Stop();
        await RunGuardedAsync("ui.saveBounds", () => ((App)WpfApplication.Current).SaveWindowBoundsAsync(Left, Top));
    }

    private static bool IsInteractiveSource(object? source)
    {
        DependencyObject? current = source as DependencyObject;
        if (current is null)
            return false;

        while (current is not null)
        {
            // Window 자체도 Control이므로 먼저 루트에서 탐색을 끝내야 한다.
            // 이전 구현은 Window까지 올라간 뒤 Control로 판정되어 사실상 모든 클릭에서 DragMove가 막혔다.
            if (current is Window)
                return false;

            // 실제 조작 컨트롤 위의 클릭은 해당 UI가 우선한다.
            if (current is System.Windows.Controls.Control)
                return true;

            if (current is FrameworkElement { Tag: string tag } && string.Equals(tag, "NoDrag", StringComparison.Ordinal))
                return true;

            // 날짜 셀 자체는 클릭 대상이므로 창 드래그를 시작하지 않는다.
            if (current is FrameworkElement { Tag: DateTime })
                return true;

            current = GetParent(current);
        }

        return false;
    }

    private static DependencyObject? GetParent(DependencyObject child)
    {
        if (child is ContentElement contentElement)
        {
            var parent = ContentOperations.GetParent(contentElement);
            if (parent is not null)
                return parent;

            if (contentElement is FrameworkContentElement frameworkContentElement)
                return frameworkContentElement.Parent;

            return null;
        }

        return child is Visual || child is System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(child)
            : LogicalTreeHelper.GetParent(child);
    }
}
