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

    public SettingsWindow(AppSettings source, bool firstRun, string? validationNotice = null)
    {
        InitializeComponent();
        _working = source.Clone();
        Title = firstRun ? "Daou Calendar Overlay - 최초 설정" : "Daou Calendar Overlay 설정";

        BaseUrlTextBox.Text = _working.BaseUrl;
        CalendarIdsTextBox.Text = string.Join(Environment.NewLine, _working.CalendarIds);
        RefreshMinutesTextBox.Text = SettingsValidation.ClampRefreshMinutes(_working.RefreshMinutes).ToString();
        StartWithWindowsCheckBox.IsChecked = _working.StartWithWindows;
        AlwaysOnTopCheckBox.IsChecked = _working.AlwaysOnTop;
        PositionLockedCheckBox.IsChecked = _working.PositionLocked;
        VersionText.Text = $"버전 {AppVersion.Display} · 프로토콜 v{NativeBridgeProtocol.ProtocolVersion}";

        if (!string.IsNullOrWhiteSpace(validationNotice))
        {
            ValidationNoticeText.Text = validationNotice;
            ValidationNoticeText.Visibility = Visibility.Visible;
        }
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

        var extractedValidation = BaseUrlPolicy.Validate(parsed.BaseUrl);
        if (!extractedValidation.IsValid)
            WpfMessageBox.Show(this, extractedValidation.Reason, "주소 확인", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var validation = BaseUrlPolicy.Validate(BaseUrlTextBox.Text);
        if (!validation.IsValid)
        {
            WpfMessageBox.Show(this, validation.Reason, "설정 확인", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
            BaseUrlTextBox.Focus();
            BaseUrlTextBox.SelectAll();
            return;
        }

        var baseUrl = validation.NormalizedBaseUrl!;

        var idsValidation = SettingsValidation.ValidateCalendarIds(CalendarIdsTextBox.Text);
        if (!idsValidation.IsValid)
        {
            WpfMessageBox.Show(this, idsValidation.Reason ?? SettingsValidation.CalendarIdsEmptyReason, "설정 확인", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
            CalendarIdsTextBox.Focus();
            return;
        }

        if (!SettingsValidation.TryParseRefreshMinutes(RefreshMinutesTextBox.Text, out var refreshMinutes))
        {
            WpfMessageBox.Show(this, SettingsValidation.RefreshMinutesReason, "설정 확인", WpfMessageBoxButton.OK, WpfMessageBoxImage.Warning);
            RefreshMinutesTextBox.Focus();
            return;
        }

        _working.BaseUrl = baseUrl;
        _working.CalendarIds = idsValidation.Ids.ToList();
        _working.RefreshMinutes = refreshMinutes;
        _working.StartWithWindows = StartWithWindowsCheckBox.IsChecked == true;
        _working.AlwaysOnTop = AlwaysOnTopCheckBox.IsChecked == true;
        _working.PositionLocked = PositionLockedCheckBox.IsChecked == true;
        _working.HiddenCalendarIds = SettingsValidation.FilterHiddenCalendarIds(_working.HiddenCalendarIds, _working.CalendarIds).ToList();

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
