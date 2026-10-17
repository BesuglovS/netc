using System.Diagnostics;
using NetcShared;

namespace NetcServer.Agent;

/// <summary>
/// Ограничение приложений: фоновый монитор завершает запрещённые процессы
/// (чёрный список) либо все, кроме разрешённых (белый список, с защитой системных).
/// Работает без прав администратора — в пределах сессии ученика.
/// </summary>
public static class AppControl
{
    private static readonly object Gate = new();
    private static AppControlMode _mode = AppControlMode.Off;
    private static HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);
    private static Task? _loop;

    /// <summary>Системные/служебные процессы, которые не завершаем в режиме белого списка.</summary>
    private static bool IsProtected(string name) => ProcessGuard.IsProtected(name);

    /// <summary>Процессы, которые завершаем при включении блокировки интернета.</summary>
    public static readonly string[] BrowserNames =
    {
        "chrome", "msedge", "firefox", "opera", "brave", "yandex", "browser", "iexplore",
        "launcher", "vivaldi"
    };

    public static void SetPolicy(AppControlMode mode, IEnumerable<string> names)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in names ?? Enumerable.Empty<string>())
        {
            string n = Normalize(raw);
            if (n.Length > 0) set.Add(n);
        }
        lock (Gate)
        {
            _mode = mode;
            _names = set;
        }
        EnsureLoop();
    }

    public static AppControlMode Mode { get { lock (Gate) return _mode; } }

    public static IReadOnlyCollection<string> Names { get { lock (Gate) return _names.ToArray(); } }

    private static void EnsureLoop()
    {
        if (_loop is not null) return;
        _loop = Task.Run(async () =>
        {
            while (true)
            {
                try { Sweep(); } catch { }
                await Task.Delay(2000).ConfigureAwait(false);
            }
        });
    }

    private static void Sweep()
    {
        AppControlMode mode;
        HashSet<string> names;
        lock (Gate) { mode = _mode; names = _names; }
        if (mode == AppControlMode.Off) return;

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                string name = Normalize(p.ProcessName);
                if (name.Length == 0 || name == "idle" || name == "system") continue;

                bool block = mode == AppControlMode.Blacklist
                    ? names.Contains(name)
                    : !names.Contains(name) && !IsProtected(name);

                if (block)
                {
                    try
                    {
                        p.Kill();
                        AgentJournal.Add("apps", "Завершён процесс: " + p.ProcessName);
                    }
                    catch { }
                }
            }
            catch { }
            finally { p.Dispose(); }
        }
    }

    private static string Normalize(string name)
    {
        name = (name ?? "").Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return name;
    }
}
