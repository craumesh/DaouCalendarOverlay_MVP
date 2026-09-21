using System.Windows;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using DaouCalendarOverlay.Models;
using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _working;
    private readonly ChromeExtensionInstaller _extensionInstaller = new();

    public AppSettings Result => _working;

    public SettingsWindow(AppSettings source, bool firstRun)
    {
        InitializeComponent();
        _working = source.Clone();
        Title = firstRun ? "Daou Calendar Overlay - 최초 설정" : "Daou Calendar Overlay 설정";

        BaseUrlTextBox.Text = _working.BaseUrl;
        CalendarIdsTextBox.Text = string.Join(Environment.NewLine, _working.CalendarIds);
        RefreshMinutesTextBox.Text = _working.RefreshMinutes.ToString();
        StartWithWindowsCheckBox.IsChecked = _working.StartWithWindows;
        AlwaysOnTopCheckBox.IsChecked = _working.AlwaysOnTop;
        PositionLockedCheckBox.IsChecked = _working.PositionLocked;
    }

    private void Extract_Click(object sender, RoutedEventArgs e)
    {
        var parsed = CalendarUrlParser.Parse(ApiUrlTextBox.Text);

        if (parsed.Status == CalendarUrlParseStatus.InvalidUrl)
        {
            WpfMessageBox.Show(this, "유효한 전체 URL을 붙여넣어 주세요.", "URL 확인", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
            return;
        }

        if (parsed.Status == CalendarUrlParseStatus.NoCalendarIds)
        {
            WpfMessageBox.Show(this, "URL에서 calendarIds[]를 찾지 못했습니다.", "추출 결과", WpfMessageBoxButton.OK, WpfMessageBoxImage.Information);
            return;
        }

        BaseUrlTextBox.Text = parsed.BaseUrl;
        CalendarIdsTextBox.Text = string.Join(Environment.NewLine, parsed.CalendarIds);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var baseUrl = BaseUrlTextBox.Text.Trim().TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            WpfMessageBox.Show(this, "DaouOffice 주소를 https://... 형식으로 입력해 주세요.", "설정 확인", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
            return;
        }

        var ids = CalendarIdsTextBox.Text
            .Split(new[] { '\r', '\n', ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct()
            .ToList();

        if (ids.Count == 0)
        {
            WpfMessageBox.Show(this, "캘린더 ID를 하나 이상 입력해 주세요.", "설정 확인", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(RefreshMinutesTextBox.Text.Trim(), out var refreshMinutes) || refreshMinutes < 1)
        {
            WpfMessageBox.Show(this, "자동 새로고침은 1분 이상의 숫자로 입력해 주세요.", "설정 확인", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
            return;
        }

        _working.BaseUrl = baseUrl;
        _working.CalendarIds = ids;
        _working.RefreshMinutes = refreshMinutes;
        _working.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
        _working.AlwaysOnTop = AlwaysOnTopCheckBox.IsChecked == true;
        _working.PositionLocked = PositionLockedCheckBox.IsChecked == true;
        _working.HiddenCalendarIds = _working.HiddenCalendarIds
            .Where(id => ids.Contains(id, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        DialogResult = true;
        Close();
    }

    private void OpenExtensionFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _extensionInstaller.OpenExtensionDirectory();
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show(this, $"Chrome 확장 폴더를 열지 못했습니다.\n\n{ex.Message}", "Chrome 브리지", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
