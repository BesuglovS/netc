using NetcServer.Core;
using NetcShared;

namespace NetcServer.Service;

/// <summary>Реализация IAgentBackend через локальный канал к трей-агенту в сессии.</summary>
public sealed class PipeAgentBackend : IAgentBackend
{
    private readonly AgentLink _link;

    public PipeAgentBackend(AgentLink link) => _link = link;

    public bool Connected => _link.Connected;

    public Task<byte[]?> CaptureAsync(int maxWidth, int quality, int timeoutMs, CancellationToken ct)
        => _link.CaptureAsync(maxWidth, quality, timeoutMs, ct);

    public Task SendInputAsync(InputEvent ev, CancellationToken ct)
        => _link.SendInputAsync(ev, ct);

    public Task ShowBroadcastAsync(CancellationToken ct)
        => _link.SendAsync(PipeMsg.ShowBroadcast, Array.Empty<byte>(), ct);

    public Task BroadcastFrameAsync(byte[] jpeg, CancellationToken ct)
        => _link.SendAsync(PipeMsg.BroadcastFrame, jpeg, ct);

    public Task HideBroadcastAsync(CancellationToken ct)
        => _link.SendAsync(PipeMsg.HideBroadcast, Array.Empty<byte>(), ct);

    public Task LockAsync(byte[] payload, CancellationToken ct)
        => _link.SendAsync(PipeMsg.Lock, payload, ct);

    public Task UnlockAsync(CancellationToken ct)
        => _link.SendAsync(PipeMsg.Unlock, Array.Empty<byte>(), ct);

    // ---- Этап 3 (в режиме службы не поддерживается) ----

    public Task PowerAsync(PowerAction action, int delaySeconds, CancellationToken ct)
        => throw new NotSupportedException("Управление питанием доступно только в автономном агенте.");

    public Task ShowMessageAsync(ChatMessage msg, Func<ChatMessage, Task> reply, CancellationToken ct)
        => throw new NotSupportedException("Сообщения доступны только в автономном агенте.");

    public Task BeginFileReceiveAsync(FileTransferInfo info, Func<FileResult, Task> notify, CancellationToken ct)
        => throw new NotSupportedException("Передача файлов доступна только в автономном агенте.");

    public Task FileReceiveChunkAsync(byte[] data, CancellationToken ct)
        => throw new NotSupportedException("Передача файлов доступна только в автономном агенте.");

    public Task EndFileReceiveAsync(CancellationToken ct)
        => throw new NotSupportedException("Передача файлов доступна только в автономном агенте.");

    public Task SendFileToClientAsync(string path, Func<MsgType, byte[], CancellationToken, Task> send, CancellationToken ct)
        => throw new NotSupportedException("Передача файлов доступна только в автономном агенте.");

    // ---- Этап 4 ----

    public Task NoteAsync(string category, string message, CancellationToken ct) => Task.CompletedTask;

    public Task ApplyAppPolicyAsync(AppPolicyMessage policy, CancellationToken ct)
        => throw new NotSupportedException("Ограничение приложений доступно только в автономном агенте.");

    public Task SetInternetAsync(InternetPolicyMessage policy, CancellationToken ct)
        => throw new NotSupportedException("Ограничение интернета доступно только в автономном агенте.");

    public Task<List<JournalEntry>> GetJournalAsync(CancellationToken ct)
        => Task.FromResult(new List<JournalEntry>());

    public Task ShowTestAsync(TestDefinition test, Func<TestResult, Task> submit, CancellationToken ct)
        => throw new NotSupportedException("Тесты доступны только в автономном агенте.");

    public Task<bool> StopTestAsync(string reason, CancellationToken ct)
        => throw new NotSupportedException("Тесты доступны только в автономном агенте.");

    public Task<PersonalizationResult> ApplyPersonalizationAsync(PersonalizationMessage msg, CancellationToken ct)
        => throw new NotSupportedException("Персонализация доступна только в автономном агенте.");

    public Task<AutostartReport> ScanAutostartAsync(bool quarantine, CancellationToken ct)
        => throw new NotSupportedException("Защита автозагрузки доступна только в автономном агенте.");

    public Task<CloseAppsResult> CloseAppsAsync(CloseAppsMessage req, CancellationToken ct)
        => throw new NotSupportedException("Закрытие окон доступно только в автономном агенте.");
}
