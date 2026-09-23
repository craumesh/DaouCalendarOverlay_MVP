using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace DaouCalendarOverlay.Services;

public sealed class NativeMessagingRegistrationService
{
    /// <summary>Chrome host 키(HKCU 상대 경로). 제거 경로(<see cref="UninstallService"/>)와 같은 출처에서 만든다.</summary>
    public static readonly string ChromeRegistryPath = UninstallService.BuildNativeHostKeyPath(UninstallService.ChromeBrowserKeyRoot);

    /// <summary>Edge host 키(HKCU 상대 경로). 제거 경로(<see cref="UninstallService"/>)와 같은 출처에서 만든다.</summary>
    public static readonly string EdgeRegistryPath = UninstallService.BuildNativeHostKeyPath(UninstallService.EdgeBrowserKeyRoot);

    private const string LogCategory = "nativehost.register";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string ManifestPath { get; }

    public NativeMessagingRegistrationService()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DaouCalendarOverlay",
            "NativeMessaging");
        ManifestPath = Path.Combine(directory, NativeBridgeProtocol.HostName + ".json");
    }

    /// <summary>등록할 host 키 경로. Chrome이 항상 먼저이고, <paramref name="includeEdge"/>가 true면 Edge가 뒤따른다.</summary>
    public static IReadOnlyList<string> GetRegistryPaths(bool includeEdge) =>
        includeEdge
            ? new[] { ChromeRegistryPath, EdgeRegistryPath }
            : new[] { ChromeRegistryPath };

    /// <summary>Chrome과 Edge가 함께 쓰는 host manifest JSON.</summary>
    public static string BuildManifestJson(string executablePath)
    {
        var manifest = new
        {
            name = NativeBridgeProtocol.HostName,
            description = "Daou Calendar Overlay native messaging bridge",
            path = executablePath,
            type = "stdio",
            allowed_origins = new[] { NativeBridgeProtocol.ExtensionOrigin }
        };

        return JsonSerializer.Serialize(manifest, JsonOptions);
    }

    /// <summary>
    /// 현재 EXE 경로로 host manifest를 갱신하고 HKCU에 등록한다. Chrome 키 실패는 예외로 던지고,
    /// Edge 키 쓰기·삭제 실패는 경고 로그만 남긴다(Edge 미설치·정책 차단 환경에서도 기동을 막지 않는다).
    /// <paramref name="registerEdge"/>가 false면 Edge host 키만 제거한다.
    /// </summary>
    public void EnsureRegistered(bool registerEdge)
    {
        var executablePath = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("현재 실행 파일 경로를 확인할 수 없습니다.");

        Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath)!);
        File.WriteAllText(ManifestPath, BuildManifestJson(executablePath));

        foreach (var path in GetRegistryPaths(registerEdge))
        {
            if (string.Equals(path, ChromeRegistryPath, StringComparison.Ordinal))
            {
                using var chromeKey = Registry.CurrentUser.CreateSubKey(path, writable: true)
                    ?? throw new InvalidOperationException("Chrome Native Messaging 레지스트리 키를 만들 수 없습니다.");
                chromeKey.SetValue(null, ManifestPath, RegistryValueKind.String);
                continue;
            }

            try
            {
                using var edgeKey = Registry.CurrentUser.CreateSubKey(path, writable: true)
                    ?? throw new InvalidOperationException("Edge Native Messaging 레지스트리 키를 만들 수 없습니다.");
                edgeKey.SetValue(null, ManifestPath, RegistryValueKind.String);
            }
            catch (Exception ex)
            {
                LogService.Warn(LogCategory, "Edge Native Messaging 등록 실패", ex);
            }
        }

        if (!registerEdge)
            RemoveEdgeRegistration();
    }

    /// <summary>
    /// Edge host 키 하나만 지운다. 상위 NativeMessagingHosts 키와 Chrome 키는 지우지 않는다.
    /// 키가 없으면 아무것도 하지 않고, 실패는 경고 로그만 남긴다.
    /// </summary>
    private static void RemoveEdgeRegistration()
    {
        try
        {
            using (var existing = Registry.CurrentUser.OpenSubKey(EdgeRegistryPath))
            {
                if (existing is null)
                    return;
            }

            Registry.CurrentUser.DeleteSubKeyTree(EdgeRegistryPath, throwOnMissingSubKey: false);
            LogService.Info(LogCategory, "Edge Native Messaging 등록 해제");
        }
        catch (Exception ex)
        {
            LogService.Warn(LogCategory, "Edge Native Messaging 등록 해제 실패", ex);
        }
    }
}
