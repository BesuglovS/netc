using System.Diagnostics;
using NetcShared;

namespace NetcServer.Watchdog;

/// <summary>
/// Принудительное завершение процессов от SYSTEM по списку идентификаторов.
/// Проверяет, что процесс принадлежит нужной сессии и не входит в защищённый набор
/// (<see cref="ProcessGuard"/>), поэтому повышенные приложения (от администратора) закрываются,
/// а системные и сам комплекс netc — нет.
/// </summary>
public static class ProcessKiller
{
    public static (int Killed, int Skipped) Kill(IReadOnlyCollection<int>? pids, uint session)
    {
        if (pids is null || pids.Count == 0) return (0, 0);

        int killed = 0, skipped = 0;
        int self = Environment.ProcessId;

        foreach (int pid in pids)
        {
            try
            {
                if (pid == self) { skipped++; continue; }

                using var p = Process.GetProcessById(pid);
                if (p.HasExited) { skipped++; continue; }
                if (p.SessionId == 0) { skipped++; continue; }                     // системная сессия
                if (session != 0xFFFFFFFF && p.SessionId != session) { skipped++; continue; }
                if (ProcessGuard.IsProtected(p.ProcessName)) { skipped++; continue; }

                p.Kill();
                killed++;
            }
            catch { skipped++; }
        }

        return (killed, skipped);
    }
}
