namespace DaouCalendarOverlay.Services;

/// <summary>오버레이 표시 모드. 펼침(달력 전체) 또는 최소화 막대.</summary>
public enum OverlayDisplayMode
{
    Expanded,
    MiniBar
}

/// <summary>창 크기를 바꿀 때 고정되는 쪽.</summary>
public enum VerticalAnchor
{
    Top,
    Bottom
}

/// <summary>막대를 기준으로 목록이 붙는 쪽.</summary>
public enum AttachDirection
{
    Below,
    Above
}

/// <summary>오버레이 창 배치에 쓰는 크기·시간 상수와 DIP-px 변환. 크기 상수는 DIP, 시간은 ms 또는 초다.</summary>
public static class OverlayLayoutMetrics
{
    public const double WindowWidthDip = 325;

    /// <summary>RootChrome Margin. 창과 카드 테두리 사이 투명 여백.</summary>
    public const double WindowMarginDip = 7;

    public const double ExpandedWindowHeightDip = 440;

    /// <summary>펼침 카드 높이(440 - 7*2).</summary>
    public const double ExpandedCardHeightDip = 426;

    /// <summary>눈에 보이는 막대 카드 높이(테두리 포함).</summary>
    public const double MiniCardHeightDip = 48;

    /// <summary>막대 창 높이(48 + 7*2).</summary>
    public const double MiniWindowHeightDip = 62;

    public const double MiniBarLineHeightDip = 17;

    /// <summary>막대에 붙는 목록 영역의 고정 높이(상세 카드 MaxHeight 380 + 여유 4).</summary>
    public const double AttachedRegionHeightDip = 384;

    public const double SnapThresholdDip = 12;

    public const double EdgeToleranceDip = 2;

    public const int ContentFadeMilliseconds = 120;

    public const int HeightAnimationMilliseconds = 200;

    public const int TickerSlideMilliseconds = 350;

    public const int TickerIntervalSeconds = 4;

    /// <summary>
    /// DIP를 물리 px로 바꾼다. WPF 레이아웃 반올림과 같은 MidpointRounding.ToEven을 쓴다.
    /// dpiScale이 0 이하이거나 NaN/무한대면 ArgumentOutOfRangeException.
    /// </summary>
    public static int DipToPx(double dip, double dpiScale)
    {
        if (double.IsNaN(dpiScale) || double.IsInfinity(dpiScale) || dpiScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(dpiScale), dpiScale, "DPI 배율은 0보다 큰 유한한 값이어야 합니다.");

        return (int)Math.Round(dip * dpiScale, MidpointRounding.ToEven);
    }
}
