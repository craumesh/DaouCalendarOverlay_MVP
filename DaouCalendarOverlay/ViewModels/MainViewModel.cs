using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;
using DaouCalendarOverlay.Models;

namespace DaouCalendarOverlay.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private static readonly MediaColor[] EventPalette =
    {
        MediaColor.FromRgb(0x5B, 0x8D, 0xE8),
        MediaColor.FromRgb(0x69, 0xB5, 0x88),
        MediaColor.FromRgb(0xB2, 0x83, 0xE6),
        MediaColor.FromRgb(0xE0, 0x8A, 0x68),
        MediaColor.FromRgb(0x4E, 0xB5, 0xB8),
        MediaColor.FromRgb(0xD0, 0x72, 0x9E),
        MediaColor.FromRgb(0xB9, 0x9A, 0x55),
        MediaColor.FromRgb(0x77, 0x9D, 0xCF)
    };

    private List<DaouCalendarEvent> _allEvents = new();
    private List<DaouCalendarEvent> _events = new();
    private readonly HashSet<string> _hiddenCalendarIds = new(StringComparer.Ordinal);
    private List<string> _configuredCalendarIds = new();
    private string _filterField = "Title";
    private string _filterText = "";
    private DateTime _displayMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private string _statusText = "준비 중";
    private bool _statusIsError;
    private bool _loginRequired;

    public ObservableCollection<DayCellViewModel> Days { get; } = new();
    public string MonthTitle => _displayMonth.ToString("yyyy년 M월");
    public DateTime DisplayMonth => _displayMonth;
    public string StatusText { get => _statusText; private set { _statusText = value; OnPropertyChanged(); } }
    public bool StatusIsError { get => _statusIsError; private set { _statusIsError = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusForeground)); } }
    public MediaBrush StatusForeground => StatusIsError ? CreateBrush(MediaColor.FromRgb(0xFF, 0x9C, 0x9C)) : CreateBrush(MediaColor.FromRgb(0x9E, 0xA3, 0xAD));
    public bool LoginRequired { get => _loginRequired; private set { _loginRequired = value; OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel()
    {
        BuildCalendar();
    }

    public void SetConfiguredCalendars(IEnumerable<string> calendarIds)
    {
        _configuredCalendarIds = calendarIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public void SetHiddenCalendars(IEnumerable<string> calendarIds)
    {
        _hiddenCalendarIds.Clear();
        foreach (var id in calendarIds.Where(id => !string.IsNullOrWhiteSpace(id)))
            _hiddenCalendarIds.Add(id);
        ApplyFilter();
    }

    public IReadOnlyList<CalendarDescriptor> GetCalendarDescriptors()
    {
        var seen = new List<string>();
        var names = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var calendarEvent in _allEvents)
        {
            var ids = calendarEvent.CalendarIds;
            var calendarNames = calendarEvent.CalendarNames;

            for (var i = 0; i < ids.Count; i++)
            {
                var id = ids[i];
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                if (!names.ContainsKey(id) && !seen.Contains(id, StringComparer.Ordinal))
                    seen.Add(id);

                if (names.ContainsKey(id))
                    continue;

                var name = i < calendarNames.Count ? calendarNames[i] : null;
                if (!string.IsNullOrWhiteSpace(name))
                    names[id] = name!;
            }
        }

        var allIds = _configuredCalendarIds
            .Concat(seen)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return allIds
            .Select(id => new CalendarDescriptor
            {
                Id = id,
                Name = names.TryGetValue(id, out var name) ? name : id
            })
            .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .ToList();
    }

    public bool IsCalendarVisible(string calendarId) => !_hiddenCalendarIds.Contains(calendarId);

    // 여러 캘린더에 공유된 일정은 소속 캘린더가 모두 숨김일 때만 숨긴다.
    private bool IsHiddenEvent(DaouCalendarEvent calendarEvent)
    {
        var ids = calendarEvent.CalendarIds;
        if (ids.Count == 0)
            return false;

        for (var i = 0; i < ids.Count; i++)
        {
            if (!_hiddenCalendarIds.Contains(ids[i]))
                return false;
        }

        return true;
    }

    public void SetEvents(IEnumerable<DaouCalendarEvent> events)
    {
        _allEvents = EventDeduplicator.Deduplicate(events).OrderBy(e => e.StartTime).ToList();
        ApplyFilter();
    }

    public void SetFilter(string field, string? text)
    {
        var nextField = string.IsNullOrWhiteSpace(field) ? "Title" : field;
        var nextText = text?.Trim() ?? "";

        if (string.Equals(_filterField, nextField, StringComparison.Ordinal) &&
            string.Equals(_filterText, nextText, StringComparison.Ordinal))
            return;

        _filterField = nextField;
        _filterText = nextText;
        ApplyFilter();
    }

    public void ClearFilter()
    {
        _filterField = "Title";
        _filterText = "";
        ApplyFilter();
    }

    public void MoveMonth(int delta)
    {
        _displayMonth = _displayMonth.AddMonths(delta);
        OnPropertyChanged(nameof(MonthTitle));
        OnPropertyChanged(nameof(DisplayMonth));
        BuildCalendar();
    }

    public void GoToday()
    {
        _displayMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        OnPropertyChanged(nameof(MonthTitle));
        OnPropertyChanged(nameof(DisplayMonth));
        BuildCalendar();
    }

    public void SetStatus(string text, bool isError)
    {
        StatusText = text;
        StatusIsError = isError;
    }

    public void SetLoginRequired(bool required) => LoginRequired = required;

    public IReadOnlyList<DaouCalendarEvent> GetEventsForDate(DateTime date) =>
        _events
            .Where(e => CalendarGrid.OccursOnDate(e, date.Date))
            .OrderByDescending(e => e.IsAllDay)
            .ThenBy(e => e.StartTime)
            .ToList();

    public IReadOnlyList<EventChipViewModel> GetEventChipsForDate(DateTime date) =>
        GetEventsForDate(date)
            .Select(CreateEventChip)
            .ToList();

    public (DateTimeOffset From, DateTimeOffset To) GetVisibleRange() => CalendarGrid.GetVisibleRange(_displayMonth);

    private void BuildCalendar()
    {
        Days.Clear();
        var gridStart = CalendarGrid.GetGridStart(_displayMonth);

        for (var i = 0; i < CalendarGrid.CellCount; i++)
        {
            var date = gridStart.AddDays(i);
            var dayEvents = GetEventsForDate(date);
            var cell = new DayCellViewModel
            {
                Date = date,
                IsCurrentMonth = date.Month == _displayMonth.Month && date.Year == _displayMonth.Year,
                IsToday = date.Date == DateTime.Today,
                // 제목 검색은 공휴일 색상을 숨기지 않지만, 캘린더 자체를 끈 경우에는 해당 캘린더의 효과도 숨긴다.
                IsHoliday = GetVisibleUnfilteredEventsForDate(date).Any(IsHolidayEvent)
            };

            // 일정이 3개 이상이면 첫 일정만 표시하고 두 번째 줄은 ... 전용으로 사용한다.
            var visibleCount = dayEvents.Count >= 3 ? 1 : dayEvents.Count;
            foreach (var calendarEvent in dayEvents.Take(visibleCount))
                cell.VisibleEvents.Add(CreateEventChip(calendarEvent));

            cell.MoreCount = dayEvents.Count >= 3 ? dayEvents.Count - 1 : 0;
            cell.NotifyAll();
            Days.Add(cell);
        }
    }

    private void ApplyFilter()
    {
        IEnumerable<DaouCalendarEvent> query = _allEvents
            .Where(e => !IsHiddenEvent(e));

        if (!string.IsNullOrWhiteSpace(_filterText))
            query = query.Where(MatchesFilter);

        _events = query.OrderBy(e => e.StartTime).ToList();
        BuildCalendar();
    }

    private bool MatchesFilter(DaouCalendarEvent calendarEvent)
    {
        var needle = _filterText;
        if (string.IsNullOrWhiteSpace(needle))
            return true;

        return _filterField switch
        {
            "Owner" => Contains(calendarEvent.OwnerOrCreatorDisplay, needle) ||
                       Contains(calendarEvent.Creator?.Name, needle) ||
                       Contains(calendarEvent.Creator?.Email, needle),
            "Audience" => Contains(calendarEvent.AttendeeDisplay, needle) ||
                          calendarEvent.Attendees.Any(a => Contains(a.Name, needle) || Contains(a.Email, needle)),
            _ => Contains(calendarEvent.Summary, needle)
        };
    }

    private IReadOnlyList<DaouCalendarEvent> GetVisibleUnfilteredEventsForDate(DateTime date) =>
        _allEvents
            .Where(e => !IsHiddenEvent(e))
            .Where(e => CalendarGrid.OccursOnDate(e, date.Date))
            .OrderByDescending(e => e.IsAllDay)
            .ThenBy(e => e.StartTime)
            .ToList();

    private static bool Contains(string? source, string needle) =>
        !string.IsNullOrWhiteSpace(source) &&
        source.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static EventChipViewModel CreateEventChip(DaouCalendarEvent calendarEvent)
    {
        var accent = GetEventColor(calendarEvent);
        return new EventChipViewModel
        {
            Event = calendarEvent,
            Text = calendarEvent.IsAllDay
                ? "종일"
                : $"{calendarEvent.StartTime:HH:mm}",
            Tooltip = BuildTooltip(calendarEvent),
            AccentBrush = CreateBrush(accent),
            ChipBackground = CreateBrush(MediaColor.FromArgb(0x24, accent.R, accent.G, accent.B))
        };
    }

    private static bool IsHolidayEvent(DaouCalendarEvent e) =>
        string.Equals(e.Type, "holiday", StringComparison.OrdinalIgnoreCase);

    private static string BuildTooltip(DaouCalendarEvent e)
    {
        var time = e.IsAllDay
            ? "종일"
            : $"{e.StartTime:yyyy-MM-dd HH:mm} ~ {e.EndTime:HH:mm}";

        var parts = new List<string>
        {
            e.Summary,
            $"[{e.CalendarDisplayName}] {time}"
        };

        if (!string.IsNullOrWhiteSpace(e.Location))
            parts.Add($"장소: {e.Location}");
        if (!string.IsNullOrWhiteSpace(e.Description))
            parts.Add(e.Description);

        return string.Join(Environment.NewLine, parts);
    }

    private static MediaColor GetEventColor(DaouCalendarEvent e)
    {
        if (int.TryParse(e.Color, out var index))
            return EventPalette[Math.Abs(index) % EventPalette.Length];

        var hash = StringComparer.Ordinal.GetHashCode(e.CalendarId ?? string.Empty);
        return EventPalette[(hash & 0x7fffffff) % EventPalette.Length];
    }

    private static MediaSolidColorBrush CreateBrush(MediaColor color)
    {
        var brush = new MediaSolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
