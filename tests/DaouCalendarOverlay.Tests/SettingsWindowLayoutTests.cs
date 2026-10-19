using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DaouCalendarOverlay.Models;
using DaouCalendarOverlay.Views;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfGrid = System.Windows.Controls.Grid;
using WpfStackPanel = System.Windows.Controls.StackPanel;
using WpfSize = System.Windows.Size;

namespace DaouCalendarOverlay.Tests;

/// <summary>
/// 설정창(ResizeMode=NoResize)에 스냅 체크박스를 더한 뒤의 배치를 실제 WPF 레이아웃으로 계산해 확인한다.
/// 창은 띄우지 않는다. 핸들만 만들어 비클라이언트 영역(제목 표시줄·테두리) 크기를 OS에서 읽는다.
/// </summary>
public sealed class SettingsWindowLayoutTests
{
    /// <summary>7.3.1까지의 설정창 높이(체크박스 4개).</summary>
    private const double PreviousWindowHeight = 644;

    private const int SaveButtonRow = 7;
    private const int BridgeBoxRow = 6;

    [Fact]
    public void SnapCheckBox_IsLastInExistingCheckBoxPanel_WithSameMargin()
    {
        RunSta(() =>
        {
            var window = new SettingsWindow(new AppSettings(), firstRun: false);
            try
            {
                var snap = (WpfCheckBox)window.FindName("SnapToScreenEdgesCheckBox");
                var register = (WpfCheckBox)window.FindName("RegisterEdgeCheckBox");
                var panel = Assert.IsType<WpfStackPanel>(snap.Parent);

                Assert.Same(panel, register.Parent);
                Assert.Equal(5, panel.Children.Count);
                Assert.Same(snap, panel.Children[panel.Children.Count - 1]);
                Assert.Equal("창을 화면 가장자리에 붙이기(스냅)", snap.Content);

                foreach (var child in panel.Children)
                {
                    var checkBox = Assert.IsType<WpfCheckBox>(child);
                    Assert.Equal(register.Margin, checkBox.Margin);
                    Assert.Null(checkBox.Style);
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SnapCheckBox_ReflectsSettingsValue(bool snapToScreenEdges)
    {
        RunSta(() =>
        {
            var window = new SettingsWindow(new AppSettings { SnapToScreenEdges = snapToScreenEdges }, firstRun: false);
            try
            {
                var snap = (WpfCheckBox)window.FindName("SnapToScreenEdgesCheckBox");
                Assert.Equal(snapToScreenEdges, snap.IsChecked);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>
    /// 창 높이는 체크박스 한 행(체크박스 높이 + 위아래 Margin) 이상 늘어야 한다. 그래서 7.3.1 배치(체크박스 4개, 644)보다
    /// 브리지 상자 행에 배정되는 높이가 줄지 않고, 저장·취소 버튼은 클라이언트 영역 안에 그대로 들어간다.
    /// </summary>
    [Fact]
    public void WindowHeight_GrowsByOneCheckBoxRow_AndKeepsButtonsInsideClientArea()
    {
        RunSta(() =>
        {
            var current = MeasureLayout(removeSnapCheckBox: false, windowHeightOverride: null);
            var previous = MeasureLayout(removeSnapCheckBox: true, windowHeightOverride: PreviousWindowHeight);

            Assert.True(current.SnapRowHeight > 0, $"체크박스 한 행 높이 {current.SnapRowHeight}");
            Assert.True(
                current.WindowHeight - PreviousWindowHeight >= current.SnapRowHeight,
                $"창 높이 증가 {current.WindowHeight - PreviousWindowHeight} < 체크박스 한 행 {current.SnapRowHeight}");

            // 늘린 높이는 체크박스 한 행을 반올림한 정도여야 한다(한 행 + 1 DIP 미만의 여유).
            Assert.True(
                current.WindowHeight - PreviousWindowHeight < current.SnapRowHeight + 1,
                $"창 높이 증가 {current.WindowHeight - PreviousWindowHeight}가 체크박스 한 행 {current.SnapRowHeight}보다 1 DIP 이상 크다");

            Assert.True(
                current.BridgeRowHeight >= previous.BridgeRowHeight - 0.01,
                $"브리지 상자 행 높이 {current.BridgeRowHeight} < 7.3.1 배치 {previous.BridgeRowHeight}");

            Assert.True(
                current.ButtonsBottom <= current.ContentHeight + 0.01,
                $"저장·취소 버튼 아래 변 {current.ButtonsBottom} > 내용 영역 높이 {current.ContentHeight}");
            Assert.True(
                previous.ButtonsBottom <= previous.ContentHeight + 0.01,
                $"7.3.1 배치의 버튼 아래 변 {previous.ButtonsBottom} > 내용 영역 높이 {previous.ContentHeight}");
        });
    }

    private sealed record LayoutResult(
        double WindowHeight,
        double SnapRowHeight,
        double BridgeRowHeight,
        double ButtonsBottom,
        double ContentHeight);

    private static LayoutResult MeasureLayout(bool removeSnapCheckBox, double? windowHeightOverride)
    {
        var window = new SettingsWindow(new AppSettings(), firstRun: false);
        try
        {
            if (windowHeightOverride is { } height)
                window.Height = height;

            var handle = new WindowInteropHelper(window).EnsureHandle();
            var dpi = VisualTreeHelper.GetDpi(window);

            Assert.True(GetWindowRect(handle, out var windowRect));
            Assert.True(GetClientRect(handle, out var clientRect));

            var nonClientWidth = (windowRect.Right - windowRect.Left - clientRect.Right) / dpi.DpiScaleX;
            var nonClientHeight = (windowRect.Bottom - windowRect.Top - clientRect.Bottom) / dpi.DpiScaleY;
            var clientWidth = window.Width - nonClientWidth;
            var clientHeight = window.Height - nonClientHeight;

            var snap = (WpfCheckBox)window.FindName("SnapToScreenEdgesCheckBox");
            var panel = (WpfStackPanel)snap.Parent;

            // 체크박스 한 행 = 체크박스 DesiredSize(Margin 포함). 측정 전에 패널에서 빼면 값이 0이 되므로 먼저 잰다.
            snap.Measure(new WpfSize(clientWidth, double.PositiveInfinity));
            var snapRowHeight = snap.DesiredSize.Height;

            if (removeSnapCheckBox)
                panel.Children.Remove(snap);

            var root = (WpfGrid)window.Content;
            root.Measure(new WpfSize(clientWidth, clientHeight));
            root.Arrange(new Rect(0, 0, clientWidth, clientHeight));
            root.UpdateLayout();

            var saveButton = FindSaveButton(root);
            var buttonsBottom = saveButton.TransformToAncestor(root)
                .TransformBounds(new Rect(0, 0, saveButton.ActualWidth, saveButton.ActualHeight))
                .Bottom;

            return new LayoutResult(
                window.Height,
                snapRowHeight,
                root.RowDefinitions[BridgeBoxRow].ActualHeight,
                buttonsBottom,
                root.ActualHeight);
        }
        finally
        {
            window.Close();
        }
    }

    private static WpfButton FindSaveButton(WpfGrid root)
    {
        foreach (var child in root.Children)
        {
            if (child is not WpfGrid row || WpfGrid.GetRow(row) != SaveButtonRow)
                continue;

            foreach (var rowChild in row.Children)
            {
                if (rowChild is not WpfStackPanel buttons)
                    continue;

                foreach (var button in buttons.Children)
                {
                    if (button is WpfButton { IsDefault: true } save)
                        return save;
                }
            }
        }

        throw new InvalidOperationException("설정창에서 저장 버튼(IsDefault)을 찾지 못했다.");
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hWnd, out NativeRect rect);
}
