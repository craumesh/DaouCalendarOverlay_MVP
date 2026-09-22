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
    GeneralError,
    ConfigurationInvalid
}

public sealed class SyncStatusChangedEventArgs : EventArgs
{
    public OverlaySyncState State { get; init; }
    public string Text { get; init; } = "";
    public bool IsError { get; init; }
}

public sealed class SyncStatusService
{
    private const string CacheOutOfRangeSuffix = " · 캐시(범위 밖)";

    private readonly object _gate = new();
    private DateTimeOffset _lastHeartbeat;
    private DateTimeOffset _lastSuccess;
    private bool _cacheOutOfRange;
    private OverlaySyncState _state = OverlaySyncState.Starting;
    private string _configurationInvalidReason = "";

    public event EventHandler<SyncStatusChangedEventArgs>? StatusChanged;

    public void MarkStarting() => Publish(OverlaySyncState.Starting, "시작 중…", false);

    public void MarkWaiting() => Publish(OverlaySyncState.WaitingForChrome, Decorate("Chrome 백그라운드 대기"), false);

    /// <summary>캐시가 오늘을 포함하지 않는 범위일 때 호출. 이후 대기/연결 대기 문구에 접미가 붙는다.</summary>
    public void MarkCacheOutOfRange()
    {
        lock (_gate)
            _cacheOutOfRange = true;
    }

    public void MarkHeartbeat()
    {
        lock (_gate)
            _lastHeartbeat = DateTimeOffset.Now;

        if (_state is OverlaySyncState.Starting or OverlaySyncState.WaitingForChrome)
            Publish(OverlaySyncState.Connected, Decorate("Chrome 브리지 연결됨 · 동기화 대기"), false);
    }

    public void MarkRequested() => Publish(OverlaySyncState.SyncRequested, "동기화 요청 중…", false);

    public void MarkSyncing() => Publish(OverlaySyncState.Syncing, "동기화 중…", false);

    public void MarkSuccess(DateTimeOffset when)
    {
        lock (_gate)
        {
            _lastSuccess = when;
            _cacheOutOfRange = false;
        }
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

    /// <summary>
    /// BaseUrl·캘린더 ID 등 설정 문제로 동기화가 불가능할 때의 상태.
    /// 확장이 30초마다 같은 사유를 다시 알려오므로 동일 사유의 재발행은 억제한다.
    /// </summary>
    public void MarkConfigurationInvalid(string reason)
    {
        lock (_gate)
        {
            if (_state == OverlaySyncState.ConfigurationInvalid &&
                string.Equals(_configurationInvalidReason, reason, StringComparison.Ordinal))
                return;
            _configurationInvalidReason = reason;
        }

        Publish(OverlaySyncState.ConfigurationInvalid, $"설정 오류: {reason}", true);
    }

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

        if (state is OverlaySyncState.AuthenticationRequired or OverlaySyncState.ExtensionError or OverlaySyncState.ConfigurationInvalid)
            return;

        var now = DateTimeOffset.Now;
        if (heartbeat == default || now - heartbeat > TimeSpan.FromSeconds(95))
        {
            var suffix = lastSuccess == default ? "" : $" · 마지막 {lastSuccess:HH:mm}";
            Publish(OverlaySyncState.WaitingForChrome, Decorate("Chrome 확장 연결 대기" + suffix), false);
        }
    }

    /// <summary>
    /// 대기/연결 대기 문구에 붙는 접미를 한 곳에서 합성한다.
    /// 붙일 것이 없으면 입력 문자열을 그대로 돌려준다.
    /// </summary>
    private string Decorate(string text)
    {
        bool outOfRange;
        lock (_gate)
            outOfRange = _cacheOutOfRange;
        return outOfRange ? text + CacheOutOfRangeSuffix : text;
    }

    private void Publish(OverlaySyncState state, string text, bool isError)
    {
        lock (_gate)
        {
            _state = state;
            if (state != OverlaySyncState.ConfigurationInvalid)
                _configurationInvalidReason = "";
        }

        StatusChanged?.Invoke(this, new SyncStatusChangedEventArgs
        {
            State = state,
            Text = text,
            IsError = isError
        });
    }
}
