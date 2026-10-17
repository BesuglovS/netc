using System;
using System.ComponentModel;
using System.Net.Sockets;
using System.Threading;
using ControlWorksCollector.Native;

namespace ControlWorksCollector.Services
{
    /// <summary>
    /// Устанавливает SMB-сессию к админ-шаре C$ удалённого компьютера под заданными учётными данными.
    /// </summary>
    public sealed class NetworkSession : IDisposable
    {
        public string UncRoot { get; private set; }

        private bool _connected;

        private NetworkSession(string uncRoot)
        {
            UncRoot = uncRoot;
        }

        public static NetworkSession Connect(string host, string userName, string password, out string error)
        {
            return Connect(host, "C$", userName, password, out error);
        }

        public static NetworkSession Connect(string host, string share, string userName, string password, out string error)
        {
            error = null;
            string unc = @"\\" + host + "\\" + share;

            Win32.NETRESOURCE nr = new Win32.NETRESOURCE();
            nr.dwType = Win32.RESOURCETYPE_DISK;
            nr.lpRemoteName = unc;

            int rc = Win32.WNetAddConnection2(ref nr, password ?? "", FormatUser(host, userName), 0);
            if (rc != 0)
            {
                error = DescribeError(rc, host);
                return null;
            }

            NetworkSession session = new NetworkSession(unc);
            session._connected = true;
            return session;
        }

        private static string FormatUser(string host, string userName)
        {
            if (string.IsNullOrWhiteSpace(userName))
            {
                return null;
            }
            // Если домен/ПК не указан — считаем, что это локальная учётка удалённого ПК.
            if (userName.IndexOf('\\') < 0 && userName.IndexOf('@') < 0)
            {
                return host + "\\" + userName;
            }
            return userName;
        }

        private static string DescribeError(int code, string host)
        {
            switch (code)
            {
                case 5: return "Доступ запрещён. Учётная запись не является администратором на " + host + ".";
                case 51: return "Сетевой запрос отклонён (возможна изоляция клиентов Wi-Fi).";
                case 53: return "Сетевой путь не найден — ПК " + host + " недоступен.";
                case 55: return "Сетевой ресурс недоступен.";
                case 64: return "Сетевое имя больше не доступно.";
                case 67: return "Сетевое имя не найдено — возможно, отключена шара C$.";
                case 85: return "Имя уже подключено с другими учётными данными.";
                case 1219: return "Конфликт учётных данных: к " + host + " уже есть подключение под другим пользователем.";
                case 1326: return "Неверный логин или пароль.";
                case 1327: return "Учётная запись ограничена (пустой пароль запрещён).";
                case 1329: return "Учётной записи запрещён вход.";
                case 1385: return "Этой учётной записи не разрешён вход на " + host + ".";
                case 1311: return "Сейчас нет доступных серверов входа.";
                case 1909: return "Учётная запись заблокирована.";
                default:
                    try
                    {
                        return "Ошибка подключения " + code + ": " + new Win32Exception(code).Message;
                    }
                    catch
                    {
                        return "Ошибка подключения " + code + ".";
                    }
            }
        }

        public void Dispose()
        {
            if (_connected)
            {
                try { Win32.WNetCancelConnection2(UncRoot, 0, true); }
                catch { /* игнорируем */ }
                _connected = false;
            }
        }
    }

    /// <summary>Вспомогательные сетевые проверки.</summary>
    public static class NetworkUtil
    {
        /// <summary>Проверяет, принимает ли хост TCP-соединения на порту 445 (SMB).</summary>
        public static bool IsSmbReachable(string host, int timeoutSeconds)
        {
            if (timeoutSeconds < 1)
            {
                timeoutSeconds = 1;
            }

            try
            {
                using (TcpClient client = new TcpClient())
                {
                    IAsyncResult ar = client.BeginConnect(host, 445, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(timeoutSeconds)))
                    {
                        return false;
                    }
                    client.EndConnect(ar);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
