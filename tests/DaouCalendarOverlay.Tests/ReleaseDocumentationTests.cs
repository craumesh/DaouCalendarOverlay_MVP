namespace DaouCalendarOverlay.Tests;

/// <summary>
/// README와 기술 문서가 빌드·배포 구성(개발 빌드 framework-dependent, publish 프로파일 분리,
/// 버전 산출물명, PDB 분리, 서명 파라미터)과 릴리스 절차 절을 담고 있는지 고정한다.
/// 절 번호 리터럴은 단언하지 않는다. 저장소 파일은 읽기만 한다.
/// </summary>
public sealed class ReleaseDocumentationTests
{
    private static readonly string TechnicalDocPath =
        RepoLayout.Path("docs", "DaouCalendarOverlay_Technical_Documentation.html");

    private static readonly string ReadmePath = RepoLayout.Path("README.md");

    [Fact]
    public void TechnicalDoc_BuildSection_ReflectsPublishProfileSplit()
    {
        var html = File.ReadAllText(TechnicalDocPath);

        Assert.Contains("win-x64.pubxml", html, StringComparison.Ordinal);
        Assert.DoesNotContain("DebugType&gt;None", html, StringComparison.Ordinal);
        Assert.Contains("DebugType&gt;portable", html, StringComparison.Ordinal);
        Assert.Contains("<h3>16.1 Publish</h3>", html, StringComparison.Ordinal);
        Assert.Contains("<h3>16.2 Extension 포함</h3>", html, StringComparison.Ordinal);
        Assert.Contains("<h3>16.3 Publish 프로파일</h3>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnicalDoc_HasReleaseProcedureSection()
    {
        var html = File.ReadAllText(TechnicalDocPath);

        Assert.Contains("id=\"release\"", html, StringComparison.Ordinal);
        Assert.Contains("빌드 전제조건·릴리스 절차", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#release\"", html, StringComparison.Ordinal);

        // 신규 절이 추가돼도 기존 절은 그대로 남아 있어야 한다.
        Assert.Contains("id=\"appendix\"", html, StringComparison.Ordinal);
        Assert.Contains("20. 핵심 상수와 운영 경로", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_DocumentsVersionedArtifactAndSymbols()
    {
        var readme = File.ReadAllText(ReadmePath);

        Assert.Contains("-CertificateThumbprint", readme, StringComparison.Ordinal);
        Assert.Contains("-DryRun", readme, StringComparison.Ordinal);
        Assert.Contains("symbols", readme, StringComparison.Ordinal);
        Assert.Contains("DaouCalendarOverlay-", readme, StringComparison.Ordinal);
    }
}
