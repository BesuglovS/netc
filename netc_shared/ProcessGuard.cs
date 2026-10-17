namespace NetcShared;

/// <summary>
/// Процессы, которые нельзя завершать: системные службы и оболочка Windows, а также сам комплекс netc.
/// Общий список для агента (ограничение приложений, закрытие окон) и службы-сторожа (SYSTEM).
/// </summary>
public static class ProcessGuard
{
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "registry", "memory compression",
        "netcserver.agent", "netc_server", "netcserver.watchdog",
        "explorer", "dwm", "csrss", "winlogon", "wininit",
        "services", "lsass", "smss", "svchost", "fontdrvhost", "ctfmon", "runtimebroker",
        "shellexperiencehost", "startmenuexperiencehost", "searchhost", "searchapp", "sihost",
        "taskhostw", "dllhost", "conhost", "wmiprvse", "spoolsv", "audiodg", "logonui",
        "backgroundtaskhost", "textinputhost", "securityhealthservice", "securityhealthsystray",
        "sppsvc", "wudfhost", "useroobebroker", "deviceassociationbroker", "lockapp",
        "applicationframehost", "systemsettings", "msmpeng",
        "nissrv", "windowsinternal.composableshell.experiences.textinput.inputapp"
    };

    /// <summary>Имя процесса (без .exe) принадлежит защищённому системному/служебному набору.</summary>
    public static bool IsProtected(string? processName) => Protected.Contains(processName ?? "");
}
