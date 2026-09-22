using System.Text;
using System.Threading;

namespace DaouCalendarOverlay.Services;

/// <summary>
/// 파일 로깅. 외부 패키지 없이 동기 쓰기 + 단일 lock 으로 구현한다.
/// 어떤 상황에서도 호출자에게 예외를 던지지 않는다(로깅 실패가 앱을 죽이면 안 된다).
/// </summary>
public static class LogService
{
    private static readonly object Gate = new();

    /// <summary>host 모드는 sendNativeMessage마다 새 프로세스가 뜨므로 같은 로그 파일을 다른 프로세스가
    /// 동시에 열 수 있다. 공유 위반(IOException)에 대비해 짧게 재시도한다.</summary>
    private const int MaxIoAttempts = 3;
    private const int IoRetryDelayMs = 20;

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
                {
                    // 롤링은 overlay/host 프로세스 간 경합으로 실패할 수 있다. 실패해도 이번 줄 기록은 계속한다.
                    try { Rotate(path); }
                    catch { /* 롤링 실패는 무시하고 계속 기록한다. */ }
                }

                AppendWithRetry(path, bytes);
            }
        }
        catch
        {
            // 로깅 실패는 삼킨다.
        }
    }

    /// <summary>
    /// 다른 프로세스(host 모드는 sendNativeMessage마다 새 프로세스)가 같은 파일을 열고 있을 수 있으므로
    /// FileShare.ReadWrite로 열고, 그래도 IOException이 나면 짧게 재시도한다. 끝내 실패하면 예외를 던져
    /// 호출자(Write)의 catch가 조용히 삼키게 한다.
    /// </summary>
    private static void AppendWithRetry(string path, byte[] bytes)
    {
        for (var attempt = 1; attempt <= MaxIoAttempts; attempt++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                fs.Write(bytes, 0, bytes.Length);
                return;
            }
            catch (IOException) when (attempt < MaxIoAttempts)
            {
                Thread.Sleep(IoRetryDelayMs);
            }
        }
    }

    private static void Rotate(string activePath)
    {
        var oldest = LogRotation.GetOldestArchivePath(activePath);
        DeleteWithRetry(oldest);

        foreach (var (source, destination) in LogRotation.BuildRotationPlan(activePath))
            MoveWithRetry(source, destination);
    }

    private static void DeleteWithRetry(string path)
    {
        for (var attempt = 1; attempt <= MaxIoAttempts; attempt++)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
                return;
            }
            catch (IOException) when (attempt < MaxIoAttempts)
            {
                Thread.Sleep(IoRetryDelayMs);
            }
        }
    }

    private static void MoveWithRetry(string source, string destination)
    {
        for (var attempt = 1; attempt <= MaxIoAttempts; attempt++)
        {
            try
            {
                if (File.Exists(source))
                    File.Move(source, destination, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < MaxIoAttempts)
            {
                Thread.Sleep(IoRetryDelayMs);
            }
        }
    }
}
