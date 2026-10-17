using System.Runtime.InteropServices;

namespace NetcServer.Agent;

/// <summary>
/// Управление мониторами ученика: погасить/включить подсветку штатной командой Windows
/// (<c>WM_SYSCOMMAND</c> / <c>SC_MONITORPOWER</c>). ПК при этом продолжает работать —
/// это энергосбережение дисплея, а не выключение машины. Любой ввод возвращает экран.
/// </summary>
public static class MonitorControl
{
    private const uint WM_SYSCOMMAND = 0x0112;
    private const int SC_MONITORPOWER = 0xF170;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private static readonly IntPtr HWND_BROADCAST = (IntPtr)0xFFFF;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    /// <summary><paramref name="off"/> = true — выключить мониторы, false — включить.</summary>
    public static void Set(bool off)
    {
        // lParam: 2 — выключить (энергосбережение), 1 — пониженное питание, -1 — включить.
        IntPtr lParam = off ? (IntPtr)2 : (IntPtr)(-1);
        try
        {
            SendMessageTimeout(HWND_BROADCAST, WM_SYSCOMMAND, (IntPtr)SC_MONITORPOWER, lParam,
                SMTO_ABORTIFHUNG, 1000, out _);
            AgentJournal.Add("monitor", off ? "Мониторы выключены" : "Мониторы включены");
        }
        catch (Exception ex)
        {
            AgentLog.Add("монитор: " + ex.Message);
        }
    }
}
