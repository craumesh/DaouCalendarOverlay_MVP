using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using DaouCalendarOverlay.Models;

namespace DaouCalendarOverlay.Services;

public sealed class CalendarBridgeServer : IAsyncDisposable
{
    /// <summary>결과를 받아 DaouOffice 조회를 진행하는 동안 유지하는 lease 길이(초).</summary>
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

    /// <summary>테스트가 HTTP 전송을 대체하기 위한 확장점. null이면 기본 HttpClientHandler를 만든다.</summary>
    public Func<HttpMessageHandler>? HttpMessageHandlerFactory { get; set; }

    /// <param name="httpHandlerFactory">HTTP 전송을 대체할 핸들러 팩터리(테스트용). null이면 기본 핸들러를 만든다.</param>
    /// <param name="pipeName">파이프 이름 재정의(테스트용). null이면 운영 파이프 이름을 쓴다.</param>
    public CalendarBridgeServer(Func<HttpMessageHandler>? httpHandlerFactory = null, string? pipeName = null)
    {
        if (httpHandlerFactory is not null)
            HttpMessageHandlerFactory = httpHandlerFactory;

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
    /// 조회가 길어지는 동안에도 <c>getConfig</c>/<c>ping</c>이 새 연결을 잡을 수 있게 여유를 둔다.
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
            bytes = await NativeBridgeProtocol.ReadFrameAsync(pipe, 8 * 1024 * 1024, cancellationToken);
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
    /// 모든 분기가 즉시 반환한다. <c>postResult</c>의 DaouOffice 조회는 백그라운드 Task로 넘어가므로
    /// 파이프 응답이 HTTP 왕복에 묶이지 않는다.
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

                var config = BuildConfig();
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
            _leaseUntil = now.AddSeconds(45);
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
    /// 확장이 보낸 결과를 받아들이고 <b>즉시</b> 응답한다. 실제 DaouOffice 조회는 백그라운드 Task에서 수행한다.
    /// lease는 여기서 갱신만 하고 <c>_activeRequestId</c>는 유지하므로 조회 중에는 새 fetch가 발행되지 않는다.
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
                LogService.Warn("bridge", $"postResult 무시: requestId 불일치 (cookies={result.CookieCount}, source={result.CookieSource ?? "none"})");
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
            () => RunResultFetchAsync(result, requestId, settingsSnapshot, fromSnapshot, toSnapshot, token),
            CancellationToken.None);
        Track(task);

        return NativeBridgeResponse.Success();
    }

    /// <summary>백그라운드 조회 래퍼. 성공/실패/취소 어느 쪽이든 마지막에 lease와 활성 요청을 해제한다.</summary>
    private async Task RunResultFetchAsync(BridgeResultPayload result, string requestId, AppSettings settingsSnapshot,
        DateTimeOffset fromSnapshot, DateTimeOffset toSnapshot, CancellationToken cancellationToken)
    {
        try
        {
            await ProcessResultAsync(result, settingsSnapshot, fromSnapshot, toSnapshot, cancellationToken);
        }
        catch (Exception ex)
        {
            LogService.Error("bridge", "postResult 처리 실패", ex);
        }
        finally
        {
            ReleaseActiveRequest(requestId);
        }
    }

    /// <summary>
    /// 조회가 끝난 요청의 lease를 해제한다.
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

    private static HttpMessageHandler CreateDefaultHandler() => new HttpClientHandler
    {
        UseCookies = false,
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All
    };

    private async Task ProcessResultAsync(BridgeResultPayload result, AppSettings settingsSnapshot,
        DateTimeOffset fromSnapshot, DateTimeOffset toSnapshot, CancellationToken cancellationToken)
    {
        LogService.Info("bridge.result", BridgeLogSummary.DescribeResult(result));

        if (string.IsNullOrWhiteSpace(result.CookieHeader) || result.CookieCount <= 0)
        {
            var retryAt = RecordFailure(authenticationFailure: true);
            var message = string.IsNullOrWhiteSpace(result.Error)
                ? "Chrome에서 DaouOffice 로그인 쿠키를 찾지 못했습니다."
                : result.Error!;
            SyncCompleted?.Invoke(this, BridgeSyncEventArgs.AuthRequired(message, retryAt));
            return;
        }

        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            var retryAt = RecordFailure(authenticationFailure: false);
            SyncCompleted?.Invoke(this, BridgeSyncEventArgs.Fail(result.Error!, BridgeFailureKind.General, retryAt));
            return;
        }

        try
        {
            var requestUri = BuildCalendarUri(settingsSnapshot, fromSnapshot, toSnapshot);
            using var handler = HttpMessageHandlerFactory?.Invoke() ?? CreateDefaultHandler();
            using var client = new HttpClient(handler, disposeHandler: false)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.TryAddWithoutValidation("Cookie", result.CookieHeader);
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            request.Headers.TryAddWithoutValidation("Accept-Language", "ko-KR,ko;q=0.9,en;q=0.8");
            if (!string.IsNullOrWhiteSpace(result.UserAgent))
                request.Headers.TryAddWithoutValidation("User-Agent", result.UserAgent);

            if (Uri.TryCreate(settingsSnapshot.BaseUrl, UriKind.Absolute, out var baseUri))
                request.Headers.Referrer = new Uri(baseUri, "/gw/app/calendar");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var location = response.Headers.Location?.ToString() ?? "";

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                var retryAt = RecordFailure(authenticationFailure: true);
                SyncCompleted?.Invoke(this, BridgeSyncEventArgs.AuthRequired("Chrome의 DaouOffice 세션이 만료되었습니다.", retryAt));
                return;
            }

            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var retryAt = RecordFailure(authenticationFailure: true);
                SyncCompleted?.Invoke(this, BridgeSyncEventArgs.AuthRequired("DaouOffice가 로그인 페이지로 리디렉션했습니다.", retryAt));
                return;
            }

            var looksLikeHtml = contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase) ||
                                body.TrimStart().StartsWith("<", StringComparison.Ordinal);
            var redirectedToLogin = location.Contains("login", StringComparison.OrdinalIgnoreCase);

            if (looksLikeHtml || redirectedToLogin)
            {
                var retryAt = RecordFailure(authenticationFailure: true);
                SyncCompleted?.Invoke(this, BridgeSyncEventArgs.AuthRequired("Chrome의 DaouOffice 세션이 만료되었거나 로그인 페이지로 이동했습니다.", retryAt));
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                var retryAt = RecordFailure(authenticationFailure: false);
                SyncCompleted?.Invoke(this, BridgeSyncEventArgs.Fail($"DaouOffice HTTP {(int)response.StatusCode}", BridgeFailureKind.Api, retryAt));
                return;
            }

            var envelope = JsonSerializer.Deserialize<DaouApiEnvelope>(body, _jsonOptions);
            if (envelope is null)
                throw new JsonException("응답 JSON이 비어 있습니다.");

            if (!IsSuccessCode(envelope.Code))
            {
                var message = string.IsNullOrWhiteSpace(envelope.Message) ? "DaouOffice API 오류" : envelope.Message!;
                var retryAt = RecordFailure(authenticationFailure: false);
                SyncCompleted?.Invoke(this, BridgeSyncEventArgs.Fail(message, BridgeFailureKind.Api, retryAt));
                return;
            }

            if (DiscardIfRangeChanged(settingsSnapshot, fromSnapshot, toSnapshot))
            {
                LogService.Warn("bridge", "표시 범위 또는 설정이 변경되어 이전 결과를 버리고 즉시 재발행합니다.");
                return;
            }

            RecordSuccess(settingsSnapshot.RefreshMinutes);
            SyncCompleted?.Invoke(this, BridgeSyncEventArgs.Ok(envelope.Data, fromSnapshot, toSnapshot));
        }
        catch (HttpRequestException ex)
        {
            LogService.Warn("bridge.http", "DaouOffice 요청 네트워크 오류", ex);
            var retryAt = RecordFailure(authenticationFailure: false);
            SyncCompleted?.Invoke(this, BridgeSyncEventArgs.Fail($"네트워크 오류: {ex.Message}", BridgeFailureKind.Network, retryAt));
        }
        catch (TaskCanceledException ex)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                LogService.Info("bridge", "종료 중 HTTP 요청 취소");
                return;
            }

            LogService.Warn("bridge.http", "DaouOffice 요청 취소/시간 초과", ex);
            var retryAt = RecordFailure(authenticationFailure: false);
            var message = ex.InnerException is TimeoutException ? "DaouOffice 응답 시간 초과" : "네트워크 요청이 취소되었습니다.";
            SyncCompleted?.Invoke(this, BridgeSyncEventArgs.Fail(message, BridgeFailureKind.Network, retryAt));
        }
        catch (Exception ex)
        {
            LogService.Warn("bridge.http", "DaouOffice 세션 브리지 실패", ex);
            var retryAt = RecordFailure(authenticationFailure: false);
            SyncCompleted?.Invoke(this, BridgeSyncEventArgs.Fail($"DaouOffice 세션 브리지 실패: {ex.Message}", BridgeFailureKind.General, retryAt));
        }
    }

    /// <summary>
    /// HTTP 왕복 중 표시 범위 또는 설정(BaseUrl/캘린더 ID)이 바뀌었으면 결과를 버리고(true)
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

    private static Uri BuildCalendarUri(AppSettings settings, DateTimeOffset from, DateTimeOffset to)
    {
        var baseUri = new Uri(settings.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var endpoint = new Uri(baseUri, "gw/api/calendar/event");
        var query = new List<string>
        {
            $"timeMin={Uri.EscapeDataString(FormatDate(from))}",
            $"timeMax={Uri.EscapeDataString(FormatDate(to))}",
            "includingAttendees=true"
        };

        foreach (var calendarId in settings.CalendarIds)
            query.Add($"calendarIds%5B%5D={Uri.EscapeDataString(calendarId)}");

        var builder = new UriBuilder(endpoint)
        {
            Query = string.Join("&", query)
        };
        return builder.Uri;
    }

    private static bool IsSuccessCode(JsonElement code)
    {
        return code.ValueKind switch
        {
            JsonValueKind.String => string.Equals(code.GetString(), "200", StringComparison.Ordinal),
            JsonValueKind.Number => code.TryGetInt32(out var value) && value == 200,
            _ => false
        };
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture);

    /// <summary>
    /// 진행 중인 HTTP를 취소하고 짧은 경계(최대 700ms)까지만 기다린 뒤 돌아온다.
    /// 종료 클릭이 백그라운드 조회에 묶이지 않게 하기 위한 것이므로 무한 대기를 하지 않는다.
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

    public static BridgeConfigResponse NoFetch(string reason) =>
        new() { Ok = true, ShouldFetch = false, NoFetchReason = reason };
}

public sealed class BridgeResultPayload
{
    [JsonPropertyName("requestId")]
    public string RequestId { get; set; } = "";

    [JsonPropertyName("cookieHeader")]
    public string? CookieHeader { get; set; }

    [JsonPropertyName("cookieCount")]
    public int CookieCount { get; set; }

    [JsonPropertyName("cookieSource")]
    public string? CookieSource { get; set; }

    [JsonPropertyName("userAgent")]
    public string? UserAgent { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
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

    public static BridgeSyncEventArgs ExtensionFailure(string error) => new()
    {
        Error = error,
        FailureKind = BridgeFailureKind.Extension
    };

    public static BridgeSyncEventArgs Fail(string error, BridgeFailureKind kind = BridgeFailureKind.General, DateTimeOffset? retryAt = null) => new()
    {
        Error = error,
        FailureKind = kind,
        RetryAt = retryAt
    };
}
