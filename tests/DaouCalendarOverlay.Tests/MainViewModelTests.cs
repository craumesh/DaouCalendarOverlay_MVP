using DaouCalendarOverlay.Models;
using DaouCalendarOverlay.ViewModels;

namespace DaouCalendarOverlay.Tests;

public sealed class MainViewModelTests
{
    private static readonly TimeSpan Kst = TimeSpan.FromHours(9);

    private static DaouCalendarEvent Ev(string timeType, DateTimeOffset start, DateTimeOffset end, string type = "normal") =>
        new DaouCalendarEvent
        {
            Id = Guid.NewGuid().ToString(),
            CalendarId = "cal-1",
            CalendarName = "테스트 캘린더",
            TimeType = timeType,
            Type = type,
            Visibility = "public",
            StartTime = start,
            EndTime = end
        };

    private static DaouCalendarEvent Shared(string id, string calendarId, string calendarName, DateTimeOffset start, DateTimeOffset end) =>
        new DaouCalendarEvent
        {
            Id = id,
            CalendarId = calendarId,
            CalendarName = calendarName,
            TimeType = "timed",
            Type = "normal",
            Visibility = "public",
            StartTime = start,
            EndTime = end
        };

    [Fact]
    public void Constructor_Builds42DayGrid()
    {
        var vm = new MainViewModel();

        Assert.Equal(42, vm.Days.Count);
    }

    [Fact]
    public void GetVisibleRange_DelegatesToCalendarGridForCurrentMonth()
    {
        var month = new DateTime(2026, 9, 1);
        var vm = new MainViewModel(() => new DateTime(2026, 9, 22));

        Assert.Equal(CalendarGrid.GetVisibleRange(month), vm.GetVisibleRange());
    }

    [Fact]
    public void MoveMonth_ShiftsVisibleRangeByThatManyMonths()
    {
        var month = new DateTime(2026, 9, 1);
        var vm = new MainViewModel(() => new DateTime(2026, 9, 22));

        vm.MoveMonth(1);
        Assert.Equal(CalendarGrid.GetVisibleRange(month.AddMonths(1)), vm.GetVisibleRange());

        vm.MoveMonth(-2);
        Assert.Equal(CalendarGrid.GetVisibleRange(month.AddMonths(-1)), vm.GetVisibleRange());
    }

    [Fact]
    public void GetEventsForDate_ReturnsAllDayHolidayOnItsOwnDay()
    {
        var vm = new MainViewModel();
        var holiday = Ev(
            "allday",
            new DateTimeOffset(2026, 9, 25, 0, 0, 0, Kst),
            new DateTimeOffset(2026, 9, 25, 23, 59, 59, 999, Kst),
            type: "holiday");

        vm.SetEvents(new[] { holiday });

        Assert.Single(vm.GetEventsForDate(new DateTime(2026, 9, 25)));
        Assert.Empty(vm.GetEventsForDate(new DateTime(2026, 9, 26)));
    }

    [Fact]
    public void GetEventsForDate_OrdersAllDayBeforeTimed()
    {
        var vm = new MainViewModel();
        var timed = Ev(
            "timed",
            new DateTimeOffset(2026, 9, 25, 9, 0, 0, Kst),
            new DateTimeOffset(2026, 9, 25, 10, 0, 0, Kst));
        var allDay = Ev(
            "allday",
            new DateTimeOffset(2026, 9, 25, 0, 0, 0, Kst),
            new DateTimeOffset(2026, 9, 25, 23, 59, 59, 999, Kst));

        vm.SetEvents(new[] { timed, allDay });

        var events = vm.GetEventsForDate(new DateTime(2026, 9, 25));
        Assert.True(events[0].IsAllDay);
    }

    [Fact]
    public void SetEvents_SameIdInTwoCalendars_ShowsEventOnceForDate()
    {
        var today = DateTime.Today;
        var start = new DateTimeOffset(today.Year, today.Month, today.Day, 9, 0, 0, Kst);
        var end = new DateTimeOffset(today.Year, today.Month, today.Day, 10, 0, 0, Kst);
        var vm = new MainViewModel();
        var first = Shared("38262", "38262", "내 캘린더", start, end);
        var second = Shared("38262", "60717", "팀 캘린더", start, end);

        vm.SetEvents(new[] { first, second });

        var eventsForDate = vm.GetEventsForDate(today);
        Assert.Single(eventsForDate);
        Assert.Equal("내 캘린더, 팀 캘린더", eventsForDate[0].CalendarDisplayName);
    }

    [Fact]
    public void SetHiddenCalendars_HidesSharedEventOnlyWhenAllCalendarsHidden()
    {
        var today = DateTime.Today;
        var start = new DateTimeOffset(today.Year, today.Month, today.Day, 9, 0, 0, Kst);
        var end = new DateTimeOffset(today.Year, today.Month, today.Day, 10, 0, 0, Kst);
        var vm = new MainViewModel();
        var first = Shared("38262", "38262", "내 캘린더", start, end);
        var second = Shared("38262", "60717", "팀 캘린더", start, end);
        vm.SetEvents(new[] { first, second });

        vm.SetHiddenCalendars(new[] { "60717" });
        Assert.Single(vm.GetEventsForDate(today));

        vm.SetHiddenCalendars(new[] { "38262", "60717" });
        Assert.Empty(vm.GetEventsForDate(today));
    }

    [Fact]
    public void GetCalendarDescriptors_IncludesEveryCalendarOfSharedEvent()
    {
        var today = DateTime.Today;
        var start = new DateTimeOffset(today.Year, today.Month, today.Day, 9, 0, 0, Kst);
        var end = new DateTimeOffset(today.Year, today.Month, today.Day, 10, 0, 0, Kst);
        var vm = new MainViewModel();
        var first = Shared("38262", "38262", "내 캘린더", start, end);
        var second = Shared("38262", "60717", "팀 캘린더", start, end);
        vm.SetEvents(new[] { first, second });

        var descriptors = vm.GetCalendarDescriptors();

        var byInner = descriptors.ToDictionary(d => d.Id, d => d.Name);
        Assert.Equal("내 캘린더", byInner["38262"]);
        Assert.Equal("팀 캘린더", byInner["60717"]);
    }

    [Fact]
    public void BuildCalendar_DuplicateIdsDoNotInflateMoreCount()
    {
        var today = DateTime.Today;
        var start = new DateTimeOffset(today.Year, today.Month, today.Day, 9, 0, 0, Kst);
        var end = new DateTimeOffset(today.Year, today.Month, today.Day, 10, 0, 0, Kst);
        var vm = new MainViewModel();
        var first = Shared("38262", "38262", "내 캘린더", start, end);
        var second = Shared("38262", "60717", "팀 캘린더", start, end);
        var other = Shared("99999", "cal-9", "다른 캘린더", start, end);
        vm.SetEvents(new[] { first, second, other });

        var cell = vm.Days.First(d => d.Date == DateTime.Today);

        Assert.Equal(0, cell.MoreCount);
        Assert.Equal(2, cell.VisibleEvents.Count);
    }

    [Fact]
    public void Constructor_UsesInjectedTodayForDisplayMonth()
    {
        var vm = new MainViewModel(() => new DateTime(2026, 9, 22));

        Assert.Equal(new DateTime(2026, 9, 1), vm.DisplayMonth);
        Assert.Equal("2026년 9월", vm.MonthTitle);
        Assert.Equal(new DateTime(2026, 9, 22), Assert.Single(vm.Days, d => d.IsToday).Date);
    }

    [Fact]
    public void GoToday_UsesInjectedToday()
    {
        var vm = new MainViewModel(() => new DateTime(2026, 9, 22));

        vm.MoveMonth(3);
        vm.GoToday();

        Assert.Equal(new DateTime(2026, 9, 1), vm.DisplayMonth);
    }

    [Fact]
    public void RefreshTodayIfChanged_ReturnsFalseWhenDateUnchanged()
    {
        var vm = new MainViewModel(() => new DateTime(2026, 9, 22));

        Assert.False(vm.RefreshTodayIfChanged());
        Assert.Equal(42, vm.Days.Count);
        Assert.Equal(new DateTime(2026, 9, 1), vm.DisplayMonth);
    }

    [Fact]
    public void RefreshTodayIfChanged_MovesTodayBadgeWithinSameMonth()
    {
        var today = new DateTime(2026, 9, 22);
        var vm = new MainViewModel(() => today);

        today = new DateTime(2026, 9, 23);

        Assert.True(vm.RefreshTodayIfChanged());
        Assert.Equal(new DateTime(2026, 9, 1), vm.DisplayMonth);
        Assert.Equal(new DateTime(2026, 9, 23), Assert.Single(vm.Days, d => d.IsToday).Date);
    }

    [Fact]
    public void RefreshTodayIfChanged_GoesToNewMonthWhenViewingCurrentMonth()
    {
        var today = new DateTime(2026, 9, 30);
        var vm = new MainViewModel(() => today);

        today = new DateTime(2026, 10, 1);

        Assert.True(vm.RefreshTodayIfChanged());
        Assert.Equal(new DateTime(2026, 10, 1), vm.DisplayMonth);
        Assert.Equal("2026년 10월", vm.MonthTitle);
        Assert.Equal(new DateTime(2026, 10, 1), Assert.Single(vm.Days, d => d.IsToday).Date);
    }

    [Fact]
    public void RefreshTodayIfChanged_KeepsDisplayMonthWhenUserBrowsedAnotherMonth()
    {
        var today = new DateTime(2026, 9, 30);
        var vm = new MainViewModel(() => today);
        vm.MoveMonth(-1);

        today = new DateTime(2026, 10, 1);

        Assert.True(vm.RefreshTodayIfChanged());
        Assert.Equal(new DateTime(2026, 8, 1), vm.DisplayMonth);
        Assert.DoesNotContain(vm.Days, d => d.IsToday);
    }

    [Fact]
    public void RefreshTodayIfChanged_ReturnsTrueOnlyOnceWhenTicksCrossMidnight()
    {
        var today = new DateTime(2026, 9, 30);
        var vm = new MainViewModel(() => today);

        var trueCount = 0;
        for (var i = 0; i < 30; i++)
            if (vm.RefreshTodayIfChanged()) trueCount++;

        today = new DateTime(2026, 10, 1);
        for (var i = 0; i < 30; i++)
            if (vm.RefreshTodayIfChanged()) trueCount++;

        Assert.Equal(1, trueCount);
        Assert.Equal(new DateTime(2026, 10, 1), vm.DisplayMonth);
        Assert.Equal(new DateTime(2026, 10, 1), Assert.Single(vm.Days, d => d.IsToday).Date);
    }

    [Fact]
    public void SetLastUpdated_FormatsStatusToolTipUsingValueOffset()
    {
        var vm = new MainViewModel(() => new DateTime(2026, 9, 22));

        vm.SetLastUpdated(new DateTimeOffset(2026, 9, 21, 8, 30, 0, Kst));

        Assert.Equal("마지막 갱신 2026-09-21 08:30", vm.StatusToolTip);
    }

    [Fact]
    public void SetLastUpdated_WithNull_ClearsStatusToolTip()
    {
        var vm = new MainViewModel(() => new DateTime(2026, 9, 22));
        vm.SetLastUpdated(new DateTimeOffset(2026, 9, 21, 8, 30, 0, Kst));

        vm.SetLastUpdated(null);

        Assert.Null(vm.StatusToolTip);
    }

    [Fact]
    public void SetLastUpdated_RaisesPropertyChangedForStatusToolTip()
    {
        var vm = new MainViewModel(() => new DateTime(2026, 9, 22));
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.StatusToolTip))
                raised = true;
        };

        vm.SetLastUpdated(new DateTimeOffset(2026, 9, 21, 8, 30, 0, Kst));

        Assert.True(raised);
    }
}
