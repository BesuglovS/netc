using Android.Content;
using NetcShared;

namespace NetcAndroid;

/// <summary>Настройки Android-клиента (общий ключ и порты). Хранятся в SharedPreferences.</summary>
public sealed class AppSettings
{
    private const string PrefsName = "netc";
    private const string KeySharedKey = "sharedKey";
    private const string KeyControlPort = "controlPort";
    private const string KeyDiscoveryPort = "discoveryPort";

    public string SharedKey { get; set; } = "netc-default-key";
    public int ControlPort { get; set; } = NetcProtocol.ControlPort;
    public int DiscoveryPort { get; set; } = NetcProtocol.DiscoveryPort;

    public bool IsDefaultKey => string.Equals(SharedKey, "netc-default-key", StringComparison.Ordinal);

    public static AppSettings Load(Context ctx)
    {
        var s = new AppSettings();
        var prefs = ctx.GetSharedPreferences(PrefsName, FileCreationMode.Private);
        if (prefs is null) return s;
        s.SharedKey = prefs.GetString(KeySharedKey, s.SharedKey) ?? s.SharedKey;
        s.ControlPort = prefs.GetInt(KeyControlPort, s.ControlPort);
        s.DiscoveryPort = prefs.GetInt(KeyDiscoveryPort, s.DiscoveryPort);
        return s;
    }

    public void Save(Context ctx)
    {
        var prefs = ctx.GetSharedPreferences(PrefsName, FileCreationMode.Private);
        prefs?.Edit()
            ?.PutString(KeySharedKey, SharedKey)
            ?.PutInt(KeyControlPort, ControlPort)
            ?.PutInt(KeyDiscoveryPort, DiscoveryPort)
            ?.Apply();
    }
}
