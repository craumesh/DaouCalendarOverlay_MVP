using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

public sealed class SettingsValidationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void ClampRefreshMinutes_BelowMin_ReturnsOne(int minutes)
    {
        Assert.Equal(1, SettingsValidation.ClampRefreshMinutes(minutes));
    }

    [Theory]
    [InlineData(1441)]
    [InlineData(100000)]
    [InlineData(int.MaxValue)]
    public void ClampRefreshMinutes_AboveMax_ReturnsMax(int minutes)
    {
        Assert.Equal(1440, SettingsValidation.ClampRefreshMinutes(minutes));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(1440)]
    public void ClampRefreshMinutes_InRange_ReturnsSameValue(int minutes)
    {
        Assert.Equal(minutes, SettingsValidation.ClampRefreshMinutes(minutes));
    }

    [Fact]
    public void ClampRefreshMinutes_ResultFitsDispatcherTimerInterval()
    {
        var clamped = SettingsValidation.ClampRefreshMinutes(int.MaxValue);
        Assert.True(TimeSpan.FromMinutes(clamped).TotalMilliseconds <= int.MaxValue);
    }

    [Fact]
    public void TryParseRefreshMinutes_AcceptsTrimmedDigitsInRange()
    {
        Assert.True(SettingsValidation.TryParseRefreshMinutes(" 5 ", out var five));
        Assert.Equal(5, five);

        Assert.True(SettingsValidation.TryParseRefreshMinutes("1440", out var maxValue));
        Assert.Equal(1440, maxValue);

        Assert.True(SettingsValidation.TryParseRefreshMinutes("0001", out var leadingZeros));
        Assert.Equal(1, leadingZeros);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1441")]
    [InlineData("99999")]
    public void TryParseRefreshMinutes_RejectsOutOfRange(string text)
    {
        Assert.False(SettingsValidation.TryParseRefreshMinutes(text, out var minutes));
        Assert.Equal(0, minutes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("-5")]
    [InlineData("+5")]
    [InlineData("1,440")]
    [InlineData("5.0")]
    public void TryParseRefreshMinutes_RejectsNonPlainDigits(string? text)
    {
        Assert.False(SettingsValidation.TryParseRefreshMinutes(text, out var minutes));
        Assert.Equal(0, minutes);
    }

    [Theory]
    [InlineData("38262")]
    [InlineData("1480365661766049793")]
    public void IsValidCalendarId_AcceptsFiveAndNineteenDigitIds(string id)
    {
        Assert.True(SettingsValidation.IsValidCalendarId(id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("12a45")]
    [InlineData("12345678901234567890")]
    [InlineData("１２３")]
    public void IsValidCalendarId_RejectsNonAsciiDigitsAndTooLong(string? id)
    {
        Assert.False(SettingsValidation.IsValidCalendarId(id));
    }

    [Fact]
    public void ValidateCalendarIds_AcceptsRealWorldIdsPreservingOrder()
    {
        var result = SettingsValidation.ValidateCalendarIds("38262, 60717\n1480365661766049793");

        Assert.True(result.IsValid);
        Assert.Equal(new[] { "38262", "60717", "1480365661766049793" }, result.Ids);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void ValidateCalendarIds_RemovesDuplicatesPreservingOrder()
    {
        var result = SettingsValidation.ValidateCalendarIds("123 456;123");

        Assert.Equal(new[] { "123", "456" }, result.Ids);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(", ;")]
    public void ValidateCalendarIds_RejectsEmptyInput(string? rawText)
    {
        var result = SettingsValidation.ValidateCalendarIds(rawText);

        Assert.False(result.IsValid);
        Assert.Empty(result.Ids);
        Assert.Equal(SettingsValidation.CalendarIdsEmptyReason, result.Reason);
    }

    [Fact]
    public void ValidateCalendarIds_ReportsInvalidTokens()
    {
        var result = SettingsValidation.ValidateCalendarIds("abc 12345 x1");

        Assert.False(result.IsValid);
        Assert.Equal(new[] { "abc", "x1" }, result.InvalidTokens);
        Assert.Contains("숫자만", result.Reason!);
        Assert.Contains("abc", result.Reason!);
    }

    [Fact]
    public void ValidateCalendarIds_InvalidTokenPreviewCapsAtThree()
    {
        var result = SettingsValidation.ValidateCalendarIds("a b c d e");

        Assert.EndsWith(" 외 2개", result.Reason!);
    }

    [Fact]
    public void FilterHiddenCalendarIds_KeepsOnlyKnownIdsPreservingOrder()
    {
        var result = SettingsValidation.FilterHiddenCalendarIds(
            new[] { "60717", "99999", "38262" },
            new[] { "38262", "60717" });

        Assert.Equal(new[] { "60717", "38262" }, result);
    }

    [Fact]
    public void FilterHiddenCalendarIds_RemovesDuplicatesAndHandlesNull()
    {
        var result = SettingsValidation.FilterHiddenCalendarIds(new[] { "123", "123" }, new[] { "123" });
        Assert.Equal(new[] { "123" }, result);

        var nullHiddenResult = SettingsValidation.FilterHiddenCalendarIds(null, new[] { "123" });
        Assert.Empty(nullHiddenResult);

        var emptyVisibleResult = SettingsValidation.FilterHiddenCalendarIds(new[] { "123" }, Array.Empty<string>());
        Assert.Empty(emptyVisibleResult);
    }

    [Fact]
    public void FilterHiddenCalendarIds_UsesOrdinalComparison()
    {
        var result = SettingsValidation.FilterHiddenCalendarIds(new[] { "ABC" }, new[] { "abc" });

        Assert.Empty(result);
    }
}
