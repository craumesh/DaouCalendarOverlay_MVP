using System.IO.Pipes;
using System.Text.Json;

namespace DaouCalendarOverlay.Services;

/// <summary>
/// native host(클라이언트)가 오버레이의 Named Pipe 서버로 요청 프레임을 중계한다.
/// 두 단계로 상대를 확인한 뒤에만 전송한다.
/// 1) <see cref="PipeOptions.CurrentUserOnly"/> — 다른 Windows 계정이 만든 파이프에는 붙지 않는다.
/// 2) 서버 프로세스 실행 파일 경로 대조 — 같은 계정이 파이프 이름을 선점했더라도 서버 EXE가
///    자기 자신과 다르면 요청 프레임을 한 바이트도 쓰지 않고 오류 응답만 돌려준다.
/// 요청 본문·인증 헤더 등 비밀값은 어떤 로그 인자에도 넣지 않는다(pid·실행 파일 경로·예외 메시지만).
/// </summary>
public static class NativeHostRelay
{
    /// <summary>파이프 연결 대기 시간(ms). Chrome이 host를 매 요청마다 새로 띄우므로 짧게 유지한다.</summary>
    public const int DefaultConnectTimeoutMs = 2500;

    /// <summary>파이프 프레임 상한(8MiB). 오버레이 → host 방향 응답에 적용한다.</summary>
    public const int PipeMessageLimit = 8 * 1024 * 1024;

    private const string LogCategory = "host.pipe";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// 파이프에 연결해 서버 신원을 확인하고, 신뢰할 수 있을 때만 <paramref name="request"/>를 보내 응답을 돌려준다.
    /// 실패해도 예외를 던지지 않고 직렬화된 오류 응답을 반환한다(취소만 그대로 전파).
    /// </summary>
    public static async Task<byte[]> ConnectAndExchangeAsync(
        string pipeName,
        byte[] request,
        string? selfExecutablePath,
        int connectTimeoutMs,
        CancellationToken cancellationToken)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            await pipe.ConnectAsync(connectTimeoutMs, cancellationToken);

            var serverProcessId = PipePeerVerifier.TryGetServerProcessId(pipe.SafePipeHandle);
            if (serverProcessId is null)
            {
                LogService.Warn(LogCategory, "파이프 서버 PID를 확인할 수 없어 요청을 전송하지 않았습니다.");
                return SerializeError(PipePeerVerifier.BuildUnverifiablePeerError());
            }

            var serverPath = PipePeerVerifier.TryGetProcessPath((int)serverProcessId.Value);
            if (!PipePeerVerifier.IsSameExecutable(selfExecutablePath, serverPath))
            {
                LogService.Warn(
                    LogCategory,
                    $"신뢰할 수 없는 파이프 서버: pid={serverProcessId.Value}, server={serverPath ?? "(unknown)"}, self={selfExecutablePath ?? "(unknown)"}");
                return SerializeError(PipePeerVerifier.BuildUntrustedPeerError(serverProcessId.Value));
            }

            await NativeBridgeProtocol.WriteFrameAsync(pipe, request, cancellationToken);
            return await NativeBridgeProtocol.ReadFrameAsync(pipe, PipeMessageLimit, cancellationToken)
                ?? SerializeError("Overlay bridge closed without a response.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 취소는 상위(NativeMessagingHost.RunAsync)가 기존대로 처리한다.
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            // CurrentUserOnly: 파이프 소유자가 현재 사용자가 아니다(예: 관리자 권한으로 실행된 오버레이).
            LogService.Warn(LogCategory, "파이프 서버가 현재 사용자 소유가 아닙니다: " + ex.Message);
            return SerializeError("Native Bridge 서버가 현재 사용자 소유가 아니어서 연결하지 않았습니다(오버레이를 관리자 권한으로 실행했는지 확인하세요).");
        }
        catch (Exception ex)
        {
            LogService.Warn(LogCategory, "파이프 연결 실패", ex);
            return SerializeError($"Daou Calendar Overlay가 실행 중이지 않거나 Native Bridge에 연결할 수 없습니다: {ex.Message}");
        }
    }

    private static byte[] SerializeError(string error) =>
        JsonSerializer.SerializeToUtf8Bytes(NativeBridgeResponse.Fail(error), JsonOptions);
}
