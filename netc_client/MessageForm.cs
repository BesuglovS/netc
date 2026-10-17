using System.Drawing;
using NetcShared;

namespace NetcClient;

/// <summary>Отправка сообщений ученикам и просмотр ответов (простой чат).</summary>
public sealed class MessageForm : Form
{
    private readonly TargetsSelector _targets;
    private readonly NetcSettings _settings;
    private readonly byte[] _key;
    private readonly Dictionary<string, ScreenClient> _conns = new();
    private readonly object _gate = new();

    private readonly TextBox _log;
    private readonly TextBox _input;
    private readonly CheckBox _expectReply;
    private readonly Button _send;

    public MessageForm(TargetSet targets, NetcSettings settings, byte[] key)
    {
        _settings = settings;
        _key = key;

        Text = "Сообщение (" + targets.Initial.Count + " ПК)";
        ClientSize = new Size(660, 460);
        MinimumSize = new Size(520, 380);
        StartPosition = FormStartPosition.CenterParent;

        // Журнал занимает всё пространство между полосой выбора и панелью ввода.
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

        // Нижняя панель: подпись, поле ввода, «Ждать ответ» и «Отправить».
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 132 };

        _input = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Segoe UI", 10F)
        };

        var lblInput = new Label
        {
            Text = "Сообщение ученику:",
            Dock = DockStyle.Top,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var right = new Panel { Dock = DockStyle.Right, Width = 180 };
        _expectReply = new CheckBox
        {
            Text = "Ждать ответ",
            Location = new Point(6, 30),
            AutoSize = true,
            Checked = true
        };
        _send = new Button
        {
            Text = "Отправить",
            Bounds = new Rectangle(6, 62, 160, 52)
        };
        _send.Click += async (_, _) => await SendAsync();
        right.Controls.AddRange(new Control[] { _expectReply, _send });

        bottom.Controls.AddRange(new Control[] { _input, lblInput, right });

        Controls.Add(_log);
        Controls.Add(bottom);
        AcceptButton = _send;

        FormClosed += (_, _) => DisposeConnections();

        _targets = TargetsHelper.AddTargetsBar(this, targets);

        // Задаём после добавления контролов, иначе масштабируется только форма (наезды при высоком DPI).
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    private void Log(string message) =>
        UiSafe.Append(_log, DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);

    private async Task<ScreenClient?> EnsureAsync(string addr)
    {
        lock (_gate)
        {
            if (_conns.TryGetValue(addr, out var existing))
            {
                if (existing.IsConnected) return existing;
                // Соединение упало — переподключаемся.
                try { existing.Dispose(); } catch { }
                _conns.Remove(addr);
            }
        }

        var client = new ScreenClient(addr, _settings.ControlPort, _key);
        client.MessageReceived += msg => LogSafe($"[{addr}] ответ: {msg.Text}");
        try
        {
            await client.ConnectAsync();
            lock (_gate) { _conns[addr] = client; }
            return client;
        }
        catch (Exception ex)
        {
            client.Dispose();
            Log($"{addr}: ошибка подключения — {ex.Message}");
            return null;
        }
    }

    private void LogSafe(string message)
    {
        try { BeginInvoke((Action)(() => Log(message))); }
        catch { }
    }

    private async Task SendAsync()
    {
        string text = _input.Text.Trim();
        if (text.Length == 0) return;

        UiSafe.SetEnabled(_send, false);
        var msg = new ChatMessage
        {
            From = Environment.MachineName,
            Text = text,
            ExpectReply = _expectReply.Checked
        };

        int ok = 0;
        foreach (string addr in _targets.Selected)
        {
            var client = await EnsureAsync(addr);
            if (client is null) continue;
            try
            {
                await client.SendMessageAsync(msg);
                ok++;
                Log($"{addr}: отправлено");
            }
            catch (Exception ex)
            {
                Log($"{addr}: ошибка — {ex.Message}");
            }
        }
        Log($"Отправлено на {ok}/{_targets.Selected.Count}");
        UiSafe.IfAlive(_input, () => _input.Clear());
        UiSafe.SetEnabled(_send, true);
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
