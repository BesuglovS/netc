using System.Drawing;
using NetcShared;

namespace NetcClient;

/// <summary>Передача файлов: учитель → ученик и ученик → учитель.</summary>
public sealed class FileForm : Form
{
    private const int TimeoutMs = 120000;

    private readonly TargetsSelector _targets;
    private readonly NetcSettings _settings;
    private readonly byte[] _key;

    private readonly TextBox _sendPath;
    private readonly Button _sendButton;
    private readonly TextBox _remotePath;
    private readonly Label _saveDirLabel;
    private readonly Button _receiveButton;
    private readonly TextBox _log;

    private string? _saveDir;

    public FileForm(TargetSet targets, NetcSettings settings, byte[] key)
    {
        _settings = settings;
        _key = key;

        Text = "Файлы (" + targets.Initial.Count + " ПК)";
        ClientSize = new Size(700, 520);
        MinimumSize = new Size(560, 440);
        StartPosition = FormStartPosition.CenterParent;

        // --- отправка учитель → ученик ---
        var grpSend = new GroupBox
        {
            Text = "Отправить файл ученикам",
            Bounds = new Rectangle(12, 8, 676, 96),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _sendPath = new TextBox
        {
            ReadOnly = true,
            Location = new Point(16, 26),
            Width = 520,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        var btnPick = new Button { Text = "Выбрать файл…", Location = new Point(544, 24), Width = 118, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        btnPick.Click += (_, _) => PickFile();
        _sendButton = new Button { Text = "Отправить", Location = new Point(16, 58), Width = 140, Height = 28 };
        _sendButton.Click += async (_, _) => await SendAsync();
        grpSend.Controls.AddRange(new Control[] { _sendPath, btnPick, _sendButton });

        // --- получение ученик → учитель ---
        var grpRecv = new GroupBox
        {
            Text = "Забрать файл с учеников",
            Bounds = new Rectangle(12, 112, 676, 128),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        var lblRemote = new Label { Text = "Путь на ученике:", Location = new Point(16, 26), AutoSize = true };
        _remotePath = new TextBox
        {
            Location = new Point(150, 24),
            Width = 386,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Text = @"C:\Users\student\Desktop\"
        };
        var btnSaveDir = new Button { Text = "Папка сохранения…", Location = new Point(544, 24), Width = 118, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        btnSaveDir.Click += (_, _) => PickSaveDir();
        _saveDirLabel = new Label { Text = "Папка не выбрана", Location = new Point(16, 56), AutoSize = true, ForeColor = Color.DimGray };
        _receiveButton = new Button { Text = "Забрать", Location = new Point(16, 84), Width = 140, Height = 28 };
        _receiveButton.Click += async (_, _) => await ReceiveAsync();
        grpRecv.Controls.AddRange(new Control[] { lblRemote, _remotePath, btnSaveDir, _saveDirLabel, _receiveButton });

        // --- журнал ---
        _log = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9F),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.Gainsboro,
            Bounds = new Rectangle(12, 248, 676, 260),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        Controls.AddRange(new Control[] { grpSend, grpRecv, _log });

        _targets = TargetsHelper.AddTargetsBar(this, targets);

        // Задаём после добавления контролов, иначе масштабируется только форма (наезды при высоком DPI).
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    private void Log(string message) =>
        UiSafe.Append(_log, DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);

    private void PickFile()
    {
        using var dlg = new OpenFileDialog { Title = "Файл для отправки" };
        if (dlg.ShowDialog(this) == DialogResult.OK) _sendPath.Text = dlg.FileName;
    }

    private void PickSaveDir()
    {
        using var dlg = new FolderBrowserDialog { Description = "Куда сохранять полученные файлы" };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _saveDir = dlg.SelectedPath;
            _saveDirLabel.Text = _saveDir;
        }
    }

    private async Task SendAsync()
    {
        string path = _sendPath.Text;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Log("Сначала выберите файл.");
            return;
        }

        UiSafe.SetEnabled(_sendButton, false);
        var size = new FileInfo(path).Length;
        Log($"Отправка «{Path.GetFileName(path)}» ({FormatSize(size)})…");

        int ok = 0;
        foreach (string addr in _targets.Selected)
        {
            using var client = new ScreenClient(addr, _settings.ControlPort, _key);
            try
            {
                using var cts = new CancellationTokenSource(TimeoutMs + 5000);
                var result = await client.SendFileAsync(path, TimeoutMs, cts.Token);
                if (result.Ok) { ok++; Log($"{addr}: OK — {result.Message} ({result.Path})"); }
                else Log($"{addr}: не удалось — {result.Message}");
            }
            catch (Exception ex)
            {
                Log($"{addr}: ошибка — {ex.Message}");
            }
        }
        Log($"Готово: {ok}/{_targets.Selected.Count}");
        UiSafe.SetEnabled(_sendButton, true);
    }

    private async Task ReceiveAsync()
    {
        string remote = _remotePath.Text.Trim();
        if (remote.Length == 0) { Log("Укажите путь к файлу на ученике."); return; }
        if (string.IsNullOrEmpty(_saveDir)) { Log("Выберите папку сохранения."); return; }

        UiSafe.SetEnabled(_receiveButton, false);
        Log($"Получение «{remote}»…");

        int ok = 0;
        foreach (string addr in _targets.Selected)
        {
            using var client = new ScreenClient(addr, _settings.ControlPort, _key);
            try
            {
                using var cts = new CancellationTokenSource(TimeoutMs + 5000);
                var result = await client.RequestFileAsync(remote, _saveDir, TimeoutMs, cts.Token);
                if (result.Ok) { ok++; Log($"{addr}: OK — {result.Path}"); }
                else Log($"{addr}: не удалось — {result.Message}");
            }
            catch (Exception ex)
            {
                Log($"{addr}: ошибка — {ex.Message}");
            }
        }
        Log($"Готово: {ok}/{_targets.Selected.Count}");
        UiSafe.SetEnabled(_receiveButton, true);
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "Б", "КБ", "МБ", "ГБ" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return v.ToString(u == 0 ? "0" : "0.0") + " " + units[u];
    }
}
