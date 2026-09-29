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
}
