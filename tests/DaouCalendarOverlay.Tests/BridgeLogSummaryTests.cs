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
