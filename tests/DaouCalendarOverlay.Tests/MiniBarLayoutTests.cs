using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using WpfBorder = System.Windows.Controls.Border;
using WpfGrid = System.Windows.Controls.Grid;
using WpfStackPanel = System.Windows.Controls.StackPanel;
using WpfTextBlock = System.Windows.Controls.TextBlock;

namespace DaouCalendarOverlay.Tests;

/// <summary>
/// 최소화 막대(MiniBarContent)가 세 구역(시계 | 일정 | 버튼)으로 나뉘고,
/// 구역 사이 간격이 6 DIP이며 일정 구역이 옅은 배경과 둥근 모서리를 갖는지 실측으로 고정한다.
/// RootChrome 안쪽 크기 309×46 DIP 슬롯(= 311−2, 48−2)에 조각을 배치하면 Margin 6,4를 뺀 내용 상자는 297×38 DIP다.
/// </summary>
public sealed class MiniBarLayoutTests
{
    private const double SlotWidth = 309;
    private const double SlotHeight = 46;
    private const double ContentWidth = 297;
    private const double ContentHeight = 38;
    private const double ZoneGap = 6;
    private const double GapTolerance = 0.5;

    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    // XamlReader로 조각만 읽을 때는 코드비하인드 처리기가 없으므로 이벤트 속성을 뺀다.
    private static readonly HashSet<string> EventAttributeNames = new(StringComparer.Ordinal)
    {
        "MouseLeftButtonUp", "MouseLeftButtonDown", "MouseEnter", "MouseLeave", "Click",
    };

    [Fact]
    public void MainWindowXaml_MiniBarContent_HasThreeZonesInFiveColumns()
    {
        var doc = XDocument.Load(RepositoryPaths.Combine("DaouCalendarOverlay", "Views", "MainWindow.xaml"));
        var content = doc.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "MiniBarContent");

        var columns = content.Element(Wpf + "Grid.ColumnDefinitions")!.Elements()
            .Select(e => (string?)e.Attribute("Width"))
            .ToArray();
        Assert.Equal(new[] { "Auto", "6", "*", "6", "Auto" }, columns);

        var clock = Named(content, "MiniClockBlock");
        var zone = Named(content, "MiniAgendaZone");
        var buttons = Named(content, "MiniButtonsZone");
        Assert.Equal("0", (string?)clock.Attribute("Grid.Column"));
        Assert.Equal("2", (string?)zone.Attribute("Grid.Column"));
        Assert.Equal("4", (string?)buttons.Attribute("Grid.Column"));
        Assert.Equal("Stretch", (string?)clock.Attribute("VerticalAlignment"));
        Assert.Equal("Stretch", (string?)zone.Attribute("VerticalAlignment"));

        // MiniAgendaArea는 일정 구역의 직계 자식이고, 막대 버튼은 버튼 구역 안에 있다.
        Assert.Same(zone, Named(content, "MiniAgendaArea").Parent);
        Assert.Same(buttons, Named(content, "MiniExpandButton").Parent);
    }

    [Fact]
    public void MiniBar_At309x46Slot_ZonesDoNotOverlapAndGapsAreSixDip()
    {
        RunSta(() =>
        {
            var root = LoadMiniBarFromMainWindowXaml();
            Layout(root);

            Assert.Equal(ContentWidth, root.ActualWidth);
            Assert.Equal(ContentHeight, root.ActualHeight);

            var clock = (WpfBorder)root.FindName("MiniClockBlock");
            var zone = (WpfBorder)root.FindName("MiniAgendaZone");
            var buttons = (WpfStackPanel)root.FindName("MiniButtonsZone");

            var clockRect = RectIn(clock, root);
            var zoneRect = RectIn(zone, root);
            var buttonsRect = RectIn(buttons, root);
            var report = $"시계={clockRect}, 일정={zoneRect}, 버튼={buttonsRect}";

            // (a) 겹치지 않는다.
            Assert.False(clockRect.IntersectsWith(Shrink(zoneRect)), $"시계와 일정이 겹친다. {report}");
            Assert.False(zoneRect.IntersectsWith(Shrink(buttonsRect)), $"일정과 버튼이 겹친다. {report}");
            Assert.False(clockRect.IntersectsWith(Shrink(buttonsRect)), $"시계와 버튼이 겹친다. {report}");
            Assert.True(clockRect.Right <= zoneRect.Left + 0.001 && zoneRect.Right <= buttonsRect.Left + 0.001,
                $"구역 순서가 시계 | 일정 | 버튼이 아니다. {report}");

            // (b) 구역 간격 6 DIP
            var clockGap = zoneRect.Left - clockRect.Right;
            var buttonGap = buttonsRect.Left - zoneRect.Right;
            Assert.True(Math.Abs(clockGap - ZoneGap) <= GapTolerance, $"시계–일정 간격 {clockGap} DIP. {report}");
            Assert.True(Math.Abs(buttonGap - ZoneGap) <= GapTolerance, $"일정–버튼 간격 {buttonGap} DIP. {report}");

            // (c) 일정 구역의 옅은 배경과 둥근 모서리
            var brush = Assert.IsType<SolidColorBrush>(zone.Background);
            Assert.True(brush.Color.A > 0, $"일정 구역 배경 알파 {brush.Color.A}");
            Assert.True(zone.CornerRadius.TopLeft >= 6, $"일정 구역 모서리 {zone.CornerRadius.TopLeft}");

            // (d) 시계와 일정 구역의 높이가 같다.
            Assert.True(Math.Abs(clock.ActualHeight - zone.ActualHeight) <= 0.5,
                $"시계 높이 {clock.ActualHeight}, 일정 높이 {zone.ActualHeight}");
        });
    }

    // 경계가 맞닿는 구역은 부동소수 오차로 교차로 판정될 수 있어 0.01씩 줄여 비교한다.
    private static Rect Shrink(Rect r) => new(r.X + 0.01, r.Y + 0.01, Math.Max(0, r.Width - 0.02), Math.Max(0, r.Height - 0.02));

    private static Rect RectIn(FrameworkElement element, Visual root)
    {
        var origin = element.TranslatePoint(new Point(0, 0), (UIElement)root);
        return new Rect(origin.X, origin.Y, element.ActualWidth, element.ActualHeight);
    }

    private static XElement Named(XElement scope, string name) =>
        scope.Descendants().Single(e => (string?)e.Attribute(X + "Name") == name);

    private static void Layout(FrameworkElement element)
    {
        element.Measure(new Size(SlotWidth, SlotHeight));
        element.Arrange(new Rect(0, 0, SlotWidth, SlotHeight));
        element.UpdateLayout();
    }

    private static WpfGrid LoadMiniBarFromMainWindowXaml()
    {
        var mainDoc = XDocument.Load(RepositoryPaths.Combine("DaouCalendarOverlay", "Views", "MainWindow.xaml"));
        var appDoc = XDocument.Load(RepositoryPaths.Combine("DaouCalendarOverlay", "App.xaml"));

        var source = mainDoc.Descendants().Single(e => (string?)e.Attribute(X + "Name") == "MiniBarContent");
        var fragment = new XElement(source);

        foreach (var attribute in fragment.DescendantsAndSelf().Attributes()
                     .Where(a => a.Name.Namespace == XNamespace.None && EventAttributeNames.Contains(a.Name.LocalName))
                     .ToList())
        {
            attribute.Remove();
        }

        // 바인딩 제거. 상태 점은 바인딩이 없으면 보이는 상태가 되어 폭 예산의 최악 경우를 시험한다.
        foreach (var attribute in fragment.DescendantsAndSelf().Attributes()
                     .Where(a => a.Value.StartsWith("{Binding", StringComparison.Ordinal))
                     .ToList())
        {
            attribute.Remove();
        }

        // 조각의 루트는 앱에서 Collapsed로 시작한다. 배치 시험에서는 보이게 한다.
        fragment.Attribute("Visibility")?.Remove();

        // StaticResource가 해석되도록 스타일을 원문에서 복사해 조각의 Resources에 넣는다.
        var baseStyle = appDoc.Descendants(Wpf + "Style")
            .Single(e => (string?)e.Attribute(X + "Key") == "HeaderButtonStyle");
        var topStyle = mainDoc.Descendants(Wpf + "Style")
            .Single(e => (string?)e.Attribute(X + "Key") == "TopHeaderButtonStyle");
        fragment.AddFirst(new XElement(Wpf + "Grid.Resources", new XElement(baseStyle), new XElement(topStyle)));

        fragment.SetAttributeValue("xmlns", Wpf.NamespaceName);
        fragment.SetAttributeValue(XNamespace.Xmlns + "x", X.NamespaceName);

        var root = (WpfGrid)XamlReader.Parse(fragment.ToString());
        ((WpfTextBlock)root.FindName("MiniDateText")).Text = "10/16 (금)";
        ((WpfTextBlock)root.FindName("MiniTimeText")).Text = "14:05";
        return root;
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
