using System.Drawing;
using NetcShared;

namespace NetcClient;

/// <summary>Ограничение приложений, интернета и фильтрация сайтов по спискам на выбранных ПК.</summary>
public sealed class RestrictionsForm : Form
{
    private const int ContentWidth = 596;
    private const int ContentHeight = 684;

    private readonly TargetsSelector _targets;
    private readonly NetcSettings _settings;
    private readonly byte[] _key;

    private readonly RadioButton _off;
    private readonly RadioButton _blacklist;
    private readonly RadioButton _whitelist;
    private readonly TextBox _names;
    private readonly Button _applyApps;
    private readonly CheckBox _killBrowsers;

    private readonly RadioButton _siteOff;
    private readonly RadioButton _siteBlack;
    private readonly RadioButton _siteWhite;
    private readonly TextBox _sites;
    private readonly Button _applySites;

    private readonly TextBox _log;

    public RestrictionsForm(TargetSet targets, NetcSettings settings, byte[] key)
    {
        _settings = settings;
        _key = key;

        Text = "Ограничения (" + targets.Initial.Count + " ПК)";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(560, 380);

        // Содержимое — в прокручиваемой панели: на низких экранах нижние элементы
        // доступны прокруткой, а кнопки действий остаются в видимой области.
        var content = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = SystemColors.Control
        };

        // --- Ограничение приложений ---
        var grpApps = new GroupBox
        {
            Text = "Ограничение приложений",
            Bounds = new Rectangle(12, 8, ContentWidth, 186)
        };
        _off = new RadioButton { Text = "Выключено", Location = new Point(16, 18), AutoSize = true, Checked = true };
        _blacklist = new RadioButton { Text = "Чёрный список (запрещать перечисленные)", Location = new Point(16, 40), AutoSize = true };
        _whitelist = new RadioButton { Text = "Белый список (разрешать только перечисленные)", Location = new Point(16, 62), AutoSize = true };
        var lblNames = new Label { Text = "Имена процессов (без .exe), по одному в строке:", Location = new Point(16, 86), AutoSize = true };
        _names = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9F),
            Bounds = new Rectangle(16, 106, 396, 62),
            Text = "notepad\r\ncalc\r\nmspaint"
        };
        _applyApps = new Button { Text = "Применить", Bounds = new Rectangle(420, 106, 160, 62) };
        _applyApps.Click += async (_, _) => await ApplyAppsAsync();
        grpApps.Controls.AddRange(new Control[] { _off, _blacklist, _whitelist, lblNames, _names, _applyApps });

        // --- Ограничение интернета (полностью) ---
        var grpNet = new GroupBox
        {
            Text = "Ограничение интернета (полностью)",
            Bounds = new Rectangle(12, 202, ContentWidth, 76)
        };
        _killBrowsers = new CheckBox { Text = "Закрыть браузеры", Location = new Point(16, 18), AutoSize = true, Checked = true };
        var btnBlock = new Button { Text = "Заблокировать", Bounds = new Rectangle(16, 44, 150, 26) };
        btnBlock.Click += async (_, _) => await ApplyInternetAsync(true);
        var btnAllow = new Button { Text = "Разрешить", Bounds = new Rectangle(176, 44, 150, 26) };
        btnAllow.Click += async (_, _) => await ApplyInternetAsync(false);
        grpNet.Controls.AddRange(new Control[] { _killBrowsers, btnBlock, btnAllow });

        // --- Фильтр сайтов по спискам ---
        var grpSites = new GroupBox
        {
            Text = "Фильтр сайтов (по домену, через локальный прокси)",
            Bounds = new Rectangle(12, 286, ContentWidth, 194)
        };
        _siteOff = new RadioButton { Text = "Выключено", Location = new Point(16, 18), AutoSize = true, Checked = true };
        _siteBlack = new RadioButton { Text = "Чёрный список (запрещать перечисленные сайты)", Location = new Point(16, 40), AutoSize = true };
        _siteWhite = new RadioButton { Text = "Белый список (разрешать только перечисленные)", Location = new Point(16, 62), AutoSize = true };
        var lblSites = new Label
        {
            Text = "Домены (по одному в строке; поддомены учитываются автоматически, напр. example.com):",
            Location = new Point(16, 86),
            AutoSize = true
        };
        _sites = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9F),
            Bounds = new Rectangle(16, 106, 396, 62),
            Text = "nayanovaacademy.ru"
        };
        _applySites = new Button { Text = "Применить фильтр сайтов", Bounds = new Rectangle(420, 106, 160, 62) };
        _applySites.Click += async (_, _) => await ApplySitesAsync();
        var lblSitesHint = new Label
        {
            Text = "Работает через системный прокси (браузеры); локальная сеть не блокируется.",
            Location = new Point(16, 174),
            AutoSize = true,
            ForeColor = Color.DimGray
        };
        grpSites.Controls.AddRange(new Control[] { _siteOff, _siteBlack, _siteWhite, lblSites, _sites, _applySites, lblSitesHint });

        // --- Защита автозагрузки ---
        var grpProt = new GroupBox
        {
            Text = "Защита автозагрузки ученика",
            Bounds = new Rectangle(12, 488, ContentWidth, 84)
        };
        var btnClean = new Button { Text = "Проверить и очистить", Bounds = new Rectangle(16, 22, 190, 28) };
        btnClean.Click += async (_, _) => await ScanAutostartAsync(true);
        var btnCheck = new Button { Text = "Только проверить", Bounds = new Rectangle(216, 22, 160, 28) };
        btnCheck.Click += async (_, _) => await ScanAutostartAsync(false);
        var lblProt = new Label
        {
            Text = "Скрипты в автозагрузке ученика и записи Run/RunOnce с интерпретаторами\nперемещаются в карантин (%LOCALAPPDATA%\\netc\\quarantine).",
            Location = new Point(16, 54),
            AutoSize = true
        };
        grpProt.Controls.AddRange(new Control[] { btnClean, btnCheck, lblProt });

        // --- Журнал ---
        _log = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9F),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.Gainsboro,
            Bounds = new Rectangle(12, 580, ContentWidth, 96)
        };

        content.Controls.AddRange(new Control[] { grpApps, grpNet, grpSites, grpProt, _log });
        Controls.Add(content);

        _targets = TargetsHelper.AddTargetsBar(this, targets);

        // Высота окна — по содержимому, но не выше рабочего стола (иначе появится прокрутка).
        var wa = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1024, 768);
        int desired = ContentHeight + 12 + _targets.Height + 8;
        ClientSize = new Size(ContentWidth + 24, Math.Max(360, Math.Min(desired, wa.Height - 60)));

        // Задаём после добавления контролов, иначе масштабируется только форма (наезды при высоком DPI).
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    private void Log(string message) =>
        UiSafe.Append(_log, DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);

    private List<string> ParseNames()
    {
        var result = new List<string>();
        foreach (var part in _names.Text.Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = part.Trim();
            if (t.Length > 0) result.Add(t);
        }
        return result;
    }

    private List<string> ParseSites()
    {
        var result = new List<string>();
        foreach (var part in _sites.Text.Split(new[] { '\r', '\n', ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = SiteFilterCore.Normalize(part);
            if (t.Length > 0 && !result.Contains(t, StringComparer.Ordinal)) result.Add(t);
        }
        return result;
    }

    private async Task ApplyAppsAsync()
    {
        var mode = _whitelist.Checked ? AppControlMode.Whitelist
            : _blacklist.Checked ? AppControlMode.Blacklist
            : AppControlMode.Off;
        var names = ParseNames();

        UiSafe.SetEnabled(_applyApps, false);
        int ok = 0;
        foreach (string addr in _targets.Selected)
        {
            using var client = new ScreenClient(addr, _settings.ControlPort, _key);
            try
            {
                await client.ConnectAsync();
                await client.SendAppPolicyAsync(mode, names);
                ok++;
                Log($"{addr}: применено ({Describe(mode)}, позиций {names.Count})");
            }
            catch (Exception ex) { Log($"{addr}: ошибка — {ex.Message}"); }
        }
        Log($"Готово: {ok}/{_targets.Selected.Count}");
        UiSafe.SetEnabled(_applyApps, true);
    }

    private async Task ApplyInternetAsync(bool blocked)
    {
        var policy = new InternetPolicyMessage { Blocked = blocked, KillBrowsers = _killBrowsers.Checked };
        int ok = 0;
        foreach (string addr in _targets.Selected)
        {
            using var client = new ScreenClient(addr, _settings.ControlPort, _key);
            try
            {
                await client.ConnectAsync();
                await client.SendInternetAsync(policy);
                ok++;
                Log($"{addr}: интернет {(blocked ? "заблокирован" : "разрешён")}");
            }
            catch (Exception ex) { Log($"{addr}: ошибка — {ex.Message}"); }
        }
        Log($"Готово: {ok}/{_targets.Selected.Count}");
    }

    private async Task ApplySitesAsync()
    {
        var mode = _siteWhite.Checked ? SiteFilterMode.Whitelist
            : _siteBlack.Checked ? SiteFilterMode.Blacklist
            : SiteFilterMode.Off;
        var sites = ParseSites();

        if (mode != SiteFilterMode.Off && sites.Count == 0)
        {
            Log("Укажите хотя бы один домен (или выберите «Выключено»).");
            return;
        }

        var policy = new InternetPolicyMessage { Blocked = false, KillBrowsers = false, SiteMode = mode, Sites = sites };

        UiSafe.SetEnabled(_applySites, false);
        int ok = 0;
        foreach (string addr in _targets.Selected)
        {
            using var client = new ScreenClient(addr, _settings.ControlPort, _key);
            try
            {
                await client.ConnectAsync();
                await client.SendInternetAsync(policy);
                ok++;
                Log($"{addr}: {DescribeSites(mode, sites.Count)}");
            }
            catch (Exception ex) { Log($"{addr}: ошибка — {ex.Message}"); }
        }
        Log($"Готово: {ok}/{_targets.Selected.Count}");
        UiSafe.SetEnabled(_applySites, true);
    }

    private async Task ScanAutostartAsync(bool quarantine)
    {
        int ok = 0;
        foreach (string addr in _targets.Selected)
        {
            using var client = new ScreenClient(addr, _settings.ControlPort, _key);
            try
            {
                var rep = await client.ScanAutostartAsync(quarantine, 15000, CancellationToken.None);
                if (rep is null) { Log($"{addr}: нет ответа"); continue; }
                ok++;
                if (rep.Findings.Count == 0) Log($"{addr}: автозагрузка чистая");
                else
                {
                    Log($"{addr}: найдено {rep.Findings.Count}, удалено {rep.Removed}");
                    foreach (var f in rep.Findings)
                        Log($"    [{f.Location}] {f.Name} — {f.Detail} {(f.Removed ? "(в карантин)" : "(не удалено)")}");
                }
            }
            catch (Exception ex) { Log($"{addr}: ошибка — {ex.Message}"); }
        }
        Log($"Готово: {ok}/{_targets.Selected.Count}");
    }

    private static string Describe(AppControlMode mode) => mode switch
    {
        AppControlMode.Blacklist => "чёрный список",
        AppControlMode.Whitelist => "белый список",
        _ => "выключено"
    };

    private static string DescribeSites(SiteFilterMode mode, int count) => mode switch
    {
        SiteFilterMode.Blacklist => $"фильтр сайтов: чёрный список, доменов {count}",
        SiteFilterMode.Whitelist => $"фильтр сайтов: белый список, доменов {count}",
        _ => "фильтр сайтов выключен"
    };
}
