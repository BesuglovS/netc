using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;

namespace NetcAndroid;

/// <summary>
/// Полоса выбора целевых ПК для окон действий: кнопка выбора (список с флажками),
/// быстрые «Все»/«Никто» и сводка «выбрано N из M». Аналог TargetsSelector десктопного клиента.
/// </summary>
public sealed class TargetsSelectorView : LinearLayout
{
    private readonly Context _ctx;
    private readonly string[] _all;
    private readonly string[] _names;
    private readonly List<string> _selected = new();
    private readonly TextView _summary;

    public TargetsSelectorView(Context ctx, IReadOnlyList<string> all, IReadOnlyList<string> names,
        IReadOnlyList<string> initial) : base(ctx)
    {
        _ctx = ctx;
        _all = all.ToArray();
        _names = names.ToArray();
        foreach (var a in initial)
            if (_all.Contains(a, StringComparer.OrdinalIgnoreCase)) _selected.Add(a);
        if (_selected.Count == 0) _selected.AddRange(_all);

        float d = ctx.Resources!.DisplayMetrics!.Density;
        int Dp(int v) => (int)(v * d);

        Orientation = Orientation.Vertical;
        SetPadding(Dp(6), Dp(4), Dp(6), Dp(4));
        SetBackgroundColor(Color.ParseColor("#EEEEEE"));

        var row = new LinearLayout(ctx) { Orientation = Orientation.Horizontal };
        row.AddView(MakeButton("Выбрать ПК…", ShowDialog));
        row.AddView(MakeButton("Все", () => { _selected.Clear(); _selected.AddRange(_all); Update(); }));
        row.AddView(MakeButton("Никто", () => { _selected.Clear(); Update(); }));

        var scroll = new HorizontalScrollView(ctx) { HorizontalScrollBarEnabled = false };
        scroll.AddView(row);
        AddView(scroll, new LayoutParams(LayoutParams.MatchParent, LayoutParams.WrapContent));

        _summary = new TextView(ctx) { Text = "" };
        _summary.SetPadding(Dp(8), Dp(4), Dp(8), Dp(2));
        _summary.SetTextSize(Android.Util.ComplexUnitType.Sp, 13);
        _summary.SetTextColor(Color.DarkGray);
        AddView(_summary, new LayoutParams(LayoutParams.MatchParent, LayoutParams.WrapContent));

        Update();
    }

    public IReadOnlyList<string> SelectedAddresses => _selected.ToList();

    public int Total => _all.Length;

    private Button MakeButton(string text, Action onClick)
    {
        float d = _ctx.Resources!.DisplayMetrics!.Density;
        var b = new Button(_ctx) { Text = text };
        b.SetTextSize(Android.Util.ComplexUnitType.Sp, 14);
        b.LayoutParameters = new LayoutParams(LayoutParams.WrapContent, (int)(44 * d))
        { RightMargin = (int)(6 * d) };
        b.Click += (_, _) => onClick();
        return b;
    }

    private void Update()
    {
        _summary.Text = _selected.Count == _all.Length
            ? $"выбраны все ({_all.Length})"
            : $"выбрано {_selected.Count} из {_all.Length}";
    }

    private void ShowDialog()
    {
        var items = new string[_all.Length];
        var checks = new bool[_all.Length];
        for (int i = 0; i < _all.Length; i++)
        {
            string name = i < _names.Length ? _names[i] : "";
            items[i] = string.IsNullOrWhiteSpace(name) ? _all[i] : $"{name}  ({_all[i]})";
            checks[i] = _selected.Contains(_all[i], StringComparer.OrdinalIgnoreCase);
        }

        void Apply()
        {
            _selected.Clear();
            for (int i = 0; i < _all.Length; i++)
                if (checks[i]) _selected.Add(_all[i]);
            Update();
        }

        new AlertDialog.Builder(_ctx)
            .SetTitle("Выбор компьютеров")
            ?.SetMultiChoiceItems(items, checks, (_, e) => { checks[e.Which] = e.IsChecked; })
            ?.SetPositiveButton("OK", (_, _) => Apply())
            ?.SetNeutralButton("Все", (_, _) => { for (int i = 0; i < checks.Length; i++) checks[i] = true; Apply(); })
            ?.SetNegativeButton("Никто", (_, _) => { for (int i = 0; i < checks.Length; i++) checks[i] = false; Apply(); })
            ?.Show();
    }
}
