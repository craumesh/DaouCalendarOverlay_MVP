using System.Globalization;

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

    /// <summary>
    /// 확장이 버전을 보고하지 않았을 때 보고 버전 자리에 쓰는 표기.
    /// 7.0.0은 getConfig에 extensionVersion을 보내지 않은 마지막 버전이다.
    /// </summary>
    private const string UnreportedExtensionVersionText = "7.0.0 이하";

    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _now;
    private DateTimeOffset _lastHeartbeat;
    private DateTimeOffset _lastSuccess;
    private bool _cacheOutOfRange;
    private DateTimeOffset _cachedAt;
    private OverlaySyncState _state = OverlaySyncState.Starting;
    private string _configurationInvalidReason = "";
    private string _extensionMismatchKey = "";
    private readonly List<string> _startupIssues = new();

    public event EventHandler<SyncStatusChangedEventArgs>? StatusChanged;

    /// <param name="now">heartbeat 기록과 만료 판정에 쓰는 시계(테스트용). null이면 <see cref="DateTimeOffset.Now"/>.</param>
    public SyncStatusService(Func<DateTimeOffset>? now = null)
    {
        _now = now ?? (() => DateTimeOffset.Now);
    }

    public void MarkStarting() => Publish(OverlaySyncState.Starting, "시작 중…", false);

    public void MarkWaiting() => Publish(OverlaySyncState.WaitingForChrome, Decorate("Chrome 백그라운드 대기"), false);

    /// <summary>캐시가 오늘을 포함하지 않는 범위일 때 호출. 이후 대기/연결 대기 문구에 접미가 붙는다.</summary>
    public void MarkCacheOutOfRange()
    {
        lock (_gate)
            _cacheOutOfRange = true;
    }

    /// <summary>캐시를 읽어 화면에 표시했을 때 호출. 이후 대기/연결 대기 문구에 " · 캐시 MM-dd HH:mm"이 붙는다.</summary>
    public void MarkCacheLoaded(DateTimeOffset cachedAt)
    {
        if (cachedAt == default)
            return;

        OverlaySyncState state;
        lock (_gate)
        {
            _cachedAt = cachedAt;
            state = _state;
        }

        if (state == OverlaySyncState.WaitingForChrome)
            Publish(OverlaySyncState.WaitingForChrome, Decorate("Chrome 백그라운드 대기"), false);
        else if (state == OverlaySyncState.Connected)
            Publish(OverlaySyncState.Connected, Decorate("Chrome 브리지 연결됨 · 동기화 대기"), false);
    }

    public void MarkHeartbeat()
    {
        lock (_gate)
            _lastHeartbeat = _now();

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
            _cachedAt = default;
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
    /// 확장 버전이 EXE가 기대하는 버전과 다를 때의 상태 문구를 만든다.
    /// 확장이 30초마다 같은 버전을 보고하므로 동일 조합의 재발행은 억제한다(MarkConfigurationInvalid와 같은 방식).
    /// 확장이 버전을 보고하지 않았으면(null·빈 값) 보고 버전 자리에 "7.0.0 이하"를 쓴다.
    /// 재로그인 안내가 더 급하므로 AuthenticationRequired 상태는 덮어쓰지 않는다(아무것도 게시하지 않는다).
    /// </summary>
    public void MarkExtensionVersionMismatch(string? reportedVersion, string expectedVersion)
    {
        var reported = string.IsNullOrWhiteSpace(reportedVersion) ? UnreportedExtensionVersionText : reportedVersion!.Trim();
        var expected = (expectedVersion ?? "").Trim();
        var key = reported + "→" + expected;
        lock (_gate)
        {
            if (_state == OverlaySyncState.AuthenticationRequired)
                return;
            if (_state == OverlaySyncState.ExtensionError &&
                string.Equals(_extensionMismatchKey, key, StringComparison.Ordinal))
                return;
            _extensionMismatchKey = key;
        }

        MarkExtensionError($"Chrome 확장 새로고침 필요 ({reported} → {expected})");
    }

    /// <summary>
    /// 확장 버전이 다시 일치함을 확인했을 때 호출한다. 현재 상태가 ExtensionError일 때만
    /// 연결됨 상태로 되돌리고, 그 외 상태에서는 아무것도 게시하지 않는다(30초마다 불리므로).
    /// </summary>
    public void MarkExtensionVersionConfirmed()
    {
        OverlaySyncState state;
        lock (_gate)
            state = _state;

        if (state == OverlaySyncState.ExtensionError)
            Publish(OverlaySyncState.Connected, Decorate("Chrome 브리지 연결됨 · 동기화 대기"), false);
    }

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

    /// <summary>확장 추출·native host 등록처럼 기동 시 선택적 구성 요소가 실패했을 때. 오버레이는 계속 동작한다.</summary>
    /// <remarks>
    /// "{component} 실패: {reason} · 로그 확인"을 GeneralError로 게시하고, 이후 대기/연결 대기 문구 끝에
    /// " · {component} 실패" 접미를 붙인다. 같은 구성 요소는 접미에 한 번만 들어간다.
    /// </remarks>
    public void MarkStartupComponentError(string component, string reason)
    {
        if (string.IsNullOrWhiteSpace(component))
            return;

        var label = component + " 실패";
        lock (_gate)
        {
            if (!_startupIssues.Any(x => string.Equals(x, label, StringComparison.Ordinal)))
                _startupIssues.Add(label);
        }

        var detail = string.IsNullOrWhiteSpace(reason) ? "" : ": " + reason;
        Publish(OverlaySyncState.GeneralError, $"{label}{detail} · 로그 확인", true);
    }

    /// <summary>같은 구성 요소가 나중에 성공하면 접미에서 제거한다(새 상태를 게시하지 않는다).</summary>
    public void MarkStartupComponentResolved(string component)
    {
        if (string.IsNullOrWhiteSpace(component))
            return;

        var label = component + " 실패";
        lock (_gate)
            _startupIssues.RemoveAll(x => string.Equals(x, label, StringComparison.Ordinal));
    }

    /// <summary>
    /// health timer(20초)가 호출한다. heartbeat가 95초 넘게 없으면 "Chrome 확장 연결 대기"를 게시한다.
    /// AuthenticationRequired·ConfigurationInvalid는 덮어쓰지 않는다. ExtensionError는 heartbeat가 살아 있는 동안만
    /// 유지하고, 만료되면(불일치 표시 중 Chrome이 꺼진 경우) 연결 대기로 바꾼다.
    /// </summary>
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

        if (state is OverlaySyncState.AuthenticationRequired or OverlaySyncState.ConfigurationInvalid)
            return;

        var now = _now();
        if (heartbeat == default || now - heartbeat > TimeSpan.FromSeconds(95))
        {
            var suffix = lastSuccess == default ? "" : $" · 마지막 {lastSuccess:HH:mm}";
            Publish(OverlaySyncState.WaitingForChrome, Decorate("Chrome 확장 연결 대기" + suffix), false);
        }
    }

    /// <summary>
    /// 대기/연결 대기 문구에 붙는 접미를 한 곳에서 합성한다(범위 밖 → 캐시 시각 → 시작 이슈 순).
    /// 붙일 것이 없으면 입력 문자열을 그대로 돌려준다.
    /// </summary>
    private string Decorate(string text)
    {
        bool outOfRange;
        DateTimeOffset cachedAt;
        string[] issues;
        lock (_gate)
        {
            outOfRange = _cacheOutOfRange;
            cachedAt = _cachedAt;
            issues = _startupIssues.ToArray();
        }

        if (outOfRange)
            text += CacheOutOfRangeSuffix;
        if (cachedAt != default)
            text += " · 캐시 " + cachedAt.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture);
        foreach (var issue in issues)
            text += " · " + issue;
        return text;
    }

    private void Publish(OverlaySyncState state, string text, bool isError)
    {
        lock (_gate)
        {
            _state = state;
            if (state != OverlaySyncState.ConfigurationInvalid)
                _configurationInvalidReason = "";
            if (state != OverlaySyncState.ExtensionError)
                _extensionMismatchKey = "";
        }

        StatusChanged?.Invoke(this, new SyncStatusChangedEventArgs
        {
            State = state,
            Text = text,
            IsError = isError
        });
    }
}
