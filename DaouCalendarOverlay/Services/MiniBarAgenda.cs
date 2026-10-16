using DaouCalendarOverlay.Models;

namespace DaouCalendarOverlay.Services;

/// <summary>
/// 최소화 막대의 오늘 일정 선택·순환을 맡는 순수 로직.
/// 시계는 인자로만 받고 WPF 타입을 참조하지 않는다.
/// </summary>
public static class MiniBarAgenda
{
    public const int VisibleLineCount = 2;
    public const string NoRemainingText = "남은 일정 없음";

    /// <summary>종일 일정은 항상 false다. 시간 일정은 max(StartTime, EndTime) &lt;= now이면 true다(DateTimeOffset 순간 비교).</summary>
    public static bool HasEnded(DaouCalendarEvent e, DateTimeOffset now)
    {
        if (e.IsAllDay)
            return false;

        var end = e.StartTime > e.EndTime ? e.StartTime : e.EndTime;
        return end <= now;
    }

    /// <summary>
    /// 입력 순서를 보존하고 끝난 항목만 뺀다. 입력은 이미 종일 먼저, 시작 시각 순이므로 다시 정렬하지 않는다.
    /// </summary>
    public static IReadOnlyList<DaouCalendarEvent> SelectRemaining(IReadOnlyList<DaouCalendarEvent> todaysVisible, DateTimeOffset now)
    {
        var result = new List<DaouCalendarEvent>(todaysVisible.Count);
        foreach (var e in todaysVisible)
        {
            if (!HasEnded(e, now))
                result.Add(e);
        }

        return result;
    }

    public static string KeyOf(DaouCalendarEvent e) => $"{e.Id}|{e.CalendarId}|{e.StartTime.UtcTicks}";

    public static bool ShouldRotate(int count) => count > VisibleLineCount;

    /// <summary>
    /// count가 보이는 줄 수 이하면 0..count-1을 돌려준다.
    /// 아니면 offset부터 순환하는 min(rows, count)개의 인덱스를 돌려준다.
    /// </summary>
    public static IReadOnlyList<int> GetWindow(int count, int offset, int rows)
    {
        if (count <= 0 || rows <= 0)
            return Array.Empty<int>();

        if (!ShouldRotate(count))
        {
            var all = new int[count];
            for (var i = 0; i < count; i++)
                all[i] = i;
            return all;
        }

        var start = Normalize(offset, count);
        var length = Math.Min(rows, count);
        var window = new int[length];
        for (var i = 0; i < length; i++)
            window[i] = (start + i) % count;
        return window;
    }

    /// <summary>회전하지 않으면 0, 회전하면 (offset + 1) % count.</summary>
    public static int NextOffset(int count, int offset)
    {
        if (!ShouldRotate(count))
            return 0;

        return (Normalize(offset, count) + 1) % count;
    }

    /// <summary>
    /// 이전 맨 윗줄 키가 newKeys에 있으면 그 인덱스를, 없으면 이전 순서에서 previousOffset+1부터
    /// 순환하며 newKeys에 남아 있는 첫 키의 인덱스를 돌려준다. 못 찾거나 회전하지 않는 개수면 0이다.
    /// </summary>
    public static int ReconcileOffset(IReadOnlyList<string> previousKeys, int previousOffset, IReadOnlyList<string> newKeys)
    {
        if (!ShouldRotate(newKeys.Count))
            return 0;
        if (previousKeys.Count == 0 || previousOffset < 0 || previousOffset >= previousKeys.Count)
            return 0;

        var top = IndexOf(newKeys, previousKeys[previousOffset]);
        if (top >= 0)
            return top;

        for (var i = 1; i < previousKeys.Count; i++)
        {
            var index = IndexOf(newKeys, previousKeys[(previousOffset + i) % previousKeys.Count]);
            if (index >= 0)
                return index;
        }

        return 0;
    }

    private static int Normalize(int offset, int count) => ((offset % count) + count) % count;

    private static int IndexOf(IReadOnlyList<string> keys, string key)
    {
        for (var i = 0; i < keys.Count; i++)
        {
            if (string.Equals(keys[i], key, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }
}
