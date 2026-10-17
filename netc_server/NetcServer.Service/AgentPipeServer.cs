using System.IO.Pipes;
using NetcShared;

namespace NetcServer.Service;

/// <summary>Именованный канал: ждёт подключения трей-агента и передаёт его в AgentLink.</summary>
public sealed class AgentPipeServer : BackgroundService
{
    private readonly AgentLink _link;
    private readonly ILogger<AgentPipeServer> _log;

    public AgentPipeServer(AgentLink link, ILogger<AgentPipeServer> log)
    {
        _link = link;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var server = new NamedPipeServerStream(
                    NetcProtocol.PipeName, PipeDirection.InOut, 4,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                _ = _link.AttachAsync(server, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Ожидание подключения агента");
                try { await Task.Delay(1000, ct).ConfigureAwait(false); } catch { break; }
            }
        }
    }
}
