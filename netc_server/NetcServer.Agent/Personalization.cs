using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using NetcShared;

namespace NetcServer.Agent;

/// <summary>Персонализация в сессии ученика: обои, тема, акцент (пользовательские),
/// экран блокировки (машинно, если есть права).</summary>
public static class Personalization
{
    private const uint SPI_SETDESKWALLPAPER = 0x0014;
    private const uint SPIF_UPDATEINIFILE = 0x01;
    private const uint SPIF_SENDCHANGE = 0x02;
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, string pvParam, uint fWinIni);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam,
        uint flags, uint timeout, out UIntPtr result);

    private static readonly IntPtr HWND_BROADCAST = (IntPtr)0xffff;

    private static string StoreDir
    {
        get
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "netc");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static PersonalizationResult Apply(PersonalizationMessage m)
    {
        if (m.Reset) return ResetAll();

        var parts = new List<string>();
        bool ok = true;

        try
        {
            if (!string.IsNullOrWhiteSpace(m.WallpaperPath))
            {
                string? dst = Store(m.WallpaperPath!, "wallpaper");
                if (dst is null) { ok = false; parts.Add("обои: файл не найден"); }
                else { SetWallpaper(dst, m.WallpaperStyle); parts.Add("обои=ок"); }
            }

            if (!string.IsNullOrWhiteSpace(m.LockScreenPath))
            {
                string? dst = Store(m.LockScreenPath!, "lockscreen");
                if (dst is null) { ok = false; parts.Add("лок-скрин: файл не найден"); }
                else if (SetLockScreen(dst, out string lmsg)) parts.Add("лок-скрин=" + lmsg);
                else { ok = false; parts.Add("лок-скрин: " + lmsg); }
            }

            if (!string.IsNullOrWhiteSpace(m.Theme))
            {
                SetTheme(string.Equals(m.Theme, "light", StringComparison.OrdinalIgnoreCase));
                parts.Add("тема=" + m.Theme);
            }

            if (m.AccentColor != 0)
            {
                SetAccent(m.AccentColor, m.AccentOnStart);
                parts.Add("акцент=#" + (m.AccentColor & 0xFFFFFF).ToString("X6"));
            }
        }
        catch (Exception ex)
        {
            ok = false;
            parts.Add("ошибка: " + ex.Message);
        }

        string message = parts.Count == 0 ? "нечего менять" : string.Join(", ", parts);
        AgentJournal.Add("personalization", message);
        return new PersonalizationResult { Ok = ok, Message = message };
    }

    /// <summary>Сброс персонализации к стандартным настройкам: светлая тема, обои/лок-скрин
    /// по умолчанию, акцент по умолчанию.</summary>
    private static PersonalizationResult ResetAll()
    {
        var parts = new List<string>();
        bool ok = true;

        try
        {
            ResetWallpaper();
            parts.Add("обои=по умолчанию");

            SetTheme(light: true);
            parts.Add("тема=светлая");

            SetAccent(0x0078D4, false);
            parts.Add("акцент=по умолчанию");

            if (TryResetLockScreen()) parts.Add("лок-скрин=сброшен");
            else if (RequestViaWatchdog("reset", null, out string lmsg)) parts.Add("лок-скрин=сброшен");
            else parts.Add("лок-скрин: " + lmsg);
        }
        catch (Exception ex)
        {
            ok = false;
            parts.Add("ошибка: " + ex.Message);
        }

        string message = "сброс: " + string.Join(", ", parts);
        AgentJournal.Add("personalization", message);
        return new PersonalizationResult { Ok = ok, Message = message };
    }

    private static void ResetWallpaper()
    {
        foreach (var f in Directory.GetFiles(StoreDir, "wallpaper.*")) { try { File.Delete(f); } catch { } }

        // Вернуть стандартные обои Windows, если они есть; иначе — пустые (сплошной цвет).
        string def = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "Web", "Wallpaper", "Windows", "img0.jpg");
        if (File.Exists(def)) { SetWallpaper(def, "fill"); return; }

        using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true))
        {
            if (key is not null) key.SetValue("Wallpaper", "", RegistryValueKind.String);
        }
        SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, "", SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
    }

    private static bool TryResetLockScreen()
    {
        try
        {
            foreach (var f in Directory.GetFiles(StoreDir, "lockscreen.*")) { try { File.Delete(f); } catch { } }

            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP", writable: true);
            if (key is null) return false;
            key.SetValue("LockScreenImageStatus", 0, RegistryValueKind.DWord);
            key.DeleteValue("LockScreenImagePath", throwOnMissingValue: false);
            key.DeleteValue("LockScreenImageUrl", throwOnMissingValue: false);
            return true;
        }
        catch { return false; }
    }

    private static string? Store(string source, string baseName)
    {
        if (!File.Exists(source)) return null;
        string ext = Path.GetExtension(source);
        string dir = StoreDir;
        foreach (var old in Directory.GetFiles(dir, baseName + ".*")) { try { File.Delete(old); } catch { } }
        string dst = Path.Combine(dir, baseName + ext);
        File.Copy(source, dst, overwrite: true);
        return dst;
    }

    private static void SetWallpaper(string path, string style)
    {
        string wStyle, tile;
        switch ((style ?? "fill").ToLowerInvariant())
        {
            case "fit": wStyle = "6"; tile = "0"; break;
            case "stretch": wStyle = "2"; tile = "0"; break;
            case "center": wStyle = "0"; tile = "0"; break;
            case "tile": wStyle = "0"; tile = "1"; break;
            default: wStyle = "10"; tile = "0"; break; // fill
        }

        using (var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true))
        {
            if (key is not null)
            {
                key.SetValue("Wallpaper", path, RegistryValueKind.String);
                key.SetValue("WallpaperStyle", wStyle, RegistryValueKind.String);
                key.SetValue("TileWallpaper", tile, RegistryValueKind.String);
            }
        }

        SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
    }

    private static void SetTheme(bool light)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        int v = light ? 1 : 0;
        key.SetValue("AppsUseLightTheme", v, RegistryValueKind.DWord);
        key.SetValue("SystemUsesLightTheme", v, RegistryValueKind.DWord);
        Broadcast("ImmersiveColorSet");
    }

    private static void SetAccent(int rgb, bool onStart)
    {
        int r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;
        int abgr = unchecked((int)(0xFF000000u | ((uint)b << 16) | ((uint)g << 8) | (uint)r));

        using (var dwm = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\DWM"))
        {
            dwm.SetValue("AccentColor", abgr, RegistryValueKind.DWord);
            dwm.SetValue("ColorPrevalence", onStart ? 1 : 0, RegistryValueKind.DWord);
        }
        using (var acc = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent"))
        {
            acc.SetValue("AccentColorMenu", abgr, RegistryValueKind.DWord);
        }
        Broadcast("ImmersiveColorSet");
    }

    private static bool TrySetLockScreen(string path)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP");
            if (key is null) return false;
            key.SetValue("LockScreenImagePath", path, RegistryValueKind.String);
            key.SetValue("LockScreenImageUrl", path, RegistryValueKind.String);
            key.SetValue("LockScreenImageStatus", 1, RegistryValueKind.DWord);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Задаёт экран блокировки: сначала пробует напрямую (если есть права), иначе — через
    /// службу-сторож (SYSTEM), т.к. в сессии ученика нет прав на запись в HKLM.</summary>
    private static bool SetLockScreen(string path, out string message)
    {
        if (TrySetLockScreen(path)) { message = "ок"; return true; }
        return RequestViaWatchdog("set", path, out message);
    }

    /// <summary>Просит службу-сторож выполнить машинную настройку. Возвращает ответ (или таймаут).</summary>
    private static bool RequestViaWatchdog(string action, string? path, out string message)
        => MachineBridge.Request(action, path, null, 8000, out message);

    private static void Broadcast(string area) =>
        SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, UIntPtr.Zero, area, SMTO_ABORTIFHUNG, 3000, out _);
}
