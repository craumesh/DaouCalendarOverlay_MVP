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

    /// <summary>
    /// settings.json 기존 키 이름은 바꾸지 않는다(설계 3절 #4). 새 키 CalendarNames(7.2.x)와
    /// OverlayMinimized·SnapToScreenEdges(7.4.0)는 추가만 한다. 기대 목록은 14개다.
    /// </summary>
    [Fact]
    public void PublicPropertyNames_KeepExistingSettingsKeys()
    {
        var names = typeof(AppSettings).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var expected = new[]
        {
            "BaseUrl", "CalendarIds", "HiddenCalendarIds", "RefreshMinutes", "StartWithWindows",
            "AlwaysOnTop", "PositionLocked", "RegisterEdge", "UiOpacity", "Left", "Top", "CalendarNames",
            "OverlayMinimized", "SnapToScreenEdges"
        };

        Assert.Equal(14, expected.Length);
        foreach (var key in expected)
        {
            Assert.Contains(key, names);
        }
    }

    /// <summary>키가 없는 옛 settings.json과 새 설정은 펼침 모드, 스냅 켬으로 시작한다.</summary>
    [Fact]
    public void Defaults_OverlayMinimizedFalse_SnapToScreenEdgesTrue()
    {
        var settings = new AppSettings();

        Assert.False(settings.OverlayMinimized);
        Assert.True(settings.SnapToScreenEdges);
    }

    /// <summary>
    /// Clone()이 새 키를 빠뜨리면 설정창 저장(SettingsWindow는 Clone 결과를 Result로 돌려준다)과
    /// 저장 스냅샷마다 최소화 상태·스냅 설정이 기본값으로 조용히 되돌아간다.
    /// </summary>
    [Fact]
    public void Clone_CopiesOverlayMinimizedAndSnap()
    {
        var original = new AppSettings { OverlayMinimized = true, SnapToScreenEdges = false };

        var clone = original.Clone();

        Assert.NotSame(original, clone);
        Assert.True(clone.OverlayMinimized);
        Assert.False(clone.SnapToScreenEdges);

        clone.OverlayMinimized = false;
        clone.SnapToScreenEdges = true;

        Assert.True(original.OverlayMinimized);
        Assert.False(original.SnapToScreenEdges);
    }

    /// <summary>JSON으로 저장되는 모든 키(=[JsonIgnore]가 아닌 쓰기 가능 속성)를 Clone()이 복사하는지 확인한다.</summary>
    [Fact]
    public void Clone_CopiesEverySerializedScalarKey()
    {
        var original = new AppSettings
        {
            BaseUrl = "https://x.daouoffice.com",
            RefreshMinutes = 17,
            StartWithWindows = false,
            AlwaysOnTop = true,
            PositionLocked = true,
            RegisterEdge = false,
            UiOpacity = 0.25,
            Left = 12.5,
            Top = -40.0,
            OverlayMinimized = true,
            SnapToScreenEdges = false
        };

        var clone = original.Clone();
        var defaults = new AppSettings();
        var compared = 0;

        foreach (var property in typeof(AppSettings).GetProperties())
        {
            // 컬렉션 키(List·Dictionary)는 별도 시험이 독립 복사를 확인한다. 계산 속성(IsConfigured)은 저장되지 않는다.
            var isCollection = property.PropertyType.IsGenericType &&
                               property.PropertyType.GetGenericTypeDefinition() != typeof(Nullable<>);
            if (!property.CanWrite || isCollection)
                continue;

            // 원본이 기본값과 같으면 Clone이 키를 빠뜨려도 시험이 통과하므로, 새 스칼라 키를 추가하면 위 초기화에도 넣어야 한다.
            Assert.NotEqual(property.GetValue(defaults), property.GetValue(original));
            Assert.Equal(property.GetValue(original), property.GetValue(clone));
            compared++;
        }

        Assert.Equal(11, compared);
    }
}
