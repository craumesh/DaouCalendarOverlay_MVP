namespace DaouCalendarOverlay.Tests;

/// <summary>
/// T2.7 .NET 10 이전 시험 기록(docs/net10-migration.md)이 필수 절과 net8/net10 비교표를 담고 있는지 고정한다.
/// 저장소 파일은 읽기만 한다.
/// </summary>
public sealed class Net10MigrationDocTests
{
    [Fact]
    public void MigrationDoc_HasRequiredSections()
    {
        var doc = ReadMigrationDoc();

        Assert.Contains("# .NET 10 이전 시험 기록", doc, StringComparison.Ordinal);
        Assert.Contains("## 1. 배경", doc, StringComparison.Ordinal);
        Assert.Contains("## 2. 시험 방법", doc, StringComparison.Ordinal);
        Assert.Contains("## 3. 비교표", doc, StringComparison.Ordinal);
        Assert.Contains("## 4. 관찰된 문제", doc, StringComparison.Ordinal);
        Assert.Contains("## 5. 결론과 권고", doc, StringComparison.Ordinal);
        Assert.Contains("## 6. 전환 체크리스트", doc, StringComparison.Ordinal);
    }

    [Fact]
    public void ComparisonTable_HasBothTargetFrameworkColumnsAndRows()
    {
        var doc = ReadMigrationDoc();

        Assert.Contains("net8.0-windows", doc, StringComparison.Ordinal);
        Assert.Contains("net10.0-windows", doc, StringComparison.Ordinal);
        Assert.Contains("Release 빌드 시간", doc, StringComparison.Ordinal);
        Assert.Contains("publish 시간", doc, StringComparison.Ordinal);
        Assert.Contains("publish EXE 크기", doc, StringComparison.Ordinal);
        Assert.Contains("host 모드 왕복", doc, StringComparison.Ordinal);
    }

    private static string ReadMigrationDoc()
    {
        var path = Path.Combine(FindRepoRoot(), "docs", "net10-migration.md");
        Assert.True(File.Exists(path), $"docs/net10-migration.md가 없습니다: {path}");
        return File.ReadAllText(path);
    }

    /// <summary>
    /// 저장소 루트 탐색은 <see cref="RepositoryPaths"/>에 위임한다(DaouCalendarOverlay.sln이 있는 디렉터리).
    /// 찾지 못하면 <see cref="RepositoryPaths"/>가 예외를 던져 테스트가 실패한다.
    /// </summary>
    private static string FindRepoRoot() => RepositoryPaths.Root;
}
