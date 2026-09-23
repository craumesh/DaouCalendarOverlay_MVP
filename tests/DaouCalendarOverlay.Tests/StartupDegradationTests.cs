using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

/// <summary>
/// 기동 시 확장 파일 추출·Native host 등록 실패가 앱을 종료시키지 않고 상태 문구로만 알려지는 경로를 고정하는 테스트.
/// App(WPF)은 인스턴스화하지 않고, App이 쓰는 조합(<see cref="StartupFailureReasons.Describe"/> 결과를
/// <see cref="SyncStatusService.MarkStartupComponentError"/>에 넘기는 경로)을 그대로 재현한다.
/// 실제 레지스트리·LocalAppData는 건드리지 않는다.
/// </summary>
public sealed class StartupDegradationTests
{
    /// <summary>레지스트리 쓰기 거부는 권한 사유 문구로 게시되고, 이후 대기 문구에 등록 실패 접미가 붙는다.</summary>
    [Fact]
    public void NativeHostRegistrationDenied_ProducesStatusTextAndWaitingSuffix()
    {
        var svc = new SyncStatusService();
        SyncStatusChangedEventArgs? captured = null;
        svc.StatusChanged += (_, e) => captured = e;

        svc.MarkStartupComponentError(
            StartupFailureReasons.NativeHostComponent,
            StartupFailureReasons.Describe(new UnauthorizedAccessException("registry denied")));

        Assert.NotNull(captured);
        Assert.Equal(OverlaySyncState.GeneralError, captured!.State);
        Assert.Equal("Native host 등록 실패: 쓰기 권한이 없습니다(정책 또는 ACL 제한) · 로그 확인", captured.Text);
        Assert.True(captured.IsError);

        svc.MarkWaiting();

        Assert.Equal("Chrome 백그라운드 대기 · Native host 등록 실패", captured!.Text);
    }

    /// <summary>확장 리소스를 찾지 못한 추출 실패는 리소스 사유 문구로 게시된다.</summary>
    [Fact]
    public void ExtensionExtractionFailure_ProducesStatusText()
    {
        var svc = new SyncStatusService();
        SyncStatusChangedEventArgs? captured = null;
        svc.StatusChanged += (_, e) => captured = e;

        svc.MarkStartupComponentError(
            StartupFailureReasons.ExtensionComponent,
            StartupFailureReasons.Describe(new InvalidOperationException("Chrome extension resource not found: x")));

        Assert.NotNull(captured);
        Assert.Equal(OverlaySyncState.GeneralError, captured!.State);
        Assert.Equal("확장 파일 설치 실패: 필요한 리소스를 찾지 못했습니다 · 로그 확인", captured.Text);
        Assert.True(captured.IsError);
    }

    /// <summary>두 구성 요소가 모두 실패하면 대기 문구 접미는 App의 호출 순서(확장 추출 → 등록)를 따른다.</summary>
    [Fact]
    public void BothComponentsFail_WaitingSuffixKeepsCallOrder()
    {
        var svc = new SyncStatusService();
        SyncStatusChangedEventArgs? captured = null;
        svc.StatusChanged += (_, e) => captured = e;

        svc.MarkStartupComponentError(
            StartupFailureReasons.ExtensionComponent,
            StartupFailureReasons.Describe(new InvalidOperationException("Chrome extension resource not found: x")));
        svc.MarkStartupComponentError(
            StartupFailureReasons.NativeHostComponent,
            StartupFailureReasons.Describe(new UnauthorizedAccessException("registry denied")));
        svc.MarkWaiting();

        Assert.NotNull(captured);
        Assert.Equal("Chrome 백그라운드 대기 · 확장 파일 설치 실패 · Native host 등록 실패", captured!.Text);
    }

    /// <summary>설정 저장 등으로 재등록이 성공하면 대기 문구의 등록 실패 접미가 사라진다.</summary>
    [Fact]
    public void RegistrationRetrySucceeds_RemovesWaitingSuffix()
    {
        var svc = new SyncStatusService();
        SyncStatusChangedEventArgs? captured = null;
        svc.StatusChanged += (_, e) => captured = e;

        svc.MarkStartupComponentError(
            StartupFailureReasons.NativeHostComponent,
            StartupFailureReasons.Describe(new UnauthorizedAccessException("registry denied")));
        svc.MarkStartupComponentResolved(StartupFailureReasons.NativeHostComponent);
        svc.MarkWaiting();

        Assert.NotNull(captured);
        Assert.Equal("Chrome 백그라운드 대기", captured!.Text);
    }
}
