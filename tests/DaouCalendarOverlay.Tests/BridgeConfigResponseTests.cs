using System.Text.Json;
using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

/// <summary>
/// 브리지 config 응답의 JSON 표면(확장이 읽는 계약)을 고정하는 테스트.
/// 직렬화 옵션은 <see cref="CalendarBridgeServer"/>와 동일한 Web 기본값을 쓴다.
/// </summary>
public sealed class BridgeConfigResponseTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    /// <summary>shouldFetch=false 응답에는 사유 코드가 함께 내려간다.</summary>
    [Fact]
    public void NoFetch_IncludesNoFetchReasonInWebJson()
    {
        var json = JsonSerializer.Serialize(
            BridgeConfigResponse.NoFetch(NoFetchReasons.InvalidBaseUrl), WebOptions);

        Assert.Contains("\"shouldFetch\":false", json);
        Assert.Contains("\"noFetchReason\":\"invalid_base_url\"", json);
    }

    /// <summary>fetch 응답에는 noFetchReason 키 자체가 나오지 않는다(호환 변경).</summary>
    [Fact]
    public void FetchResponse_OmitsNoFetchReasonWhenNull()
    {
        var json = JsonSerializer.Serialize(
            new BridgeConfigResponse { Ok = true, ShouldFetch = true, RequestId = "r1" }, WebOptions);

        Assert.DoesNotContain("noFetchReason", json);
    }
}
