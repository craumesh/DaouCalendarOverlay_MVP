namespace DaouCalendarOverlay.Tests;

/// <summary>
/// T2.10에서 삭제한 미사용 멤버·리소스가 프로덕션 소스에 다시 들어오지 않도록 막는 가드.
/// 실행 파일 경로 획득을 Environment.ProcessPath로 통일한 상태도 함께 고정한다.
/// 스캔 대상은 저장소의 DaouCalendarOverlay 폴더 아래 *.cs, *.xaml이며 bin/obj 산출물은 제외한다.
/// tests/와 docs/는 스캔하지 않는다(이 파일 자신과 작업 지시서의 근거 문장이 걸리지 않게 하기 위해).
/// 저장소 루트 탐색은 <see cref="RepositoryPaths"/>를 재사용한다.
/// </summary>
public sealed class DeadCodeGuardTests
{
    private static readonly string[] RemovedSymbols =
    {
        "HasButtonAncestor",
        "MoreButtonStyle",
    };

    [Fact]
    public void RemovedMembers_AreNotReferencedInProductionSources()
    {
        var files = EnumerateProductionSources("*.cs", "*.xaml");

        // 스캔이 비어 있으면 가드가 무의미해진다. 삭제 대상이 있던 두 파일이 스캔 범위에 들어오는지 확인한다.
        Assert.Contains(files, file => file.EndsWith(Path.Combine("Views", "MainWindow.xaml.cs"), StringComparison.OrdinalIgnoreCase));
        Assert.Contains(files, file => string.Equals(Path.GetFileName(file), "App.xaml", StringComparison.OrdinalIgnoreCase));

        var violations = new List<string>();

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            foreach (var symbol in RemovedSymbols)
            {
                if (text.Contains(symbol, StringComparison.Ordinal))
                    violations.Add($"{file} ({symbol})");
            }
        }

        Assert.True(
            violations.Count == 0,
            "삭제한 멤버가 프로덕션 소스에서 다시 참조됨:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    /// <summary>
    /// 현재 프로세스의 실행 파일 경로는 <c>Environment.ProcessPath</c>로만 얻는다.
    /// <c>MainModule</c>은 다른 프로세스(파이프 서버)의 경로를 조회하는 PipePeerVerifier에만 남아야 한다.
    /// </summary>
    [Fact]
    public void ExecutablePathLookup_UsesEnvironmentProcessPath()
    {
        const string allowedFileName = "PipePeerVerifier.cs";
        var files = EnumerateProductionSources("*.cs");

        var matches = files
            .Where(file => File.ReadAllText(file).Contains("MainModule", StringComparison.Ordinal))
            .ToList();

        var violations = matches
            .Where(file => !string.Equals(Path.GetFileName(file), allowedFileName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(
            violations.Count == 0,
            "MainModule로 실행 파일 경로를 얻는 프로덕션 소스가 남아 있음(Environment.ProcessPath를 써야 함):" +
            Environment.NewLine + string.Join(Environment.NewLine, violations));

        // 허용 대상도 정확히 하나여야 한다(Services/PipePeerVerifier.cs). 사라지거나 늘면 가드 전제가 바뀐 것이다.
        var allowed = Assert.Single(matches);
        Assert.True(
            allowed.EndsWith(Path.Combine("Services", allowedFileName), StringComparison.OrdinalIgnoreCase),
            $"MainModule 허용 파일 위치가 예상과 다름: {allowed}");
    }

    private static IReadOnlyList<string> EnumerateProductionSources(params string[] patterns)
    {
        var sourceRoot = RepositoryPaths.Combine("DaouCalendarOverlay");
        Assert.True(Directory.Exists(sourceRoot), $"source root not found: {sourceRoot}");

        return patterns
            .SelectMany(pattern => Directory.EnumerateFiles(sourceRoot, pattern, SearchOption.AllDirectories))
            .Where(file => !IsBuildOutput(sourceRoot, file))
            .Distinct(StringComparer.OrdinalIgnoreCase)
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
