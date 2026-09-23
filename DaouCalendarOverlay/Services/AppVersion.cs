using System.Reflection;

namespace DaouCalendarOverlay.Services;

/// <summary>
/// 앱 버전의 단일 출처. 트레이 툴팁·설정창·로그가 모두 이 클래스만 쓴다(버전 문자열을 다른 곳에서 조립하지 않는다).
/// 값의 원천은 csproj의 <c>Version</c>/<c>InformationalVersion</c>이며, git 저장소에서 빌드하면 SDK가
/// InformationalVersion 끝에 <c>+커밋 SHA</c>를 자동으로 붙인다.
/// </summary>
public static class AppVersion
{
    /// <summary>버전을 읽을 수 없을 때 쓰는 값.</summary>
    public const string Fallback = "0.0.0";

    /// <summary>NotifyIcon.Text 최대 길이. 넘으면 예외가 난다.</summary>
    public const int TrayTextMaxLength = 63;

    /// <summary>앱 어셈블리의 InformationalVersion. 예: "7.1.0" 또는 "7.1.0+ab12cd3".</summary>
    public static string Informational { get; } = ReadInformational();

    /// <summary>사용자 표시용 버전(빌드 메타데이터 제외). 예: "7.1.0".</summary>
    public static string Display { get; } = FormatDisplay(Informational);

    /// <summary>
    /// InformationalVersion에서 첫 '+' 앞부분(빌드 메타데이터 제외)만 취한다. 비어 있으면 <see cref="Fallback"/>.
    /// </summary>
    public static string FormatDisplay(string? informational)
    {
        if (string.IsNullOrWhiteSpace(informational))
            return Fallback;

        var trimmed = informational.Trim();
        var plusIndex = trimmed.IndexOf('+');
        var core = (plusIndex >= 0 ? trimmed.Substring(0, plusIndex) : trimmed).Trim();
        return core.Length == 0 ? Fallback : core;
    }

    /// <summary>
    /// 트레이 툴팁 문자열을 <see cref="TrayTextMaxLength"/>자 이하로 자른다. 말줄임표는 붙이지 않는다.
    /// </summary>
    public static string ClampTrayText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text.Length <= TrayTextMaxLength ? text : text.Substring(0, TrayTextMaxLength);
    }

    /// <summary>로그 첫 줄용 문자열. 테스트를 위해 값 주입 오버로드를 둔다.</summary>
    /// <remarks>
    /// 형식: <c>DaouCalendarOverlay {버전} mode={mode} pid={processId}</c>.
    /// <paramref name="informational"/>이 비어 있지 않고 <paramref name="display"/>와 다르면 버전 부분은
    /// <c>{display} ({informational})</c>이 된다(커밋 SHA를 잘라내지 않는다). 예외를 던지지 않는다.
    /// </remarks>
    public static string FormatStartupLine(string? mode, int processId, string display, string? informational)
    {
        var modeText = string.IsNullOrWhiteSpace(mode) ? "unknown" : mode;
        var versionText = string.IsNullOrWhiteSpace(informational) || string.Equals(display, informational, StringComparison.Ordinal)
            ? display
            : $"{display} ({informational})";
        return $"DaouCalendarOverlay {versionText} mode={modeText} pid={processId}";
    }

    /// <summary>로그 첫 줄용 문자열. 현재 앱의 <see cref="Display"/>/<see cref="Informational"/>을 쓴다.</summary>
    public static string FormatStartupLine(string? mode, int processId) =>
        FormatStartupLine(mode, processId, Display, Informational);

    /// <summary>
    /// 앱 어셈블리의 커스텀 특성에서 InformationalVersion을 읽는다. 실패하면 어셈블리 버전, 그것도 없으면 <see cref="Fallback"/>.
    /// 단일 파일 배포에서 비어 있는 <c>Assembly.Location</c>은 쓰지 않는다.
    /// </summary>
    private static string ReadInformational()
    {
        try
        {
            var assembly = typeof(AppVersion).Assembly;
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
                return informational.Trim();

            var version = assembly.GetName().Version?.ToString();
            return string.IsNullOrWhiteSpace(version) ? Fallback : version;
        }
        catch
        {
            return Fallback;
        }
    }
}
