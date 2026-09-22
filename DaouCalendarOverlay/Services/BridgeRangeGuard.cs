namespace DaouCalendarOverlay.Services;

/// <summary>HTTP 왕복 도중 표시 범위/설정이 바뀌었는지 판정하는 순수 로직.</summary>
public static class BridgeRangeGuard
{
    /// <summary>스냅샷 범위가 현재 요청 범위와 다르면 true(= 결과를 버려야 함).</summary>
    public static bool IsStale(
        DateTimeOffset snapshotFrom,
        DateTimeOffset snapshotTo,
        DateTimeOffset currentFrom,
        DateTimeOffset currentTo)
        => snapshotFrom != currentFrom || snapshotTo != currentTo;

    /// <summary>
    /// BaseUrl 또는 캘린더 ID 집합이 스냅샷과 다르면 true(= 결과를 버려야 함).
    /// HTTP 진행 중 범위는 그대로인데 설정창 저장으로 BaseUrl/캘린더 ID만 바뀐 경우를 잡기 위한 것이다.
    /// 캘린더 ID 비교는 순서 무관이다.
    /// </summary>
    public static bool IsSettingsStale(
        string snapshotBaseUrl,
        IEnumerable<string> snapshotCalendarIds,
        string currentBaseUrl,
        IEnumerable<string> currentCalendarIds)
    {
        if (!string.Equals(snapshotBaseUrl, currentBaseUrl, StringComparison.OrdinalIgnoreCase))
            return true;

        var snapshotSet = new HashSet<string>(snapshotCalendarIds, StringComparer.Ordinal);
        var currentSet = new HashSet<string>(currentCalendarIds, StringComparer.Ordinal);
        return !snapshotSet.SetEquals(currentSet);
    }
}
