namespace DaouCalendarOverlay.Services;

public enum OverlaySyncState
{
    Starting,
    WaitingForChrome,
    Connected,
    SyncRequested,
    Syncing,
    Synced,
    AuthenticationRequired,
    NetworkError,
    ExtensionError,
    GeneralError
}

public sealed class SyncStatusChangedEventArgs : EventArgs
{
    public OverlaySyncState State { get; init; }
    public string Text { get; init; } = "";
    public bool IsError { get; init; }
}

public sealed class SyncStatusService
{
    private readonly object _gate = new();
    private DateTimeOffset _lastHeartbeat;
    private DateTimeOffset _lastSuccess;
    private OverlaySyncState _state = OverlaySyncState.Starting;

    public event EventHandler<SyncStatusChangedEventArgs>? StatusChanged;

    public void MarkStarting() => Publish(OverlaySyncState.Starting, "시작 중…", false);

    public void MarkWaiting() => Publish(OverlaySyncState.WaitingForChrome, "Chrome 백그라운드 대기", false);

    public void MarkHeartbeat()
    {
        lock (_gate)
            _lastHeartbeat = DateTimeOffset.Now;

        if (_state is OverlaySyncState.Starting or OverlaySyncState.WaitingForChrome)
            Publish(OverlaySyncState.Connected, "Chrome 브리지 연결됨 · 동기화 대기", false);
    }

    public void MarkRequested() => Publish(OverlaySyncState.SyncRequested, "동기화 요청 중…", false);

    public void MarkSyncing() => Publish(OverlaySyncState.Syncing, "동기화 중…", false);

    public void MarkSuccess(DateTimeOffset when)
    {
        lock (_gate)
            _lastSuccess = when;
        Publish(OverlaySyncState.Synced, $"정상 · 동기화 {when:HH:mm}", false);
    }

    public void MarkAuthenticationRequired(string? message = null) =>
        Publish(OverlaySyncState.AuthenticationRequired,
            string.IsNullOrWhiteSpace(message) ? "DaouOffice 재로그인 필요" : message!, true);

    public void MarkNetworkError(string? message, DateTimeOffset? retryAt = null)
    {
        var suffix = retryAt is DateTimeOffset retry ? $" · 재시도 {retry:HH:mm}" : "";
        var text = string.IsNullOrWhiteSpace(message) ? "네트워크 오류" : message!;
        Publish(OverlaySyncState.NetworkError, text + suffix, true);
    }

    public void MarkExtensionError(string message) => Publish(OverlaySyncState.ExtensionError, message, true);

    public void MarkGeneralError(string message, DateTimeOffset? retryAt = null)
    {
        var suffix = retryAt is DateTimeOffset retry ? $" · 재시도 {retry:HH:mm}" : "";
        Publish(OverlaySyncState.GeneralError, message + suffix, true);
    }

    /// <summary>설정·캐시 등 로컬 파일 저장이 실패했을 때의 상태 문구를 만든다.</summary>
    public void MarkPersistenceError(string target) =>
        Publish(OverlaySyncState.GeneralError, $"{target} 저장 실패 · 로그 확인", true);

    public void EvaluateHealth()
    {
        DateTimeOffset heartbeat;
        DateTimeOffset lastSuccess;
        OverlaySyncState state;

        lock (_gate)
        {
            heartbeat = _lastHeartbeat;
            lastSuccess = _lastSuccess;
            state = _state;
        }

        if (state is OverlaySyncState.AuthenticationRequired or OverlaySyncState.ExtensionError)
            return;

        var now = DateTimeOffset.Now;
        if (heartbeat == default || now - heartbeat > TimeSpan.FromSeconds(95))
        {
            var suffix = lastSuccess == default ? "" : $" · 마지막 {lastSuccess:HH:mm}";
            Publish(OverlaySyncState.WaitingForChrome, "Chrome 확장 연결 대기" + suffix, false);
        }
    }

    private void Publish(OverlaySyncState state, string text, bool isError)
    {
        lock (_gate)
            _state = state;

        StatusChanged?.Invoke(this, new SyncStatusChangedEventArgs
        {
            State = state,
            Text = text,
            IsError = isError
        });
    }
}
