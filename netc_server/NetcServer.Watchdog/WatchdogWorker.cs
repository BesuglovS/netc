using System.Diagnostics;

namespace NetcServer.Watchdog;

/// <summary>
/// Сторожевая служба (SYSTEM). Следит, чтобы агент работал в сессии ученика,
/// и поднимает его заново, если ученик его закрыл. Сеть держит сам агент —
/// служба только запускает и перезапускает его.
/// </summary>
public sealed class WatchdogWorker : BackgroundService
{
    private const string AgentProcessName = "NetcServer.Agent";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(8);

    private readonly ILogger<WatchdogWorker> _log;
    private readonly string _agentExe = ResolveAgentExe();

    public WatchdogWorker(ILogger<WatchdogWorker> log) => _log = log;

    private static string ResolveAgentExe()
    {
        string? env = Environment.GetEnvironmentVariable("NETC_AGENT_EXE");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;

        string local = Path.Combine(AppContext.BaseDirectory, "NetcServer.Agent.exe");
        if (File.Exists(local)) return local;

        return @"C:\Program Files\netc\NetcServer.Agent.exe";
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _log.LogInformation("netc_watchdog запущен; агент: {path}", _agentExe);

        try { HardenPower(); }
        catch (Exception ex) { _log.LogWarning(ex, "watchdog: усиление питания не удалось"); }

        while (!ct.IsCancellationRequested)
        {
            try { EnsureAgent(); }
            catch (Exception ex) { _log.LogWarning(ex, "watchdog: ошибка цикла"); }

            try { await Task.Delay(Interval, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// Отключает гибернацию (иначе пользовательский скрипт с `shutdown /h` усыпляет ПК)
    /// и запрещает автосон/гашение экрана. Выполняется от SYSTEM. Отключить: NETC_NO_HARDENING=1.
    /// </summary>
    private void HardenPower()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("NETC_NO_HARDENING"), "1", StringComparison.Ordinal))
            return;

        RunPowerCfg("/h", "off");
        RunPowerCfg("/change", "standby-timeout-ac", "0");
        RunPowerCfg("/change", "standby-timeout-dc", "0");
        RunPowerCfg("/change", "monitor-timeout-ac", "0");
        RunPowerCfg("/change", "monitor-timeout-dc", "0");
        _log.LogInformation("watchdog: гибернация выключена, сон/дисплей = Никогда");
    }

    private void RunPowerCfg(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("powercfg") { CreateNoWindow = true, UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            var p = Process.Start(psi);
            p?.WaitForExit(8000);
        }
        catch (Exception ex)
        {
            _log.LogWarning("watchdog: powercfg ошибка: {msg}", ex.Message);
        }
    }

    private void EnsureAgent()
    {
        if (!File.Exists(_agentExe)) return;

        uint session = SessionLauncher.GetActiveSessionId();
        if (session == 0xFFFFFFFF || session == 0) return;   // нет интерактивной сессии

        // Важно сверять сессию: агент, зависший в неактивной сессии, не должен скрывать
        // отсутствие агента у активного ученика.
        if (IsAgentRunning(session)) return;

        bool ok = SessionLauncher.StartInSession(session, _agentExe, null, Path.GetDirectoryName(_agentExe));
        _log.LogInformation("watchdog: агент не найден — запуск в сессии {session}: {ok}", session, ok);
    }

    private static bool IsAgentRunning(uint session)
    {
        try
        {
            var procs = Process.GetProcessesByName(AgentProcessName);
            try
            {
                foreach (var p in procs)
                {
                    try
                    {
                        if ((uint)p.SessionId == session) return true;
                    }
                    catch { /* нет доступа к сессии процесса — считаем, что не он */ }
                }
            }
            finally
            {
                foreach (var p in procs) p.Dispose();
            }
        }
        catch { }
        return false;
    }
}
