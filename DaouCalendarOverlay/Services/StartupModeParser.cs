namespace DaouCalendarOverlay.Services;

/// <summary>
/// 프로세스 실행 인자에서 시작 모드를 판정하는 순수 로직. <c>Program.Main</c>이 WPF를 만들기 전에 호출한다.
/// T2.4에서 --uninstall 등 스위치 판정을 이 클래스에 추가한다(인자 파싱은 native 분기보다 먼저).
/// </summary>
public static class StartupModeParser
{
    /// <summary>
    /// Chrome이 Native Messaging host로 실행했는지 판정한다. 인자 중 하나가 고정 확장 origin
    /// (<c>chrome-extension://{ExtensionId}</c>)으로 시작하면 true다(대소문자 무시, 위치 무관).
    /// </summary>
    public static bool IsNativeInvocation(IReadOnlyList<string>? args)
    {
        if (args is null || args.Count == 0)
            return false;

        return args.Any(arg => arg is not null
            && arg.StartsWith($"chrome-extension://{NativeBridgeProtocol.ExtensionId}", StringComparison.OrdinalIgnoreCase));
    }
}
