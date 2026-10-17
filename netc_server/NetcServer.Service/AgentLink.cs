using System.Text;
using NetcShared;

namespace NetcServer.Service;

/// <summary>Локальный канал к трей-агенту в сессии ученика.</summary>
public sealed class AgentLink
{
    private readonly ILogger<AgentLink> _log;
    private readonly SemaphoreSlim _captureLock = new(1, 1);
    private readonly object _gate = new();
    private Stream? _stream;
    private TaskCompletionSource<byte[]>? _pending;

    public AgentLink(ILogger<AgentLink> log) => _log = log;

    public bool Connected { get { lock (_gate) return _stream != null; } }

    public async Task AttachAsync(Stream stream, CancellationToken ct)
    {
        lock (_gate) { _stream = stream; }
        _log.LogInformation("Канал агента подключён");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var msg = await NetFrame.ReadAsync(stream, ct).ConfigureAwait(false);
                if (msg is null) break;

                switch ((PipeMsg)msg.Value.Type)
                {
                    case PipeMsg.Frame:
                        TaskCompletionSource<byte[]>? p;
                        lock (_gate) { p = _pending; _pending = null; }
                        p?.TrySetResult(msg.Value.Payload);
                        break;

                    case PipeMsg.Register:
                        _log.LogInformation("Агент зарегистрирован: {name}",
                            Encoding.UTF8.GetString(msg.Value.Payload));
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Канал агента: чтение прервано");
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_stream, stream))
                {
                    _stream = null;
                    _pending?.TrySetResult(Array.Empty<byte>());
                    _pending = null;
                }
            }
            _log.LogWarning("Канал агента закрыт");
        }
    }

    public async Task<byte[]?> CaptureAsync(int maxWidth, int quality, int timeoutMs, CancellationToken ct)
    {
        Stream? stream;
        lock (_gate) stream = _stream;
        if (stream is null) return null;

        if (!await _captureLock.WaitAsync(timeoutMs, ct).ConfigureAwait(false)) return null;
        try
        {
            var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate) { _pending = tcs; }

            byte[] payload = Json.ToBytes(new CaptureRequest { MaxWidth = maxWidth, Quality = quality });
            try
            {
                await NetFrame.WriteAsync(stream, (byte)PipeMsg.Capture, payload, ct).ConfigureAwait(false);
            }
            catch
            {
                lock (_gate) { if (_pending == tcs) _pending = null; }
                return null;
            }

            Task done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, ct)).ConfigureAwait(false);
            if (done == tcs.Task)
            {
                byte[] data = await tcs.Task.ConfigureAwait(false);
                return data.Length == 0 ? null : data;
            }
            lock (_gate) { if (_pending == tcs) _pending = null; }
            return null;
        }
        finally
        {
            _captureLock.Release();
        }
    }

    public async Task SendInputAsync(InputEvent ev, CancellationToken ct)
    {
        await SendAsync(PipeMsg.Input, Json.ToBytes(ev), ct).ConfigureAwait(false);
    }

    public async Task SendAsync(PipeMsg type, byte[]? payload, CancellationToken ct)
    {
        Stream? stream;
        lock (_gate) stream = _stream;
        if (stream is null) return;

        try
        {
            await NetFrame.WriteAsync(stream, (byte)type, payload ?? Array.Empty<byte>(), ct).ConfigureAwait(false);
        }
        catch { /* агент отключён */ }
    }
}
