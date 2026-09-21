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
}
