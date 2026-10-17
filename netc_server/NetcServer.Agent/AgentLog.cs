using System.Text;

namespace NetcServer.Agent;

/// <summary>
/// Простой файловый лог агента: <c>%LOCALAPPDATA%\netc\agent.log</c> с ротацией по размеру.
/// Раньше сообщения уходили только в <see cref="System.Diagnostics.Debug"/>, что в релизе не видно.
/// </summary>
public static class AgentLog
{
    private static readonly object Gate = new();
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "netc", "agent.log");

    private const long MaxBytes = 1_000_000;

    public static void Add(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxBytes)
                {
                    try { File.Delete(LogPath + ".1"); } catch { }
                    try { File.Move(LogPath, LogPath + ".1", overwrite: true); } catch { }
                }
                File.AppendAllText(LogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + Environment.NewLine,
                    Encoding.UTF8);
            }
        }
        catch { /* лог не критичен */ }
    }
}
