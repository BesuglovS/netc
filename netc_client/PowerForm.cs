using System.Drawing;
using NetcShared;

namespace NetcClient;

/// <summary>Управление питанием выбранных ученических ПК.</summary>
public sealed class PowerForm : Form
{
    private readonly TargetsSelector _targets;
    private readonly NetcSettings _settings;
    private readonly byte[] _key;

    private readonly RadioButton _shutdown;
    private readonly RadioButton _restart;
    private readonly RadioButton _logoff;
    private readonly CheckBox _noDelay;
    private readonly NumericUpDown _delay;
    private readonly TextBox _log;
    private readonly Button _apply;

    public PowerForm(TargetSet targets, NetcSettings settings, byte[] key)
    {
        _settings = settings;
        _key = key;

        Text = "Питание компьютеров (" + targets.Initial.Count + ")";
        ClientSize = new Size(500, 340);
        MinimumSize = new Size(440, 320);
        StartPosition = FormStartPosition.CenterParent;

        var group = new GroupBox
        {
            Text = "Команда",
            Bounds = new Rectangle(12, 8, 476, 116),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _shutdown = new RadioButton { Text = "Выключить", Location = new Point(16, 24), AutoSize = true, Checked = true };
        _restart = new RadioButton { Text = "Перезагрузить", Location = new Point(16, 50), AutoSize = true };
        _logoff = new RadioButton { Text = "Завершить сеанс", Location = new Point(16, 76), AutoSize = true };
        group.Controls.AddRange(new Control[] { _shutdown, _restart, _logoff });

        var lblDelay = new Label { Text = "Задержка, сек:", Location = new Point(220, 26), AutoSize = true };
        _delay = new NumericUpDown { Location = new Point(320, 22), Width = 80, Minimum = 0, Maximum = 600, Value = 10 };
        _noDelay = new CheckBox { Text = "Сейчас (0)", Location = new Point(220, 56), AutoSize = true };
        _noDelay.CheckedChanged += (_, _) =>
        {
            _delay.Enabled = !_noDelay.Checked;
        };
        group.Controls.AddRange(new Control[] { lblDelay, _delay, _noDelay });

        _apply = new Button
        {
            Text = "Применить",
            Bounds = new Rectangle(12, 132, 160, 34),
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        _apply.Click += async (_, _) => await ApplyAsync();

        _log = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9F),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.Gainsboro,
            Bounds = new Rectangle(12, 176, 476, 152),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        Controls.AddRange(new Control[] { group, _apply, _log });

        _targets = TargetsHelper.AddTargetsBar(this, targets);

        // Задаём после добавления контролов, иначе масштабируется только форма (наезды при высоком DPI).
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    private void Log(string message) =>
        UiSafe.Append(_log, DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);

    private async Task ApplyAsync()
    {
        PowerAction action = _restart.Checked ? PowerAction.Restart
            : _logoff.Checked ? PowerAction.Logoff
            : PowerAction.Shutdown;
        int delay = _noDelay.Checked ? 0 : (int)_delay.Value;

        var targets = _targets.Selected;
        if (targets.Count == 0) { Log("Не выбрано ни одного ПК."); return; }

        if (MessageBox.Show(this,
                $"Выполнить команду «{Describe(action)}» на {targets.Count} ПК?",
                "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        UiSafe.SetEnabled(_apply, false);
        int ok = 0;
        foreach (string addr in targets)
        {
            using var client = new ScreenClient(addr, _settings.ControlPort, _key);
            try
            {
                await client.ConnectAsync();
                await client.SendPowerAsync(action, delay);
                ok++;
                Log($"{addr}: команда отправлена ({Describe(action)})");
            }
            catch (Exception ex)
            {
                Log($"{addr}: ошибка — {ex.Message}");
            }
        }
        Log($"Готово: {ok}/{targets.Count}");
        UiSafe.SetEnabled(_apply, true);
    }

    private static string Describe(PowerAction action) => action switch
    {
        PowerAction.Restart => "перезагрузка",
        PowerAction.Logoff => "завершение сеанса",
        _ => "выключение"
    };
}
