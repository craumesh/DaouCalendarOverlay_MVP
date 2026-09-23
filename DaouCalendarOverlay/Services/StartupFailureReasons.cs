using System.Text;

namespace DaouCalendarOverlay.Services;

/// <summary>
/// 기동 시 선택적 구성 요소(확장 파일 추출, Native host 등록) 실패를 사용자 표시용 사유 문구로 바꾼다.
/// 순수 함수만 둔다: 로깅하지 않고 예외를 던지지 않는다. 상태 문구 조립은 <see cref="SyncStatusService"/>가 한다.
/// </summary>
public static class StartupFailureReasons
{
    /// <summary>사유 문구 최대 길이. 넘으면 끝을 '…'로 바꿔 정확히 이 길이로 자른다.</summary>
    public const int MaxLength = 120;

    /// <summary>Native Messaging host 레지스트리 등록 구성 요소 이름.</summary>
    public const string NativeHostComponent = "Native host 등록";

    /// <summary>Chrome 확장 파일 추출 구성 요소 이름.</summary>
    public const string ExtensionComponent = "확장 파일 설치";

    /// <summary>
    /// 예외 종류별 사유 문구. 판정 순서는 고정이다(파생 타입인 FileNotFound/DirectoryNotFound를 IOException보다 먼저 본다).
    /// 알 수 없는 예외는 <see cref="Normalize"/>한 메시지를, 메시지가 비어 있으면 예외 타입 이름을 돌려준다.
    /// </summary>
    public static string Describe(Exception? ex)
    {
        if (ex is null)
            return "알 수 없는 오류";

        if (ex is AggregateException agg && agg.InnerExceptions.Count == 1)
            return Describe(agg.InnerExceptions[0]);

        if (ex is UnauthorizedAccessException)
            return "쓰기 권한이 없습니다(정책 또는 ACL 제한)";

        if (ex is System.Security.SecurityException)
            return "보안 정책이 접근을 차단했습니다";

        if (ex is FileNotFoundException or DirectoryNotFoundException)
            return "필요한 경로를 찾을 수 없습니다";

        if (ex is IOException)
            return "파일을 쓰지 못했습니다(사용 중이거나 디스크 문제)";

        if (ex is InvalidOperationException)
            return "필요한 리소스를 찾지 못했습니다";

        var normalized = Normalize(ex.Message);
        return normalized.Length == 0 ? ex.GetType().Name : normalized;
    }

    /// <summary>
    /// 줄바꿈·탭을 공백으로 바꾸고 연속 공백을 하나로 줄인 뒤 앞뒤 공백을 없앤다.
    /// 결과가 <see cref="MaxLength"/>를 넘으면 앞 <c>MaxLength - 1</c>자에 '…'를 붙인다. null/빈 값은 빈 문자열.
    /// </summary>
    public static string Normalize(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return "";

        var builder = new StringBuilder(message.Length);
        var previousWasSpace = false;
        foreach (var raw in message)
        {
            var ch = raw is '\r' or '\n' or '\t' ? ' ' : raw;
            if (ch == ' ')
            {
                if (previousWasSpace)
                    continue;
                previousWasSpace = true;
            }
            else
            {
                previousWasSpace = false;
            }
            builder.Append(ch);
        }

        var text = builder.ToString().Trim();
        return text.Length > MaxLength ? text.Substring(0, MaxLength - 1) + "…" : text;
    }
}
