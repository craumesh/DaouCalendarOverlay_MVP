using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

public sealed class BaseUrlPolicyTests
{
    [Fact]
    public void Validate_HttpsSubdomain_IsValidAndNormalized()
    {
        var result = BaseUrlPolicy.Validate("https://company.daouoffice.com");

        Assert.True(result.IsValid);
        Assert.Equal("https://company.daouoffice.com", result.NormalizedBaseUrl);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void Validate_StripsPathQueryAndFragment()
    {
        var result = BaseUrlPolicy.Validate("https://company.daouoffice.com/gw/app/calendar?calendarIds[]=1#x");

        Assert.True(result.IsValid);
        Assert.Equal("https://company.daouoffice.com", result.NormalizedBaseUrl);
    }

    [Fact]
    public void Validate_UppercaseSchemeAndHost_AreLowercased()
    {
        var result = BaseUrlPolicy.Validate("HTTPS://Company.DaouOffice.COM");

        Assert.True(result.IsValid);
        Assert.Equal("https://company.daouoffice.com", result.NormalizedBaseUrl);
    }

    [Fact]
    public void Validate_MultiLevelSubdomain_IsValid()
    {
        var result = BaseUrlPolicy.Validate("https://a.b.daouoffice.com");

        Assert.True(result.IsValid);
        Assert.Equal("https://a.b.daouoffice.com", result.NormalizedBaseUrl);
    }

    [Fact]
    public void Validate_NonDefaultPort_IsPreserved()
    {
        var custom = BaseUrlPolicy.Validate("https://company.daouoffice.com:8443/x");
        Assert.True(custom.IsValid);
        Assert.Equal("https://company.daouoffice.com:8443", custom.NormalizedBaseUrl);

        var defaultPort = BaseUrlPolicy.Validate("https://company.daouoffice.com:443");
        Assert.True(defaultPort.IsValid);
        Assert.Equal("https://company.daouoffice.com", defaultPort.NormalizedBaseUrl);
    }

    [Fact]
    public void Validate_TrailingSlashesAndWhitespace_AreTrimmed()
    {
        var result = BaseUrlPolicy.Validate("   https://company.daouoffice.com///   ");

        Assert.True(result.IsValid);
        Assert.Equal("https://company.daouoffice.com", result.NormalizedBaseUrl);
    }

    [Fact]
    public void Validate_TrailingDotHost_IsNormalized()
    {
        var result = BaseUrlPolicy.Validate("https://company.daouoffice.com./");

        Assert.True(result.IsValid);
        Assert.Equal("https://company.daouoffice.com", result.NormalizedBaseUrl);
    }

    [Fact]
    public void Validate_HttpScheme_IsRejected()
    {
        var result = BaseUrlPolicy.Validate("http://company.daouoffice.com");

        Assert.False(result.IsValid);
        Assert.Contains("https", result.Reason!);
    }

    [Fact]
    public void Validate_ApexDomain_IsRejected()
    {
        var result = BaseUrlPolicy.Validate("https://daouoffice.com");

        Assert.False(result.IsValid);
        Assert.Contains("daouoffice.com 자체", result.Reason!);
    }

    [Theory]
    [InlineData("https://example.com")]
    [InlineData("https://notdaouoffice.com")]
    [InlineData("https://daouoffice.com.evil.com")]
    [InlineData("https://daouoffice.co.kr")]
    public void Validate_ForeignHost_IsRejected(string baseUrl)
    {
        var result = BaseUrlPolicy.Validate(baseUrl);

        Assert.False(result.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    [Fact]
    public void Validate_UserInfo_IsRejected()
    {
        var result = BaseUrlPolicy.Validate("https://user:pw@company.daouoffice.com");

        Assert.False(result.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_NullOrWhitespace_IsRejected(string? baseUrl)
    {
        var result = BaseUrlPolicy.Validate(baseUrl);

        Assert.False(result.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("company.daouoffice.com")]
    [InlineData("ftp://company.daouoffice.com")]
    public void Validate_MalformedOrRelative_IsRejected(string baseUrl)
    {
        var result = BaseUrlPolicy.Validate(baseUrl);

        Assert.False(result.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    [Fact]
    public void Validate_InvalidResult_HasReasonAndNoNormalizedValue()
    {
        var result = BaseUrlPolicy.Validate("http://company.daouoffice.com");

        Assert.Null(result.NormalizedBaseUrl);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetStartupNotice_BlankBaseUrl_ReturnsNull(string? baseUrl)
    {
        Assert.Null(BaseUrlPolicy.GetStartupNotice(baseUrl));
    }

    [Fact]
    public void GetStartupNotice_ValidBaseUrl_ReturnsNull()
    {
        Assert.Null(BaseUrlPolicy.GetStartupNotice("https://company.daouoffice.com"));
    }

    [Fact]
    public void GetStartupNotice_InvalidBaseUrl_ReturnsReason()
    {
        const string baseUrl = "http://company.daouoffice.com";

        Assert.Equal(BaseUrlPolicy.Validate(baseUrl).Reason, BaseUrlPolicy.GetStartupNotice(baseUrl));
    }
}
