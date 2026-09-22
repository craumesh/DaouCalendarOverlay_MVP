using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

public sealed class PipePeerVerifierTests
{
    [Fact]
    public void IsSameExecutable_ReturnsTrue_ForIdenticalPaths()
    {
        Assert.True(PipePeerVerifier.IsSameExecutable(
            @"C:\App\DaouCalendarOverlay.exe",
            @"C:\App\DaouCalendarOverlay.exe"));
    }

    [Fact]
    public void IsSameExecutable_ReturnsTrue_IgnoringCase()
    {
        Assert.True(PipePeerVerifier.IsSameExecutable(
            @"C:\App\DaouCalendarOverlay.exe",
            @"c:\app\daoucalendaroverlay.EXE"));
    }

    [Fact]
    public void IsSameExecutable_ReturnsTrue_AfterNormalizingRelativeSegments()
    {
        Assert.True(PipePeerVerifier.IsSameExecutable(
            @"C:\App\..\App\.\DaouCalendarOverlay.exe",
            @"C:\App\DaouCalendarOverlay.exe"));
    }

    [Fact]
    public void IsSameExecutable_ReturnsFalse_ForDifferentExecutables()
    {
        Assert.False(PipePeerVerifier.IsSameExecutable(
            @"C:\App\DaouCalendarOverlay.exe",
            @"C:\Evil\DaouCalendarOverlay.exe"));
    }

    [Theory]
    [InlineData(null, @"C:\App\a.exe")]
    [InlineData("", @"C:\App\a.exe")]
    [InlineData("   ", @"C:\App\a.exe")]
    [InlineData(@"C:\App\a.exe", null)]
    [InlineData(@"C:\App\a.exe", "")]
    public void IsSameExecutable_ReturnsFalse_ForNullOrEmptyInput(string? self, string? server)
    {
        Assert.False(PipePeerVerifier.IsSameExecutable(self, server));
    }

    [Fact]
    public void IsSameExecutable_ReturnsFalse_WhenPathCannotBeNormalized()
    {
        var unnormalizable = "C:\\bad\0path.exe";

        Assert.False(PipePeerVerifier.IsSameExecutable(unnormalizable, unnormalizable));
    }

    [Fact]
    public void TryGetProcessPath_MatchesCurrentProcess()
    {
        Assert.True(PipePeerVerifier.IsSameExecutable(
            Environment.ProcessPath,
            PipePeerVerifier.TryGetProcessPath(Environment.ProcessId)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TryGetProcessPath_ReturnsNull_ForInvalidProcessId(int processId)
    {
        Assert.Null(PipePeerVerifier.TryGetProcessPath(processId));
    }

    [Fact]
    public void UntrustedPeerErrors_DoNotLeakSecrets()
    {
        var untrusted = PipePeerVerifier.BuildUntrustedPeerError(4321);
        var unverifiable = PipePeerVerifier.BuildUnverifiablePeerError();

        Assert.Contains("4321", untrusted, StringComparison.Ordinal);

        foreach (var message in new[] { untrusted, unverifiable })
        {
            Assert.DoesNotContain("cookie", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Cookie", message, StringComparison.Ordinal);
            Assert.DoesNotContain("token", message, StringComparison.OrdinalIgnoreCase);
        }
    }

    // 기술 문서 §18(장애 대응)과 README 문제 해결 항목이 인용하는 문구를 고정한다.
    // 이 문자열이 바뀌면 문서 쪽을 코드에 맞춰 갱신해야 한다.
    [Fact]
    public void UntrustedPeerError_MatchesDocumentedText()
    {
        Assert.Equal(
            "Named Pipe 서버(PID 4321)가 Daou Calendar Overlay 실행 파일이 아니어서 요청을 전송하지 않았습니다.",
            PipePeerVerifier.BuildUntrustedPeerError(4321));
        Assert.Equal(
            "Named Pipe 서버 프로세스를 확인할 수 없어 요청을 전송하지 않았습니다.",
            PipePeerVerifier.BuildUnverifiablePeerError());
    }
}
