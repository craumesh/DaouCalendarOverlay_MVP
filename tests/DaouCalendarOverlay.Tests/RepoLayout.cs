namespace DaouCalendarOverlay.Tests;

/// <summary>
/// 빌드·배포 관련 테스트가 저장소 안 파일(csproj, pubxml, publish.ps1, 문서)을 읽기 위한 경로 헬퍼.
/// 저장소 루트 탐색은 <see cref="RepositoryPaths"/>에 위임한다. 읽기 전용으로만 쓴다.
/// </summary>
internal static class RepoLayout
{
    internal static string Root => RepositoryPaths.Root;

    internal static string Path(params string[] segments)
        => RepositoryPaths.Combine(segments);
}
