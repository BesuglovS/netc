using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace ControlWorksCollector.Services
{
    /// <summary>Загрузка/сохранение config.json в %AppData%\ControlWorksCollector.</summary>
    public static class ConfigStore
    {
        public static string ConfigDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ControlWorksCollector");
            }
        }

        public static string ConfigFile
        {
            get { return Path.Combine(ConfigDirectory, "config.json"); }
        }

        public static string DefaultDestination
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Сборы");
            }
        }

        public static AppConfig Load()
        {
            AppConfig cfg = null;
            try
            {
                if (File.Exists(ConfigFile))
                {
                    string json = File.ReadAllText(ConfigFile);
                    JavaScriptSerializer serializer = new JavaScriptSerializer();
                    serializer.MaxJsonLength = int.MaxValue;
                    cfg = serializer.Deserialize<AppConfig>(json);
                }
            }
            catch
            {
                cfg = null;
            }

            if (cfg == null)
            {
                cfg = new AppConfig();
            }

            if (string.IsNullOrWhiteSpace(cfg.DestinationRoot))
            {
                cfg.DestinationRoot = DefaultDestination;
            }
            if (cfg.ConnectTimeoutSeconds <= 0)
            {
                cfg.ConnectTimeoutSeconds = 15;
            }
            if (cfg.MaxParallel <= 0)
            {
                cfg.MaxParallel = 4;
            }
            if (cfg.Computers == null)
            {
                cfg.Computers = new List<string>();
            }
            if (cfg.PcRecords == null)
            {
                cfg.PcRecords = new List<PcRecord>();
            }

            // Если файл списка не задан, но рядом с exe есть ips.txt — учитываем его при миграции.
            if (string.IsNullOrWhiteSpace(cfg.ComputerListFile))
            {
                string def = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ips.txt");
                if (File.Exists(def))
                {
                    cfg.ComputerListFile = def;
                }
            }

            // Разовая миграция старых списков (Computers / ips.txt) в инвентарь PcRecords.
            if (!cfg.LegacyImported)
            {
                bool added = false;
                if (cfg.PcRecords.Count == 0)
                {
                    foreach (string c in cfg.Computers)
                    {
                        added |= AddIpRecord(cfg, c);
                    }
                    foreach (string line in LoadComputerList(cfg.ComputerListFile))
                    {
                        added |= AddIpRecord(cfg, line);
                    }
                }
                cfg.LegacyImported = true;
                if (added)
                {
                    try { Save(cfg); }
                    catch { /* не критично */ }
                }
            }

            // Расшифровываем пароль для использования в рантайме.
            if (!string.IsNullOrEmpty(cfg.EncryptedPassword))
            {
                try { cfg.Password = Native.Win32.Unprotect(cfg.EncryptedPassword); }
                catch { cfg.Password = ""; }
            }

            return cfg;
        }

        private static bool AddIpRecord(AppConfig cfg, string value)
        {
            string v = (value ?? "").Trim();
            if (v.Length == 0 || v.StartsWith("#") || v.StartsWith("//"))
            {
                return false;
            }
            foreach (PcRecord r in cfg.PcRecords)
            {
                if (string.Equals(r.Ip, v, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            cfg.PcRecords.Add(new PcRecord { Ip = v });
            return true;
        }

        /// <summary>Сохраняет конфигурацию, изменяя пароль (шифрует или очищает).</summary>
        public static void Save(AppConfig cfg, bool savePassword)
        {
            if (savePassword)
            {
                cfg.EncryptedPassword = Native.Win32.Protect(cfg.Password ?? "");
            }
            else
            {
                cfg.EncryptedPassword = "";
            }
            WriteJson(cfg);
        }

        /// <summary>Сохраняет конфигурацию, не трогая уже сохранённый пароль.</summary>
        public static void Save(AppConfig cfg)
        {
            WriteJson(cfg);
        }

        private static void WriteJson(AppConfig cfg)
        {
            Directory.CreateDirectory(ConfigDirectory);
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = int.MaxValue;
            string json = serializer.Serialize(cfg);
            File.WriteAllText(ConfigFile, json);
        }

        /// <summary>Читает список адресов из текстового файла (по одному в строке; # и // — комментарии).
        /// Поддерживается и расширенный формат «адрес|имя|модель|mac» — берётся только адрес.</summary>
        public static List<string> LoadComputerList(string path)
        {
            List<string> list = new List<string>();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return list;
            }
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//"))
                {
                    continue;
                }
                string addr = line.Split('|')[0].Trim();
                if (addr.Length > 0)
                {
                    list.Add(addr);
                }
            }
            return list;
        }
    }
}
