namespace DaouCalendarOverlay.Services;

/// <summary>HTTP 왕복 도중 표시 범위가 바뀌었는지 판정하는 순수 로직.</summary>
public static class BridgeRangeGuard
{
    /// <summary>스냅샷 범위가 현재 요청 범위와 다르면 true(= 결과를 버려야 함).</summary>
    public static bool IsStale(
        DateTimeOffset snapshotFrom,
        DateTimeOffset snapshotTo,
        DateTimeOffset currentFrom,
        DateTimeOffset currentTo)
        => snapshotFrom != currentFrom || snapshotTo != currentTo;
}
