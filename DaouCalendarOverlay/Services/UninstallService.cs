using Microsoft.Win32;

namespace DaouCalendarOverlay.Services;

/// <summary>
/// 제거 실행 결과. <see cref="Removed"/>는 실제로 지운 항목, <see cref="Failed"/>는 지우려 했으나 실패한 항목이다.
/// 처음부터 없던 항목은 어느 쪽에도 들어가지 않는다.
/// </summary>
public sealed class UninstallResult
{
    public UninstallResult(IEnumerable<string> removed, IEnumerable<string> failed)
    {
        ArgumentNullException.ThrowIfNull(removed);
        ArgumentNullException.ThrowIfNull(failed);

        Removed = Array.AsReadOnly(removed.ToArray());
        Failed = Array.AsReadOnly(failed.ToArray());
    }

    public IReadOnlyList<string> Removed { get; }

    public IReadOnlyList<string> Failed { get; }

    public bool HasFailures => Failed.Count > 0;
}

/// <summary>
/// 제거 대상 레지스트리 항목 하나. <see cref="KeyPath"/>는 HKCU 기준 상대 경로다.
/// </summary>
public sealed class UninstallTarget
{
    public UninstallTarget(string keyPath, string? valueName, string description)
    {
        if (string.IsNullOrWhiteSpace(keyPath))
            throw new ArgumentException("레지스트리 키 경로가 비어 있습니다.", nameof(keyPath));
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("설명이 비어 있습니다.", nameof(description));

        KeyPath = keyPath;
        ValueName = valueName;
        Description = description;
    }

    /// <summary>HKCU 상대 경로.</summary>
    public string KeyPath { get; }

    /// <summary>null이면 키 트리 전체를 삭제하고, 아니면 이 값 하나만 삭제한다.</summary>
    public string? ValueName { get; }

    /// <summary>사람이 읽는 설명(결과 요약과 문서에 그대로 쓰인다).</summary>
    public string Description { get; }
}

/// <summary>
/// 등록 해제(HKCU Run 값, Chrome/Edge Native Messaging Host 키)와 사용자 데이터 폴더 삭제.
/// 경로 계산과 결과 요약은 순수 함수로 두어 단위 테스트한다. 실제 레지스트리를 만지는 것은 <see cref="Run"/>뿐이다.
/// </summary>
public static class UninstallService
{
    /// <summary>시작 프로그램 등록 키. <see cref="StartupService"/>의 키와 같아야 한다.</summary>
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>시작 프로그램 등록 값 이름. <see cref="StartupService"/>의 값 이름과 같아야 한다.</summary>
    public const string RunValueName = "DaouCalendarOverlay";

    public const string ChromeBrowserKeyRoot = @"Software\Google\Chrome";

    public const string EdgeBrowserKeyRoot = @"Software\Microsoft\Edge";

    /// <summary>사용자 데이터 폴더 이름. <see cref="TryRemoveUserData"/>는 이 이름의 폴더만 지운다.</summary>
    private const string UserDataFolderName = "DaouCalendarOverlay";

    private const string LogCategory = "uninstall";

    /// <summary>
    /// 브라우저 레지스트리 루트(예: <c>Software\Google\Chrome</c>)에서 Native Messaging Host 키 경로를 만든다.
    /// </summary>
    public static string BuildNativeHostKeyPath(string browserKeyRoot)
    {
        if (string.IsNullOrWhiteSpace(browserKeyRoot))
            throw new ArgumentException("브라우저 레지스트리 루트가 비어 있습니다.", nameof(browserKeyRoot));

        var root = browserKeyRoot.Trim('\\');
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("브라우저 레지스트리 루트가 비어 있습니다.", nameof(browserKeyRoot));

        return $@"{root}\NativeMessagingHosts\{NativeBridgeProtocol.HostName}";
    }

    /// <summary>제거할 레지스트리 항목 3개(Run 값, Chrome host 키, Edge host 키)를 이 순서로 돌려준다.</summary>
    public static IReadOnlyList<UninstallTarget> GetRegistryTargets() => new[]
    {
        new UninstallTarget(RunKeyPath, RunValueName, "시작 프로그램 등록(HKCU Run)"),
        new UninstallTarget(BuildNativeHostKeyPath(ChromeBrowserKeyRoot), null, "Chrome Native Messaging Host 등록"),
        new UninstallTarget(BuildNativeHostKeyPath(EdgeBrowserKeyRoot), null, "Edge Native Messaging Host 등록")
    };

    /// <summary>%LOCALAPPDATA%\DaouCalendarOverlay. 경로만 계산하고 디렉터리를 만들지 않는다.</summary>
    public static string GetUserDataDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), UserDataFolderName);

    /// <summary>
    /// 레지스트리 등록을 해제하고, <paramref name="removeUserData"/>가 true면 마지막에 사용자 데이터 폴더를 지운다.
    /// 항목마다 따로 처리하며 예외를 밖으로 던지지 않는다.
    /// </summary>
    public static UninstallResult Run(bool removeUserData)
    {
        var removed = new List<string>();
        var failed = new List<string>();

        LogService.Info(LogCategory, $"제거 시작 removeUserData={removeUserData}");

        foreach (var target in GetRegistryTargets())
        {
            try
            {
                var entry = target.ValueName is null
                    ? RemoveKeyTree(target)
                    : RemoveValue(target, target.ValueName);

                if (entry is not null)
                {
                    removed.Add(entry);
                    LogService.Info(LogCategory, $"제거: {entry}");
                }
            }
            catch (Exception ex)
            {
                failed.Add($"{target.Description}: {ex.Message}");
                LogService.Warn(LogCategory, $"{target.Description} 제거 실패", ex);
            }
        }

        // 로그 파일이 사용자 데이터 폴더 안에 있으므로 레지스트리 처리와 그 로깅이 모두 끝난 뒤에 지운다.
        if (removeUserData)
        {
            try
            {
                var directory = GetUserDataDirectory();
                LogService.Info(LogCategory, $"사용자 데이터 폴더 삭제 시도: {directory}");

                if (TryRemoveUserData(directory, out var error))
                    removed.Add($"사용자 데이터 폴더 ({directory})");
                else
                    failed.Add($"사용자 데이터 폴더: {error}");
            }
            catch (Exception ex)
            {
                failed.Add($"사용자 데이터 폴더: {ex.Message}");
            }
        }

        // 사용자 데이터를 지웠다면 로그 폴더가 없으므로 이 줄은 기록되지 않는다(LogService는 폴더를 다시 만들지 않는다).
        LogService.Info(LogCategory, $"제거 완료 removed={removed.Count} failed={failed.Count}");
        return new UninstallResult(removed, failed);
    }

    /// <summary>
    /// 사용자 데이터 폴더를 재귀 삭제한다. 드라이브 루트, 상대 경로, 이름이 <c>DaouCalendarOverlay</c>가 아닌 폴더는
    /// 거부한다. 폴더가 이미 없으면 성공으로 본다. 예외를 던지지 않는다.
    /// </summary>
    public static bool TryRemoveUserData(string directory, out string? error)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            error = "경로가 비어 있습니다.";
            return false;
        }

        try
        {
            // 상대 경로는 현재 디렉터리 기준으로 풀려 엉뚱한 폴더를 가리킬 수 있다(예: LocalAppData 조회가 빈 문자열일 때).
            if (!Path.IsPathFullyQualified(directory))
            {
                error = "절대 경로가 아닙니다.";
                return false;
            }

            var full = Path.GetFullPath(directory);
            var root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root) || string.Equals(TrimTrailingSeparators(full), TrimTrailingSeparators(root), StringComparison.OrdinalIgnoreCase))
            {
                error = "드라이브 루트는 삭제할 수 없습니다.";
                return false;
            }

            if (!string.Equals(Path.GetFileName(TrimTrailingSeparators(full)), UserDataFolderName, StringComparison.Ordinal))
            {
                error = "DaouCalendarOverlay 폴더가 아닙니다.";
                return false;
            }

            if (!Directory.Exists(full))
            {
                error = null;
                return true;
            }

            Directory.Delete(full, recursive: true);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>결과 MessageBox와 로그에 쓰는 사람이 읽는 요약. 줄 구분은 <see cref="Environment.NewLine"/>.</summary>
    public static string BuildSummary(UninstallResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Removed.Count == 0 && result.Failed.Count == 0)
            return "제거할 항목이 없습니다.";

        var lines = new List<string>();
        if (result.Removed.Count > 0)
        {
            lines.Add("제거됨:");
            lines.AddRange(result.Removed.Select(item => $"- {item}"));
        }

        if (result.Failed.Count > 0)
        {
            if (lines.Count > 0)
                lines.Add(string.Empty);
            lines.Add("실패:");
            lines.AddRange(result.Failed.Select(item => $"- {item}"));
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>CLI 종료 코드. 실패 항목이 하나라도 있으면 1.</summary>
    public static int ToExitCode(UninstallResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.HasFailures ? 1 : 0;
    }

    /// <summary>값이 있으면 지우고 요약 항목을 돌려준다. 키나 값이 없으면 null(이미 없는 상태).</summary>
    private static string? RemoveValue(UninstallTarget target, string valueName)
    {
        // 값이 없는데 쓰기 권한만 없는 환경(GPO 등)에서 실패로 보고하지 않도록 먼저 읽기 전용으로 확인한다.
        using (var readKey = Registry.CurrentUser.OpenSubKey(target.KeyPath))
        {
            if (readKey?.GetValue(valueName) is null)
                return null;
        }

        using var key = Registry.CurrentUser.OpenSubKey(target.KeyPath, writable: true)
            ?? throw new InvalidOperationException($"레지스트리 키를 열 수 없습니다: {target.KeyPath}");
        if (key.GetValue(valueName) is null)
            return null;

        key.DeleteValue(valueName, throwOnMissingValue: false);
        return $"{target.Description} ({target.KeyPath}\\{valueName})";
    }

    /// <summary>키가 있으면 트리째 지우고 요약 항목을 돌려준다. 키가 없으면 null(이미 없는 상태).</summary>
    private static string? RemoveKeyTree(UninstallTarget target)
    {
        using (var key = Registry.CurrentUser.OpenSubKey(target.KeyPath))
        {
            if (key is null)
                return null;
        }

        Registry.CurrentUser.DeleteSubKeyTree(target.KeyPath, throwOnMissingSubKey: false);
        return $"{target.Description} ({target.KeyPath})";
    }

    private static string TrimTrailingSeparators(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
