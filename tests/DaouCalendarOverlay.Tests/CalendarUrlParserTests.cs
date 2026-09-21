using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

public sealed class CalendarUrlParserTests
{
    [Fact]
    public void Parse_ExtractsBaseUrlAndCalendarIds()
    {
        var result = CalendarUrlParser.Parse(
            "https://daou.example.com/app/calendar/api/events?calendarIds[]=abc&calendarIds[]=def&from=2026-09-01T00:00:00%2B09:00");

        Assert.Equal(CalendarUrlParseStatus.Success, result.Status);
        Assert.Equal("https://daou.example.com", result.BaseUrl);
        Assert.Equal(new[] { "abc", "def" }, result.CalendarIds);
    }

    [Fact]
    public void Parse_PercentEncodedBracketKey_IsAlsoAccepted()
    {
        var result = CalendarUrlParser.Parse(
            "https://daou.example.com/app/calendar/api/events?calendarIds%5B%5D=abc&calendarIds%5B%5D=def&from=2026-09-01T00:00:00%2B09:00");

        Assert.Equal(new[] { "abc", "def" }, result.CalendarIds);
    }

    [Fact]
    public void Parse_KeyComparisonIsCaseInsensitive()
    {
        var result = CalendarUrlParser.Parse("https://daou.example.com/x?CALENDARIDS[]=abc");

        Assert.Equal(new[] { "abc" }, result.CalendarIds);
    }

    [Fact]
    public void Parse_PlusIsDecodedAsSpaceAndValueTrimmed()
    {
        var result = CalendarUrlParser.Parse("https://daou.example.com/x?calendarIds[]=a+b&calendarIds[]=%20c%20");

        Assert.Equal(new[] { "a b", "c" }, result.CalendarIds);
    }

    [Fact]
    public void Parse_DuplicateIdsAreDeduplicatedPreservingOrder()
    {
        var result = CalendarUrlParser.Parse("https://daou.example.com/x?calendarIds[]=a&calendarIds[]=b&calendarIds[]=a");

        Assert.Equal(new[] { "a", "b" }, result.CalendarIds);
    }

    [Fact]
    public void Parse_EmptyOrWhitespaceValuesAreSkipped()
    {
        var result = CalendarUrlParser.Parse("https://daou.example.com/x?calendarIds[]=&calendarIds[]=%20&calendarIds[]=a");

        Assert.Equal(new[] { "a" }, result.CalendarIds);
    }

    [Fact]
    public void Parse_KeepsNonDefaultPortInBaseUrl()
    {
        var result = CalendarUrlParser.Parse("https://daou.example.com:8443/x?calendarIds[]=a");

        Assert.Equal("https://daou.example.com:8443", result.BaseUrl);
    }

    [Fact]
    public void Parse_AcceptsHttpScheme()
    {
        var result = CalendarUrlParser.Parse("http://daou.example.com/x?calendarIds[]=a");

        Assert.Equal(CalendarUrlParseStatus.Success, result.Status);
        Assert.Equal("http://daou.example.com", result.BaseUrl);
    }

    [Fact]
    public void Parse_TrimsSurroundingWhitespaceOfInput()
    {
        var result = CalendarUrlParser.Parse("   https://daou.example.com/x?calendarIds[]=a   ");

        Assert.Equal(CalendarUrlParseStatus.Success, result.Status);
    }

    [Fact]
    public void Parse_WithoutCalendarIds_ReturnsNoCalendarIds()
    {
        var result = CalendarUrlParser.Parse("https://daou.example.com/x?from=2026-09-01");

        Assert.Equal(CalendarUrlParseStatus.NoCalendarIds, result.Status);
        Assert.Equal("", result.BaseUrl);
        Assert.Empty(result.CalendarIds);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("daou.example.com/x?calendarIds[]=a")]
    [InlineData(null)]
    public void Parse_InvalidInput_ReturnsInvalidUrl(string? input)
    {
        var result = CalendarUrlParser.Parse(input);

        Assert.Equal(CalendarUrlParseStatus.InvalidUrl, result.Status);
    }
}
