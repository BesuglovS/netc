using System.Text.Json;
using NetcShared;

namespace NetcClient;

/// <summary>
/// Составление тестов (вопросы разного типа), выдача их ученикам, досрочное завершение
/// и сбор результатов с экспортом.
/// </summary>
public sealed class TestForm : Form
{
    private const int ResultTimeoutMs = 0; // 0 — ждать сдачу без ограничения (снимается кнопкой)

    private readonly TargetsSelector _targets;
    private readonly NetcSettings _settings;
    private readonly byte[] _key;

    private readonly List<TestQuestion> _questions = new();
    private string _testId = Guid.NewGuid().ToString("N");

    private readonly TextBox _title;
    private readonly NumericUpDown _timer;
    private readonly CheckBox _shuffleQuestions;
    private readonly CheckBox _shuffleOptions;
    private readonly CheckBox _showResult;
    private readonly NumericUpDown _passPercent;
    private readonly TextBox _pin;
    private readonly DataGridView _grid;
    private readonly DataGridView _results;
    private readonly TextBox _log;
    private readonly Button _send;
    private readonly Button _stop;

    private CancellationTokenSource? _cts;
    private readonly object _gate = new();
    private readonly Dictionary<string, ScreenClient> _active = new(StringComparer.OrdinalIgnoreCase);

    public TestForm(TargetSet targets, NetcSettings settings, byte[] key)
    {
        _settings = settings;
        _key = key;

        Text = "Тест (" + targets.Initial.Count + " ПК)";
        ClientSize = new Size(980, 760);
        MinimumSize = new Size(820, 620);
        StartPosition = FormStartPosition.CenterParent;

        _title = new TextBox { Width = 240, Text = "Контрольная работа" };
        _timer = new NumericUpDown { Width = 64, Minimum = 0, Maximum = 600 };
        _shuffleQuestions = new CheckBox { Text = "Перемешивать вопросы", AutoSize = true };
        _shuffleOptions = new CheckBox { Text = "Перемешивать варианты", AutoSize = true };
        _showResult = new CheckBox { Text = "Показывать результат ученику", AutoSize = true, Checked = true };
        _passPercent = new NumericUpDown { Width = 64, Minimum = 0, Maximum = 100 };
        _pin = new TextBox { Width = 140 };

        var lblTitle = new Label { Text = "Название:", AutoSize = true, Margin = new Padding(0, 5, 4, 0) };
        var lblTimer = new Label { Text = "Таймер (мин), 0 — без лимита:", AutoSize = true, Margin = new Padding(24, 5, 4, 0) };
        var lblPass = new Label { Text = "Проходной %:", AutoSize = true, Margin = new Padding(0, 5, 4, 0) };
        var lblPin = new Label { Text = "PIN аварийного выхода:", AutoSize = true, Margin = new Padding(24, 5, 4, 0) };
        var lblQuestions = new Label { Text = "Вопросы:", Location = new Point(12, 108), AutoSize = true };

        // Строки параметров на FlowLayoutPanel — подписи не наезжают на поля при любом DPI.
        var row1 = SettingsRow(new Point(12, 8));
        row1.Controls.AddRange(new Control[] { lblTitle, _title, lblTimer, _timer });
        var row2 = SettingsRow(new Point(12, 44));
        row2.Controls.AddRange(new Control[] { _shuffleQuestions, _shuffleOptions, _showResult });
        var row3 = SettingsRow(new Point(12, 74));
        row3.Controls.AddRange(new Control[] { lblPass, _passPercent, lblPin, _pin });

        _grid = new DataGridView
        {
            Location = new Point(12, 132),
            Size = new Size(956, 232),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            AllowUserToAddRows = false,
            RowHeadersVisible = false,
            ReadOnly = true,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            AllowUserToResizeRows = false
        };
        _grid.Columns.Add("colType", "Тип");
        _grid.Columns.Add("colQ", "Вопрос");
        _grid.Columns.Add("colAns", "Верный ответ");
        _grid.Columns.Add("colPts", "Баллы");
        _grid.Columns["colType"]!.FillWeight = 16;
        _grid.Columns["colQ"]!.FillWeight = 46;
        _grid.Columns["colAns"]!.FillWeight = 30;
        _grid.Columns["colPts"]!.FillWeight = 8;
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) EditSelected(); };

        var btnAdd = new Button { Text = "Добавить", Location = new Point(12, 374), Width = 130, Height = 30 };
        btnAdd.Click += (_, _) => AddQuestion();
        var btnEdit = new Button { Text = "Изменить", Location = new Point(150, 374), Width = 130, Height = 30 };
        btnEdit.Click += (_, _) => EditSelected();
        var btnDel = new Button { Text = "Удалить", Location = new Point(288, 374), Width = 130, Height = 30 };
        btnDel.Click += (_, _) => DeleteSelected();
        var btnUp = new Button { Text = "Вверх", Location = new Point(426, 374), Width = 90, Height = 30 };
        btnUp.Click += (_, _) => MoveQuestion(-1);
        var btnDown = new Button { Text = "Вниз", Location = new Point(522, 374), Width = 90, Height = 30 };
        btnDown.Click += (_, _) => MoveQuestion(1);
        var btnImport = new Button { Text = "Импорт JSON", Location = new Point(680, 374), Width = 140, Height = 30 };
        btnImport.Click += (_, _) => ImportJson();
        var btnExport = new Button { Text = "Экспорт JSON", Location = new Point(826, 374), Width = 140, Height = 30 };
        btnExport.Click += (_, _) => ExportJson();

        _send = new Button { Text = "Отправить тест", Location = new Point(12, 414), Width = 170, Height = 34 };
        _send.Click += async (_, _) => await SendAsync();
        _stop = new Button { Text = "Завершить тест", Location = new Point(190, 414), Width = 170, Height = 34, Enabled = false };
        _stop.Click += async (_, _) => await StopAsync();
        var btnCsv = new Button { Text = "Экспорт результатов (CSV)", Location = new Point(770, 414), Width = 196, Height = 34 };
        btnCsv.Click += (_, _) => ExportCsv();

        var lblResults = new Label { Text = "Результаты:", Location = new Point(12, 458), AutoSize = true };
        _results = new DataGridView
        {
            Location = new Point(12, 482),
            Size = new Size(956, 140),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            AllowUserToAddRows = false,
            RowHeadersVisible = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            AllowUserToResizeRows = false
        };
        _results.Columns.Add("rPc", "ПК");
        _results.Columns.Add("rStudent", "Ученик");
        _results.Columns.Add("rScore", "Баллы");
        _results.Columns.Add("rPct", "%");
        _results.Columns.Add("rTime", "Время");
        _results.Columns.Add("rReason", "Итог");
        _results.Columns["rPc"]!.FillWeight = 16;
        _results.Columns["rStudent"]!.FillWeight = 16;
        _results.Columns["rScore"]!.FillWeight = 14;
        _results.Columns["rPct"]!.FillWeight = 10;
        _results.Columns["rTime"]!.FillWeight = 14;
        _results.Columns["rReason"]!.FillWeight = 30;

        _log = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9F),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.Gainsboro,
            Bounds = new Rectangle(12, 632, 956, 116),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };

        Controls.AddRange(new Control[]
        {
            row1, row2, row3,
            lblQuestions, _grid,
            btnAdd, btnEdit, btnDel, btnUp, btnDown, btnImport, btnExport,
            _send, _stop, btnCsv, lblResults, _results, _log
        });

        _targets = TargetsHelper.AddTargetsBar(this, targets);

        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;

        RefreshGrid();
    }

    private void Log(string message) =>
        UiSafe.Append(_log, DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine);

    // ------------------------------------------------------------------ Редактор

    private void RefreshGrid()
    {
        int selected = _grid.CurrentRow?.Index ?? -1;
        _grid.Rows.Clear();
        foreach (var q in _questions)
        {
            _grid.Rows.Add(KindLabel(q.Kind), Shorten(q.Text, 120), AnswerSummary(q), FmtPoints(q.Points));
        }
        if (selected >= 0 && selected < _grid.Rows.Count) _grid.CurrentCell = _grid.Rows[selected].Cells[0];
    }

    private int CurrentIndex() => _grid.CurrentRow?.Index ?? -1;

    private void AddQuestion()
    {
        using var dlg = new QuestionEditorForm();
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _questions.Add(dlg.Result);
        RefreshGrid();
    }

    private void EditSelected()
    {
        int i = CurrentIndex();
        if (i < 0 || i >= _questions.Count) { Log("Выберите вопрос."); return; }
        using var dlg = new QuestionEditorForm(_questions[i]);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _questions[i] = dlg.Result;
        RefreshGrid();
        _grid.CurrentCell = _grid.Rows[Math.Min(i, _grid.Rows.Count - 1)].Cells[0];
    }

    private void DeleteSelected()
    {
        int i = CurrentIndex();
        if (i < 0 || i >= _questions.Count) { Log("Выберите вопрос."); return; }
        _questions.RemoveAt(i);
        RefreshGrid();
    }

    private void MoveQuestion(int delta)
    {
        int i = CurrentIndex();
        int j = i + delta;
        if (i < 0 || j < 0 || j >= _questions.Count) return;
        (_questions[i], _questions[j]) = (_questions[j], _questions[i]);
        RefreshGrid();
        _grid.CurrentCell = _grid.Rows[j].Cells[0];
    }

    // ------------------------------------------------------------------ JSON

    private void ExportJson()
    {
        if (_questions.Count == 0) { Log("Нет вопросов для экспорта."); return; }
        using var dlg = new SaveFileDialog { Filter = "Тест (*.json)|*.json", FileName = "test.json" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var def = BuildDefinition();
            File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(def, Json.Options));
            Log("Экспортировано: " + dlg.FileName);
        }
        catch (Exception ex) { Log("Ошибка экспорта: " + ex.Message); }
    }

    private void ImportJson()
    {
        using var dlg = new OpenFileDialog { Filter = "Тест (*.json)|*.json|Все файлы (*.*)|*.*" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var def = JsonSerializer.Deserialize<TestDefinition>(File.ReadAllText(dlg.FileName), Json.Options);
            if (def is null || def.Questions.Count == 0) { Log("Файл не содержит вопросов."); return; }
            _questions.Clear();
            _questions.AddRange(def.Questions);
            ApplyDefinition(def);
            RefreshGrid();
            Log($"Импортировано {def.Questions.Count} вопр. из {Path.GetFileName(dlg.FileName)}");
        }
        catch (Exception ex) { Log("Ошибка импорта: " + ex.Message); }
    }

    private void ApplyDefinition(TestDefinition def)
    {
        _testId = string.IsNullOrWhiteSpace(def.Id) ? Guid.NewGuid().ToString("N") : def.Id;
        _title.Text = def.Title;
        _timer.Value = Math.Clamp(def.DurationSeconds / 60, 0, 600);
        _shuffleQuestions.Checked = def.ShuffleQuestions;
        _shuffleOptions.Checked = def.ShuffleOptions;
        _showResult.Checked = def.ShowResultToStudent;
        _passPercent.Value = (decimal)Math.Clamp(def.PassPercent, 0, 100);
        _pin.Text = def.EscapePin;
    }

    private TestDefinition BuildDefinition() => new()
    {
        Id = _testId,
        Title = string.IsNullOrWhiteSpace(_title.Text) ? "Тест" : _title.Text.Trim(),
        Questions = _questions.ToList(),
        DurationSeconds = (int)_timer.Value * 60,
        ShuffleQuestions = _shuffleQuestions.Checked,
        ShuffleOptions = _shuffleOptions.Checked,
        ShowResultToStudent = _showResult.Checked,
        PassPercent = (double)_passPercent.Value,
        EscapePin = _pin.Text.Trim()
    };

    // ------------------------------------------------------------------ Отправка

    private async Task SendAsync()
    {
        if (_questions.Count == 0) { Log("Добавьте хотя бы один вопрос."); return; }

        // Новый идентификатор на каждую отправку: иначе у ученика мог подгрузиться черновик
        // предыдущей (прерванной) попытки того же теста.
        _testId = Guid.NewGuid().ToString("N");
        var def = BuildDefinition();
        UiSafe.SetEnabled(_send, false);
        UiSafe.SetEnabled(_stop, true);
        _results.Rows.Clear();
        _cts = new CancellationTokenSource();

        Log($"Отправка теста «{def.Title}» ({def.Questions.Count} вопр.) на {_targets.Selected.Count} ПК" +
            (def.DurationSeconds > 0 ? $", таймер {def.DurationSeconds / 60} мин" : ", без лимита времени") + "…");

        var tasks = _targets.Selected.Select(addr => RunOneAsync(addr, def, _cts.Token)).ToList();
        await Task.WhenAll(tasks);

        Log("Готово.");
        UiSafe.SetEnabled(_send, true);
        UiSafe.SetEnabled(_stop, false);
    }

    private async Task RunOneAsync(string addr, TestDefinition def, CancellationToken ct)
    {
        var client = new ScreenClient(addr, _settings.ControlPort, _key);
        lock (_gate) _active[addr] = client;
        try
        {
            var res = await client.SendTestAsync(def, ResultTimeoutMs, ct);
            if (res is null) Log($"{addr}: результат не получен (таймаут/отмена)");
            else Report(addr, res);
        }
        catch (OperationCanceledException) { Log($"{addr}: отменено"); }
        catch (Exception ex) { Log($"{addr}: ошибка — {ex.Message}"); }
        finally
        {
            lock (_gate) _active.Remove(addr);
            client.Dispose();
        }
    }

    private async Task StopAsync()
    {
        ScreenClient[] clients;
        lock (_gate) clients = _active.Values.ToArray();
        if (clients.Length == 0) { Log("Нет активных тестов."); return; }

        UiSafe.SetEnabled(_stop, false);
        Log($"Досрочное завершение теста на {clients.Length} ПК…");
        foreach (var c in clients)
        {
            try { await c.StopTestAsync("teacher"); }
            catch (Exception ex) { Log($"{c.Host}: ошибка завершения — {ex.Message}"); }
        }
        Log("Команда завершения отправлена; результаты придут по мере ответа ПК.");
    }

    private void Report(string addr, TestResult res)
    {
        string reason = res.EndReason switch
        {
            "submit" => "сдал",
            "timeout" => "время вышло",
            "teacher" => "снят учителем",
            "escape" => "аварийный выход",
            "replaced" => "заменён",
            "disconnect" => "связь потеряна",
            _ => res.EndReason
        };

        var minutes = TimeSpan.FromMilliseconds(res.DurationMs).ToString(@"mm\:ss");
        int i = _results.Rows.Add(
            addr,
            res.Student,
            res.ManualCount > 0
                ? $"{Fmt(res.Score)}/{Fmt(res.MaxScore)} (+{Fmt(res.PendingManual)} ручн.)"
                : $"{Fmt(res.Score)}/{Fmt(res.MaxScore)}",
            res.Percent + "%",
            minutes,
            reason);
        if (res.Completed && res.Passed) _results.Rows[i].DefaultCellStyle.ForeColor = Color.FromArgb(22, 130, 93);
        else if (!res.Completed) _results.Rows[i].DefaultCellStyle.ForeColor = Color.DarkOrange;

        Log($"{addr}: {res.Student} — {Fmt(res.Score)}/{Fmt(res.MaxScore)} ({res.Percent}%), {reason}");
    }

    private void ExportCsv()
    {
        if (_results.Rows.Count == 0) { Log("Нет результатов для экспорта."); return; }
        using var dlg = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "test-results.csv" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("ПК;Ученик;Баллы;Процент;Время;Итог");
            foreach (DataGridViewRow row in _results.Rows)
            {
                if (row.IsNewRow) continue;
                var cells = new[]
                {
                    Convert.ToString(row.Cells[0].Value), Convert.ToString(row.Cells[1].Value),
                    Convert.ToString(row.Cells[2].Value), Convert.ToString(row.Cells[3].Value),
                    Convert.ToString(row.Cells[4].Value), Convert.ToString(row.Cells[5].Value)
                };
                sb.AppendLine(string.Join(";", cells.Select(Csv)));
            }
            File.WriteAllText(dlg.FileName, sb.ToString(), System.Text.Encoding.UTF8);
            Log("Результаты сохранены: " + dlg.FileName);
        }
        catch (Exception ex) { Log("Ошибка экспорта: " + ex.Message); }
    }

    private static string Csv(string? v)
    {
        v ??= "";
        return v.Contains(';') || v.Contains('"') ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }

    private static FlowLayoutPanel SettingsRow(Point location) => new()
    {
        Location = location,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        WrapContents = false,
        FlowDirection = FlowDirection.LeftToRight
    };

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        try { _cts?.Cancel(); } catch { }
        ScreenClient[] clients;
        lock (_gate) clients = _active.Values.ToArray();
        foreach (var c in clients) { try { _ = c.StopTestAsync("teacher"); } catch { } }
        base.OnFormClosing(e);
    }

    // ------------------------------------------------------------------ Утилиты

    private static string Fmt(double v) => v == Math.Floor(v) ? ((int)v).ToString() : v.ToString("0.##");

    private static string FmtPoints(double p) => Fmt(p);

    private static string Shorten(string s, int max)
    {
        s = s.Replace("\r", " ").Replace("\n", " ").Trim();
        return s.Length <= max ? s : s[..max] + "…";
    }

    private static string AnswerSummary(TestQuestion q) => q.Kind switch
    {
        QuestionKind.Single when q.CorrectIndex >= 0 && q.CorrectIndex < q.Options.Count => q.Options[q.CorrectIndex],
        QuestionKind.TrueFalse => q.CorrectIndex == 1 ? "Неверно" : "Верно",
        QuestionKind.Multi => string.Join(", ", q.CorrectIndices.Where(i => i >= 0 && i < q.Options.Count).Select(i => q.Options[i])),
        QuestionKind.ShortText => string.Join(" / ", q.CorrectText),
        QuestionKind.Numeric => q.Tolerance > 0
            ? $"{q.CorrectNumber:0.######} ± {q.Tolerance:0.######}"
            : q.CorrectNumber.ToString("0.######"),
        QuestionKind.Essay => "— (вручную)",
        QuestionKind.Matching => "пары: " + q.MatchLeft.Count,
        _ => ""
    };

    private static string KindLabel(QuestionKind k) => k switch
    {
        QuestionKind.Single => "Один вариант",
        QuestionKind.Multi => "Несколько",
        QuestionKind.TrueFalse => "Верно/Неверно",
        QuestionKind.ShortText => "Короткий текст",
        QuestionKind.Numeric => "Число",
        QuestionKind.Essay => "Эссе",
        QuestionKind.Matching => "Сопоставление",
        _ => k.ToString()
    };
}
