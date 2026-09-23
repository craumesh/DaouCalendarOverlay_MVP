namespace DaouCalendarOverlay.Tests;

/// <summary>
/// 테스트에서 저장소 안 파일(app.manifest, 문서 등)을 읽기 위한 경로 헬퍼. 읽기 전용으로만 쓴다.
/// </summary>
internal static class RepositoryPaths
{
    internal static string Root { get; } = FindRoot();

    internal static string Combine(params string[] parts)
        => Path.Combine(new[] { Root }.Concat(parts).ToArray());

    /// <summary>
    /// 테스트 출력 폴더에서 상위로 올라가며 DaouCalendarOverlay.sln이 있는 저장소 루트를 찾는다.
    /// </summary>
    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "DaouCalendarOverlay.sln")))
                return dir.FullName;
        }

        throw new InvalidOperationException(
            $"DaouCalendarOverlay.sln이 있는 저장소 루트를 찾지 못했습니다. 시작 위치: {AppContext.BaseDirectory}");
    }
}
