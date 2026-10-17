using NetcServer.Core;
using NetcShared;

namespace NetcServer.Service;

/// <summary>Запускает сетевые серверы (ядро) с backend'ом через канал к агенту.</summary>
public sealed class ServiceRunner : BackgroundService
{
    private readonly NetcSettings _settings;
    private readonly IAgentBackend _backend;
    private readonly ILogger<ServiceRunner> _log;

    public ServiceRunner(NetcSettings settings, IAgentBackend backend, ILogger<ServiceRunner> log)
    {
        _settings = settings;
        _backend = backend;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var tcp = new TcpControlServer(_settings, _backend, m => _log.LogInformation("{Message}", m));
        var disc = new DiscoveryResponder(_settings, m => _log.LogInformation("{Message}", m));
        await Task.WhenAll(tcp.RunAsync(ct), disc.RunAsync(ct)).ConfigureAwait(false);
    }
}
