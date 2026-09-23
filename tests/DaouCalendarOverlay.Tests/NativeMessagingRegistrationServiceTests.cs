using System.Text.Json;
using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

// 이 파일의 테스트는 레지스트리를 건드리지 않는다. 경로 계산과 manifest JSON 생성 같은 순수 함수만 검증한다.
public sealed class NativeMessagingRegistrationServiceTests
{
    [Fact]
    public void GetRegistryPaths_IncludeEdge_ReturnsChromeAndEdgePaths()
    {
        var paths = NativeMessagingRegistrationService.GetRegistryPaths(includeEdge: true);

        Assert.Equal(2, paths.Count);
        Assert.Equal(NativeMessagingRegistrationService.ChromeRegistryPath, paths[0]);
        Assert.Equal(NativeMessagingRegistrationService.EdgeRegistryPath, paths[1]);
    }

    [Fact]
    public void GetRegistryPaths_WithoutEdge_ReturnsChromeOnly()
    {
        var paths = NativeMessagingRegistrationService.GetRegistryPaths(includeEdge: false);

        Assert.Single(paths);
        Assert.Equal(NativeMessagingRegistrationService.ChromeRegistryPath, paths[0]);
        Assert.DoesNotContain(NativeMessagingRegistrationService.EdgeRegistryPath, paths);
    }

    [Fact]
    public void RegistryPaths_EndWithHostName()
    {
        var chrome = NativeMessagingRegistrationService.ChromeRegistryPath;
        var edge = NativeMessagingRegistrationService.EdgeRegistryPath;

        Assert.EndsWith("\\" + NativeBridgeProtocol.HostName, chrome, StringComparison.Ordinal);
        Assert.EndsWith("\\" + NativeBridgeProtocol.HostName, edge, StringComparison.Ordinal);
        Assert.StartsWith(@"Software\Google\Chrome\NativeMessagingHosts\", chrome, StringComparison.Ordinal);
        Assert.StartsWith(@"Software\Microsoft\Edge\NativeMessagingHosts\", edge, StringComparison.Ordinal);

        // 기존 private const RegistryPath와 같은 값이어야 한다(이미 등록된 사용자 환경과의 역호환).
        Assert.Equal(@"Software\Google\Chrome\NativeMessagingHosts\com.daou.calendar_overlay", chrome);
    }

    [Fact]
    public void RegistryPaths_MatchUninstallTargets()
    {
        var targets = UninstallService.GetRegistryTargets();

        Assert.Equal(NativeMessagingRegistrationService.ChromeRegistryPath, targets[1].KeyPath);
        Assert.Equal(NativeMessagingRegistrationService.EdgeRegistryPath, targets[2].KeyPath);
    }

    [Fact]
    public void BuildManifestJson_ContainsHostNameAndExecutablePath()
    {
        const string executablePath = @"C:\tmp\DaouCalendarOverlay.exe";

        using var document = JsonDocument.Parse(NativeMessagingRegistrationService.BuildManifestJson(executablePath));
        var root = document.RootElement;

        Assert.Equal(NativeBridgeProtocol.HostName, root.GetProperty("name").GetString());
        Assert.Equal(executablePath, root.GetProperty("path").GetString());
        Assert.Equal("stdio", root.GetProperty("type").GetString());
    }

    [Fact]
    public void BuildManifestJson_ContainsExtensionOrigin()
    {
        using var document = JsonDocument.Parse(NativeMessagingRegistrationService.BuildManifestJson(@"C:\tmp\DaouCalendarOverlay.exe"));
        var origins = document.RootElement.GetProperty("allowed_origins");

        Assert.Equal(JsonValueKind.Array, origins.ValueKind);
        Assert.Equal(1, origins.GetArrayLength());
        Assert.Equal(NativeBridgeProtocol.ExtensionOrigin, origins[0].GetString());
    }
}
