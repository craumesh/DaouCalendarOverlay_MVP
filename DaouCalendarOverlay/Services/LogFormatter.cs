using System.Globalization;
using System.Text;

namespace DaouCalendarOverlay.Services;

/// <summary>
/// 로그 한 줄을 만드는 순수 함수 모음. 파일 I/O나 상태를 갖지 않는다.
/// </summary>
public static class LogFormatter
{
    public const string TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";

    private const string ExceptionIndent = "    ";
    private const string DefaultCategory = "general";
    private const string DefaultLevel = "INFO";

    /// <summary>
    /// 형식: {yyyy-MM-dd HH:mm:ss.fff} [{LEVEL}] {category}: {message}
    /// 예외가 있으면 다음 줄부터 스페이스 4칸 들여쓴 예외 문자열이 이어진다.
    /// 반환값은 끝에 개행을 포함하지 않는다.
    /// </summary>
    public static string FormatLine(DateTimeOffset timestamp, string level, string category, string message, Exception? ex)
    {
        var normalizedLevel = string.IsNullOrWhiteSpace(level) ? DefaultLevel : level.ToUpperInvariant();
        var normalizedCategory = string.IsNullOrWhiteSpace(category) ? DefaultCategory : category;
        var normalizedMessage = CollapseNewlines(message);

        var head = string.Concat(
            timestamp.ToString(TimestampFormat, CultureInfo.InvariantCulture),
            " [",
            normalizedLevel,
            "] ",
            normalizedCategory,
            ": ",
            normalizedMessage);

        if (ex is null)
            return head;

        var builder = new StringBuilder(head);
        foreach (var line in ex.ToString().Replace("\r\n", "\n").Split('\n'))
        {
            builder.Append(Environment.NewLine);
            builder.Append(ExceptionIndent);
            builder.Append(line);
        }

        return builder.ToString();
    }

    private static string CollapseNewlines(string message)
    {
        if (string.IsNullOrEmpty(message))
            return string.Empty;

        // 순서 주의: \r\n 을 먼저 공백 한 칸으로 바꾼 뒤 남은 \n, \r 을 처리한다.
        return message.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
    }
}
