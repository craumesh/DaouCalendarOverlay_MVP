using System.Text;

namespace DaouCalendarOverlay.Services;

/// <summary>
/// 파일 로깅. 외부 패키지 없이 동기 쓰기 + 단일 lock 으로 구현한다.
/// 어떤 상황에서도 호출자에게 예외를 던지지 않는다(로깅 실패가 앱을 죽이면 안 된다).
/// </summary>
public static class LogService
{
    private static readonly object Gate = new();

    private static string? _directory;
    private static string _prefix = "app";

    /// <summary>기본 로그 디렉터리(%LOCALAPPDATA%\DaouCalendarOverlay\logs).</summary>
    public static string DefaultLogDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DaouCalendarOverlay", "logs");

    /// <summary>초기화에 성공한 로그 디렉터리. 미초기화이면 null.</summary>
    public static string? LogDirectory => _directory;

    /// <summary>초기화 성공 여부.</summary>
    public static bool IsInitialized => _directory is not null;

    /// <summary>
    /// 앱 시작 시 1회 호출(GUI는 "overlay", native host 모드는 "host").
    /// 디렉터리를 만들 수 없으면 조용히 비활성 상태로 둔다. 여러 번 호출해도 안전하다.
    /// </summary>
    public static void Initialize(string logDirectory, string filePrefix)
    {
        lock (Gate)
        {
            if (string.IsNullOrWhiteSpace(logDirectory) || string.IsNullOrWhiteSpace(filePrefix))
                return;

            try
            {
                Directory.CreateDirectory(logDirectory);
                _directory = logDirectory;
                _prefix = filePrefix;
            }
            catch
            {
                // 디렉터리를 만들 수 없으면 로깅을 끈 상태로 계속 동작한다.
                _directory = null;
            }
        }
    }

    public static void Info(string category, string message) => Write("INFO", category, message, null);

    public static void Warn(string category, string message, Exception? ex = null) => Write("WARN", category, message, ex);

    public static void Error(string category, string message, Exception? ex = null) => Write("ERROR", category, message, ex);

    private static void Write(string level, string category, string message, Exception? ex)
    {
        if (_directory is null)
            return;

        try
        {
            lock (Gate)
            {
                var directory = _directory;
                if (directory is null)
                    return;

                var now = DateTimeOffset.Now;
                var path = Path.Combine(directory, LogRotation.GetFileName(_prefix, now));
                var bytes = new UTF8Encoding(false).GetBytes(LogFormatter.FormatLine(now, level, category, message, ex) + Environment.NewLine);

                if (File.Exists(path) && LogRotation.ShouldRotate(new FileInfo(path).Length, bytes.Length))
                    Rotate(path);

                using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                fs.Write(bytes, 0, bytes.Length);
            }
        }
        catch
        {
            // 로깅 실패는 삼킨다.
        }
    }

    private static void Rotate(string activePath)
    {
        var oldest = LogRotation.GetOldestArchivePath(activePath);
        if (File.Exists(oldest))
            File.Delete(oldest);

        foreach (var (source, destination) in LogRotation.BuildRotationPlan(activePath))
        {
            if (File.Exists(source))
                File.Move(source, destination, overwrite: true);
        }
    }
}
