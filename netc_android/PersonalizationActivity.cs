using Android.App;
using Android.Content;
using Android.Views;
using Android.Widget;
using NetcShared;

namespace NetcAndroid;

/// <summary>
/// Персонализация ученических ПК: обои, экран блокировки, тема, акцент и сброс к стандартным.
/// Картинки выбираются через SAF, копируются в кэш и передаются на ПК файловым каналом.
/// </summary>
[Activity(Label = "Персонализация", Theme = "@android:style/Theme.Material.Light.DarkActionBar")]
public sealed class PersonalizationActivity : ActionActivity
{
    private const int PickWallpaper = 1002;
    private const int PickLock = 1003;

    private static readonly string[] AccentPresets =
    {
        "#0078D4", "#0067B8", "#2D7D9A", "#107C10", "#6B69D6", "#744DA9",
        "#B4009E", "#E3008C", "#CA5010", "#EF6950", "#8E562E", "#78A000",
        "#767676", "#000000", "#C30052", "#498205"
    };

    private string? _wallpaperPath;
    private string? _lockPath;
    private int _accent = 0x0078D4;

    private TextView _wallpaperLabel = null!;
    private TextView _lockLabel = null!;
    private TextView _accentLabel = null!;
    private Spinner _style = null!;
    private Spinner _theme = null!;
    private CheckBox _accentOn = null!;

    protected override void BuildContent(LinearLayout content)
    {
        // --- Обои ---
        AddLabel(content, "Обои:");
        _wallpaperLabel = AddValue(content, "картинка не выбрана");
        AddButton(content, "Картинка обоев…", () => PickImage(PickWallpaper));
        _style = AddSpinner(content, new[] { "Заполнить", "По размеру", "Растянуть", "По центру", "Замостить" });
        AddButton(content, "Применить обои", () => Apply(wallpaper: true));

        // --- Экран блокировки ---
        AddLabel(content, "Экран блокировки:");
        _lockLabel = AddValue(content, "картинка не выбрана");
        AddButton(content, "Картинка экрана блокировки…", () => PickImage(PickLock));
        AddButton(content, "Применить", () => Apply(lockScreen: true));

        // --- Тема ---
        AddLabel(content, "Тема:");
        _theme = AddSpinner(content, new[] { "Не менять", "Тёмная", "Светлая" });
        AddButton(content, "Применить тему", () => Apply(theme: true));

        // --- Акцент ---
        AddLabel(content, "Акцент:");
        _accentLabel = AddValue(content, AccentText());
        AddButton(content, "Цвет акцента…", PickAccent);
        _accentOn = new CheckBox(this) { Text = "Показывать акцент в панели задач и заголовках" };
        content.AddView(_accentOn);
        AddButton(content, "Применить акцент", () => Apply(accent: true));

        // --- Сброс ---
        AddButton(content, "Сбросить к стандартным (светлая тема)", Reset);
    }

    private TextView AddValue(LinearLayout parent, string text)
    {
        var t = new TextView(this) { Text = text };
        t.SetTextSize(Android.Util.ComplexUnitType.Sp, 14);
        t.SetTextColor(Android.Graphics.Color.DarkGray);
        t.SetPadding(Dp(6), 0, 0, Dp(4));
        parent.AddView(t);
        return t;
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

    private void PickImage(int requestCode)
    {
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("image/*");
        StartActivityForResult(intent, requestCode);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (resultCode != Result.Ok || data?.Data is null) return;
        if (requestCode != PickWallpaper && requestCode != PickLock) return;

        var uri = data.Data;
        string name = uri.LastPathSegment ?? "image";
        int slash = name.LastIndexOf('/');
        if (slash >= 0) name = name.Substring(slash + 1);
        if (name.Length == 0) name = "image";

        try
        {
            var dir = Path.Combine(CacheDir!.AbsolutePath, "pers");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, name);
            using var input = ContentResolver!.OpenInputStream(uri)
                              ?? throw new IOException("нет доступа к файлу");
            using var output = File.Create(path);
            input.CopyTo(output);

            if (requestCode == PickWallpaper) { _wallpaperPath = path; _wallpaperLabel.Text = name; }
            else { _lockPath = path; _lockLabel.Text = name; }
        }
        catch (Exception ex)
        {
            Log("Не удалось прочитать картинку: " + ex.Message);
        }
    }

    private void PickAccent()
    {
        var labels = AccentPresets.Select(c => c).ToArray();
        new AlertDialog.Builder(this)
            .SetTitle("Цвет акцента")
            ?.SetItems(labels, (_, e) =>
            {
                if (e.Which < 0 || e.Which >= AccentPresets.Length) return;
                _accent = int.Parse(AccentPresets[e.Which].Substring(1), System.Globalization.NumberStyles.HexNumber);
                _accentLabel.Text = AccentText();
            })
            ?.SetNegativeButton("Отмена", (_, _) => { })
            ?.Show();
    }

    private string AccentText() => "выбран #" + (_accent & 0xFFFFFF).ToString("X6");

    private string StyleCode() => _style.SelectedItemPosition switch
    {
        1 => "fit",
        2 => "stretch",
        3 => "center",
        4 => "tile",
        _ => "fill"
    };

    private void Apply(bool wallpaper = false, bool lockScreen = false, bool theme = false, bool accent = false)
    {
        if (wallpaper && _wallpaperPath is null) { Log("Обои: сначала выберите картинку."); return; }
        if (lockScreen && _lockPath is null) { Log("Экран блокировки: сначала выберите картинку."); return; }

        string? themeCode = null;
        if (theme)
        {
            themeCode = _theme.SelectedItemPosition == 1 ? "dark"
                : _theme.SelectedItemPosition == 2 ? "light" : null;
            if (themeCode is null) { Log("Тема: выберите «Тёмная» или «Светлая»."); return; }
        }

        string style = StyleCode();
        string? wallpaperPath = _wallpaperPath;
        string? lockPath = _lockPath;
        int accentColor = _accent;
        bool accentOn = _accentOn.Checked;
        var summary = new List<string>();
        if (wallpaper) summary.Add("обои");
        if (lockScreen) summary.Add("лок-скрин");
        if (theme) summary.Add("тема");
        if (accent) summary.Add("акцент #" + (_accent & 0xFFFFFF).ToString("X6"));
        Log("Применяю: " + string.Join(", ", summary) + "…");

        RunAction(async client =>
        {
            using var cts = new CancellationTokenSource(180000);
            string? wp = null, lk = null;

            if (wallpaper)
            {
                var r = await client.SendFileAsync(wallpaperPath!, 120000, cts.Token);
                if (!r.Ok) return "обои не переданы — " + r.Message;
                wp = r.Path;
            }
            if (lockScreen)
            {
                var r = await client.SendFileAsync(lockPath!, 120000, cts.Token);
                if (!r.Ok) return "лок-скрин не передан — " + r.Message;
                lk = r.Path;
            }

            var msg = new PersonalizationMessage
            {
                WallpaperPath = wp,
                WallpaperStyle = style,
                LockScreenPath = lk,
                Theme = themeCode,
                AccentColor = accent ? accentColor : 0,
                AccentOnStart = accentOn
            };

            var res = await client.ApplyPersonalizationAsync(msg, 30000, cts.Token);
            return res is null ? "нет ответа" : res.Message;
        });
    }

    private void Reset()
    {
        new AlertDialog.Builder(this)
            .SetTitle("Сброс персонализации")
            ?.SetMessage("Вернуть стандартные обои, светлую тему и акцент по умолчанию?")
            ?.SetPositiveButton("Сбросить", (_, _) => RunAction(async client =>
            {
                using var cts = new CancellationTokenSource(30000);
                var msg = new PersonalizationMessage { Reset = true, Theme = "light", AccentColor = 0x0078D4 };
                var res = await client.ApplyPersonalizationAsync(msg, 20000, cts.Token);
                return res is null ? "нет ответа" : res.Message;
            }))
            ?.SetNegativeButton("Отмена", (_, _) => { })
            ?.Show();
    }
}
