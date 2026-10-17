using NetcShared;

namespace NetcClient;

/// <summary>Окно учителя: список ПК, обнаружение и обновление сведений.</summary>
public sealed class MainForm : Form
{
    private readonly NetcSettings _settings;
    private byte[] _key;
    private readonly List<string> _addresses = new();
    private readonly DataGridView _grid;
    private readonly TextBox _log;
    private readonly ToolStripStatusLabel _status;
    private readonly string _listPath;
    private readonly Dictionary<string, string> _macs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SavedPc> _saved = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Сохранённые в netc_pcs.txt сведения о ПК (имя/модель/MAC).</summary>
    private sealed class SavedPc
    {
        public string Name = "";
        public string Model = "";
        public string Mac = "";
    }

    /// <summary>
    /// Список ПК класса по умолчанию (порядок и адреса соответствуют файлу ips.txt;
    /// имена R114-01…R114-24). Подставляется при первом запуске, когда список ещё не создан.
    /// </summary>
    private static readonly string[] DefaultAddresses =
    {
        "10.12.6.16",  // R114-01
        "10.12.6.17",  // R114-02
        "10.12.6.15",  // R114-03
        "10.12.6.19",  // R114-04
        "10.12.6.24",  // R114-05
        "10.12.5.27",  // R114-06
        "10.90.38.49", // R114-07
        "10.90.9.49",  // R114-08
        "10.90.3.153", // R114-09
        "10.90.1.127", // R114-10
        "10.90.3.38",  // R114-11
        "10.90.1.236", // R114-12
        "10.90.2.63",  // R114-13
        "10.90.7.186", // R114-14
        "10.12.6.13",  // R114-15
        "10.12.6.14",  // R114-16
        "10.12.6.10",  // R114-17
        "10.90.0.85",  // R114-18
        "10.12.6.12",  // R114-19
        "10.90.5.108", // R114-20
        "10.90.2.76",  // R114-21
        "10.99.4.16",  // R114-22
        "10.90.5.153", // R114-23
        "10.90.3.39"   // R114-24
    };

    /// <summary>Имена ПК по умолчанию (соответствуют порядку ips.txt: R114-01…R114-24).</summary>
    private static readonly Dictionary<string, string> DefaultNames = BuildDefaultNames();

    private static Dictionary<string, string> BuildDefaultNames()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < DefaultAddresses.Length; i++)
            map[DefaultAddresses[i]] = "R114-" + (i + 1).ToString("00");
        return map;
    }

    /// <summary>
    /// Модели ПК по умолчанию — заполняют колонку «Модель» до первого «Обновить сведения»
    /// (после подключения заменяются реальными данными агента). Инвентаризация — см. netc_PROGRESS.md §9.1.
    /// </summary>
    private static readonly Dictionary<string, string> DefaultModels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["10.12.6.16"]  = "Acer TravelMate P215-53", // R114-01
        ["10.12.6.17"]  = "Acer TravelMate P215-53", // R114-02
        ["10.12.6.15"]  = "Acer TravelMate P215-53", // R114-03
        ["10.12.6.19"]  = "Acer TravelMate P215-53", // R114-04
        ["10.12.6.24"]  = "Acer TravelMate P215-53", // R114-05
        ["10.12.5.27"]  = "Acer TravelMate P215-53", // R114-06
        ["10.90.38.49"] = "ICL RAYbook Bi1504",      // R114-07
        ["10.90.9.49"]  = "ICL RAYbook Bi1504",      // R114-08
        ["10.90.3.153"] = "ICL RAYbook Bi1504",      // R114-09
        ["10.90.1.127"] = "ICL RAYbook Bi1504",      // R114-10
        ["10.90.3.38"]  = "ICL RAYbook Bi1504",      // R114-11
        ["10.90.1.236"] = "ICL RAYbook Bi1504",      // R114-12
        ["10.90.2.63"]  = "ICL RAYbook Bi1504",      // R114-13
        ["10.90.7.186"] = "ICL RAYbook Bi1504",      // R114-14
        ["10.12.6.13"]  = "Acer TravelMate P215-53", // R114-15
        ["10.12.6.14"]  = "Acer TravelMate P215-53", // R114-16
        ["10.12.6.10"]  = "Acer TravelMate P215-53", // R114-17
        ["10.90.0.85"]  = "Acer TravelMate P215-53", // R114-18
        ["10.12.6.12"]  = "Acer TravelMate P215-53", // R114-19
        ["10.90.5.108"] = "ICL RAYbook Si1507",      // R114-20
        ["10.90.2.76"]  = "ICL RAYbook Bi1504",      // R114-21
        ["10.99.4.16"]  = "ICL RAYbook Bi1504",      // R114-22
        ["10.90.5.153"] = "ICL RAYbook Si159",       // R114-23
        ["10.90.3.39"]  = "ICL RAYbook Bi1504"       // R114-24
    };

    public MainForm()
    {
        _settings = NetcSettings.Load();
        _key = NetcCrypto.DeriveKey(_settings.SharedKey);
        _listPath = Path.Combine(AppContext.BaseDirectory, "netc_pcs.txt");

        Text = "netc_client — пульт учителя";
        ClientSize = new Size(920, 580);
        MinimumSize = new Size(720, 460);
        StartPosition = FormStartPosition.CenterScreen;

        var top = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Padding = new Padding(8, 8, 8, 4)
        };

        var btnDiscover = MakeButton("Обнаружить");
        btnDiscover.Click += async (_, _) => await DiscoverAsync();
        var btnRefresh = MakeButton("Обновить сведения");
        btnRefresh.Click += async (_, _) => await RefreshAllAsync();
        var btnAdd = MakeButton("Добавить ПК");
        btnAdd.Click += (_, _) => AddPc();
        var btnRemove = MakeButton("Удалить");
        btnRemove.Click += (_, _) => RemoveSelected();
        var btnSettings = MakeButton("Настройки");
        btnSettings.Click += OnSettings;
        var btnScreens = MakeButton("Просмотр экранов");
        btnScreens.Click += (_, _) => OpenScreens();
        var btnBroadcast = MakeButton("Трансляция / блокировка");
        btnBroadcast.Click += (_, _) => OpenBroadcast();
        var btnPower = MakeButton("Питание");
        btnPower.Click += (_, _) => OpenPower();
        var btnMessage = MakeButton("Сообщение");
        btnMessage.Click += (_, _) => OpenMessage();
        var btnFiles = MakeButton("Файлы");
        btnFiles.Click += (_, _) => OpenFiles();
        var btnRestrictions = MakeButton("Ограничения");
        btnRestrictions.Click += (_, _) => OpenRestrictions();
        var btnJournal = MakeButton("Журнал");
        btnJournal.Click += (_, _) => OpenJournal();
        var btnTest = MakeButton("Тест");
        btnTest.Click += (_, _) => OpenTest();
        var btnPers = MakeButton("Персонализация");
        btnPers.Click += (_, _) => OpenPersonalization();
        var btnWake = MakeButton("Включить");
        btnWake.Click += (_, _) => WakePcs();
        var btnCloseWin = MakeButton("Закрыть окна");
        btnCloseWin.Click += (_, _) => OpenCloseWindows();

        top.Controls.Add(btnDiscover);
        top.Controls.Add(btnRefresh);
        top.Controls.Add(btnAdd);
        top.Controls.Add(btnRemove);
        top.Controls.Add(btnScreens);
        top.Controls.Add(btnBroadcast);
        top.Controls.Add(btnPower);
        top.Controls.Add(btnMessage);
        top.Controls.Add(btnFiles);
        top.Controls.Add(btnRestrictions);
        top.Controls.Add(btnJournal);
        top.Controls.Add(btnTest);
        top.Controls.Add(btnPers);
        top.Controls.Add(btnWake);
        top.Controls.Add(btnCloseWin);
        top.Controls.Add(btnSettings);

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            AllowUserToResizeRows = false
        };
        _grid.Columns.Add("colAddr", "Адрес");
        _grid.Columns.Add("colName", "Имя ПК");
        _grid.Columns.Add("colUser", "Пользователь");
        _grid.Columns.Add("colModel", "Модель");
        _grid.Columns.Add("colOs", "ОС");
        _grid.Columns.Add("colStatus", "Статус");
        _grid.Columns[0].FillWeight = 16;
        _grid.Columns[1].FillWeight = 15;
        _grid.Columns[2].FillWeight = 14;
        _grid.Columns[3].FillWeight = 22;
        _grid.Columns[4].FillWeight = 20;
        _grid.Columns[5].FillWeight = 13;

        _log = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9F),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.Gainsboro
        };

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            Panel1MinSize = 150,
            Panel2MinSize = 80,
            SplitterDistance = 400
        };
        split.Panel1.Controls.Add(_grid);
        split.Panel2.Controls.Add(_log);

        var status = new StatusStrip();
        _status = new ToolStripStatusLabel("Готово");
        status.Items.Add(_status);

        Controls.Add(split);
        Controls.Add(top);
        Controls.Add(status);

        LoadAddresses();
        Populate();

        if (string.Equals(_settings.SharedKey, "netc-default-key", StringComparison.Ordinal))
        {
            Log("ВНИМАНИЕ: используется ключ по умолчанию «netc-default-key». " +
                "Задайте свой общий ключ в «Настройках» и разложите его на ученические ПК.");
        }

        // Задаём после добавления контролов, иначе масштабируется только форма (наезды при высоком DPI).
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    private static Button MakeButton(string text)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(90, 30),
            Padding = new Padding(8, 0, 8, 0),
            Margin = new Padding(3, 6, 3, 3)
        };
        return b;
    }

    private void Log(string message)
    {
        _log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);
    }

    private void LoadAddresses()
    {
        _addresses.Clear();
        _saved.Clear();
        bool loaded = false;
        try
        {
            if (File.Exists(_listPath))
            {
                foreach (var line in File.ReadAllLines(_listPath))
                {
                    var t = line.Trim();
                    if (t.Length == 0) continue;
                    // Формат строки: «адрес|имя|модель|mac» (старый формат — только адрес).
                    var parts = t.Split('|');
                    var addr = parts[0].Trim();
                    if (addr.Length == 0) continue;
                    _addresses.Add(addr);
                    loaded = true;
                    if (parts.Length > 1)
                    {
                        var sp = new SavedPc
                        {
                            Name = parts.Length > 1 ? parts[1].Trim() : "",
                            Model = parts.Length > 2 ? parts[2].Trim() : "",
                            Mac = parts.Length > 3 ? parts[3].Trim() : ""
                        };
                        _saved[addr] = sp;
                        if (sp.Mac.Length > 0) _macs[addr] = sp.Mac;
                    }
                }
            }
        }
        catch { }

        // Первый запуск (список ещё не создан или пуст) — берём известные ПК класса:
        // сначала ips.txt рядом с exe (единый источник с развёртыванием), иначе встроенный список.
        if (!loaded)
        {
            var fromIps = TryReadIpsFile();
            _addresses.AddRange(fromIps.Count > 0 ? fromIps : (IEnumerable<string>)DefaultAddresses);
            SaveAddresses();
        }
    }

    /// <summary>Читает адреса из ips.txt рядом с exe/в рабочем каталоге (по одному в строке;
    /// строка формата «адрес|имя|модель|mac» — берётся только адрес).</summary>
    private static List<string> TryReadIpsFile()
    {
        var result = new List<string>();
        try
        {
            foreach (var dir in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                var path = Path.Combine(dir, "ips.txt");
                if (!File.Exists(path)) continue;
                foreach (var line in File.ReadAllLines(path))
                {
                    var t = line.Trim();
                    if (t.Length == 0 || t.StartsWith("#") || t.StartsWith("//")) continue;
                    var addr = t.Split('|')[0].Trim();
                    if (addr.Length > 0 && !result.Contains(addr, StringComparer.OrdinalIgnoreCase))
                        result.Add(addr);
                }
                if (result.Count > 0) break;
            }
        }
        catch { }
        return result;
    }

    private void SaveAddresses()
    {
        try
        {
            var lines = new List<string>();
            foreach (var addr in _addresses)
            {
                var row = FindRow(addr);
                string name = row is null ? "" : (Convert.ToString(row.Cells[1].Value) ?? "");
                string model = row is null ? "" : (Convert.ToString(row.Cells[3].Value) ?? "");
                string mac = _macs.TryGetValue(addr, out var m) ? m : "";
                // Если известны только адрес — пишем как раньше, иначе расширенную строку.
                if (name.Length == 0 && model.Length == 0 && mac.Length == 0) lines.Add(addr);
                else lines.Add(string.Join("|", addr, San(name), San(model), San(mac)));
            }
            File.WriteAllLines(_listPath, lines);
        }
        catch { }
    }

    private static string San(string value) =>
        value.Replace("|", " ").Replace("\r", " ").Replace("\n", " ").Trim();

    private void Populate()
    {
        _grid.Rows.Clear();
        foreach (var addr in _addresses)
        {
            _saved.TryGetValue(addr, out var sp);
            string name = !string.IsNullOrWhiteSpace(sp?.Name) ? sp!.Name
                : (DefaultNames.TryGetValue(addr, out var n) ? n : "");
            string model = !string.IsNullOrWhiteSpace(sp?.Model) ? sp!.Model
                : (DefaultModels.TryGetValue(addr, out var m) ? m : "");
            int i = _grid.Rows.Add(addr, name, "", model, "", "—");
            _grid.Rows[i].Tag = addr;
        }
        _status.Text = "Компьютеров: " + _addresses.Count;
    }

    private DataGridViewRow? FindRow(string address)
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (string.Equals(Convert.ToString(row.Tag), address, StringComparison.OrdinalIgnoreCase))
                return row;
        }
        return null;
    }

    private void UpsertRow(string address, PcInfo? info)
    {
        var row = FindRow(address);
        if (row is null)
        {
            if (!_addresses.Contains(address, StringComparer.OrdinalIgnoreCase))
            {
                _addresses.Add(address);
                SaveAddresses();
            }
            int i = _grid.Rows.Add(address, "", "", DefaultModels.TryGetValue(address, out var dm) ? dm : "", "", "—");
            row = _grid.Rows[i];
            row.Tag = address;
        }

        if (info is null)
        {
            row.Cells[5].Value = "нет ответа";
            row.DefaultCellStyle.ForeColor = Color.DarkOrange;
        }
        else
        {
            row.Cells[1].Value = info.MachineName;
            row.Cells[2].Value = info.UserName;
            row.Cells[3].Value = string.IsNullOrWhiteSpace(info.Model)
                ? ""
                : (string.IsNullOrWhiteSpace(info.Manufacturer) ? info.Model : info.Manufacturer + " " + info.Model);
            row.Cells[4].Value = info.OsVersion;
            row.Cells[5].Value = "OK";
            row.DefaultCellStyle.ForeColor = Color.Black;
            if (!string.IsNullOrEmpty(info.MacAddress)) _macs[address] = info.MacAddress;
        }
    }

    private async Task DiscoverAsync()
    {
        _status.Text = "Поиск…";
        Log("Обнаружение (UDP " + _settings.DiscoveryPort + ")…");
        try
        {
            var found = await NetcClientCore.DiscoverAsync(_settings.DiscoveryPort, _settings.SharedKey, 2500, CancellationToken.None);
            foreach (var pc in found)
            {
                UpsertRow(pc.Address, pc.Info);
                Log("Найден: " + pc.Address + " (" + pc.Info.MachineName + ")");
            }
            SaveAddresses();
            _status.Text = "Найдено: " + found.Count;
        }
        catch (Exception ex)
        {
            Log("Ошибка обнаружения: " + ex.Message);
        }
    }

    private async Task RefreshAllAsync()
    {
        if (_addresses.Count == 0) { Log("Список пуст."); return; }
        _status.Text = "Обновление…";
        var addrs = _addresses.ToList();
        // Параллельно с ограничением числа одновременных подключений (24 ПК → не ждём по 4 с каждый).
        using var sem = new SemaphoreSlim(8);
        var tasks = addrs.Select(async addr =>
        {
            await sem.WaitAsync();
            try
            {
                var info = await NetcClientCore.QueryAsync(addr, _settings.ControlPort, _key, 4000, CancellationToken.None);
                UpsertRow(addr, info);
                Log(addr + ": " + (info is null ? "нет ответа" : info.MachineName + " / " + info.UserName));
            }
            finally { sem.Release(); }
        }).ToList();
        await Task.WhenAll(tasks);
        SaveAddresses();
        _status.Text = "Готово";
    }

    private void AddPc()
    {
        var value = Prompt("Добавить ПК", "IP-адрес или имя компьютера:");
        if (string.IsNullOrWhiteSpace(value)) return;
        value = value.Trim();
        if (!_addresses.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            _addresses.Add(value);
            string model = DefaultModels.TryGetValue(value, out var m) ? m : "";
            int i = _grid.Rows.Add(value, "", "", model, "", "—");
            _grid.Rows[i].Tag = value;
            SaveAddresses();
        }
    }

    private void RemoveSelected()
    {
        // Сначала собираем адреса выбранных строк, потом удаляем (безопасно при обходе SelectedRows).
        var addrs = new List<string>();
        foreach (DataGridViewRow row in _grid.SelectedRows)
        {
            if (row.IsNewRow) continue;
            string? addr = Convert.ToString(row.Tag);
            if (!string.IsNullOrEmpty(addr)) addrs.Add(addr!);
        }
        foreach (var addr in addrs)
        {
            _addresses.RemoveAll(a => string.Equals(a, addr, StringComparison.OrdinalIgnoreCase));
            _saved.Remove(addr);
            _macs.Remove(addr);
        }
        SaveAddresses();
        Populate();
    }

    private void OpenScreens()
    {
        if (_addresses.Count == 0)
        {
            Log("Список ПК пуст — сначала добавьте или обнаружьте компьютеры.");
            return;
        }

        // Карта «адрес → имя ПК» из таблицы (заполняется кнопкой «Обновить сведения»).
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) continue;
            string? addr = Convert.ToString(row.Tag);
            string? name = Convert.ToString(row.Cells[1].Value);
            if (!string.IsNullOrEmpty(addr) && !string.IsNullOrWhiteSpace(name)) names[addr!] = name!;
        }

        var form = new ScreenWallForm(_addresses.ToList(), names, _settings, _key);
        form.Show(this);
    }

    private void OpenBroadcast()
    {
        if (_addresses.Count == 0)
        {
            Log("Список ПК пуст — сначала добавьте или обнаружьте компьютеры.");
            return;
        }
        var form = new BroadcastForm(BuildTargetSet(), _settings, _key);
        form.Show(this);
    }

    /// <summary>Набор целевых ПК для окон действий: все ПК, их имена и текущее выделение в таблице.</summary>
    private TargetSet BuildTargetSet()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow) continue;
            string? addr = Convert.ToString(row.Tag);
            string? name = Convert.ToString(row.Cells[1].Value);
            if (!string.IsNullOrEmpty(addr)) names[addr!] = name ?? "";
        }

        var all = _addresses.ToList();
        var namesList = all.Select(a => names.TryGetValue(a, out var n) ? n : "").ToList();
        return new TargetSet(all, namesList, SelectedOrAll());
    }

    /// <summary>Выбранные строки, а если ничего не выбрано — все ПК из списка.</summary>
    private List<string> SelectedOrAll()
    {
        var sel = new List<string>();
        foreach (DataGridViewRow row in _grid.SelectedRows)
        {
            if (row.IsNewRow) continue;
            string? addr = Convert.ToString(row.Tag);
            if (!string.IsNullOrEmpty(addr)) sel.Add(addr!);
        }
        if (sel.Count == 0) sel.AddRange(_addresses);
        return sel;
    }

    private void OpenPower()
    {
        if (_addresses.Count == 0) { Log("Список ПК пуст — сначала добавьте или обнаружьте компьютеры."); return; }
        new PowerForm(BuildTargetSet(), _settings, _key).Show(this);
    }

    private void OpenMessage()
    {
        if (_addresses.Count == 0) { Log("Список ПК пуст — сначала добавьте или обнаружьте компьютеры."); return; }
        new MessageForm(BuildTargetSet(), _settings, _key).Show(this);
    }

    private void OpenFiles()
    {
        if (_addresses.Count == 0) { Log("Список ПК пуст — сначала добавьте или обнаружьте компьютеры."); return; }
        new FileForm(BuildTargetSet(), _settings, _key).Show(this);
    }

    private void OpenRestrictions()
    {
        if (_addresses.Count == 0) { Log("Список ПК пуст — сначала добавьте или обнаружьте компьютеры."); return; }
        new RestrictionsForm(BuildTargetSet(), _settings, _key).Show(this);
    }

    private void OpenJournal()
    {
        if (_addresses.Count == 0) { Log("Список ПК пуст — сначала добавьте или обнаружьте компьютеры."); return; }
        new JournalForm(BuildTargetSet(), _settings, _key).Show(this);
    }

    private void OpenTest()
    {
        if (_addresses.Count == 0) { Log("Список ПК пуст — сначала добавьте или обнаружьте компьютеры."); return; }
        new TestForm(BuildTargetSet(), _settings, _key).Show(this);
    }

    private void OpenPersonalization()
    {
        if (_addresses.Count == 0) { Log("Список ПК пуст — сначала добавьте или обнаружьте компьютеры."); return; }
        new PersonalizationForm(BuildTargetSet(), _settings, _key).Show(this);
    }

    private void OpenCloseWindows()
    {
        if (_addresses.Count == 0) { Log("Список ПК пуст — сначала добавьте или обнаружьте компьютеры."); return; }
        new CloseWindowsForm(BuildTargetSet(), _settings, _key).Show(this);
    }

    /// <summary>Дистанционное включение (Wake-on-LAN) выбранных ПК.</summary>
    private void WakePcs()
    {        var targets = SelectedOrAll();
        if (targets.Count == 0) { Log("Список ПК пуст — сначала добавьте или обнаружьте компьютеры."); return; }

        int sent = 0, noMac = 0;
        foreach (string addr in targets)
        {
            if (!_macs.TryGetValue(addr, out var mac) || string.IsNullOrEmpty(mac))
            {
                noMac++;
                Log(addr + ": нет MAC — нажмите «Обновить сведения», когда ПК включён.");
                continue;
            }
            if (Wol.Wake(mac, addr)) { sent++; Log(addr + ": magic-пакет отправлен (" + mac + ")"); }
            else Log(addr + ": ошибка отправки пакета");
        }
        Log($"WOL: отправлено {sent}, без MAC {noMac}");
    }

    private void OnSettings(object? sender, EventArgs e)
    {
        using var dlg = new Form
        {
            Text = "Настройки",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(430, 180),
            MaximizeBox = false,
            MinimizeBox = false
        };

        var lblKey = new Label { Text = "Общий ключ:", AutoSize = true, Location = new Point(12, 18) };
        var txtKey = new TextBox { Text = _settings.SharedKey, Location = new Point(130, 15), Width = 280 };
        var lblPort = new Label { Text = "Порт управления:", AutoSize = true, Location = new Point(12, 52) };
        var numPort = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = _settings.ControlPort, Location = new Point(130, 49), Width = 100 };
        var lblDisc = new Label { Text = "Порт обнаружения:", AutoSize = true, Location = new Point(12, 86) };
        var numDisc = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = _settings.DiscoveryPort, Location = new Point(130, 83), Width = 100 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(245, 130), Width = 80, Height = 28 };
        var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Location = new Point(331, 130), Width = 80, Height = 28 };

        dlg.Controls.AddRange(new Control[] { lblKey, txtKey, lblPort, numPort, lblDisc, numDisc, ok, cancel });
        dlg.AcceptButton = ok;
        dlg.CancelButton = cancel;
        dlg.AutoScaleDimensions = new SizeF(96F, 96F);
        dlg.AutoScaleMode = AutoScaleMode.Dpi;

        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _settings.SharedKey = txtKey.Text.Trim();
            _settings.ControlPort = (int)numPort.Value;
            _settings.DiscoveryPort = (int)numDisc.Value;
            _settings.Save();
            _key = NetcCrypto.DeriveKey(_settings.SharedKey);
            Log("Настройки сохранены. Ключ обновлён.");
            if (string.Equals(_settings.SharedKey, "netc-default-key", StringComparison.Ordinal))
            {
                MessageBox.Show(this,
                    "Оставлен ключ по умолчанию «netc-default-key». Любой в этой сети, кто его знает,\n" +
                    "сможет управлять ученическими ПК. Задайте свой ключ и разложите его на всех агентах.",
                    "Безопасность", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    private static string? Prompt(string title, string label)
    {
        using var dlg = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(380, 120),
            MaximizeBox = false,
            MinimizeBox = false
        };
        var lbl = new Label { Text = label, AutoSize = true, Location = new Point(12, 15) };
        var txt = new TextBox { Location = new Point(12, 40), Width = 356 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(212, 78), Width = 75 };
        var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Location = new Point(293, 78), Width = 75 };
        dlg.Controls.AddRange(new Control[] { lbl, txt, ok, cancel });
        dlg.AcceptButton = ok;
        dlg.CancelButton = cancel;
        dlg.AutoScaleDimensions = new SizeF(96F, 96F);
        dlg.AutoScaleMode = AutoScaleMode.Dpi;
        return dlg.ShowDialog() == DialogResult.OK ? txt.Text : null;
    }
}
