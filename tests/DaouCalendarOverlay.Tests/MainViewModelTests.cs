using DaouCalendarOverlay.Models;
using DaouCalendarOverlay.ViewModels;

namespace DaouCalendarOverlay.Tests;

public sealed class MainViewModelTests
{
    private static readonly TimeSpan Kst = TimeSpan.FromHours(9);

    private static void RunSta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { body(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            throw new InvalidOperationException("STA 스레드에서 테스트 본문이 실패했습니다.", failure);
    }

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
        var today = DateTime.Today;
        var month = new DateTime(today.Year, today.Month, 1);
        var vm = new MainViewModel();

        Assert.Equal(CalendarGrid.GetVisibleRange(month), vm.GetVisibleRange());
    }

    [Fact]
    public void MoveMonth_ShiftsVisibleRangeByThatManyMonths()
    {
        var today = DateTime.Today;
        var month = new DateTime(today.Year, today.Month, 1);
        var vm = new MainViewModel();

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
}
