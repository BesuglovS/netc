using System.Drawing;
using System.Text;
using NetcShared;

namespace NetcClient;

/// <summary>Просмотр журнала событий агентов.</summary>
public sealed class JournalForm : Form
{
    private readonly TargetsSelector _targets;
    private readonly NetcSettings _settings;
    private readonly byte[] _key;
    private readonly TextBox _text;
    private readonly Button _refresh;

    public JournalForm(TargetSet targets, NetcSettings settings, byte[] key)
    {
        _settings = settings;
        _key = key;

        Text = "Журнал (" + targets.Initial.Count + " ПК)";
        ClientSize = new Size(760, 560);
        MinimumSize = new Size(560, 400);
        StartPosition = FormStartPosition.CenterParent;

        _refresh = new Button { Text = "Запросить журнал", Bounds = new Rectangle(12, 10, 170, 32), Anchor = AnchorStyles.Top | AnchorStyles.Left };
        _refresh.Click += async (_, _) => await RefreshAsync();

        _text = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 9F),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.Gainsboro,
            Bounds = new Rectangle(12, 52, 736, 496),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        Controls.AddRange(new Control[] { _refresh, _text });
        Shown += async (_, _) => await RefreshAsync();

        _targets = TargetsHelper.AddTargetsBar(this, targets);

        // Задаём после добавления контролов, иначе масштабируется только форма (наезды при высоком DPI).
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    private async Task RefreshAsync()
    {
        UiSafe.SetEnabled(_refresh, false);
        UiSafe.IfAlive(_text, () => _text.Clear());

        var addrs = _targets.Selected.ToList();
        var results = new string[addrs.Count];
        using var sem = new SemaphoreSlim(6);
        var tasks = addrs.Select(async (addr, idx) =>
        {
            await sem.WaitAsync();
            try
            {
                using var client = new ScreenClient(addr, _settings.ControlPort, _key);
                try
                {
                    var entries = await client.RequestJournalAsync(8000, CancellationToken.None);
                    if (entries is null)
                    {
                        results[idx] = $"{addr}: нет ответа{Environment.NewLine}{Environment.NewLine}";
                        return;
                    }

                    var sb = new StringBuilder();
                    sb.Append($"=== {addr}  (записей: {entries.Count}) ==={Environment.NewLine}");
                    foreach (var e in entries.OrderBy(e => e.TimeUtc))
                    {
                        string time = e.TimeUtc.ToLocalTime().ToString("dd.MM HH:mm:ss");
                        sb.Append($"{time}  [{e.Category}] {e.Message}{Environment.NewLine}");
                    }
                    sb.Append(Environment.NewLine);
                    results[idx] = sb.ToString();
                }
                catch (Exception ex)
                {
                    results[idx] = $"{addr}: ошибка — {ex.Message}{Environment.NewLine}{Environment.NewLine}";
                }
            }
            finally { sem.Release(); }
        }).ToList();
        await Task.WhenAll(tasks);

        UiSafe.IfAlive(_text, () =>
        {
            foreach (var r in results)
            {
                if (!string.IsNullOrEmpty(r)) UiSafe.AppendBounded(_text, r);
            }
            _text.SelectionStart = _text.TextLength;
            _text.ScrollToCaret();
        });
        UiSafe.SetEnabled(_refresh, true);
    }
}
