using System.Text;
using System.Text.Json;

namespace DaouCalendarOverlay.Services;

/// <summary>
/// 브리지 요청/결과를 로그 한 줄로 요약한다.
/// 인증에 쓰이는 쿠키 문자열이나 브라우저 식별 문자열은 절대 요약에 넣지 않는다(개수와 source만 남긴다).
/// 어떤 입력에도 예외를 던지지 않는다.
/// </summary>
public static class BridgeLogSummary
{
    private const string UnknownType = "type=unknown";

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

            if (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object)
            {
                if (result.TryGetProperty("cookieCount", out var count) && count.ValueKind == JsonValueKind.Number)
                    builder.Append(" cookieCount=").Append(count.GetRawText());

                if (result.TryGetProperty("cookieSource", out var source) && source.ValueKind == JsonValueKind.String)
                    builder.Append(" cookieSource=").Append(source.GetString());
            }

            return builder.ToString();
        }
        catch
        {
            // 파싱 실패는 로그 요약을 포기하는 것으로 충분하다.
            return UnknownType;
        }
    }

    /// <summary>역직렬화된 결과 페이로드를 요약한다(비밀값 제외).</summary>
    public static string DescribeResult(BridgeResultPayload? result)
    {
        if (result is null)
            return "result=null";

        try
        {
            var source = result.CookieSource ?? "none";
            var hasError = !string.IsNullOrWhiteSpace(result.Error);
            return $"requestId={result.RequestId} cookieCount={result.CookieCount} cookieSource={source} hasError={hasError}";
        }
        catch
        {
            return "result=unknown";
        }
    }
}
