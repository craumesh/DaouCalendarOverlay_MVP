using System.Runtime.CompilerServices;
using System.Windows;

namespace DaouCalendarOverlay.Services;

/// <summary>
/// 제거 UI 글루(확인 창, 결과 창, --uninstall CLI 흐름). 실제 제거 로직은 <see cref="UninstallService"/>에 있다.
/// </summary>
public static class UninstallFlow
{
    private const string Title = "Daou Calendar Overlay 제거";

    private const string CommonNote =
        "Chrome/Edge 확장은 chrome://extensions(edge://extensions)에서 직접 제거하고, EXE 파일은 수동으로 삭제하세요.";

    /// <summary>확인 창 2단계. null이면 사용자 취소, true/false는 사용자 데이터 삭제 여부.</summary>
    public static bool? Confirm()
    {
        var proceed = MessageBox.Show(
            "Daou Calendar Overlay 등록을 제거합니다.\n\n· 시작 프로그램 등록(HKCU Run)\n· Chrome / Edge Native Messaging Host 등록\n\n계속할까요?",
            Title,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning);
        if (proceed != MessageBoxResult.OK)
            return null;

        var removeUserData = MessageBox.Show(
            $"설정·캐시·로그 폴더도 함께 삭제할까요?\n\n{UninstallService.GetUserDataDirectory()}\n(설정, 일정 캐시, 로그, 추출된 Chrome 확장 파일이 모두 삭제됩니다.)",
            Title,
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        return removeUserData switch
        {
            MessageBoxResult.Yes => true,
            MessageBoxResult.No => false,
            _ => null
        };
    }

    /// <summary>결과 MessageBox. extraNote는 있으면 요약 뒤에 빈 줄과 함께 붙인다.</summary>
    public static void ShowResult(UninstallResult result, string? extraNote = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        var paragraphs = new List<string> { UninstallService.BuildSummary(result) };
        if (!string.IsNullOrWhiteSpace(extraNote))
            paragraphs.Add(extraNote);
        paragraphs.Add(CommonNote);

        MessageBox.Show(
            string.Join(Environment.NewLine + Environment.NewLine, paragraphs),
            Title,
            MessageBoxButton.OK,
            result.HasFailures ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    /// <summary>
    /// CLI(--uninstall) 전용. Confirm → UninstallService.Run → ShowResult. 종료 코드 반환(취소 0, 실패 항목 있음 1).
    /// </summary>
    /// <remarks>
    /// Program.Main이 직접 호출하므로 인라인되면 안 된다. 인라인되면 Main JIT 시점에 이 본문의 WPF MessageBox 참조가
    /// 풀려 native host 모드에서도 WPF 어셈블리가 로드될 수 있다.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int RunInteractive()
    {
        try
        {
            var removeUserData = Confirm();
            if (removeUserData is null)
            {
                LogService.Info("uninstall", "사용자 취소");
                return 0;
            }

            var result = UninstallService.Run(removeUserData.Value);
            ShowResult(result);
            return UninstallService.ToExitCode(result);
        }
        catch (Exception ex)
        {
            // MessageBox 표시 자체가 실패해도 프로세스가 크래시하지 않게 한다.
            LogService.Error("uninstall", "제거 실패", ex);
            return 1;
        }
    }
}
