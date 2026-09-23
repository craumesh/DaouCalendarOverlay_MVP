namespace DaouCalendarOverlay.Services;

/// <summary>확장이 보고한 버전과 EXE에 포함된 기대 버전 비교(순수 로직).</summary>
public static class ExtensionVersionGuard
{
    /// <summary>공백 제거 후 "major.minor.build" 형태로 정규화한다. 파싱 실패 시 Trim 결과, null/공백이면 빈 문자열.</summary>
    public static string Normalize(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return "";

        var trimmed = version.Trim();
        if (Version.TryParse(trimmed, out var parsed))
            return $"{parsed.Major}.{parsed.Minor}.{Math.Max(parsed.Build, 0)}";

        return trimmed;
    }

    /// <summary>보고된 버전이 기대 버전과 다르면 true. 보고값이 비어 있으면(필드를 보내지 않는 구버전 확장) true.</summary>
    public static bool IsMismatch(string? reportedVersion, string expectedVersion)
    {
        if (string.IsNullOrWhiteSpace(expectedVersion))
            return false;

        var reported = Normalize(reportedVersion);
        if (reported.Length == 0)
            return true;

        return !string.Equals(reported, Normalize(expectedVersion), StringComparison.Ordinal);
    }
}
