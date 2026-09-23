using System.Text;
using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

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
            "{\"type\":\"getConfig\",\"lastError\":\"getConfig: boom\",\"extensionVersion\":\"7.1.0\",\"protocolVersion\":1}"));

        Assert.Equal("type=getConfig protocolVersion=1", summary);
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

    [Fact]
    public void DescribeRequest_PostResult_OmitsCookieHeaderValue()
    {
        const string json = """
            {"type":"postResult","result":{"requestId":"r1","cookieHeader":"JSESSIONID=SUPERSECRET","cookieCount":3,"cookieSource":"live"}}
            """;

        var summary = BridgeLogSummary.DescribeRequest(Utf8(json));

        Assert.DoesNotContain("SUPERSECRET", summary);
        Assert.DoesNotContain("cookieHeader", summary);
        Assert.Contains("type=postResult", summary);
        Assert.Contains("cookieCount=3", summary);
        Assert.Contains("cookieSource=live", summary);
    }

    [Fact]
    public void DescribeRequest_InvalidJson_ReturnsUnknownType()
    {
        Assert.Equal("type=unknown", BridgeLogSummary.DescribeRequest(Utf8("not json")));
        Assert.Equal("type=unknown", BridgeLogSummary.DescribeRequest(Array.Empty<byte>()));
        Assert.Equal("type=unknown", BridgeLogSummary.DescribeRequest(null));
    }

    [Fact]
    public void DescribeResult_IncludesCookieCountAndSourceButNotHeader()
    {
        var payload = new BridgeResultPayload
        {
            RequestId = "abc",
            CookieHeader = "K=SECRET",
            CookieCount = 7,
            CookieSource = "session-cache",
            Error = null
        };

        var summary = BridgeLogSummary.DescribeResult(payload);

        Assert.Contains("requestId=abc", summary);
        Assert.Contains("cookieCount=7", summary);
        Assert.Contains("cookieSource=session-cache", summary);
        Assert.Contains("hasError=False", summary);
        Assert.DoesNotContain("SECRET", summary);
    }

    [Fact]
    public void DescribeResult_Null_ReturnsResultNull()
    {
        Assert.Equal("result=null", BridgeLogSummary.DescribeResult(null));
    }
}
