using System.Text.Json.Serialization;

namespace DaouCalendarOverlay.Models;

public sealed class AppSettings
{
    public string BaseUrl { get; set; } = "";
    public List<string> CalendarIds { get; set; } = new();
    public List<string> HiddenCalendarIds { get; set; } = new();
    public int RefreshMinutes { get; set; } = 5;
    public bool StartWithWindows { get; set; } = true;
    public bool AlwaysOnTop { get; set; } = false;
    public bool PositionLocked { get; set; } = false;
    public double UiOpacity { get; set; } = 1.0;
    public double? Left { get; set; }
    public double? Top { get; set; }

    [JsonIgnore]
    public bool IsConfigured =>
        Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
        CalendarIds.Count > 0;

    public AppSettings Clone() => new()
    {
        BaseUrl = BaseUrl,
        CalendarIds = new List<string>(CalendarIds),
        HiddenCalendarIds = new List<string>(HiddenCalendarIds),
        RefreshMinutes = RefreshMinutes,
        StartWithWindows = StartWithWindows,
        AlwaysOnTop = AlwaysOnTop,
        PositionLocked = PositionLocked,
        UiOpacity = UiOpacity,
        Left = Left,
        Top = Top
    };
}
