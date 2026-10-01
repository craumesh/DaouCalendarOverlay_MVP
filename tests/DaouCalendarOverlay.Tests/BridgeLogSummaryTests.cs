using System.Text;
using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

/// <summary>
/// 브리지 로그 요약(host 로그와 앱 로그가 함께 쓴다)이 허용한 전송 필드만 정제해 남기고,
/// 응답 본문·쿠키·requestId 원문은 남기지 않는지 고정한다.
/// </summary>
public sealed class BridgeLogSummaryTests
{
    private static byte[] Utf8(string json) => Encoding.UTF8.GetBytes(json);

    [Fact]
    public void DescribeRequest_GetConfig_ReturnsTypeOnly()
    {
        var summary = BridgeLogSummary.DescribeRequest(Utf8("{\"type\":\"getConfig\"}"));

        Assert.Equal("type=getConfig", summary);
    }

    /// <summary>getConfig에 실린 protocolVersion은 로그 요약에 남고, lastError·extensionVersion 원문은 붙지 않는다.</summary>
    [Fact]
    public void DescribeRequest_GetConfigWithProtocolVersion_IncludesProtocolVersion()
    {
        var summary = BridgeLogSummary.DescribeRequest(Utf8(
            "{\"type\":\"getConfig\",\"lastError\":\"getConfig: boom\",\"extensionVersion\":\"7.3.0\",\"protocolVersion\":2}"));

        Assert.Equal("type=getConfig protocolVersion=2", summary);
    }

    /// <summary>protocolVersion이 숫자가 아니면 요약에 붙이지 않는다(요약은 예외를 던지지 않는다).</summary>
    [Theory]
    [InlineData("{\"type\":\"getConfig\",\"protocolVersion\":null}")]
    [InlineData("{\"type\":\"getConfig\",\"protocolVersion\":\"x\"}")]
    [InlineData("{\"type\":\"getConfig\",\"protocolVersion\":1.5}")]
    public void DescribeRequest_GetConfigWithNonIntegerProtocolVersion_OmitsIt(string json)
    {
        Assert.Equal("type=getConfig", BridgeLogSummary.DescribeRequest(Utf8(json)));
    }

    /// <summary>전체 postResult 요약은 설계 6.3절 예시와 글자 단위로 같다(requestId와 body는 빠진다).</summary>
    [Fact]
    public void DescribeRequest_PostResult_MatchesDocumentedFormat()
    {
        const string json = """
            {"type":"postResult","protocolVersion":2,"result":{"requestId":"3f2a91c0","outcome":"response","status":200,"responseType":"basic","redirected":false,"contentType":"application/json;charset=UTF-8","body":"{\"code\":200}","bodyLength":53088,"elapsedMs":812}}
            """;

        var summary = BridgeLogSummary.DescribeRequest(Utf8(json));

        Assert.Equal(
            "type=postResult protocolVersion=2 outcome=response status=200 contentType=application/json bodyLength=53088 redirected=false elapsedMs=812",
            summary);
    }

    /// <summary>본문에 든 일정 제목·이메일과 body 키 자체는 요약에 나오지 않는다(host 로그 파일 보호).</summary>
    [Fact]
    public void DescribeRequest_PostResult_OmitsBodyContent()
    {
        const string json = """
            {"type":"postResult","protocolVersion":2,"result":{"requestId":"r1","outcome":"response","status":200,"contentType":"application/json","body":"{\"code\":200,\"data\":[{\"summary\":\"SECRET-TITLE\",\"attendees\":[{\"email\":\"someone@example.com\"}]}],\"body\":\"x\"}","bodyLength":90,"redirected":false,"elapsedMs":5}}
            """;

        var summary = BridgeLogSummary.DescribeRequest(Utf8(json));

        Assert.StartsWith("type=postResult protocolVersion=2 outcome=response status=200", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-TITLE", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("someone@example.com", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("\"body\"", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("body=", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("requestId", summary, StringComparison.Ordinal);
    }

    /// <summary>7.1.0 형식(쿠키 필드)의 postResult가 와도 쿠키 이름·값이 요약에 남지 않는다.</summary>
    [Fact]
    public void DescribeRequest_LegacyCookiePostResult_OmitsCookieFields()
    {
        const string json = """
            {"type":"postResult","result":{"requestId":"r1","cookieHeader":"JSESSIONID=SUPERSECRET","cookieCount":3,"cookieSource":"live","userAgent":"Mozilla/5.0 SUPERSECRET"}}
            """;

        var summary = BridgeLogSummary.DescribeRequest(Utf8(json));

        Assert.Equal("type=postResult", summary);
        Assert.DoesNotContain("SUPERSECRET", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("cookie", summary, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>outcome은 토큰 문자만, contentType은 ';' 앞 소문자 media type만 남긴다.</summary>
    [Fact]
    public void DescribeRequest_SanitizesOutcomeAndContentType()
    {
        const string json = """
            {"type":"postResult","protocolVersion":2,"result":{"outcome":"a b<c","contentType":"Application/JSON; charset=UTF-8","errorName":"Type Error<script>"}}
            """;

        var summary = BridgeLogSummary.DescribeRequest(Utf8(json));

        Assert.Equal("type=postResult protocolVersion=2 outcome=abc contentType=application/json errorName=TypeErrorscript", summary);
    }

    /// <summary>JSON 종류가 맞지 않는 필드는 붙이지 않는다(문자열 status, 숫자 outcome, 문자열 redirected 등).</summary>
    [Fact]
    public void DescribeRequest_WrongJsonKinds_AreOmitted()
    {
        const string json = """
            {"type":"postResult","protocolVersion":2,"result":{"outcome":5,"status":"200","contentType":null,"bodyLength":1.5,"redirected":"true","elapsedMs":99999999999,"errorName":{"x":1}}}
            """;

        Assert.Equal("type=postResult protocolVersion=2", BridgeLogSummary.DescribeRequest(Utf8(json)));
    }

    /// <summary>선택 필드 refreshState·refreshStatus는 요약 끝에 붙고, 본문 문자열은 남지 않는다.</summary>
    [Fact]
    public void DescribeRequest_PostResultWithRefreshFields_AppendsThemAtEnd()
    {
        const string json = """
            {"type":"postResult","protocolVersion":2,"result":{"outcome":"response","status":401,"errorName":"E","body":"SECRET-BODY","refreshState":"waiting_tab","refreshStatus":200}}
            """;

        var summary = BridgeLogSummary.DescribeRequest(Utf8(json));

        Assert.EndsWith(" refreshState=waiting_tab refreshStatus=200", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-BODY", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeRequest_RefreshStateIsSanitizedAsToken()
    {
        const string json = """
            {"type":"postResult","result":{"refreshState":"a b<script>"}}
            """;

        Assert.Equal("type=postResult refreshState=abscript", BridgeLogSummary.DescribeRequest(Utf8(json)));
    }

    [Fact]
    public void DescribeRequest_StringRefreshStatus_IsOmitted()
    {
        const string json = """
            {"type":"postResult","result":{"refreshState":"refreshed","refreshStatus":"200"}}
            """;

        var summary = BridgeLogSummary.DescribeRequest(Utf8(json));

        Assert.Equal("type=postResult refreshState=refreshed", summary);
        Assert.DoesNotContain("refreshStatus", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeResult_WithRefreshFields_AppendsThemAtEnd()
    {
        var payload = new BridgeResultPayload
        {
            RequestId = "r1",
            Outcome = "response",
            Status = 401,
            RefreshState = "refreshed",
            RefreshStatus = 200
        };

        Assert.EndsWith(" errorName=none refreshState=refreshed refreshStatus=200",
            BridgeLogSummary.DescribeResult(payload), StringComparison.Ordinal);
    }

    [Fact]
    public void DescribeResult_WithoutRefreshFields_KeepsLegacyString()
    {
        var payload = new BridgeResultPayload
        {
            RequestId = "",
            Outcome = "timeout",
            Redirected = true,
            ElapsedMs = 25003,
            ErrorName = "Abort Error!",
            RefreshState = null,
            RefreshStatus = null
        };

        Assert.Equal(
            "requestId=none outcome=timeout status=none contentType=none bodyLength=none redirected=true elapsedMs=25003 errorName=AbortError",
            BridgeLogSummary.DescribeResult(payload));
    }

    [Fact]
    public void DescribeRequest_InvalidJson_ReturnsUnknownType()
    {
        Assert.Equal("type=unknown", BridgeLogSummary.DescribeRequest(Utf8("not json")));
        Assert.Equal("type=unknown", BridgeLogSummary.DescribeRequest(Array.Empty<byte>()));
        Assert.Equal("type=unknown", BridgeLogSummary.DescribeRequest(null));
    }

    /// <summary>결과 요약은 고정 순서의 전송 필드만 쓰고, 값이 없으면 none으로 채운다. body는 쓰지 않는다.</summary>
    [Fact]
    public void DescribeResult_FormatsTransportFields()
    {
        var full = new BridgeResultPayload
        {
            RequestId = "3f2a91c0-<x>",
            Outcome = "response",
            Status = 401,
            ResponseType = "basic",
            Redirected = false,
            ContentType = "Application/JSON; charset=UTF-8",
            Body = "{\"code\":\"ROUTE-0004\",\"message\":\"SECRET-TITLE someone@example.com\"}",
            BodyLength = 47,
            ElapsedMs = 120
        };

        var summary = BridgeLogSummary.DescribeResult(full);

        Assert.Equal(
            "requestId=3f2a91c0x outcome=response status=401 contentType=application/json bodyLength=47 redirected=false elapsedMs=120 errorName=none",
            summary);
        Assert.DoesNotContain("SECRET-TITLE", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("someone@example.com", summary, StringComparison.Ordinal);

        var timeout = new BridgeResultPayload
        {
            RequestId = "",
            Outcome = "timeout",
            Redirected = true,
            ElapsedMs = 25003,
            ErrorName = "Abort Error!"
        };

        Assert.Equal(
            "requestId=none outcome=timeout status=none contentType=none bodyLength=none redirected=true elapsedMs=25003 errorName=AbortError",
            BridgeLogSummary.DescribeResult(timeout));
    }

    [Fact]
    public void DescribeResult_Null_ReturnsResultNull()
    {
        Assert.Equal("result=null", BridgeLogSummary.DescribeResult(null));
    }

    /// <summary>토큰은 [A-Za-z0-9_]만 32자까지, media type은 [a-z0-9.+/-]만 64자까지. 비면 none이다.</summary>
    [Theory]
    [InlineData(null, "none")]
    [InlineData("", "none")]
    [InlineData("<>!", "none")]
    [InlineData("한글abc_1", "abc_1")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", "ABCDEFGHIJKLMNOPQRSTUVWXYZ012345")]
    public void Token_KeepsAllowedCharactersOnly(string? input, string expected)
    {
        Assert.Equal(expected, BridgeLogSummary.Token(input));
    }

    [Theory]
    [InlineData(null, "none")]
    [InlineData("", "none")]
    [InlineData(";charset=UTF-8", "none")]
    [InlineData("Application/Problem+JSON; charset=UTF-8", "application/problem+json")]
    [InlineData("text/html", "text/html")]
    [InlineData("application/vnd.api+json", "application/vnd.api+json")]
    [InlineData("te xt/<html>", "text/html")]
    public void MediaType_NormalizesContentType(string? input, string expected)
    {
        Assert.Equal(expected, BridgeLogSummary.MediaType(input));
    }

    [Fact]
    public void MediaType_TruncatesTo64Characters()
    {
        Assert.Equal(new string('a', 64), BridgeLogSummary.MediaType(new string('A', 100)));
    }
}
