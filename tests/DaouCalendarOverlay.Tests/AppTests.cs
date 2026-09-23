namespace DaouCalendarOverlay.Tests;

/// <summary>
/// App(WPF)은 인스턴스화하지 않고 <c>DaouCalendarOverlay/App.xaml.cs</c> 소스를 읽어
/// 동기화 결과 처리(<c>BridgeServer_SyncCompleted</c>)의 재로그인 배너 규칙을 고정한다.
/// 확장 버전 불일치(<c>BridgeFailureKind.Extension</c>)는 getConfig마다 올라오는 신호이므로
/// 재로그인 배너를 끄지 않아야 한다. 저장소 파일을 읽기만 한다.
/// </summary>
public sealed class AppTests
{
    private const string SetLoginRequiredCall = "SetLoginRequired(";

    [Fact]
    public void SyncCompleted_ExtensionFailure_DoesNotTouchLoginBanner()
    {
        var handler = ReadSyncCompletedHandler();
        var switchStart = IndexOrFail(handler, "switch (e.FailureKind)", 0);

        // 재로그인 분기의 return 뒤부터 switch 앞까지는 모든 실패 종류가 지나가는 공통 경로다. 여기서 배너를 끄면 안 된다.
        var authStart = IndexOrFail(handler, "if (e.AuthenticationRequired)", 0);
        var authReturn = IndexOrFail(handler, "return;", authStart);
        Assert.True(authReturn < switchStart, "재로그인 분기가 switch보다 앞에 있어야 합니다.");
        var commonPath = handler.Substring(authReturn, switchStart - authReturn);
        Assert.DoesNotContain(SetLoginRequiredCall, commonPath, StringComparison.Ordinal);

        var extensionSection = CaseSection(handler, "case BridgeFailureKind.Extension:", switchStart);
        Assert.Contains("MarkExtensionVersionMismatch", extensionSection, StringComparison.Ordinal);
        Assert.DoesNotContain(SetLoginRequiredCall, extensionSection, StringComparison.Ordinal);
    }

    [Fact]
    public void SyncCompleted_NetworkAndGeneralFailures_StillClearLoginBanner()
    {
        var handler = ReadSyncCompletedHandler();
        var switchStart = IndexOrFail(handler, "switch (e.FailureKind)", 0);

        var networkSection = CaseSection(handler, "case BridgeFailureKind.Network:", switchStart);
        Assert.Contains("SetLoginRequired(false)", networkSection, StringComparison.Ordinal);

        var defaultSection = CaseSection(handler, "default:", switchStart);
        Assert.Contains("SetLoginRequired(false)", defaultSection, StringComparison.Ordinal);
    }

    private static string ReadSyncCompletedHandler()
    {
        var source = File.ReadAllText(RepositoryPaths.Combine("DaouCalendarOverlay", "App.xaml.cs"));
        var start = IndexOrFail(source, "private void BridgeServer_SyncCompleted(", 0);
        var end = IndexOrFail(source, "private void SyncStatus_StatusChanged(", start);
        return source.Substring(start, end - start);
    }

    /// <summary>case 레이블부터 그 case의 첫 <c>break;</c>까지를 돌려준다.</summary>
    private static string CaseSection(string handler, string label, int from)
    {
        var start = IndexOrFail(handler, label, from);
        var end = IndexOrFail(handler, "break;", start);
        return handler.Substring(start, end - start);
    }

    private static int IndexOrFail(string text, string value, int from)
    {
        var index = text.IndexOf(value, from, StringComparison.Ordinal);
        Assert.True(index >= 0, $"App.xaml.cs에서 '{value}'를 찾지 못했습니다(구조가 바뀌었으면 이 가드를 함께 갱신).");
        return index;
    }
}
