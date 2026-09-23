using System.Runtime.CompilerServices;
using DaouCalendarOverlay.Services;

namespace DaouCalendarOverlay;

internal static class Program
{
    [STAThread]
    internal static int Main(string[] args)
    {
        // T2.4: --uninstall 등 인자 처리는 여기(native 분기 이전)에 추가한다.
        if (StartupModeParser.IsUninstallInvocation(args))
        {
            LogService.Initialize(LogService.DefaultLogDirectory, "overlay");
            return UninstallFlow.RunInteractive();
        }

        if (StartupModeParser.IsNativeInvocation(args))
            return RunNativeHost();

        return RunGui();
    }

    // App 타입 참조를 별도 메서드로 격리한다. Main 본문이 App을 직접 참조하면 Main을 JIT할 때
    // WPF 어셈블리(PresentationFramework/WindowsBase/System.Xaml)가 host 모드에서도 로드된다.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunGui()
    {
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }

    private static int RunNativeHost()
    {
        // host 분기는 App.OnStartup을 타지 않으므로 여기서 반드시 로깅을 초기화한다.
        LogService.Initialize(LogService.DefaultLogDirectory, "host");
        LogService.Info("startup", AppVersion.FormatStartupLine("host", Environment.ProcessId));
        LogService.Info("host", $"host 모드 시작 pid={Environment.ProcessId}");
        try
        {
            NativeMessagingHost.RunAsync().GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception ex)
        {
            LogService.Error("host", "native host 실행 실패", ex);
            return 1;
        }
    }
}
