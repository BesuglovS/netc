using System.Net;
using System.Net.Sockets;
using System.Text;
using NetcShared;

namespace NetcServer.Agent;

/// <summary>
/// Локальный HTTP-прокси на <c>127.0.0.1</c> для фильтрации сайтов по чёрному/белому списку.
/// Браузеры, использующие системный прокси, обращаются сюда; хост берётся из абсолютного URI (HTTP)
/// либо из строки <c>CONNECT</c> (HTTPS). Фильтрация — по имени хоста (контент не анализируется),
/// поэтому работает и для HTTPS. Работает в сессии ученика без прав администратора.
/// </summary>
public sealed class SiteFilter : IDisposable
{
    private const int MaxHeadBytes = 64 * 1024;
    private const int CopyBufferSize = 32 * 1024;
    private const int JournalThrottleMs = 5000;

    private readonly int _port;
    private readonly object _gate = new();
    private SiteFilterMode _mode = SiteFilterMode.Off;
    private string[] _sites = Array.Empty<string>();

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    private string _lastBlockedHost = "";
    private long _lastBlockedAtTicks;

    public SiteFilter(int port) => _port = port;

    public int Port => _port;
    public bool Running { get; private set; }

    /// <summary>Обновляет режим и список (можно вызывать на работающем фильтре).</summary>
    public void Update(SiteFilterMode mode, IEnumerable<string>? sites)
    {
        var set = (sites ?? Enumerable.Empty<string>())
            .Select(SiteFilterCore.Normalize)
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        lock (_gate) { _mode = mode; _sites = set; }
    }

    public void Start()
    {
        if (Running) return;

        var listener = new TcpListener(IPAddress.Loopback, _port);
        try { listener.Start(); }
        catch (Exception ex)
        {
            AgentLog.Add("фильтр сайтов: не удалось занять 127.0.0.1:" + _port + " — " + ex.Message);
            return;
        }

        _listener = listener;
        _cts = new CancellationTokenSource();
        Running = true;
        _ = AcceptLoopAsync(listener, _cts.Token);
        AgentLog.Add("фильтр сайтов: запущен на 127.0.0.1:" + _port);
    }

    public void Stop()
    {
        if (!Running && _listener is null) return;
        Running = false;
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        try { _cts?.Dispose(); } catch { }
        _cts = null;
        _listener = null;
        AgentLog.Add("фильтр сайтов: остановлен");
    }

    public void Dispose() => Stop();

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch { continue; }
            _ = HandleClientAsync(client, ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            NetworkStream? stream = null;
            try
            {
                client.NoDelay = true;
                stream = client.GetStream();

                var head = await ReadHeadAsync(stream, ct).ConfigureAwait(false);
                if (head is null) return;

                if (!TryParseTarget(head.Value.Head, out string host, out int port, out bool isConnect, out byte[]? forwardHead))
                {
                    await WriteAsync(stream, "HTTP/1.1 400 Bad Request\r\nConnection: close\r\n\r\n", ct).ConfigureAwait(false);
                    return;
                }

                if (IsBlocked(host))
                {
                    JournalBlocked(host);
                    await WriteBlockAsync(stream, host, isConnect, ct).ConfigureAwait(false);
                    return;
                }

                using var upstream = new TcpClient();
                await upstream.ConnectAsync(host, port, ct).ConfigureAwait(false);
                upstream.NoDelay = true;
                var up = upstream.GetStream();

                if (isConnect)
                {
                    await WriteAsync(stream, "HTTP/1.1 200 Connection Established\r\n\r\n", ct).ConfigureAwait(false);
                }
                else
                {
                    await up.WriteAsync(forwardHead!, ct).ConfigureAwait(false);
                    // Байты, прочитанные сразу после заголовка (тело POST и т.п.), тоже пересылаем.
                    if (head.Value.Extra.Length > 0)
                        await up.WriteAsync(head.Value.Extra, ct).ConfigureAwait(false);
                    if (head.Value.Extra.Length > 0 || forwardHead!.Length > 0)
                        await up.FlushAsync(ct).ConfigureAwait(false);
                }

                await Task.WhenAny(
                    PumpAsync(stream, up, ct),
                    PumpAsync(up, stream, ct)).ConfigureAwait(false);
            }
            catch { /* соединение закрыто или ошибка — игнорируем */ }
            finally { try { stream?.Dispose(); } catch { } }
        }
    }

    private bool IsBlocked(string host)
    {
        SiteFilterMode mode;
        string[] sites;
        lock (_gate) { mode = _mode; sites = _sites; }
        return SiteFilterCore.IsBlocked(mode, sites, host);
    }

    private void JournalBlocked(string host)
    {
        // Несколько запросов к одному хосту подряд не забивают журнал.
        long now = Environment.TickCount64;
        if (host == _lastBlockedHost && now - _lastBlockedAtTicks < JournalThrottleMs) return;
        _lastBlockedHost = host;
        _lastBlockedAtTicks = now;
        AgentJournal.Add("internet", "Заблокирован сайт: " + host);
    }

    // ---- Разбор запроса ----

    private static async Task<(byte[] Head, byte[] Extra)?> ReadHeadAsync(NetworkStream stream, CancellationToken ct)
    {
        var buf = new byte[MaxHeadBytes];
        int len = 0;
        while (len < buf.Length)
        {
            int r = await stream.ReadAsync(buf.AsMemory(len, buf.Length - len), ct).ConfigureAwait(false);
            if (r == 0) return null;
            len += r;

            int end = HeadEnd(buf, len);
            if (end >= 0)
            {
                var head = buf.AsSpan(0, end).ToArray();
                var extra = buf.AsSpan(end, len - end).ToArray();
                return (head, extra);
            }
        }
        return null;
    }

    private static int HeadEnd(byte[] b, int len)
    {
        for (int i = 3; i < len; i++)
        {
            if (b[i - 3] == (byte)'\r' && b[i - 2] == (byte)'\n' && b[i - 1] == (byte)'\r' && b[i] == (byte)'\n')
                return i + 1;
        }
        return -1;
    }

    private static bool TryParseTarget(byte[] head, out string host, out int port, out bool isConnect, out byte[]? forwardHead)
    {
        host = "";
        port = 80;
        isConnect = false;
        forwardHead = null;

        int lineEnd = Array.IndexOf(head, (byte)'\n');
        if (lineEnd < 0) return false;

        string firstLine = Encoding.ASCII.GetString(head, 0, lineEnd).Trim();
        var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;

        string method = parts[0].ToUpperInvariant();
        string target = parts[1];
        string version = parts.Length > 2 ? parts[2] : "HTTP/1.0";

        if (method == "CONNECT")
        {
            isConnect = true;
            (host, port) = SplitHostPort(target, 443);
            return host.Length > 0;
        }

        // Прокси-запрос: абсолютный URI «http://host[:port]/path».
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Host.Length > 0
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            host = uri.Host;
            port = uri.IsDefaultPort ? (uri.Scheme == Uri.UriSchemeHttps ? 443 : 80) : uri.Port;

            // Переписываем строку запроса в origin-form (её ждёт сервер назначения).
            string rest = head.Length > lineEnd + 1
                ? Encoding.ASCII.GetString(head, lineEnd + 1, head.Length - lineEnd - 1)
                : "";
            string rebuilt = method + " " + uri.PathAndQuery + " " + version + "\r\n" + rest;
            forwardHead = Encoding.ASCII.GetBytes(rebuilt);
            return host.Length > 0;
        }

        // Origin-form «/path» — хост берём из заголовка Host (некоторые клиенты так делают).
        if (target.StartsWith('/'))
        {
            string text = Encoding.ASCII.GetString(head);
            var m = System.Text.RegularExpressions.Regex.Match(text, @"(?im)^Host:\s*([^\r\n]+)");
            if (!m.Success) return false;
            (host, port) = SplitHostPort(m.Groups[1].Value.Trim(), 80);
            forwardHead = head; // уже origin-form — пересылаем как есть
            return host.Length > 0;
        }

        return false;
    }

    private static (string host, int port) SplitHostPort(string value, int defaultPort)
    {
        value = value.Trim();
        int close = value.IndexOf(']');                       // IPv6: [::1]:443
        if (close >= 0)
        {
            string h6 = value[..(close + 1)].Trim('[', ']');
            int after = value.IndexOf(':', close);
            if (after > 0 && int.TryParse(value[(after + 1)..], out int p6)) return (h6, p6);
            return (h6, defaultPort);
        }
        int colon = value.LastIndexOf(':');
        if (colon > 0 && int.TryParse(value[(colon + 1)..], out int p)) return (value[..colon], p);
        return (value, defaultPort);
    }

    // ---- Ответы ----

    private static async Task WriteBlockAsync(Stream stream, string host, bool isConnect, CancellationToken ct)
    {
        if (isConnect)
        {
            // Для HTTPS достаточно закрыть соединение с ошибкой — браузер покажет страницу-ошибку.
            await WriteAsync(stream, "HTTP/1.1 403 Forbidden\r\nConnection: close\r\n\r\n", ct).ConfigureAwait(false);
            return;
        }

        string safeHost = WebUtility.HtmlEncode(host);
        string body =
            "<!doctype html><meta charset=\"utf-8\"><title>Сайт заблокирован</title>" +
            "<body style=\"font-family:'Segoe UI',sans-serif;text-align:center;margin-top:12%;color:#1c2026\">" +
            "<h1 style=\"color:#2563eb\">Сайт заблокирован</h1>" +
            "<p>Доступ к <b>" + safeHost + "</b> закрыт учителем.</p>" +
            "<p style=\"color:#788291\">netc — управление классом</p></body>";
        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);

        string head = "HTTP/1.1 403 Forbidden\r\n"
            + "Content-Type: text/html; charset=utf-8\r\n"
            + "Connection: close\r\n"
            + "Content-Length: " + bodyBytes.Length + "\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct).ConfigureAwait(false);
        await stream.WriteAsync(bodyBytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static Task WriteAsync(Stream stream, string text, CancellationToken ct)
        => stream.WriteAsync(Encoding.ASCII.GetBytes(text), ct).AsTask();

    private static async Task PumpAsync(Stream from, Stream to, CancellationToken ct)
    {
        var buf = new byte[CopyBufferSize];
        try
        {
            while (true)
            {
                int r = await from.ReadAsync(buf, ct).ConfigureAwait(false);
                if (r <= 0) break;
                await to.WriteAsync(buf.AsMemory(0, r), ct).ConfigureAwait(false);
                await to.FlushAsync(ct).ConfigureAwait(false);
            }
        }
        catch { }
        // Дошли до конца одного направления — закрываем приёмную сторону, чтобы завершился и второй поток.
        try { to.Close(); } catch { }
    }
}
