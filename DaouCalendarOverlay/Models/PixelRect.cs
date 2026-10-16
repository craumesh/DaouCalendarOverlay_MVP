namespace DaouCalendarOverlay.Models;

/// <summary>
/// 물리 픽셀 단위 사각형. Right/Bottom은 배타(exclusive)로 Win32 RECT와 같은 의미다.
/// WPF·WinForms 타입을 참조하지 않는 순수 값 타입이다.
/// </summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    /// <summary>크기를 바꾸지 않고 (dx, dy)만큼 평행 이동한 사각형을 돌려준다.</summary>
    public PixelRect Offset(int dx, int dy) => new(Left + dx, Top + dy, Right + dx, Bottom + dy);

    /// <summary>
    /// 네 변을 inset만큼 안쪽으로 줄인 사각형을 돌려준다.
    /// 음수를 넣으면 바깥쪽으로 넓히는 Inflate로 쓸 수 있다.
    /// </summary>
    public PixelRect Deflate(int inset) => new(Left + inset, Top + inset, Right - inset, Bottom - inset);
}
