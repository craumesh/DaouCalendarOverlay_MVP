using Microsoft.Win32;

namespace DaouCalendarOverlay.Services;

public sealed class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DaouCalendarOverlay";

    public void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ??
                            Registry.CurrentUser.CreateSubKey(RunKey, writable: true);

            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exe))
                    key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // 시작프로그램 등록 실패가 앱 실행 자체를 막아서는 안 된다.
        }
    }
}
