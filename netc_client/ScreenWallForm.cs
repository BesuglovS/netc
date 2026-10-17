using System.Drawing;
using NetcShared;

namespace NetcClient;

/// <summary>Стена миниатюр экранов учеников.</summary>
public sealed class ScreenWallForm : Form
{
    private readonly NetcSettings _settings;
    private readonly byte[] _key;
    private readonly List<ScreenClient> _clients = new();
    private readonly IReadOnlyDictionary<string, string> _names;

    public ScreenWallForm(IReadOnlyList<string> addresses, IReadOnlyDictionary<string, string> names,
        NetcSettings settings, byte[] key)
    {
        _settings = settings;
        _key = key;
        _names = names;

        Text = "Экраны учеников";
        ClientSize = new Size(1120, 680);
        StartPosition = FormStartPosition.CenterParent;

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Padding = new Padding(6),
            WrapContents = true
        };
        Controls.Add(flow);

        foreach (string addr in addresses)
        {
            var tile = new Panel
            {
                Width = 344,
                Height = 224,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(6)
            };
            var pic = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Black
            };
            var lbl = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 26,
                AutoSize = false,
                AutoEllipsis = true,
                Text = Caption(addr),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(40, 40, 40),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(4, 0, 4, 0)
            };
            tile.Controls.Add(pic);
            tile.Controls.Add(lbl);

            EventHandler dbl = (_, _) => OpenControl(addr);
            pic.DoubleClick += dbl;
            tile.DoubleClick += dbl;

            flow.Controls.Add(tile);

            var client = new ScreenClient(addr, settings.ControlPort, key);
            client.FrameReceived += bytes =>
            {
                var bmp = ImageUtil.DecodeJpeg(bytes);
                if (bmp is null) return;
                try
                {
                    pic.BeginInvoke((Action)(() =>
                    {
                        var old = pic.Image;
                        pic.Image = bmp;
                        old?.Dispose();
                    }));
                }
                catch { bmp.Dispose(); }
            };
            _clients.Add(client);
            _ = StartClientSafeAsync(client, addr, lbl);
        }

        FormClosed += (_, _) =>
        {
            foreach (var c in _clients) c.Dispose();
        };

        // Задаём после добавления контролов, иначе масштабируется только форма (наезды при высоком DPI).
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    /// <summary>Подпись плитки: «Имя ПК (адрес)», а если имя неизвестно — только адрес.</summary>
    private string Caption(string addr)
    {
        return _names.TryGetValue(addr, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name + "  (" + addr + ")"
            : addr;
    }

    /// <summary>Держит плитку живой: подключается, стримит и переподключается при обрыве.</summary>
    private async Task StartClientSafeAsync(ScreenClient client, string addr, Label lbl)
    {
        while (!IsDisposed)
        {
            // Подписываемся на обрыв ДО подключения: иначе быстрый разрыв сразу после хендшейка
            // останется незамеченным и плитка «застынет» навсегда.
            var closed = new TaskCompletionSource();
            void Handler(bool ok) { if (!ok) closed.TrySetResult(); }
            client.ConnectionChanged += Handler;
            try
            {
                await client.StartScreenAsync(320, 40, 1);
                SetCaption(lbl, Caption(addr));

                // Подключение могло упасть между StartScreen и проверкой — не ждём событие вечно.
                if (!client.IsConnected) closed.TrySetResult();
                await closed.Task;
            }
            catch
            {
                SetCaption(lbl, Caption(addr) + " — нет связи");
            }
            finally
            {
                client.ConnectionChanged -= Handler;
            }

            if (IsDisposed) break;
            try { await Task.Delay(3000); } catch { break; }
        }
    }

    private void SetCaption(Label lbl, string text)
    {
        try
        {
            if (lbl.IsHandleCreated) lbl.BeginInvoke((Action)(() => lbl.Text = text));
        }
        catch { }
    }

    private void OpenControl(string addr)
    {
        var form = new ControlForm(addr, _settings, _key);
        form.Show(this);
    }
}
