using System.Reflection;
using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay.Tests;

/// <summary>
/// CLI 제거 흐름(<see cref="UninstallFlow.RunCli"/>)의 오버레이 실행 중 차단을 고정한다.
/// 실제 뮤텍스·레지스트리·MessageBox를 쓰지 않는다. 실행 여부 확인, 안내 창, 확인 창+제거는 모두 가짜 델리게이트로 주입한다.
/// </summary>
public sealed class UninstallFlowTests
{
    [Fact]
    public void RunCli_OverlayRunning_ShowsNoticeAndDoesNotUninstall()
    {
        var notices = 0;
        var uninstalls = 0;

        var exitCode = UninstallFlow.RunCli(
            isOverlayRunning: () => true,
            showOverlayRunning: () => notices++,
            confirmAndUninstall: () =>
            {
                uninstalls++;
                return 0;
            });

        Assert.Equal(UninstallService.OverlayRunningExitCode, exitCode);
        Assert.NotEqual(0, exitCode);
        Assert.Equal(1, notices);
        Assert.Equal(0, uninstalls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void RunCli_OverlayNotRunning_RunsUninstallAndReturnsItsExitCode(int uninstallExitCode)
    {
        var notices = 0;
        var uninstalls = 0;

        var exitCode = UninstallFlow.RunCli(
            isOverlayRunning: () => false,
            showOverlayRunning: () => notices++,
            confirmAndUninstall: () =>
            {
                uninstalls++;
                return uninstallExitCode;
            });

        Assert.Equal(uninstallExitCode, exitCode);
        Assert.Equal(0, notices);
        Assert.Equal(1, uninstalls);
    }

    [Fact]
    public void RunCli_ProbeThrows_BlocksWithoutUninstall()
    {
        var notices = 0;
        var uninstalls = 0;

        var exitCode = UninstallFlow.RunCli(
            isOverlayRunning: () => throw new IOException("probe failed"),
            showOverlayRunning: () => notices++,
            confirmAndUninstall: () =>
            {
                uninstalls++;
                return 0;
            });

        Assert.Equal(UninstallService.OverlayRunningExitCode, exitCode);
        Assert.Equal(1, notices);
        Assert.Equal(0, uninstalls);
    }

    [Fact]
    public void RunCli_NoticeThrows_StillReturnsOverlayRunningExitCode()
    {
        var uninstalls = 0;

        var exitCode = UninstallFlow.RunCli(
            isOverlayRunning: () => true,
            showOverlayRunning: () => throw new InvalidOperationException("no UI"),
            confirmAndUninstall: () =>
            {
                uninstalls++;
                return 0;
            });

        Assert.Equal(UninstallService.OverlayRunningExitCode, exitCode);
        Assert.Equal(0, uninstalls);
    }

    [Fact]
    public void RunCli_UninstallThrows_ReturnsOne()
    {
        var exitCode = UninstallFlow.RunCli(
            isOverlayRunning: () => false,
            showOverlayRunning: () => { },
            confirmAndUninstall: () => throw new InvalidOperationException("boom"));

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public void OverlayRunningMessage_TellsToExitOverlayOrUseTrayRemoval()
    {
        Assert.Contains("아무것도 지우지 않았습니다", UninstallFlow.OverlayRunningMessage, StringComparison.Ordinal);
        Assert.Contains("종료", UninstallFlow.OverlayRunningMessage, StringComparison.Ordinal);
        Assert.Contains("완전 제거…", UninstallFlow.OverlayRunningMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void RunInteractive_UsesSingleInstanceMutexProbe()
    {
        var runInteractive = typeof(UninstallFlow).GetMethod(nameof(UninstallFlow.RunInteractive), BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(runInteractive);

        var referenced = GetReferencedMethods(runInteractive!);

        Assert.Contains(referenced, method => method.DeclaringType == typeof(SingleInstanceService)
            && method.Name == nameof(SingleInstanceService.IsPrimaryRunning));
        Assert.Contains(referenced, method => method.DeclaringType == typeof(UninstallFlow)
            && method.Name == nameof(UninstallFlow.RunCli));
    }

    [Fact]
    public void TrayUninstall_DoesNotApplyCliOverlayCheck()
    {
        // 트레이 "완전 제거…"는 오버레이 자신이 실행 중일 때 돈다. CLI 검사가 들어가면 항상 막히므로 쓰지 않아야 한다.
        var tray = typeof(App).GetMethod("UninstallFromTray", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(tray);

        var referenced = GetReferencedMethods(tray!);

        // 스캔이 동작하는지 확인하는 기준점: 트레이 경로는 확인 창을 직접 부른다.
        Assert.Contains(referenced, method => method.DeclaringType == typeof(UninstallFlow)
            && method.Name == nameof(UninstallFlow.Confirm));

        Assert.DoesNotContain(referenced, method => method.DeclaringType == typeof(SingleInstanceService)
            && method.Name == nameof(SingleInstanceService.IsPrimaryRunning));
        Assert.DoesNotContain(referenced, method => method.DeclaringType == typeof(UninstallService)
            && method.Name == nameof(UninstallService.ShouldBlockCliUninstall));
        Assert.DoesNotContain(referenced, method => method.DeclaringType == typeof(UninstallFlow)
            && (method.Name == nameof(UninstallFlow.RunCli) || method.Name == nameof(UninstallFlow.RunInteractive)));
    }

    /// <summary>
    /// 메서드 IL에서 call/callvirt/newobj/ldftn 뒤의 메타데이터 토큰을 풀어 참조 메서드를 모은다(메서드 그룹 → 델리게이트 변환은 ldftn).
    /// 피연산자 경계를 해석하지 않는 단순 스캔이므로 유효한 메서드 토큰으로 풀리지 않는 바이트열은 건너뛴다.
    /// </summary>
    private static List<MethodBase> GetReferencedMethods(MethodInfo method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        Assert.NotNull(il);

        var result = new List<MethodBase>();
        for (var i = 0; i + 4 < il!.Length; i++)
        {
            var isCall = il[i] is 0x28 or 0x6F or 0x73;
            var isLdftn = il[i] == 0x06 && i > 0 && il[i - 1] == 0xFE;
            if (!isCall && !isLdftn)
                continue;

            try
            {
                var resolved = method.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1));
                if (resolved is not null)
                    result.Add(resolved);
            }
            catch (ArgumentException)
            {
                // 유효한 메서드 토큰이 아니다.
            }
        }

        return result;
    }
}
