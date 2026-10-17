using NetcServer.Core;
using NetcShared;

namespace NetcServer.Agent;

internal static class Program
{
    private static readonly CancellationTokenSource Cts = new();

    [STAThread]
    private static void Main()
    {
        // Защита от двойного запуска в сессии (автозапуск Run + сторожевaя служба).
        using var single = new Mutex(true, @"Local\netc_agent_single", out bool createdNew);
        if (!createdNew) return;

        ApplicationConfiguration.Initialize();

        var settings = NetcSettings.Load();
        using var context = new TrayContext();        // инициализирует AgentUi (UI-хост)
        var backend = new LocalAgentBackend();

        // Восстановление после падения/перезапуска: снять «залипшие» киоск и блокирующий прокси.
        KioskGuard.RecoverOnStart();
        InternetControl.RecoverOnStart();

        // Защита автозагрузки ученика (периодическая проверка/карантин).
        AutostartGuard.EnsureLoop();

        // Автономный режим: агент сам держит сетевой сервер (служба не нужна).
        var tcp = new TcpControlServer(settings, backend, Log);
        var disc = new DiscoveryResponder(settings, Log);
        _ = Task.Run(() => tcp.RunAsync(Cts.Token));
        _ = Task.Run(() => disc.RunAsync(Cts.Token));

        // Глобальные обработчики — чтобы сбои попадали в файловый лог, а не терялись.
        AppDomain.CurrentDomain.UnhandledException += (_, e) => AgentLog.Add("НЕОБРАБОТАННОЕ: " + e.ExceptionObject);
        Application.ThreadException += (_, e) => AgentLog.Add("ОШИБКА UI: " + e.Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => AgentLog.Add("ОШИБКА ЗАДАЧИ: " + e.Exception.Message);

        Log("Агент запущен: " + Environment.MachineName + " / " + Environment.UserName);
        Application.Run(context);
        Cts.Cancel();
    }

    private static void Log(string message)
    {
        System.Diagnostics.Debug.WriteLine(message);
        AgentLog.Add(message);
    }
}
