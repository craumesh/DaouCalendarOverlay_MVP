namespace DaouCalendarOverlay.Models;

/// <summary>
/// 42칸 달력 그리드의 날짜 계산과 일정-날짜 매칭. UI 비의존 순수 로직.
/// </summary>
public static class CalendarGrid
{
    public const int CellCount = 42;

    /// <summary>DaouOffice 일정 데이터의 기준 오프셋(+09:00).</summary>
    public static readonly TimeSpan KstOffset = TimeSpan.FromHours(9);

    public static DateTime GetGridStart(DateTime displayMonth)
    {
        var first = new DateTime(displayMonth.Year, displayMonth.Month, 1);
        return first.AddDays(-(int)first.DayOfWeek);
    }

    public static (DateTimeOffset From, DateTimeOffset To) GetVisibleRange(DateTime displayMonth)
    {
        var gridStart = GetGridStart(displayMonth);
        var gridEnd = gridStart.AddDays(CellCount).AddMilliseconds(-1);
        return (
            new DateTimeOffset(gridStart.Year, gridStart.Month, gridStart.Day, 0, 0, 0, KstOffset),
            new DateTimeOffset(gridEnd.Year, gridEnd.Month, gridEnd.Day, 23, 59, 59, 999, KstOffset)
        );
    }

    /// <summary>date는 시각 성분이 없는 날짜여야 한다(호출자가 .Date로 정규화).</summary>
    public static bool OccursOnDate(DaouCalendarEvent calendarEvent, DateTime date)
    {
        if (calendarEvent.IsAllDay)
        {
            var start = calendarEvent.StartTime.Date;
            var end = calendarEvent.EndTime.Date;
            if (end < start)
                end = start;
            return date >= start && date <= end;
        }

        var dayStart = new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, KstOffset);
        var dayEnd = dayStart.AddDays(1);
        return calendarEvent.StartTime < dayEnd && calendarEvent.EndTime > dayStart;
    }
}
