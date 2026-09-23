using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

// 이 파일의 테스트는 실제 레지스트리를 읽거나 쓰지 않는다. 경로 계산·가드·결과 변환 같은 순수 로직만 검증하고,
// 파일 삭제는 임시 디렉터리 안에서만 한다.
public sealed class UninstallServiceTests
{
    [Fact]
    public void BuildNativeHostKeyPath_ChromeRoot_ReturnsHostKeyPath()
    {
        Assert.Equal(
            @"Software\Google\Chrome\NativeMessagingHosts\com.daou.calendar_overlay",
            UninstallService.BuildNativeHostKeyPath(UninstallService.ChromeBrowserKeyRoot));

        // 앞뒤 역슬래시는 잘라낸다.
        Assert.Equal(
            @"Software\Google\Chrome\NativeMessagingHosts\com.daou.calendar_overlay",
            UninstallService.BuildNativeHostKeyPath(@"\Software\Google\Chrome\"));
    }

    [Fact]
    public void BuildNativeHostKeyPath_EdgeRoot_UsesHostNameConstant()
    {
        var path = UninstallService.BuildNativeHostKeyPath(UninstallService.EdgeBrowserKeyRoot);

        Assert.StartsWith(@"Software\Microsoft\Edge\NativeMessagingHosts\", path, StringComparison.Ordinal);
        Assert.EndsWith(NativeBridgeProtocol.HostName, path, StringComparison.Ordinal);
        Assert.Equal(@"Software\Microsoft\Edge\NativeMessagingHosts\" + NativeBridgeProtocol.HostName, path);
    }

    [Fact]
    public void BuildNativeHostKeyPath_BlankRoot_Throws()
    {
        Assert.Throws<ArgumentException>(() => UninstallService.BuildNativeHostKeyPath(null!));
        Assert.Throws<ArgumentException>(() => UninstallService.BuildNativeHostKeyPath(""));
        Assert.Throws<ArgumentException>(() => UninstallService.BuildNativeHostKeyPath("   "));
        Assert.Throws<ArgumentException>(() => UninstallService.BuildNativeHostKeyPath(@"\\"));
    }

    [Fact]
    public void GetRegistryTargets_ReturnsRunValueAndChromeAndEdgeHostKeys()
    {
        var targets = UninstallService.GetRegistryTargets();

        Assert.Equal(3, targets.Count);

        // StartupService가 쓰는 키·값 이름과 같아야 한다(StartupService의 상수는 private이라 리터럴로 고정한다).
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Run", UninstallService.RunKeyPath);
        Assert.Equal(UninstallService.RunKeyPath, targets[0].KeyPath);
        Assert.Equal("DaouCalendarOverlay", targets[0].ValueName);

        Assert.Null(targets[1].ValueName);
        Assert.Equal(@"Software\Google\Chrome\NativeMessagingHosts\com.daou.calendar_overlay", targets[1].KeyPath);

        Assert.Null(targets[2].ValueName);
        Assert.Equal(@"Software\Microsoft\Edge\NativeMessagingHosts\com.daou.calendar_overlay", targets[2].KeyPath);
    }

    [Fact]
    public void GetUserDataDirectory_IsLocalAppDataDaouCalendarOverlay()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DaouCalendarOverlay");

        Assert.Equal(expected, UninstallService.GetUserDataDirectory());
    }

    [Fact]
    public void TryRemoveUserData_DeletesDirectoryTree()
    {
        var parent = CreateTempParentPath();
        var directory = Path.Combine(parent, "DaouCalendarOverlay");
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "logs"));
            Directory.CreateDirectory(Path.Combine(directory, "ChromeExtension"));
            File.WriteAllText(Path.Combine(directory, "settings.json"), "{}");
            File.WriteAllText(Path.Combine(directory, "logs", "overlay-20260923.log"), "line");
            File.WriteAllText(Path.Combine(directory, "ChromeExtension", "manifest.json"), "{}");

            var ok = UninstallService.TryRemoveUserData(directory, out var error);

            Assert.True(ok);
            Assert.Null(error);
            Assert.False(Directory.Exists(directory));
            // 대상 폴더만 지우고 부모는 건드리지 않는다.
            Assert.True(Directory.Exists(parent));
        }
        finally
        {
            DeleteIfExists(parent);
        }
    }

    [Fact]
    public void TryRemoveUserData_MissingDirectory_ReturnsTrue()
    {
        var parent = CreateTempParentPath();
        var directory = Path.Combine(parent, "DaouCalendarOverlay");
        try
        {
            var ok = UninstallService.TryRemoveUserData(directory, out var error);

            Assert.True(ok);
            Assert.Null(error);
            Assert.False(Directory.Exists(parent));
        }
        finally
        {
            DeleteIfExists(parent);
        }
    }

    [Fact]
    public void TryRemoveUserData_DriveRoot_ReturnsFalse()
    {
        var root = Path.GetPathRoot(Path.GetTempPath())!;

        var ok = UninstallService.TryRemoveUserData(root, out var error);

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.True(Directory.Exists(root));
    }

    [Fact]
    public void TryRemoveUserData_OtherFolderName_ReturnsFalse()
    {
        var parent = CreateTempParentPath();
        var directory = Path.Combine(parent, "NotOurs");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "keep.txt"), "keep");

            var ok = UninstallService.TryRemoveUserData(directory, out var error);

            Assert.False(ok);
            Assert.False(string.IsNullOrWhiteSpace(error));
            Assert.True(Directory.Exists(directory));
            Assert.True(File.Exists(Path.Combine(directory, "keep.txt")));

            // 이름이 맞는 폴더의 상위(..)로 빠져나가는 경로도 이름 가드에 걸린다.
            var escaping = Path.Combine(parent, "DaouCalendarOverlay", "..", "NotOurs");
            Assert.False(UninstallService.TryRemoveUserData(escaping, out _));
            Assert.True(Directory.Exists(directory));

            // 상대 경로는 현재 디렉터리 기준으로 풀리므로 거부한다. 가드가 깨져도 아무것도 지우지 않도록
            // 존재하지 않는 경로를 쓴다(가드가 없으면 "이미 없음"으로 true가 되어 테스트가 실패한다).
            var relative = Path.Combine("DaouCalendarOverlayTest_" + Guid.NewGuid().ToString("N"), "DaouCalendarOverlay");
            Assert.False(UninstallService.TryRemoveUserData(relative, out var relativeError));
            Assert.False(string.IsNullOrWhiteSpace(relativeError));
        }
        finally
        {
            DeleteIfExists(parent);
        }
    }

    [Fact]
    public void ToExitCode_NoFailures_ReturnsZero()
    {
        Assert.Equal(0, UninstallService.ToExitCode(new UninstallResult(Array.Empty<string>(), Array.Empty<string>())));
        Assert.Equal(0, UninstallService.ToExitCode(new UninstallResult(new[] { "A" }, Array.Empty<string>())));
    }

    [Fact]
    public void ToExitCode_WithFailures_ReturnsOne()
    {
        var result = new UninstallResult(new[] { "A" }, new[] { "B: 오류" });

        Assert.True(result.HasFailures);
        Assert.Equal(1, UninstallService.ToExitCode(result));
    }

    [Fact]
    public void BuildSummary_WithRemovedAndFailedItems_ListsBoth()
    {
        var summary = UninstallService.BuildSummary(new UninstallResult(new[] { "A" }, new[] { "B: 오류" }));

        Assert.Contains("제거됨:", summary, StringComparison.Ordinal);
        Assert.Contains("- A", summary, StringComparison.Ordinal);
        Assert.Contains("실패:", summary, StringComparison.Ordinal);
        Assert.Contains("- B: 오류", summary, StringComparison.Ordinal);

        // 제거 목록이 실패 목록보다 먼저 나온다.
        Assert.True(
            summary.IndexOf("제거됨:", StringComparison.Ordinal) < summary.IndexOf("실패:", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildSummary_Empty_ReturnsNothingRemovedMessage()
    {
        var summary = UninstallService.BuildSummary(new UninstallResult(Array.Empty<string>(), Array.Empty<string>()));

        Assert.Equal("제거할 항목이 없습니다.", summary);
    }

    [Fact]
    public void BuildSummary_RemovedOnly_DoesNotContainFailureHeader()
    {
        var summary = UninstallService.BuildSummary(new UninstallResult(new[] { "A", "B" }, Array.Empty<string>()));

        Assert.Contains("제거됨:", summary, StringComparison.Ordinal);
        Assert.Contains("- A", summary, StringComparison.Ordinal);
        Assert.Contains("- B", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("실패:", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void GetRegistryTargets_DescriptionsAreNonEmptyAndDistinct()
    {
        var targets = UninstallService.GetRegistryTargets();

        Assert.Equal(3, targets.Count);
        Assert.All(targets, target => Assert.False(string.IsNullOrWhiteSpace(target.Description)));
        Assert.Equal(3, targets.Select(target => target.Description).Distinct(StringComparer.Ordinal).Count());
    }

    private static string CreateTempParentPath() =>
        Path.Combine(Path.GetTempPath(), "DaouCalendarOverlayTest_" + Guid.NewGuid().ToString("N"));

    private static void DeleteIfExists(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
