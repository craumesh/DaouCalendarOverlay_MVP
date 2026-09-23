using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

/// <summary>
/// CHANGELOG.md·README.md·기술 문서의 버전 표기가 앱 버전(<see cref="AppVersion"/>)과 어긋나지 않는지 확인한다.
/// 저장소 파일을 읽기만 한다.
/// </summary>
public sealed class ChangelogTests
{
    [Fact]
    public void Changelog_ContainsCurrentAndInitialVersionHeadings()
    {
        var lines = ReadLines(RepositoryPaths.Combine("CHANGELOG.md"));

        Assert.Contains(lines, line => line.StartsWith($"## {AppVersion.Display} - ", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("## 7.0.0 - ", StringComparison.Ordinal));
    }

    [Fact]
    public void Documentation_CoverShowsCurrentVersion()
    {
        var html = File.ReadAllText(RepositoryPaths.Combine("docs", "DaouCalendarOverlay_Technical_Documentation.html"));

        Assert.Contains($"v{AppVersion.Display}", html, StringComparison.Ordinal);
        Assert.DoesNotContain("v7.0.0", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_HasVersionSection()
    {
        var readme = File.ReadAllText(RepositoryPaths.Combine("README.md"));

        Assert.Contains("## 버전", readme, StringComparison.Ordinal);
        Assert.Contains("CHANGELOG.md", readme, StringComparison.Ordinal);
    }

    private static string[] ReadLines(string path)
        => File.ReadAllText(path)
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .ToArray();
}
