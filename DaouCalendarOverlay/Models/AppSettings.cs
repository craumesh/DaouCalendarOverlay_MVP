using System.Text.Json.Serialization;
using DaouCalendarOverlay.Services;

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
    public bool RegisterEdge { get; set; } = true;
    public double UiOpacity { get; set; } = 1.0;
    public double? Left { get; set; }
    public double? Top { get; set; }

    /// <summary>캘린더 ID → 마지막으로 확인한 표시 이름. 키가 없는 옛 settings.json에서는 빈 사전이다.</summary>
    public Dictionary<string, string> CalendarNames { get; set; } = new();

    /// <summary>오버레이를 막대(최소화) 상태로 시작한다. 키가 없는 옛 settings.json에서는 false(펼침)다.</summary>
    public bool OverlayMinimized { get; set; } = false;

    /// <summary>창을 끌 때 작업 영역 가장자리에 붙인다. 키가 없는 옛 settings.json에서는 true다.</summary>
    public bool SnapToScreenEdges { get; set; } = true;

    [JsonIgnore]
    public bool IsConfigured =>
        BaseUrlPolicy.Validate(BaseUrl).IsValid &&
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
        RegisterEdge = RegisterEdge,
        UiOpacity = UiOpacity,
        Left = Left,
        Top = Top,
        // settings.json에 "CalendarNames": null이 들어 있어도 복제(설정 창·저장 스냅샷)가 실패하지 않게 한다.
        CalendarNames = CalendarNames is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(CalendarNames),
        OverlayMinimized = OverlayMinimized,
        SnapToScreenEdges = SnapToScreenEdges
    };
}
