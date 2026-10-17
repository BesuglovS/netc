using System;
using System.Collections.Generic;
using System.IO;
using ControlWorksCollector.Native;

namespace ControlWorksCollector.Services
{
    /// <summary>
    /// Собирает папки с рабочих столов ученических ПК, созданные в выбранную дату.
    /// ПК ищется сначала по имени компьютера (устойчиво к смене IP), затем по IP.
    /// Доступ — через SMB-шару C$ под локальным администратором.
    /// </summary>
    public sealed class DesktopCollector
    {
        private readonly AppConfig _cfg;
        private readonly Action<string> _log;

        private static readonly string[] ExcludedProfiles =
        {
            "Public", "Default", "Default User", "All Users",
            "DefaultAppPool", "WDAGUtilityAccount"
        };

        private sealed class DesktopInfo
        {
            public string Path;
            public string Owner;
        }

        public DesktopCollector(AppConfig cfg, Action<string> log)
        {
            _cfg = cfg;
            _log = log ?? delegate { };
        }

        public CollectResult Collect(PcTarget pc, DateTime date)
        {
            CollectResult result = new CollectResult();
            result.Pc = pc.Name;
            result.StartedAt = DateTime.Now;
            result.Status = PcStatus.Running;

            DateTime day = date.Date;
            int probeTimeout = Math.Max(2, _cfg.ConnectTimeoutSeconds / 3);

            List<string> candidates = BuildCandidates(pc);
            if (candidates.Count == 0)
            {
                result.Status = PcStatus.Error;
                result.Message = "Не заданы ни имя ПК, ни IP.";
                result.FinishedAt = DateTime.Now;
                return result;
            }

            // Выбираем доступный адрес: сначала имя ПК, затем IP.
            string chosen = null;
            foreach (string cand in candidates)
            {
                if (NetworkUtil.IsSmbReachable(cand, probeTimeout))
                {
                    chosen = cand;
                    break;
                }
            }

            if (chosen == null)
            {
                result.Status = PcStatus.Offline;
                result.Message = "ПК недоступен (нет ответа на порт 445) по " + string.Join(", ", candidates.ToArray()) + ".";
                result.FinishedAt = DateTime.Now;
                return result;
            }

            string resolvedIp = ResolveIp(chosen);
            result.ResolvedIp = resolvedIp;

            NetworkSession session = null;
            try
            {
                string error;
                session = NetworkSession.Connect(chosen, _cfg.UserName, _cfg.Password, out error);
                if (session == null)
                {
                    result.Status = PcStatus.Error;
                    result.Message = error;
                    result.FinishedAt = DateTime.Now;
                    return result;
                }

                // Обогащаем запись: актуальный IP и (если неизвестно) имя ПК.
                if (pc.Record != null)
                {
                    if (!string.IsNullOrWhiteSpace(resolvedIp))
                    {
                        pc.Record.Ip = resolvedIp;
                    }
                    if (string.IsNullOrWhiteSpace(pc.Record.Hostname))
                    {
                        string hn = Win32.GetRemoteComputerName(chosen);
                        if (!string.IsNullOrWhiteSpace(hn))
                        {
                            pc.Record.Hostname = hn;
                            result.Hostname = hn;
                        }
                    }
                }

                string usersRoot = session.UncRoot + @"\Users";
                if (!Directory.Exists(usersRoot))
                {
                    result.Status = PcStatus.Error;
                    result.Message = "Не найден каталог Users на удалённом ПК.";
                    result.FinishedAt = DateTime.Now;
                    return result;
                }

                string folderTag = string.IsNullOrWhiteSpace(resolvedIp) ? chosen : resolvedIp;

                List<DesktopInfo> desktops = CollectDesktopLocations(usersRoot);
                foreach (DesktopInfo di in desktops)
                {
                    CollectFromDesktop(di.Path, di.Owner, chosen, folderTag, day, result);
                }

                result.Status = PcStatus.Ok;
                result.Message = result.FoldersFound == 0
                    ? "Папок за " + day.ToString("dd.MM.yyyy") + " не найдено."
                    : "Скопировано папок: " + result.FoldersCopied + " из " + result.FoldersFound + ".";
            }
            catch (Exception ex)
            {
                result.Status = PcStatus.Error;
                result.Message = ex.Message;
                _log(pc.Name + ": ошибка — " + ex.Message);
            }
            finally
            {
                if (session != null)
                {
                    session.Dispose();
                }
                result.FinishedAt = DateTime.Now;
            }

            return result;
        }

        private static List<string> BuildCandidates(PcTarget pc)
        {
            List<string> list = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(pc.Ip) && seen.Add(pc.Ip.Trim()))
            {
                list.Add(pc.Ip.Trim());
            }
            if (!string.IsNullOrWhiteSpace(pc.Hostname) && seen.Add(pc.Hostname.Trim()))
            {
                list.Add(pc.Hostname.Trim());
            }
            return list;
        }

        private static string ResolveIp(string host)
        {
            if (string.IsNullOrWhiteSpace(host))
            {
                return "";
            }

            System.Net.IPAddress parsed;
            if (System.Net.IPAddress.TryParse(host.Trim(), out parsed))
            {
                return parsed.ToString();
            }

            try
            {
                System.Net.IPAddress[] addrs = System.Net.Dns.GetHostAddresses(host);
                foreach (System.Net.IPAddress a in addrs)
                {
                    if (a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        return a.ToString();
                    }
                }
            }
            catch
            {
                // разрешение имени недоступно — используем исходную строку
            }

            return host;
        }

        private List<DesktopInfo> CollectDesktopLocations(string usersRoot)
        {
            List<DesktopInfo> list = new List<DesktopInfo>();

            foreach (string profile in SafeGetDirectories(usersRoot))
            {
                string profName = Path.GetFileName(profile);
                if (IsExcludedProfile(profName))
                {
                    continue;
                }

                AddDesktop(list, Path.Combine(profile, "Desktop"), profName);
            }

            if (_cfg.IncludePublicDesktop)
            {
                AddDesktop(list, Path.Combine(usersRoot, "Public", "Desktop"), "Public");
            }

            return list;
        }

        private void CollectFromDesktop(string desktopPath, string owner, string logHost, string folderTag, DateTime day, CollectResult result)
        {
            string baseDest = Path.Combine(_cfg.DestinationRoot, day.ToString("yyyy-MM-dd"));

            foreach (string folder in SafeGetDirectories(desktopPath))
            {
                DateTime created;
                try { created = Directory.GetCreationTime(folder); }
                catch { continue; }

                if (created.Date != day.Date)
                {
                    continue;
                }

                result.FoldersFound++;

                string name = Path.GetFileName(folder);
                string folderName = name + " (" + folderTag + ")";
                string dest = MakeUnique(Path.Combine(baseDest, folderName), owner);

                try
                {
                    Directory.CreateDirectory(dest);
                    int files = CopyTree(folder, dest);
                    result.FoldersCopied++;
                    result.FilesCopied += files;
                    _log(logHost + ": «" + folderName + "» (" + owner + "), файлов: " + files + ".");
                }
                catch (Exception ex)
                {
                    _log(logHost + ": не удалось скопировать «" + name + "»: " + ex.Message);
                }
            }
        }

        private static string MakeUnique(string path, string owner)
        {
            if (!Directory.Exists(path) && !File.Exists(path))
            {
                return path;
            }
            string candidate = path + " (" + owner + ")";
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
            {
                return candidate;
            }
            int i = 2;
            while (Directory.Exists(candidate + " " + i) || File.Exists(candidate + " " + i))
            {
                i++;
            }
            return candidate + " " + i;
        }

        private int CopyTree(string source, string dest)
        {
            int count = 0;
            Directory.CreateDirectory(dest);

            foreach (string file in SafeGetFiles(source))
            {
                string target = Path.Combine(dest, Path.GetFileName(file));
                try
                {
                    File.Copy(file, target, true);
                    count++;
                }
                catch (Exception ex)
                {
                    _log("Не скопирован файл " + file + ": " + ex.Message);
                }
            }

            foreach (string dir in SafeGetDirectories(source))
            {
                count += CopyTree(dir, Path.Combine(dest, Path.GetFileName(dir)));
            }

            return count;
        }

        private static bool IsExcludedProfile(string name)
        {
            foreach (string ex in ExcludedProfiles)
            {
                if (string.Equals(ex, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static void AddDesktop(List<DesktopInfo> list, string path, string owner)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    list.Add(new DesktopInfo { Path = path, Owner = owner });
                }
            }
            catch
            {
                // пропускаем недоступные
            }
        }

        private static string[] SafeGetDirectories(string path)
        {
            try { return Directory.GetDirectories(path); }
            catch { return new string[0]; }
        }

        private static string[] SafeGetFiles(string path)
        {
            try { return Directory.GetFiles(path); }
            catch { return new string[0]; }
        }
    }
}
