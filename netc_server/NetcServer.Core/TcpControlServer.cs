using System.Net;
using System.Net.Sockets;
using NetcShared;

namespace NetcServer.Core;

/// <summary>TCP-сервер управления: рукопожатие, сведения, поток экрана, ввод, трансляция, блокировка.</summary>
public sealed class TcpControlServer
{
    private readonly NetcSettings _settings;
    private readonly IAgentBackend _agent;
    private readonly Action<string> _log;
    private readonly byte[] _key;

    // Сколько активных соединений держат трансляцию/блокировку. Оверлеи снимаются только
    // когда соединение-владелец отключилось И больше их никто не держит. Иначе любой короткий
    // запрос («Обновить сведения», журнал) рушил бы блокировку/трансляцию у всего класса.
    private int _broadcastOwners;
    private int _lockOwners;

    public TcpControlServer(NetcSettings settings, IAgentBackend agent, Action<string> log)
    {
        _settings = settings;
        _agent = agent;
        _log = log ?? (_ => { });
        _key = NetcCrypto.DeriveKey(settings.SharedKey);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Any, _settings.ControlPort);
        try { listener.Start(); }
        catch (Exception ex)
        {
            _log("TCP: не удалось открыть порт " + _settings.ControlPort + ": " + ex.Message);
            return;
        }
        _log("netc: прослушивание TCP :" + _settings.ControlPort);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                _ = HandleClientAsync(client, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _log("TCP: " + ex.Message); }
        finally { listener.Stop(); }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        string ep = client.Client.RemoteEndPoint?.ToString() ?? "?";
        using (client)
        {
            client.NoDelay = true;
            using var channel = new SecureChannel(client.GetStream(), _key);

            var first = await channel.ReceiveAsync(ct).ConfigureAwait(false);
            if (first is null || first.Value.Type != MsgType.Hello)
            {
                _log("Отклонено (неверный ключ): " + ep);
                return;
            }

            // Разовые запросы (например «Обновить сведения») не считаем «сессией учителя» —
            // иначе журнал агента забивается записями «Подключён/Отключён учитель».
            var hello = first.Value.Payload.Length > 0 ? Json.FromBytes<HelloMessage>(first.Value.Payload) : null;
            bool quiet = string.Equals(hello?.Purpose, "query", StringComparison.OrdinalIgnoreCase);

            var info = PcInfoProvider.Get();
            await channel.SendAsync(MsgType.HelloAck, Json.ToBytes(new HelloAck
            {
                Version = NetcProtocol.Version,
                Ok = true,
                MachineName = info.MachineName
            }), ct).ConfigureAwait(false);

            if (!quiet)
            {
                _log("Подключён учитель с " + ep);
                await _agent.NoteAsync("session", "Подключён учитель " + ep, ct).ConfigureAwait(false);
            }

            var writeLock = new SemaphoreSlim(1, 1);
            CancellationTokenSource? streamCts = null;
            bool testActive = false;
            bool ownsBroadcast = false;
            bool ownsLock = false;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var msg = await channel.ReceiveAsync(ct).ConfigureAwait(false);
                    if (msg is null) break;

                    switch (msg.Value.Type)
                    {
                        case MsgType.InfoRequest:
                            await SendAsync(channel, writeLock, MsgType.InfoResponse, Json.ToBytes(PcInfoProvider.Get()), ct);
                            break;

                        case MsgType.ScreenStart:
                        {
                            streamCts?.Cancel();
                            streamCts?.Dispose();
                            var pars = Json.FromBytes<ScreenStartMessage>(msg.Value.Payload) ?? new ScreenStartMessage();
                            streamCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                            _ = StreamLoopAsync(channel, writeLock, pars, streamCts.Token);
                            break;
                        }

                        case MsgType.ScreenStop:
                            streamCts?.Cancel();
                            streamCts?.Dispose();
                            streamCts = null;
                            break;

                        case MsgType.InputEvent:
                        {
                            var ev = Json.FromBytes<InputEvent>(msg.Value.Payload);
                            if (ev is not null) await _agent.SendInputAsync(ev, ct).ConfigureAwait(false);
                            break;
                        }

                        case MsgType.BroadcastStart:
                            if (!ownsBroadcast)
                            {
                                ownsBroadcast = true;
                                Interlocked.Increment(ref _broadcastOwners);
                            }
                            await _agent.ShowBroadcastAsync(ct).ConfigureAwait(false);
                            break;

                        case MsgType.BroadcastFrame:
                            await _agent.BroadcastFrameAsync(msg.Value.Payload, ct).ConfigureAwait(false);
                            break;

                        case MsgType.BroadcastStop:
                            // Снимает трансляцию только соединение-владелец, и только когда
                            // не осталось других владельцев (иначе посторонний запрос рушил бы оверлей).
                            if (ownsBroadcast)
                            {
                                ownsBroadcast = false;
                                if (Interlocked.Decrement(ref _broadcastOwners) <= 0)
                                    await _agent.HideBroadcastAsync(ct).ConfigureAwait(false);
                            }
                            break;

                        case MsgType.LockScreen:
                            if (!ownsLock)
                            {
                                ownsLock = true;
                                Interlocked.Increment(ref _lockOwners);
                            }
                            await _agent.LockAsync(msg.Value.Payload, ct).ConfigureAwait(false);
                            break;

                        case MsgType.UnlockScreen:
                            if (ownsLock)
                            {
                                ownsLock = false;
                                if (Interlocked.Decrement(ref _lockOwners) <= 0)
                                    await _agent.UnlockAsync(ct).ConfigureAwait(false);
                            }
                            break;

                        case MsgType.PowerCommand:
                        {
                            var p = Json.FromBytes<PowerCommandMessage>(msg.Value.Payload) ?? new PowerCommandMessage();
                            await _agent.PowerAsync(p.Action, p.DelaySeconds, ct).ConfigureAwait(false);
                            await SendAsync(channel, writeLock, MsgType.PowerAck, Json.ToBytes("ok"), ct).ConfigureAwait(false);
                            break;
                        }

                        case MsgType.MessageSend:
                        {
                            var chat = Json.FromBytes<ChatMessage>(msg.Value.Payload);
                            if (chat is not null)
                            {
                                Func<ChatMessage, Task> reply = m =>
                                    SendAsync(channel, writeLock, MsgType.MessageReply, Json.ToBytes(m), ct);
                                await _agent.ShowMessageAsync(chat, reply, ct).ConfigureAwait(false);
                            }
                            break;
                        }

                        case MsgType.FileStart:
                        {
                            var fi = Json.FromBytes<FileTransferInfo>(msg.Value.Payload);
                            if (fi is not null)
                            {
                                Func<FileResult, Task> notify = r =>
                                    SendAsync(channel, writeLock, MsgType.FileResult, Json.ToBytes(r), ct);
                                await _agent.BeginFileReceiveAsync(fi, notify, ct).ConfigureAwait(false);
                            }
                            break;
                        }

                        case MsgType.FileChunk:
                            await _agent.FileReceiveChunkAsync(msg.Value.Payload, ct).ConfigureAwait(false);
                            break;

                        case MsgType.FileEnd:
                            await _agent.EndFileReceiveAsync(ct).ConfigureAwait(false);
                            break;

                        case MsgType.FileRequest:
                        {
                            var req = Json.FromBytes<FileRequest>(msg.Value.Payload);
                            if (req is not null)
                            {
                                await _agent.SendFileToClientAsync(req.Path,
                                    (t, p, c) => SendAsync(channel, writeLock, t, p, c), ct).ConfigureAwait(false);
                            }
                            break;
                        }

                        case MsgType.AppPolicy:
                        {
                            var pol = Json.FromBytes<AppPolicyMessage>(msg.Value.Payload) ?? new AppPolicyMessage();
                            await _agent.ApplyAppPolicyAsync(pol, ct).ConfigureAwait(false);
                            await SendAsync(channel, writeLock, MsgType.AppPolicyAck, Json.ToBytes("ok"), ct).ConfigureAwait(false);
                            break;
                        }

                        case MsgType.InternetPolicy:
                        {
                            var ip = Json.FromBytes<InternetPolicyMessage>(msg.Value.Payload) ?? new InternetPolicyMessage();
                            await _agent.SetInternetAsync(ip, ct).ConfigureAwait(false);
                            await SendAsync(channel, writeLock, MsgType.InternetAck, Json.ToBytes("ok"), ct).ConfigureAwait(false);
                            break;
                        }

                        case MsgType.JournalRequest:
                        {
                            var entries = await _agent.GetJournalAsync(ct).ConfigureAwait(false);
                            await SendAsync(channel, writeLock, MsgType.JournalResponse,
                                Json.ToBytes(new JournalResponse { Entries = entries }), ct).ConfigureAwait(false);
                            break;
                        }

                        case MsgType.TestStart:
                        {
                            var test = Json.FromBytes<TestDefinition>(msg.Value.Payload);
                            if (test is not null)
                            {
                                Func<TestResult, Task> submit = r =>
                                    SendAsync(channel, writeLock, MsgType.TestResult, Json.ToBytes(r), ct);
                                await _agent.ShowTestAsync(test, submit, ct).ConfigureAwait(false);
                                testActive = true;
                            }
                            break;
                        }

                        case MsgType.TestStop:
                        {
                            string reason = "teacher";
                            if (msg.Value.Payload.Length > 0)
                            {
                                var req = Json.FromBytes<TestStopMessage>(msg.Value.Payload);
                                if (req is not null && !string.IsNullOrWhiteSpace(req.Reason)) reason = req.Reason;
                            }
                            await _agent.StopTestAsync(reason, ct).ConfigureAwait(false);
                            testActive = false;
                            await SendAsync(channel, writeLock, MsgType.TestAck, Json.ToBytes("ok"), ct).ConfigureAwait(false);
                            break;
                        }

                        case MsgType.Personalization:
                        {
                            var p = Json.FromBytes<PersonalizationMessage>(msg.Value.Payload) ?? new PersonalizationMessage();
                            var result = await _agent.ApplyPersonalizationAsync(p, ct).ConfigureAwait(false);
                            await SendAsync(channel, writeLock, MsgType.PersonalizationAck, Json.ToBytes(result), ct).ConfigureAwait(false);
                            break;
                        }

                        case MsgType.AutostartScan:
                        {
                            bool quarantine = true;
                            if (msg.Value.Payload.Length > 0)
                            {
                                var req = Json.FromBytes<AutostartScanRequest>(msg.Value.Payload);
                                if (req is not null) quarantine = req.Quarantine;
                            }
                            var report = await _agent.ScanAutostartAsync(quarantine, ct).ConfigureAwait(false);
                            await SendAsync(channel, writeLock, MsgType.AutostartReport, Json.ToBytes(report), ct).ConfigureAwait(false);
                            break;
                        }

                        case MsgType.CloseApps:
                        {
                            var req = Json.FromBytes<CloseAppsMessage>(msg.Value.Payload) ?? new CloseAppsMessage();
                            var res = await _agent.CloseAppsAsync(req, ct).ConfigureAwait(false);
                            await SendAsync(channel, writeLock, MsgType.CloseAppsAck, Json.ToBytes(res), ct).ConfigureAwait(false);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log("Соединение с " + ep + " закрыто: " + ex.Message);
            }
            finally
            {
                if (!quiet)
                {
                    try { await _agent.NoteAsync("session", "Отключён учитель " + ep, CancellationToken.None).ConfigureAwait(false); } catch { }
                }
                // Снимаем оверлеи, только если их держало именно это соединение и больше никто.
                if (ownsBroadcast && Interlocked.Decrement(ref _broadcastOwners) <= 0)
                {
                    try { await _agent.HideBroadcastAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                }
                if (ownsLock && Interlocked.Decrement(ref _lockOwners) <= 0)
                {
                    try { await _agent.UnlockAsync(CancellationToken.None).ConfigureAwait(false); } catch { }
                }
                // Учитель отключился, пока шёл тест — снимаем киоск, чтобы ПК не остался заблокированным.
                if (testActive)
                {
                    try { await _agent.StopTestAsync("disconnect", CancellationToken.None).ConfigureAwait(false); } catch { }
                }
                streamCts?.Cancel();
                streamCts?.Dispose();
                writeLock.Dispose();
            }
        }
    }

    private async Task StreamLoopAsync(SecureChannel channel, SemaphoreSlim writeLock, ScreenStartMessage p, CancellationToken ct)
    {
        int fps = Math.Clamp(p.Fps, 1, 30);
        int interval = 1000 / fps;
        int failStreak = 0;

        while (!ct.IsCancellationRequested)
        {
            byte[]? frame;
            try
            {
                frame = await _agent.CaptureAsync(p.MaxWidth, p.Quality, 3000, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log("Поток экрана: сбой захвата — " + ex.Message);
                frame = null;
            }

            if (frame is not null)
            {
                failStreak = 0;
                try { await SendAsync(channel, writeLock, MsgType.ScreenFrame, frame, ct); }
                catch { break; }
            }
            else if (++failStreak >= 10)
            {
                break;
            }

            try { await Task.Delay(interval, ct).ConfigureAwait(false); }
            catch { break; }
        }
    }

    private static async Task SendAsync(SecureChannel channel, SemaphoreSlim writeLock, MsgType type, byte[]? payload, CancellationToken ct)
    {
        await writeLock.WaitAsync(ct).ConfigureAwait(false);
        try { await channel.SendAsync(type, payload, ct).ConfigureAwait(false); }
        finally { writeLock.Release(); }
    }
}
