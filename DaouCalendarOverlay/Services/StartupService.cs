using Microsoft.Win32;

namespace DaouCalendarOverlay.Services;

public sealed class StartupService
{
    /// <summary>시작 프로그램 등록 키(HKCU 상대 경로). 제거 경로(<see cref="UninstallService"/>)와 같은 출처를 쓴다.</summary>
    public const string RunKeyPath = UninstallService.RunKeyPath;

    /// <summary>시작 프로그램 등록 값 이름. 제거 경로(<see cref="UninstallService"/>)와 같은 출처를 쓴다.</summary>
    public const string RunValueName = UninstallService.RunValueName;

    public void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true) ??
                            Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exe))
                    key.SetValue(RunValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(RunValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            // 시작프로그램 등록 실패가 앱 실행 자체를 막아서는 안 된다.
            LogService.Warn("startup.run", "시작프로그램 등록 실패", ex);
        }
    }
}
