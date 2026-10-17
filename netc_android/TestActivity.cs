using System.Text;
using System.Text.Json;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using NetcShared;

namespace NetcAndroid;

/// <summary>
/// Тесты: составление (7 типов вопросов), импорт/экспорт JSON, выдача ученикам,
/// сбор результатов и экспорт CSV.
/// </summary>
[Activity(Label = "Тест", Theme = "@android:style/Theme.Material.Light.DarkActionBar")]
public sealed class TestActivity : ActionActivity
{
    private const int RequestPickJson = 3001;
    private const int RequestSaveJson = 3002;
    private const int RequestSaveCsv = 3003;
    private const int RequestEditQuestion = 3004;

    private readonly List<TestQuestion> _questions = new();
    private readonly List<string> _resultLines = new();
    private readonly List<string[]> _resultRows = new();
    private readonly Dictionary<string, ScreenClient> _active = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    private string _testId = Guid.NewGuid().ToString("N");
    private CancellationTokenSource? _cts;

    private EditText _title = null!;
    private EditText _timer = null!;
    private EditText _passPercent = null!;
    private EditText _pin = null!;
    private CheckBox _shuffleQ = null!;
    private CheckBox _shuffleO = null!;
    private CheckBox _showResult = null!;

    private ListView _questionList = null!;
    private ListView _resultList = null!;
    private ArrayAdapter<string> _questionAdapter = null!;
    private ArrayAdapter<string> _resultAdapter = null!;
    private Button _send = null!;
    private Button _stop = null!;
    private LinearLayout _split = null!;

    private bool _wide;

    protected override void BuildContent(LinearLayout content)
    {
        _wide = Resources!.Configuration!.ScreenWidthDp >= 900;

        // ---- Параметры теста ----
        _title = new EditText(this) { Text = "Контрольная работа" };
        _title.SetTextSize(Android.Util.ComplexUnitType.Sp, 15);
        content.AddView(Row(new[] { "Название:", "" }, new View[] { _title }));

        _timer = AddInline(content, "Таймер (мин, 0 — без лимита):", "0", 6);
        _passPercent = AddInline(content, "Проходной %:", "70", 4);
        _pin = AddInline(content, "PIN аварийного выхода:", "", 8);

        var checks = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        _shuffleQ = new CheckBox(this) { Text = "Перемешивать вопросы" };
        _shuffleO = new CheckBox(this) { Text = "Перемешивать варианты" };
        _showResult = new CheckBox(this) { Text = "Показывать результат", Checked = true };
        checks.AddView(_shuffleQ);
        checks.AddView(_shuffleO);
        checks.AddView(_showResult);
        content.AddView(checks);

        // ---- Вопросы / результаты ----
        _split = new LinearLayout(this) { Orientation = _wide ? Orientation.Horizontal : Orientation.Vertical };

        _split.AddView(BuildQuestionsPane(), new LinearLayout.LayoutParams(
            _wide ? 0 : ViewGroup.LayoutParams.MatchParent,
            _wide ? ViewGroup.LayoutParams.MatchParent : ViewGroup.LayoutParams.WrapContent,
            _wide ? 3f : 0f));

        if (_wide)
        {
            var div = new View(this);
            div.SetBackgroundColor(Color.ParseColor("#D0D0D0"));
            _split.AddView(div, new LinearLayout.LayoutParams(Dp(1), ViewGroup.LayoutParams.MatchParent));
        }

        _split.AddView(BuildResultsPane(), new LinearLayout.LayoutParams(
            _wide ? 0 : ViewGroup.LayoutParams.MatchParent,
            _wide ? ViewGroup.LayoutParams.MatchParent : ViewGroup.LayoutParams.WrapContent,
            _wide ? 2f : 0f));

        content.AddView(_split, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, _wide ? Dp(260) : ViewGroup.LayoutParams.WrapContent));

        RefreshQuestions();
    }

    private LinearLayout BuildQuestionsPane()
    {
        var pane = new LinearLayout(this) { Orientation = Orientation.Vertical };
        pane.AddView(SectionLabel("Вопросы:"));

        _questionList = new ListView(this);
        _questionList.ChoiceMode = ChoiceMode.Single;
        _questionAdapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleListItem1);
        _questionList.Adapter = _questionAdapter;
        pane.AddView(_questionList, _wide
            ? new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f)
            : new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(140)));

        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        RowButton(row, "Добавить", AddQuestion);
        RowButton(row, "Изменить", EditSelected);
        RowButton(row, "Удалить", DeleteSelected);
        RowButton(row, "Вверх", () => MoveQuestion(-1));
        RowButton(row, "Вниз", () => MoveQuestion(1));
        pane.AddView(row);

        var row2 = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        RowButton(row2, "Импорт JSON", ImportJson);
        RowButton(row2, "Экспорт JSON", ExportJson);
        pane.AddView(row2);
        return pane;
    }

    private LinearLayout BuildResultsPane()
    {
        var pane = new LinearLayout(this) { Orientation = Orientation.Vertical };
        pane.AddView(SectionLabel("Результаты:"));

        _resultList = new ListView(this);
        _resultAdapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleListItem1);
        _resultList.Adapter = _resultAdapter;
        pane.AddView(_resultList, _wide
            ? new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f)
            : new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(120)));

        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        _send = RowButton(row, "Отправить тест", () => _ = SendAsync());
        _stop = RowButton(row, "Завершить тест", () => _ = StopAsync());
        _stop.Enabled = false;
        RowButton(row, "Экспорт CSV", ExportCsv);
        pane.AddView(row);
        return pane;
    }

    // ---- Вспомогательные элементы ----

    private TextView SectionLabel(string text)
    {
        var t = new TextView(this) { Text = text };
        t.SetTextSize(Android.Util.ComplexUnitType.Sp, 15);
        t.SetPadding(0, Dp(8), 0, Dp(2));
        return t;
    }

    private LinearLayout Row(string[] labels, View[] views)
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        for (int i = 0; i < labels.Length; i++)
        {
            if (labels[i].Length > 0)
            {
                var l = new TextView(this) { Text = labels[i] };
                l.SetPadding(0, Dp(6), Dp(8), 0);
                row.AddView(l);
            }
            if (i < views.Length) row.AddView(views[i]);
        }
        return row;
    }

    private EditText AddInline(LinearLayout parent, string label, string value, int widthDp)
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var l = new TextView(this) { Text = label };
        l.SetPadding(0, Dp(6), Dp(8), 0);
        row.AddView(l);
        var e = new EditText(this) { Text = value };
        e.SetTextSize(Android.Util.ComplexUnitType.Sp, 15);
        row.AddView(e, new LinearLayout.LayoutParams(Dp(widthDp * 10), ViewGroup.LayoutParams.WrapContent));
        parent.AddView(row);
        return e;
    }

    private Button RowButton(LinearLayout parent, string text, Action onClick)
    {
        var b = new Button(this) { Text = text };
        b.SetTextSize(Android.Util.ComplexUnitType.Sp, 14);
        b.LayoutParameters = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, (int)(46 * Resources!.DisplayMetrics!.Density))
        { RightMargin = Dp(6), BottomMargin = Dp(4) };
        b.Click += (_, _) => { try { onClick(); } catch (Exception ex) { Log("Ошибка: " + ex.Message); } };
        parent.AddView(b);
        return b;
    }

    // ---- Редактор вопросов ----

    private void RefreshQuestions()
    {
        _questionAdapter.Clear();
        foreach (var q in _questions)
            _questionAdapter.Add($"{KindLabel(q.Kind)}: {Shorten(q.Text, 80)}  [{Fmt(q.Points)} б.]");
        _questionAdapter.NotifyDataSetChanged();
    }

    private int CurrentIndex() => _questionList.SelectedItemPosition;

    private void AddQuestion()
    {
        QuestionEditorActivity.Input = null;
        StartActivityForResult(new Intent(this, typeof(QuestionEditorActivity)), RequestEditQuestion);
    }

    private void EditSelected()
    {
        int i = CurrentIndex();
        if (i < 0 || i >= _questions.Count) { Log("Выберите вопрос."); return; }
        QuestionEditorActivity.Input = _questions[i];
        StartActivityForResult(new Intent(this, typeof(QuestionEditorActivity)), RequestEditQuestion);
    }

    private void DeleteSelected()
    {
        int i = CurrentIndex();
        if (i < 0 || i >= _questions.Count) { Log("Выберите вопрос."); return; }
        _questions.RemoveAt(i);
        RefreshQuestions();
    }

    private void MoveQuestion(int delta)
    {
        int i = CurrentIndex();
        int j = i + delta;
        if (i < 0 || j < 0 || j >= _questions.Count) return;
        (_questions[i], _questions[j]) = (_questions[j], _questions[i]);
        RefreshQuestions();
        _questionList.SetSelection(j);
    }

    // ---- JSON ----

    private void ImportJson()
    {
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("*/*");
        StartActivityForResult(intent, RequestPickJson);
    }

    private void ExportJson()
    {
        if (_questions.Count == 0) { Log("Нет вопросов для экспорта."); return; }
        var intent = new Intent(Intent.ActionCreateDocument);
        intent.SetType("application/json");
        intent.PutExtra(Intent.ExtraTitle, "test.json");
        StartActivityForResult(intent, RequestSaveJson);
    }

    private void ExportCsv()
    {
        if (_resultRows.Count == 0) { Log("Нет результатов для экспорта."); return; }
        var intent = new Intent(Intent.ActionCreateDocument);
        intent.SetType("text/csv");
        intent.PutExtra(Intent.ExtraTitle, "test-results.csv");
        StartActivityForResult(intent, RequestSaveCsv);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (resultCode != Result.Ok && requestCode != RequestEditQuestion) return;

        if (requestCode == RequestEditQuestion)
        {
            if (QuestionEditorActivity.Output is null) return;
            int i = CurrentIndex();
            if (i >= 0 && i < _questions.Count && QuestionEditorActivity.Input is not null)
                _questions[i] = QuestionEditorActivity.Output;
            else
                _questions.Add(QuestionEditorActivity.Output);
            RefreshQuestions();
            return;
        }

        if (data?.Data is null) return;
        var uri = data.Data;

        try
        {
            if (requestCode == RequestPickJson)
            {
                using var input = ContentResolver!.OpenInputStream(uri);
                if (input is null) { Log("Нет доступа к файлу."); return; }
                using var reader = new StreamReader(input, Encoding.UTF8);
                string text = reader.ReadToEnd();
                var def = JsonSerializer.Deserialize<TestDefinition>(text, Json.Options);
                if (def is null || def.Questions.Count == 0) { Log("Файл не содержит вопросов."); return; }
                _questions.Clear();
                _questions.AddRange(def.Questions);
                ApplyDefinition(def);
                RefreshQuestions();
                Log($"Импортировано {def.Questions.Count} вопр.");
            }
            else if (requestCode == RequestSaveJson)
            {
                using var output = ContentResolver!.OpenOutputStream(uri);
                if (output is null) { Log("Нет доступа к файлу."); return; }
                using var writer = new StreamWriter(output, new UTF8Encoding(false));
                writer.Write(JsonSerializer.Serialize(BuildDefinition(), Json.Options));
                Log("Тест экспортирован в JSON.");
            }
            else if (requestCode == RequestSaveCsv)
            {
                using var output = ContentResolver!.OpenOutputStream(uri);
                if (output is null) { Log("Нет доступа к файлу."); return; }
                using var writer = new StreamWriter(output, new UTF8Encoding(true));
                writer.WriteLine("ПК;Ученик;Баллы;Процент;Время;Итог");
                foreach (var row in _resultRows)
                    writer.WriteLine(string.Join(";", row.Select(Csv)));
                Log("Результаты экспортированы в CSV.");
            }
        }
        catch (Exception ex)
        {
            Log("Ошибка: " + ex.Message);
        }
    }

    private void ApplyDefinition(TestDefinition def)
    {
        _testId = string.IsNullOrWhiteSpace(def.Id) ? Guid.NewGuid().ToString("N") : def.Id;
        _title.Text = def.Title;
        _timer.Text = (def.DurationSeconds / 60).ToString();
        _shuffleQ.Checked = def.ShuffleQuestions;
        _shuffleO.Checked = def.ShuffleOptions;
        _showResult.Checked = def.ShowResultToStudent;
        _passPercent.Text = Fmt(def.PassPercent);
        _pin.Text = def.EscapePin;
    }

    private TestDefinition BuildDefinition() => new()
    {
        Id = _testId,
        Title = string.IsNullOrWhiteSpace(_title.Text) ? "Тест" : _title.Text!.Trim(),
        Questions = _questions.ToList(),
        DurationSeconds = (int)ParseDouble(_timer.Text, 0) * 60,
        ShuffleQuestions = _shuffleQ.Checked,
        ShuffleOptions = _shuffleO.Checked,
        ShowResultToStudent = _showResult.Checked,
        PassPercent = ParseDouble(_passPercent.Text, 0),
        EscapePin = _pin.Text?.Trim() ?? ""
    };

    // ---- Отправка ----

    private async Task SendAsync()
    {
        if (_questions.Count == 0) { Log("Добавьте хотя бы один вопрос."); return; }
        var targets = Targets.SelectedAddresses;
        if (targets.Count == 0) { Log("Не выбрано ни одного ПК."); return; }

        _testId = Guid.NewGuid().ToString("N");
        var def = BuildDefinition();
        _send.Enabled = false;
        _stop.Enabled = true;
        _resultLines.Clear();
        _resultRows.Clear();
        _resultAdapter.NotifyDataSetChanged();
        _cts = new CancellationTokenSource();

        Log($"Отправка теста «{def.Title}» ({def.Questions.Count} вопр.) на {targets.Count} ПК" +
            (def.DurationSeconds > 0 ? $", таймер {def.DurationSeconds / 60} мин" : ", без лимита времени") + "…");

        var tasks = targets.Select(addr => RunOneAsync(addr, def, _cts.Token)).ToList();
        await Task.WhenAll(tasks);

        Log("Готово.");
        _send.Enabled = true;
        _stop.Enabled = false;
    }

    private async Task RunOneAsync(string address, TestDefinition def, CancellationToken ct)
    {
        var client = new ScreenClient(address, Settings.ControlPort, Key);
        lock (_gate) _active[address] = client;
        try
        {
            var res = await client.SendTestAsync(def, 0, ct);
            if (res is null) Log($"{address}: результат не получен");
            else Report(address, res);
        }
        catch (System.OperationCanceledException) { Log($"{address}: отменено"); }
        catch (Exception ex) { Log($"{address}: ошибка — {ex.Message}"); }
        finally
        {
            lock (_gate) _active.Remove(address);
            client.Dispose();
        }
    }

    private async Task StopAsync()
    {
        ScreenClient[] clients;
        lock (_gate) clients = _active.Values.ToArray();
        if (clients.Length == 0) { Log("Нет активных тестов."); return; }

        _stop.Enabled = false;
        Log($"Досрочное завершение теста на {clients.Length} ПК…");
        foreach (var c in clients)
        {
            try { await c.StopTestAsync("teacher"); }
            catch (Exception ex) { Log($"{c.Host}: ошибка завершения — {ex.Message}"); }
        }
        Log("Команда завершения отправлена; результаты придут по мере ответа ПК.");
    }

    private void Report(string address, TestResult res)
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

        string score = res.ManualCount > 0
            ? $"{Fmt(res.Score)}/{Fmt(res.MaxScore)} (+{Fmt(res.PendingManual)} ручн.)"
            : $"{Fmt(res.Score)}/{Fmt(res.MaxScore)}";
        string time = TimeSpan.FromMilliseconds(res.DurationMs).ToString(@"mm\:ss");
        string line = $"{address} · {res.Student} · {score} · {res.Percent}% · {time} · {reason}";

        _resultRows.Add(new[] { address, res.Student, score, res.Percent + "%", time, reason });
        RunOnUiThread(() =>
        {
            _resultLines.Add(line);
            _resultAdapter.Clear();
            foreach (var l in _resultLines) _resultAdapter.Add(l);
            _resultAdapter.NotifyDataSetChanged();
        });
        Log($"{address}: {res.Student} — {score} ({res.Percent}%), {reason}" +
            (res.Completed && res.Passed ? " ✔" : ""));
    }

    protected override void OnDestroy()
    {
        try { _cts?.Cancel(); } catch { }
        ScreenClient[] clients;
        lock (_gate) clients = _active.Values.ToArray();
        foreach (var c in clients) { try { _ = c.StopTestAsync("teacher"); } catch { } }
        base.OnDestroy();
    }

    // ---- Утилиты ----

    private static string Csv(string? v)
    {
        v ??= "";
        return v.Contains(';') || v.Contains('"') ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }

    private static string Fmt(double v) => v == Math.Floor(v)
        ? ((int)v).ToString()
        : v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private static double ParseDouble(string? s, double fallback)
    {
        s = (s ?? "").Trim().Replace(',', '.');
        return double.TryParse(s, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : fallback;
    }

    private static string Shorten(string s, int max)
    {
        s = s.Replace("\r", " ").Replace("\n", " ").Trim();
        return s.Length <= max ? s : s[..max] + "…";
    }

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
