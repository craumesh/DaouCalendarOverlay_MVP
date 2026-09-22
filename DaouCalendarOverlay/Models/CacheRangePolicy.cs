namespace DaouCalendarOverlay.Models;

/// <summary>캐시에 저장된 조회 범위가 오늘(KST 기준)을 포함하는지 판정하는 순수 로직.</summary>
public static class CacheRangePolicy
{
    /// <summary>
    /// 저장된 범위가 오늘(KST)을 포함하면 true.
    /// 범위가 없거나(옛 캐시) 뒤집혀 있으면 false = "범위 밖"으로 취급한다.
    /// </summary>
    public static bool CoversToday(DateTimeOffset? rangeFrom, DateTimeOffset? rangeTo, DateTimeOffset now)
    {
        if (rangeFrom is not DateTimeOffset from || rangeTo is not DateTimeOffset to)
            return false;

        var fromDate = from.ToOffset(CalendarGrid.KstOffset).Date;
        var toDate = to.ToOffset(CalendarGrid.KstOffset).Date;
        if (toDate < fromDate)
            return false;

        var today = now.ToOffset(CalendarGrid.KstOffset).Date;
        return today >= fromDate && today <= toDate;
    }
}
