using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

/// <summary>
/// 브리지 <c>noFetchReason</c> 코드 값과 분류·표시 문구를 고정하는 테스트.
/// 코드 값은 확장·문서와 공유하는 계약이므로 문자열을 그대로 단언한다.
/// </summary>
public sealed class NoFetchReasonsTests
{
    /// <summary>코드 값은 소문자 스네이크로 고정된다(설계 문서 9절 계약).</summary>
    [Fact]
    public void Constants_UseLowerSnakeCaseValues()
    {
        Assert.Equal("not_configured", NoFetchReasons.NotConfigured);
        Assert.Equal("invalid_base_url", NoFetchReasons.InvalidBaseUrl);
        Assert.Equal("range_not_ready", NoFetchReasons.RangeNotReady);
        Assert.Equal("lease_active", NoFetchReasons.LeaseActive);
        Assert.Equal("backoff", NoFetchReasons.Backoff);
        Assert.Equal("not_due", NoFetchReasons.NotDue);
        Assert.Equal("protocol_mismatch", NoFetchReasons.ProtocolMismatch);
    }

    /// <summary>설정을 고쳐야 풀리는 사유만 설정 문제로 분류된다.</summary>
    [Fact]
    public void IsConfigurationProblem_TrueForConfigReasons()
    {
        Assert.True(NoFetchReasons.IsConfigurationProblem(NoFetchReasons.NotConfigured));
        Assert.True(NoFetchReasons.IsConfigurationProblem(NoFetchReasons.InvalidBaseUrl));
    }

    /// <summary>일시적인 스케줄링 사유와 빈 값은 설정 문제가 아니다.</summary>
    [Theory]
    [InlineData("lease_active")]
    [InlineData("backoff")]
    [InlineData("not_due")]
    [InlineData("range_not_ready")]
    [InlineData("protocol_mismatch")]
    [InlineData(null)]
    [InlineData("")]
    public void IsConfigurationProblem_FalseForSchedulingReasonsAndNull(string? reason)
    {
        Assert.False(NoFetchReasons.IsConfigurationProblem(reason));
    }

    /// <summary>알려진 사유는 사용자에게 보여줄 한국어 문구를 돌려준다.</summary>
    [Fact]
    public void Describe_KnownReasons_ReturnKoreanSentences()
    {
        Assert.False(string.IsNullOrWhiteSpace(NoFetchReasons.Describe(NoFetchReasons.NotConfigured)));
        Assert.False(string.IsNullOrWhiteSpace(NoFetchReasons.Describe(NoFetchReasons.InvalidBaseUrl)));
        Assert.Contains("daouoffice.com", NoFetchReasons.Describe(NoFetchReasons.InvalidBaseUrl));
        Assert.False(string.IsNullOrWhiteSpace(NoFetchReasons.Describe(NoFetchReasons.ProtocolMismatch)));
        Assert.Contains("프로토콜", NoFetchReasons.Describe(NoFetchReasons.ProtocolMismatch));
    }

    /// <summary>모르는 코드는 그대로, 빈 값은 안전한 기본 문구로 변환된다.</summary>
    [Fact]
    public void Describe_UnknownOrBlankReason_IsSafe()
    {
        Assert.Equal("who_knows", NoFetchReasons.Describe("who_knows"));
        Assert.Equal("알 수 없는 사유", NoFetchReasons.Describe(null));
    }
}
