namespace DaouCalendarOverlay.Models;

/// <summary>
/// 물리 픽셀 단위 점. 커서 위치처럼 크기가 없는 좌표에 쓴다.
/// WPF·WinForms 타입을 참조하지 않는 순수 값 타입이다.
/// </summary>
public readonly record struct PixelPoint(int X, int Y);
