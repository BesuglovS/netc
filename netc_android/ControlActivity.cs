using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Widget;
using NetcShared;
using InputEvent = NetcShared.InputEvent;

namespace NetcAndroid;

/// <summary>
/// Удалённое управление одним ПК: поток экрана, мышь по касанию (нормированные координаты),
/// прокрутка двумя пальцами, экранные клавиши и ввод текста.
/// </summary>
[Activity(Label = "Управление", Theme = "@android:style/Theme.Material.NoActionBar.Fullscreen",
    ScreenOrientation = Android.Content.PM.ScreenOrientation.SensorLandscape)]
public sealed class ControlActivity : Activity, View.IOnTouchListener
{
    private AppSettings _settings = new();
    private byte[] _key = Array.Empty<byte>();
    private string _address = "";

    private ImageView _picture = null!;
    private TextView _status = null!;
    private EditText _input = null!;

    private ScreenClient? _client;
    private CancellationTokenSource? _cts;

    private int _frameW = 1;
    private int _frameH = 1;
    private float _lastX, _lastY;
    private float _scrollAnchorY = float.MinValue;
    private bool _guard;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        _settings = AppSettings.Load(this);
        _key = NetcCrypto.DeriveKey(_settings.SharedKey);
        _address = Intent?.GetStringExtra("address") ?? "";

        BuildUi();

        _cts = new CancellationTokenSource();
        _ = StreamAsync(_cts.Token);
    }

    private void BuildUi()
    {
        float d = Resources!.DisplayMetrics!.Density;
        int Dp(int v) => (int)(v * d);
        bool wide = Resources.Configuration!.ScreenWidthDp >= 900;

        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetBackgroundColor(Color.Black);

        _status = new TextView(this) { Text = _address + " — подключение…" };
        _status.SetTextColor(Color.White);
        _status.SetBackgroundColor(Color.ParseColor("#202020"));
        _status.SetPadding(Dp(12), Dp(6), Dp(12), Dp(6));
        _status.SetTextSize(Android.Util.ComplexUnitType.Sp, 13);
        root.AddView(_status, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        _picture = new ImageView(this);
        _picture.SetBackgroundColor(Color.Black);
        _picture.SetScaleType(ImageView.ScaleType.FitCenter);
        _picture.Focusable = true;
        _picture.SetOnTouchListener(this);
        root.AddView(_picture, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0, 1f));

        // Экранные клавиши (прокручиваются по горизонтали).
        var keys = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        keys.SetPadding(Dp(6), Dp(4), Dp(6), Dp(4));
        keys.SetBackgroundColor(Color.ParseColor("#202020"));

        var btnRmb = KeyButton("ПКМ", () => SendMouseClick("right"));
        btnRmb.SetTextColor(Color.ParseColor("#FFD24D"));
        keys.AddView(btnRmb);
        keys.AddView(KeyButton("Esc", () => SendKey(KeyMap.Escape)));
        keys.AddView(KeyButton("Tab", () => SendKey(KeyMap.Tab)));
        keys.AddView(KeyButton("Enter", () => SendKey(KeyMap.Return)));
        keys.AddView(KeyButton("⌫", () => SendKey(KeyMap.Back)));
        keys.AddView(KeyButton("←", () => SendKey(KeyMap.Left)));
        keys.AddView(KeyButton("↑", () => SendKey(KeyMap.Up)));
        keys.AddView(KeyButton("→", () => SendKey(KeyMap.Right)));
        keys.AddView(KeyButton("↓", () => SendKey(KeyMap.Down)));
        keys.AddView(KeyButton("Win", () => SendKey(KeyMap.Win)));
        keys.AddView(KeyButton("Alt+Tab", () => SendCombo(KeyMap.Alt, KeyMap.Tab)));
        keys.AddView(KeyButton("Ctrl+Alt+Del", () => SendCombo(KeyMap.Ctrl, KeyMap.Alt, KeyMap.Delete)));

        var keysScroll = new HorizontalScrollView(this) { HorizontalScrollBarEnabled = false };
        keysScroll.AddView(keys);
        root.AddView(keysScroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        _input = new EditText(this)
        {
            Hint = "Печатайте текст — символы уходят на ПК",
            InputType = InputTypes.ClassText | InputTypes.TextFlagNoSuggestions
        };
        _input.SetTextSize(Android.Util.ComplexUnitType.Sp, 15);
        _input.SetSingleLine(true);
        _input.SetBackgroundColor(Color.White);
        _input.SetPadding(Dp(12), Dp(8), Dp(12), Dp(8));
        _input.AfterTextChanged += OnAfterTextChanged;
        root.AddView(_input, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        SetContentView(root);
        root.SetOnApplyWindowInsetsListener(new InsetsListener());
    }

    private Button KeyButton(string text, Action onClick)
    {
        float d = Resources!.DisplayMetrics!.Density;
        var b = new Button(this) { Text = text };
        b.SetTextSize(Android.Util.ComplexUnitType.Sp, 14);
        b.SetBackgroundColor(Color.ParseColor("#404040"));
        b.SetTextColor(Color.White);
        var lp = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, (int)(44 * d))
        { RightMargin = (int)(6 * d) };
        b.LayoutParameters = lp;
        b.Click += (_, _) => { try { onClick(); } catch { } };
        return b;
    }

    // ---- Поток экрана ----

    private async Task StreamAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            ScreenClient? client = null;
            try
            {
                client = new ScreenClient(_address, _settings.ControlPort, _key);
                _client = client;
                var disconnected = new TaskCompletionSource();
                client.ConnectionChanged += ok => { if (!ok) disconnected.TrySetResult(); };
                client.FrameReceived += OnFrame;

                SetStatus(_address + " — подключение…");
                await client.StartScreenAsync(1280, 55, 5);
                SetStatus(_address + " — подключено, можно управлять");

                await Task.WhenAny(disconnected.Task, Task.Delay(Timeout.Infinite, ct));
                SetStatus(_address + " — связь потеряна");
            }
            catch (Exception ex)
            {
                SetStatus(_address + " — ошибка: " + ex.Message);
            }
            finally
            {
                _client = null;
                try { client?.Dispose(); } catch { }
            }

            if (ct.IsCancellationRequested) break;
            try { await Task.Delay(3000, ct); } catch { break; }
        }
    }

    private void OnFrame(byte[] jpeg)
    {
        var bmp = AndroidImage.DecodeJpeg(jpeg);
        if (bmp is null) return;
        _frameW = bmp.Width;
        _frameH = bmp.Height;
        RunOnUiThread(() =>
        {
            if (IsFinishing || IsDestroyed) { bmp.Recycle(); return; }
            _picture.SetImageBitmap(bmp);
        });
    }

    private void SetStatus(string text) => RunOnUiThread(() => _status.Text = text);

    // ---- Мышь ----

    public bool OnTouch(View? v, MotionEvent? e)
    {
        if (e is null) return false;
        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                _lastX = e.GetX();
                _lastY = e.GetY();
                _scrollAnchorY = float.MinValue;
                SendMouse("down", "left", _lastX, _lastY);
                return true;

            case MotionEventActions.Move:
                if (e.PointerCount >= 2)
                {
                    float y = (e.GetY(0) + e.GetY(1)) / 2f;
                    if (_scrollAnchorY == float.MinValue) _scrollAnchorY = y;
                    float dy = _scrollAnchorY - y;
                    if (Math.Abs(dy) >= 24)
                    {
                        SendWheel((int)(dy * 4));
                        _scrollAnchorY = y;
                    }
                }
                else
                {
                    _lastX = e.GetX();
                    _lastY = e.GetY();
                    SendMouse("move", "left", _lastX, _lastY);
                }
                return true;

            case MotionEventActions.PointerUp:
                _scrollAnchorY = float.MinValue;
                return true;

            case MotionEventActions.Up:
                SendMouse("up", "left", e.GetX(), e.GetY());
                return true;
        }
        return false;
    }

    private void SendMouseClick(string button)
    {
        SendMouse("down", button, _lastX, _lastY);
        SendMouse("up", button, _lastX, _lastY);
    }

    private void SendMouse(string action, string button, float x, float y)
    {
        var (nx, ny) = Normalize(x, y);
        Fire(new InputEvent { Type = "mouse", Action = action, Button = button, X = nx, Y = ny });
    }

    private void SendWheel(int delta)
    {
        var (nx, ny) = Normalize(_lastX, _lastY);
        Fire(new InputEvent { Type = "mouse", Action = "wheel", X = nx, Y = ny, Delta = delta });
    }

    private (int nx, int ny) Normalize(float x, float y)
    {
        int vw = Math.Max(1, _picture.Width);
        int vh = Math.Max(1, _picture.Height);
        double scale = Math.Min(vw / (double)_frameW, vh / (double)_frameH);
        int dw = Math.Max(1, (int)(_frameW * scale));
        int dh = Math.Max(1, (int)(_frameH * scale));
        int ox = (vw - dw) / 2;
        int oy = (vh - dh) / 2;
        double fx = (x - ox) / (double)dw;
        double fy = (y - oy) / (double)dh;
        int nx = (int)(Math.Clamp(fx, 0, 1) * 65535);
        int ny = (int)(Math.Clamp(fy, 0, 1) * 65535);
        return (nx, ny);
    }

    // ---- Клавиатура ----

    private void SendKey(int vk)
    {
        Fire(new InputEvent { Type = "key", Vk = vk, Down = true });
        Fire(new InputEvent { Type = "key", Vk = vk, Down = false });
    }

    private void SendCombo(params int[] vks)
    {
        foreach (int vk in vks) Fire(new InputEvent { Type = "key", Vk = vk, Down = true });
        for (int i = vks.Length - 1; i >= 0; i--)
            Fire(new InputEvent { Type = "key", Vk = vks[i], Down = false });
    }

    private void SendChar(char c)
    {
        var m = KeyMap.Char(c);
        if (m is null) return;
        if (m.Value.Shift)
        {
            Fire(new InputEvent { Type = "key", Vk = KeyMap.Shift, Down = true });
            Fire(new InputEvent { Type = "key", Vk = m.Value.Vk, Down = true });
            Fire(new InputEvent { Type = "key", Vk = m.Value.Vk, Down = false });
            Fire(new InputEvent { Type = "key", Vk = KeyMap.Shift, Down = false });
        }
        else
        {
            Fire(new InputEvent { Type = "key", Vk = m.Value.Vk, Down = true });
            Fire(new InputEvent { Type = "key", Vk = m.Value.Vk, Down = false });
        }
    }

    private void OnAfterTextChanged(object? sender, AfterTextChangedEventArgs e)
    {
        if (_guard) return;
        var text = e.Editable?.ToString();
        if (string.IsNullOrEmpty(text)) return;

        foreach (char c in text) SendChar(c);

        _guard = true;
        _input.Text = "";
        _guard = false;
    }

    private void Fire(InputEvent ev)
    {
        try
        {
            var c = _client;
            if (c is null || !c.IsConnected) return;
            _ = c.SendInputAsync(ev);
        }
        catch { }
    }

    protected override void OnDestroy()
    {
        try { _cts?.Cancel(); } catch { }
        try { _client?.Dispose(); } catch { }
        base.OnDestroy();
    }
}
