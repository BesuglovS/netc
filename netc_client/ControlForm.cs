using System.Drawing;
using NetcShared;

namespace NetcClient;

/// <summary>Окно удалённого управления одним ПК.</summary>
public sealed class ControlForm : Form
{
    private readonly ScreenClient _client;
    private readonly PictureBox _pic;
    private readonly Label _status;

    private int _frameW = 1;
    private int _frameH = 1;

    public ControlForm(string address, NetcSettings settings, byte[] key)
    {
        Text = "Управление: " + address;
        ClientSize = new Size(1000, 700);
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;

        _client = new ScreenClient(address, settings.ControlPort, key);

        _pic = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Black,
            TabStop = false
        };
        _status = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 22,
            Text = "Подключение…",
            TextAlign = ContentAlignment.MiddleLeft
        };
        Controls.Add(_pic);
        Controls.Add(_status);

        _client.FrameReceived += bytes =>
        {
            var bmp = ImageUtil.DecodeJpeg(bytes);
            if (bmp is null) return;
            _frameW = bmp.Width;
            _frameH = bmp.Height;
            try
            {
                _pic.BeginInvoke((Action)(() =>
                {
                    var old = _pic.Image;
                    _pic.Image = bmp;
                    old?.Dispose();
                }));
            }
            catch { bmp.Dispose(); }
        };
        _client.ConnectionChanged += ok =>
        {
            if (!ok) SetStatus("Связь потеряна — переподключение…");
        };

        _pic.MouseMove += (_, e) => SendMouse("move", e);
        _pic.MouseDown += (_, e) => SendMouse("down", e);
        _pic.MouseUp += (_, e) => SendMouse("up", e);
        _pic.MouseWheel += (_, e) => SendWheel(e);
        KeyDown += (_, e) => SendKey(e, true);
        KeyUp += (_, e) => SendKey(e, false);

        Shown += (_, _) => _ = StreamLoopAsync();
        FormClosed += (_, _) => _client.Dispose();

        // Задаём после добавления контролов, иначе масштабируется только форма (наезды при высоком DPI).
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    /// <summary>Поток экрана с авто-переподключением при обрыве.</summary>
    private async Task StreamLoopAsync()
    {
        while (!IsDisposed)
        {
            var closed = new TaskCompletionSource();
            void Handler(bool ok) { if (!ok) closed.TrySetResult(); }
            _client.ConnectionChanged += Handler;
            try
            {
                await _client.StartScreenAsync(1280, 55, 5);
                SetStatus("Подключено — можно управлять мышью и клавиатурой");
                if (!_client.IsConnected) closed.TrySetResult();
                await closed.Task;
            }
            catch (Exception ex)
            {
                SetStatus("Нет связи: " + ex.Message + " — переподключение…");
            }
            finally { _client.ConnectionChanged -= Handler; }

            if (IsDisposed) break;
            try { await Task.Delay(3000); } catch { break; }
        }
    }

    private void SetStatus(string text)
    {
        try
        {
            if (_status.IsDisposed) return;
            if (_status.IsHandleCreated && _status.InvokeRequired)
                _status.BeginInvoke((Action)(() => _status.Text = text));
            else
                _status.Text = text;
        }
        catch { }
    }

    private (int nx, int ny) Normalize(Point p)
    {
        int pw = Math.Max(1, _pic.ClientSize.Width);
        int ph = Math.Max(1, _pic.ClientSize.Height);
        double scale = Math.Min(pw / (double)_frameW, ph / (double)_frameH);
        int dw = Math.Max(1, (int)(_frameW * scale));
        int dh = Math.Max(1, (int)(_frameH * scale));
        int ox = (pw - dw) / 2;
        int oy = (ph - dh) / 2;

        double fx = (p.X - ox) / (double)dw;
        double fy = (p.Y - oy) / (double)dh;
        int nx = (int)(Math.Clamp(fx, 0, 1) * 65535);
        int ny = (int)(Math.Clamp(fy, 0, 1) * 65535);
        return (nx, ny);
    }

    private async void SendMouse(string action, MouseEventArgs e)
    {
        try
        {
            var (nx, ny) = Normalize(e.Location);
            string button = e.Button == MouseButtons.Right ? "right"
                : e.Button == MouseButtons.Middle ? "middle" : "left";
            await _client.SendInputAsync(new InputEvent
            {
                Type = "mouse",
                Action = action,
                Button = button,
                X = nx,
                Y = ny
            });
        }
        catch { /* связь потеряна — молча игнорируем ввод */ }
    }

    private async void SendWheel(MouseEventArgs e)
    {
        try
        {
            var (nx, ny) = Normalize(e.Location);
            await _client.SendInputAsync(new InputEvent
            {
                Type = "mouse",
                Action = "wheel",
                X = nx,
                Y = ny,
                Delta = e.Delta
            });
        }
        catch { }
    }

    private async void SendKey(KeyEventArgs e, bool down)
    {
        try
        {
            await _client.SendInputAsync(new InputEvent
            {
                Type = "key",
                Vk = (int)e.KeyCode,
                Down = down
            });
        }
        catch { }
    }
}
