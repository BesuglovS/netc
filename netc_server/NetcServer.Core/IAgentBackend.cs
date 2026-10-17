using NetcShared;

namespace NetcServer.Core;

/// <summary>Приёмник команд на ученическом ПК: захват экрана, ввод, трансляция, блокировка.
/// Реализуется либо напрямую в сессии (агент), либо через канал к службе.</summary>
public interface IAgentBackend
{
    bool Connected { get; }

    Task<byte[]?> CaptureAsync(int maxWidth, int quality, int timeoutMs, CancellationToken ct);

    Task SendInputAsync(InputEvent ev, CancellationToken ct);

    Task ShowBroadcastAsync(CancellationToken ct);

    Task BroadcastFrameAsync(byte[] jpeg, CancellationToken ct);

    Task HideBroadcastAsync(CancellationToken ct);

    Task LockAsync(byte[] payload, CancellationToken ct);

    Task UnlockAsync(CancellationToken ct);

    // ---- Этап 3: питание, сообщения, файлы ----

    /// <summary>Выполнить команду питания (выключение/перезагрузка/завершение сеанса).</summary>
    Task PowerAsync(PowerAction action, int delaySeconds, CancellationToken ct);

    /// <summary>Показать ученику сообщение; при необходимости — с возможностью ответа.</summary>
    Task ShowMessageAsync(ChatMessage msg, Func<ChatMessage, Task> reply, CancellationToken ct);

    /// <summary>Начать приём файла от учителя.</summary>
    Task BeginFileReceiveAsync(FileTransferInfo info, Func<FileResult, Task> notify, CancellationToken ct);

    /// <summary>Дописать полученный фрагмент файла.</summary>
    Task FileReceiveChunkAsync(byte[] data, CancellationToken ct);

    /// <summary>Завершить приём файла и уведомить учителя.</summary>
    Task EndFileReceiveAsync(CancellationToken ct);

    /// <summary>Отправить файл учителю (ученик → учитель). <paramref name="send"/> пишет кадр в канал.</summary>
    Task SendFileToClientAsync(string path, Func<MsgType, byte[], CancellationToken, Task> send, CancellationToken ct);

    // ---- Этап 4: ограничения, журнал, тесты ----

    /// <summary>Добавить запись в журнал агента.</summary>
    Task NoteAsync(string category, string message, CancellationToken ct);

    /// <summary>Применить политику ограничения приложений.</summary>
    Task ApplyAppPolicyAsync(AppPolicyMessage policy, CancellationToken ct);

    /// <summary>Применить политику ограничения интернета (полная блокировка или фильтр сайтов).</summary>
    Task SetInternetAsync(InternetPolicyMessage policy, CancellationToken ct);

    /// <summary>Вернуть журнал агента.</summary>
    Task<List<JournalEntry>> GetJournalAsync(CancellationToken ct);

    /// <summary>Показать ученику тест; <paramref name="submit"/> вызывается при завершении.</summary>
    Task ShowTestAsync(TestDefinition test, Func<TestResult, Task> submit, CancellationToken ct);

    /// <summary>Досрочно завершить активный тест; true — если тест был активен.</summary>
    Task<bool> StopTestAsync(string reason, CancellationToken ct);

    /// <summary>Применить настройки персонализации (обои, лок-скрин, тема, акцент).</summary>
    Task<PersonalizationResult> ApplyPersonalizationAsync(PersonalizationMessage msg, CancellationToken ct);

    /// <summary>Проверить (и при необходимости вычистить) автозагрузку ученика.</summary>
    Task<AutostartReport> ScanAutostartAsync(bool quarantine, CancellationToken ct);

    /// <summary>Закрыть все пользовательские окна и приложения (при необходимости — принудительно).</summary>
    Task<CloseAppsResult> CloseAppsAsync(CloseAppsMessage req, CancellationToken ct);
}
