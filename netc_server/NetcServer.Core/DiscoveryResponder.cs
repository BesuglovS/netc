using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using NetcShared;

namespace NetcServer.Core;

/// <summary>UDP-ответчик обнаружения.</summary>
public sealed class DiscoveryResponder
{
    private readonly NetcSettings _settings;
    private readonly Action<string> _log;

    public DiscoveryResponder(NetcSettings settings, Action<string> log)
    {
        _settings = settings;
        _log = log ?? (_ => { });
    }

    public async Task RunAsync(CancellationToken ct)
    {
        UdpClient udp;
        try
        {
            udp = new UdpClient(new IPEndPoint(IPAddress.Any, _settings.DiscoveryPort));
        }
        catch (Exception ex)
        {
            // Порт занят/запрещён — не роняем задачу молча (в отличие от TCP-сервера ниже всё логируем).
            _log("UDP: не удалось открыть порт " + _settings.DiscoveryPort + ": " + ex.Message);
            return;
        }
        using (udp)
        {
            _log("netc: прослушивание UDP :" + _settings.DiscoveryPort);

            while (!ct.IsCancellationRequested)
            {
                UdpReceiveResult r;
                try
                {
                    r = await udp.ReceiveAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _log("UDP приём: " + ex.Message);
                    continue;
                }

                string text = Encoding.UTF8.GetString(r.Buffer).Trim();
                // Отвечаем только на запрос с токеном из общего ключа: «NETC?1 <токен>».
                // Принимаем удлинённый токен и старый (совместимость на время обновления класса).
                string shortTok = NetcProtocol.DiscoveryToken(_settings.SharedKey);
                string longTok = NetcProtocol.DiscoveryTokenLong(_settings.SharedKey);
                string matchTok;
                if (text == NetcProtocol.DiscoveryRequest + " " + longTok) matchTok = longTok;
                else if (text == NetcProtocol.DiscoveryRequest + " " + shortTok) matchTok = shortTok;
                else continue;

                try
                {
                    string reply = NetcProtocol.DiscoveryReplyPrefix + matchTok + " " +
                        JsonSerializer.Serialize(Redact(PcInfoProvider.Get()), Json.Options);
                    await udp.SendAsync(Encoding.UTF8.GetBytes(reply), r.RemoteEndPoint, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log("UDP ответ: " + ex.Message);
                }
            }
        }
    }

    /// <summary>Ответ по UDP не шифруется, поэтому не отдаём посторонним имя пользователя,
    /// версию ОС и агента — оставляем только то, что нужно для списка ПК.</summary>
    private static PcInfo Redact(PcInfo info) => new()
    {
        MachineName = info.MachineName,
        Manufacturer = info.Manufacturer,
        Model = info.Model,
        MacAddress = info.MacAddress,
        Addresses = info.Addresses
    };
}
