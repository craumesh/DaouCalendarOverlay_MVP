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
}
