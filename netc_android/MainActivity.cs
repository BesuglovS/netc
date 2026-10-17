using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Net.Wifi;
using Android.OS;
using Android.Views;
using Android.Widget;
using NetcShared;

namespace NetcAndroid;

/// <summary>
/// Главный экран пульта учителя на планшете: список ПК, обнаружение, обновление сведений,
/// добавление/удаление ПК и настройки. Фаза 1 (каркас и сеть).
///
/// Раскладка адаптивная: на большом экране (планшет, ландшафт) — две панели
/// (слева список ПК, справа журнал и статус); на узком экране — вертикально.
/// </summary>
[Activity(Label = "netc — пульт учителя", MainLauncher = true,
    Theme = "@android:style/Theme.Material.Light.DarkActionBar",
    ConfigurationChanges = Android.Content.PM.ConfigChanges.Orientation
        | Android.Content.PM.ConfigChanges.ScreenSize
        | Android.Content.PM.ConfigChanges.ScreenLayout)]
public sealed class MainActivity : Activity
{
    private AppSettings _settings = new();
    private byte[] _key = Array.Empty<byte>();
    private PcStore _store = null!;
    private PcListAdapter _adapter = null!;

    private ListView _list = null!;
    private TextView _log = null!;
    private TextView _status = null!;
    private TextView _summary = null!;
    private ScrollView _logScroll = null!;

    private readonly List<string> _logLines = new();
    private bool _busy;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        _settings = AppSettings.Load(this);
        _key = NetcCrypto.DeriveKey(_settings.SharedKey);
        _store = new PcStore(this);
        _store.Load();

        BuildUi();

        _adapter = new PcListAdapter(this, _store.Items, OnToggleEntry);
        _list.Adapter = _adapter;

        if (_settings.IsDefaultKey)
            AddLog("ВНИМАНИЕ: используется ключ по умолчанию «netc-default-key». " +
                   "Задайте свой ключ в «Настройках» и разложите его на ученические ПК.");
        AddLog($"ПК в списке: {_store.Items.Count}. Порт управления {_settings.ControlPort}, обнаружения {_settings.DiscoveryPort}.");

        TryOpenRequestedScreen();
    }

    /// <summary>Открыть экран по параметру запуска: extra <c>open</c> (screens, test, pers, power, …).</summary>
    private void TryOpenRequestedScreen()
    {
        string? open = Intent?.GetStringExtra("open");
        if (string.IsNullOrWhiteSpace(open)) return;
        var map = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["screens"] = typeof(ScreensActivity),
            ["control"] = typeof(ControlActivity),
            ["power"] = typeof(PowerActivity),
            ["message"] = typeof(MessageActivity),
            ["files"] = typeof(FilesActivity),
            ["restrictions"] = typeof(RestrictionsActivity),
            ["journal"] = typeof(JournalActivity),
            ["close"] = typeof(CloseWindowsActivity),
            ["lock"] = typeof(LockActivity),
            ["test"] = typeof(TestActivity),
            ["pers"] = typeof(PersonalizationActivity)
        };
        if (map.TryGetValue(open.Trim(), out var type))
        {
            var intent = new Intent(this, type);
            string? address = Intent?.GetStringExtra("address");
            if (address is not null) intent.PutExtra("address", address);
            StartActivity(intent);
        }
        else
        {
            AddLog("Неизвестный экран: " + open);
        }
    }

    // ---- Интерфейс ----

    /// <summary>Поворот экрана: перестраиваем раскладку (портрет ↔ две панели), журнал сохраняем.</summary>
    public override void OnConfigurationChanged(Android.Content.Res.Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        BuildUi();
        _adapter = new PcListAdapter(this, _store.Items, OnToggleEntry);
        _list.Adapter = _adapter;
        _log.Text = string.Join("\n", _logLines);
    }

    private void BuildUi()
    {
        float d = Resources!.DisplayMetrics!.Density;
        int Dp(int v) => (int)(v * d);
        bool wide = Resources.Configuration!.ScreenWidthDp >= 900;

        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };

        // Полоса кнопок (прокручивается по горизонтали).
        var bar = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        bar.SetPadding(Dp(6), Dp(4), Dp(6), Dp(4));
        bar.AddView(MakeButton("Обнаружить", async () => await DiscoverAsync()));
        bar.AddView(MakeButton("Обновить сведения", async () => await RefreshAllAsync()));
        bar.AddView(MakeButton("Добавить ПК", ShowAddDialog));
        bar.AddView(MakeButton("Удалить", RemoveSelected));
        bar.AddView(MakeButton("Выделить все", () => SetAllSelected(true)));
        bar.AddView(MakeButton("Снять", () => SetAllSelected(false)));
        bar.AddView(MakeButton("Экраны", OpenScreens));
        bar.AddView(MakeButton("Блокировка", () => StartActivity(typeof(LockActivity))));
        bar.AddView(MakeButton("Питание", () => StartActivity(typeof(PowerActivity))));
        bar.AddView(MakeButton("Сообщение", () => StartActivity(typeof(MessageActivity))));
        bar.AddView(MakeButton("Файлы", () => StartActivity(typeof(FilesActivity))));
        bar.AddView(MakeButton("Ограничения", () => StartActivity(typeof(RestrictionsActivity))));
        bar.AddView(MakeButton("Журнал", () => StartActivity(typeof(JournalActivity))));
        bar.AddView(MakeButton("Закрыть окна", () => StartActivity(typeof(CloseWindowsActivity))));
        bar.AddView(MakeButton("Тест", () => StartActivity(typeof(TestActivity))));
        bar.AddView(MakeButton("Персонализация", () => StartActivity(typeof(PersonalizationActivity))));
        bar.AddView(MakeButton("Включить", WakePcs));
        bar.AddView(MakeButton("Настройки", ShowSettingsDialog));

        var barScroll = new HorizontalScrollView(this) { HorizontalScrollBarEnabled = false };
        barScroll.AddView(bar);
        root.AddView(barScroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        _summary = new TextView(this) { Text = "" };
        _summary.SetPadding(Dp(14), Dp(2), Dp(14), Dp(6));
        _summary.SetTextSize(Android.Util.ComplexUnitType.Sp, wide ? 15 : 13);
        _summary.SetTextColor(Color.DarkGray);

        _list = new ListView(this) { DividerHeight = 1 };
        _list.SetBackgroundColor(Color.White);

        _log = new TextView(this);
        _log.SetTextSize(Android.Util.ComplexUnitType.Sp, wide ? 13 : 12);
        _log.SetTypeface(Typeface.Monospace, TypefaceStyle.Normal);
        _log.SetBackgroundColor(Color.ParseColor("#1E1E1E"));
        _log.SetTextColor(Color.ParseColor("#DCDCDC"));
        _log.SetPadding(Dp(10), Dp(6), Dp(10), Dp(6));
        _logScroll = new ScrollView(this);
        _logScroll.AddView(_log);

        _status = new TextView(this) { Text = "Готово" };
        _status.SetPadding(Dp(14), Dp(8), Dp(14), Dp(8));
        _status.SetTextSize(Android.Util.ComplexUnitType.Sp, wide ? 16 : 14);

        // Панель слева: сводка + список ПК.
        var left = new LinearLayout(this) { Orientation = Orientation.Vertical };
        left.AddView(_summary, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        left.AddView(_list, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0, 1f));

        // Панель справа: журнал + статус.
        var right = new LinearLayout(this) { Orientation = Orientation.Vertical };
        right.AddView(_logScroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent,
            wide ? 0 : Dp(150),
            wide ? 1f : 0f));
        right.AddView(_status, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        if (wide)
        {
            var content = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            content.AddView(left, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 2f));
            var divider = new View(this);
            divider.SetBackgroundColor(Color.ParseColor("#D0D0D0"));
            content.AddView(divider, new LinearLayout.LayoutParams(Dp(1), ViewGroup.LayoutParams.MatchParent));
            content.AddView(right, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));
            root.AddView(content, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, 0, 1f));
        }
        else
        {
            root.AddView(left, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, 0, 1f));
            root.AddView(right, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        }

        SetContentView(root);

        // Edge-to-edge (Android 15+): отступаем от системных панелей.
        root.SetOnApplyWindowInsetsListener(new InsetsListener());

        UpdateSummary();
    }

    private Button MakeButton(string text, Action onClick)
    {
        float d = Resources!.DisplayMetrics!.Density;
        bool wide = Resources.Configuration!.ScreenWidthDp >= 900;
        var b = new Button(this) { Text = text };
        b.SetTextSize(Android.Util.ComplexUnitType.Sp, wide ? 16 : 14);
        b.SetMinHeight((int)((wide ? 48 : 40) * d));
        var lp = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        { RightMargin = (int)(6 * d) };
        b.LayoutParameters = lp;
        b.Click += (_, _) =>
        {
            try { onClick(); }
            catch (Exception ex) { AddLog("Ошибка: " + ex.Message); }
        };
        return b;
    }

    // ---- Действия ----

    private void OnToggleEntry(PcEntry entry)
    {
        entry.Selected = !entry.Selected;
        _adapter.NotifyDataSetChanged();
        UpdateSummary();
    }

    private void SetAllSelected(bool value)
    {
        foreach (var e in _store.Items) e.Selected = value;
        _adapter.NotifyDataSetChanged();
        UpdateSummary();
    }

    private void OpenScreens() => StartActivity(new Intent(this, typeof(ScreensActivity)));

    /// <summary>Дистанционное включение (Wake-on-LAN) выбранных (или всех) ПК по сохранённому MAC.</summary>
    private void WakePcs()
    {
        var targets = _store.Items.Where(e => e.Selected).ToList();
        if (targets.Count == 0) targets = _store.Items.ToList();
        if (targets.Count == 0) { AddLog("Список ПК пуст."); return; }

        int sent = 0, noMac = 0;
        foreach (var e in targets)
        {
            if (string.IsNullOrEmpty(e.Mac))
            {
                noMac++;
                AddLog($"{e.Address}: нет MAC — нажмите «Обновить сведения», когда ПК включён");
                continue;
            }
            if (Wol.Wake(e.Mac, e.Address))
            {
                sent++;
                AddLog($"{e.Address}: magic-пакет отправлен ({e.Mac})");
            }
            else AddLog($"{e.Address}: ошибка отправки пакета");
        }
        AddLog($"WOL: отправлено {sent}, без MAC {noMac}");
    }

    private void UpdateSummary()
    {
        int total = _store.Items.Count;
        int sel = _store.Items.Count(e => e.Selected);
        RunOnUiThread(() => _summary.Text = sel == 0
            ? $"выбрано 0 из {total}"
            : (sel == total ? $"выбраны все ({total})" : $"выбрано {sel} из {total}"));
    }

    private async Task DiscoverAsync()
    {
        if (_busy) { AddLog("Операция уже выполняется…"); return; }
        _busy = true;
        SetStatus("Поиск…");
        AddLog($"Обнаружение (UDP {_settings.DiscoveryPort})…");

        WifiManager.MulticastLock? ml = null;
        try
        {
            if (GetSystemService(WifiService) is WifiManager wifi)
            {
                var l = wifi.CreateMulticastLock("netc_disc");
                l?.SetReferenceCounted(false);
                l?.Acquire();
                ml = l;
            }
        }
        catch { /* блокировка недоступна — пробуем без неё */ }

        try
        {
            var found = await NetcClientCore.DiscoverAsync(
                _settings.DiscoveryPort, _settings.SharedKey, 2500, CancellationToken.None);

            foreach (var pc in found)
            {
                ApplyInfo(pc.Address, pc.Info);
                AddLog($"Найден: {pc.Address} ({pc.Info.MachineName})");
            }
            _store.Save();
            RunOnUiThread(() => _adapter.NotifyDataSetChanged());
            SetStatus($"Найдено: {found.Count}");
        }
        catch (Exception ex)
        {
            AddLog("Ошибка обнаружения: " + ex.Message);
            SetStatus("Ошибка обнаружения");
        }
        finally
        {
            try { ml?.Release(); } catch { }
            _busy = false;
        }
    }

    private async Task RefreshAllAsync()
    {
        if (_busy) { AddLog("Операция уже выполняется…"); return; }
        var snapshot = _store.Items.ToList();
        if (snapshot.Count == 0) { AddLog("Список пуст."); return; }

        _busy = true;
        SetStatus("Обновление…");
        foreach (var e in snapshot) e.Status = PcStatus.Checking;
        RunOnUiThread(() => _adapter.NotifyDataSetChanged());

        using var sem = new SemaphoreSlim(8);
        var tasks = snapshot.Select(async e =>
        {
            await sem.WaitAsync();
            try
            {
                var info = await NetcClientCore.QueryAsync(
                    e.Address, _settings.ControlPort, _key, 4000, CancellationToken.None);
                ApplyInfo(e.Address, info);
                AddLog(e.Address + ": " + (info is null ? "нет ответа" : info.MachineName + " / " + info.UserName));
                RunOnUiThread(() => _adapter.NotifyDataSetChanged());
            }
            catch (Exception ex)
            {
                AddLog(e.Address + ": ошибка — " + ex.Message);
            }
            finally { sem.Release(); }
        }).ToList();

        await Task.WhenAll(tasks);
        _store.Save();
        SetStatus("Готово");
        _busy = false;
    }

    /// <summary>Обновляет запись ПК сведениями агента (или помечает «нет ответа»).</summary>
    private void ApplyInfo(string address, PcInfo? info)
    {
        var e = _store.Find(address);
        if (e is null)
        {
            e = new PcEntry { Address = address };
            _store.Items.Add(e);
        }

        if (info is null)
        {
            e.Status = PcStatus.NoReply;
            return;
        }

        e.Name = string.IsNullOrWhiteSpace(info.MachineName) ? e.Name : info.MachineName;
        e.User = info.UserName;
        e.Model = string.IsNullOrWhiteSpace(info.Model)
            ? e.Model
            : (string.IsNullOrWhiteSpace(info.Manufacturer) ? info.Model : info.Manufacturer + " " + info.Model);
        e.Os = info.OsVersion;
        if (!string.IsNullOrEmpty(info.MacAddress)) e.Mac = info.MacAddress;
        e.Status = PcStatus.Ok;
    }

    private void ShowAddDialog()
    {
        var input = new EditText(this) { Hint = "IP-адрес или имя компьютера" };
        float d = Resources!.DisplayMetrics!.Density;
        var wrap = new FrameLayout(this);
        wrap.SetPadding((int)(16 * d), 0, (int)(16 * d), 0);
        wrap.AddView(input);

        new AlertDialog.Builder(this)
            .SetTitle("Добавить ПК")
            ?.SetView(wrap)
            ?.SetPositiveButton("OK", (_, _) =>
            {
                var value = input.Text?.Trim() ?? "";
                if (value.Length == 0) return;
                if (_store.Find(value) is not null) { AddLog("ПК уже в списке: " + value); return; }
                _store.Items.Add(new PcEntry { Address = value });
                _store.Save();
                _adapter.NotifyDataSetChanged();
                UpdateSummary();
                AddLog("Добавлен ПК: " + value);
            })
            ?.SetNegativeButton("Отмена", (_, _) => { })
            ?.Show();
    }

    private void RemoveSelected()
    {
        var sel = _store.Items.Where(e => e.Selected).ToList();
        if (sel.Count == 0) { AddLog("Ничего не выделено."); return; }

        new AlertDialog.Builder(this)
            .SetTitle("Удалить ПК")
            ?.SetMessage($"Удалить из списка {sel.Count} ПК?")
            ?.SetPositiveButton("Удалить", (_, _) =>
            {
                foreach (var e in sel) _store.Items.Remove(e);
                _store.Save();
                _adapter.NotifyDataSetChanged();
                UpdateSummary();
                AddLog($"Удалено ПК: {sel.Count}");
            })
            ?.SetNegativeButton("Отмена", (_, _) => { })
            ?.Show();
    }

    private void ShowSettingsDialog()
    {
        float d = Resources!.DisplayMetrics!.Density;
        int Dp(int v) => (int)(v * d);
        var wrap = new LinearLayout(this) { Orientation = Orientation.Vertical };
        wrap.SetPadding(Dp(18), Dp(4), Dp(18), Dp(4));

        var editKey = new EditText(this) { Text = _settings.SharedKey };
        var editControl = new EditText(this) { Text = _settings.ControlPort.ToString() };
        editControl.InputType = Android.Text.InputTypes.ClassNumber;
        var editDisc = new EditText(this) { Text = _settings.DiscoveryPort.ToString() };
        editDisc.InputType = Android.Text.InputTypes.ClassNumber;

        wrap.AddView(new TextView(this) { Text = "Общий ключ:" });
        wrap.AddView(editKey);
        wrap.AddView(new TextView(this) { Text = "Порт управления:" });
        wrap.AddView(editControl);
        wrap.AddView(new TextView(this) { Text = "Порт обнаружения:" });
        wrap.AddView(editDisc);

        new AlertDialog.Builder(this)
            .SetTitle("Настройки")
            ?.SetView(wrap)
            ?.SetPositiveButton("OK", (_, _) =>
            {
                _settings.SharedKey = editKey.Text?.Trim() ?? "";
                if (int.TryParse(editControl.Text, out var cp) && cp is > 0 and <= 65535) _settings.ControlPort = cp;
                if (int.TryParse(editDisc.Text, out var dp) && dp is > 0 and <= 65535) _settings.DiscoveryPort = dp;
                _settings.Save(this);
                _key = NetcCrypto.DeriveKey(_settings.SharedKey);
                AddLog("Настройки сохранены. Ключ обновлён.");
                if (_settings.IsDefaultKey)
                    AddLog("ВНИМАНИЕ: оставлен ключ по умолчанию «netc-default-key».");
            })
            ?.SetNegativeButton("Отмена", (_, _) => { })
            ?.Show();
    }

    // ---- Журнал и статус ----

    private void AddLog(string message)
    {
        var line = DateTime.Now.ToString("HH:mm:ss") + "  " + message;
        RunOnUiThread(() =>
        {
            _logLines.Add(line);
            if (_logLines.Count > 300) _logLines.RemoveRange(0, _logLines.Count - 300);
            _log.Text = string.Join("\n", _logLines);
            _logScroll.Post(() => _logScroll.FullScroll(FocusSearchDirection.Down));
        });
    }

    private void SetStatus(string text) => RunOnUiThread(() => _status.Text = text);
}
