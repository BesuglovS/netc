using System;
using System.Net;
using ControlWorksCollector.Native;

namespace ControlWorksCollector.Services
{
    public sealed class DiscoverResult
    {
        public bool Ok { get; set; }
        public string Hostname { get; set; }
        public string Ip { get; set; }
        public string Mac { get; set; }
        public string Error { get; set; }

        public DiscoverResult()
        {
            Hostname = "";
            Ip = "";
            Mac = "";
            Error = "";
        }
    }

    /// <summary>Определение имени компьютера и MAC по известному адресу/имени.</summary>
    public static class PcDiscovery
    {
        public static DiscoverResult Discover(PcRecord rec, string userName, string password, int timeoutSeconds)
        {
            DiscoverResult res = new DiscoverResult();

            System.Collections.Generic.List<string> candidates = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(rec.Ip)) candidates.Add(rec.Ip.Trim());
            if (!string.IsNullOrWhiteSpace(rec.Hostname)) candidates.Add(rec.Hostname.Trim());

            if (candidates.Count == 0)
            {
                res.Error = "Не заданы имя ПК и IP.";
                return res;
            }

            string chosen = null;
            foreach (string c in candidates)
            {
                if (NetworkUtil.IsSmbReachable(c, Math.Max(2, timeoutSeconds / 3)))
                {
                    chosen = c;
                    break;
                }
            }
            if (chosen == null)
            {
                res.Error = "ПК недоступен.";
                return res;
            }

            NetworkSession session = null;
            try
            {
                string err;
                session = NetworkSession.Connect(chosen, "IPC$", userName, password, out err);
                if (session == null)
                {
                    res.Error = err;
                    return res;
                }

                // Имя компьютера (по установленной сессии).
                res.Hostname = Win32.GetRemoteComputerName(chosen);

                // Текущий IP.
                string ip = chosen;
                IPAddress parsed;
                if (!IPAddress.TryParse(chosen, out parsed))
                {
                    try
                    {
                        foreach (IPAddress a in Dns.GetHostAddresses(chosen))
                        {
                            if (a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            {
                                ip = a.ToString();
                                break;
                            }
                        }
                    }
                    catch
                    {
                        // оставляем исходное значение
                    }
                }
                res.Ip = ip;

                // MAC (в пределах локальной подсети).
                res.Mac = Win32.GetMacFromArp(ip);

                res.Ok = true;
            }
            catch (Exception ex)
            {
                res.Error = ex.Message;
            }
            finally
            {
                if (session != null)
                {
                    session.Dispose();
                }
            }

            return res;
        }
    }
}
