using System.Text.RegularExpressions;
using Microsoft.Win32;
using NetcShared;

namespace NetcServer.Agent;

/// <summary>
/// Защита автозагрузки ученика: находит подозрительные элементы (скрипты в папке
/// «Автозагрузка», записи HKCU Run/RunOnce, запускающие скрипты/интерпретаторы),
/// уносит их в карантин и пишет в журнал. Дополнительно помечает скрипты, которые
/// пытаются выключить/усыпить ПК (shutdown, SetSuspendState и т.п.).
/// </summary>
public static class AutostartGuard
{
    private static readonly Regex ScriptExt =
        new(@"\.(py|pyw|bat|cmd|vbs|vbe|js|jse|wsf|ps1|hta|scr|jar)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Interpreter =
        new(@"\b(wscript|cscript|mshta|powershell|pwsh|pythonw?|node|java|javaw)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] DangerousTokens =
    {
        "shutdown", "setsuspendstate", "setsystempowerstate", "powrprof", "hibernate", "rundll32"
    };

    private static readonly object Gate = new();
    private static Task? _loop;

    public static void EnsureLoop()
    {
        if (_loop is not null) return;
        _loop = Task.Run(async () =>
        {
            while (true)
            {
                try { Scan(quarantine: true, journal: true); } catch { }
                await Task.Delay(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
            }
        });
    }

    public static AutostartReport Scan(bool quarantine, bool journal = false)
    {
        var report = new AutostartReport();

        lock (Gate)
        {
            ScanFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Автозагрузка (ученик)", quarantine, canDelete: true, viaWatchdog: false, report);
            ScanFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "Автозагрузка (общая)", quarantine, canDelete: false, viaWatchdog: true, report);
            ScanRunKey(@"Software\Microsoft\Windows\CurrentVersion\Run", "HKCU Run", quarantine, report);
            ScanRunKey(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", "HKCU RunOnce", quarantine, report);
        }

        if (journal && report.Findings.Count > 0)
        {
            AgentJournal.Add("autostart",
                $"Защита автозагрузки: найдено {report.Findings.Count}, удалено {report.Removed}");
        }
        return report;
    }

    private static void ScanFolder(string dir, string location, bool quarantine, bool canDelete, bool viaWatchdog, AutostartReport report)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

        foreach (var file in Directory.GetFiles(dir))
        {
            string name = Path.GetFileName(file);
            string ext = Path.GetExtension(file);
            if (!ScriptExt.IsMatch(ext)) continue;

            string detail = Describe(file);
            bool removed = false;
            if (quarantine)
            {
                // Папку ученика правит сам агент; общую — служба-сторож от SYSTEM (нет прав у ученика).
                removed = canDelete ? TryQuarantine(file)
                                    : viaWatchdog && TryQuarantineViaWatchdog(file);
            }

            report.Findings.Add(new AutostartFinding { Location = location, Name = name, Detail = detail, Removed = removed });
            if (removed) report.Removed++;
        }
    }

    private static void ScanRunKey(string subKey, string location, bool quarantine, AutostartReport report)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKey, writable: quarantine);
            if (key is null) return;

            foreach (var valueName in key.GetValueNames())
            {
                string command = Convert.ToString(key.GetValue(valueName)) ?? "";
                bool suspicious = ScriptExt.IsMatch(command) || Interpreter.IsMatch(command);
                if (!suspicious) continue;

                string detail = Dangerous(command) ? "команда выключения/сна: " + command : command;
                bool removed = false;
                if (quarantine)
                {
                    try { key.DeleteValue(valueName, throwOnMissingValue: false); removed = true; }
                    catch { }
                }
                report.Findings.Add(new AutostartFinding { Location = location, Name = valueName, Detail = detail, Removed = removed });
                if (removed) report.Removed++;
            }
        }
        catch { }
    }

    private static string Describe(string file)
    {
        try
        {
            var info = new FileInfo(file);
            if (info.Length <= 200_000)
            {
                string text = File.ReadAllText(file);
                foreach (var t in DangerousTokens)
                {
                    if (text.Contains(t, StringComparison.OrdinalIgnoreCase))
                        return "содержит команду «" + t + "»";
                }
            }
        }
        catch { }
        return "скрипт в автозагрузке";
    }

    private static bool Dangerous(string command)
    {
        foreach (var t in DangerousTokens)
        {
            if (command.Contains(t, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool TryQuarantine(string file)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "netc", "quarantine");
            Directory.CreateDirectory(dir);
            string dst = Path.Combine(dir,
                DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Path.GetFileName(file));
            File.Move(file, dst);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Общую «Автозагрузку» ученик (обычный пользователь) править не может — просим службу-сторож
    /// (SYSTEM) перенести скрипт в карантин через <see cref="MachineBridge"/>.
    /// </summary>
    private static bool TryQuarantineViaWatchdog(string file)
        => MachineBridge.Request("quarantine-file", file, null, timeoutMs: 5000, out _);
}
