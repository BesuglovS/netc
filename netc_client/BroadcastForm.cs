using System.Drawing;
using NetcShared;

namespace NetcClient;

/// <summary>Трансляция экрана учителя и блокировка экранов учеников.</summary>
public sealed class BroadcastForm : Form
{
    private readonly TargetsSelector _targets;
    private readonly NetcSettings _settings;
    private readonly byte[] _key;
    private readonly Dictionary<string, ScreenClient> _conns = new();
    private readonly object _gate = new();

    private readonly TextBox _log;
    private readonly CheckBox _blank;
    private CancellationTokenSource? _cts;

    public BroadcastForm(TargetSet targets, NetcSettings settings, byte[] key)
    {
        _settings = settings;
        _key = key;

        Text = "Трансляция и блокировка";
        ClientSize = new Size(640, 340);
        MinimumSize = new Size(520, 260);
        StartPosition = FormStartPosition.CenterParent;

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Padding = new Padding(8, 8, 8, 4)
        };

        var btnStart = MakeButton("Начать трансляцию");
        btnStart.Click += async (_, _) => await StartBroadcastAsync();
        var btnStop = MakeButton("Остановить трансляцию");
        btnStop.Click += async (_, _) => await StopBroadcastAsync();
        var btnLock = MakeButton("Заблокировать экраны");
        btnLock.Click += async (_, _) => await DoLockAsync(true);
        var btnUnlock = MakeButton("Разблокировать");
        btnUnlock.Click += async (_, _) => await DoLockAsync(false);
        _blank = new CheckBox { Text = "Чёрный экран", AutoSize = true, Margin = new Padding(12, 9, 3, 3) };

        top.Controls.Add(btnStart);
        top.Controls.Add(btnStop);
        top.Controls.Add(btnLock);
        top.Controls.Add(btnUnlock);
        top.Controls.Add(_blank);

        _log = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9F),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.Gainsboro
        };

        Controls.Add(_log);
        Controls.Add(top);

        _targets = TargetsHelper.AddTargetsBar(this, targets);

        // Синхронное закрытие: async-обработчик FormClosing не ожидается WinForms, поэтому форма
        // успевала закрыться до отправки BroadcastStop. Достаточно отменить цикл и закрыть
        // соединения — агент снимает оверлеи сам, когда отключается владелец (TcpControlServer).
        FormClosing += (_, _) =>
        {
            try { _cts?.Cancel(); } catch { }
            try { _cts?.Dispose(); } catch { }
            _cts = null;
            DisposeConnections();
        };

        // Задаём после добавления контролов, иначе масштабируется только форма (наезды при высоком DPI).
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    private static Button MakeButton(string text) => new Button
    {
        Text = text,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        MinimumSize = new Size(110, 30),
        Padding = new Padding(8, 0, 8, 0),
        Margin = new Padding(3, 6, 3, 3)
    };

    private void Log(string message) =>
        UiSafe.Append(_log, DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);

    private async Task EnsureConnectionsAsync()
    {
        var selected = _targets.Selected;

        // Отключаем ПК, с которых сняли выбор, — команды должны идти только по текущему выбору.
        lock (_gate)
        {
            foreach (var addr in _conns.Keys.ToList())
            {
                if (!selected.Contains(addr, StringComparer.OrdinalIgnoreCase))
                {
                    try { _conns[addr].Dispose(); } catch { }
                    _conns.Remove(addr);
                }
            }
        }

        foreach (string addr in selected)
        {
            lock (_gate) { if (_conns.ContainsKey(addr)) continue; }

            var client = new ScreenClient(addr, _settings.ControlPort, _key);
            try
            {
                await client.ConnectAsync();
                lock (_gate) { _conns[addr] = client; }
                Log(addr + ": подключено");
            }
            catch (Exception ex)
            {
                client.Dispose();
                Log(addr + ": ошибка подключения — " + ex.Message);
            }
        }
    }

    private async Task StartBroadcastAsync()
    {
        await EnsureConnectionsAsync();
        List<ScreenClient> list = SelectedConnections();
        foreach (var c in list)
        {
            try { await c.SendBroadcastStartAsync(); }
            catch (Exception ex) { Log(c.Host + ": ошибка трансляции — " + ex.Message); }
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        Log("Трансляция начата на " + list.Count + " ПК");
        _ = BroadcastLoopAsync(_cts.Token);
    }

    private async Task BroadcastLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            byte[] frame = ClientScreenCapture.CaptureJpeg(1280, 55);
            if (frame.Length > 0)
            {
                foreach (var c in SelectedConnections())
                {
                    try { await c.SendBroadcastFrameAsync(frame); } catch { }
                }
            }
            try { await Task.Delay(500, ct); } catch { break; }
        }
    }

    private async Task StopBroadcastAsync()
    {
        try { _cts?.Cancel(); } catch { }
        _cts?.Dispose();
        _cts = null;
        foreach (var c in SelectedConnections())
        {
            try { await c.SendBroadcastStopAsync(); } catch { }
        }
        Log("Трансляция остановлена");
    }

    private async Task DoLockAsync(bool lockIt)
    {
        await EnsureConnectionsAsync();
        List<ScreenClient> list = SelectedConnections();
        foreach (var c in list)
        {
            try
            {
                if (lockIt) await c.SendLockAsync(_blank.Checked, "Экран заблокирован учителем");
                else await c.SendUnlockAsync();
            }
            catch (Exception ex) { Log(c.Host + ": ошибка — " + ex.Message); }
        }
        Log(lockIt ? ("Заблокировано ПК: " + list.Count) : ("Разблокировано ПК: " + list.Count));
    }

    /// <summary>Активные соединения только с текущим выбранным набором ПК.</summary>
    private List<ScreenClient> SelectedConnections()
    {
        var selected = _targets.Selected;
        lock (_gate)
        {
            return _conns
                .Where(kv => selected.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
                .Select(kv => kv.Value)
                .ToList();
        }
    }

    private void DisposeConnections()
    {
        lock (_gate)
        {
            foreach (var c in _conns.Values) c.Dispose();
            _conns.Clear();
        }
    }
}
