using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace DaouCalendarOverlay.Models;

public sealed class DayCellViewModel : INotifyPropertyChanged
{
    private static readonly MediaBrush NormalBrush = Brush(0x19, 0xFF, 0xFF, 0xFF);
    private static readonly MediaBrush TodayBrush = Brush(0x24, 0x5D, 0x9C, 0xFF);
    private static readonly MediaBrush OtherMonthBrush = Brush(0x08, 0xFF, 0xFF, 0xFF);
    private static readonly MediaBrush NormalBorderBrush = Brush(0x16, 0xFF, 0xFF, 0xFF);
    private static readonly MediaBrush TodayBorderBrush = Brush(0xC8, 0x72, 0xA9, 0xFF);
    private static readonly MediaBrush TodayBadgeBrush = Brush(0xE5, 0x4F, 0x86, 0xD9);
    private static readonly MediaBrush TransparentBrush = MediaBrushes.Transparent;
    private static readonly MediaBrush SundayBrush = Brush(0xFF, 0xFF, 0x9A, 0x9A);
    private static readonly MediaBrush SaturdayBrush = Brush(0xFF, 0x9C, 0xC3, 0xFF);
    private static readonly MediaBrush MutedBrush = Brush(0xFF, 0x7B, 0x7E, 0x86);

    public DateTime Date { get; init; }
    public bool IsCurrentMonth { get; init; }
    public bool IsToday { get; init; }
    public bool IsHoliday { get; init; }
    public string DayText => Date.Day.ToString();
    public ObservableCollection<EventChipViewModel> VisibleEvents { get; } = new();
    public int MoreCount { get; set; }
    public string MoreIndicatorText => MoreCount > 0 ? "..." : "";
    public Visibility MoreVisibility => MoreCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    public MediaBrush CellBackground => IsToday ? TodayBrush : IsCurrentMonth ? NormalBrush : OtherMonthBrush;
    public MediaBrush CellBorderBrush => IsToday ? TodayBorderBrush : NormalBorderBrush;
    public Thickness CellBorderThickness => IsToday ? new Thickness(1.2) : new Thickness(1);
    public MediaBrush DayBadgeBackground => IsToday ? TodayBadgeBrush : TransparentBrush;

    public MediaBrush DayForeground
    {
        get
        {
            // 법정 공휴일은 현재 달/다른 달 여부와 관계없이 일요일과 같은 빨간색으로 표시한다.
            if (IsHoliday)
                return SundayBrush;
            if (!IsCurrentMonth)
                return MutedBrush;
            if (IsToday)
                return MediaBrushes.White;

            return Date.DayOfWeek switch
            {
                DayOfWeek.Sunday => SundayBrush,
                DayOfWeek.Saturday => SaturdayBrush,
                _ => MediaBrushes.White
            };
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void NotifyAll()
    {
        OnPropertyChanged(nameof(MoreCount));
        OnPropertyChanged(nameof(MoreIndicatorText));
        OnPropertyChanged(nameof(MoreVisibility));
    }

    private static MediaSolidColorBrush Brush(byte a, byte r, byte g, byte b)
    {
        var brush = new MediaSolidColorBrush(MediaColor.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class EventChipViewModel
{
    public required DaouCalendarEvent Event { get; init; }
    public string Text { get; init; } = "";
    public string Tooltip { get; init; } = "";
    public MediaBrush AccentBrush { get; init; } = MediaBrushes.SteelBlue;
    public MediaBrush ChipBackground { get; init; } = MediaBrushes.Transparent;

    public string Title => Event.Summary;
    public string TimeText => Event.IsAllDay
        ? "종일"
        : $"{Event.StartTime:HH:mm} - {Event.EndTime:HH:mm}";
    public string CalendarName => Event.CalendarDisplayName;
    public string OwnerText => $"소유/작성 · {Event.OwnerOrCreatorDisplay}";
    public string AudienceText => $"공유/참석 · {Event.AttendeeDisplay}";
    public string VisibilityText => $"공개범위 · {Event.VisibilityDisplay}";
}
