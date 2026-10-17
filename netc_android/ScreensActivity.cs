using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using NetcShared;

namespace NetcAndroid;

/// <summary>
/// Стена миниатюр экранов учеников: для каждого ПК отдельное соединение, поток JPEG-кадров,
/// декодирование и показ в плитке. Тап по плитке открывает удалённое управление.
/// </summary>
[Activity(Label = "Экраны учеников", Theme = "@android:style/Theme.Material.Light.DarkActionBar",
    ScreenOrientation = Android.Content.PM.ScreenOrientation.SensorLandscape)]
public sealed class ScreensActivity : Activity
{
    private const int TileWidth = 320;
    private const int TileQuality = 40;
    private const int TileFps = 1;

    private readonly List<TileState> _tiles = new();
    private AppSettings _settings = new();
    private byte[] _key = Array.Empty<byte>();
    private PcStore _store = null!;
    private CancellationTokenSource? _cts;
    private TextView _status = null!;

    private sealed class TileState
    {
        public string Address = "";
        public string Name = "";
        public ImageView? View;
        public TextView? Caption;
        public ScreenClient? Client;
        public int FrameW = 1;
        public int FrameH = 1;
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        _settings = AppSettings.Load(this);
        _key = NetcCrypto.DeriveKey(_settings.SharedKey);
        _store = new PcStore(this);
        _store.Load();

        BuildUi();

        _cts = new CancellationTokenSource();
        foreach (var t in _tiles)
            _ = StreamAsync(t, _cts.Token);
    }

    private void BuildUi()
    {
        float d = Resources!.DisplayMetrics!.Density;
        int Dp(int v) => (int)(v * d);
        bool wide = Resources.Configuration!.ScreenWidthDp >= 900;
        int screenDp = Resources.Configuration.ScreenWidthDp;
        int columns = Math.Max(1, screenDp / 380);
        int tileHeight = (int)((wide ? 210 : 180) * d);

        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };

        _status = new TextView(this) { Text = $"ПК: {_store.Items.Count}" };
        _status.SetPadding(Dp(14), Dp(8), Dp(14), Dp(8));
        _status.SetTextSize(Android.Util.ComplexUnitType.Sp, wide ? 16 : 14);
        root.AddView(_status, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        var scroll = new ScrollView(this);
        var col = new LinearLayout(this) { Orientation = Orientation.Vertical };
        scroll.AddView(col);
        root.AddView(scroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0, 1f));

        var items = _store.Items.ToList();
        for (int i = 0; i < items.Count; i += columns)
        {
            var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            for (int j = 0; j < columns; j++)
            {
                int idx = i + j;
                View child;
                if (idx < items.Count)
                {
                    var e = items[idx];
                    child = BuildTile(e, tileHeight, Dp);
                }
                else
                {
                    child = new View(this);
                }
                var lp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
                lp.SetMargins(Dp(6), Dp(6), Dp(6), Dp(6));
                row.AddView(child, lp);
            }
            col.AddView(row, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        }

        SetContentView(root);
        root.SetOnApplyWindowInsetsListener(new InsetsListener());
    }

    private View BuildTile(PcEntry e, int tileHeight, Func<int, int> dp)
    {
        var tile = new LinearLayout(this) { Orientation = Orientation.Vertical, Clickable = true, Focusable = true };
        tile.SetBackgroundColor(Color.ParseColor("#202020"));

        var pic = new ImageView(this);
        pic.SetBackgroundColor(Color.Black);
        pic.SetScaleType(ImageView.ScaleType.FitCenter);
        tile.AddView(pic, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, tileHeight));

        var name = string.IsNullOrWhiteSpace(e.Name) ? e.Address : e.Name;
        var caption = new TextView(this)
        {
            Text = $"{name}  ({e.Address})"
        };
        caption.SetTextSize(Android.Util.ComplexUnitType.Sp, 13);
        caption.SetTextColor(Color.White);
        caption.SetBackgroundColor(Color.ParseColor("#303030"));
        caption.SetPadding(dp(8), dp(4), dp(8), dp(4));
        caption.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
        caption.SetSingleLine(true);
        tile.AddView(caption, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        var state = new TileState { Address = e.Address, Name = name, View = pic, Caption = caption };
        _tiles.Add(state);

        tile.Click += (_, _) =>
        {
            var intent = new Intent(this, typeof(ControlActivity));
            intent.PutExtra("address", e.Address);
            StartActivity(intent);
        };
        return tile;
    }

    private async Task StreamAsync(TileState t, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            ScreenClient? client = null;
            try
            {
                client = new ScreenClient(t.Address, _settings.ControlPort, _key);
                var disconnected = new TaskCompletionSource();
                client.ConnectionChanged += ok => { if (!ok) disconnected.TrySetResult(); };
                client.FrameReceived += bytes => OnFrame(t, bytes);
                t.Client = client;

                SetCaption(t, $"{t.Name}  ({t.Address})");
                await client.StartScreenAsync(TileWidth, TileQuality, TileFps);

                await Task.WhenAny(disconnected.Task, Task.Delay(Timeout.Infinite, ct));
            }
            catch
            {
                SetCaption(t, $"{t.Name}  ({t.Address}) — нет связи");
            }
            finally
            {
                t.Client = null;
                try { client?.Dispose(); } catch { }
            }

            if (ct.IsCancellationRequested) break;
            try { await Task.Delay(3000, ct); } catch { break; }
        }
    }

    private void OnFrame(TileState t, byte[] jpeg)
    {
        var bmp = AndroidImage.DecodeJpeg(jpeg);
        if (bmp is null) return;
        t.FrameW = bmp.Width;
        t.FrameH = bmp.Height;
        RunOnUiThread(() =>
        {
            if (t.View is null) { bmp.Recycle(); return; }
            t.View.SetImageBitmap(bmp);
        });
    }

    private void SetCaption(TileState t, string text)
        => RunOnUiThread(() => { if (t.Caption is not null) t.Caption.Text = text; });

    protected override void OnDestroy()
    {
        try { _cts?.Cancel(); } catch { }
        foreach (var t in _tiles) { try { t.Client?.Dispose(); } catch { } }
        base.OnDestroy();
    }
}
