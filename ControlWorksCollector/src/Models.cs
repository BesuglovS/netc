using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace ControlWorksCollector
{
    /// <summary>Запись об ученическом ПК: устойчивые идентификаторы + последний адрес.</summary>
    public sealed class PcRecord
    {
        public string Label { get; set; }     // отображаемое название (напр. «ПК 1, каб. 12»)
        public string Hostname { get; set; }  // имя компьютера (стабильный идентификатор)
        public string Mac { get; set; }       // MAC-адрес (справочно)
        public string Ip { get; set; }        // последний известный IP/адрес
        public string Note { get; set; }      // примечание

        public PcRecord()
        {
            Label = "";
            Hostname = "";
            Mac = "";
            Ip = "";
            Note = "";
        }

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Label)) return Label.Trim();
                if (!string.IsNullOrWhiteSpace(Hostname)) return Hostname.Trim();
                if (!string.IsNullOrWhiteSpace(Ip)) return Ip.Trim();
                return "(без имени)";
            }
        }

        public PcRecord Clone()
        {
            return new PcRecord
            {
                Label = Label,
                Hostname = Hostname,
                Mac = Mac,
                Ip = Ip,
                Note = Note
            };
        }
    }

    /// <summary>Цель сбора: адрес для подключения + имя для отображения.</summary>
    public sealed class PcTarget
    {
        public string Name { get; set; }
        public string Hostname { get; set; }
        public string Ip { get; set; }
        public string Mac { get; set; }

        /// <summary>Исходная запись (для обновления «последнего IP» и имени).</summary>
        [ScriptIgnore]
        public PcRecord Record { get; set; }

        public PcTarget()
        {
            Name = "";
            Hostname = "";
            Ip = "";
            Mac = "";
        }

        public static PcTarget FromRecord(PcRecord r)
        {
            PcTarget t = new PcTarget();
            t.Name = r.DisplayName;
            t.Hostname = (r.Hostname ?? "").Trim();
            t.Ip = (r.Ip ?? "").Trim();
            t.Mac = (r.Mac ?? "").Trim();
            t.Record = r;
            return t;
        }
    }

    /// <summary>Конфигурация приложения (сериализуется в config.json).</summary>
    public sealed class AppConfig
    {
        public string UserName { get; set; }
        public string EncryptedPassword { get; set; }
        public string DestinationRoot { get; set; }
        public int ConnectTimeoutSeconds { get; set; }
        public int MaxParallel { get; set; }
        public bool IncludePublicDesktop { get; set; }

        /// <summary>Инвентарь ПК.</summary>
        public List<PcRecord> PcRecords { get; set; }

        // --- устаревшие поля (только для разовой миграции из старых конфигов) ---
        public List<string> Computers { get; set; }
        public string ComputerListFile { get; set; }
        public bool LegacyImported { get; set; }

        /// <summary>Пароль в открытом виде. Только для рантайма, не сериализуется.</summary>
        [ScriptIgnore]
        public string Password { get; set; }

        public AppConfig()
        {
            UserName = "";
            EncryptedPassword = "";
            DestinationRoot = "";
            ConnectTimeoutSeconds = 15;
            MaxParallel = 4;
            IncludePublicDesktop = true;
            PcRecords = new List<PcRecord>();
            Computers = new List<string>();
            ComputerListFile = "";
            LegacyImported = false;
            Password = "";
        }
    }

    public enum PcStatus
    {
        Pending,
        Running,
        Offline,
        Ok,
        Error,
        Cancelled
    }

    /// <summary>Результат сбора по одному компьютеру.</summary>
    public sealed class CollectResult
    {
        public string Pc { get; set; }
        public PcStatus Status { get; set; }
        public int FoldersFound { get; set; }
        public int FoldersCopied { get; set; }
        public int FilesCopied { get; set; }
        public string Message { get; set; }
        public string ResolvedIp { get; set; }
        public string Hostname { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime FinishedAt { get; set; }

        public CollectResult()
        {
            Pc = "";
            Message = "";
            ResolvedIp = "";
            Hostname = "";
        }
    }
}
