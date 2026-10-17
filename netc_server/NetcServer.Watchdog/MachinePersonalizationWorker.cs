using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using NetcShared;

namespace NetcServer.Watchdog;

/// <summary>
/// Выполняет машинные настройки персонализации от SYSTEM по запросам агента.
/// Экран блокировки хранится в машинном ключе <c>HKLM\...\PersonalizationCSP</c>, куда агент
/// (сессия ученика, обычный пользователь) писать не может. Агент кладёт запрос в общую папку
/// <see cref="MachinePersonalization.ExchangeDirectory"/>, сторож применяет и пишет ответ.
/// </summary>
public sealed class MachinePersonalizationWorker : BackgroundService
{
    private const string CspKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StaleAge = TimeSpan.FromMinutes(30);

    /// <summary>Расширения, которые сторож согласен перенести из общей «Автозагрузки» в карантин.</summary>
    private static readonly string[] QuarantinableExt =
        { ".py", ".pyw", ".bat", ".cmd", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".ps1", ".hta", ".scr", ".jar" };

    private readonly ILogger<MachinePersonalizationWorker> _log;

    public MachinePersonalizationWorker(ILogger<MachinePersonalizationWorker> log) => _log = log;

    private static string Dir => MachinePersonalization.ExchangeDirectory;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _log.LogInformation("netc_watchdog: обмен машинной персонализацией: {dir}", Dir);

        try { EnsureDirectory(); }
        catch (Exception ex) { _log.LogWarning(ex, "personalize: не удалось подготовить папку обмена"); }

        while (!ct.IsCancellationRequested)
        {
            try { ProcessPending(); }
            catch (Exception ex) { _log.LogWarning(ex, "personalize: ошибка обработки запроса"); }

            try { await Task.Delay(Interval, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Создаёт папку обмена и выдаёт группе «Пользователи» право записи (иначе агент не положит запрос).</summary>
    private void EnsureDirectory()
    {
        Directory.CreateDirectory(Dir);
        try
        {
            var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            var rule = new FileSystemAccessRule(users, FileSystemRights.Modify,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow);

            var di = new DirectoryInfo(Dir);
            var sec = di.GetAccessControl();
            sec.AddAccessRule(rule);
            di.SetAccessControl(sec);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "personalize: не удалось выдать права на папку {dir}", Dir);
        }

        CleanupStale();
    }

    /// <summary>Убирает брошенные файлы (запрос/ответ), оставшиеся от прошлых запусков.</summary>
    private void CleanupStale()
    {
        try
        {
            var now = DateTime.UtcNow;
            foreach (var f in Directory.GetFiles(Dir))
            {
                try
                {
                    if (now - File.GetLastWriteTimeUtc(f) > StaleAge) File.Delete(f);
                }
                catch { }
            }
        }
        catch { }
    }

    private void ProcessPending()
    {
        if (!Directory.Exists(Dir)) { EnsureDirectory(); return; }

        foreach (var reqPath in Directory.GetFiles(Dir, MachinePersonalization.RequestPrefix + "*.json"))
        {
            string id = Path.GetFileName(reqPath)[MachinePersonalization.RequestPrefix.Length..];
            string resPath = Path.Combine(Dir, MachinePersonalization.ResponsePrefix + id);

            MachinePersonalization.Request? req = null;
            try { req = System.Text.Json.JsonSerializer.Deserialize<MachinePersonalization.Request>(
                File.ReadAllBytes(reqPath), Json.Options); }
            catch { }

            var res = req is null
                ? new MachinePersonalization.Response { Ok = false, Message = "некорректный запрос" }
                : Apply(req);

            try { File.WriteAllBytes(resPath, Json.ToBytes(res)); }
            catch (Exception ex) { _log.LogWarning(ex, "personalize: не удалось записать ответ"); }

            try { File.Delete(reqPath); }
            catch { }
        }
    }

    private MachinePersonalization.Response Apply(MachinePersonalization.Request req)
    {
        if (string.Equals(req.Action, "kill-pids", StringComparison.OrdinalIgnoreCase))
            return KillProcesses(req.Pids);

        if (string.Equals(req.Action, "quarantine-file", StringComparison.OrdinalIgnoreCase))
            return QuarantineFile(req.Path);

        if (string.Equals(req.Action, "restart-agent", StringComparison.OrdinalIgnoreCase))
        {
            // Активация новой сборки без перезагрузки: убить агента — WatchdogWorker поднимет свежий.
            int n = 0;
            foreach (var p in Process.GetProcessesByName("NetcServer.Agent"))
            {
                try { p.Kill(); n++; }
                catch { }
                finally { p.Dispose(); }
            }
            return new MachinePersonalization.Response { Ok = true, Message = "агент перезапускается (остановлено " + n + ")" };
        }

        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(CspKey);
            if (key is null) return new MachinePersonalization.Response { Ok = false, Message = "нет доступа к HKLM" };

            if (string.Equals(req.Action, "reset", StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue("LockScreenImageStatus", 0, RegistryValueKind.DWord);
                key.DeleteValue("LockScreenImagePath", throwOnMissingValue: false);
                key.DeleteValue("LockScreenImageUrl", throwOnMissingValue: false);
                return new MachinePersonalization.Response { Ok = true, Message = "сброшен" };
            }

            if (string.IsNullOrWhiteSpace(req.Path) || !File.Exists(req.Path))
                return new MachinePersonalization.Response { Ok = false, Message = "файл не найден: " + req.Path };

            key.SetValue("LockScreenImagePath", req.Path!, RegistryValueKind.String);
            key.SetValue("LockScreenImageUrl", req.Path!, RegistryValueKind.String);
            key.SetValue("LockScreenImageStatus", 1, RegistryValueKind.DWord);
            return new MachinePersonalization.Response { Ok = true, Message = "ок" };
        }
        catch (Exception ex)
        {
            return new MachinePersonalization.Response { Ok = false, Message = ex.Message };
        }
    }

    /// <summary>
    /// Принудительно завершает указанные процессы ученической сессии от SYSTEM
    /// (в отличие от агента, сторож может закрыть и приложения, запущенные от администратора).
    /// </summary>
    private MachinePersonalization.Response KillProcesses(List<int>? pids)
    {
        if (pids is null || pids.Count == 0)
            return new MachinePersonalization.Response { Ok = true, Message = "нет процессов", Count = 0 };

        var (killed, skipped) = ProcessKiller.Kill(pids, SessionLauncher.GetActiveSessionId());

        string message = $"завершено процессов: {killed}" + (skipped > 0 ? $", пропущено: {skipped}" : "");
        _log.LogInformation("netc_watchdog: kill-pids — {message}", message);
        return new MachinePersonalization.Response { Ok = true, Message = message, Count = killed };
    }

    /// <summary>
    /// Переносит скрипт из общей «Автозагрузки» в карантин от SYSTEM по запросу агента (ученик
    /// не имеет прав на запись в общую «Автозагрузку»). Путь ограничен папкой общей автозагрузки
    /// и списком расширений — чтобы запрос через общую папку обмена не позволял двигать
    /// произвольные файлы от имени SYSTEM.
    /// </summary>
    private MachinePersonalization.Response QuarantineFile(string? path)
        => QuarantineFileCore(
            path,
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "netc", "quarantine"),
            _log);

    /// <summary>Ядро переноса в карантин (выделено для тестируемости; <paramref name="root"/> — ожидаемый корень).</summary>
    internal static MachinePersonalization.Response QuarantineFileCore(
        string? path, string root, string quarantineDir, ILogger? log = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                return new MachinePersonalization.Response { Ok = false, Message = "путь не указан" };

            string prefix = Path.GetFullPath(root);
            if (!prefix.EndsWith(Path.DirectorySeparatorChar)) prefix += Path.DirectorySeparatorChar;

            string full = Path.GetFullPath(path);
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return new MachinePersonalization.Response { Ok = false, Message = "путь вне общей автозагрузки" };

            if (!QuarantinableExt.Contains(Path.GetExtension(full), StringComparer.OrdinalIgnoreCase))
                return new MachinePersonalization.Response { Ok = false, Message = "недопустимое расширение" };

            if (!File.Exists(full))
                return new MachinePersonalization.Response { Ok = false, Message = "файл не найден" };

            Directory.CreateDirectory(quarantineDir);
            string dst = Path.Combine(quarantineDir,
                DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Path.GetFileName(full));

            File.Move(full, dst);
            log?.LogInformation("netc_watchdog: скрипт из общей автозагрузки → карантин: {dst}", dst);
            return new MachinePersonalization.Response { Ok = true, Message = "карантин: " + dst, Count = 1 };
        }
        catch (Exception ex)
        {
            return new MachinePersonalization.Response { Ok = false, Message = ex.Message };
        }
    }
}
