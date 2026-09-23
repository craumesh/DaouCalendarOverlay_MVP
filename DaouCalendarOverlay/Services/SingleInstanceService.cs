using System.IO;
using System.IO.Pipes;
using System.Text;

namespace DaouCalendarOverlay.Services;

public sealed class SingleInstanceService : IAsyncDisposable
{
    private const string MutexName = @"Local\DaouCalendarOverlay.Singleton";
    private const string PipeName = "DaouCalendarOverlay.Activation";

    private readonly CancellationTokenSource _cts = new();
    private Mutex? _mutex;
    private Task? _listenLoop;
    private bool _ownsMutex;

    public event EventHandler? ActivateRequested;

    public bool TryAcquirePrimary()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        _ownsMutex = createdNew;
        return createdNew;
    }

    /// <summary>
    /// 오버레이가 실행 중인지 단일 인스턴스 뮤텍스가 있는지로 확인만 한다(CLI --uninstall 사전 확인용).
    /// </summary>
    /// <remarks>
    /// 확인만 하고 뮤텍스를 만들거나 붙잡지 않는다. <see cref="Mutex.TryOpenExisting(string, out Mutex)"/>는 이미 있는
    /// 뮤텍스만 열고 없으면 만들지 않으며, 연 핸들은 소유권을 얻지 않은 채(WaitOne 없음) 즉시 폐기한다.
    /// <c>new Mutex(..., MutexName, ...)</c>로 확인하면 오버레이가 꺼져 있을 때 뮤텍스가 새로 생기고, 그 핸들을 쥐고 있는 동안
    /// 기동하는 오버레이의 <see cref="TryAcquirePrimary"/>가 createdNew=false를 받아 두 번째 인스턴스로 처리돼 뜨지 못한다.
    /// </remarks>
    public static bool IsPrimaryRunning()
    {
        try
        {
            if (!Mutex.TryOpenExisting(MutexName, out var existing))
                return false;

            existing.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // 뮤텍스는 있지만 열 권한이 없다. 있다는 것 자체가 오버레이가 실행 중이라는 뜻이다.
            return true;
        }
    }

    public void StartListening()
    {
        if (!_ownsMutex || _listenLoop is not null)
            return;

        _listenLoop = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    public static async Task NotifyPrimaryAsync()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(
                    ".",
                    PipeName,
                    PipeDirection.Out,
                    PipeOptions.Asynchronous);

                await client.ConnectAsync(350, CancellationToken.None);
                var payload = Encoding.UTF8.GetBytes("activate\n");
                await client.WriteAsync(payload);
                await client.FlushAsync();
                return;
            }
            catch
            {
                await Task.Delay(120);
            }
        }
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await server.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
                var line = await reader.ReadLineAsync().WaitAsync(cancellationToken);
                if (string.Equals(line, "activate", StringComparison.OrdinalIgnoreCase))
                    ActivateRequested?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                if (!cancellationToken.IsCancellationRequested)
                    await Task.Delay(250, cancellationToken);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_listenLoop is not null)
        {
            try { await _listenLoop; } catch { }
        }

        if (_ownsMutex && _mutex is not null)
        {
            try { _mutex.ReleaseMutex(); } catch { }
        }

        _mutex?.Dispose();
        _cts.Dispose();
    }
}
