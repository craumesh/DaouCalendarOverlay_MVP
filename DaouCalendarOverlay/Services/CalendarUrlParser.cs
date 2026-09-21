namespace DaouCalendarOverlay.Services;

public enum CalendarUrlParseStatus
{
    Success,
    InvalidUrl,
    NoCalendarIds
}

public sealed class CalendarUrlParseResult
{
    public CalendarUrlParseStatus Status { get; init; }
    public string BaseUrl { get; init; } = "";
    public IReadOnlyList<string> CalendarIds { get; init; } = new List<string>();
    public bool IsSuccess => Status == CalendarUrlParseStatus.Success;

    public static CalendarUrlParseResult Failure(CalendarUrlParseStatus status) => new() { Status = status };
}

public static class CalendarUrlParser
{
    public const string CalendarIdsQueryKey = "calendarIds[]";

    public static CalendarUrlParseResult Parse(string? rawUrl)
    {
        var raw = (rawUrl ?? string.Empty).Trim();
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri))
            return CalendarUrlParseResult.Failure(CalendarUrlParseStatus.InvalidUrl);

        var ids = new List<string>();
        var query = uri.Query.TrimStart('?');
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0].Replace('+', ' '));
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : "";
            if (string.Equals(key, CalendarIdsQueryKey, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
                ids.Add(value.Trim());
        }

        if (ids.Count == 0)
            return CalendarUrlParseResult.Failure(CalendarUrlParseStatus.NoCalendarIds);

        return new CalendarUrlParseResult
        {
            Status = CalendarUrlParseStatus.Success,
            BaseUrl = $"{uri.Scheme}://{uri.Authority}",
            CalendarIds = ids.Distinct().ToList()
        };
    }
}
