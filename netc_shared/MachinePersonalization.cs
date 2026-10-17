using System.Text.Json;

namespace NetcShared;

/// <summary>
/// Обмен «агент → служба-сторож» для машинных настроек персонализации.
/// Экран блокировки задаётся машинным ключом <c>HKLM\...\PersonalizationCSP</c>, для записи
/// в который нужны права администратора. Агент работает в сессии ученика (обычный пользователь),
/// поэтому запрос выполняет служба-сторож от SYSTEM.
///
/// Транспорт — общая папка <see cref="ExchangeDirectory"/>: агент кладёт запрос, сторож отвечает.
/// </summary>
public static class MachinePersonalization
{
    /// <summary>Общая папка обмена (доступна и ученику, и SYSTEM): <c>C:\ProgramData\netc\personalize</c>.</summary>
    public static string ExchangeDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "netc", "personalize");

    /// <summary>Префикс файла запроса: <c>req-&lt;id&gt;.json</c>.</summary>
    public const string RequestPrefix = "req-";

    /// <summary>Префикс файла ответа: <c>res-&lt;id&gt;.json</c>.</summary>
    public const string ResponsePrefix = "res-";

    /// <summary>Запрос агента к сторожу.</summary>
    public sealed class Request
    {
        /// <summary><c>set</c> — задать лок-скрин, <c>reset</c> — вернуть стандартный,
        /// <c>restart-agent</c> — перезапустить агента (активация новой сборки без перезагрузки),
        /// <c>kill-pids</c> — завершить процессы ученической сессии от SYSTEM,
        /// <c>quarantine-file</c> — перенести скрипт из общей «Автозагрузки» в карантин от SYSTEM.</summary>
        public string Action { get; set; } = "set";

        /// <summary>Путь к картинке на ученическом ПК (для <c>set</c>).</summary>
        public string? Path { get; set; }

        /// <summary>Для действия <c>kill-pids</c>: идентификаторы процессов для завершения от SYSTEM.</summary>
        public List<int> Pids { get; set; } = new();
    }

    /// <summary>Ответ сторожа агенту.</summary>
    public sealed class Response
    {
        public bool Ok { get; set; }
        public string Message { get; set; } = "";

        /// <summary>Числовой результат (например, сколько процессов завершено).</summary>
        public int Count { get; set; }
    }

    /// <summary>Атомарная запись запроса: во временный файл, затем переименование.</summary>
    public static void WriteRequest(string path, Request request)
    {
        string tmp = path + ".tmp";
        File.WriteAllBytes(tmp, Json.ToBytes(request));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Чтение ответа сторожа.</summary>
    public static Response? ReadResponse(string path)
        => JsonSerializer.Deserialize<Response>(File.ReadAllBytes(path), Json.Options);
}
