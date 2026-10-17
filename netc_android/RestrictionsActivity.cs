using Android.App;
using Android.Views;
using Android.Widget;
using NetcShared;

namespace NetcAndroid;

/// <summary>Ограничения: приложения, интернет, фильтр сайтов и проверка автозагрузки.</summary>
[Activity(Label = "Ограничения", Theme = "@android:style/Theme.Material.Light.DarkActionBar")]
public sealed class RestrictionsActivity : ActionActivity
{
    private Spinner _appMode = null!;
    private EditText _appNames = null!;
    private CheckBox _block = null!;
    private CheckBox _killBrowsers = null!;
    private Spinner _siteMode = null!;
    private EditText _sites = null!;

    protected override void BuildContent(LinearLayout content)
    {
        // --- Приложения ---
        AddLabel(content, "Ограничение приложений (имена процессов без .exe):");
        _appMode = AddSpinner(content, new[] { "Выключено", "Чёрный список", "Белый список" });
        _appNames = AddEdit(content, "vk.exe, telegram.exe", "через запятую");
        AddButton(content, "Применить к приложениям", ApplyApps);

        // --- Интернет ---
        AddLabel(content, "Ограничение интернета:");
        _block = new CheckBox(this) { Text = "Полная блокировка (прокси + закрытие браузеров)" };
        _killBrowsers = new CheckBox(this) { Text = "Закрывать браузеры", Checked = true };
        content.AddView(_block);
        content.AddView(_killBrowsers);
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.AddView(MakeButton(row, "Заблокировать", () => { _block.Checked = true; ApplyInternet(); }));
        row.AddView(MakeButton(row, "Разрешить", () => { _block.Checked = false; ApplyInternet(); }));
        content.AddView(row);

        // --- Фильтр сайтов ---
        AddLabel(content, "Фильтр сайтов (домены, поддомены матчатся автоматически):");
        _siteMode = AddSpinner(content, new[] { "Выключено", "Чёрный список", "Белый список" });
        _sites = AddEdit(content, "nayanovaacademy.ru", "через запятую");
        AddButton(content, "Применить фильтр сайтов", ApplyInternet);

        // --- Автозагрузка ---
        AddLabel(content, "Автозагрузка ученика (подозрительные скрипты → карантин):");
        AddButton(content, "Проверить и почистить", () => ScanAutostart(true));
        AddButton(content, "Только проверить", () => ScanAutostart(false));
    }

    private Spinner AddSpinner(LinearLayout parent, string[] items)
    {
        var adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem, items);
        adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
        var sp = new Spinner(this) { Adapter = adapter };
        parent.AddView(sp, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        return sp;
    }

    private Button MakeButton(LinearLayout parent, string text, Action onClick)
    {
        var b = new Button(this) { Text = text };
        b.SetTextSize(Android.Util.ComplexUnitType.Sp, 15);
        b.LayoutParameters = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, (int)(46 * Resources!.DisplayMetrics!.Density))
        { RightMargin = Dp(6) };
        b.Click += (_, _) => { try { onClick(); } catch (Exception ex) { Log("Ошибка: " + ex.Message); } };
        parent.AddView(b);
        return b;
    }

    private static List<string> Split(string? text) =>
        (text ?? "").Split(new[] { ',', ';', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

    private void ApplyApps()
    {
        var mode = _appMode.SelectedItemPosition switch
        {
            1 => AppControlMode.Blacklist,
            2 => AppControlMode.Whitelist,
            _ => AppControlMode.Off
        };
        var names = Split(_appNames.Text);

        RunAction(async client =>
        {
            await client.SendAppPolicyAsync(mode, names);
            return $"приложения: режим {mode}, позиций {names.Count}";
        });
    }

    private void ApplyInternet()
    {
        var policy = new InternetPolicyMessage
        {
            Blocked = _block.Checked,
            KillBrowsers = _killBrowsers.Checked,
            SiteMode = _siteMode.SelectedItemPosition switch
            {
                1 => SiteFilterMode.Blacklist,
                2 => SiteFilterMode.Whitelist,
                _ => SiteFilterMode.Off
            },
            Sites = Split(_sites.Text)
        };

        RunAction(async client =>
        {
            await client.SendInternetAsync(policy);
            return $"интернет: {(policy.Blocked ? "блокирован" : "разрешён")}, сайты: {policy.SiteMode}, позиций {policy.Sites.Count}";
        });
    }

    private void ScanAutostart(bool quarantine)
    {
        RunAction(async client =>
        {
            var report = await client.ScanAutostartAsync(quarantine, 10000, CancellationToken.None);
            if (report is null) return "нет ответа";
            foreach (var f in report.Findings)
                Log($"    [{f.Location}] {f.Name} {f.Detail}{(f.Removed ? " — удалено" : "")}");
            return $"найдено {report.Findings.Count}, удалено {report.Removed}";
        });
    }
}
