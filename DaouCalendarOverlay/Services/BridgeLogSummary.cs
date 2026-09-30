using System.Globalization;
using System.Text;
using System.Text.Json;

namespace DaouCalendarOverlay.Services;

/// <summary>
/// 브리지 요청/결과를 로그 한 줄로 요약한다.
/// 응답 본문과 쿠키는 요약에 넣지 않는다. 허용한 전송 필드(outcome, status, contentType, bodyLength,
/// redirected, elapsedMs, errorName, refreshState, refreshStatus)만 JSON 종류가 맞을 때 정제해서 붙인다.
/// 이 요약은 host 프로세스(NativeMessagingHost)도 쓰므로 host 로그 파일에도 같은 규칙이 적용된다.
/// 어떤 입력에도 예외를 던지지 않는다.
/// </summary>
public static class BridgeLogSummary
{
    private const string UnknownType = "type=unknown";
    private const string None = "none";
    private const int MaxTokenLength = 32;
    private const int MaxMediaTypeLength = 64;
    private const int MaxRequestIdLength = 64;

    /// <summary>파이프로 들어온 원본 UTF-8 JSON 요청을 요약한다.</summary>
    public static string DescribeRequest(byte[]? utf8Json)
    {
        if (utf8Json is null || utf8Json.Length == 0)
            return UnknownType;

        try
        {
            using var document = JsonDocument.Parse(new ReadOnlyMemory<byte>(utf8Json));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return UnknownType;

            var type = root.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
                ? typeElement.GetString()
                : null;

            var builder = new StringBuilder("type=");
            builder.Append(string.IsNullOrWhiteSpace(type) ? "unknown" : type);

            // 확장이 싣는 프로토콜 버전. 정수가 아니거나 없으면 아무것도 붙이지 않는다.
            if (TryGetInt32(root, "protocolVersion", out var protocolVersion))
                builder.Append(" protocolVersion=").Append(protocolVersion.ToString(CultureInfo.InvariantCulture));

            // postResult의 전송 필드만 순서대로 읽는다. 본문, requestId, 쿠키 계열 등 나머지 필드는 읽지 않는다.
            if (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object)
            {
                if (TryGetString(result, "outcome", out var outcome))
                    builder.Append(" outcome=").Append(Token(outcome));

                if (TryGetInt32(result, "status", out var status))
                    builder.Append(" status=").Append(status.ToString(CultureInfo.InvariantCulture));

                if (TryGetString(result, "contentType", out var contentType))
                    builder.Append(" contentType=").Append(MediaType(contentType));

                if (TryGetInt32(result, "bodyLength", out var bodyLength))
                    builder.Append(" bodyLength=").Append(bodyLength.ToString(CultureInfo.InvariantCulture));

                if (result.TryGetProperty("redirected", out var redirected) &&
                    redirected.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    builder.Append(" redirected=").Append(redirected.ValueKind == JsonValueKind.True ? "true" : "false");

                if (TryGetInt32(result, "elapsedMs", out var elapsedMs))
                    builder.Append(" elapsedMs=").Append(elapsedMs.ToString(CultureInfo.InvariantCulture));

                if (TryGetString(result, "errorName", out var errorName))
                    builder.Append(" errorName=").Append(Token(errorName));

                // 7.3.0 확장이 만료 401에서 토큰 갱신을 시도했을 때만 붙는 선택 필드.
                if (TryGetString(result, "refreshState", out var refreshState))
                    builder.Append(" refreshState=").Append(Token(refreshState));

                if (TryGetInt32(result, "refreshStatus", out var refreshStatus))
                    builder.Append(" refreshStatus=").Append(refreshStatus.ToString(CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
        catch
        {
            // 파싱 실패는 로그 요약을 포기하는 것으로 충분하다.
            return UnknownType;
        }
    }

    /// <summary>역직렬화된 결과 페이로드를 요약한다(본문 제외).</summary>
    public static string DescribeResult(BridgeResultPayload? result)
    {
        if (result is null)
            return "result=null";

        try
        {
            var builder = new StringBuilder();
            builder.Append("requestId=").Append(RequestIdToken(result.RequestId));
            builder.Append(" outcome=").Append(Token(result.Outcome));
            builder.Append(" status=").Append(IntOrNone(result.Status));
            builder.Append(" contentType=").Append(MediaType(result.ContentType));
            builder.Append(" bodyLength=").Append(IntOrNone(result.BodyLength));
            builder.Append(" redirected=").Append(result.Redirected ? "true" : "false");
            builder.Append(" elapsedMs=").Append(IntOrNone(result.ElapsedMs));
            builder.Append(" errorName=").Append(Token(result.ErrorName));

            if (result.RefreshState is not null)
                builder.Append(" refreshState=").Append(Token(result.RefreshState));

            if (result.RefreshStatus is int refreshStatus)
                builder.Append(" refreshStatus=").Append(refreshStatus.ToString(CultureInfo.InvariantCulture));

            return builder.ToString();
        }
        catch
        {
            return "result=unknown";
        }
    }

    /// <summary>로그용 토큰. <c>[A-Za-z0-9_]</c>만 남기고 32자로 자른다. 비면 <c>"none"</c>.</summary>
    public static string Token(string? value)
    {
        var token = Keep(value, MaxTokenLength, static c => IsAsciiLetterOrDigit(c) || c == '_');
        return token.Length == 0 ? None : token;
    }

    /// <summary>
    /// 로그용 media type. <c>';'</c> 앞부분을 소문자로 바꾸고 <c>[a-z0-9.+/-]</c>만 남겨 64자로 자른다. 비면 <c>"none"</c>.
    /// </summary>
    public static string MediaType(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return None;

        var separator = value.IndexOf(';');
        var head = (separator >= 0 ? value.Substring(0, separator) : value).ToLowerInvariant();
        var media = Keep(head, MaxMediaTypeLength,
            static c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '+' or '/' or '-');
        return media.Length == 0 ? None : media;
    }

    private static string RequestIdToken(string? value)
    {
        var token = Keep(value, MaxRequestIdLength, IsAsciiLetterOrDigit);
        return token.Length == 0 ? None : token;
    }

    private static string IntOrNone(int? value) =>
        value is int number ? number.ToString(CultureInfo.InvariantCulture) : None;

    private static bool TryGetInt32(JsonElement parent, string name, out int value)
    {
        value = 0;
        return parent.TryGetProperty(name, out var element) &&
               element.ValueKind == JsonValueKind.Number &&
               element.TryGetInt32(out value);
    }

    private static bool TryGetString(JsonElement parent, string name, out string? value)
    {
        value = null;
        if (!parent.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
            return false;

        value = element.GetString();
        return true;
    }

    private static bool IsAsciiLetterOrDigit(char c) =>
        c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';

    private static string Keep(string? value, int maxLength, Func<char, bool> allowed)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        var builder = new StringBuilder(Math.Min(value.Length, maxLength));
        foreach (var c in value)
        {
            if (!allowed(c))
                continue;

            builder.Append(c);
            if (builder.Length >= maxLength)
                break;
        }

        return builder.ToString();
    }
}
