using System.Globalization;

namespace DaouCalendarOverlay.Services;

/// <summary>
/// 로그 파일 이름과 롤링 계획을 계산하는 순수 함수 모음. 파일 존재 여부는 보지 않는다.
/// </summary>
public static class LogRotation
{
    /// <summary>활성 로그 파일 최대 크기(1MB).</summary>
    public const long MaxFileBytes = 1024 * 1024;

    /// <summary>보관 파일 수(활성 1 + .1 ~ .4).</summary>
    public const int MaxFiles = 5;

    private const string DefaultPrefix = "app";
    private const string DateFormat = "yyyyMMdd";

    /// <summary>예: ("overlay", 2026-09-22) → "overlay-20260922.log".</summary>
    public static string GetFileName(string prefix, DateTimeOffset timestamp)
    {
        var normalizedPrefix = string.IsNullOrWhiteSpace(prefix) ? DefaultPrefix : prefix;
        return string.Concat(normalizedPrefix, "-", timestamp.ToString(DateFormat, CultureInfo.InvariantCulture), ".log");
    }

    /// <summary>예: ("...\overlay-20260922.log", 1) → "...\overlay-20260922.log.1".</summary>
    public static string GetArchivePath(string activePath, int index)
    {
        if (index < 1 || index > MaxFiles - 1)
            throw new ArgumentOutOfRangeException(nameof(index), index, $"보관 인덱스는 1~{MaxFiles - 1} 범위여야 합니다.");

        return string.Concat(activePath, ".", index.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>가장 오래된 보관 파일 경로(= .4).</summary>
    public static string GetOldestArchivePath(string activePath) => GetArchivePath(activePath, MaxFiles - 1);

    /// <summary>현재 크기 + 쓰려는 바이트 수가 상한을 넘으면 true(같으면 false).</summary>
    public static bool ShouldRotate(long currentLengthBytes, int pendingByteCount) =>
        currentLengthBytes + pendingByteCount > MaxFileBytes;

    /// <summary>
    /// 적용 순서대로의 이동 계획: (.3→.4), (.2→.3), (.1→.2), (active→.1).
    /// 파일 존재 여부는 호출자가 확인한다.
    /// </summary>
    public static IReadOnlyList<(string Source, string Destination)> BuildRotationPlan(string activePath)
    {
        var plan = new List<(string Source, string Destination)>(MaxFiles - 1);
        for (var index = MaxFiles - 1; index >= 2; index--)
            plan.Add((GetArchivePath(activePath, index - 1), GetArchivePath(activePath, index)));

        plan.Add((activePath, GetArchivePath(activePath, 1)));
        return plan;
    }
}
