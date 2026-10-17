using System.Net;
using System.Net.Sockets;

namespace NetcShared;

/// <summary>Отправка magic-пакета Wake-on-LAN.</summary>
public static class Wol
{
    /// <summary>Шлёт magic-пакет по MAC. <paramref name="ipHint"/> — для адресов подсети (если есть).</summary>
    public static bool Wake(string mac, string? ipHint = null)
    {
        byte[]? macBytes = ParseMac(mac);
        if (macBytes is null) return false;

        byte[] packet = new byte[6 + 16 * 6];
        for (int i = 0; i < 6; i++) packet[i] = 0xFF;
        for (int r = 0; r < 16; r++) Buffer.BlockCopy(macBytes, 0, packet, 6 + r * 6, 6);

        var targets = new List<IPAddress> { IPAddress.Broadcast };
        if (ipHint is not null && IPAddress.TryParse(ipHint, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            targets.Add(new IPAddress(new byte[] { b[0], b[1], b[2], 255 }));   // /24
            targets.Add(new IPAddress(new byte[] { b[0], b[1], 255, 255 }));   // /16
        }

        bool any = false;
        using var udp = new UdpClient { EnableBroadcast = true };
        foreach (var t in targets.Distinct())
        {
            foreach (int port in new[] { 9, 7 })
            {
                try { udp.Send(packet, packet.Length, new IPEndPoint(t, port)); any = true; }
                catch { }
            }
        }
        return any;
    }

    private static byte[]? ParseMac(string mac)
    {
        try
        {
            var parts = mac.Split(new[] { '-', ':', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 6) return null;
            var bytes = new byte[6];
            for (int i = 0; i < 6; i++) bytes[i] = Convert.ToByte(parts[i], 16);
            return bytes;
        }
        catch { return null; }
    }
}
