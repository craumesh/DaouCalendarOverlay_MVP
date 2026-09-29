using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;
using DaouCalendarOverlay.Models;

namespace DaouCalendarOverlay.Services;

public sealed class CalendarBridgeServer : IAsyncDisposable
{
    /// <summary>
    /// getConfig에서 조회를 지시할 때 잡는 lease 길이(초). 확장이 DaouOffice 조회(시간 초과 포함)와
    /// postResult 전송을 이 안에 끝내야 한다. 확장의 fetch 시간 초과는 이 값보다 충분히 짧아야 한다.
    /// </summary>
    public const int FetchLeaseSeconds = 45;

    /// <summary>postResult를 받은 뒤 결과 처리를 진행하는 동안 유지하는 lease 길이(초).</summary>
    private const int ResultLeaseSeconds = 60;

    private readonly object _gate = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<Task, byte> _backgroundTasks = new();
    private readonly string _pipeName;
    private Task? _acceptLoop;

    private AppSettings _settings = new();
    private DateTimeOffset _from;
    private DateTimeOffset _to;
    private DateTimeOffset _nextAttemptAt;
    private DateTimeOffset _leaseUntil;
    private string _activeRequestId = "";
    private string _processingRequestId = "";
    private bool _forceRefresh = true;
    private int _consecutiveFailures;

    public event EventHandler<BridgeSyncEventArgs>? SyncCompleted;
    public event EventHandler? BridgeHeartbeat;
    public event EventHandler? FetchIssued;

    /// <summary>설정 문제로 fetch를 내보내지 못했을 때의 사유 코드. lock 밖에서만 발생한다.</summary>
    public event EventHandler<string>? ConfigurationInvalid;

    /// <summary>getConfig의 extensionVersion이 기대 버전과 일치할 때마다 발생한다. 불일치 상태 해제용.</summary>
    public event EventHandler? ExtensionVersionConfirmed;

    /// <summary>테스트가 결과 처리 시작을 붙잡기 위한 확장점. null이면 아무것도 하지 않는다. 운영 코드는 설정하지 않는다.</summary>
    public Func<CancellationToken, Task>? ResultProcessingHook { get; set; }

    /// <param name="pipeName">파이프 이름 재정의(테스트용). null이면 운영 파이프 이름을 쓴다.</param>
    public CalendarBridgeServer(string? pipeName = null)
    {
        _pipeName = string.IsNullOrWhiteSpace(pipeName) ? NativeBridgeProtocol.PipeName : pipeName!;
    }

    public void Start()
    {
        if (_acceptLoop is not null)
            return;

        _acceptLoop = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    public bool UpdateRequest(AppSettings settings, DateTimeOffset from, DateTimeOffset to, bool force)
    {
        lock (_gate)
        {
            var rangeChanged = _from != from || _to != to;
            var settingsChanged =
                !string.Equals(_settings.BaseUrl, settings.BaseUrl, StringComparison.OrdinalIgnoreCase) ||
                _settings.RefreshMinutes != settings.RefreshMinutes ||
                !_settings.CalendarIds.SequenceEqual(settings.CalendarIds, StringComparer.Ordinal);

            _settings = settings.Clone();
            _from = from;
            _to = to;

            var requested = force || rangeChanged || settingsChanged;
            if (requested)
            {
                _forceRefresh = true;
                _activeRequestId = "";
                _leaseUntil = DateTimeOffset.MinValue;
                _nextAttemptAt = DateTimeOffset.MinValue;
            }

            return requested;
        }
    }

    public void ForceRefresh()
    {
        lock (_gate)
        {
            _forceRefresh = true;
            _activeRequestId = "";
            _leaseUntil = DateTimeOffset.MinValue;
            _nextAttemptAt = DateTimeOffset.MinValue;
        }
    }

    /// <summary>
    /// 파이프 서버 인스턴스 수. accept 루프가 요청 처리와 분리돼 있어도 처리 중인 연결이 인스턴스를 차지하므로,
    /// 처리가 길어지는 동안에도 <c>getConfig</c>/<c>ping</c>이 새 연결을 잡을 수 있게 여유를 둔다.
    /// </summary>
    private const int PipeServerInstances = 4;

    /// <summary>
    /// 연결을 받는 일만 한다. 요청 처리는 별도 Task로 넘기므로 한 요청이 길어져도 다음 연결이 막히지 않는다.
    /// </summary>
    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    PipeServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await pipe.WaitForConnectionAsync(cancellationToken);

                var accepted = pipe;
                pipe = null;                      // 소유권 이전: 처리 Task가 dispose 한다
                Track(Task.Run(() => ServeClientAsync(accepted, cancellationToken), CancellationToken.None));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // 인스턴스(PipeServerInstances)가 모두 사용 중이면 생성이 IOException으로 실패한다.
                // 흔한 배압 상황이므로 로그가 불어나지 않게 Info로 남기고, 그 외 예외만 Warn으로 남긴다.
                if (ex is IOException)
                    LogService.Info("bridge.pipe", $"파이프 accept 대기: 인스턴스 사용 중 ({ex.Message})");
                else
                    LogService.Warn("bridge.pipe", "파이프 accept 실패", ex);

                if (cancellationToken.IsCancellationRequested)
                    break;

                try { await Task.Delay(250, cancellationToken); }
                catch (OperationCanceledException) { break; }
            }
            finally
            {
                if (pipe is not null)
                    await pipe.DisposeAsync();
            }
        }
    }

    /// <summary>연결 하나를 끝까지 처리하고 반드시 파이프를 정리한다. 처리 실패가 accept 루프를 멈추지 않게 한다.</summary>
    private async Task ServeClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        try
        {
            await HandlePipeClientAsync(pipe, cancellationToken);
        }
        catch (Exception ex)
        {
            LogService.Warn("bridge.pipe", "파이프 요청 처리 실패", ex);
        }
        finally
        {
            await pipe.DisposeAsync();
        }
    }

    private async Task HandlePipeClientAsync(Stream pipe, CancellationToken cancellationToken)
    {
        NativeBridgeResponse response;
        byte[]? bytes = null;
        var sw = Stopwatch.StartNew();

        try
        {
            bytes = await NativeBridgeProtocol.ReadFrameAsync(pipe, NativeHostRelay.PipeMessageLimit, cancellationToken);
            if (bytes is null)
            {
                response = NativeBridgeResponse.Fail("Empty native bridge request.");
            }
            else
            {
                var request = JsonSerializer.Deserialize<NativeBridgeRequest>(bytes, _jsonOptions);
                response = await HandleRequestAsync(request);
            }
        }
        catch (Exception ex)
        {
            LogService.Warn("bridge.pipe", "파이프 요청 처리 실패", ex);
            response = NativeBridgeResponse.Fail($"Native bridge request failed: {ex.Message}");
        }

        LogService.Info("bridge.pipe", $"{BridgeLogSummary.DescribeRequest(bytes)} elapsed={sw.ElapsedMilliseconds}ms ok={response.Ok}");

        var payload = JsonSerializer.SerializeToUtf8Bytes(response, _jsonOptions);
        await NativeBridgeProtocol.WriteFrameAsync(pipe, payload, cancellationToken);
    }

    /// <summary>파이프 요청 처리 진입점. 테스트에서 직접 호출한다.</summary>
    /// <remarks>
    /// 모든 분기가 즉시 반환한다. <c>postResult</c>의 결과 처리는 백그라운드 Task로 넘어가므로
    /// 파이프 응답이 결과 처리에 묶이지 않는다.
    /// </remarks>
    public Task<NativeBridgeResponse> HandleRequestAsync(NativeBridgeRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Type))
            return Task.FromResult(NativeBridgeResponse.Fail("Invalid native bridge request."));

        BridgeHeartbeat?.Invoke(this, EventArgs.Empty);

        switch (request.Type)
        {
            case "ping":
                return Task.FromResult(NativeBridgeResponse.Success());

            case "getConfig":
            {
                if (!string.IsNullOrWhiteSpace(request.LastError))
                {
                    var reported = request.LastError!.Length > 300
                        ? request.LastError!.Substring(0, 300)
                        : request.LastError!;
                    LogService.Warn("extension", $"확장 보고 오류 ver={request.ExtensionVersion ?? "?"}: {reported}");
                }

                var expectedExtensionVersion = ChromeExtensionInstaller.ExpectedExtensionVersion;
                if (ExtensionVersionGuard.IsMismatch(request.ExtensionVersion, expectedExtensionVersion))
                {
                    LogService.Warn("extension",
                        $"확장 버전 불일치 reported={request.ExtensionVersion ?? "(없음)"} expected={expectedExtensionVersion}");
                    SyncCompleted?.Invoke(this, BridgeSyncEventArgs.ExtensionFailure(request.ExtensionVersion, expectedExtensionVersion));
                }
                else
                {
                    ExtensionVersionConfirmed?.Invoke(this, EventArgs.Empty);
                }

                // protocol 게이트: 불일치면 BuildConfig를 부르지 않는다(requestId·lease를 발급하지 않는다).
                BridgeConfigResponse config;
                if (request.ProtocolVersion != NativeBridgeProtocol.ProtocolVersion)
                {
                    LogService.Warn("bridge", $"getConfig protocolVersion 불일치 reported={request.ProtocolVersion?.ToString(CultureInfo.InvariantCulture) ?? "(없음)"} expected={NativeBridgeProtocol.ProtocolVersion}");
                    config = BridgeConfigResponse.NoFetch(NoFetchReasons.ProtocolMismatch);
                }
                else
                {
                    config = BuildConfig();
                }

                if (config.ShouldFetch)
                {
                    LogService.Info("bridge", $"fetch 발행 requestId={config.RequestId}");
                    FetchIssued?.Invoke(this, EventArgs.Empty);
                }
                else if (NoFetchReasons.IsConfigurationProblem(config.NoFetchReason))
                {
                    LogService.Warn("bridge", $"getConfig noFetch reason={config.NoFetchReason}");
                    ConfigurationInvalid?.Invoke(this, config.NoFetchReason!);
                }

                return Task.FromResult(NativeBridgeResponse.Success(config));
            }

            case "postResult":
                // 구버전 확장의 결과(쿠키 필드 형식 포함)는 lease와 상태를 건드리지 않고 거부한다.
                if (request.ProtocolVersion != NativeBridgeProtocol.ProtocolVersion)
                {
                    LogService.Warn("bridge", $"postResult 거부: protocolVersion={request.ProtocolVersion?.ToString(CultureInfo.InvariantCulture) ?? "(없음)"} expected={NativeBridgeProtocol.ProtocolVersion}");
                    return Task.FromResult(NativeBridgeResponse.Fail("Unsupported protocolVersion."));
                }
                if (request.Result is null)
                    return Task.FromResult(NativeBridgeResponse.Fail("Missing bridge result payload."));
                return Task.FromResult(AcceptResult(request.Result));

            default:
                return Task.FromResult(NativeBridgeResponse.Fail($"Unknown native bridge request: {request.Type}"));
        }
    }

    private BridgeConfigResponse BuildConfig()
    {
        lock (_gate)
        {
            if (!_settings.IsConfigured)
                return BridgeConfigResponse.NoFetch(
                    BaseUrlPolicy.Validate(_settings.BaseUrl).IsValid
                        ? NoFetchReasons.NotConfigured
                        : NoFetchReasons.InvalidBaseUrl);

            if (_from == default || _to == default)
                return BridgeConfigResponse.NoFetch(NoFetchReasons.RangeNotReady);

            var validation = BaseUrlPolicy.Validate(_settings.BaseUrl);
            if (!validation.IsValid)
                return BridgeConfigResponse.NoFetch(NoFetchReasons.InvalidBaseUrl);

            var now = DateTimeOffset.Now;
            var leaseActive = !string.IsNullOrEmpty(_activeRequestId) && now < _leaseUntil;
            if (leaseActive)
                return BridgeConfigResponse.NoFetch(NoFetchReasons.LeaseActive);

            var due = _forceRefresh || _nextAttemptAt == default || now >= _nextAttemptAt;
            if (!due)
                return BridgeConfigResponse.NoFetch(_consecutiveFailures > 0 ? NoFetchReasons.Backoff : NoFetchReasons.NotDue);

            _activeRequestId = Guid.NewGuid().ToString("N");
            _leaseUntil = now.AddSeconds(FetchLeaseSeconds);
            _forceRefresh = false;

            return new BridgeConfigResponse
            {
                Ok = true,
                ShouldFetch = true,
                RequestId = _activeRequestId,
                BaseUrl = validation.NormalizedBaseUrl!,
                TimeMin = FormatDate(_from),
                TimeMax = FormatDate(_to),
                IncludingAttendees = true,
                CalendarIds = new List<string>(_settings.CalendarIds)
            };
        }
    }

    /// <summary>
    /// 확장이 보낸 결과를 받아들이고 <b>즉시</b> 응답한다. 결과 처리(판정과 반영)는 백그라운드 Task에서 수행한다.
    /// lease는 여기서 갱신만 하고 <c>_activeRequestId</c>는 유지하므로 결과 처리 중에는 새 fetch가 발행되지 않는다.
    /// </summary>
    private NativeBridgeResponse AcceptResult(BridgeResultPayload result)
    {
        string requestId;
        AppSettings settingsSnapshot;
        DateTimeOffset fromSnapshot;
        DateTimeOffset toSnapshot;

        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(result.RequestId) ||
                !string.Equals(result.RequestId, _activeRequestId, StringComparison.Ordinal))
            {
                LogService.Warn("bridge", $"postResult 무시: requestId 불일치 (outcome={BridgeLogSummary.Token(result.Outcome)})");
                return NativeBridgeResponse.Success();
            }

            if (string.Equals(_processingRequestId, result.RequestId, StringComparison.Ordinal))
            {
                LogService.Warn("bridge", "postResult 무시: 같은 requestId가 이미 처리 중");
                return NativeBridgeResponse.Success();
            }

            requestId = _activeRequestId;
            _processingRequestId = requestId;
            settingsSnapshot = _settings.Clone();
            fromSnapshot = _from;
            toSnapshot = _to;
            _leaseUntil = DateTimeOffset.Now.AddSeconds(ResultLeaseSeconds);
        }

        // 종료 진행 중이면(Cancel()은 Dispose()보다 먼저 호출된다) _cts.Token을 읽지 않는다.
        // 늦게 도착한 postResult가 이미 dispose된 CTS를 읽어 ObjectDisposedException을 내는 경로를 없앤다.
        if (_cts.IsCancellationRequested)
        {
            ReleaseActiveRequest(requestId);
            return NativeBridgeResponse.Success();
        }

        var token = _cts.Token;
        var task = Task.Run(
            () => RunResultProcessingAsync(result, requestId, settingsSnapshot, fromSnapshot, toSnapshot, token),
            CancellationToken.None);
        Track(task);

        return NativeBridgeResponse.Success();
    }

    /// <summary>
    /// 백그라운드 결과 처리 래퍼. 성공/실패/취소 어느 쪽이든 마지막에 lease와 활성 요청을 해제한다.
    /// 종료 취소는 로그만 남기고, 그 밖의 예외는 일반 실패로 알린다(예외 메시지 원문은 문구에 넣지 않는다).
    /// </summary>
    private async Task RunResultProcessingAsync(BridgeResultPayload result, string requestId, AppSettings settingsSnapshot,
        DateTimeOffset fromSnapshot, DateTimeOffset toSnapshot, CancellationToken cancellationToken)
    {
        try
        {
            await ProcessResultAsync(result, settingsSnapshot, fromSnapshot, toSnapshot, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogService.Info("bridge", "종료 중 결과 처리 취소");
        }
        catch (Exception ex)
        {
            LogService.Error("bridge", "postResult 처리 실패", ex);
            var retryAt = RecordFailure(authenticationFailure: false);
            var message = string.Format(CultureInfo.InvariantCulture, BridgeResultClassifier.ProcessingFailedFormat, ex.GetType().Name);
            try
            {
                SyncCompleted?.Invoke(this, BridgeSyncEventArgs.Fail(message, BridgeFailureKind.General, retryAt));
            }
            catch (Exception notifyException)
            {
                // 실패 알림 구독자가 던져도 백그라운드 Task가 faulted로 끝나지 않게 한다.
                LogService.Error("bridge", "postResult 처리 실패 알림 실패", notifyException);
            }
        }
        finally
        {
            ReleaseActiveRequest(requestId);
        }
    }

    /// <summary>
    /// 결과 처리가 끝난 요청의 lease를 해제한다.
    /// 그 사이 <c>UpdateRequest</c>/<c>ForceRefresh</c>/<c>DiscardIfRangeChanged</c>가 새 요청을 만들었으면 건드리지 않는다.
    /// </summary>
    private void ReleaseActiveRequest(string requestId)
    {
        lock (_gate)
        {
            if (string.Equals(_processingRequestId, requestId, StringComparison.Ordinal))
                _processingRequestId = "";

            if (!string.Equals(_activeRequestId, requestId, StringComparison.Ordinal))
                return;

            _activeRequestId = "";
            _leaseUntil = DateTimeOffset.MinValue;
        }
    }

    /// <summary>종료 시 짧게 기다릴 수 있도록 백그라운드 Task를 추적한다.</summary>
    private void Track(Task task)
    {
        _backgroundTasks[task] = 0;
        _ = task.ContinueWith(
            t => _backgroundTasks.TryRemove(t, out _),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task ProcessResultAsync(BridgeResultPayload result, AppSettings settingsSnapshot,
        DateTimeOffset fromSnapshot, DateTimeOffset toSnapshot, CancellationToken cancellationToken)
    {
        LogService.Info("bridge.result", BridgeLogSummary.DescribeResult(result));

        var hook = ResultProcessingHook;
        if (hook is not null)
            await hook(cancellationToken);

        var verdict = BridgeResultClassifier.Classify(result);
        LogVerdict(result.RequestId, verdict);

        if (!verdict.IsSuccess)
        {
            var authentication = verdict.Kind == BridgeFailureKind.Authentication;
            var retryAt = RecordFailure(authenticationFailure: authentication);
            SyncCompleted?.Invoke(this, authentication
                ? BridgeSyncEventArgs.AuthRequired(verdict.Message, retryAt)
                : BridgeSyncEventArgs.Fail(verdict.Message, verdict.Kind, retryAt));
            return;
        }

        if (DiscardIfRangeChanged(settingsSnapshot, fromSnapshot, toSnapshot))
        {
            LogService.Warn("bridge", "표시 범위 또는 설정이 변경되어 이전 결과를 버리고 즉시 재발행합니다.");
            return;
        }

        RecordSuccess(settingsSnapshot.RefreshMinutes);
        SyncCompleted?.Invoke(this, BridgeSyncEventArgs.Ok(verdict.Events, fromSnapshot, toSnapshot));
    }

    /// <summary>
    /// 판정 결과를 <c>bridge.result</c>에 한 줄로 남긴다. 성공은 Info, 실패는 Warn이다.
    /// 영문 코드(ReasonCode)와 정제된 ApiCode만 쓰고 상태 문구와 본문은 쓰지 않는다.
    /// </summary>
    private void LogVerdict(string requestId, BridgeResultVerdict verdict)
    {
        var name = verdict.Kind switch
        {
            BridgeFailureKind.None => "success",
            BridgeFailureKind.Authentication => "auth",
            BridgeFailureKind.Network => "network",
            BridgeFailureKind.Api => "api",
            _ => "general"
        };

        var line = $"requestId={requestId} verdict={name} reason={verdict.ReasonCode}";
        if (!string.IsNullOrEmpty(verdict.ApiCode))
            line += $" apiCode={verdict.ApiCode}";

        if (verdict.IsSuccess)
            LogService.Info("bridge.result", line);
        else
            LogService.Warn("bridge.result", line);
    }

    /// <summary>
    /// 결과 처리 중 표시 범위 또는 설정(BaseUrl/캘린더 ID)이 바뀌었으면 결과를 버리고(true)
    /// 즉시 재발행되도록 lease/backoff 상태를 초기화한다.
    /// 범위는 같지만 설정창 저장으로 BaseUrl/캘린더 ID만 바뀐 경우도 폐기 대상이다.
    /// 성공/실패 카운터(<c>_consecutiveFailures</c>)는 건드리지 않는다.
    /// </summary>
    private bool DiscardIfRangeChanged(AppSettings settingsSnapshot, DateTimeOffset fromSnapshot, DateTimeOffset toSnapshot)
    {
        lock (_gate)
        {
            var rangeStale = BridgeRangeGuard.IsStale(fromSnapshot, toSnapshot, _from, _to);
            var settingsStale = BridgeRangeGuard.IsSettingsStale(
                settingsSnapshot.BaseUrl, settingsSnapshot.CalendarIds,
                _settings.BaseUrl, _settings.CalendarIds);

            if (!rangeStale && !settingsStale)
                return false;

            _forceRefresh = true;
            _activeRequestId = "";
            _leaseUntil = DateTimeOffset.MinValue;
            _nextAttemptAt = DateTimeOffset.MinValue;
            return true;
        }
    }

    private void RecordSuccess(int refreshMinutes)
    {
        lock (_gate)
        {
            _consecutiveFailures = 0;
            _nextAttemptAt = DateTimeOffset.Now.AddMinutes(Math.Max(1, refreshMinutes));
        }
    }

    private DateTimeOffset RecordFailure(bool authenticationFailure)
    {
        lock (_gate)
        {
            _consecutiveFailures = Math.Min(_consecutiveFailures + 1, 8);
            var delay = authenticationFailure
                ? TimeSpan.FromMinutes(1)
                : TimeSpan.FromSeconds(Math.Min(600, 30 * Math.Pow(2, Math.Min(_consecutiveFailures - 1, 4))));
            _nextAttemptAt = DateTimeOffset.Now.Add(delay);
            return _nextAttemptAt;
        }
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture);

    /// <summary>
    /// 진행 중인 결과 처리를 취소하고 짧은 경계(최대 700ms)까지만 기다린 뒤 돌아온다.
    /// 종료 클릭이 백그라운드 결과 처리에 묶이지 않게 하기 위한 것이므로 무한 대기를 하지 않는다.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();

        if (_acceptLoop is not null)
        {
            try { await _acceptLoop.WaitAsync(TimeSpan.FromMilliseconds(500)); }
            catch (Exception ex) { LogService.Warn("bridge", "accept 루프 종료 대기 초과/오류", ex); }
        }

        var pending = _backgroundTasks.Keys.ToArray();
        if (pending.Length > 0)
        {
            try { await Task.WhenAll(pending).WaitAsync(TimeSpan.FromMilliseconds(200)); }
            catch (Exception ex) { LogService.Warn("bridge", "백그라운드 작업 종료 대기 초과/오류", ex); }
        }

        try { _cts.Dispose(); }
        catch (Exception ex) { LogService.Warn("bridge", "CTS dispose 실패", ex); }
    }
}

public sealed class BridgeConfigResponse
{
    public bool Ok { get; init; } = true;
    public bool ShouldFetch { get; init; }
    public string RequestId { get; init; } = "";
    public string BaseUrl { get; init; } = "";
    public string TimeMin { get; init; } = "";
    public string TimeMax { get; init; } = "";
    public bool IncludingAttendees { get; init; }
    public List<string> CalendarIds { get; init; } = new();

    /// <summary><see cref="ShouldFetch"/>가 false인 모든 응답에 실리는 사유 코드(<see cref="NoFetchReasons"/>).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NoFetchReason { get; init; }

    /// <summary>
    /// 앱의 브리지 프로토콜 버전. NoFetch를 포함한 모든 응답에 실린다.
    /// 확장은 이 값이 자신의 버전과 다르거나 없으면(구버전 앱) 조회하지 않는다.
    /// </summary>
    [JsonPropertyName("protocolVersion")]
    public int ProtocolVersion { get; init; } = NativeBridgeProtocol.ProtocolVersion;

    public static BridgeConfigResponse NoFetch(string reason) =>
        new() { Ok = true, ShouldFetch = false, NoFetchReason = reason };
}

/// <summary>
/// postResult의 <c>result</c>. 확장이 DaouOffice를 직접 조회한 전송 수준 사실(outcome, status, 본문 원문 등)이다.
/// 의미 판정은 <see cref="BridgeResultClassifier"/>가 한다. 본문은 로그에 남기지 않는다.
/// </summary>
public sealed class BridgeResultPayload
{
    [JsonPropertyName("requestId")]    public string RequestId { get; set; } = "";
    [JsonPropertyName("outcome")]      public string? Outcome { get; set; }
    [JsonPropertyName("status")]       public int? Status { get; set; }
    [JsonPropertyName("responseType")] public string? ResponseType { get; set; }
    [JsonPropertyName("redirected")]   public bool Redirected { get; set; }
    [JsonPropertyName("contentType")]  public string? ContentType { get; set; }
    [JsonPropertyName("body")]         public string? Body { get; set; }
    [JsonPropertyName("bodyLength")]   public int? BodyLength { get; set; }
    [JsonPropertyName("elapsedMs")]    public int? ElapsedMs { get; set; }
    [JsonPropertyName("errorName")]    public string? ErrorName { get; set; }
}

public enum BridgeFailureKind
{
    None,
    Authentication,
    Network,
    Extension,
    Api,
    General
}

public sealed class BridgeSyncEventArgs : EventArgs
{
    public bool Success { get; init; }
    public bool AuthenticationRequired { get; init; }
    public string? Error { get; init; }
    public BridgeFailureKind FailureKind { get; init; }
    public DateTimeOffset? RetryAt { get; init; }
    public List<DaouCalendarEvent> Events { get; init; } = new();

    /// <summary>이 결과가 요청된 표시 범위의 시작(<c>CalendarGrid.GetVisibleRange</c> 결과).</summary>
    public DateTimeOffset RangeFrom { get; init; }

    /// <summary>이 결과가 요청된 표시 범위의 끝.</summary>
    public DateTimeOffset RangeTo { get; init; }

    /// <summary>확장이 보고한 버전(보고하지 않았으면 null). FailureKind=Extension일 때만 의미가 있다.</summary>
    public string? ExtensionVersion { get; init; }

    /// <summary>EXE에 임베드된 기대 확장 버전.</summary>
    public string? ExpectedExtensionVersion { get; init; }

    public static BridgeSyncEventArgs Ok(List<DaouCalendarEvent> events, DateTimeOffset rangeFrom, DateTimeOffset rangeTo) => new()
    {
        Success = true,
        Events = events,
        RangeFrom = rangeFrom,
        RangeTo = rangeTo
    };

    public static BridgeSyncEventArgs AuthRequired(string error, DateTimeOffset? retryAt = null) => new()
    {
        AuthenticationRequired = true,
        Error = error,
        FailureKind = BridgeFailureKind.Authentication,
        RetryAt = retryAt
    };

    /// <summary>확장 버전이 EXE의 기대 버전과 다를 때. 문구는 SyncStatusService가 만든다.</summary>
    public static BridgeSyncEventArgs ExtensionFailure(string? reportedVersion, string expectedVersion) => new()
    {
        FailureKind = BridgeFailureKind.Extension,
        ExtensionVersion = reportedVersion,
        ExpectedExtensionVersion = expectedVersion
    };

    public static BridgeSyncEventArgs Fail(string error, BridgeFailureKind kind = BridgeFailureKind.General, DateTimeOffset? retryAt = null) => new()
    {
        Error = error,
        FailureKind = kind,
        RetryAt = retryAt
    };
}
