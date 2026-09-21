using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace DaouCalendarOverlay.Services;

public sealed class NativeMessagingRegistrationService
{
    private const string RegistryPath = @"Software\Google\Chrome\NativeMessagingHosts\com.daou.calendar_overlay";
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public string ManifestPath { get; }

    public NativeMessagingRegistrationService()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DaouCalendarOverlay",
            "NativeMessaging");
        ManifestPath = Path.Combine(directory, NativeBridgeProtocol.HostName + ".json");
    }

    public void EnsureRegistered()
    {
        var executablePath = Environment.ProcessPath
            ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("현재 실행 파일 경로를 확인할 수 없습니다.");

        Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath)!);

        var manifest = new
        {
            name = NativeBridgeProtocol.HostName,
            description = "Daou Calendar Overlay native messaging bridge",
            path = executablePath,
            type = "stdio",
            allowed_origins = new[] { NativeBridgeProtocol.ExtensionOrigin }
        };

        File.WriteAllText(ManifestPath, JsonSerializer.Serialize(manifest, _jsonOptions));

        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true)
            ?? throw new InvalidOperationException("Chrome Native Messaging 레지스트리 키를 만들 수 없습니다.");
        key.SetValue(null, ManifestPath, RegistryValueKind.String);
    }
}
