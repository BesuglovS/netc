using System.Drawing;
using NetcShared;

namespace NetcClient;

/// <summary>Закрытие всех окон и приложений (включая Проводник) на выбранных ученических ПК.</summary>
public sealed class CloseWindowsForm : Form
{
    private const int TimeoutMs = 20000;

    private readonly TargetsSelector _targets;
    private readonly NetcSettings _settings;
    private readonly byte[] _key;

    private readonly CheckBox _includeExplorer;
    private readonly CheckBox _force;
    private readonly Button _apply;
    private readonly TextBox _log;

    public CloseWindowsForm(TargetSet targets, NetcSettings settings, byte[] key)
    {
        _settings = settings;
        _key = key;

        Text = "Закрыть окна и приложения (" + targets.Initial.Count + " ПК)";
        ClientSize = new Size(580, 400);
        MinimumSize = new Size(480, 300);
        StartPosition = FormStartPosition.CenterParent;

        var grp = new GroupBox
        {
            Text = "Параметры",
            Bounds = new Rectangle(12, 8, 556, 116),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        _includeExplorer = new CheckBox
        {
            Text = "Закрывать окна Проводника (рабочий стол и панель задач не затрагиваются)",
            Location = new Point(16, 22),
            AutoSize = true,
            Checked = true
        };
        _force = new CheckBox
        {
            Text = "Принудительно завершать процессы, если окно не закрылось (в т.ч. запущенные от администратора)",
            Location = new Point(16, 50),
            AutoSize = true
        };
        var lblHint = new Label
        {
            Text = "Мягкое закрытие выполняет агент; принудительное завершение (включая админ-приложения) — служба-сторож.",
            Location = new Point(16, 78),
            AutoSize = true,
            ForeColor = Color.DimGray
        };
        grp.Controls.AddRange(new Control[] { _includeExplorer, _force, lblHint });

        _apply = new Button
        {
            Text = "Закрыть окна",
            Bounds = new Rectangle(12, 136, 190, 34),
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
            Bounds = new Rectangle(12, 180, 556, 196),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        Controls.AddRange(new Control[] { grp, _apply, _log });

        _targets = TargetsHelper.AddTargetsBar(this, targets);

        // Задаём после добавления контролов, иначе масштабируется только форма (наезды при высоком DPI).
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    private void Log(string message) =>
        UiSafe.Append(_log, DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);

    private async Task ApplyAsync()
    {
        var targets = _targets.Selected;
        if (targets.Count == 0) { Log("Не выбрано ни одного ПК."); return; }

        string forcedNote = _force.Checked ? " (с принудительным завершением)" : "";
        if (MessageBox.Show(this,
                $"Закрыть все окна и приложения на {targets.Count} ПК{forcedNote}?\n" +
                "Несохранённые данные могут быть потеряны.",
                "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        var req = new CloseAppsMessage
        {
            IncludeExplorer = _includeExplorer.Checked,
            Force = _force.Checked,
            GraceMs = 2500
        };

        UiSafe.SetEnabled(_apply, false);
        int ok = 0;
        foreach (string addr in targets)
        {
            using var client = new ScreenClient(addr, _settings.ControlPort, _key);
            try
            {
                var res = await client.CloseAppsAsync(req, TimeoutMs, CancellationToken.None);
                if (res is null) Log($"{addr}: нет ответа");
                else { ok++; Log($"{addr}: {res.Message}"); }
            }
            catch (Exception ex) { Log($"{addr}: ошибка — {ex.Message}"); }
        }
        Log($"Готово: {ok}/{targets.Count}");
        UiSafe.SetEnabled(_apply, true);
    }
}
