using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
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
    private HwndSource? _hwndSource;

    public MainWindow(AppSettings settings)
    {
        InitializeComponent();
        DataContext = _vm;
        ApplySettings(settings);
        _opacityUiReady = true;
        StartClock();

        Closing += (_, e) =>
        {
            if (_allowClose)
                return;

            e.Cancel = true;
            Hide();
        };

        SourceInitialized += MainWindow_SourceInitialized;
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
        Closed += (_, _) =>
        {
            _clockTimer?.Stop();
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
            _vm.SetConfiguredCalendars(settings.CalendarIds);
            _vm.SetKnownCalendarNames(settings.CalendarNames);
            _vm.SetHiddenCalendars(settings.HiddenCalendarIds);

            var opacity = Math.Clamp(settings.UiOpacity, 0.0, 1.0);
            Opacity = 1.0;
            ApplySurfaceOpacity(opacity);
            if (OpacitySlider is not null)
                OpacitySlider.Value = opacity;
            UpdateOpacityLabel(opacity);

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
        finally
        {
            _applyingSettings = false;
        }

        if (IsLoaded)
            Dispatcher.BeginInvoke(new Action(ClampToVisibleWorkArea));
    }

    public void SetHiddenCalendars(IEnumerable<string> hiddenCalendarIds) => _vm.SetHiddenCalendars(hiddenCalendarIds);

    public IReadOnlyList<CalendarDescriptor> GetCalendarDescriptors() => _vm.GetCalendarDescriptors();

    public void SetKnownCalendarNames(IReadOnlyDictionary<string, string>? names) => _vm.SetKnownCalendarNames(names);

    public bool IsCalendarVisible(string calendarId) => _vm.IsCalendarVisible(calendarId);

    public void SetPositionLocked(bool locked) => _positionLocked = locked;

    public void SetEvents(IEnumerable<DaouCalendarEvent> events, DateTimeOffset updatedAt, string status)
    {
        _vm.SetEvents(events);
        _vm.SetLastUpdated(updatedAt == default ? null : updatedAt);
        _vm.SetStatus(status, false);
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
        if (_positionLocked ||
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

    private void Hide_Click(object sender, RoutedEventArgs e) => Hide();

    private void DayCell_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: DateTime date } cell)
            return;

        ShowDayEvents(date);
        e.Handled = true;
    }

    private void ShowDayEvents(DateTime date)
    {
        var events = _vm.GetEventChipsForDate(date);
        DayEventsDateText.Text = date.ToString("M월 d일 dddd", KoreanCulture);
        DayEventsCountText.Text = events.Count == 0 ? "일정 없음" : $"일정 {events.Count}개";
        DayEventsItemsControl.ItemsSource = events;
        DayEventsItemsControl.Visibility = events.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        DayEventsEmptyText.Visibility = events.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DayEventsOverlay.Visibility = Visibility.Visible;
    }

    private void DayListEvent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { Tag: DaouCalendarEvent calendarEvent })
            return;

        DayEventsOverlay.Visibility = Visibility.Collapsed;
        ShowEventDetails(calendarEvent, returnToDayList: true);
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
                DayEventsOverlay.Visibility == Visibility.Visible);

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
                    Hide();
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

    private void ContextHide_Click(object sender, RoutedEventArgs e) => Hide();

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
        if (msg == WmDpiChanged)
            _ = Dispatcher.BeginInvoke(new Action(ClampToVisibleWorkArea));
        return IntPtr.Zero;
    }

    private void ClampToVisibleWorkArea()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect))
            return;

        var screen = Forms.Screen.FromHandle(hwnd);
        var work = screen.WorkingArea;
        var width = Math.Max(1, rect.Right - rect.Left);
        var height = Math.Max(1, rect.Bottom - rect.Top);

        var x = width >= work.Width
            ? work.Left
            : Math.Clamp(rect.Left, work.Left, work.Right - width);
        var y = height >= work.Height
            ? work.Top
            : Math.Clamp(rect.Top, work.Top, work.Bottom - height);

        if (x == rect.Left && y == rect.Top)
            return;

        SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, SwpNoZOrder | SwpNoActivate);
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
                return;

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
