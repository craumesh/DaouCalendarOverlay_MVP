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
