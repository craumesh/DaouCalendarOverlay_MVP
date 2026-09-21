using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using WpfApplication = System.Windows.Application;
using DaouCalendarOverlay.Models;
using DaouCalendarOverlay.Services;
using DaouCalendarOverlay.Views;
using Forms = System.Windows.Forms;

namespace DaouCalendarOverlay;

public partial class App : WpfApplication
{
    private readonly SettingsService _settingsService = new();
    private readonly CacheService _cacheService = new();
    private readonly StartupService _startupService = new();
    private readonly ChromeExtensionInstaller _extensionInstaller = new();
    private readonly NativeMessagingRegistrationService _nativeMessagingRegistration = new();
    private readonly CalendarBridgeServer _bridgeServer = new();
    private readonly SyncStatusService _syncStatus = new();

    private SingleInstanceService? _singleInstance;
    private AppSettings _settings = new();
    private MainWindow? _overlayWindow;
    private DispatcherTimer? _refreshTimer;
    private DispatcherTimer? _healthTimer;
    private Forms.NotifyIcon? _trayIcon;
    private bool _isExiting;

    public AppSettings Settings => _settings;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Chrome Native Messaging Host로 실행된 경우 GUI/싱글 인스턴스 로직을 타지 않는다.
        if (NativeMessagingHost.IsNativeInvocation(e.Args))
        {
            await NativeMessagingHost.RunAsync();
            Shutdown();
            return;
        }

        _singleInstance = new SingleInstanceService();
        if (!_singleInstance.TryAcquirePrimary())
        {
            await SingleInstanceService.NotifyPrimaryAsync();
            await _singleInstance.DisposeAsync();
            Shutdown();
            return;
        }

        _singleInstance.ActivateRequested += SingleInstance_ActivateRequested;
        _singleInstance.StartListening();
        _syncStatus.StatusChanged += SyncStatus_StatusChanged;
        _syncStatus.MarkStarting();

        try
        {
            _settings = await _settingsService.LoadAsync();
            _extensionInstaller.EnsureExtracted();
            _nativeMessagingRegistration.EnsureRegistered();

            if (!_settings.IsConfigured)
            {
                var setup = new SettingsWindow(_settings, firstRun: true);
                if (setup.ShowDialog() != true)
                {
                    await ExitApplicationAsync();
                    return;
                }

                _settings = setup.Result;
                await _settingsService.SaveAsync(_settings);
                _extensionInstaller.EnsureExtracted();
                _nativeMessagingRegistration.EnsureRegistered();
            }

            _startupService.Apply(_settings.StartWithWindows);

            _overlayWindow = new MainWindow(_settings);
            _overlayWindow.Show();
            CreateTrayIcon();

            var cache = await _cacheService.LoadAsync();
            if (cache.Events.Count > 0)
                _overlayWindow.SetEvents(cache.Events, cache.LastUpdated, $"캐시 {cache.LastUpdated:HH:mm}");

            _bridgeServer.SyncCompleted += BridgeServer_SyncCompleted;
            _bridgeServer.BridgeHeartbeat += BridgeServer_BridgeHeartbeat;
            _bridgeServer.FetchIssued += BridgeServer_FetchIssued;
            _bridgeServer.Start();

            ConfigureRefreshTimer();
            ConfigureHealthTimer();
            _syncStatus.MarkWaiting();
            await RefreshAsync(true);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"앱 초기화 중 오류가 발생했습니다.\n\n{ex.Message}\n\n설정 파일: %LOCALAPPDATA%\\DaouCalendarOverlay\\settings.json",
                "Daou Calendar Overlay",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            await ExitApplicationAsync();
        }
    }

    public Task RefreshAsync(bool force = false)
    {
        if (_overlayWindow is null)
            return Task.CompletedTask;

        var (from, to) = _overlayWindow.GetVisibleRange();
        var requested = _bridgeServer.UpdateRequest(_settings, from, to, force);
        if (requested)
            _syncStatus.MarkRequested();
        return Task.CompletedTask;
    }

    private void BridgeServer_BridgeHeartbeat(object? sender, EventArgs e) =>
        _syncStatus.MarkHeartbeat();

    private void BridgeServer_FetchIssued(object? sender, EventArgs e) =>
        _syncStatus.MarkSyncing();

    private void BridgeServer_SyncCompleted(object? sender, BridgeSyncEventArgs e)
    {
        _ = Dispatcher.InvokeAsync(async () =>
        {
            if (_overlayWindow is null)
                return;

            if (e.Success)
            {
                var now = DateTimeOffset.Now;
                _overlayWindow.SetEvents(e.Events, now, $"동기화 {now:HH:mm}");
                _overlayWindow.SetLoginRequired(false);
                _syncStatus.MarkSuccess(now);
                await _cacheService.SaveAsync(new CalendarCache
                {
                    LastUpdated = now,
                    Events = e.Events
                });
                return;
            }

            if (e.AuthenticationRequired)
            {
                _overlayWindow.SetLoginRequired(true);
                _syncStatus.MarkAuthenticationRequired(
                    string.IsNullOrWhiteSpace(e.Error) ? "DaouOffice 재로그인 필요" : e.Error);
                return;
            }

            _overlayWindow.SetLoginRequired(false);
            switch (e.FailureKind)
            {
                case BridgeFailureKind.Network:
                    _syncStatus.MarkNetworkError(e.Error, e.RetryAt);
                    break;
                case BridgeFailureKind.Extension:
                    _syncStatus.MarkExtensionError(e.Error ?? "Chrome 확장 프로그램을 다시 로드해 주세요.");
                    break;
                default:
                    _syncStatus.MarkGeneralError(e.Error ?? "동기화 실패", e.RetryAt);
                    break;
            }
        });
    }

    private void SyncStatus_StatusChanged(object? sender, SyncStatusChangedEventArgs e)
    {
        _ = Dispatcher.InvokeAsync(() => _overlayWindow?.SetStatus(e.Text, e.IsError));
    }

    public async Task OpenSettingsAsync()
    {
        if (_overlayWindow is null)
            return;

        var dialog = new SettingsWindow(_settings, firstRun: false)
        {
            Owner = _overlayWindow
        };

        if (dialog.ShowDialog() != true)
            return;

        _settings = dialog.Result;
        _settings.HiddenCalendarIds = _settings.HiddenCalendarIds
            .Where(id => _settings.CalendarIds.Contains(id, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        await _settingsService.SaveAsync(_settings);
        _startupService.Apply(_settings.StartWithWindows);
        _extensionInstaller.EnsureExtracted();
        _nativeMessagingRegistration.EnsureRegistered();
        _overlayWindow.ApplySettings(_settings);
        ConfigureRefreshTimer();
        await RefreshAsync(true);
    }

    public void OpenDaouOffice()
    {
        if (string.IsNullOrWhiteSpace(_settings.BaseUrl))
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _settings.BaseUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            _syncStatus.MarkGeneralError("DaouOffice 페이지를 열지 못했습니다.");
        }
    }

    public void OpenChromeExtensionFolder()
    {
        try
        {
            _extensionInstaller.OpenExtensionDirectory();
        }
        catch (Exception ex)
        {
            _syncStatus.MarkGeneralError($"확장 폴더 열기 실패: {ex.Message}");
        }
    }

    public void ToggleOverlay()
    {
        if (_overlayWindow is null)
            return;

        if (_overlayWindow.IsVisible)
            _overlayWindow.Hide();
        else
            ActivateOverlay();
    }

    public async Task SaveWindowBoundsAsync(double left, double top)
    {
        _settings.Left = left;
        _settings.Top = top;
        await _settingsService.SaveAsync(_settings);
    }

    public async Task SetAlwaysOnTopAsync(bool enabled)
    {
        _settings.AlwaysOnTop = enabled;
        await _settingsService.SaveAsync(_settings);
        _overlayWindow?.ApplySettings(_settings);
    }

    public async Task SetPositionLockedAsync(bool locked)
    {
        _settings.PositionLocked = locked;
        await _settingsService.SaveAsync(_settings);
        _overlayWindow?.SetPositionLocked(locked);
    }

    public async Task SetUiOpacityAsync(double opacity)
    {
        _settings.UiOpacity = Math.Clamp(opacity, 0.0, 1.0);
        await _settingsService.SaveAsync(_settings);
    }

    public async Task SetCalendarVisibilityAsync(string calendarId, bool visible)
    {
        if (string.IsNullOrWhiteSpace(calendarId))
            return;

        if (visible)
            _settings.HiddenCalendarIds.RemoveAll(id => string.Equals(id, calendarId, StringComparison.Ordinal));
        else if (!_settings.HiddenCalendarIds.Contains(calendarId, StringComparer.Ordinal))
            _settings.HiddenCalendarIds.Add(calendarId);

        await _settingsService.SaveAsync(_settings);
        _overlayWindow?.SetHiddenCalendars(_settings.HiddenCalendarIds);
    }

    public async Task ShowAllCalendarsAsync()
    {
        _settings.HiddenCalendarIds.Clear();
        await _settingsService.SaveAsync(_settings);
        _overlayWindow?.SetHiddenCalendars(_settings.HiddenCalendarIds);
    }

    private void ConfigureRefreshTimer()
    {
        _refreshTimer?.Stop();
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(Math.Max(1, _settings.RefreshMinutes))
        };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync(false);
        _refreshTimer.Start();
    }

    private void ConfigureHealthTimer()
    {
        _healthTimer?.Stop();
        _healthTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(20)
        };
        _healthTimer.Tick += (_, _) => _syncStatus.EvaluateHealth();
        _healthTimer.Start();
    }

    private void CreateTrayIcon()
    {
        var trayIcon = System.Drawing.SystemIcons.Application;
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(exePath))
            {
                var extracted = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (extracted is not null)
                    trayIcon = extracted;
            }
        }
        catch
        {
            // 단일 EXE 환경에서 아이콘 추출에 실패하면 기본 아이콘을 사용한다.
        }

        _trayIcon = new Forms.NotifyIcon
        {
            Visible = true,
            Text = "Daou Calendar Overlay",
            Icon = trayIcon
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("달력 보이기/숨기기", null, (_, _) => Dispatcher.Invoke(ToggleOverlay));
        menu.Items.Add("새로고침", null, (_, _) => Dispatcher.Invoke(async () => await RefreshAsync(true)));
        menu.Items.Add("DaouOffice 열기", null, (_, _) => Dispatcher.Invoke(OpenDaouOffice));
        menu.Items.Add("Chrome 확장 폴더 열기", null, (_, _) => Dispatcher.Invoke(OpenChromeExtensionFolder));
        menu.Items.Add("설정", null, (_, _) => Dispatcher.Invoke(async () => await OpenSettingsAsync()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => Dispatcher.Invoke(ExitApplication));
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ToggleOverlay);
    }

    private void SingleInstance_ActivateRequested(object? sender, EventArgs e) =>
        _ = Dispatcher.InvokeAsync(ActivateOverlay);

    private void ActivateOverlay()
    {
        if (_overlayWindow is null)
            return;

        if (!_overlayWindow.IsVisible)
            _overlayWindow.Show();

        if (_overlayWindow.WindowState == WindowState.Minimized)
            _overlayWindow.WindowState = WindowState.Normal;

        _overlayWindow.Activate();
    }

    private void ExitApplication() => _ = ExitApplicationAsync();

    private async Task ExitApplicationAsync()
    {
        if (_isExiting)
            return;

        _isExiting = true;
        _refreshTimer?.Stop();
        _healthTimer?.Stop();

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        _bridgeServer.SyncCompleted -= BridgeServer_SyncCompleted;
        _bridgeServer.BridgeHeartbeat -= BridgeServer_BridgeHeartbeat;
        _bridgeServer.FetchIssued -= BridgeServer_FetchIssued;
        await _bridgeServer.DisposeAsync();

        if (_singleInstance is not null)
        {
            _singleInstance.ActivateRequested -= SingleInstance_ActivateRequested;
            await _singleInstance.DisposeAsync();
        }

        _syncStatus.StatusChanged -= SyncStatus_StatusChanged;
        _overlayWindow?.AllowCloseAndClose();
        Shutdown();
    }
}
