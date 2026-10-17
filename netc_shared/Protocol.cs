using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetcShared;

/// <summary>Общие константы протокола комплекса netc.</summary>
public static class NetcProtocol
{
    public const int ControlPort = 7801;
    public const int DiscoveryPort = 7802;
    public const int ScreenPort = 7803;

    /// <summary>Порт локального HTTP-прокси фильтрации сайтов (только 127.0.0.1, слушает агент).</summary>
    public const int SiteFilterPort = 7804;

    public const int Version = 1;

    /// <summary>Текст UDP-запроса обнаружения.</summary>
    public const string DiscoveryRequest = "NETC?1";

    /// <summary>Префикс UDP-ответа обнаружения.</summary>
    public const string DiscoveryReplyPrefix = "NETC!";

    /// <summary>Короткий токен из общего ключа для UDP-обнаружения.
    /// Ответчик реагирует только на запрос «NETC?1 &lt;токен&gt;», клиент принимает ответ
    /// «NETC!&lt;токен&gt; {json}» — так обнаружение не отвечает посторонним в сети.</summary>
    public static string DiscoveryToken(string sharedKey)
    {
        byte[] key = NetcCrypto.DeriveKey(sharedKey);
        return Convert.ToHexString(key.AsSpan(0, 4));
    }

    /// <summary>Удлинённый токен обнаружения (8 байт). Новые агенты принимают и его, и старый
    /// (4 байта) — чтобы обнаружение работало в смешанном классе во время обновления.</summary>
    public static string DiscoveryTokenLong(string sharedKey)
    {
        byte[] key = NetcCrypto.DeriveKey(sharedKey);
        return Convert.ToHexString(key.AsSpan(0, 8));
    }

    /// <summary>Имя именованного канала служба ↔ трей-агент.</summary>
    public const string PipeName = "netc_agent";
}

/// <summary>Типы сообщений канала управления.</summary>
public enum MsgType : byte
{
    Hello = 1,
    HelloAck = 2,
    InfoRequest = 10,
    InfoResponse = 11,
    ScreenStart = 20,
    ScreenStop = 21,
    ScreenFrame = 22,
    InputEvent = 30,
    BroadcastStart = 40,
    BroadcastFrame = 41,
    BroadcastStop = 42,
    LockScreen = 50,
    UnlockScreen = 51,
    PowerCommand = 60,
    PowerAck = 61,
    MessageSend = 70,
    MessageReply = 71,
    FileStart = 80,
    FileChunk = 81,
    FileEnd = 82,
    FileResult = 83,
    FileRequest = 84,
    AppPolicy = 90,
    AppPolicyAck = 91,
    InternetPolicy = 92,
    InternetAck = 93,
    JournalRequest = 94,
    JournalResponse = 95,
    TestStart = 100,
    TestResult = 101,
    TestStop = 102,
    TestStatus = 103,
    TestAck = 104,
    Personalization = 105,
    PersonalizationAck = 106,
    AutostartScan = 107,
    AutostartReport = 108,
    CloseApps = 109,
    CloseAppsAck = 110,
    MonitorPower = 111,
    Error = 255
}

/// <summary>Действие питания на ученическом ПК.</summary>
public enum PowerAction : byte
{
    Shutdown = 0,
    Restart = 1,
    Logoff = 2
}

/// <summary>Команда питания (учитель → агент).</summary>
public sealed class PowerCommandMessage
{
    public PowerAction Action { get; set; } = PowerAction.Shutdown;

    /// <summary>Задержка перед выполнением, секунды (для выключения/перезагрузки).</summary>
    public int DelaySeconds { get; set; } = 5;
}

/// <summary>Текстовое сообщение (учитель → ученик) или ответ ученика.</summary>
public sealed class ChatMessage
{
    public string From { get; set; } = "";
    public string Text { get; set; } = "";
    public bool ExpectReply { get; set; }
}

/// <summary>Заголовок передачи файла (первый кадр потока файла).</summary>
public sealed class FileTransferInfo
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
}

/// <summary>Результат приёма/передачи файла.</summary>
public sealed class FileResult
{
    public bool Ok { get; set; }
    public string Message { get; set; } = "";
    public string Path { get; set; } = "";
}

/// <summary>Запрос файла с ученического ПК (учитель → агент).</summary>
public sealed class FileRequest
{
    public string Path { get; set; } = "";
}

// ---- Этап 4: ограничения, журнал, тесты ----

/// <summary>Режим ограничения приложений.</summary>
public enum AppControlMode : byte
{
    Off = 0,
    Blacklist = 1,
    Whitelist = 2
}

/// <summary>Политика ограничения приложений (по именам процессов, без .exe).</summary>
public sealed class AppPolicyMessage
{
    public AppControlMode Mode { get; set; } = AppControlMode.Off;
    public List<string> Names { get; set; } = new();
}

/// <summary>Режим фильтрации сайтов по домену (через локальный прокси агента).</summary>
public enum SiteFilterMode : byte
{
    /// <summary>Фильтрация выключена.</summary>
    Off = 0,

    /// <summary>Чёрный список: запрещены перечисленные домены (и их поддомены).</summary>
    Blacklist = 1,

    /// <summary>Белый список: разрешены только перечисленные домены (локальная сеть — всегда).</summary>
    Whitelist = 2
}

/// <summary>Политика ограничения интернета (через прокси текущего пользователя).</summary>
public sealed class InternetPolicyMessage
{
    public bool Blocked { get; set; }
    public bool KillBrowsers { get; set; } = true;

    /// <summary>Режим фильтрации сайтов: Off — выключен, иначе работает локальный прокси-фильтр.</summary>
    public SiteFilterMode SiteMode { get; set; } = SiteFilterMode.Off;

    /// <summary>Домены фильтра (для Blacklist/Whitelist). Поддомены матчатся автоматически.</summary>
    public List<string> Sites { get; set; } = new();
}

/// <summary>Запись журнала агента.</summary>
public sealed class JournalEntry
{
    public DateTime TimeUtc { get; set; } = DateTime.UtcNow;
    public string Category { get; set; } = "";
    public string Message { get; set; } = "";
}

/// <summary>Ответ с журналом агента.</summary>
public sealed class JournalResponse
{
    public List<JournalEntry> Entries { get; set; } = new();
}

/// <summary>Тип вопроса теста.</summary>
public enum QuestionKind : byte
{
    /// <summary>Один правильный вариант из списка.</summary>
    Single = 0,

    /// <summary>Несколько правильных вариантов (флажки).</summary>
    Multi = 1,

    /// <summary>Верно / Неверно.</summary>
    TrueFalse = 2,

    /// <summary>Короткий текстовый ответ.</summary>
    ShortText = 3,

    /// <summary>Числовой ответ (опционально с допуском).</summary>
    Numeric = 4,

    /// <summary>Развёрнутый ответ — проверяет учитель вручную.</summary>
    Essay = 5,

    /// <summary>Сопоставление пар (левый список ↔ правый).</summary>
    Matching = 6
}

/// <summary>Один вопрос теста. Поля используются в зависимости от <see cref="Kind"/>.</summary>
public sealed class TestQuestion
{
    public string Text { get; set; } = "";

    public QuestionKind Kind { get; set; } = QuestionKind.Single;

    /// <summary>Варианты ответа для Single/Multi/TrueFalse.</summary>
    public List<string> Options { get; set; } = new();

    /// <summary>Индекс верного варианта (0-based) для Single/TrueFalse.</summary>
    public int CorrectIndex { get; set; }

    /// <summary>Индексы верных вариантов (0-based) для Multi.</summary>
    public List<int> CorrectIndices { get; set; } = new();

    /// <summary>Допустимые текстовые ответы для ShortText.</summary>
    public List<string> CorrectText { get; set; } = new();

    /// <summary>Сравнивать текстовый ответ без учёта регистра.</summary>
    public bool IgnoreCase { get; set; } = true;

    /// <summary>Верное число для Numeric.</summary>
    public double CorrectNumber { get; set; }

    /// <summary>Допуск (±) для Numeric; 0 — точное совпадение.</summary>
    public double Tolerance { get; set; }

    /// <summary>Левый список для Matching.</summary>
    public List<string> MatchLeft { get; set; } = new();

    /// <summary>Правый список для Matching; пара — по одинаковому индексу.</summary>
    public List<string> MatchRight { get; set; } = new();

    /// <summary>Вес вопроса в баллах.</summary>
    public double Points { get; set; } = 1;

    /// <summary>Начислять частичный балл (Multi/Matching).</summary>
    public bool PartialCredit { get; set; }

    /// <summary>Пояснение, показывается ученику после сдачи (может быть пустым).</summary>
    public string Explanation { get; set; } = "";

    /// <summary>Необязательная картинка к вопросу (путь на ученическом ПК).</summary>
    public string? ImagePath { get; set; }
}

/// <summary>Тестовое задание (учитель → агент).</summary>
public sealed class TestDefinition
{
    /// <summary>Идентификатор задания (для сопоставления результатов и черновиков).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Title { get; set; } = "Тест";

    public List<TestQuestion> Questions { get; set; } = new();

    /// <summary>Ограничение времени на весь тест в секундах; 0 — без ограничения.</summary>
    public int DurationSeconds { get; set; }

    /// <summary>Перемешивать вопросы.</summary>
    public bool ShuffleQuestions { get; set; }

    /// <summary>Перемешивать варианты ответа.</summary>
    public bool ShuffleOptions { get; set; }

    /// <summary>Показывать ученику результат сразу после сдачи.</summary>
    public bool ShowResultToStudent { get; set; } = true;

    /// <summary>Проходной процент (0 — не задан).</summary>
    public double PassPercent { get; set; }

    /// <summary>PIN для аварийного выхода на месте (пусто — выхода нет).</summary>
    public string EscapePin { get; set; } = "";
}

/// <summary>Ответ ученика на один вопрос. Индексы — в исходном (не перемешанном) порядке.</summary>
public sealed class TestAnswer
{
    public int QuestionIndex { get; set; }

    /// <summary>Выбранные варианты (Single/Multi/TrueFalse).</summary>
    public List<int> SelectedIndices { get; set; } = new();

    /// <summary>Текстовый ответ (ShortText/Essay).</summary>
    public string TextAnswer { get; set; } = "";

    /// <summary>Числовой ответ (Numeric).</summary>
    public double NumberAnswer { get; set; }

    /// <summary>Задан ли числовой ответ.</summary>
    public bool NumberSet { get; set; }

    /// <summary>Выбор для каждой пары Matching: MatchPicks[i] — индекс правого элемента для левого i.</summary>
    public List<int> MatchPicks { get; set; } = new();

    /// <summary>Время, потраченное на вопрос, мс.</summary>
    public long TimeSpentMs { get; set; }

    /// <summary>Верно ли (с учётом автопроверки).</summary>
    public bool Correct { get; set; }

    /// <summary>Требует ручной проверки (эссе).</summary>
    public bool Manual { get; set; }

    /// <summary>Начислено баллов.</summary>
    public double Awarded { get; set; }
}

/// <summary>Результат прохождения теста (агент → учитель).</summary>
public sealed class TestResult
{
    public string TestId { get; set; } = "";
    public string Student { get; set; } = "";
    public string Title { get; set; } = "";

    /// <summary>Набрано баллов (учитывая автоматически проверяемые вопросы).</summary>
    public double Score { get; set; }

    /// <summary>Максимум баллов по автоматически проверяемым вопросам.</summary>
    public double MaxScore { get; set; }

    /// <summary>Баллы, ожидающие ручной проверки (эссе).</summary>
    public double PendingManual { get; set; }

    /// <summary>Число вопросов, ожидающих ручной проверки.</summary>
    public int ManualCount { get; set; }

    /// <summary>Сдан ли тест полностью (иначе снят/по таймауту).</summary>
    public bool Completed { get; set; }

    /// <summary>Причина завершения: submit | timeout | teacher | escape | replaced.</summary>
    public string EndReason { get; set; } = "";

    /// <summary>Длительность прохождения, мс.</summary>
    public long DurationMs { get; set; }

    /// <summary>Пройден ли порог <see cref="TestDefinition.PassPercent"/>.</summary>
    public bool Passed { get; set; }

    public List<TestAnswer> Answers { get; set; } = new();

    /// <summary>Процент по автоматически проверяемым вопросам.</summary>
    public double Percent => MaxScore <= 0 ? 0 : Math.Round(Score / MaxScore * 100, 1);
}

/// <summary>Команда учителя: досрочно завершить тест и забрать текущие ответы.</summary>
public sealed class TestStopMessage
{
    public string Reason { get; set; } = "teacher";
}

/// <summary>Прогресс прохождения теста (агент → учитель).</summary>
public sealed class TestStatusMessage
{
    public string TestId { get; set; } = "";
    public int Answered { get; set; }
    public int Total { get; set; }
}

/// <summary>Настройки персонализации (учитель → агент). Пути — уже переданные на ПК файлы.</summary>
public sealed class PersonalizationMessage
{
    /// <summary>Путь к картинке обоев на ученическом ПК (из результата передачи файла).</summary>
    public string? WallpaperPath { get; set; }

    /// <summary>Режим обоев: fill | fit | stretch | center | tile.</summary>
    public string WallpaperStyle { get; set; } = "fill";

    /// <summary>Путь к картинке экрана блокировки на ученическом ПК (если есть права — машинно).</summary>
    public string? LockScreenPath { get; set; }

    /// <summary>Тема: "dark" | "light" | null (не менять).</summary>
    public string? Theme { get; set; }

    /// <summary>Акцентный цвет RGB (0 — не менять).</summary>
    public int AccentColor { get; set; }

    /// <summary>Показывать акцент в панели задач и заголовках.</summary>
    public bool AccentOnStart { get; set; }

    /// <summary>Сбросить персонализацию к стандартным настройкам: светлая тема, обои/лок-скрин
    /// по умолчанию, акцент по умолчанию. Если true — остальные поля игнорируются.</summary>
    public bool Reset { get; set; }
}

/// <summary>Результат применения персонализации.</summary>
public sealed class PersonalizationResult
{
    public bool Ok { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>Запрос проверки автозагрузки ученика.</summary>
public sealed class AutostartScanRequest
{
    /// <summary>Удалять/складывать в карантин найденное (иначе только отчёт).</summary>
    public bool Quarantine { get; set; } = true;
}

/// <summary>Найденный элемент автозагрузки.</summary>
public sealed class AutostartFinding
{
    public string Location { get; set; } = "";
    public string Name { get; set; } = "";
    public string Detail { get; set; } = "";
    public bool Removed { get; set; }
}

/// <summary>Отчёт о проверке автозагрузки.</summary>
public sealed class AutostartReport
{
    public List<AutostartFinding> Findings { get; set; } = new();
    public int Removed { get; set; }
}

/// <summary>Команда «закрыть все окна и приложения» на ученическом ПК (учитель → агент).</summary>
public sealed class CloseAppsMessage
{
    /// <summary>Закрывать также окна Проводника (Explorer). Рабочий стол и панель задач не затрагиваются.</summary>
    public bool IncludeExplorer { get; set; } = true;

    /// <summary>Принудительно завершать процессы, чьи окна не закрылись за <see cref="GraceMs"/>.</summary>
    public bool Force { get; set; }

    /// <summary>Ожидание добровольного закрытия перед принудительным завершением, мс.</summary>
    public int GraceMs { get; set; } = 2500;
}

/// <summary>Результат закрытия окон.</summary>
public sealed class CloseAppsResult
{
    /// <summary>Сколько окон получили команду закрытия.</summary>
    public int Windows { get; set; }

    /// <summary>Сколько процессов завершено принудительно.</summary>
    public int Forced { get; set; }

    public string Message { get; set; } = "";
}

/// <summary>Команда управления мониторами ученика: погасить/включить подсветку (без выключения ПК).</summary>
public sealed class MonitorPowerMessage
{
    /// <summary>true — выключить мониторы (энергосбережение), false — включить.</summary>
    public bool Off { get; set; } = true;
}

/// <summary>Сведения об ученическом ПК.</summary>
public sealed class PcInfo
{
    public string MachineName { get; set; } = "";
    public string UserName { get; set; } = "";
    public string OsVersion { get; set; } = "";
    public string AgentVersion { get; set; } = "";
    public string MacAddress { get; set; } = "";
    /// <summary>Производитель ПК (BIOS SystemManufacturer).</summary>
    public string Manufacturer { get; set; } = "";
    /// <summary>Модель ПК (BIOS SystemProductName).</summary>
    public string Model { get; set; } = "";
    public List<string> Addresses { get; set; } = new();
}

public sealed class HelloMessage
{
    public int Version { get; set; }
    public string ClientName { get; set; } = "";

    /// <summary>Назначение подключения: "query" — разовый опрос сведений, "" — управляющая сессия.
    /// Разовые опросы не считаются «сессией учителя» и не засоряют журнал агента.</summary>
    public string Purpose { get; set; } = "";
}

public sealed class HelloAck
{
    public int Version { get; set; }
    public bool Ok { get; set; }
    public string MachineName { get; set; } = "";
    public string Error { get; set; } = "";
}

/// <summary>Настройки приложения (общий ключ и порты).</summary>
public sealed class NetcSettings
{
    public string SharedKey { get; set; } = "netc-default-key";
    public int ControlPort { get; set; } = NetcProtocol.ControlPort;
    public int DiscoveryPort { get; set; } = NetcProtocol.DiscoveryPort;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string DefaultPath => Path.Combine(AppContext.BaseDirectory, "netc.json");

    public static NetcSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
            {
                var s = JsonSerializer.Deserialize<NetcSettings>(File.ReadAllText(path), JsonOpts);
                if (s != null) return s;
            }
        }
        catch { /* используем значения по умолчанию */ }
        return new NetcSettings();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        try { File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOpts)); }
        catch { }
    }
}

/// <summary>Помощники JSON для сообщений протокола.</summary>
public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static byte[] ToBytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);

    public static T? FromBytes<T>(byte[] data) => JsonSerializer.Deserialize<T>(data, Options);
}
