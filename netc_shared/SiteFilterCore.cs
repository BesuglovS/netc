using System.Net;

namespace NetcShared;

/// <summary>
/// Сопоставление хоста со списком фильтра сайтов (белый/чёрный список) и решение о блокировке.
/// Используется локальным прокси агента (<c>NetcServer.Agent.SiteFilter</c>).
/// </summary>
public static class SiteFilterCore
{
    /// <summary>
    /// Нормализует запись фильтра (или хост): нижний регистр, без схемы, пути, порта и ведущих «*.»/«www.».
    /// Примеры: «https://WWW.Example.com/path?x=1» → «example.com»; «*.school.ru» → «school.ru».
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        string s = value.Trim().ToLowerInvariant();

        int scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) s = s[(scheme + 3)..];

        int cut = s.IndexOfAny(new[] { '/', '?', '#', '\\' });
        if (cut >= 0) s = s[..cut];

        int at = s.IndexOf('@');
        if (at >= 0) s = s[(at + 1)..];

        if (s.StartsWith("*.")) s = s[2..];
        if (s.StartsWith("www.")) s = s[4..];

        // Отбрасываем порт (IPv6 в скобках не трогаем).
        if (!s.StartsWith('['))
        {
            int colon = s.LastIndexOf(':');
            if (colon > 0 && s.IndexOf(':') == colon) s = s[..colon];
        }

        return s.TrimEnd('.');
    }

    /// <summary>Совпадает ли хост с шаблоном: точное совпадение или поддомен.</summary>
    public static bool Matches(string pattern, string host)
    {
        if (pattern.Length == 0 || host.Length == 0) return false;
        return host.Equals(pattern, StringComparison.Ordinal)
            || host.EndsWith("." + pattern, StringComparison.Ordinal);
    }

    public static bool MatchesAny(IEnumerable<string> patterns, string host)
    {
        foreach (var p in patterns)
        {
            if (Matches(p, host)) return true;
        }
        return false;
    }

    /// <summary>Нужно ли блокировать хост согласно режиму и списку.</summary>
    public static bool IsBlocked(SiteFilterMode mode, IReadOnlyList<string> sites, string? hostRaw)
    {
        string host = Normalize(hostRaw);
        if (host.Length == 0) return false;

        // Локальная сеть и обратный адрес не фильтруются: в белом списке они должны остаться доступными.
        if (IsLocal(host)) return false;

        return mode switch
        {
            SiteFilterMode.Blacklist => MatchesAny(sites, host),
            SiteFilterMode.Whitelist => !MatchesAny(sites, host),
            _ => false
        };
    }

    /// <summary>Локальное/сетевое имя или частный IP — не подлежит фильтрации.</summary>
    public static bool IsLocal(string host)
    {
        if (host is "localhost" or "127.0.0.1" or "::1" or "[::1]") return true;
        if (!host.Contains('.')) return true;                       // NetBIOS / однословное имя
        if (host.EndsWith(".local") || host.EndsWith(".lan") || host.EndsWith(".home")) return true;
        if (IPAddress.TryParse(host, out var ip) && IsPrivate(ip)) return true;
        return false;
    }

    private static bool IsPrivate(IPAddress ip)
    {
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            byte[] b = ip.GetAddressBytes();
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254)
                || b[0] == 127;
        }
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || (ip.GetAddressBytes()[0] & 0xFE) == 0xFC;
        }
        return false;
    }
}
