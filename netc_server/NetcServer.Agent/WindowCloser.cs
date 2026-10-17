using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NetcShared;

namespace NetcServer.Agent;

/// <summary>Кандидат на закрытие: пользовательское окно в сессии ученика.</summary>
public sealed record WindowTarget(IntPtr Handle, uint Pid, string Process, string ClassName, bool Killable);

/// <summary>
/// Закрытие всех пользовательских окон в сессии ученика: приложения и окна Проводника.
/// Всем подходящим окнам отправляется <c>WM_CLOSE</c>; при <see cref="CloseAppsMessage.Force"/> —
/// процессы, чьи окна не закрылись, завершаются принудительно.
/// Рабочий стол, панель задач, системные службы и сам агент не затрагиваются.
/// </summary>
public static class WindowCloser
{
    private const uint WM_CLOSE = 0x0010;
    private const uint GW_OWNER = 4;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Классы окон оболочки (рабочий стол, панель задач, меню «Пуск», переключатель задач) — не закрываем.</summary>
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow",
        "Windows.Internal.Shell.TabProxyWindow", "ForegroundStaging",
        "TaskManagerWindow", "MultitaskingViewFrame", "SysShadow",
        "Windows.UI.Composition.DesktopWindowContentBridge"
    };

    /// <summary>
    /// Возвращает окна, подлежащие закрытию (без отправки сообщений) — используется и для проверок.
    /// Владельцу процесса самого агента и системным окнам не соответствует.
    /// </summary>
    public static List<WindowTarget> EnumerateTargets(bool includeExplorer)
    {
        int selfPid = Environment.ProcessId;
        var list = new List<WindowTarget>();

        EnumWindows((h, _) =>
        {
            // Только видимые верхнеуровневые окна без владельца (главные окна приложений).
            if (!IsWindowVisible(h)) return true;
            if (GetWindow(h, GW_OWNER) != IntPtr.Zero) return true;

            string cls = ClassName(h);
            if (ShellClasses.Contains(cls)) return true;

            bool explorerWindow = cls is "CabinetWClass" or "ExploreWClass";
            if (explorerWindow && !includeExplorer) return true;
            bool uwpFrame = cls == "ApplicationFrameWindow";

            GetWindowThreadProcessId(h, out uint pid);
            if (pid == 0 || pid == selfPid) return true;

            string proc = ProcessName(pid);
            if (proc.Length == 0) return true;                          // не удалось определить — не трогаем

            // Проводник: закрываем только окна папок (CabinetWClass/ExploreWClass),
            // а служебные окна оболочки (DummyDWMListenerWindow и т.п.) не трогаем.
            if (proc == "explorer" && !explorerWindow) return true;

            bool killable = !explorerWindow && !uwpFrame && proc != "explorer";
            if (killable && ProcessGuard.IsProtected(proc)) return true;   // системные/служебные процессы

            list.Add(new WindowTarget(h, pid, proc, cls, killable));
            return true;
        }, IntPtr.Zero);

        return list;
    }

    public static async Task<CloseAppsResult> CloseAsync(CloseAppsMessage req, CancellationToken ct)
    {
        var targets = EnumerateTargets(req.IncludeExplorer);
        int closed = 0;
        var appPids = new HashSet<int>();

        foreach (var t in targets)
        {
            if (t.Killable) appPids.Add((int)t.Pid);
            if (PostMessage(t.Handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero)) closed++;
        }

        int forced = 0;
        string extra = "";
        if (req.Force && appPids.Count > 0)
        {
            int grace = Math.Clamp(req.GraceMs, 0, 30000);
            try { await Task.Delay(grace, ct).ConfigureAwait(false); } catch { }

            // Принудительное завершение выполняет служба-сторож (SYSTEM): она может закрыть
            // и приложения, запущенные от администратора (агенту это недоступно — UIPI).
            var (wdOk, wdMsg, wdCount) = await MachineBridge
                .RequestAsync("kill-pids", null, appPids, 8000, ct).ConfigureAwait(false);

            if (wdOk)
            {
                forced = wdCount;
                extra = " (через сторож)";
            }
            else
            {
                // Сторож недоступен — завершаем сами то, что можем (повышенные приложения не поддадутся).
                foreach (int pid in appPids)
                {
                    try
                    {
                        using var p = Process.GetProcessById(pid);
                        if (p.HasExited) continue;
                        p.Kill();
                        forced++;
                    }
                    catch { }
                }
                extra = " (локально; " + wdMsg + ")";
            }
        }

        var result = new CloseAppsResult
        {
            Windows = closed,
            Forced = forced,
            Message = $"закрытие окон: {closed}" + (req.Force ? $", принудительно завершено процессов: {forced}{extra}" : "")
        };
        AgentJournal.Add("windows", result.Message);
        return result;
    }

    private static string ClassName(IntPtr h)
    {
        var sb = new StringBuilder(256);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string ProcessName(uint pid)
    {
        try { return Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant(); }
        catch { return ""; }
    }
}
