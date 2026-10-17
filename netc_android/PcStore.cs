using Android.Content;

namespace NetcAndroid;

/// <summary>
/// Список ПК класса: загрузка/сохранение в приватный файл <c>netc_pcs.txt</c> рядом с приложением.
/// Формат строки: «адрес|имя|модель|mac» (старый формат — только адрес).
/// При первом запуске подставляются известные 24 ПК (адреса и имена — как в клиенте учителя).
/// </summary>
public sealed class PcStore
{
    private readonly string _path;

    public List<PcEntry> Items { get; } = new();

    /// <summary>Известные ПК класса: адрес, имя (R114-NN) и модель (инвентаризация §9.1).</summary>
    public static readonly (string Addr, string Name, string Model)[] Defaults =
    {
        ("10.12.6.16",  "R114-01", "Acer TravelMate P215-53"),
        ("10.12.6.17",  "R114-02", "Acer TravelMate P215-53"),
        ("10.12.6.15",  "R114-03", "Acer TravelMate P215-53"),
        ("10.12.6.19",  "R114-04", "Acer TravelMate P215-53"),
        ("10.12.6.24",  "R114-05", "Acer TravelMate P215-53"),
        ("10.12.5.27",  "R114-06", "Acer TravelMate P215-53"),
        ("10.90.38.49", "R114-07", "ICL RAYbook Bi1504"),
        ("10.90.9.49",  "R114-08", "ICL RAYbook Bi1504"),
        ("10.90.3.153", "R114-09", "ICL RAYbook Bi1504"),
        ("10.90.1.127", "R114-10", "ICL RAYbook Bi1504"),
        ("10.90.3.38",  "R114-11", "ICL RAYbook Bi1504"),
        ("10.90.1.236", "R114-12", "ICL RAYbook Bi1504"),
        ("10.90.2.63",  "R114-13", "ICL RAYbook Bi1504"),
        ("10.90.7.186", "R114-14", "ICL RAYbook Bi1504"),
        ("10.12.6.13",  "R114-15", "Acer TravelMate P215-53"),
        ("10.12.6.14",  "R114-16", "Acer TravelMate P215-53"),
        ("10.12.6.10",  "R114-17", "Acer TravelMate P215-53"),
        ("10.90.0.85",  "R114-18", "Acer TravelMate P215-53"),
        ("10.12.6.12",  "R114-19", "Acer TravelMate P215-53"),
        ("10.90.5.108", "R114-20", "ICL RAYbook Si1507"),
        ("10.90.2.76",  "R114-21", "ICL RAYbook Bi1504"),
        ("10.99.4.16",  "R114-22", "ICL RAYbook Bi1504"),
        ("10.90.5.153", "R114-23", "ICL RAYbook Si159"),
        ("10.90.3.39",  "R114-24", "ICL RAYbook Bi1504")
    };

    public PcStore(Context ctx)
    {
        _path = Path.Combine(ctx.FilesDir!.AbsolutePath, "netc_pcs.txt");
    }

    public void Load()
    {
        Items.Clear();
        bool loaded = false;
        try
        {
            if (File.Exists(_path))
            {
                foreach (var line in File.ReadAllLines(_path))
                {
                    var t = line.Trim();
                    if (t.Length == 0) continue;
                    var parts = t.Split('|');
                    var addr = parts[0].Trim();
                    if (addr.Length == 0) continue;
                    Items.Add(new PcEntry
                    {
                        Address = addr,
                        Name = parts.Length > 1 ? parts[1].Trim() : "",
                        Model = parts.Length > 2 ? parts[2].Trim() : "",
                        Mac = parts.Length > 3 ? parts[3].Trim() : ""
                    });
                    loaded = true;
                }
            }
        }
        catch { }

        if (!loaded || Items.Count == 0)
        {
            Items.Clear();
            foreach (var d in Defaults)
                Items.Add(new PcEntry { Address = d.Addr, Name = d.Name, Model = d.Model });
            Save();
        }
    }

    public void Save()
    {
        try
        {
            var lines = Items.Select(e =>
            {
                if (string.IsNullOrEmpty(e.Name) && string.IsNullOrEmpty(e.Model) && string.IsNullOrEmpty(e.Mac))
                    return e.Address;
                return string.Join("|", e.Address, San(e.Name), San(e.Model), San(e.Mac));
            });
            File.WriteAllLines(_path, lines);
        }
        catch { }
    }

    public PcEntry? Find(string address) =>
        Items.FirstOrDefault(e => string.Equals(e.Address, address, StringComparison.OrdinalIgnoreCase));

    private static string San(string value) =>
        value.Replace("|", " ").Replace("\r", " ").Replace("\n", " ").Trim();
}
