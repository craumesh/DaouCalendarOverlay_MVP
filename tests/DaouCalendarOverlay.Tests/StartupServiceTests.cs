using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

/// <summary>
/// 시작 프로그램 등록(HKCU Run) 키·값 이름이 제거 경로(<see cref="UninstallService"/>)와 같은 출처를 쓰는지 고정한다.
/// 실제 레지스트리를 읽거나 쓰지 않는다(<see cref="StartupService.Apply"/>는 호출하지 않는다). 저장소 소스 파일은 읽기만 한다.
/// </summary>
public sealed class StartupServiceTests
{
    private const string RunKeyLiteralFragment = @"CurrentVersion\Run";

    [Fact]
    public void RunKey_SharesUninstallServiceConstants()
    {
        Assert.Equal(UninstallService.RunKeyPath, StartupService.RunKeyPath);
        Assert.Equal(UninstallService.RunValueName, StartupService.RunValueName);

        // 제거 대상의 첫 항목(Run 값)이 StartupService가 등록하는 키·값과 같아야 한다.
        var runTarget = UninstallService.GetRegistryTargets()[0];
        Assert.Equal(StartupService.RunKeyPath, runTarget.KeyPath);
        Assert.Equal(StartupService.RunValueName, runTarget.ValueName);
    }

    [Fact]
    public void RunKeyLiteral_IsDefinedOnlyInUninstallService()
    {
        var sourceRoot = RepositoryPaths.Combine("DaouCalendarOverlay");
        var files = EnumerateProductionSources(sourceRoot);

        // 스캔이 비어 있으면 가드가 무의미해진다. 두 서비스 파일이 스캔 범위에 들어오는지 확인한다.
        var startupFile = Assert.Single(files, file => file.EndsWith(Path.Combine("Services", "StartupService.cs"), StringComparison.OrdinalIgnoreCase));
        Assert.Single(files, file => file.EndsWith(Path.Combine("Services", "UninstallService.cs"), StringComparison.OrdinalIgnoreCase));

        var occurrences = files
            .SelectMany(file => Enumerable.Repeat(file, CountOccurrences(File.ReadAllText(file), RunKeyLiteralFragment)))
            .ToList();

        // Run 키 경로 리터럴은 UninstallService.RunKeyPath 한 곳에만 있어야 한다.
        var only = Assert.Single(occurrences);
        Assert.EndsWith(Path.Combine("Services", "UninstallService.cs"), only, StringComparison.OrdinalIgnoreCase);

        // StartupService는 자체 리터럴 없이 UninstallService 상수를 참조한다.
        var startupSource = File.ReadAllText(startupFile);
        Assert.Contains("UninstallService.RunKeyPath", startupSource, StringComparison.Ordinal);
        Assert.Contains("UninstallService.RunValueName", startupSource, StringComparison.Ordinal);
        Assert.DoesNotContain("\"DaouCalendarOverlay\"", startupSource, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string fragment)
    {
        var count = 0;
        for (var index = text.IndexOf(fragment, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(fragment, index + fragment.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    private static IReadOnlyList<string> EnumerateProductionSources(string sourceRoot)
    {
        Assert.True(Directory.Exists(sourceRoot), $"source root not found: {sourceRoot}");

        return Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(sourceRoot, file))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsBuildOutput(string sourceRoot, string file)
    {
        var relative = Path.GetRelativePath(sourceRoot, file);
        var segments = relative.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        return segments.Any(segment =>
            string.Equals(segment, "bin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(segment, "obj", StringComparison.OrdinalIgnoreCase));
    }
}
