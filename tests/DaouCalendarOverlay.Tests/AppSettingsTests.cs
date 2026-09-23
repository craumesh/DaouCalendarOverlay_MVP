using DaouCalendarOverlay.Models;

namespace DaouCalendarOverlay.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void IsConfigured_HttpsSubdomainWithCalendarIds_IsTrue()
    {
        var settings = new AppSettings
        {
            BaseUrl = "https://company.daouoffice.com",
            CalendarIds = new List<string> { "38262" }
        };

        Assert.True(settings.IsConfigured);
    }

    [Fact]
    public void IsConfigured_HttpScheme_IsFalse()
    {
        var settings = new AppSettings
        {
            BaseUrl = "http://company.daouoffice.com",
            CalendarIds = new List<string> { "38262" }
        };

        Assert.False(settings.IsConfigured);
    }

    [Fact]
    public void IsConfigured_ApexHost_IsFalse()
    {
        var settings = new AppSettings
        {
            BaseUrl = "https://daouoffice.com",
            CalendarIds = new List<string> { "38262" }
        };

        Assert.False(settings.IsConfigured);
    }

    [Fact]
    public void IsConfigured_WithoutCalendarIds_IsFalse()
    {
        var settings = new AppSettings
        {
            BaseUrl = "https://company.daouoffice.com",
            CalendarIds = new List<string>()
        };

        Assert.False(settings.IsConfigured);
    }

    [Fact]
    public void RegisterEdge_DefaultsToTrue()
    {
        Assert.True(new AppSettings().RegisterEdge);
    }

    [Fact]
    public void Clone_CopiesRegisterEdge()
    {
        var settings = new AppSettings { RegisterEdge = false };

        Assert.False(settings.Clone().RegisterEdge);
    }

    [Fact]
    public void CalendarNames_DefaultsToEmptyDictionary()
    {
        var settings = new AppSettings();

        Assert.NotNull(settings.CalendarNames);
        Assert.Empty(settings.CalendarNames);
    }

    [Fact]
    public void Clone_CopiesCalendarNamesIndependently()
    {
        var settings = new AppSettings();
        settings.CalendarNames["123"] = "팀 캘린더";

        var clone = settings.Clone();
        settings.CalendarNames["456"] = "다른 캘린더";
        settings.CalendarNames["123"] = "바뀐 이름";

        Assert.NotSame(settings.CalendarNames, clone.CalendarNames);
        var entry = Assert.Single(clone.CalendarNames);
        Assert.Equal("123", entry.Key);
        Assert.Equal("팀 캘린더", entry.Value);
    }

    [Fact]
    public void Clone_NullCalendarNames_ProducesEmptyDictionary()
    {
        var settings = new AppSettings { CalendarNames = null! };

        var clone = settings.Clone();

        Assert.NotNull(clone.CalendarNames);
        Assert.Empty(clone.CalendarNames);
    }

    /// <summary>settings.json 기존 키 이름은 바꾸지 않는다(설계 3절 #4). 새 키 CalendarNames는 추가만 한다.</summary>
    [Fact]
    public void PublicPropertyNames_KeepExistingSettingsKeys()
    {
        var names = typeof(AppSettings).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var key in new[]
                 {
                     "BaseUrl", "CalendarIds", "HiddenCalendarIds", "RefreshMinutes", "StartWithWindows",
                     "AlwaysOnTop", "PositionLocked", "RegisterEdge", "UiOpacity", "Left", "Top", "CalendarNames"
                 })
        {
            Assert.Contains(key, names);
        }
    }
}
