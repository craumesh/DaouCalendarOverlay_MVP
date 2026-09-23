using System.Diagnostics;
using System.Text.Json;

namespace DaouCalendarOverlay.Services;

public static class NativeMessagingHost
{
    private const int ChromeInputLimit = 64 * 1024 * 1024;
    private const int ChromeOutputLimit = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static bool IsNativeInvocation(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
            return false;

        return args.Any(arg => arg.StartsWith($"chrome-extension://{NativeBridgeProtocol.ExtensionId}", StringComparison.OrdinalIgnoreCase));
    }

    public static async Task RunAsync(CancellationToken cancellationToken = default)
    {
        using var input = Console.OpenStandardInput();
        using var output = Console.OpenStandardOutput();
        var sw = Stopwatch.StartNew();

        try
        {
            var request = await NativeBridgeProtocol.ReadFrameAsync(input, ChromeInputLimit, cancellationToken);
            if (request is null)
                return;

            LogService.Info("host", $"요청 수신 {BridgeLogSummary.DescribeRequest(request)}");

            var response = await NativeHostRelay.ConnectAndExchangeAsync(
                NativeBridgeProtocol.PipeName,
                request,
                Environment.ProcessPath,
                NativeHostRelay.DefaultConnectTimeoutMs,
                cancellationToken);

            if (response.Length > ChromeOutputLimit)
            {
                LogService.Warn("host", "응답이 Chrome 1MB 한도를 초과");
                response = SerializeError("Native host response exceeded Chrome's 1 MB limit.");
            }

            await NativeBridgeProtocol.WriteFrameAsync(output, response, cancellationToken);
            LogService.Info("host", $"요청 완료 elapsed={sw.ElapsedMilliseconds}ms");
        }
        catch (Exception ex)
        {
            LogService.Error("host", "native host 오류", ex);
            var response = SerializeError($"Native Messaging Host 오류: {ex.Message}");
            if (response.Length <= ChromeOutputLimit)
            {
                try { await NativeBridgeProtocol.WriteFrameAsync(output, response, cancellationToken); }
                catch (Exception writeEx) { LogService.Warn("host", "오류 응답 전송 실패", writeEx); }
            }
        }
    }

    private static byte[] SerializeError(string error) =>
        JsonSerializer.SerializeToUtf8Bytes(NativeBridgeResponse.Fail(error), JsonOptions);
}
