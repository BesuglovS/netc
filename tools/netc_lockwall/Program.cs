using System.Net.Sockets;
using NetcShared;

// Раскладка картинок ЭКРАНА БЛОКИРОВКИ по ПК: i-й ПК из ips.txt получает i-й файл из папки
// (сортировка по имени файла). Экран блокировки — машинная настройка, агент выполняет её через
// службу-сторож (SYSTEM).
//
//   NetcLockWall [imagesDir] [ipsFile] [--dry]
//
// Пример:
//   dotnet run --project C:\admin\files\tools\netc_lockwall -- C:\admin\files\lock-cats C:\admin\files\ips.txt

string dir = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : @"C:\admin\files\lock-cats";
string ipsFile = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : @"C:\admin\files\ips.txt";
bool dry = args.Contains("--dry");

var ips = File.ReadAllLines(ipsFile).Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
string[] exts = { ".jpg", ".jpeg", ".png", ".bmp" };
var imgs = Directory.GetFiles(dir)
    .Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant()))
    .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
    .ToArray();

int n = ips.Length;
if (imgs.Length == 0) { Console.WriteLine($"В папке {dir} нет картинок."); return; }
Console.WriteLine($"ПК: {ips.Length}, картинок: {imgs.Length}, режим: {(imgs.Length >= ips.Length ? "по порядку" : "циклично")}");
if (dry)
{
    for (int i = 0; i < n; i++)
        Console.WriteLine($"  R114-{i + 1:D2}  {ips[i],-14} <- {Path.GetFileName(imgs[i % imgs.Length])}");
    Console.WriteLine("(сухой прогон: ничего не применялось)");
    return;
}
if (n == 0) { Console.WriteLine("Нечего применять."); return; }

var settings = NetcSettings.Load();
var key = NetcCrypto.DeriveKey(settings.SharedKey);
int ok = 0;

for (int i = 0; i < n; i++)
{
    string ip = ips[i];
    string img = imgs[i % imgs.Length];
    string name = Path.GetFileName(img);
    try
    {
        using var tcp = new TcpClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await tcp.ConnectAsync(ip, settings.ControlPort, cts.Token);
        tcp.NoDelay = true;
        using var ch = new SecureChannel(tcp.GetStream(), key);

        await ch.SendAsync(MsgType.Hello,
            Json.ToBytes(new HelloMessage { Version = NetcProtocol.Version, ClientName = "lockwall" }), cts.Token);
        var ack = await ch.ReceiveAsync(cts.Token);
        if (ack?.Type != MsgType.HelloAck) { Console.WriteLine($"[{ip}] нет рукопожатия"); continue; }

        byte[] data = File.ReadAllBytes(img);
        await ch.SendAsync(MsgType.FileStart,
            Json.ToBytes(new FileTransferInfo { Name = name, Size = data.Length }), cts.Token);
        for (int off = 0; off < data.Length; off += 32768)
        {
            int len = Math.Min(32768, data.Length - off);
            var chunk = new byte[len];
            Array.Copy(data, off, chunk, 0, len);
            await ch.SendAsync(MsgType.FileChunk, chunk, cts.Token);
        }
        await ch.SendAsync(MsgType.FileEnd, Array.Empty<byte>(), cts.Token);

        var fm = await ch.ReceiveAsync(cts.Token);
        var fr = fm is null ? null : Json.FromBytes<FileResult>(fm.Value.Payload);
        if (fr is null || !fr.Ok)
        {
            Console.WriteLine($"[{ip}] R114-{i + 1:D2}  файл не передан: {fr?.Message}");
            continue;
        }

        var msg = new PersonalizationMessage { LockScreenPath = fr.Path };
        await ch.SendAsync(MsgType.Personalization, Json.ToBytes(msg), cts.Token);
        var pm = await ch.ReceiveAsync(cts.Token);
        var pr = pm is null ? null : Json.FromBytes<PersonalizationResult>(pm.Value.Payload);

        if (pr?.Ok == true) ok++;
        Console.WriteLine($"[{ip}] R114-{i + 1:D2}  {name}  ->  {(pr?.Ok == true ? "OK" : "FAIL")}  {pr?.Message}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{ip}] R114-{i + 1:D2}  {name}  ->  ОШИБКА: {ex.Message}");
    }
}

Console.WriteLine($"Готово: {ok}/{n}");
