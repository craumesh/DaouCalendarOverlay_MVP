using System.Collections.ObjectModel;
using System.Windows.Media;

namespace DaouCalendarOverlay.Services;

/// <summary>
/// 일정 색 인덱스(DaouOffice <c>color</c> 값) → 표시 색 매핑.
/// 실제 DaouOffice 색표를 확보하기 전까지는 기존 8색 팔레트(<c>index % 8</c>)와 같은 색을 유지한다.
/// </summary>
public static class EventColorPalette
{
    private static readonly Color[] PaletteColors =
    {
        Color.FromRgb(0x5B, 0x8D, 0xE8),
        Color.FromRgb(0x69, 0xB5, 0x88),
        Color.FromRgb(0xB2, 0x83, 0xE6),
        Color.FromRgb(0xE0, 0x8A, 0x68),
        Color.FromRgb(0x4E, 0xB5, 0xB8),
        Color.FromRgb(0xD0, 0x72, 0x9E),
        Color.FromRgb(0xB9, 0x9A, 0x55),
        Color.FromRgb(0x77, 0x9D, 0xCF)
    };

    /// <summary>실데이터에서 관측된 색 인덱스 범위(1~18).</summary>
    private const int ObservedIndexMin = 1;
    private const int ObservedIndexMax = 18;

    /// <summary>기본 8색 팔레트(순서 고정).</summary>
    public static IReadOnlyList<Color> Palette { get; } = Array.AsReadOnly(PaletteColors);

    /// <summary>
    /// 색 인덱스 → 색. 실제 색값을 채울 수 있도록 사전 형태로 두며, 현재는 기존 동작과 같은
    /// <c>Palette[index % Palette.Count]</c>로 채운다.
    /// </summary>
    public static IReadOnlyDictionary<int, Color> IndexColors { get; } = BuildIndexColors();

    /// <summary>
    /// 일정의 색 값을 표시 색으로 바꾼다. 숫자면 <see cref="IndexColors"/>, 표에 없으면 팔레트 나머지 연산,
    /// 숫자가 아니거나 비어 있으면 캘린더 ID 해시로 팔레트 색을 고른다. 예외를 던지지 않는다.
    /// </summary>
    public static Color Resolve(string? colorValue, string? calendarId)
    {
        if (int.TryParse(colorValue, out var index))
        {
            if (IndexColors.TryGetValue(index, out var mapped))
                return mapped;

            // Math.Abs(int)는 int.MinValue에서 OverflowException을 던지므로 long으로 넓혀 계산한다.
            return PaletteColors[(int)(Math.Abs((long)index) % PaletteColors.Length)];
        }

        var hash = StringComparer.Ordinal.GetHashCode(calendarId ?? string.Empty);
        return PaletteColors[(hash & 0x7fffffff) % PaletteColors.Length];
    }

    private static IReadOnlyDictionary<int, Color> BuildIndexColors()
    {
        var map = new Dictionary<int, Color>();
        for (var i = ObservedIndexMin; i <= ObservedIndexMax; i++)
            map[i] = PaletteColors[i % PaletteColors.Length];
        return new ReadOnlyDictionary<int, Color>(map);
    }
}
