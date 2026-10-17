using System.Diagnostics;
using NetcServer.Core;
using NetcShared;

namespace NetcServer.Agent;

/// <summary>Прямая реализация IAgentBackend в сессии ученика (без службы).</summary>
public sealed class LocalAgentBackend : IAgentBackend
{
    private const int FileChunkSize = 64 * 1024;

    /// <summary>Верхний предел размера принимаемого файла (защита от забивания диска).</summary>
    public const long MaxFileBytes = 2L * 1024 * 1024 * 1024;

    private readonly object _gate = new();
    private FileStream? _rx;
    private string? _rxPath;
    private long _rxExpected;
    private long _rxReceived;
    private Func<FileResult, Task>? _rxNotify;

    public bool Connected => true;

    public Task<byte[]?> CaptureAsync(int maxWidth, int quality, int timeoutMs, CancellationToken ct)
        => Task.FromResult<byte[]?>(ScreenCapture.CaptureJpeg(maxWidth, quality));

    public Task SendInputAsync(InputEvent ev, CancellationToken ct)
    {
        InputInjector.Handle(ev);
        return Task.CompletedTask;
    }

    public Task ShowBroadcastAsync(CancellationToken ct)
    {
        AgentUi.ShowBroadcast();
        return Task.CompletedTask;
    }

    public Task BroadcastFrameAsync(byte[] jpeg, CancellationToken ct)
    {
        AgentUi.SetBroadcastFrame(jpeg);
        return Task.CompletedTask;
    }

    public Task HideBroadcastAsync(CancellationToken ct)
    {
        AgentUi.HideBroadcast();
        return Task.CompletedTask;
    }

    public Task LockAsync(byte[] payload, CancellationToken ct)
    {
        AgentUi.ShowLock(payload);
        return Task.CompletedTask;
    }

    public Task UnlockAsync(CancellationToken ct)
    {
        AgentUi.HideLock();
        return Task.CompletedTask;
    }

    // ---- Этап 3 ----

    public Task PowerAsync(PowerAction action, int delaySeconds, CancellationToken ct)
    {
        int d = Math.Clamp(delaySeconds, 0, 600);
        string args = action switch
        {
            PowerAction.Restart => "/r /t " + d + " /f",
            PowerAction.Logoff => "/l",
            _ => "/s /t " + d + " /f"
        };
        AgentJournal.Add("power", "Команда питания: " + action + " (задержка " + d + "с)");
        try
        {
            Process.Start(new ProcessStartInfo("shutdown", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        catch { /* питание недоступно — игнорируем */ }
        return Task.CompletedTask;
    }

    public Task ShowMessageAsync(ChatMessage msg, Func<ChatMessage, Task> reply, CancellationToken ct)
    {
        AgentJournal.Add("message", "Сообщение учителя: " + Truncate(msg.Text, 120));
        AgentUi.ShowMessage(msg, reply);
        return Task.CompletedTask;
    }

    public Task BeginFileReceiveAsync(FileTransferInfo info, Func<FileResult, Task> notify, CancellationToken ct)
    {
        if (info.Size < 0 || info.Size > MaxFileBytes)
        {
            AgentJournal.Add("files", $"Отклонён приём файла «{info.Name}»: размер {info.Size} байт");
            return notify(new FileResult
            {
                Ok = false,
                Message = $"Недопустимый размер файла: {info.Size} байт (лимит {MaxFileBytes})"
            });
        }

        lock (_gate)
        {
            CleanupReceive();
            _rxNotify = notify;
            _rxExpected = info.Size;
            _rxReceived = 0;

            string dir = NetcPaths.IncomingDirectory;
            Directory.CreateDirectory(dir);
            string name = NetcPaths.SanitizeFileName(
                string.IsNullOrWhiteSpace(info.Name) ? "file.bin" : info.Name);
            _rxPath = NetcPaths.UniquePath(dir, name);
            _rx = new FileStream(_rxPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        }
        return Task.CompletedTask;
    }

    public async Task FileReceiveChunkAsync(byte[] data, CancellationToken ct)
    {
        FileStream? fs;
        lock (_gate) fs = _rx;
        if (fs is null || data.Length == 0) return;

        await fs.WriteAsync(data, ct).ConfigureAwait(false);
        lock (_gate) _rxReceived += data.Length;
    }

    public async Task EndFileReceiveAsync(CancellationToken ct)
    {
        FileStream? fs;
        string? path;
        long expected, received;
        Func<FileResult, Task>? notify;

        lock (_gate)
        {
            fs = _rx;
            path = _rxPath;
            expected = _rxExpected;
            received = _rxReceived;
            notify = _rxNotify;

            // Не вызываем CleanupReceive(): он закрыл бы поток до сброса. Обнуляем поля вручную.
            _rx = null;
            _rxPath = null;
            _rxExpected = 0;
            _rxReceived = 0;
            _rxNotify = null;
        }
        if (fs is null || notify is null) return;

        try
        {
            await fs.FlushAsync(ct).ConfigureAwait(false);
            await fs.DisposeAsync().ConfigureAwait(false);

            bool ok = expected <= 0 || received == expected;
            await notify(new FileResult
            {
                Ok = ok,
                Path = path ?? "",
                Message = ok ? "Сохранено" : $"Получено {received} из {expected} байт"
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            try { await fs.DisposeAsync().ConfigureAwait(false); } catch { }
            await notify(new FileResult { Ok = false, Path = path ?? "", Message = ex.Message }).ConfigureAwait(false);
        }
    }

    public async Task SendFileToClientAsync(string path, Func<MsgType, byte[], CancellationToken, Task> send, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                await send(MsgType.FileResult,
                    Json.ToBytes(new FileResult { Ok = false, Message = "Файл не найден: " + path }), ct).ConfigureAwait(false);
                return;
            }

            var fi = new FileInfo(path);
            await send(MsgType.FileStart,
                Json.ToBytes(new FileTransferInfo { Name = fi.Name, Size = fi.Length }), ct).ConfigureAwait(false);

            byte[] buffer = new byte[FileChunkSize];
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            int read;
            while ((read = await fs.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                byte[] chunk = buffer.AsSpan(0, read).ToArray();
                await send(MsgType.FileChunk, chunk, ct).ConfigureAwait(false);
            }
            await send(MsgType.FileEnd, Array.Empty<byte>(), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            try
            {
                await send(MsgType.FileResult,
                    Json.ToBytes(new FileResult { Ok = false, Message = ex.Message }), ct).ConfigureAwait(false);
            }
            catch { }
        }
    }

    // ---- Этап 4 ----

    public Task NoteAsync(string category, string message, CancellationToken ct)
    {
        AgentJournal.Add(category, message);
        return Task.CompletedTask;
    }

    public Task ApplyAppPolicyAsync(AppPolicyMessage policy, CancellationToken ct)
    {
        AppControl.SetPolicy(policy.Mode, policy.Names);
        AgentJournal.Add("apps", policy.Mode == AppControlMode.Off
            ? "Ограничение приложений выключено"
            : $"Ограничение приложений: {policy.Mode}, позиций {policy.Names.Count}");
        return Task.CompletedTask;
    }

    public Task SetInternetAsync(InternetPolicyMessage policy, CancellationToken ct)
    {
        InternetControl.Set(policy);
        return Task.CompletedTask;
    }

    public Task<List<JournalEntry>> GetJournalAsync(CancellationToken ct)
        => Task.FromResult(AgentJournal.Snapshot());

    public Task ShowTestAsync(TestDefinition test, Func<TestResult, Task> submit, CancellationToken ct)
    {
        AgentJournal.Add("test", $"Тест «{test.Title}» ({test.Questions.Count} вопр.) выдан");
        AgentUi.ShowTest(test, async result =>
        {
            AgentJournal.Add("test",
                $"Тест «{result.Title}»: {result.Score:0.##}/{result.MaxScore:0.##} ({result.Percent}%), причина: {result.EndReason}");
            await submit(result).ConfigureAwait(false);
        });
        return Task.CompletedTask;
    }

    public Task<bool> StopTestAsync(string reason, CancellationToken ct)
    {
        bool had = AgentUi.HasTest;
        if (had)
        {
            AgentJournal.Add("test", "Тест завершён досрочно (" + reason + ")");
            AgentUi.StopTest(reason);
        }
        return Task.FromResult(had);
    }

    public Task<PersonalizationResult> ApplyPersonalizationAsync(PersonalizationMessage msg, CancellationToken ct)
        => Task.FromResult(Personalization.Apply(msg));

    public Task<AutostartReport> ScanAutostartAsync(bool quarantine, CancellationToken ct)
        => Task.FromResult(AutostartGuard.Scan(quarantine, journal: true));

    public Task<CloseAppsResult> CloseAppsAsync(CloseAppsMessage req, CancellationToken ct)
        => WindowCloser.CloseAsync(req, ct);

    public Task SetMonitorAsync(bool off, CancellationToken ct)
    {
        MonitorControl.Set(off);
        return Task.CompletedTask;
    }

    private static string Truncate(string? s, int max)
    {
        s ??= "";
        return s.Length <= max ? s : s[..max] + "…";
    }

    private void CleanupReceive()
    {
        try { _rx?.Dispose(); } catch { }
        _rx = null;
        _rxPath = null;
        _rxExpected = 0;
        _rxReceived = 0;
        _rxNotify = null;
    }
}
