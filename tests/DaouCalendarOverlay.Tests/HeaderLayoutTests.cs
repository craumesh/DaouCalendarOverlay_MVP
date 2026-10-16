using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using WpfPath = System.Windows.Shapes.Path;
using System.Windows.Threading;
using System.Xml.Linq;
using WpfButton = System.Windows.Controls.Button;
using WpfGrid = System.Windows.Controls.Grid;
using WpfTextBlock = System.Windows.Controls.TextBlock;

namespace DaouCalendarOverlay.Tests;

/// <summary>
/// 헤더 첫 줄(HeaderBar)의 폭 예산을 실측으로 고정한다.
/// 폭 293 DIP(= 창 311 − 테두리 2 − 내용 Margin 16)에서 월 제목 칸이 110 DIP 이상이고,
/// 버튼 7개가 20×20 DIP 이상이며 내용이 버튼 안쪽(테두리 1을 뺀 크기)에 들어가야 한다.
/// 눈으로만 보면 글자·아이콘 잘림을 놓치므로 같은 PC의 글꼴로 측정한다.
/// </summary>
public sealed class HeaderLayoutTests
{
    private const double HeaderWidth = 293;
    private const double MinMonthColumnWidth = 110;
    private const double MinButtonSize = 20;
    private const string MonthText = "2026년 12월";

    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    // XamlReader로 조각만 읽을 때는 코드비하인드 처리기가 없으므로 이벤트 속성을 뺀다.
    private static readonly HashSet<string> EventAttributeNames = new(StringComparer.Ordinal)
    {
        "MouseLeftButtonUp", "MouseLeftButtonDown", "MouseEnter", "MouseLeave", "Click",
    };

    [Fact]
    public void MainWindowXaml_HeaderBar_HasNamedGridWithThreeColumns()
    {
        var doc = XDocument.Load(RepositoryPaths.Combine("DaouCalendarOverlay", "Views", "MainWindow.xaml"));
        var header = doc.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "HeaderBar");

        Assert.Equal(Wpf + "Grid", header.Name);
        Assert.Equal("0", (string?)header.Attribute("Grid.Row"));
        Assert.Equal(3, header.Element(Wpf + "Grid.ColumnDefinitions")!.Elements().Count());
    }

    [Fact]
    public void HeaderFirstRow_At293Dip_KeepsMonthTitleAndButtonsUnclipped()
    {
        RunSta(() =>
        {
            var header = LoadHeaderFromMainWindowXaml();
            header.Measure(new Size(HeaderWidth, double.PositiveInfinity));
            header.Arrange(new Rect(0, 0, HeaderWidth, header.DesiredSize.Height));
            header.UpdateLayout();

            Assert.Equal(HeaderWidth, header.ActualWidth);

            var monthColumn = header.ColumnDefinitions[1].ActualWidth;
            var buttons = Descendants<WpfButton>(header)
                .Where(b => WpfGrid.GetRow(FirstAncestorInGrid(b, header)) == 0)
                .ToList();
            var report = string.Join(", ", buttons.Select(b => $"{Describe(b)}={b.ActualWidth}x{b.ActualHeight}"));

            // (a) 월 제목 칸
            Assert.True(monthColumn >= MinMonthColumnWidth,
                $"월 제목 칸 {monthColumn} DIP < {MinMonthColumnWidth} DIP. 버튼: {report}");

            // (b) 버튼 7개의 최소 크기
            Assert.Equal(7, buttons.Count);
            foreach (var button in buttons)
            {
                Assert.True(button.ActualWidth >= MinButtonSize && button.ActualHeight >= MinButtonSize,
                    $"버튼 {Describe(button)} 크기 {button.ActualWidth}x{button.ActualHeight} < {MinButtonSize}x{MinButtonSize}. 전체: {report}");
            }

            // (c) 내용이 버튼 안쪽(테두리 1을 뺀 크기)에 들어간다.
            foreach (var button in buttons)
            {
                var content = MeasureContent(button);
                var innerWidth = button.ActualWidth - 2;
                var innerHeight = button.ActualHeight - 2;
                Assert.True(content.Width <= innerWidth && content.Height <= innerHeight,
                    $"버튼 {Describe(button)} 내용 {content.Width}x{content.Height}이 안쪽 {innerWidth}x{innerHeight}을 넘는다. FontSize={button.FontSize}");
            }

            // (d) 월 제목이 1열 안에 들어간다.
            var title = Descendants<WpfTextBlock>(header).Single(t => t.Text == MonthText);
            title.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Assert.True(title.DesiredSize.Width <= monthColumn,
                $"월 제목 폭 {title.DesiredSize.Width} DIP가 1열 {monthColumn} DIP를 넘는다.");
        });
    }

    private static string Describe(WpfButton button) =>
        button.Content is string text ? $"'{text}'" : (button.ToolTip as string ?? button.GetType().Name);

    private static Size MeasureContent(WpfButton button)
    {
        FrameworkElement element = button.Content switch
        {
            string text => new WpfTextBlock
            {
                Text = text,
                FontSize = button.FontSize,
                FontFamily = button.FontFamily,
            },
            WpfPath path => path,
            _ => throw new InvalidOperationException($"시험이 모르는 버튼 내용 형식: {button.Content?.GetType()}"),
        };
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return element.DesiredSize;
    }

    // 버튼이 들어 있는 HeaderBar 직계 자식(StackPanel 또는 Grid 자식)을 돌려 Grid.Row를 읽게 한다.
    private static UIElement FirstAncestorInGrid(DependencyObject child, WpfGrid grid)
    {
        DependencyObject current = child;
        while (VisualTreeHelper.GetParent(current) is { } parent && parent != grid)
            current = parent;
        return (UIElement)current;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                yield return match;
            foreach (var nested in Descendants<T>(child))
                yield return nested;
        }
    }

    private static WpfGrid LoadHeaderFromMainWindowXaml()
    {
        var mainDoc = XDocument.Load(RepositoryPaths.Combine("DaouCalendarOverlay", "Views", "MainWindow.xaml"));
        var appDoc = XDocument.Load(RepositoryPaths.Combine("DaouCalendarOverlay", "App.xaml"));

        var source = mainDoc.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "HeaderBar");
        var fragment = new XElement(source);

        // 2행(날짜·시계·필터 줄)은 첫 줄 판정에 필요 없다.
        fragment.Elements()
            .Where(e => (string?)e.Attribute("Grid.Row") == "1")
            .ToList()
            .ForEach(e => e.Remove());

        foreach (var attribute in fragment.DescendantsAndSelf().Attributes()
                     .Where(a => a.Name.Namespace == XNamespace.None && EventAttributeNames.Contains(a.Name.LocalName))
                     .ToList())
        {
            attribute.Remove();
        }

        // 바인딩 제거. MonthTitle의 Text만 고정 문자열로 둔다.
        foreach (var attribute in fragment.DescendantsAndSelf().Attributes()
                     .Where(a => a.Value.StartsWith("{Binding", StringComparison.Ordinal))
                     .ToList())
        {
            if (attribute.Name.LocalName == "Text" && attribute.Value.Contains("MonthTitle", StringComparison.Ordinal))
                attribute.Value = MonthText;
            else
                attribute.Remove();
        }

        // StaticResource가 해석되도록 두 스타일을 원문에서 복사해 조각의 Resources에 넣는다.
        var baseStyle = appDoc.Descendants(Wpf + "Style")
            .Single(e => (string?)e.Attribute(X + "Key") == "HeaderButtonStyle");
        var topStyle = mainDoc.Descendants(Wpf + "Style")
            .Single(e => (string?)e.Attribute(X + "Key") == "TopHeaderButtonStyle");
        fragment.AddFirst(new XElement(Wpf + "Grid.Resources", new XElement(baseStyle), new XElement(topStyle)));

        fragment.SetAttributeValue("xmlns", Wpf.NamespaceName);
        fragment.SetAttributeValue(XNamespace.Xmlns + "x", X.NamespaceName);

        return (WpfGrid)XamlReader.Parse(fragment.ToString());
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
