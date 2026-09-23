using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace DaouCalendarOverlay.Services;

/// <summary>
/// DaouOffice "열기"를 기본 브라우저 대신 Chrome으로 직접 띄운다. 확장이 쿠키를 얻는 브라우저는 확장이 설치된
/// Chrome이므로, 기본 브라우저가 다른 경우에도 Chrome을 띄워야 재로그인이 동기화로 이어진다.
/// 실패하면 false를 돌려주고 기본 브라우저 fallback은 호출자가 맡는다.
/// 레지스트리 값 해석과 후보 선택은 순수 함수로 두어 단위 테스트한다. 실제 레지스트리·파일시스템·프로세스를
/// 만지는 것은 <see cref="FindChromePath"/>와 <see cref="OpenInChrome"/>뿐이다.
/// </summary>
public static class BrowserLauncher
{
    /// <summary>Chrome 실행 파일 등록 키. HKCU와 HKLM에서 같은 상대 경로를 쓴다.</summary>
    public const string ChromeAppPathsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe";

    private const string ChromeExecutableName = "chrome.exe";

    private const string PathValueName = "Path";

    private const string LogCategory = "ui.openDaou";

    /// <summary>
    /// App Paths 레지스트리 값을 실행 파일 경로 형태로 정리한다. 앞뒤 공백과 큰따옴표 한 겹을 벗기고
    /// 환경 변수를 확장한다. 파일 존재 여부는 검사하지 않는다(순수 함수).
    /// </summary>
    public static string? NormalizeChromePath(string? rawRegistryValue)
    {
        if (string.IsNullOrWhiteSpace(rawRegistryValue))
            return null;

        var value = rawRegistryValue.Trim();
        if (value.StartsWith('"'))
            value = value[1..];
        if (value.EndsWith('"'))
            value = value[..^1];

        value = value.Trim();
        if (value.Length == 0)
            return null;

        var expanded = Environment.ExpandEnvironmentVariables(value);
        return string.IsNullOrWhiteSpace(expanded) ? null : expanded;
    }

    /// <summary>
    /// Chrome 명령줄 인자로 넘겨도 되는 URL인지 판정한다. 인자 주입 방지 목적이다:
    /// <c>--incognito</c> 같은 스위치, <c>file:</c> 등 다른 스킴, 상대 경로가 Chrome에 전달되지 않도록
    /// 절대 URI이면서 스킴이 http/https인 경우만 허용한다.
    /// </summary>
    public static bool IsLaunchableUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        // 스위치로 해석될 수 있는 문자열은 URI 파싱 결과와 무관하게 거부한다.
        if (url.TrimStart().StartsWith('-'))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 레지스트리에서 읽은 App Paths 값들 중 실제로 존재하는 Chrome 실행 파일 경로를 고른다.
    /// <paramref name="keys"/>는 호출자가 정한 우선순위(HKCU → HKLM) 순서로 본다. 각 항목에서 기본값(실행 파일 전체 경로)을
    /// 먼저 보고, 없거나 파일이 없으면 <c>Path</c> 값(설치 폴더) + <c>chrome.exe</c>를 본다.
    /// 문자열이 아닌 값과 null은 무시한다. 파일 존재 확인은 <paramref name="fileExists"/>로 주입받는다(순수 함수).
    /// </summary>
    public static string? SelectChromePath(
        IEnumerable<(object? DefaultValue, object? PathValue)>? keys,
        Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(fileExists);

        if (keys is null)
            return null;

        foreach (var (defaultValue, pathValue) in keys)
        {
            if (defaultValue is string rawDefault)
            {
                var executable = NormalizeChromePath(rawDefault);
                if (executable is not null && fileExists(executable))
                    return executable;
            }

            if (pathValue is string rawDirectory)
            {
                var directory = NormalizeChromePath(rawDirectory);
                if (directory is not null)
                {
                    var executable = Path.Combine(directory, ChromeExecutableName);
                    if (fileExists(executable))
                        return executable;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// HKCU → HKLM 순으로 <see cref="ChromeAppPathsKey"/>를 읽어 Chrome 실행 파일 경로를 찾는다.
    /// 찾지 못하면 null. 어떤 예외도 밖으로 던지지 않는다.
    /// </summary>
    public static string? FindChromePath()
    {
        try
        {
            var keys = new List<(object? DefaultValue, object? PathValue)>();

            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                try
                {
                    using var key = hive.OpenSubKey(ChromeAppPathsKey, writable: false);
                    if (key is null)
                        continue;

                    keys.Add((key.GetValue(null), key.GetValue(PathValueName)));
                }
                catch (Exception ex)
                {
                    LogService.Warn(LogCategory, "Chrome 경로 조회 실패", ex);
                }
            }

            return SelectChromePath(keys, File.Exists);
        }
        catch (Exception ex)
        {
            LogService.Warn(LogCategory, "Chrome 경로 조회 실패", ex);
            return null;
        }
    }

    /// <summary>
    /// <paramref name="url"/>을 Chrome으로 직접 연다. URL이 http/https가 아니거나, Chrome을 찾지 못하거나,
    /// 실행에 실패하면 false를 돌려준다(호출자가 기본 브라우저로 fallback). 예외를 던지지 않는다.
    /// </summary>
    public static bool OpenInChrome(string url)
    {
        try
        {
            if (!IsLaunchableUrl(url))
                return false;

            var chromePath = FindChromePath();
            if (chromePath is null)
                return false;

            var startInfo = new ProcessStartInfo
            {
                FileName = chromePath,
                UseShellExecute = false
            };
            // ArgumentList는 인자 하나로 인용해 넘기므로 URL 안의 공백이 별도 인자로 쪼개지지 않는다.
            startInfo.ArgumentList.Add(url);

            using var process = Process.Start(startInfo);
            return true;
        }
        catch (Exception ex)
        {
            LogService.Warn(LogCategory, "Chrome 직접 실행 실패", ex);
            return false;
        }
    }
}
