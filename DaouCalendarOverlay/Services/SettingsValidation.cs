using System.Globalization;

namespace DaouCalendarOverlay.Services;

/// <summary>
/// <see cref="SettingsValidation.ValidateCalendarIds"/> 결과. 유효하면 <see cref="Ids"/>만 의미 있고,
/// 유효하지 않으면 <see cref="InvalidTokens"/>와 사용자에게 그대로 보여줄 <see cref="Reason"/>이 채워진다.
/// </summary>
public sealed class CalendarIdsValidation
{
    public bool IsValid { get; init; }
    public IReadOnlyList<string> Ids { get; init; } = new List<string>();
    public IReadOnlyList<string> InvalidTokens { get; init; } = new List<string>();
    public string? Reason { get; init; }
}

/// <summary>
/// 설정 값(자동 새로고침 분, 캘린더 ID)의 유효성 판정 단일 지점.
/// 순수 함수만 두며 I/O·시계·WPF 타입을 참조하지 않는다. BaseUrl 검증(스킴·호스트 정책)은 이 클래스의 책임이 아니다.
/// </summary>
public static class SettingsValidation
{
    public const int MinRefreshMinutes = 1;
    public const int MaxRefreshMinutes = 1440;
    public const int MaxCalendarIdLength = 19;
    public const string RefreshMinutesReason = "자동 새로고침은 1~1440 사이의 숫자(분)로 입력해 주세요.";
    public const string CalendarIdsEmptyReason = "캘린더 ID를 하나 이상 입력해 주세요.";

    private static readonly char[] CalendarIdSeparators = { '\r', '\n', ',', ';', ' ' };

    /// <summary>범위 밖 값을 1~1440으로 잘라낸다. int.MinValue/int.MaxValue에서도 오버플로 없이 동작한다.</summary>
    public static int ClampRefreshMinutes(int minutes) =>
        Math.Clamp(minutes, MinRefreshMinutes, MaxRefreshMinutes);

    /// <summary>
    /// 순수 숫자(부호·소수점·자릿수 구분 기호 없음)만 허용하고 1~1440 범위를 벗어나면 거부한다.
    /// 실패 시 클램프한 값을 돌려주지 않는다(호출자가 거부 메시지를 띄워야 한다).
    /// </summary>
    public static bool TryParseRefreshMinutes(string? text, out int minutes)
    {
        minutes = 0;
        if (text is null)
            return false;

        var trimmed = text.Trim();
        if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            return false;

        if (value < MinRefreshMinutes || value > MaxRefreshMinutes)
            return false;

        minutes = value;
        return true;
    }

    /// <summary>
    /// 캘린더 ID 규칙: 공백 제거 후 길이 1~19, 모든 문자가 ASCII 숫자('0'~'9')일 때만 true.
    /// 유니코드 전각 숫자까지 통과시키는 표준 숫자 판정 API는 쓰지 않는다.
    /// </summary>
    public static bool IsValidCalendarId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;

        var trimmed = id.Trim();
        if (trimmed.Length == 0 || trimmed.Length > MaxCalendarIdLength)
            return false;

        foreach (var c in trimmed)
        {
            if (c < '0' || c > '9')
                return false;
        }

        return true;
    }

    /// <summary>
    /// 기존 <c>SettingsWindow.Save_Click</c>과 동일한 분리 규칙(줄바꿈·쉼표·세미콜론·공백)으로 토큰화하고
    /// 각 토큰을 <see cref="IsValidCalendarId"/>로 검증한다. 서수 비교로 중복을 제거하며 입력 순서를 보존한다.
    /// </summary>
    public static CalendarIdsValidation ValidateCalendarIds(string? rawText)
    {
        var tokens = (rawText ?? string.Empty)
            .Split(CalendarIdSeparators, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim())
            .Where(token => token.Length > 0)
            .Distinct()
            .ToList();

        if (tokens.Count == 0)
        {
            return new CalendarIdsValidation
            {
                IsValid = false,
                Ids = new List<string>(),
                InvalidTokens = new List<string>(),
                Reason = CalendarIdsEmptyReason
            };
        }

        var invalidTokens = tokens.Where(token => !IsValidCalendarId(token)).ToList();
        if (invalidTokens.Count > 0)
        {
            var preview = string.Join(", ", invalidTokens.Take(3));
            if (invalidTokens.Count > 3)
                preview += $" 외 {invalidTokens.Count - 3}개";

            return new CalendarIdsValidation
            {
                IsValid = false,
                Ids = tokens,
                InvalidTokens = invalidTokens,
                Reason = $"캘린더 ID는 숫자만 입력할 수 있습니다(1~{MaxCalendarIdLength}자리). 확인 필요: {preview}"
            };
        }

        return new CalendarIdsValidation
        {
            IsValid = true,
            Ids = tokens,
            InvalidTokens = new List<string>(),
            Reason = null
        };
    }

    /// <summary>
    /// hidden 목록에서 visible(현재 캘린더 ID) 집합에 포함된 항목만 입력 순서를 보존해 서수 기준 중복 제거한다.
    /// hidden 또는 visibleIds가 null이면 빈 목록을 돌려준다(방어적).
    /// </summary>
    public static IReadOnlyList<string> FilterHiddenCalendarIds(IEnumerable<string>? hidden, IEnumerable<string> visibleIds)
    {
        if (hidden is null || visibleIds is null)
            return new List<string>();

        var visibleSet = new HashSet<string>(visibleIds, StringComparer.Ordinal);

        return hidden
            .Where(id => visibleSet.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }
}
