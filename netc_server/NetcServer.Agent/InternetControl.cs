using System.Diagnostics;
using Microsoft.Win32;
using NetcShared;

namespace NetcServer.Agent;

/// <summary>
/// Ограничение интернета для текущего пользователя через системный прокси:
/// <list type="bullet">
/// <item><c>Blocked</c> — прокси на несуществующий адрес (интернет отключён целиком);</item>
/// <item><c>SiteMode</c> ≠ Off — локальный прокси <see cref="SiteFilter"/> с чёрным/белым списком доменов;</item>
/// <item>иначе — прежние настройки прокси восстанавливаются.</item>
/// </list>
/// Работает без прав администратора (только текущий пользователь).
/// </summary>
public static class InternetControl
{
    private const string SettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const string DeadProxy = "127.0.0.1:1";
    private static string FilterProxy => "127.0.0.1:" + NetcProtocol.SiteFilterPort;

    private static readonly object Gate = new();
    private static bool _active;      // прокси сейчас перехвачен нами
    private static bool _saved;       // сохранены прежние настройки прокси
    private static int _prevEnable;
    private static string? _prevServer;
    private static SiteFilter? _filter;

    public static bool Active { get { lock (Gate) return _active; } }

    public static void Set(InternetPolicyMessage policy)
    {
        bool blocked = policy.Blocked;
        var mode = policy.SiteMode;
        var sites = policy.Sites ?? new List<string>();

        lock (Gate)
        {
            if (blocked)
            {
                StopFilter();
                TakeOverProxy(DeadProxy);
            }
            else if (mode != SiteFilterMode.Off)
            {
                _filter ??= new SiteFilter(NetcProtocol.SiteFilterPort);
                _filter.Update(mode, sites);
                _filter.Start();
                TakeOverProxy(FilterProxy);
            }
            else
            {
                Restore(false);
            }
        }

        if (blocked && policy.KillBrowsers)
        {
            foreach (var name in AppControl.BrowserNames) KillByName(name);
        }

        string desc = blocked
            ? "Интернет ограничен"
            : mode == SiteFilterMode.Off
                ? "Интернет разрешён"
                : (mode == SiteFilterMode.Whitelist ? "Фильтр сайтов: белый список" : "Фильтр сайтов: чёрный список")
                  + $", позиций {sites.Count}";
        AgentJournal.Add("internet", desc);
    }

    /// <summary>
    /// Восстановление после перезапуска агента/перезагрузки ПК: если в реестре остался наш прокси
    /// (блокирующий или фильтрующий), убрать его — иначе интернет останется под управлением
    /// потерявшего состояние агента.
    /// </summary>
    public static void RecoverOnStart()
    {
        lock (Gate)
        {
            _active = false;
            _saved = false;
            Restore(true);
        }
    }

    private static void TakeOverProxy(string server)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKey, writable: true);
            if (key is null) return;

            if (!_saved)
            {
                _prevEnable = key.GetValue("ProxyEnable") is int e ? e : 0;
                _prevServer = key.GetValue("ProxyServer") as string;
                _saved = true;
            }

            key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
            key.SetValue("ProxyServer", server, RegistryValueKind.String);
            _active = true;
        }
        catch (Exception ex)
        {
            AgentLog.Add("интернет: не удалось задать прокси — " + ex.Message);
        }
    }

    /// <summary><paramref name="recover"/> — режим восстановления: снимаем только «наш» прокси,
    /// если он остался, не трогая чужие настройки.</summary>
    private static void Restore(bool recover)
    {
        StopFilter();

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKey, writable: true);
            if (key is null) return;

            if (recover)
            {
                string? server = key.GetValue("ProxyServer") as string;
                bool enabled = key.GetValue("ProxyEnable") is int e && e != 0;
                if (enabled && IsOurProxy(server))
                {
                    key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                    key.DeleteValue("ProxyServer", throwOnMissingValue: false);
                    AgentJournal.Add("internet", "Сброшен прокси фильтра/блокировки после перезапуска агента");
                }
                return;
            }

            if (!_active && !_saved) return; // мы ничего не перехватывали — не трогаем пользовательский прокси

            if (_saved)
            {
                key.SetValue("ProxyEnable", _prevEnable, RegistryValueKind.DWord);
                if (_prevServer is null) key.DeleteValue("ProxyServer", throwOnMissingValue: false);
                else key.SetValue("ProxyServer", _prevServer, RegistryValueKind.String);
            }
            else
            {
                key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
            }

            _active = false;
            _saved = false;
        }
        catch { }
    }

    private static bool IsOurProxy(string? server) =>
        string.Equals(server, DeadProxy, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(server, FilterProxy, StringComparison.OrdinalIgnoreCase);

    private static void StopFilter()
    {
        try { _filter?.Stop(); } catch { }
    }

    private static void KillByName(string name)
    {
        foreach (var p in Process.GetProcessesByName(name))
        {
            try { p.Kill(); } catch { } finally { p.Dispose(); }
        }
    }
}
