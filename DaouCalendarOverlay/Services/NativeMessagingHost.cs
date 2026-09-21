using System.IO.Pipes;
using System.Text.Json;

namespace DaouCalendarOverlay.Services;

public static class NativeMessagingHost
{
    private const int ChromeInputLimit = 64 * 1024 * 1024;
    private const int ChromeOutputLimit = 1024 * 1024;
    private const int PipeMessageLimit = 8 * 1024 * 1024;
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

        try
        {
            var request = await NativeBridgeProtocol.ReadFrameAsync(input, ChromeInputLimit, cancellationToken);
            if (request is null)
                return;

            byte[] response;
            try
            {
                using var pipe = new NamedPipeClientStream(
                    ".",
                    NativeBridgeProtocol.PipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);

                await pipe.ConnectAsync(2500, cancellationToken);
                await NativeBridgeProtocol.WriteFrameAsync(pipe, request, cancellationToken);
                response = await NativeBridgeProtocol.ReadFrameAsync(pipe, PipeMessageLimit, cancellationToken)
                    ?? SerializeError("Overlay bridge closed without a response.");
            }
            catch (Exception ex)
            {
                response = SerializeError($"Daou Calendar Overlay가 실행 중이지 않거나 Native Bridge에 연결할 수 없습니다: {ex.Message}");
            }

            if (response.Length > ChromeOutputLimit)
                response = SerializeError("Native host response exceeded Chrome's 1 MB limit.");

            await NativeBridgeProtocol.WriteFrameAsync(output, response, cancellationToken);
        }
        catch (Exception ex)
        {
            var response = SerializeError($"Native Messaging Host 오류: {ex.Message}");
            if (response.Length <= ChromeOutputLimit)
            {
                try { await NativeBridgeProtocol.WriteFrameAsync(output, response, cancellationToken); }
                catch { }
            }
        }
    }

    private static byte[] SerializeError(string error) =>
        JsonSerializer.SerializeToUtf8Bytes(NativeBridgeResponse.Fail(error), JsonOptions);
}
