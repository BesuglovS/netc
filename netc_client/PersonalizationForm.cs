using System.Drawing;
using NetcShared;

namespace NetcClient;

/// <summary>Централизованная персонализация ученических ПК: обои, экран блокировки, тема, акцент.</summary>
public sealed class PersonalizationForm : Form
{
    private const int FileTimeoutMs = 120000;

    private readonly TargetsSelector _targets;
    private readonly NetcSettings _settings;
    private readonly byte[] _key;

    private string? _wallpaper;
    private string? _lockScreen;
    private int _accent = 0x0078D4;

    private readonly TextBox _wallpaperPath;
    private readonly ComboBox _style;
    private readonly TextBox _lockPath;
    private readonly ComboBox _theme;
    private readonly CheckBox _accentOn;
    private readonly Button _accentButton;
    private readonly Button _applyWall;
    private readonly Button _applyLock;
    private readonly Button _applyTheme;
    private readonly Button _applyAccent;
    private readonly Button _reset;
    private readonly Button _applyReset;
    private readonly TextBox _log;

    public PersonalizationForm(TargetSet targets, NetcSettings settings, byte[] key)
    {
        _settings = settings;
        _key = key;

        Text = "Персонализация (" + targets.Initial.Count + " ПК)";
        ClientSize = new Size(680, 560);
        MinimumSize = new Size(560, 480);
        StartPosition = FormStartPosition.CenterParent;

        // --- Обои ---
        var gbWall = new GroupBox { Text = "Обои", Bounds = new Rectangle(12, 8, 656, 96), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        _wallpaperPath = new TextBox { ReadOnly = true, Location = new Point(136, 26), Width = 344, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        var btnW = new Button { Text = "Картинка…", Location = new Point(16, 24), Width = 110 };
        btnW.Click += (_, _) => { var p = PickImage("Картинка обоев"); if (p is not null) { _wallpaper = p; _wallpaperPath.Text = p; } };
        _applyWall = new Button { Text = "Применить обои", Location = new Point(492, 24), Width = 152, Height = 28, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        _applyWall.Click += async (_, _) => await ApplyAsync(wallpaper: true, lockScreen: false, theme: false, accent: false);
        var lblStyle = new Label { Text = "Режим:", Location = new Point(16, 60), AutoSize = true };
        _style = new ComboBox { Location = new Point(90, 56), Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
        _style.Items.AddRange(new object[] { "Заполнить", "По размеру", "Растянуть", "По центру", "Замостить" });
        _style.SelectedIndex = 0;
        gbWall.Controls.AddRange(new Control[] { btnW, _wallpaperPath, _applyWall, lblStyle, _style });

        // --- Экран блокировки ---
        var gbLock = new GroupBox { Text = "Экран блокировки", Bounds = new Rectangle(12, 112, 656, 64), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        _lockPath = new TextBox { ReadOnly = true, Location = new Point(136, 26), Width = 344, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        var btnL = new Button { Text = "Картинка…", Location = new Point(16, 24), Width = 110 };
        btnL.Click += (_, _) => { var p = PickImage("Картинка экрана блокировки"); if (p is not null) { _lockScreen = p; _lockPath.Text = p; } };
        _applyLock = new Button { Text = "Применить", Location = new Point(492, 23), Width = 152, Height = 29, Anchor = AnchorStyles.Top | AnchorStyles.Right };
        _applyLock.Click += async (_, _) => await ApplyAsync(wallpaper: false, lockScreen: true, theme: false, accent: false);
        gbLock.Controls.AddRange(new Control[] { btnL, _lockPath, _applyLock });

        // --- Тема и акцент ---
        var gbTheme = new GroupBox { Text = "Тема и акцент", Bounds = new Rectangle(12, 184, 656, 120), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        var lblTheme = new Label { Text = "Тема:", Location = new Point(16, 28), AutoSize = true };
        _theme = new ComboBox { Location = new Point(90, 24), Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
        _theme.Items.AddRange(new object[] { "Не менять", "Тёмная", "Светлая" });
        _theme.SelectedIndex = 0;
        _applyTheme = new Button { Text = "Применить тему", Location = new Point(270, 22), Width = 170, Height = 30 };
        _applyTheme.Click += async (_, _) => await ApplyAsync(wallpaper: false, lockScreen: false, theme: true, accent: false);
        _accentButton = new Button { Text = "Цвет акцента…", Location = new Point(16, 62), Width = 150, Height = 30 };
        _accentButton.Click += (_, _) => PickAccent();
        _applyAccent = new Button { Text = "Применить акцент", Location = new Point(180, 62), Width = 180, Height = 30 };
        _applyAccent.Click += async (_, _) => await ApplyAsync(wallpaper: false, lockScreen: false, theme: false, accent: true);
        _accentOn = new CheckBox { Text = "Показывать акцент в панели задач и заголовках", Location = new Point(16, 98), AutoSize = true };
        gbTheme.Controls.AddRange(new Control[] { lblTheme, _theme, _applyTheme, _accentButton, _applyAccent, _accentOn });

        // --- Общие кнопки ---
        _reset = new Button { Text = "Сбросить выбор", Bounds = new Rectangle(12, 312, 170, 34) };
        _reset.Click += (_, _) => { _wallpaper = null; _lockScreen = null; _wallpaperPath.Clear(); _lockPath.Clear(); _style.SelectedIndex = 0; _theme.SelectedIndex = 0; };
        _applyReset = new Button { Text = "Сбросить к стандартным (светлая тема)", Bounds = new Rectangle(192, 312, 320, 34) };
        _applyReset.Click += async (_, _) => await ResetAsync();

        _log = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9F),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.Gainsboro,
            Bounds = new Rectangle(12, 354, 656, 194),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        Controls.AddRange(new Control[] { gbWall, gbLock, gbTheme, _reset, _applyReset, _log });
        UpdateAccentButton();

        _targets = TargetsHelper.AddTargetsBar(this, targets);

        // Задаём после добавления контролов, иначе масштабируется только форма (наезды при высоком DPI).
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    private void Log(string message) =>
        UiSafe.Append(_log, DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);

    private void SetBusy(bool busy)
    {
        UiSafe.SetEnabled(_applyWall, !busy);
        UiSafe.SetEnabled(_applyLock, !busy);
        UiSafe.SetEnabled(_applyTheme, !busy);
        UiSafe.SetEnabled(_applyAccent, !busy);
        UiSafe.SetEnabled(_reset, !busy);
        UiSafe.SetEnabled(_applyReset, !busy);
    }

    private string? PickImage(string title)
    {
        using var dlg = new OpenFileDialog
        {
            Title = title,
            Filter = "Изображения|*.jpg;*.jpeg;*.png;*.bmp|Все файлы|*.*"
        };
        return dlg.ShowDialog(this) == DialogResult.OK ? dlg.FileName : null;
    }

    private void PickAccent()
    {
        using var dlg = new ColorDialog { Color = Color.FromArgb((_accent >> 16) & 0xFF, (_accent >> 8) & 0xFF, _accent & 0xFF), FullOpen = true };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _accent = (dlg.Color.R << 16) | (dlg.Color.G << 8) | dlg.Color.B;
            UpdateAccentButton();
        }
    }

    private void UpdateAccentButton()
    {
        _accentButton.BackColor = Color.FromArgb((_accent >> 16) & 0xFF, (_accent >> 8) & 0xFF, _accent & 0xFF);
        _accentButton.ForeColor = _accentButton.BackColor.GetBrightness() < 0.5 ? Color.White : Color.Black;
        _accentButton.Text = "Цвет акцента…  #" + (_accent & 0xFFFFFF).ToString("X6");
    }

    private string StyleCode() => _style.SelectedIndex switch
    {
        1 => "fit",
        2 => "stretch",
        3 => "center",
        4 => "tile",
        _ => "fill"
    };

    /// <summary>Применяет только выбранные настройки персонализации к целевым ПК.</summary>
    private async Task ApplyAsync(bool wallpaper, bool lockScreen, bool theme, bool accent)
    {
        if (wallpaper && _wallpaper is null) { Log("Обои: сначала выберите картинку."); return; }
        if (lockScreen && _lockScreen is null) { Log("Экран блокировки: сначала выберите картинку."); return; }

        string? themeCode = null;
        if (theme)
        {
            themeCode = _theme.SelectedIndex == 1 ? "dark" : _theme.SelectedIndex == 2 ? "light" : null;
            if (themeCode is null) { Log("Тема: выберите «Тёмная» или «Светлая»."); return; }
        }

        var targets = _targets.Selected;
        if (targets.Count == 0) { Log("Не выбрано ни одного ПК."); return; }

        SetBusy(true);
        var summary = new List<string>();
        if (wallpaper) summary.Add("обои");
        if (lockScreen) summary.Add("лок-скрин");
        if (theme) summary.Add("тема");
        if (accent) summary.Add("акцент #" + (_accent & 0xFFFFFF).ToString("X6"));
        Log("Применяю: " + string.Join(", ", summary) + " — на " + targets.Count + " ПК…");

        int ok = 0;
        foreach (string addr in targets)
        {
            using var client = new ScreenClient(addr, _settings.ControlPort, _key);
            try
            {
                using var cts = new CancellationTokenSource(FileTimeoutMs + 30000);

                string? wp = null, lk = null;
                if (wallpaper)
                {
                    var r = await client.SendFileAsync(_wallpaper!, FileTimeoutMs, cts.Token);
                    if (r.Ok) wp = r.Path; else Log($"{addr}: обои не переданы — {r.Message}");
                }
                if (lockScreen)
                {
                    var r = await client.SendFileAsync(_lockScreen!, FileTimeoutMs, cts.Token);
                    if (r.Ok) lk = r.Path; else Log($"{addr}: лок-скрин не передан — {r.Message}");
                }

                var msg = new PersonalizationMessage
                {
                    WallpaperPath = wp,
                    WallpaperStyle = StyleCode(),
                    LockScreenPath = lk,
                    Theme = themeCode,
                    AccentColor = accent ? _accent : 0,
                    AccentOnStart = _accentOn.Checked
                };

                var res = await client.ApplyPersonalizationAsync(msg, 20000, cts.Token);
                if (res is null) Log($"{addr}: нет ответа");
                else { if (res.Ok) ok++; Log($"{addr}: {res.Message}"); }
            }
            catch (Exception ex) { Log($"{addr}: ошибка — {ex.Message}"); }
        }
        Log($"Готово: {ok}/{targets.Count}");
        SetBusy(false);
    }

    /// <summary>Сброс персонализации на целевых ПК к стандартным настройкам (светлая тема).</summary>
    private async Task ResetAsync()
    {
        var targets = _targets.Selected;
        if (targets.Count == 0) { Log("Не выбрано ни одного ПК."); return; }

        SetBusy(true);
        Log($"Сброс персонализации к стандартным (светлая тема) — на {targets.Count} ПК…");

        int ok = 0;
        foreach (string addr in targets)
        {
            using var client = new ScreenClient(addr, _settings.ControlPort, _key);
            try
            {
                using var cts = new CancellationTokenSource(20000);
                // Reset — новый агент сделает полный сброс; Theme/Accent оставлены для совместимости
                // со старым агентом (он сбросит хотя бы тему и акцент).
                var msg = new PersonalizationMessage { Reset = true, Theme = "light", AccentColor = 0x0078D4 };
                var res = await client.ApplyPersonalizationAsync(msg, 15000, cts.Token);
                if (res is null) Log($"{addr}: нет ответа");
                else { if (res.Ok) ok++; Log($"{addr}: {res.Message}"); }
            }
            catch (Exception ex) { Log($"{addr}: ошибка — {ex.Message}"); }
        }
        Log($"Готово: {ok}/{targets.Count}");
        SetBusy(false);
    }
}
