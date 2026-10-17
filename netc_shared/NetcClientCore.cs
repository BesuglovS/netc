using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace NetcShared;

public sealed record DiscoveredPc(string Address, PcInfo Info);

/// <summary>Клиентская логика: обнаружение агентов и запрос сведений.</summary>
public static class NetcClientCore
{
    public static async Task<List<DiscoveredPc>> DiscoverAsync(int port, string sharedKey, int timeoutMs, CancellationToken ct)
    {
        var results = new List<DiscoveredPc>();
        var seen = new HashSet<string>();

        using var udp = new UdpClient();
        udp.EnableBroadcast = true;
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        // Запрос содержит токен из общего ключа — отвечают только «свои» агенты.
        // Шлём удлинённый и старый токен: смешанный класс (часть ПК ещё на старой сборке) отвечает
        // на тот токен, который знает.
        string shortTok = NetcProtocol.DiscoveryToken(sharedKey);
        string longTok = NetcProtocol.DiscoveryTokenLong(sharedKey);
        try
        {
            await udp.SendAsync(Encoding.UTF8.GetBytes(NetcProtocol.DiscoveryRequest + " " + longTok),
                new IPEndPoint(IPAddress.Broadcast, port), ct);
            await udp.SendAsync(Encoding.UTF8.GetBytes(NetcProtocol.DiscoveryRequest + " " + shortTok),
                new IPEndPoint(IPAddress.Broadcast, port), ct);
        }
        catch { /* broadcast может быть запрещён */ }

        string longPrefix = NetcProtocol.DiscoveryReplyPrefix + longTok + " ";
        string shortPrefix = NetcProtocol.DiscoveryReplyPrefix + shortTok + " ";
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!ct.IsCancellationRequested)
        {
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero) break;

            Task<UdpReceiveResult> receive = udp.ReceiveAsync(ct).AsTask();
            Task delay = Task.Delay(remaining, ct);
            Task finished = await Task.WhenAny(receive, delay);
            if (finished != receive) break;

            UdpReceiveResult r;
            try { r = await receive; }
            catch { break; }

            string text = Encoding.UTF8.GetString(r.Buffer);
            string? prefix = text.StartsWith(longPrefix, StringComparison.Ordinal) ? longPrefix
                : text.StartsWith(shortPrefix, StringComparison.Ordinal) ? shortPrefix
                : null;
            if (prefix is null) continue;

            PcInfo? info = JsonSerializer.Deserialize<PcInfo>(
                text.Substring(prefix.Length), Json.Options);
            if (info is null) continue;

            string addr = r.RemoteEndPoint.Address.ToString();
            if (seen.Add(addr)) results.Add(new DiscoveredPc(addr, info));
        }
        return results;
    }

    public static async Task<PcInfo?> QueryAsync(string host, int port, byte[] key, int timeoutMs, CancellationToken ct)
    {
        using var tcp = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        try
        {
            await tcp.ConnectAsync(host, port, cts.Token);
            tcp.NoDelay = true;
            using var channel = new SecureChannel(tcp.GetStream(), key);

            await channel.SendAsync(MsgType.Hello, Json.ToBytes(new HelloMessage
            {
                Version = NetcProtocol.Version,
                ClientName = Environment.MachineName,
                Purpose = "query"
            }), cts.Token);

            var ack = await channel.ReceiveAsync(cts.Token);
            if (ack is null || ack.Value.Type != MsgType.HelloAck) return null;

            await channel.SendAsync(MsgType.InfoRequest, null, cts.Token);
            var info = await channel.ReceiveAsync(cts.Token);
            if (info is null || info.Value.Type != MsgType.InfoResponse) return null;

            return Json.FromBytes<PcInfo>(info.Value.Payload);
        }
        catch
        {
            return null;
        }
    }
}
