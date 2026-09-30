using System.Reflection;
using System.Text.Json;
using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

/// <summary>
/// <see cref="BridgeResultClassifier.Classify"/> 판정표(설계 3.5절)를 행마다 고정하는 표 테스트.
/// 각 케이스는 이름으로 <see cref="Cases"/>에서 꺼낸다(페이로드를 xUnit 직렬화 대상으로 두지 않기 위함).
/// 실패 케이스의 본문에는 일정 제목·이메일을 넣어, 문구와 코드에 본문이 새지 않는지도 함께 본다.
/// </summary>
public sealed class BridgeResultClassifierTests
{
    private const string SecretTitle = "SECRET-TITLE";
    private const string SecretEmail = "someone@example.com";

    /// <summary>본문에 섞어 넣는 비밀·개인정보 조각(JSON 객체 멤버 형태).</summary>
    private const string SecretMembers = "\"summary\":\"" + SecretTitle + "\",\"email\":\"" + SecretEmail + "\"";

    private const string OkEventJson =
        "{\"id\":\"1\",\"calendarId\":\"12345\",\"calendarName\":\"내 캘린더\",\"summary\":\"테스트\",\"timeType\":\"timed\"," +
        "\"startTime\":\"2026-09-22T10:00:00+09:00\",\"endTime\":\"2026-09-22T11:00:00+09:00\"}";

    private const string SecretHtml = "<html><body>" + SecretTitle + " " + SecretEmail + "</body></html>";

    private sealed record Case(
        BridgeResultPayload? Payload,
        BridgeFailureKind Kind,
        string ReasonCode,
        string Message,
        string? ApiCode = null);

    private static BridgeResultPayload Response(int? status, string? body, string? contentType = "application/json;charset=UTF-8",
        string? responseType = "basic", bool redirected = false) => new()
    {
        RequestId = "r1",
        Outcome = BridgeFetchOutcomes.Response,
        Status = status,
        ResponseType = responseType,
        Redirected = redirected,
        ContentType = contentType,
        Body = body,
        BodyLength = body?.Length,
        ElapsedMs = 10
    };

    private static BridgeResultPayload Outcome(string? outcome, string? errorName = null, int? status = null, int? bodyLength = null) => new()
    {
        RequestId = "r1",
        Outcome = outcome,
        Status = status,
        BodyLength = bodyLength,
        ErrorName = errorName,
        ElapsedMs = 25003
    };

    private static string Http(int status) => string.Format(System.Globalization.CultureInfo.InvariantCulture, BridgeResultClassifier.HttpErrorFormat, status);

    private static readonly Dictionary<string, Case> Cases = new(StringComparer.Ordinal)
    {
        // 0: result null
        ["r00_null"] = new(null, BridgeFailureKind.General, "unknown_outcome", BridgeResultClassifier.UnknownOutcomeMessage),

        // 1~2: 전송 실패
        ["r01_timeout"] = new(Outcome(BridgeFetchOutcomes.Timeout, "AbortError"), BridgeFailureKind.Network, "timeout", BridgeResultClassifier.TimeoutMessage),
        ["r02_network"] = new(Outcome(BridgeFetchOutcomes.Network, "TypeError"), BridgeFailureKind.Network, "network", BridgeResultClassifier.NetworkMessage),

        // 3: tooLarge(N0 포맷)
        ["r03_tooLarge"] = new(Outcome(BridgeFetchOutcomes.TooLarge, status: 200, bodyLength: 1200000),
            BridgeFailureKind.Api, "too_large", "DaouOffice 응답이 너무 큽니다 (1,200,000자)"),
        ["r03_tooLarge_noLength"] = new(Outcome(BridgeFetchOutcomes.TooLarge, status: 200),
            BridgeFailureKind.Api, "too_large", "DaouOffice 응답이 너무 큽니다 (0자)"),

        // 4: 확장 오류(errorName 정제)
        ["r04_error_sanitized"] = new(Outcome(BridgeFetchOutcomes.Error, "Type Error<script>"),
            BridgeFailureKind.General, "extension_error", "Chrome 확장 조회 오류: TypeErrorscript"),
        ["r04_error_invalidUrl"] = new(Outcome(BridgeFetchOutcomes.Error, "InvalidUrl"),
            BridgeFailureKind.General, "extension_error", "Chrome 확장 조회 오류: InvalidUrl"),
        ["r04_error_null"] = new(Outcome(BridgeFetchOutcomes.Error),
            BridgeFailureKind.General, "extension_error", "Chrome 확장 조회 오류: Error"),
        ["r04_error_onlyDisallowed"] = new(Outcome(BridgeFetchOutcomes.Error, "오류 <>@.-"),
            BridgeFailureKind.General, "extension_error", "Chrome 확장 조회 오류: Error"),
        ["r04_error_long"] = new(Outcome(BridgeFetchOutcomes.Error, new string('A', 100)),
            BridgeFailureKind.General, "extension_error", "Chrome 확장 조회 오류: " + new string('A', 64)),

        // 5: outcome이 response가 아님
        ["r05_outcomeNull"] = new(Outcome(null), BridgeFailureKind.General, "unknown_outcome", BridgeResultClassifier.UnknownOutcomeMessage),
        ["r05_outcomeEmpty"] = new(Outcome(""), BridgeFailureKind.General, "unknown_outcome", BridgeResultClassifier.UnknownOutcomeMessage),
        ["r05_outcomeWeird"] = new(Outcome("weird"), BridgeFailureKind.General, "unknown_outcome", BridgeResultClassifier.UnknownOutcomeMessage),
        ["r05_outcomeCaseMismatch"] = new(Outcome("Timeout"), BridgeFailureKind.General, "unknown_outcome", BridgeResultClassifier.UnknownOutcomeMessage),

        // 보충 (a): response인데 status 없음
        ["a_responseWithoutStatus"] = new(Response(null, "{" + SecretMembers + "}"),
            BridgeFailureKind.General, "unknown_outcome", BridgeResultClassifier.UnknownOutcomeMessage),

        // 6: 리디렉션
        ["r06_opaqueredirect"] = new(Response(0, "", contentType: "", responseType: "opaqueredirect", redirected: true),
            BridgeFailureKind.Authentication, "redirect", BridgeResultClassifier.RedirectMessage),
        ["r06_opaqueredirectTypeOnly"] = new(Response(0, "", contentType: "", responseType: "opaqueredirect"),
            BridgeFailureKind.Authentication, "redirect", BridgeResultClassifier.RedirectMessage),
        ["r06_302"] = new(Response(302, SecretHtml, contentType: "text/html"),
            BridgeFailureKind.Authentication, "redirect", BridgeResultClassifier.RedirectMessage),
        ["r06_redirectedFlag200"] = new(Response(200, "{\"code\":200,\"data\":[]}", redirected: true),
            BridgeFailureKind.Authentication, "redirect", BridgeResultClassifier.RedirectMessage),

        // 7: 401/403 (ApiCode는 본문 JSON 최상위 code)
        ["r07_401_route0004"] = new(Response(401, "{\"code\":\"ROUTE-0004\",\"message\":\"x\"}", contentType: "application/json"),
            BridgeFailureKind.Authentication, "http_401", BridgeResultClassifier.AuthRequiredMessage, "ROUTE-0004"),
        ["r07_401_numberCode"] = new(Response(401, "{\"code\":401," + SecretMembers + "}"),
            BridgeFailureKind.Authentication, "http_401", BridgeResultClassifier.AuthRequiredMessage, "401"),
        ["r07_401_codeSanitized"] = new(Response(401, "{\"code\":\"ab c<d>/e_f-" + new string('9', 40) + "\"}"),
            BridgeFailureKind.Authentication, "http_401", BridgeResultClassifier.AuthRequiredMessage, "abcde_f-" + new string('9', 24)),
        ["r07_401_codeCaseSensitive"] = new(Response(401, "{\"Code\":\"ROUTE-0004\"}"),
            BridgeFailureKind.Authentication, "http_401", BridgeResultClassifier.AuthRequiredMessage),
        ["r07_401_codeNotStringOrNumber"] = new(Response(401, "{\"code\":{\"x\":1}}"),
            BridgeFailureKind.Authentication, "http_401", BridgeResultClassifier.AuthRequiredMessage),
        ["r07_401_arrayRoot"] = new(Response(401, "[{\"code\":\"ROUTE-0004\"}]"),
            BridgeFailureKind.Authentication, "http_401", BridgeResultClassifier.AuthRequiredMessage),
        ["r07_401_invalidJson"] = new(Response(401, "{not json " + SecretTitle),
            BridgeFailureKind.Authentication, "http_401", BridgeResultClassifier.AuthRequiredMessage),
        ["r07_401_loneSurrogate"] = new(Response(401, "{\"code\":\"\uD800\"}"),
            BridgeFailureKind.Authentication, "http_401", BridgeResultClassifier.AuthRequiredMessage),
        ["r07_401_emptyBody"] = new(Response(401, ""),
            BridgeFailureKind.Authentication, "http_401", BridgeResultClassifier.AuthRequiredMessage),
        ["r07_401_tooLongBody"] = new(Response(401, "{\"code\":\"ROUTE-0004\",\"pad\":\"" + new string('x', BridgeResultClassifier.MaxBodyChars) + "\"}"),
            BridgeFailureKind.Authentication, "http_401", BridgeResultClassifier.AuthRequiredMessage),
        ["r07_403_html"] = new(Response(403, SecretHtml, contentType: "text/html"),
            BridgeFailureKind.Authentication, "http_403", BridgeResultClassifier.AuthRequiredMessage),

        // 8: 본문 길이 초과
        ["r08_bodyTooLong"] = new(Response(200, new string('a', BridgeResultClassifier.MaxBodyChars + 1)),
            BridgeFailureKind.Api, "too_large", "DaouOffice 응답이 너무 큽니다 (1,000,001자)"),

        // 9: 2xx가 아닌 status(HTML 규칙보다 먼저)
        ["r09_500json"] = new(Response(500, "{\"code\":500,\"message\":\"" + SecretTitle + "\"}"),
            BridgeFailureKind.Api, "http_500", Http(500)),
        ["r09_500html"] = new(Response(500, SecretHtml, contentType: "text/html; charset=UTF-8"),
            BridgeFailureKind.Api, "http_500", Http(500)),
        ["r09_404empty"] = new(Response(404, ""),
            BridgeFailureKind.Api, "http_404", Http(404)),
        ["r09_199"] = new(Response(199, "{" + SecretMembers + "}"),
            BridgeFailureKind.Api, "http_199", Http(199)),

        // 10: 빈 본문
        ["r10_204empty"] = new(Response(204, ""), BridgeFailureKind.Api, "empty_body", BridgeResultClassifier.EmptyBodyMessage),
        ["r10_200null"] = new(Response(200, null), BridgeFailureKind.Api, "empty_body", BridgeResultClassifier.EmptyBodyMessage),
        ["r10_200whitespace"] = new(Response(200, "  \r\n\t "), BridgeFailureKind.Api, "empty_body", BridgeResultClassifier.EmptyBodyMessage),

        // 11: 2xx HTML
        ["r11_200textHtml"] = new(Response(200, SecretHtml, contentType: "text/html; charset=UTF-8"),
            BridgeFailureKind.Authentication, "login_page", BridgeResultClassifier.LoginPageMessage),
        ["r11_200textHtmlUpper"] = new(Response(200, "{\"code\":200," + SecretMembers + "}", contentType: "TEXT/HTML"),
            BridgeFailureKind.Authentication, "login_page", BridgeResultClassifier.LoginPageMessage),
        ["r11_200jsonButHtmlBody"] = new(Response(200, "  \n<html>" + SecretTitle + "</html>"),
            BridgeFailureKind.Authentication, "login_page", BridgeResultClassifier.LoginPageMessage),

        // 12: JSON 해석 실패
        ["r12_invalidJson"] = new(Response(200, "{not json " + SecretTitle + " " + SecretEmail),
            BridgeFailureKind.Api, "invalid_json", BridgeResultClassifier.InvalidJsonMessage),
        ["r12_jsonNull"] = new(Response(200, "null"),
            BridgeFailureKind.Api, "invalid_json", BridgeResultClassifier.InvalidJsonMessage),
        ["r12_jsonArray"] = new(Response(200, "[" + "{" + SecretMembers + "}]"),
            BridgeFailureKind.Api, "invalid_json", BridgeResultClassifier.InvalidJsonMessage),
        ["r12_wrongEventType"] = new(Response(200, "{\"code\":200,\"data\":[{\"id\":\"1\",\"startTime\":\"" + SecretTitle + "\"," + SecretMembers + "}]}"),
            BridgeFailureKind.Api, "invalid_json", BridgeResultClassifier.InvalidJsonMessage),
        ["r12_loneSurrogate"] = new(Response(200, "{\"code\":200,\"message\":\"\uD800\"}"),
            BridgeFailureKind.Api, "invalid_json", BridgeResultClassifier.InvalidJsonMessage),

        // 13: 성공
        ["r13_successNumberCode"] = new(Response(200, "{\"code\":200,\"data\":[" + OkEventJson + "]}"),
            BridgeFailureKind.None, "ok", ""),
        ["r13_successStringCode"] = new(Response(200, "{\"code\":\"200\",\"data\":[" + OkEventJson + "]}"),
            BridgeFailureKind.None, "ok", ""),
        ["r13_successDataNull"] = new(Response(200, "{\"code\":200,\"data\":null}"),
            BridgeFailureKind.None, "ok", ""),
        ["r13_successDataMissing"] = new(Response(200, "{\"code\":200}"),
            BridgeFailureKind.None, "ok", ""),

        // 14: 200 안의 인증 필요 code
        ["r14_route0004"] = new(Response(200, "{\"code\":\"ROUTE-0004\",\"message\":\"" + SecretTitle + "\"}"),
            BridgeFailureKind.Authentication, "api_auth_code", BridgeResultClassifier.AuthRequiredMessage, "ROUTE-0004"),
        ["r14_code401String"] = new(Response(200, "{\"code\":\"401\"," + SecretMembers + "}"),
            BridgeFailureKind.Authentication, "api_auth_code", BridgeResultClassifier.AuthRequiredMessage, "401"),
        ["r14_code401Number"] = new(Response(200, "{\"code\":401," + SecretMembers + "}"),
            BridgeFailureKind.Authentication, "api_auth_code", BridgeResultClassifier.AuthRequiredMessage, "401"),

        // 15: 그 밖의 API 오류(envelope.message만 문구에 그대로)
        ["r15_code500WithMessage"] = new(Response(200, "{\"code\":500,\"message\":\"서버 점검 중\"}"),
            BridgeFailureKind.Api, "api_code", "서버 점검 중", "500"),
        ["r15_code500WithoutMessage"] = new(Response(200, "{\"code\":500,\"message\":\"  \"," + SecretMembers + "}"),
            BridgeFailureKind.Api, "api_code", BridgeResultClassifier.ApiErrorFallbackMessage, "500"),
        ["r15_codeMissing"] = new(Response(200, "{\"data\":[]," + SecretMembers + "}"),
            BridgeFailureKind.Api, "api_code", BridgeResultClassifier.ApiErrorFallbackMessage),
        ["r15_codeRouteOther"] = new(Response(200, "{\"code\":\"ROUTE-0005\"}"),
            BridgeFailureKind.Api, "api_code", BridgeResultClassifier.ApiErrorFallbackMessage, "ROUTE-0005"),
        ["r15_messageWithSecret"] = new(Response(200, "{\"code\":\"E1\",\"message\":\"" + SecretTitle + " " + SecretEmail + "\"}"),
            BridgeFailureKind.Api, "api_code", SecretTitle + " " + SecretEmail, "E1"),
    };

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in Cases.Keys)
            data.Add(name);
        return data;
    }

    /// <summary>판정표의 각 행: Kind, ReasonCode, Message, ApiCode가 표와 같다.</summary>
    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Classify_MatchesDecisionTable(string name)
    {
        var expected = Cases[name];

        var verdict = BridgeResultClassifier.Classify(expected.Payload);

        Assert.Equal(expected.Kind, verdict.Kind);
        Assert.Equal(expected.Kind == BridgeFailureKind.None, verdict.IsSuccess);
        Assert.Equal(expected.ReasonCode, verdict.ReasonCode);
        Assert.Equal(expected.Message, verdict.Message);
        Assert.Equal(expected.ApiCode, verdict.ApiCode);
        Assert.NotNull(verdict.Events);
        if (!verdict.IsSuccess)
            Assert.Empty(verdict.Events);
    }

    /// <summary>
    /// 본문에 일정 제목·이메일이 든 모든 실패 케이스에서 Message·ReasonCode·ApiCode에 두 값이 없다.
    /// envelope.message를 그대로 쓰는 api_code 케이스만 예외다.
    /// </summary>
    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Classify_Failure_DoesNotLeakBodyContent(string name)
    {
        var verdict = BridgeResultClassifier.Classify(Cases[name].Payload);
        if (verdict.IsSuccess || verdict.ReasonCode == "api_code")
            return;

        foreach (var secret in new[] { SecretTitle, SecretEmail })
        {
            Assert.DoesNotContain(secret, verdict.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, verdict.ReasonCode, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, verdict.ApiCode ?? "", StringComparison.Ordinal);
        }
    }

    /// <summary>성공이면 envelope의 이벤트가 필드까지 역직렬화되어 Events로 온다.</summary>
    [Theory]
    [InlineData("200")]
    [InlineData("\"200\"")]
    public void Classify_Success_DeserializesEvents(string code)
    {
        var verdict = BridgeResultClassifier.Classify(Response(200, "{\"code\":" + code + ",\"message\":\"OK\",\"data\":[" + OkEventJson + "]}"));

        Assert.True(verdict.IsSuccess);
        Assert.Null(verdict.ApiCode);
        var single = Assert.Single(verdict.Events);
        Assert.Equal("1", single.Id);
        Assert.Equal("12345", single.CalendarId);
        Assert.Equal("테스트", single.Summary);
        Assert.Equal(new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.FromHours(9)), single.StartTime);
    }

    /// <summary>data가 null이어도 Events는 null이 아닌 빈 목록이다(보충 규칙 f).</summary>
    [Fact]
    public void Classify_SuccessWithNullData_ReturnsEmptyEventList()
    {
        var verdict = BridgeResultClassifier.Classify(Response(200, "{\"code\":200,\"data\":null}"));

        Assert.True(verdict.IsSuccess);
        Assert.NotNull(verdict.Events);
        Assert.Empty(verdict.Events);
    }

    /// <summary>tooLarge 문구의 숫자는 현재 문화권과 무관하게 InvariantCulture N0로 찍는다.</summary>
    [Fact]
    public void Classify_TooLarge_UsesInvariantThousandsSeparator()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var verdict = BridgeResultClassifier.Classify(Outcome(BridgeFetchOutcomes.TooLarge, status: 200, bodyLength: 1200000));

            Assert.Equal("DaouOffice 응답이 너무 큽니다 (1,200,000자)", verdict.Message);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    /// <summary>상태 문구 상수는 설계 6.1절 문구와 글자 단위로 같다(문서·App이 이 값을 그대로 표시한다).</summary>
    [Fact]
    public void Messages_MatchDesignedWording()
    {
        Assert.Equal(1_000_000, BridgeResultClassifier.MaxBodyChars);
        Assert.Equal("Chrome의 DaouOffice 로그인이 필요합니다", BridgeResultClassifier.AuthRequiredMessage);
        Assert.Equal("DaouOffice가 로그인 페이지로 리디렉션했습니다.", BridgeResultClassifier.RedirectMessage);
        Assert.Equal("Chrome의 DaouOffice 세션이 만료되었거나 로그인 페이지로 이동했습니다.", BridgeResultClassifier.LoginPageMessage);
        Assert.Equal("DaouOffice 응답 시간 초과", BridgeResultClassifier.TimeoutMessage);
        Assert.Equal("네트워크 오류: DaouOffice에 연결하지 못했습니다", BridgeResultClassifier.NetworkMessage);
        Assert.Equal("DaouOffice HTTP {0}", BridgeResultClassifier.HttpErrorFormat);
        Assert.Equal("DaouOffice API 오류", BridgeResultClassifier.ApiErrorFallbackMessage);
        Assert.Equal("DaouOffice 응답이 비어 있습니다", BridgeResultClassifier.EmptyBodyMessage);
        Assert.Equal("DaouOffice 응답을 해석하지 못했습니다", BridgeResultClassifier.InvalidJsonMessage);
        Assert.Equal("DaouOffice 응답이 너무 큽니다 ({0}자)", BridgeResultClassifier.TooLargeFormat);
        Assert.Equal("Chrome 확장 조회 오류: {0}", BridgeResultClassifier.ExtensionErrorFormat);
        Assert.Equal("Chrome 확장 결과 형식을 알 수 없습니다", BridgeResultClassifier.UnknownOutcomeMessage);
        Assert.Equal("동기화 결과 처리 실패: {0}", BridgeResultClassifier.ProcessingFailedFormat);
    }

    /// <summary>outcome 코드 리터럴은 worker 계약(설계 3.3절)과 같다.</summary>
    [Fact]
    public void Outcomes_MatchWorkerContract()
    {
        Assert.Equal("response", BridgeFetchOutcomes.Response);
        Assert.Equal("timeout", BridgeFetchOutcomes.Timeout);
        Assert.Equal("network", BridgeFetchOutcomes.Network);
        Assert.Equal("tooLarge", BridgeFetchOutcomes.TooLarge);
        Assert.Equal("error", BridgeFetchOutcomes.Error);
    }

    // ---- 7.3.0: postResult 선택 필드 refreshState/refreshStatus와 '세션 갱신 대기' 판정 ----

    /// <summary>만료 응답 본문(401 + ROUTE-0006).</summary>
    private const string ExpiredBody = "{\"code\":\"ROUTE-0006\"}";

    /// <summary>로그아웃 응답 본문(401 + ROUTE-0004).</summary>
    private const string LoggedOutBody = "{\"code\":\"ROUTE-0004\"}";

    private static BridgeResultPayload WithRefresh(int status, string body, string? refreshState, int? refreshStatus = null)
    {
        var payload = Response(status, body, contentType: "application/json");
        payload.RefreshState = refreshState;
        payload.RefreshStatus = refreshStatus;
        return payload;
    }

    /// <summary>공개 쓰기 가능 속성을 모두 복사한다. 공유 표 케이스(<see cref="Cases"/>)를 바꾸지 않기 위함이다.</summary>
    private static BridgeResultPayload Clone(BridgeResultPayload source)
    {
        var copy = new BridgeResultPayload();
        foreach (var property in typeof(BridgeResultPayload).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.CanRead && property.CanWrite)
                property.SetValue(copy, property.GetValue(source));
        }

        return copy;
    }

    private static string[] AllRefreshStates() => typeof(BridgeRefreshStates)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .ToArray();

    /// <summary>①② 만료 401 + 확장이 갱신을 미룬 상태(waiting_tab, budget)면 '세션 갱신 대기'로 판정한다.</summary>
    [Theory]
    [InlineData(BridgeRefreshStates.WaitingTab)]
    [InlineData(BridgeRefreshStates.Budget)]
    public void Classify_Expired401WithPendingRefresh_ReportsSessionRefreshPending(string refreshState)
    {
        var verdict = BridgeResultClassifier.Classify(WithRefresh(401, ExpiredBody, refreshState));

        Assert.Equal(BridgeFailureKind.Authentication, verdict.Kind);
        Assert.False(verdict.IsSuccess);
        Assert.Equal("session_refresh_pending", verdict.ReasonCode);
        Assert.Equal(BridgeResultClassifier.SessionRefreshPendingMessage, verdict.Message);
        Assert.Equal("ROUTE-0006", verdict.ApiCode);
        Assert.Empty(verdict.Events);
    }

    /// <summary>③⑧ 만료 401이어도 갱신 대기가 아닌 상태(refreshed·rejected·failed·no_token·cooldown·cooldown_rejected·unavailable)면 기존 인증 필요 판정이다.</summary>
    [Theory]
    [InlineData(BridgeRefreshStates.Rejected, 401)]
    [InlineData(BridgeRefreshStates.Rejected, 403)]
    [InlineData(BridgeRefreshStates.Refreshed, 200)]
    [InlineData(BridgeRefreshStates.Failed, 500)]
    [InlineData(BridgeRefreshStates.Failed, 0)]
    [InlineData(BridgeRefreshStates.NoToken, null)]
    [InlineData(BridgeRefreshStates.Cooldown, null)]
    [InlineData(BridgeRefreshStates.CooldownRejected, null)]
    [InlineData(BridgeRefreshStates.Unavailable, null)]
    public void Classify_Expired401WithNonPendingRefresh_ReportsAuthRequired(string refreshState, int? refreshStatus)
    {
        var verdict = BridgeResultClassifier.Classify(WithRefresh(401, ExpiredBody, refreshState, refreshStatus));

        Assert.Equal(BridgeFailureKind.Authentication, verdict.Kind);
        Assert.Equal("http_401", verdict.ReasonCode);
        Assert.Equal(BridgeResultClassifier.AuthRequiredMessage, verdict.Message);
        Assert.Equal("ROUTE-0006", verdict.ApiCode);
    }

    /// <summary>④ refreshState가 없는 만료 401(7.2.0 확장)은 7.2.0과 같은 인증 필요 판정이다.</summary>
    [Fact]
    public void Classify_Expired401WithoutRefreshState_MatchesLegacyVerdict()
    {
        var verdict = BridgeResultClassifier.Classify(WithRefresh(401, ExpiredBody, refreshState: null));

        Assert.Equal(BridgeFailureKind.Authentication, verdict.Kind);
        Assert.Equal("http_401", verdict.ReasonCode);
        Assert.Equal(BridgeResultClassifier.AuthRequiredMessage, verdict.Message);
        Assert.Equal("ROUTE-0006", verdict.ApiCode);
    }

    /// <summary>⑤ 로그아웃 401(ROUTE-0004)은 확장이 waiting_tab을 실어도 인증 필요다.</summary>
    [Fact]
    public void Classify_LoggedOut401WithWaitingTab_ReportsAuthRequired()
    {
        var verdict = BridgeResultClassifier.Classify(WithRefresh(401, LoggedOutBody, BridgeRefreshStates.WaitingTab));

        Assert.Equal(BridgeFailureKind.Authentication, verdict.Kind);
        Assert.Equal("http_401", verdict.ReasonCode);
        Assert.Equal(BridgeResultClassifier.AuthRequiredMessage, verdict.Message);
        Assert.Equal("ROUTE-0004", verdict.ApiCode);
    }

    /// <summary>⑥ 403은 본문이 ROUTE-0006이고 waiting_tab이어도 갱신 대기가 아니다(401만 대상).</summary>
    [Fact]
    public void Classify_Expired403WithWaitingTab_ReportsAuthRequired()
    {
        var verdict = BridgeResultClassifier.Classify(WithRefresh(403, ExpiredBody, BridgeRefreshStates.WaitingTab));

        Assert.Equal(BridgeFailureKind.Authentication, verdict.Kind);
        Assert.Equal("http_403", verdict.ReasonCode);
        Assert.Equal(BridgeResultClassifier.AuthRequiredMessage, verdict.Message);
        Assert.Equal("ROUTE-0006", verdict.ApiCode);
    }

    /// <summary>⑦ refreshState 비교는 대소문자·공백을 구분한다(Ordinal). 계약 밖 값은 기존 판정이다.</summary>
    [Theory]
    [InlineData("WAITING_TAB")]
    [InlineData("Waiting_Tab")]
    [InlineData("BUDGET")]
    [InlineData(" waiting_tab")]
    [InlineData("budget ")]
    [InlineData("")]
    [InlineData("disabled")]
    public void Classify_Expired401WithOutOfContractRefreshState_ReportsAuthRequired(string refreshState)
    {
        var verdict = BridgeResultClassifier.Classify(WithRefresh(401, ExpiredBody, refreshState));

        Assert.Equal(BridgeFailureKind.Authentication, verdict.Kind);
        Assert.Equal("http_401", verdict.ReasonCode);
        Assert.Equal(BridgeResultClassifier.AuthRequiredMessage, verdict.Message);
        Assert.Equal("ROUTE-0006", verdict.ApiCode);
    }

    /// <summary>
    /// 만료 판정은 본문 최상위 code가 정확히 ROUTE-0006일 때만이다. 소문자 코드, 코드 없음, 중첩 code,
    /// 해석 불가·과대 본문은 waiting_tab이 있어도 기존 판정(ApiCode도 기존 추출 규칙 그대로)이다.
    /// </summary>
    [Theory]
    [InlineData("{\"code\":\"route-0006\"}", "route-0006")]
    [InlineData("{\"code\":\"ROUTE-00060\"}", "ROUTE-00060")]
    [InlineData("{\"data\":{\"code\":\"ROUTE-0006\"}}", null)]
    [InlineData("[{\"code\":\"ROUTE-0006\"}]", null)]
    [InlineData("{not json ROUTE-0006", null)]
    [InlineData("", null)]
    public void Classify_401WithWaitingTabButNotExpiredCode_ReportsAuthRequired(string body, string? expectedApiCode)
    {
        var verdict = BridgeResultClassifier.Classify(WithRefresh(401, body, BridgeRefreshStates.WaitingTab));

        Assert.Equal(BridgeFailureKind.Authentication, verdict.Kind);
        Assert.Equal("http_401", verdict.ReasonCode);
        Assert.Equal(BridgeResultClassifier.AuthRequiredMessage, verdict.Message);
        Assert.Equal(expectedApiCode, verdict.ApiCode);
    }

    /// <summary>
    /// 과대 본문(MaxBodyChars 초과)은 code를 읽지 않으므로 ROUTE-0006이어도 갱신 대기가 아니다.
    /// </summary>
    [Fact]
    public void Classify_Expired401WithTooLongBody_ReportsAuthRequired()
    {
        var body = "{\"code\":\"ROUTE-0006\",\"pad\":\"" + new string('x', BridgeResultClassifier.MaxBodyChars) + "\"}";

        var verdict = BridgeResultClassifier.Classify(WithRefresh(401, body, BridgeRefreshStates.WaitingTab));

        Assert.Equal("http_401", verdict.ReasonCode);
        Assert.Equal(BridgeResultClassifier.AuthRequiredMessage, verdict.Message);
        Assert.Null(verdict.ApiCode);
    }

    /// <summary>
    /// 리디렉션은 7단계보다 먼저다. 만료 본문과 waiting_tab이 있어도 redirect 판정이다.
    /// </summary>
    [Fact]
    public void Classify_RedirectedExpired401WithWaitingTab_StaysRedirect()
    {
        var payload = WithRefresh(401, ExpiredBody, BridgeRefreshStates.WaitingTab);
        payload.Redirected = true;

        var verdict = BridgeResultClassifier.Classify(payload);

        Assert.Equal(BridgeFailureKind.Authentication, verdict.Kind);
        Assert.Equal("redirect", verdict.ReasonCode);
        Assert.Equal(BridgeResultClassifier.RedirectMessage, verdict.Message);
        Assert.Null(verdict.ApiCode);
    }

    /// <summary>
    /// I-13: 판정표의 모든 기존 행은 refreshState·refreshStatus를 어떤 값으로 실어도 결과(Kind, ReasonCode, Message, ApiCode)가 그대로다.
    /// 기존 행에는 401 + ROUTE-0006이 없으므로 새 판정이 끼어들 자리가 없어야 한다.
    /// </summary>
    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Classify_RefreshFieldsOnExistingRows_DoNotChangeVerdict(string name)
    {
        var expected = Cases[name];
        if (expected.Payload is null)
            return;

        var states = AllRefreshStates().Cast<string?>().Append(null).Append("WAITING_TAB").Append("");
        foreach (var state in states)
        {
            foreach (var status in new int?[] { null, 0, 200, 401 })
            {
                var payload = Clone(expected.Payload);
                payload.RefreshState = state;
                payload.RefreshStatus = status;

                var verdict = BridgeResultClassifier.Classify(payload);

                Assert.Equal(expected.Kind, verdict.Kind);
                Assert.Equal(expected.ReasonCode, verdict.ReasonCode);
                Assert.Equal(expected.Message, verdict.Message);
                Assert.Equal(expected.ApiCode, verdict.ApiCode);
            }
        }

        // 복사본만 바꿨는지 확인한다(공유 표 케이스 오염 방지).
        Assert.Null(expected.Payload.RefreshState);
        Assert.Null(expected.Payload.RefreshStatus);
    }

    /// <summary>
    /// postResult JSON을 앱 파이프 서버와 같은 Web 기본 옵션으로 역직렬화한다.
    /// 두 필드가 있으면 값(0 포함)을 읽고, 7.2.0 확장처럼 없으면 null이며, 읽은 결과로 판정이 이어진다.
    /// </summary>
    [Fact]
    public void PostResultJson_ReadsRefreshFieldsWhenPresentAndNullWhenAbsent()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var pending = JsonSerializer.Deserialize<NativeBridgeRequest>(
            """
            {"type":"postResult","protocolVersion":2,"result":{"requestId":"r1","outcome":"response","status":401,"responseType":"basic","redirected":false,"contentType":"application/json","body":"{\"code\":\"ROUTE-0006\"}","bodyLength":21,"elapsedMs":120,"refreshState":"waiting_tab"}}
            """,
            options);

        Assert.NotNull(pending?.Result);
        Assert.Equal(BridgeRefreshStates.WaitingTab, pending!.Result!.RefreshState);
        Assert.Null(pending.Result.RefreshStatus);
        var pendingVerdict = BridgeResultClassifier.Classify(pending.Result);
        Assert.Equal("session_refresh_pending", pendingVerdict.ReasonCode);
        Assert.Equal(BridgeResultClassifier.SessionRefreshPendingMessage, pendingVerdict.Message);

        var failedNoResponse = JsonSerializer.Deserialize<NativeBridgeRequest>(
            """
            {"type":"postResult","protocolVersion":2,"result":{"requestId":"r2","outcome":"response","status":401,"responseType":"basic","redirected":false,"contentType":"application/json","body":"{\"code\":\"ROUTE-0006\"}","bodyLength":21,"elapsedMs":120,"refreshState":"failed","refreshStatus":0}}
            """,
            options);

        Assert.NotNull(failedNoResponse?.Result);
        Assert.Equal(BridgeRefreshStates.Failed, failedNoResponse!.Result!.RefreshState);
        Assert.Equal(0, failedNoResponse.Result.RefreshStatus);

        var refreshed = JsonSerializer.Deserialize<NativeBridgeRequest>(
            """
            {"type":"postResult","protocolVersion":2,"result":{"requestId":"r3","outcome":"response","status":200,"refreshState":"refreshed","refreshStatus":200}}
            """,
            options);

        Assert.Equal(BridgeRefreshStates.Refreshed, refreshed!.Result!.RefreshState);
        Assert.Equal(200, refreshed.Result.RefreshStatus);

        var legacy = JsonSerializer.Deserialize<NativeBridgeRequest>(
            """
            {"type":"postResult","protocolVersion":2,"result":{"requestId":"r4","outcome":"response","status":401,"responseType":"basic","redirected":false,"contentType":"application/json","body":"{\"code\":\"ROUTE-0006\"}","bodyLength":21,"elapsedMs":120}}
            """,
            options);

        Assert.NotNull(legacy?.Result);
        Assert.Null(legacy!.Result!.RefreshState);
        Assert.Null(legacy.Result.RefreshStatus);
        var legacyVerdict = BridgeResultClassifier.Classify(legacy.Result);
        Assert.Equal("http_401", legacyVerdict.ReasonCode);
        Assert.Equal(BridgeResultClassifier.AuthRequiredMessage, legacyVerdict.Message);
        Assert.Equal("ROUTE-0006", legacyVerdict.ApiCode);
    }

    /// <summary>refreshState 리터럴 9개는 worker 계약과 글자 단위로 같고, 그 밖의 값(disabled 등)은 없다.</summary>
    [Fact]
    public void RefreshStates_MatchWorkerContract()
    {
        Assert.Equal("refreshed", BridgeRefreshStates.Refreshed);
        Assert.Equal("rejected", BridgeRefreshStates.Rejected);
        Assert.Equal("failed", BridgeRefreshStates.Failed);
        Assert.Equal("no_token", BridgeRefreshStates.NoToken);
        Assert.Equal("cooldown", BridgeRefreshStates.Cooldown);
        Assert.Equal("cooldown_rejected", BridgeRefreshStates.CooldownRejected);
        Assert.Equal("waiting_tab", BridgeRefreshStates.WaitingTab);
        Assert.Equal("budget", BridgeRefreshStates.Budget);
        Assert.Equal("unavailable", BridgeRefreshStates.Unavailable);

        Assert.Equal(
            new[] { "budget", "cooldown", "cooldown_rejected", "failed", "no_token", "refreshed", "rejected", "unavailable", "waiting_tab" },
            AllRefreshStates().OrderBy(v => v, StringComparer.Ordinal).ToArray());
    }

    /// <summary>만료 코드와 갱신 대기 문구는 설계 문구와 글자 단위로 같다.</summary>
    [Fact]
    public void SessionRefreshConstants_MatchDesignedWording()
    {
        Assert.Equal("ROUTE-0006", BridgeResultClassifier.ExpiredApiCode);
        Assert.Equal("DaouOffice 세션 갱신을 기다리는 중입니다", BridgeResultClassifier.SessionRefreshPendingMessage);
    }
}
