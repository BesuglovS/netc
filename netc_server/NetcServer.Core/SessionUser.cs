using System.Runtime.InteropServices;

namespace NetcServer.Core;

/// <summary>Имя пользователя активной консольной сессии. Если процесс уже в сессии — возвращает его напрямую.</summary>
public static class SessionUser
{
    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQuerySessionInformationW(IntPtr hServer, uint sessionId, int wtsInfoClass,
        out IntPtr ppBuffer, out uint pBytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr p);

    private const int WTSUserName = 5;

    public static string GetActiveUserName()
    {
        // В интерактивном процессе (агент) имя пользователя известно напрямую.
        string direct = Environment.UserName;
        if (!string.IsNullOrEmpty(direct) &&
            !string.Equals(direct, "SYSTEM", StringComparison.OrdinalIgnoreCase))
        {
            return direct;
        }

        try
        {
            uint sid = WTSGetActiveConsoleSessionId();
            if (sid == 0xFFFFFFFF) return direct;

            if (WTSQuerySessionInformationW(IntPtr.Zero, sid, WTSUserName, out IntPtr buf, out _) && buf != IntPtr.Zero)
            {
                try { return Marshal.PtrToStringUni(buf) ?? direct; }
                finally { WTSFreeMemory(buf); }
            }
        }
        catch { }
        return direct;
    }
}
