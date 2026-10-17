using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;

namespace NetcAndroid;

/// <summary>Адаптер списка ПК: флажок выбора, имя/адрес, сводка и статус.</summary>
public sealed class PcListAdapter : BaseAdapter
{
    private readonly Context _ctx;
    private readonly IList<PcEntry> _items;
    private readonly Action<PcEntry> _onToggle;

    public PcListAdapter(Context ctx, IList<PcEntry> items, Action<PcEntry> onToggle)
    {
        _ctx = ctx;
        _items = items;
        _onToggle = onToggle;
    }

    public override int Count => _items.Count;

    public override Java.Lang.Object? GetItem(int position) => null;

    public override long GetItemId(int position) => position;

    public PcEntry EntryAt(int position) => _items[position];

    public override View GetView(int position, View? convertView, ViewGroup? parent)
    {
        var e = _items[position];
        float d = _ctx.Resources!.DisplayMetrics!.Density;
        bool wide = _ctx.Resources.Configuration!.ScreenWidthDp >= 900;
        int Dp(int v) => (int)(v * d);

        var row = new LinearLayout(_ctx) { Orientation = Orientation.Horizontal, Clickable = true, Focusable = true };
        row.SetPadding(Dp(wide ? 18 : 12), Dp(wide ? 12 : 8), Dp(wide ? 18 : 12), Dp(wide ? 12 : 8));

        var check = new CheckBox(_ctx)
        {
            Checked = e.Selected,
            Focusable = false,
            Clickable = false
        };
        check.ScaleX = wide ? 1.25f : 1f;
        check.ScaleY = wide ? 1.25f : 1f;
        row.AddView(check);

        var col = new LinearLayout(_ctx) { Orientation = Orientation.Vertical };
        col.SetPadding(Dp(10), 0, 0, 0);

        var title = new TextView(_ctx) { Text = e.Title };
        title.SetTextSize(Android.Util.ComplexUnitType.Sp, wide ? 19 : 17);
        title.SetTextColor(Color.Black);
        col.AddView(title);

        var sub = new TextView(_ctx) { Text = e.Subtitle };
        sub.SetTextSize(Android.Util.ComplexUnitType.Sp, wide ? 15 : 13);
        sub.SetTextColor(Color.DarkGray);
        col.AddView(sub);

        var lp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
        row.AddView(col, lp);

        var status = new TextView(_ctx) { Text = StatusText(e) };
        status.SetTextSize(Android.Util.ComplexUnitType.Sp, wide ? 15 : 13);
        status.SetTextColor(StatusColor(e.Status));
        row.AddView(status);

        row.Click += (_, _) => _onToggle(e);
        return row;
    }

    private static string StatusText(PcEntry e) => e.Status switch
    {
        PcStatus.Ok => "OK",
        PcStatus.NoReply => "нет ответа",
        PcStatus.Checking => "…",
        _ => "—"
    };

    private static Color StatusColor(PcStatus status) => status switch
    {
        PcStatus.Ok => Color.ParseColor("#1B7F3B"),
        PcStatus.NoReply => Color.ParseColor("#C26A00"),
        PcStatus.Checking => Color.Gray,
        _ => Color.Gray
    };
}
