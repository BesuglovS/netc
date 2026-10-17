using Android.App;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using NetcShared;

namespace NetcAndroid;

/// <summary>
/// Базовый экран действия: полоса выбора ПК сверху, содержимое действия и журнал снизу.
/// Наследники только добавляют свои элементы в <see cref="Content"/>.
/// </summary>
public abstract class ActionActivity : Activity
{
    protected AppSettings Settings = new();
    protected byte[] Key = Array.Empty<byte>();
    protected PcStore Store = null!;
    protected TargetsSelectorView Targets = null!;
    protected LinearLayout Content = null!;

    private readonly List<string> _lines = new();
    private TextView _log = null!;
    private ScrollView _logScroll = null!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        Settings = AppSettings.Load(this);
        Key = NetcCrypto.DeriveKey(Settings.SharedKey);
        Store = new PcStore(this);
        Store.Load();

        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };

        var all = Store.Items.Select(e => e.Address).ToList();
        var names = Store.Items.Select(e => e.Name).ToList();
        Targets = new TargetsSelectorView(this, all, names, all);
        root.AddView(Targets, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        Content = new LinearLayout(this) { Orientation = Orientation.Vertical };
        Content.SetPadding(Dp(12), Dp(6), Dp(12), Dp(6));
        BuildContent(Content);

        var contentScroll = new ScrollView(this);
        contentScroll.AddView(Content);
        root.AddView(contentScroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0, 1f));

        _log = new TextView(this);
        _log.SetTextSize(Android.Util.ComplexUnitType.Sp, 12);
        _log.SetTypeface(Typeface.Monospace, TypefaceStyle.Normal);
        _log.SetBackgroundColor(Color.ParseColor("#1E1E1E"));
        _log.SetTextColor(Color.ParseColor("#DCDCDC"));
        _log.SetPadding(Dp(8), Dp(6), Dp(8), Dp(6));

        _logScroll = new ScrollView(this);
        _logScroll.SetBackgroundColor(Color.ParseColor("#1E1E1E"));
        _logScroll.AddView(_log);
        root.AddView(_logScroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, Dp(160)));

        SetContentView(root);
        root.SetOnApplyWindowInsetsListener(new InsetsListener());
    }

    protected abstract void BuildContent(LinearLayout content);

    protected int Dp(int v) => (int)(v * Resources!.DisplayMetrics!.Density);

    protected Button AddButton(LinearLayout parent, string text, Action onClick)
    {
        var b = new Button(this) { Text = text };
        b.SetTextSize(Android.Util.ComplexUnitType.Sp, 15);
        var lp = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, (int)(48 * Resources!.DisplayMetrics!.Density))
        { RightMargin = Dp(6), BottomMargin = Dp(4) };
        b.LayoutParameters = lp;
        b.Click += (_, _) => { try { onClick(); } catch (Exception ex) { Log("Ошибка: " + ex.Message); } };
        parent.AddView(b);
        return b;
    }

    protected TextView AddLabel(LinearLayout parent, string text)
    {
        var t = new TextView(this) { Text = text };
        t.SetTextSize(Android.Util.ComplexUnitType.Sp, 15);
        t.SetPadding(0, Dp(6), 0, Dp(2));
        parent.AddView(t);
        return t;
    }

    protected EditText AddEdit(LinearLayout parent, string value, string? hint = null)
    {
        var e = new EditText(this) { Text = value };
        if (hint is not null) e.Hint = hint;
        e.SetTextSize(Android.Util.ComplexUnitType.Sp, 15);
        parent.AddView(e, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        return e;
    }

    protected void Log(string message)
    {
        var line = DateTime.Now.ToString("HH:mm:ss") + "  " + message;
        RunOnUiThread(() =>
        {
            _lines.Add(line);
            if (_lines.Count > 500) _lines.RemoveRange(0, _lines.Count - 500);
            _log.Text = string.Join("\n", _lines);
            _logScroll.Post(() => _logScroll.FullScroll(FocusSearchDirection.Down));
        });
    }

    protected async void RunAction(Func<ScreenClient, Task<string>> action)
    {
        var targets = Targets.SelectedAddresses;
        if (targets.Count == 0) { Log("Не выбрано ни одного ПК."); return; }
        Log($"Выполняется на {targets.Count} ПК…");
        try
        {
            await ActionSupport.RunAsync(targets, Settings, Key, Log, action);
        }
        catch (Exception ex)
        {
            Log("Ошибка: " + ex.Message);
        }
        Log("Готово.");
    }
}
