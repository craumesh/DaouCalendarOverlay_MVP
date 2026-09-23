namespace DaouCalendarOverlay.Models;

/// <summary>
/// 같은 <see cref="DaouCalendarEvent.Id"/>가 서로 다른 캘린더로 여러 번 내려온 이벤트를
/// 1건으로 병합하고 소속 캘린더 ID/이름을 보존하는 순수 로직.
/// </summary>
public static class EventDeduplicator
{
    public static IReadOnlyList<DaouCalendarEvent> Deduplicate(IEnumerable<DaouCalendarEvent> events)
    {
        if (events is null)
            return Array.Empty<DaouCalendarEvent>();

        var result = new List<DaouCalendarEvent>();
        var groups = new Dictionary<string, (List<string> Ids, List<string> Names)>(StringComparer.Ordinal);

        foreach (var e in events)
        {
            if (e is null)
                continue;

            if (string.IsNullOrWhiteSpace(e.Id))
            {
                result.Add(e);
                continue;
            }

            var eventIds = e.CalendarIds;
            var eventNames = e.CalendarNames;

            if (!groups.TryGetValue(e.Id, out var membership))
            {
                membership = (new List<string>(), new List<string>());
                groups[e.Id] = membership;
                result.Add(e);
            }

            for (var i = 0; i < eventIds.Count; i++)
            {
                var id = eventIds[i];
                var name = i < eventNames.Count ? eventNames[i] : "";

                if (membership.Ids.Contains(id, StringComparer.Ordinal))
                    continue;

                membership.Ids.Add(id);
                membership.Names.Add(name);
            }
        }

        foreach (var rep in result)
        {
            if (string.IsNullOrWhiteSpace(rep.Id))
                continue;

            var membership = groups[rep.Id];
            rep.CalendarIds = membership.Ids;
            rep.CalendarNames = membership.Names;
        }

        return result;
    }
}
