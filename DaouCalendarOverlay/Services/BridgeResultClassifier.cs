using System.Globalization;
using System.Text;
using System.Text.Json;
using DaouCalendarOverlay.Models;

namespace DaouCalendarOverlay.Services;

/// <summary>worker가 보낸 fetch outcome 코드. 이 클래스에만 리터럴을 둔다.</summary>
public static class BridgeFetchOutcomes
{
    public const string Response = "response";
    public const string Timeout = "timeout";
    public const string Network = "network";
    public const string TooLarge = "tooLarge";
    public const string Error = "error";
}

/// <summary>
/// worker가 postResult <c>result.refreshState</c>로 보내는 토큰 갱신 결과 상태. 이 클래스에만 리터럴을 둔다.
/// 만료 401(ROUTE-0006)을 감지했을 때만 오며, 7.2.0 확장은 보내지 않는다.
/// </summary>
public static class BridgeRefreshStates
{
    /// <summary>갱신 POST가 2xx였다. 조회를 1회 다시 했다.</summary>
    public const string Refreshed = "refreshed";

    /// <summary>갱신 POST가 401 또는 403이었다(갱신 거절).</summary>
    public const string Rejected = "rejected";

    /// <summary>갱신 POST가 그 밖의 상태였거나 응답이 없었다(네트워크·타임아웃).</summary>
    public const string Failed = "failed";

    /// <summary>갱신에 쓸 세션이 저장소에 없어 갱신하지 않았다.</summary>
    public const string NoToken = "no_token";

    /// <summary>직전 시도 뒤 쿨다운(5분) 안이라 갱신하지 않았다.</summary>
    public const string Cooldown = "cooldown";

    /// <summary>직전 거절 뒤 쿨다운(30분) 안이라 갱신하지 않았다.</summary>
    public const string CooldownRejected = "cooldown_rejected";

    /// <summary>DaouOffice 탭이 열려 있어 페이지의 갱신을 기다렸다(갱신 대기).</summary>
    public const string WaitingTab = "waiting_tab";

    /// <summary>이번 동기화의 남은 시간 예산이 모자라 갱신을 다음 주기로 미뤘다(갱신 대기).</summary>
    public const string Budget = "budget";

    /// <summary>탭 조회 실패, 갱신 URL 검증 실패, 예외로 갱신 여부를 판단하지 못했다.</summary>
    public const string Unavailable = "unavailable";
}

/// <summary><see cref="BridgeResultClassifier.Classify"/>의 판정 결과.</summary>
public sealed class BridgeResultVerdict
{
    /// <summary>실패 종류. <see cref="BridgeFailureKind.None"/>이면 성공이다.</summary>
    public BridgeFailureKind Kind { get; init; }

    public bool IsSuccess => Kind == BridgeFailureKind.None;

    /// <summary>로그용 영문 코드(판정표의 ReasonCode).</summary>
    public string ReasonCode { get; init; } = "";

    /// <summary>상태 문구(한국어). 성공이면 빈 문자열이다.</summary>
    public string Message { get; init; } = "";

    /// <summary>로그용 API code. <c>[A-Za-z0-9_-]</c>만 최대 32자. 없으면 null.</summary>
    public string? ApiCode { get; init; }

    /// <summary>성공일 때만 채운다.</summary>
    public List<DaouCalendarEvent> Events { get; init; } = new();
}

/// <summary>
/// postResult의 전송 수준 사실을 인증 필요/네트워크/API/일반/성공으로 판정하는 순수 함수.
/// 예외를 던지지 않는다. 한국어 상태 문구는 여기 상수에서만 만든다.
/// 문구에는 응답 본문 원문을 넣지 않는다(API 오류의 envelope.message만 예외).
/// </summary>
public static class BridgeResultClassifier
{
    /// <summary>본문 최대 길이(UTF-16 단위). worker MAX_BODY_CHARS와 같아야 한다.</summary>
    public const int MaxBodyChars = 1_000_000;

    public const string AuthRequiredMessage = "Chrome의 DaouOffice 로그인이 필요합니다";
    public const string RedirectMessage = "DaouOffice가 로그인 페이지로 리디렉션했습니다.";
    public const string LoginPageMessage = "Chrome의 DaouOffice 세션이 만료되었거나 로그인 페이지로 이동했습니다.";
    public const string TimeoutMessage = "DaouOffice 응답 시간 초과";
    public const string NetworkMessage = "네트워크 오류: DaouOffice에 연결하지 못했습니다";
    public const string HttpErrorFormat = "DaouOffice HTTP {0}";
    public const string ApiErrorFallbackMessage = "DaouOffice API 오류";
    public const string EmptyBodyMessage = "DaouOffice 응답이 비어 있습니다";
    public const string InvalidJsonMessage = "DaouOffice 응답을 해석하지 못했습니다";
    public const string TooLargeFormat = "DaouOffice 응답이 너무 큽니다 ({0}자)";
    public const string ExtensionErrorFormat = "Chrome 확장 조회 오류: {0}";
    public const string UnknownOutcomeMessage = "Chrome 확장 결과 형식을 알 수 없습니다";
    public const string ProcessingFailedFormat = "동기화 결과 처리 실패: {0}";

    /// <summary>DaouOffice가 만료된 세션에 돌려주는 401 본문의 최상위 code. worker EXPIRED_API_CODE와 같아야 한다.</summary>
    public const string ExpiredApiCode = "ROUTE-0006";

    /// <summary>만료 401인데 확장이 갱신을 미뤘을 때(<see cref="BridgeRefreshStates.WaitingTab"/>, <see cref="BridgeRefreshStates.Budget"/>)의 문구.</summary>
    public const string SessionRefreshPendingMessage = "DaouOffice 세션 갱신을 기다리는 중입니다";

    private const int MaxErrorNameLength = 64;
    private const int MaxApiCodeLength = 32;
    private const string DefaultErrorName = "Error";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>판정표(설계 3.5절)를 위에서부터 첫 일치 순서로 적용한다.</summary>
    public static BridgeResultVerdict Classify(BridgeResultPayload? result)
    {
        // 0: 결과 없음
        if (result is null)
            return Failure(BridgeFailureKind.General, "unknown_outcome", UnknownOutcomeMessage);

        var outcome = result.Outcome;

        // 1: 시간 초과
        if (string.Equals(outcome, BridgeFetchOutcomes.Timeout, StringComparison.Ordinal))
            return Failure(BridgeFailureKind.Network, "timeout", TimeoutMessage);

        // 2: 네트워크 실패
        if (string.Equals(outcome, BridgeFetchOutcomes.Network, StringComparison.Ordinal))
            return Failure(BridgeFailureKind.Network, "network", NetworkMessage);

        // 3: worker가 본문을 싣지 않은 크기 초과
        if (string.Equals(outcome, BridgeFetchOutcomes.TooLarge, StringComparison.Ordinal))
            return Failure(BridgeFailureKind.Api, "too_large", FormatTooLarge(result.BodyLength ?? 0));

        // 4: worker 내부 오류(URL 생성·검증 실패 등)
        if (string.Equals(outcome, BridgeFetchOutcomes.Error, StringComparison.Ordinal))
            return Failure(BridgeFailureKind.General, "extension_error",
                string.Format(CultureInfo.InvariantCulture, ExtensionErrorFormat, SanitizeErrorName(result.ErrorName)));

        // 5: 알 수 없는 outcome(null, 빈 값 포함)
        if (!string.Equals(outcome, BridgeFetchOutcomes.Response, StringComparison.Ordinal))
            return Failure(BridgeFailureKind.General, "unknown_outcome", UnknownOutcomeMessage);

        // 보충 (a): response인데 status가 없으면 형식 오류다.
        if (result.Status is not int status)
            return Failure(BridgeFailureKind.General, "unknown_outcome", UnknownOutcomeMessage);

        var body = result.Body;

        // 6: 리디렉션(redirect: "manual"이면 opaqueredirect/status 0)
        if (result.Redirected ||
            string.Equals(result.ResponseType, "opaqueredirect", StringComparison.Ordinal) ||
            status is >= 300 and <= 399)
            return Failure(BridgeFailureKind.Authentication, "redirect", RedirectMessage);

        // 7: 인증 실패. 본문 최상위 code(로그아웃 ROUTE-0004, 만료 ROUTE-0006 등)를 로그용으로 추출한다.
        //    만료 401인데 확장이 갱신을 미뤘으면(waiting_tab, budget) 문구만 '세션 갱신 대기'로 바꾼다.
        //    refreshState가 없거나 그 밖의 값이면 7.2.0과 같은 판정이다.
        if (status is 401 or 403)
        {
            var authApiCode = TryReadTopLevelCode(body);
            if (status == 401 &&
                string.Equals(authApiCode, ExpiredApiCode, StringComparison.Ordinal) &&
                (string.Equals(result.RefreshState, BridgeRefreshStates.WaitingTab, StringComparison.Ordinal) ||
                 string.Equals(result.RefreshState, BridgeRefreshStates.Budget, StringComparison.Ordinal)))
                return Failure(BridgeFailureKind.Authentication, "session_refresh_pending", SessionRefreshPendingMessage, authApiCode);

            return Failure(BridgeFailureKind.Authentication,
                status == 401 ? "http_401" : "http_403",
                AuthRequiredMessage,
                authApiCode);
        }

        // 8: 본문 크기 초과
        if (body is not null && body.Length > MaxBodyChars)
            return Failure(BridgeFailureKind.Api, "too_large", FormatTooLarge(body.Length));

        // 9: 2xx가 아닌 status. HTML 규칙보다 먼저 본다(500 HTML 오류 페이지를 세션 만료로 오판하지 않는다).
        if (status is < 200 or > 299)
            return Failure(BridgeFailureKind.Api,
                "http_" + status.ToString(CultureInfo.InvariantCulture),
                string.Format(CultureInfo.InvariantCulture, HttpErrorFormat, status));

        // 10: 빈 본문
        if (string.IsNullOrWhiteSpace(body))
            return Failure(BridgeFailureKind.Api, "empty_body", EmptyBodyMessage);

        // 11: 2xx인데 HTML(로그인 페이지)
        if ((result.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) ?? false) ||
            body.TrimStart().StartsWith("<", StringComparison.Ordinal))
            return Failure(BridgeFailureKind.Authentication, "login_page", LoginPageMessage);

        // 12: envelope 역직렬화
        DaouApiEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<DaouApiEnvelope>(body, JsonOptions);
        }
        catch (JsonException)
        {
            envelope = null;
        }
        catch (NotSupportedException)
        {
            envelope = null;
        }
        catch (ArgumentException)
        {
            // 짝이 맞지 않는 서로게이트가 든 문자열은 UTF-8로 바꾸지 못해 ArgumentException이 난다.
            envelope = null;
        }

        if (envelope is null)
            return Failure(BridgeFailureKind.Api, "invalid_json", InvalidJsonMessage);

        // 13: 성공
        if (IsSuccessCode(envelope.Code))
        {
            return new BridgeResultVerdict
            {
                Kind = BridgeFailureKind.None,
                ReasonCode = "ok",
                Message = "",
                Events = envelope.Data ?? new()
            };
        }

        var apiCode = SanitizeApiCode(envelope.Code);

        // 14: 200 응답 안의 인증 필요 code(방어적 판정)
        if (IsAuthenticationCode(envelope.Code))
            return Failure(BridgeFailureKind.Authentication, "api_auth_code", AuthRequiredMessage, apiCode);

        // 15: 그 밖의 API 오류. envelope.message만 문구에 그대로 쓴다.
        var message = string.IsNullOrWhiteSpace(envelope.Message) ? ApiErrorFallbackMessage : envelope.Message!;
        return Failure(BridgeFailureKind.Api, "api_code", message, apiCode);
    }

    private static BridgeResultVerdict Failure(BridgeFailureKind kind, string reasonCode, string message, string? apiCode = null) => new()
    {
        Kind = kind,
        ReasonCode = reasonCode,
        Message = message,
        ApiCode = apiCode
    };

    private static string FormatTooLarge(int length) =>
        string.Format(CultureInfo.InvariantCulture, TooLargeFormat, length.ToString("N0", CultureInfo.InvariantCulture));

    private static bool IsSuccessCode(JsonElement code)
    {
        return code.ValueKind switch
        {
            JsonValueKind.String => string.Equals(code.GetString(), "200", StringComparison.Ordinal),
            JsonValueKind.Number => code.TryGetInt32(out var value) && value == 200,
            _ => false
        };
    }

    private static bool IsAuthenticationCode(JsonElement code)
    {
        return code.ValueKind switch
        {
            JsonValueKind.String => string.Equals(code.GetString(), "ROUTE-0004", StringComparison.Ordinal) ||
                                    string.Equals(code.GetString(), "401", StringComparison.Ordinal),
            JsonValueKind.Number => code.TryGetInt32(out var value) && value == 401,
            _ => false
        };
    }

    /// <summary>
    /// 인증 실패 응답 본문의 최상위 <c>code</c>(대소문자 구분)를 로그용으로 읽는다.
    /// 본문이 비었거나 너무 길거나 JSON 객체가 아니면 null이다. 파싱 실패는 무시한다.
    /// </summary>
    private static string? TryReadTopLevelCode(string? body)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Length > MaxBodyChars)
            return null;

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            return root.TryGetProperty("code", out var code) ? SanitizeApiCode(code) : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            // 짝이 맞지 않는 서로게이트가 든 문자열은 UTF-8로 바꾸지 못한다.
            return null;
        }
    }

    /// <summary>String이면 값, Number면 원문 표기를 쓰고 <c>[A-Za-z0-9_-]</c>만 남겨 32자로 자른다. 비면 null.</summary>
    private static string? SanitizeApiCode(JsonElement code)
    {
        var raw = code.ValueKind switch
        {
            JsonValueKind.String => code.GetString(),
            JsonValueKind.Number => code.GetRawText(),
            _ => null
        };

        var sanitized = Keep(raw, MaxApiCodeLength, static c => IsAsciiLetterOrDigit(c) || c is '_' or '-');
        return sanitized.Length == 0 ? null : sanitized;
    }

    /// <summary><c>[A-Za-z0-9_]</c>만 남기고 64자로 자른다. 비면 <c>"Error"</c>.</summary>
    private static string SanitizeErrorName(string? errorName)
    {
        var sanitized = Keep(errorName, MaxErrorNameLength, static c => IsAsciiLetterOrDigit(c) || c == '_');
        return sanitized.Length == 0 ? DefaultErrorName : sanitized;
    }

    private static bool IsAsciiLetterOrDigit(char c) =>
        c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';

    private static string Keep(string? value, int maxLength, Func<char, bool> allowed)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        var builder = new StringBuilder(Math.Min(value.Length, maxLength));
        foreach (var c in value)
        {
            if (!allowed(c))
                continue;

            builder.Append(c);
            if (builder.Length >= maxLength)
                break;
        }

        return builder.ToString();
    }
}
