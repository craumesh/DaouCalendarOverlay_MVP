using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

/// <summary>
/// 확장이 보고한 버전과 EXE 기대 버전을 비교하는 <see cref="ExtensionVersionGuard"/> 순수 로직 테스트.
/// </summary>
public sealed class ExtensionVersionGuardTests
{
    /// <summary>버전이 같으면 불일치가 아니다.</summary>
    [Fact]
    public void IsMismatch_ReturnsFalseWhenVersionsMatch()
    {
        Assert.False(ExtensionVersionGuard.IsMismatch("7.1.0", "7.1.0"));
    }

    /// <summary>뒤쪽 0 구성 요소와 앞뒤 공백은 비교에 영향을 주지 않는다.</summary>
    [Fact]
    public void IsMismatch_IgnoresTrailingZeroAndWhitespace()
    {
        Assert.False(ExtensionVersionGuard.IsMismatch(" 7.1.0.0 ", "7.1.0"));
        Assert.False(ExtensionVersionGuard.IsMismatch("7.1", "7.1.0"));
    }

    /// <summary>구버전 확장이 보고한 버전은 불일치다.</summary>
    [Fact]
    public void IsMismatch_ReturnsTrueForOlderExtension()
    {
        Assert.True(ExtensionVersionGuard.IsMismatch("7.0.0", "7.1.0"));
    }

    /// <summary>버전 필드를 보내지 않는 구버전 확장(null/빈 문자열/공백)은 불일치로 본다.</summary>
    [Fact]
    public void IsMismatch_TreatsMissingReportedVersionAsMismatch()
    {
        Assert.True(ExtensionVersionGuard.IsMismatch(null, "7.1.0"));
        Assert.True(ExtensionVersionGuard.IsMismatch("", "7.1.0"));
        Assert.True(ExtensionVersionGuard.IsMismatch("   ", "7.1.0"));
    }

    /// <summary>기대 버전을 모르면 경고하지 않는다.</summary>
    [Fact]
    public void IsMismatch_ReturnsFalseWhenExpectedVersionUnknown()
    {
        Assert.False(ExtensionVersionGuard.IsMismatch("7.0.0", ""));
    }

    /// <summary>빈 입력은 빈 문자열, 파싱 불가 입력은 원문 유지, 두 구성 요소 버전은 build 0으로 보충한다.</summary>
    [Fact]
    public void Normalize_ReturnsEmptyForBlankAndKeepsUnparsableInput()
    {
        Assert.Equal("", ExtensionVersionGuard.Normalize(null));
        Assert.Equal("abc", ExtensionVersionGuard.Normalize("abc"));
        Assert.Equal("7.1.0", ExtensionVersionGuard.Normalize("  7.1 "));
    }
}
