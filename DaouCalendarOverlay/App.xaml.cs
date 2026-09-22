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
    private DispatcherTimer? _healthTimer;
    private Forms.NotifyIcon? _trayIcon;
    private bool _isExiting;

    public AppSettings Settings => _settings;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Chrome Native Messaging Host로 실행된 경우 GUI/싱글 인스턴스 로직을 타지 않는다.
            if (NativeMessagingHost.IsNativeInvocation(e.Args))
            {
                LogService.Initialize(LogService.DefaultLogDirectory, "host");
                await NativeMessagingHost.RunAsync();
                Shutdown();
                return;
            }

            LogService.Initialize(LogService.DefaultLogDirectory, "overlay");
            RegisterGlobalExceptionHandlers();
            LogService.Info("startup", $"overlay 시작 pid={Environment.ProcessId}");

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
                var loadedRefreshMinutes = _settings.RefreshMinutes;
                _settings.RefreshMinutes = SettingsValidation.ClampRefreshMinutes(loadedRefreshMinutes);
                if (loadedRefreshMinutes != _settings.RefreshMinutes)
                    LogService.Warn("settings", $"RefreshMinutes {loadedRefreshMinutes} → {_settings.RefreshMinutes} 으로 보정");
                _extensionInstaller.EnsureExtracted();
                _nativeMessagingRegistration.EnsureRegistered();

                if (!_settings.IsConfigured)
                {
                    var setup = new SettingsWindow(_settings, firstRun: true, BaseUrlPolicy.GetStartupNotice(_settings.BaseUrl));
                    if (setup.ShowDialog() != true)
                    {
                        await ExitApplicationAsync();
                        return;
                    }

                    _settings = setup.Result;
                    await SaveSettingsAsync();
                    _extensionInstaller.EnsureExtracted();
                    _nativeMessagingRegistration.EnsureRegistered();
                }

                _startupService.Apply(_settings.StartWithWindows);

                _overlayWindow = new MainWindow(_settings);
                _overlayWindow.Show();
                CreateTrayIcon();

                var cache = await _cacheService.LoadAsync();
                if (cache.Events.Count > 0)
                {
                    _overlayWindow.SetEvents(cache.Events, cache.LastUpdated, $"캐시 {cache.LastUpdated:HH:mm}");
                    _syncStatus.MarkCacheLoaded(cache.LastUpdated);
                    if (!CacheRangePolicy.CoversToday(cache.RangeFrom, cache.RangeTo, DateTimeOffset.Now))
                    {
                        _syncStatus.MarkCacheOutOfRange();
                        LogService.Info("cache", "캐시 범위가 오늘을 포함하지 않아 범위 밖으로 표기합니다.");
                    }
                }

                _bridgeServer.SyncCompleted += BridgeServer_SyncCompleted;
                _bridgeServer.BridgeHeartbeat += BridgeServer_BridgeHeartbeat;
                _bridgeServer.FetchIssued += BridgeServer_FetchIssued;
                _bridgeServer.ConfigurationInvalid += BridgeServer_ConfigurationInvalid;
                _bridgeServer.Start();

                ConfigureHealthTimer();
                _syncStatus.MarkWaiting();
                await RefreshAsync(true);
            }
            catch (Exception ex)
            {
                LogService.Error("startup", "앱 초기화 실패", ex);
                System.Windows.MessageBox.Show(
                    $"앱 초기화 중 오류가 발생했습니다.\n\n{ex.Message}\n\n설정 파일: %LOCALAPPDATA%\\DaouCalendarOverlay\\settings.json",
                    "Daou Calendar Overlay",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                await ExitApplicationAsync();
            }
        }
        catch (Exception ex)
        {
            LogService.Error("startup", "OnStartup 실패", ex);
            _syncStatus.MarkGeneralError($"시작 실패: {ex.Message}");
        }
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogService.Error("dispatcher", "처리되지 않은 UI 예외", e.Exception);
        _syncStatus.MarkGeneralError($"오류: {e.Exception.Message}");
        e.Handled = true;   // 로그 후 계속 실행
    }

    private void CurrentDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        LogService.Error("appdomain", $"처리되지 않은 예외 terminating={e.IsTerminating}", e.ExceptionObject as Exception);
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogService.Warn("task", "관찰되지 않은 Task 예외", e.Exception);
        e.SetObserved();
    }

    /// <summary>로그를 남기고 상태 텍스트로 사용자에게 알린다(앱은 계속 동작한다).</summary>
    public void ReportError(string category, string message, Exception? ex = null)
    {
        LogService.Error(category, message, ex);
        _syncStatus.MarkGeneralError(ex is null ? message : $"{message}: {ex.Message}");
    }

    /// <summary>설정 저장 실패를 치명적 오류로 만들지 않는다. 실패는 상태 표시줄과 로그로만 알린다.</summary>
    private async Task<bool> SaveSettingsAsync()
    {
        try
        {
            var saved = await _settingsService.SaveAsync(_settings);
            if (!saved)
                _syncStatus.MarkPersistenceError("설정");
            return saved;
        }
        catch (Exception ex)
        {
            LogService.Error("settings", "설정 저장 실패", ex);
            _syncStatus.MarkPersistenceError("설정");
            return false;
        }
    }

    /// <summary>트레이 메뉴에서 로그 디렉터리를 탐색기로 연다.</summary>
    public void OpenLogFolder()
    {
        try
        {
            var dir = LogService.LogDirectory ?? LogService.DefaultLogDirectory;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LogService.Warn("tray", "로그 폴더 열기 실패", ex);
            _syncStatus.MarkGeneralError($"로그 폴더 열기 실패: {ex.Message}");
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

    // 상태 텍스트의 UI 마샬링은 SyncStatus_StatusChanged가 처리하므로 여기서 Dispatcher를 쓰지 않는다.
    private void BridgeServer_ConfigurationInvalid(object? sender, string reason) =>
        _syncStatus.MarkConfigurationInvalid(NoFetchReasons.Describe(reason));

    private void BridgeServer_SyncCompleted(object? sender, BridgeSyncEventArgs e)
    {
        _ = Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                if (_overlayWindow is null)
                    return;

                if (e.Success)
                {
                    var now = DateTimeOffset.Now;
                    _overlayWindow.SetEvents(e.Events, now, $"동기화 {now:HH:mm}");
                    _overlayWindow.SetLoginRequired(false);
                    _syncStatus.MarkSuccess(now);
                    LogService.Info("sync", $"동기화 성공 events={e.Events.Count}");

                    try
                    {
                        var cacheSaved = await _cacheService.SaveAsync(new CalendarCache
                        {
                            LastUpdated = now,
                            RangeFrom = e.RangeFrom,
                            RangeTo = e.RangeTo,
                            Events = e.Events
                        });
                        if (!cacheSaved)
                            _syncStatus.MarkPersistenceError("캐시");
                    }
                    catch (Exception cacheEx)
                    {
                        // T1.2a 이후 SaveAsync 는 예외를 던지지 않지만 방어적으로 남겨 둔다.
                        LogService.Warn("cache", "캐시 저장 실패", cacheEx);
                    }

                    return;
                }

                if (e.AuthenticationRequired)
                {
                    LogService.Warn("sync", $"인증 실패: {e.Error}");
                    _overlayWindow.SetLoginRequired(true);
                    _syncStatus.MarkAuthenticationRequired(
                        string.IsNullOrWhiteSpace(e.Error) ? "DaouOffice 재로그인 필요" : e.Error);
                    return;
                }

                _overlayWindow.SetLoginRequired(false);
                switch (e.FailureKind)
                {
                    case BridgeFailureKind.Network:
                        LogService.Warn("sync", $"네트워크 실패: {e.Error}");
                        _syncStatus.MarkNetworkError(e.Error, e.RetryAt);
                        break;
                    case BridgeFailureKind.Extension:
                        LogService.Warn("sync", $"확장 오류: {e.Error}");
                        _syncStatus.MarkExtensionError(e.Error ?? "Chrome 확장 프로그램을 다시 로드해 주세요.");
                        break;
                    default:
                        LogService.Warn("sync", $"동기화 실패: {e.Error}");
                        _syncStatus.MarkGeneralError(e.Error ?? "동기화 실패", e.RetryAt);
                        break;
                }
            }
            catch (Exception ex)
            {
                ReportError("sync", "동기화 결과 처리 실패", ex);
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
        _settings.RefreshMinutes = SettingsValidation.ClampRefreshMinutes(_settings.RefreshMinutes);
        _settings.HiddenCalendarIds = _settings.HiddenCalendarIds
            .Where(id => _settings.CalendarIds.Contains(id, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        await SaveSettingsAsync();
        _startupService.Apply(_settings.StartWithWindows);
        _extensionInstaller.EnsureExtracted();
        _nativeMessagingRegistration.EnsureRegistered();
        _overlayWindow.ApplySettings(_settings);
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
        catch (Exception ex)
        {
            LogService.Warn("ui.openDaou", "DaouOffice 열기 실패", ex);
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
        await SaveSettingsAsync();
    }

    public async Task SetAlwaysOnTopAsync(bool enabled)
    {
        _settings.AlwaysOnTop = enabled;
        await SaveSettingsAsync();
        _overlayWindow?.ApplySettings(_settings);
    }

    public async Task SetPositionLockedAsync(bool locked)
    {
        _settings.PositionLocked = locked;
        await SaveSettingsAsync();
        _overlayWindow?.SetPositionLocked(locked);
    }

    public async Task SetUiOpacityAsync(double opacity)
    {
        _settings.UiOpacity = Math.Clamp(opacity, 0.0, 1.0);
        await SaveSettingsAsync();
    }

    public async Task SetCalendarVisibilityAsync(string calendarId, bool visible)
    {
        if (string.IsNullOrWhiteSpace(calendarId))
            return;

        if (visible)
            _settings.HiddenCalendarIds.RemoveAll(id => string.Equals(id, calendarId, StringComparison.Ordinal));
        else if (!_settings.HiddenCalendarIds.Contains(calendarId, StringComparer.Ordinal))
            _settings.HiddenCalendarIds.Add(calendarId);

        await SaveSettingsAsync();
        _overlayWindow?.SetHiddenCalendars(_settings.HiddenCalendarIds);
    }

    public async Task ShowAllCalendarsAsync()
    {
        _settings.HiddenCalendarIds.Clear();
        await SaveSettingsAsync();
        _overlayWindow?.SetHiddenCalendars(_settings.HiddenCalendarIds);
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
        catch (Exception ex)
        {
            // 단일 EXE 환경에서 아이콘 추출에 실패하면 기본 아이콘을 사용한다.
            LogService.Warn("tray", "트레이 아이콘 추출 실패", ex);
        }

        _trayIcon = new Forms.NotifyIcon
        {
            Visible = true,
            Text = "Daou Calendar Overlay",
            Icon = trayIcon
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("달력 보이기/숨기기", null, (_, _) => Dispatcher.Invoke(ToggleOverlay));
        menu.Items.Add("새로고침", null, (_, _) => Dispatcher.Invoke(async () =>
        {
            try { await RefreshAsync(true); }
            catch (Exception ex) { ReportError("tray.refresh", "새로고침 실패", ex); }
        }));
        menu.Items.Add("DaouOffice 열기", null, (_, _) => Dispatcher.Invoke(OpenDaouOffice));
        menu.Items.Add("Chrome 확장 폴더 열기", null, (_, _) => Dispatcher.Invoke(OpenChromeExtensionFolder));
        menu.Items.Add("로그 폴더 열기", null, (_, _) => Dispatcher.Invoke(OpenLogFolder));
        menu.Items.Add("설정", null, (_, _) => Dispatcher.Invoke(async () =>
        {
            try { await OpenSettingsAsync(); }
            catch (Exception ex) { ReportError("tray.settings", "설정 열기 실패", ex); }
        }));
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

    private void ExitApplication() => _ = ExitGuardedAsync();

    private async Task ExitGuardedAsync()
    {
        try
        {
            await ExitApplicationAsync();
        }
        catch (Exception ex)
        {
            LogService.Error("shutdown", "종료 처리 실패", ex);
            Shutdown();
        }
    }

    private async Task ExitApplicationAsync()
    {
        if (_isExiting)
            return;

        _isExiting = true;
        LogService.Info("shutdown", "overlay 종료");
        _healthTimer?.Stop();

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        _bridgeServer.SyncCompleted -= BridgeServer_SyncCompleted;
        _bridgeServer.BridgeHeartbeat -= BridgeServer_BridgeHeartbeat;
        _bridgeServer.FetchIssued -= BridgeServer_FetchIssued;
        _bridgeServer.ConfigurationInvalid -= BridgeServer_ConfigurationInvalid;
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
