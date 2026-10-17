using System.Drawing;
using NetcShared;

namespace NetcClient;

/// <summary>Управление мониторами ученических ПК: погасить/включить подсветку без выключения ПК.</summary>
public sealed class MonitorForm : Form
{
    private readonly TargetsSelector _targets;
    private readonly NetcSettings _settings;
    private readonly byte[] _key;

    private readonly Button _off;
    private readonly Button _on;
    private readonly TextBox _log;

    public MonitorForm(TargetSet targets, NetcSettings settings, byte[] key)
    {
        _settings = settings;
        _key = key;

        Text = "Мониторы (" + targets.Initial.Count + " ПК)";
        ClientSize = new Size(580, 360);
        MinimumSize = new Size(480, 280);
        StartPosition = FormStartPosition.CenterParent;

        var hint = new Label
        {
            Text = "Гасит/включает подсветку мониторов штатной командой Windows. ПК продолжает работать;\n" +
                   "любое движение мыши или нажатие клавиши у ученика снова включает экран.",
            Bounds = new Rectangle(12, 8, 556, 44),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        _off = new Button { Text = "Выключить мониторы", Bounds = new Rectangle(12, 58, 240, 40) };
        _off.Click += async (_, _) => await ApplyAsync(true);
        _on = new Button { Text = "Включить мониторы", Bounds = new Rectangle(264, 58, 240, 40) };
        _on.Click += async (_, _) => await ApplyAsync(false);

        _log = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9F),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.Gainsboro,
            Bounds = new Rectangle(12, 108, 556, 240),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        Controls.AddRange(new Control[] { hint, _off, _on, _log });

        _targets = TargetsHelper.AddTargetsBar(this, targets);

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    private void Log(string message) =>
        UiSafe.Append(_log, DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);

    private async Task ApplyAsync(bool off)
    {
        var targets = _targets.Selected;
        if (targets.Count == 0) { Log("Не выбрано ни одного ПК."); return; }

        var title = off ? "Выключить" : "Включить";
        if (MessageBox.Show(this, $"{title} мониторы на {targets.Count} ПК?", "Подтверждение",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        UiSafe.SetEnabled(_off, false);
        UiSafe.SetEnabled(_on, false);

        int ok = 0;
        foreach (string addr in targets)
        {
            using var client = new ScreenClient(addr, _settings.ControlPort, _key);
            try
            {
                await client.ConnectAsync();
                await client.SendMonitorAsync(off);
                ok++;
                Log($"{addr}: {(off ? "мониторы выключены" : "мониторы включены")}");
            }
            catch (Exception ex) { Log($"{addr}: ошибка — {ex.Message}"); }
        }
        Log($"Готово: {ok}/{targets.Count}");

        UiSafe.SetEnabled(_off, true);
        UiSafe.SetEnabled(_on, true);
    }
}
