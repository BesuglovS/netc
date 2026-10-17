using System.Net.Sockets;

namespace NetcShared;

/// <summary>Соединение с одним ПК: рукопожатие, поток экрана, ввод, трансляция, блокировка,
/// питание, сообщения и передача файлов.</summary>
public sealed class ScreenClient : IDisposable
{
    private const int FileChunkSize = 64 * 1024;

    /// <summary>Верхний предел размера принимаемого от ученика файла (защита от забивания диска).</summary>
    public const long MaxFileBytes = 2L * 1024 * 1024 * 1024;

    private readonly string _host;
    private readonly int _port;
    private readonly byte[] _key;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly object _fileGate = new();

    private TcpClient? _tcp;
    private SecureChannel? _channel;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;

    // Приём файла (ученик → учитель)
    private FileStream? _inFile;
    private long _inExpected;
    private long _inReceived;
    private string? _inDir;
    private TaskCompletionSource<FileResult>? _inTcs;

    // Передача файла (учитель → ученик): ждём подтверждение
    private TaskCompletionSource<FileResult>? _outTcs;

    // Журнал и тест: ожидание ответа
    private TaskCompletionSource<List<JournalEntry>>? _journalTcs;
    private TaskCompletionSource<TestResult>? _testTcs;
    private TaskCompletionSource<PersonalizationResult>? _persTcs;
    private TaskCompletionSource<AutostartReport>? _autoTcs;
    private TaskCompletionSource<CloseAppsResult>? _closeTcs;

    public event Action<byte[]>? FrameReceived;
    public event Action<bool>? ConnectionChanged;
    public event Action<ChatMessage>? MessageReceived;
    public event Action<FileResult>? FileResultReceived;

    public ScreenClient(string host, int port, byte[] key)
    {
        _host = host;
        _port = port;
        _key = key;
    }

    public string Host => _host;

    public bool IsConnected => _channel is not null;

    public async Task ConnectAsync()
    {
        Stop();

        // Дожидаемся завершения прошлого цикла приёма: его finally завершает ожидающие
        // операции (журнал/тест/персонализация/файлы) — иначе он мог бы «закрыть» уже
        // новое соединение результатом «disconnect».
        var prevLoop = _receiveLoop;
        _receiveLoop = null;
        if (prevLoop is not null)
        {
            try { await prevLoop.ConfigureAwait(false); } catch { }
        }

        _cts = new CancellationTokenSource();
        CancellationToken ct = _cts.Token;

        _tcp = new TcpClient();
        await _tcp.ConnectAsync(_host, _port, ct);
        _tcp.NoDelay = true;
        _channel = new SecureChannel(_tcp.GetStream(), _key);

        await _channel.SendAsync(MsgType.Hello, Json.ToBytes(new HelloMessage
        {
            Version = NetcProtocol.Version,
            ClientName = Environment.MachineName
        }), ct);

        var ack = await _channel.ReceiveAsync(ct);
        if (ack is null || ack.Value.Type != MsgType.HelloAck)
        {
            throw new InvalidOperationException("агент не ответил (неверный ключ?)");
        }

        _receiveLoop = ReceiveLoopAsync(ct);
        ConnectionChanged?.Invoke(true);
    }

    public async Task StartScreenAsync(int maxWidth, int quality, int fps)
    {
        await ConnectAsync();
        await SendAsync(MsgType.ScreenStart, Json.ToBytes(new ScreenStartMessage
        {
            MaxWidth = maxWidth,
            Quality = quality,
            Fps = fps
        }));
    }

    public Task SendBroadcastStartAsync() => SendAsync(MsgType.BroadcastStart, Array.Empty<byte>());
    public Task SendBroadcastFrameAsync(byte[] jpeg) => SendAsync(MsgType.BroadcastFrame, jpeg);
    public Task SendBroadcastStopAsync() => SendAsync(MsgType.BroadcastStop, Array.Empty<byte>());
    public Task SendLockAsync(bool blank, string text) => SendAsync(MsgType.LockScreen, Json.ToBytes(new LockMessage { Blank = blank, Text = text }));
    public Task SendUnlockAsync() => SendAsync(MsgType.UnlockScreen, Array.Empty<byte>());
    public Task SendInputAsync(InputEvent ev) => SendAsync(MsgType.InputEvent, Json.ToBytes(ev));

    public Task SendPowerAsync(PowerAction action, int delaySeconds)
        => SendAsync(MsgType.PowerCommand, Json.ToBytes(new PowerCommandMessage { Action = action, DelaySeconds = delaySeconds }));

    public Task SendMessageAsync(ChatMessage msg) => SendAsync(MsgType.MessageSend, Json.ToBytes(msg));

    public Task SendAppPolicyAsync(AppControlMode mode, IEnumerable<string> names)
        => SendAsync(MsgType.AppPolicy, Json.ToBytes(new AppPolicyMessage
        {
            Mode = mode,
            Names = names.ToList()
        }));

    public Task SendInternetAsync(InternetPolicyMessage policy)
        => SendAsync(MsgType.InternetPolicy, Json.ToBytes(policy));

    /// <summary>Запросить журнал агента.</summary>
    public async Task<List<JournalEntry>?> RequestJournalAsync(int timeoutMs, CancellationToken ct)
    {
        await ConnectAsync();
        var tcs = new TaskCompletionSource<List<JournalEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_fileGate) _journalTcs = tcs;

        await SendCheckedAsync(MsgType.JournalRequest, Array.Empty<byte>(), ct);

        var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, ct));
        if (done == tcs.Task) return await tcs.Task;
        lock (_fileGate) { if (_journalTcs == tcs) _journalTcs = null; }
        return null;
    }

    /// <summary>Отправить тест и дождаться результата (timeoutMs = 0 — без ограничения времени).</summary>
    public async Task<TestResult?> SendTestAsync(TestDefinition test, int timeoutMs, CancellationToken ct)
    {
        await ConnectAsync();
        var tcs = new TaskCompletionSource<TestResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_fileGate) _testTcs = tcs;

        await SendCheckedAsync(MsgType.TestStart, Json.ToBytes(test), ct);

        if (timeoutMs <= 0) return await tcs.Task.WaitAsync(ct);

        var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, ct));
        if (done == tcs.Task) return await tcs.Task;
        lock (_fileGate) { if (_testTcs == tcs) _testTcs = null; }
        return null;
    }

    /// <summary>Досрочно завершить активный тест на этом ПК (результат придёт в SendTestAsync).</summary>
    public Task StopTestAsync(string reason) =>
        SendCheckedAsync(MsgType.TestStop, Json.ToBytes(new TestStopMessage { Reason = reason }), CancellationToken.None);

    /// <summary>Применить персонализацию (картинки уже должны быть переданы на ПК).</summary>
    public async Task<PersonalizationResult?> ApplyPersonalizationAsync(PersonalizationMessage msg, int timeoutMs, CancellationToken ct)
    {
        if (!IsConnected) await ConnectAsync();

        var tcs = new TaskCompletionSource<PersonalizationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_fileGate) _persTcs = tcs;

        await SendCheckedAsync(MsgType.Personalization, Json.ToBytes(msg), ct);

        var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, ct));
        if (done == tcs.Task) return await tcs.Task;
        lock (_fileGate) { if (_persTcs == tcs) _persTcs = null; }
        return null;
    }

    /// <summary>Проверить/вычистить автозагрузку ученика.</summary>
    public async Task<AutostartReport?> ScanAutostartAsync(bool quarantine, int timeoutMs, CancellationToken ct)
    {
        if (!IsConnected) await ConnectAsync();

        var tcs = new TaskCompletionSource<AutostartReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_fileGate) _autoTcs = tcs;

        await SendCheckedAsync(MsgType.AutostartScan, Json.ToBytes(new AutostartScanRequest { Quarantine = quarantine }), ct);

        var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, ct));
        if (done == tcs.Task) return await tcs.Task;
        lock (_fileGate) { if (_autoTcs == tcs) _autoTcs = null; }
        return null;
    }

    /// <summary>Закрыть все окна и приложения на ПК и дождаться результата.</summary>
    public async Task<CloseAppsResult?> CloseAppsAsync(CloseAppsMessage req, int timeoutMs, CancellationToken ct)
    {
        await ConnectAsync();

        var tcs = new TaskCompletionSource<CloseAppsResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_fileGate) _closeTcs = tcs;

        await SendCheckedAsync(MsgType.CloseApps, Json.ToBytes(req), ct);

        var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, ct));
        if (done == tcs.Task) return await tcs.Task;
        lock (_fileGate) { if (_closeTcs == tcs) _closeTcs = null; }
        return null;
    }

    /// <summary>Отправить файл на ученический ПК и дождаться подтверждения.</summary>
    public async Task<FileResult> SendFileAsync(string localPath, int timeoutMs, CancellationToken ct)
    {
        await ConnectAsync();
        var fi = new FileInfo(localPath);
        var tcs = new TaskCompletionSource<FileResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_fileGate) _outTcs = tcs;

        await SendCheckedAsync(MsgType.FileStart,
            Json.ToBytes(new FileTransferInfo { Name = fi.Name, Size = fi.Length }), ct);

        byte[] buffer = new byte[FileChunkSize];
        await using (var fs = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            int read;
            while ((read = await fs.ReadAsync(buffer, ct)) > 0)
            {
                await SendCheckedAsync(MsgType.FileChunk, buffer.AsSpan(0, read).ToArray(), ct);
            }
        }
        await SendCheckedAsync(MsgType.FileEnd, Array.Empty<byte>(), ct);

        var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, ct));
        if (done == tcs.Task) return await tcs.Task;
        lock (_fileGate) { if (_outTcs == tcs) _outTcs = null; }
        return new FileResult { Ok = false, Message = "таймаут подтверждения" };
    }

    /// <summary>Запросить файл с ученического ПК и сохранить его в <paramref name="localDir"/>.</summary>
    public async Task<FileResult> RequestFileAsync(string remotePath, string localDir, int timeoutMs, CancellationToken ct)
    {
        await ConnectAsync();
        var tcs = new TaskCompletionSource<FileResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_fileGate) { _inTcs = tcs; _inDir = localDir; }

        await SendCheckedAsync(MsgType.FileRequest, Json.ToBytes(new FileRequest { Path = remotePath }), ct);

        var done = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs, ct));
        if (done == tcs.Task) return await tcs.Task;
        lock (_fileGate) { if (_inTcs == tcs) { _inTcs = null; CleanupInFile(); } }
        return new FileResult { Ok = false, Message = "таймаут получения" };
    }

    private async Task SendAsync(MsgType type, byte[] payload)
    {
        var channel = _channel ?? throw new InvalidOperationException("нет соединения с агентом");
        await _writeLock.WaitAsync();
        try { await channel.SendAsync(type, payload); }
        finally { _writeLock.Release(); }
    }

    private async Task SendCheckedAsync(MsgType type, byte[] payload, CancellationToken ct)
    {
        var channel = _channel ?? throw new InvalidOperationException("нет соединения с агентом");
        await _writeLock.WaitAsync(ct);
        try { await channel.SendAsync(type, payload, ct); }
        finally { _writeLock.Release(); }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var msg = await _channel!.ReceiveAsync(ct);
                if (msg is null) break;

                switch (msg.Value.Type)
                {
                    case MsgType.ScreenFrame:
                        if (msg.Value.Payload.Length > 0) FrameReceived?.Invoke(msg.Value.Payload);
                        break;

                    case MsgType.MessageReply:
                    {
                        var chat = Json.FromBytes<ChatMessage>(msg.Value.Payload);
                        if (chat is not null) MessageReceived?.Invoke(chat);
                        break;
                    }

                    case MsgType.FileStart:
                        HandleFileStart(Json.FromBytes<FileTransferInfo>(msg.Value.Payload));
                        break;

                    case MsgType.FileChunk:
                        HandleFileChunk(msg.Value.Payload);
                        break;

                    case MsgType.FileEnd:
                        HandleFileEnd();
                        break;

                    case MsgType.FileResult:
                    {
                        var fr = Json.FromBytes<FileResult>(msg.Value.Payload) ?? new FileResult();
                        TaskCompletionSource<FileResult>? outTcs;
                        lock (_fileGate) { outTcs = _outTcs; _outTcs = null; }
                        outTcs?.TrySetResult(fr);
                        FileResultReceived?.Invoke(fr);
                        break;
                    }

                    case MsgType.JournalResponse:
                    {
                        var jr = Json.FromBytes<JournalResponse>(msg.Value.Payload) ?? new JournalResponse();
                        TaskCompletionSource<List<JournalEntry>>? tcs;
                        lock (_fileGate) { tcs = _journalTcs; _journalTcs = null; }
                        tcs?.TrySetResult(jr.Entries);
                        break;
                    }

                    case MsgType.TestResult:
                    {
                        var tr = Json.FromBytes<TestResult>(msg.Value.Payload) ?? new TestResult();
                        TaskCompletionSource<TestResult>? tcs;
                        lock (_fileGate) { tcs = _testTcs; _testTcs = null; }
                        tcs?.TrySetResult(tr);
                        break;
                    }

                    case MsgType.PersonalizationAck:
                    {
                        var pr = Json.FromBytes<PersonalizationResult>(msg.Value.Payload) ?? new PersonalizationResult();
                        TaskCompletionSource<PersonalizationResult>? tcs;
                        lock (_fileGate) { tcs = _persTcs; _persTcs = null; }
                        tcs?.TrySetResult(pr);
                        break;
                    }

                    case MsgType.AutostartReport:
                    {
                        var ar = Json.FromBytes<AutostartReport>(msg.Value.Payload) ?? new AutostartReport();
                        TaskCompletionSource<AutostartReport>? tcs;
                        lock (_fileGate) { tcs = _autoTcs; _autoTcs = null; }
                        tcs?.TrySetResult(ar);
                        break;
                    }

                    case MsgType.CloseAppsAck:
                    {
                        var cr = Json.FromBytes<CloseAppsResult>(msg.Value.Payload) ?? new CloseAppsResult();
                        TaskCompletionSource<CloseAppsResult>? tcs;
                        lock (_fileGate) { tcs = _closeTcs; _closeTcs = null; }
                        tcs?.TrySetResult(cr);
                        break;
                    }
                }
            }
        }
        catch { /* разрыв */ }
        finally
        {
            lock (_fileGate)
            {
                CleanupInFile();
                // Завершаем все ожидающие операции, иначе окна «Тест»/«Журнал»/«Персонализация» зависнут.
                _inTcs?.TrySetResult(new FileResult { Ok = false, Message = "соединение закрыто" });
                _inTcs = null;
                _outTcs?.TrySetResult(new FileResult { Ok = false, Message = "соединение закрыто" });
                _outTcs = null;
                _journalTcs?.TrySetResult(new List<JournalEntry>());
                _journalTcs = null;
                _testTcs?.TrySetResult(new TestResult { EndReason = "disconnect" });
                _testTcs = null;
                _persTcs?.TrySetResult(new PersonalizationResult { Ok = false, Message = "соединение закрыто" });
                _persTcs = null;
                _autoTcs?.TrySetResult(new AutostartReport());
                _autoTcs = null;
                _closeTcs?.TrySetResult(new CloseAppsResult { Message = "соединение закрыто" });
                _closeTcs = null;
            }

            // Помечаем соединение оборванным: иначе IsConnected навсегда остаётся true
            // и формы (например MessageForm) не переподключаются к агенту.
            try { _channel?.Dispose(); } catch { }
            _channel = null;
            try { _tcp?.Dispose(); } catch { }
            _tcp = null;

            ConnectionChanged?.Invoke(false);
        }
    }

    private void HandleFileStart(FileTransferInfo? info)
    {
        if (info is null) return;

        if (info.Size < 0 || info.Size > MaxFileBytes)
        {
            TaskCompletionSource<FileResult>? big;
            lock (_fileGate) { big = _inTcs; _inTcs = null; CleanupInFile(); }
            big?.TrySetResult(new FileResult
            {
                Ok = false,
                Message = $"Файл слишком большой: {info.Size} байт (лимит {MaxFileBytes})"
            });
            return;
        }

        string dir;
        lock (_fileGate) dir = _inDir ?? Path.GetTempPath();

        try
        {
            Directory.CreateDirectory(dir);
            string name = SanitizeFileName(string.IsNullOrWhiteSpace(info.Name) ? "file.bin" : info.Name);
            string path = UniquePath(dir, name);
            var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);

            lock (_fileGate)
            {
                CleanupInFile();
                _inFile = fs;
                _inExpected = info.Size;
                _inReceived = 0;
            }
        }
        catch (Exception ex)
        {
            TaskCompletionSource<FileResult>? tcs;
            lock (_fileGate) { tcs = _inTcs; _inTcs = null; }
            tcs?.TrySetResult(new FileResult { Ok = false, Message = ex.Message });
        }
    }

    private void HandleFileChunk(byte[] data)
    {
        var fs = _inFile;
        if (fs is null || data.Length == 0) return;
        try
        {
            fs.Write(data, 0, data.Length);
            lock (_fileGate) _inReceived += data.Length;
        }
        catch { }
    }

    private void HandleFileEnd()
    {
        FileStream? fs;
        long expected, received;
        TaskCompletionSource<FileResult>? tcs;
        string path;

        lock (_fileGate)
        {
            fs = _inFile;
            path = fs?.Name ?? "";
            expected = _inExpected;
            received = _inReceived;
            tcs = _inTcs;
            _inFile = null;
            _inTcs = null;
        }

        if (fs is not null)
        {
            try { fs.Flush(); fs.Dispose(); } catch { }
        }

        bool ok = fs is not null && (expected <= 0 || received == expected);
        tcs?.TrySetResult(new FileResult
        {
            Ok = ok,
            Path = path,
            Message = ok ? "Сохранено" : $"Получено {received} из {expected} байт"
        });
    }

    private void CleanupInFile()
    {
        try { _inFile?.Dispose(); } catch { }
        _inFile = null;
        _inExpected = 0;
        _inReceived = 0;
        _inDir = null;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        name = name.Trim();
        return name.Length == 0 ? "file.bin" : name;
    }

    private static string UniquePath(string dir, string fileName)
    {
        string path = Path.Combine(dir, fileName);
        if (!File.Exists(path)) return path;

        string stem = Path.GetFileNameWithoutExtension(fileName);
        string ext = Path.GetExtension(fileName);
        for (int i = 1; ; i++)
        {
            string candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    public void Stop()
    {
        try { _cts?.Cancel(); } catch { }
        try { _channel?.Dispose(); } catch { }
        try { _tcp?.Dispose(); } catch { }
        _channel = null;
        _tcp = null;
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose() => Stop();
}
