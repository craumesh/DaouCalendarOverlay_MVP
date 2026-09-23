using System.Text.Json;
using System.Text.Json.Serialization;

namespace DaouCalendarOverlay.Models;

public sealed class DaouApiEnvelope
{
    [JsonPropertyName("code")]
    public JsonElement Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("data")]
    public List<DaouCalendarEvent> Data { get; set; } = new();
}

public sealed class DaouCalendarPerson
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("position")]
    public string? Position { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("photoPath")]
    public string? PhotoPath { get; set; }

    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            var baseName = !string.IsNullOrWhiteSpace(Name)
                ? Name!
                : !string.IsNullOrWhiteSpace(Email)
                    ? Email!
                    : !string.IsNullOrWhiteSpace(Id)
                        ? Id
                        : "알 수 없음";

            return !string.IsNullOrWhiteSpace(Position)
                ? $"{baseName} {Position}"
                : baseName;
        }
    }
}

public sealed class DaouCalendarEvent
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("calendarId")]
    public string CalendarId { get; set; } = "";

    [JsonPropertyName("calendarName")]
    public string CalendarName { get; set; } = "";

    [JsonPropertyName("calendarOwnerId")]
    public string? CalendarOwnerId { get; set; }

    [JsonPropertyName("color")]
    public string? Color { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("creator")]
    public DaouCalendarPerson? Creator { get; set; }

    [JsonPropertyName("attendees")]
    public List<DaouCalendarPerson> Attendees { get; set; } = new();

    [JsonPropertyName("summary")]
    public string Summary { get; set; } = "(제목 없음)";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("recurrence")]
    public string? Recurrence { get; set; }

    [JsonPropertyName("visibility")]
    public string? Visibility { get; set; }

    [JsonPropertyName("timeType")]
    public string TimeType { get; set; } = "timed";

    [JsonPropertyName("startTime")]
    public DateTimeOffset StartTime { get; set; }

    [JsonPropertyName("endTime")]
    public DateTimeOffset EndTime { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [JsonPropertyName("lunar")]
    public bool Lunar { get; set; }

    [JsonPropertyName("solar")]
    public bool Solar { get; set; }

    [JsonIgnore]
    public bool IsAllDay => string.Equals(TimeType, "allday", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public string OwnerOrCreatorDisplay
    {
        get
        {
            DaouCalendarPerson? owner = null;

            if (!string.IsNullOrWhiteSpace(CalendarOwnerId))
            {
                if (Creator is not null && string.Equals(Creator.Id, CalendarOwnerId, StringComparison.Ordinal))
                    owner = Creator;
                else
                    owner = Attendees.FirstOrDefault(a => string.Equals(a.Id, CalendarOwnerId, StringComparison.Ordinal));
            }

            owner ??= Creator;
            return owner?.DisplayName ?? "확인 불가";
        }
    }

    [JsonIgnore]
    public string AttendeeDisplay
    {
        get
        {
            var people = Attendees
                .Where(a => a is not null)
                .GroupBy(a => !string.IsNullOrWhiteSpace(a.Id)
                    ? $"id:{a.Id}"
                    : !string.IsNullOrWhiteSpace(a.Email)
                        ? $"mail:{a.Email}"
                        : $"name:{a.Name}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First().DisplayName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();

            return people.Count > 0 ? string.Join(", ", people) : "없음";
        }
    }

    [JsonIgnore]
    public string VisibilityDisplay => Visibility?.ToLowerInvariant() switch
    {
        "private" => "비공개",
        "public" => "공개",
        _ => string.IsNullOrWhiteSpace(Visibility) ? "확인 불가" : Visibility!
    };

    private IReadOnlyList<string>? _calendarIds;
    private IReadOnlyList<string>? _calendarNames;

    /// <summary>소속 캘린더 ID 목록. 기본값은 자기 자신 1개(<see cref="CalendarId"/>).</summary>
    [JsonIgnore]
    public IReadOnlyList<string> CalendarIds
    {
        get => _calendarIds ?? new[] { CalendarId };
        set => _calendarIds = value;
    }

    /// <summary>소속 캘린더 이름 목록. <see cref="CalendarIds"/>와 인덱스가 1:1 대응한다.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> CalendarNames
    {
        get => _calendarNames ?? new[] { CalendarName };
        set => _calendarNames = value;
    }

    /// <summary>상세 카드/칩에 표시할 캘린더 이름(복수면 ", "로 연결).</summary>
    [JsonIgnore]
    public string CalendarDisplayName
    {
        get
        {
            var names = CalendarNames
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (names.Count > 0)
                return string.Join(", ", names);

            return !string.IsNullOrWhiteSpace(CalendarName) ? CalendarName : CalendarId;
        }
    }
}


public sealed class CalendarDescriptor
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
}

public sealed class CalendarCache
{
    public DateTimeOffset LastUpdated { get; set; }

    /// <summary>이 캐시가 담고 있는 조회 범위의 시작(<see cref="CalendarGrid.GetVisibleRange"/> 결과). 옛 캐시에는 없어 null이다.</summary>
    public DateTimeOffset? RangeFrom { get; set; }

    /// <summary>이 캐시가 담고 있는 조회 범위의 끝. 옛 캐시에는 없어 null이다.</summary>
    public DateTimeOffset? RangeTo { get; set; }

    public List<DaouCalendarEvent> Events { get; set; } = new();
}
