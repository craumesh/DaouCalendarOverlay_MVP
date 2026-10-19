using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using WpfBorder = System.Windows.Controls.Border;
using WpfCanvas = System.Windows.Controls.Canvas;
using WpfColumnDefinition = System.Windows.Controls.ColumnDefinition;
using WpfGrid = System.Windows.Controls.Grid;
using WpfStackPanel = System.Windows.Controls.StackPanel;
using WpfTextBlock = System.Windows.Controls.TextBlock;

namespace DaouCalendarOverlay.Tests;

/// <summary>
/// 막대 일정 영역(MiniAgendaArea)의 순환 슬라이드 구조를 확인한다.
/// 슬롯(34 DIP)보다 큰 StackPanel(3행 51 DIP)이 레이아웃 클립을 받으면 그 클립이 RenderTransform과 함께 움직여
/// 슬라이드 중 들어오는 3번째 행이 보이지 않는다. Canvas로 감싸 클립을 받지 않게 한다.
/// </summary>
public sealed class MiniAgendaLayoutTests
{
    private const double SlotHeight = 34;
    private const double RowHeight = 17;
    private const double AreaWidth = 200;
    private const double TimeColumnWidth = 34;

    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    // XamlReader로 조각만 읽을 때는 코드비하인드 처리기가 없으므로 이벤트 속성을 뺀다.
    private static readonly HashSet<string> EventAttributeNames = new(StringComparer.Ordinal)
    {
        "MouseLeftButtonUp", "MouseLeftButtonDown", "MouseEnter", "MouseLeave", "Click",
    };

    [Fact]
    public void MainWindowXaml_MiniAgendaRows_IsInsideCanvas_UnderClippedArea()
    {
        var doc = XDocument.Load(RepositoryPaths.Combine("DaouCalendarOverlay", "Views", "MainWindow.xaml"));

        var rows = doc.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "MiniAgendaRows");
        var canvas = rows.Parent;
        Assert.NotNull(canvas);
        Assert.Equal(Wpf + "Canvas", canvas!.Name);

        var area = doc.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "MiniAgendaArea");
        Assert.Equal("True", (string?)area.Attribute("ClipToBounds"));
        Assert.Equal("34", (string?)area.Attribute("Height"));
        Assert.Contains(canvas.Ancestors(), a => a == area);

        // 빈 목록 문구는 Canvas 밖 형제여야 세로 가운데 정렬이 유지된다.
        var empty = doc.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "MiniAgendaEmptyText");
        Assert.NotSame(canvas, empty.Parent);
        Assert.Same(canvas.Parent, empty.Parent);
    }

    /// <summary>
    /// 실제 MainWindow.xaml의 MiniAgendaArea 조각을 그대로 읽어 렌더링한다.
    /// Y=-17(슬라이드 끝)에서 슬롯 아래 절반에 3번째 행이 그려져야 하고, Y=-8.5(슬라이드 중간)에서도 아래 줄이 비지 않아야 한다.
    /// </summary>
    [Fact]
    public void MainWindowXamlArea_ThirdRowIsVisibleWhileSliding()
    {
        RunSta(() =>
        {
            var area = LoadAreaFromMainWindowXaml();
            var rows = (WpfStackPanel)area.FindName("MiniAgendaRows");
            var canvas = (WpfCanvas)area.FindName("MiniAgendaCanvas");
            var empty = (WpfTextBlock)area.FindName("MiniAgendaEmptyText");
            var shift = Assert.IsType<TranslateTransform>(rows.RenderTransform);

            // 앱은 일정이 있으면 빈 문구를 접는다(RebuildMiniRows).
            empty.Visibility = Visibility.Collapsed;
            AddColoredRows(rows);
            Layout(area);

            Assert.Null(System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(rows));
            Assert.Equal(AreaWidth, canvas.ActualWidth);
            Assert.Equal(canvas.ActualWidth, rows.ActualWidth);

            // 슬라이드 시작(Y=0): 1·2번째 행(빨강·파랑)이 보인다.
            AssertPixel(area, shift, 0, y: 8, Colors.Red);
            AssertPixel(area, shift, 0, y: 25, Colors.Blue);

            // 슬라이드 중간: 아래 줄(y=28·33)은 3번째 행(초록)이 들어와 채운다.
            AssertPixel(area, shift, -RowHeight / 2, y: 28, Colors.Green);
            AssertPixel(area, shift, -RowHeight / 2, y: 33, Colors.Green);

            // 슬라이드 끝(Y=-17): 2·3번째 행이 슬롯 전체를 채운다.
            AssertPixel(area, shift, -RowHeight, y: 3, Colors.Blue);
            AssertPixel(area, shift, -RowHeight, y: 20, Colors.Green);
            AssertPixel(area, shift, -RowHeight, y: 28, Colors.Green);
            AssertPixel(area, shift, -RowHeight, y: 33, Colors.Green);
        });
    }

    /// <summary>
    /// Canvas 폭 바인딩으로 행 폭이 일정 영역 폭에 묶여 제목 열이 남은 폭으로 제한되는지 확인한다.
    /// 폭이 무한이면 제목이 잘리지 않고(CharacterEllipsis 미적용) 영역 밖으로 넘친다.
    /// </summary>
    [Fact]
    public void MainWindowXamlArea_RowWidthIsBoundToArea_SoTitleTrims()
    {
        RunSta(() =>
        {
            var area = LoadAreaFromMainWindowXaml();
            var rows = (WpfStackPanel)area.FindName("MiniAgendaRows");
            ((WpfTextBlock)area.FindName("MiniAgendaEmptyText")).Visibility = Visibility.Collapsed;

            const string longTitle = "아주 긴 일정 제목이 막대 일정 영역의 폭을 훨씬 넘어서는 경우에 말줄임이 붙는지 확인한다";
            var title = new WpfTextBlock
            {
                Text = longTitle,
                FontSize = 11.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var row = new WpfGrid { Height = RowHeight };
            row.ColumnDefinitions.Add(new WpfColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new WpfColumnDefinition { Width = new GridLength(TimeColumnWidth) });
            row.ColumnDefinitions.Add(new WpfColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            WpfGrid.SetColumn(title, 2);
            row.Children.Add(title);
            rows.Children.Add(row);

            Layout(area);

            var natural = new WpfTextBlock { Text = longTitle, FontSize = 11.5 };
            natural.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.True(natural.DesiredSize.Width > AreaWidth, "시험 전제: 제목이 영역보다 길어야 한다.");

            Assert.Equal(AreaWidth, row.ActualWidth);
            Assert.True(title.ActualWidth <= AreaWidth - TimeColumnWidth + 0.5,
                $"제목 폭 {title.ActualWidth}이 남은 폭 {AreaWidth - TimeColumnWidth}을 넘는다.");
        });
    }

    /// <summary>
    /// 대조 시험: Canvas 없이 StackPanel을 바로 두면 레이아웃 클립(0..34)이 RenderTransform과 함께 움직여
    /// 슬라이드 끝에서 아래 줄이 투명하게 빈다. 위 시험이 이 결함을 실제로 잡는다는 근거다.
    /// </summary>
    [Fact]
    public void StructureWithoutCanvas_LosesThirdRowWhileSliding()
    {
        RunSta(() =>
        {
            var shift = new TranslateTransform();
            var rows = new WpfStackPanel { RenderTransform = shift };
            AddColoredRows(rows);
            var grid = new WpfGrid();
            grid.Children.Add(rows);
            var area = new WpfBorder
            {
                Height = SlotHeight,
                Width = AreaWidth,
                ClipToBounds = true,
                Background = Brushes.Transparent,
                Child = grid,
            };
            Layout(area);

            Assert.NotNull(System.Windows.Controls.Primitives.LayoutInformation.GetLayoutClip(rows));

            shift.Y = -RowHeight;
            area.UpdateLayout();
            Assert.Equal(0, ReadPixel(area, 28)[3]);
        });
    }

    private static WpfBorder LoadAreaFromMainWindowXaml()
    {
        var doc = XDocument.Load(RepositoryPaths.Combine("DaouCalendarOverlay", "Views", "MainWindow.xaml"));
        var source = doc.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "MiniAgendaArea");
        var fragment = new XElement(source);

        foreach (var attribute in fragment.DescendantsAndSelf().Attributes()
                     .Where(a => a.Name.Namespace == XNamespace.None && EventAttributeNames.Contains(a.Name.LocalName))
                     .ToList())
        {
            attribute.Remove();
        }

        fragment.SetAttributeValue("xmlns", Wpf.NamespaceName);
        fragment.SetAttributeValue(XNamespace.Xmlns + "x", X.NamespaceName);

        var area = (WpfBorder)XamlReader.Parse(fragment.ToString());
        area.Width = AreaWidth;
        return area;
    }

    private static void AddColoredRows(WpfStackPanel rows)
    {
        foreach (var color in new[] { Colors.Red, Colors.Blue, Colors.Green })
        {
            rows.Children.Add(new WpfBorder
            {
                Height = RowHeight,
                Background = new SolidColorBrush(color),
            });
        }
    }

    private static void AssertPixel(WpfBorder area, TranslateTransform shift, double shiftY, int y, Color expected)
    {
        shift.Y = shiftY;
        area.UpdateLayout();
        var pixel = ReadPixel(area, y);

        // Pbgra32: B, G, R, A. 불투명 단색이므로 정확히 일치해야 한다.
        Assert.True(
            pixel[3] == 255 && pixel[2] == expected.R && pixel[1] == expected.G && pixel[0] == expected.B,
            $"Y={shiftY}, y={y}: 기대 {expected}, 실제 A={pixel[3]} R={pixel[2]} G={pixel[1]} B={pixel[0]}");
    }

    private static byte[] ReadPixel(FrameworkElement area, int y)
    {
        var bitmap = new RenderTargetBitmap((int)AreaWidth, (int)SlotHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(area);
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect(10, y, 1, 1), pixel, 4, 0);
        return pixel;
    }

    private static void Layout(FrameworkElement element)
    {
        element.Measure(new Size(AreaWidth, SlotHeight));
        element.Arrange(new Rect(0, 0, AreaWidth, SlotHeight));
        element.UpdateLayout();
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
}
