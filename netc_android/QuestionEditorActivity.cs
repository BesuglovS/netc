using Android.App;
using Android.Graphics;
using Android.OS;
using Android.Text;
using Android.Views;
using Android.Widget;
using NetcShared;

namespace NetcAndroid;

/// <summary>
/// Редактор одного вопроса теста: поля зависят от типа вопроса.
/// Вход/выход — через статические <see cref="Input"/> и <see cref="Output"/>.
/// </summary>
[Activity(Label = "Вопрос", Theme = "@android:style/Theme.Material.Light.DarkActionBar")]
public sealed class QuestionEditorActivity : Activity
{
    /// <summary>Вопрос для редактирования (null — новый).</summary>
    public static TestQuestion? Input;

    /// <summary>Результат редактирования (при <see cref="Result.Ok"/>).</summary>
    public static TestQuestion? Output;

    private Spinner _kind = null!;
    private EditText _text = null!;
    private LinearLayout _dyn = null!;

    private EditText _options = null!;
    private EditText _correct = null!;
    private EditText _correctMulti = null!;
    private Spinner _trueFalse = null!;
    private EditText _shortAnswers = null!;
    private CheckBox _ignoreCase = null!;
    private EditText _number = null!;
    private EditText _tolerance = null!;
    private EditText _left = null!;
    private EditText _right = null!;

    private EditText _points = null!;
    private CheckBox _partial = null!;
    private EditText _explanation = null!;
    private EditText _image = null!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        BuildUi();
        LoadQuestion(Input);
    }

    private int Dp(int v) => (int)(v * Resources!.DisplayMetrics!.Density);

    private void BuildUi()
    {
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };

        var scroll = new ScrollView(this) { FillViewport = true };
        var form = new LinearLayout(this) { Orientation = Orientation.Vertical };
        form.SetPadding(Dp(14), Dp(8), Dp(14), Dp(8));
        scroll.AddView(form);
        root.AddView(scroll, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0, 1f));

        // Тип вопроса
        form.AddView(Label("Тип вопроса"));
        _kind = new Spinner(this);
        var kindAdapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem,
            Enum.GetValues<QuestionKind>().Select(KindLabel).ToArray());
        kindAdapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
        _kind.Adapter = kindAdapter;
        _kind.ItemSelected += (_, _) => RebuildDyn();
        form.AddView(_kind, Match());

        // Текст вопроса
        form.AddView(Label("Текст вопроса"));
        _text = MultiEdit(3);
        form.AddView(_text, Match());

        // Динамическая часть
        _dyn = new LinearLayout(this) { Orientation = Orientation.Vertical };
        form.AddView(_dyn, Match());

        // Общие поля
        form.AddView(Label("Баллы за вопрос"));
        _points = Edit("1");
        _points.InputType = InputTypes.ClassNumber | InputTypes.NumberFlagDecimal;
        form.AddView(_points, new LinearLayout.LayoutParams(Dp(140), ViewGroup.LayoutParams.WrapContent));

        _partial = new CheckBox(this) { Text = "Начислять частичный балл за неполный ответ" };
        form.AddView(_partial);

        form.AddView(Label("Пояснение к ответу (необязательно)"));
        _explanation = MultiEdit(2);
        form.AddView(_explanation, Match());

        form.AddView(Label("Картинка — путь на ученическом ПК (необязательно)"));
        _image = Edit("");
        form.AddView(_image, Match());

        // Кнопки
        var buttons = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        buttons.SetPadding(Dp(14), Dp(6), Dp(14), Dp(10));
        var ok = new Button(this) { Text = "Сохранить" };
        ok.Click += (_, _) => { if (Build()) { Output = _built; SetResult(Result.Ok); Finish(); } };
        var cancel = new Button(this) { Text = "Отмена" };
        cancel.Click += (_, _) => { SetResult(Result.Canceled); Finish(); };
        buttons.AddView(ok, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { RightMargin = Dp(8) });
        buttons.AddView(cancel);
        root.AddView(buttons, Match());

        SetContentView(root);
        root.SetOnApplyWindowInsetsListener(new InsetsListener());
    }

    private TestQuestion? _built;

    private LinearLayout.LayoutParams Match() =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);

    private TextView Label(string text)
    {
        var t = new TextView(this) { Text = text };
        t.SetTextSize(Android.Util.ComplexUnitType.Sp, 14);
        t.SetPadding(0, Dp(8), 0, Dp(2));
        return t;
    }

    private EditText Edit(string value)
    {
        var e = new EditText(this) { Text = value };
        e.SetTextSize(Android.Util.ComplexUnitType.Sp, 15);
        return e;
    }

    private EditText MultiEdit(int lines)
    {
        var e = new EditText(this);
        e.SetTextSize(Android.Util.ComplexUnitType.Sp, 15);
        e.SetLines(lines);
        e.Gravity = GravityFlags.Top;
        e.InputType = InputTypes.ClassText | InputTypes.TextFlagMultiLine;
        return e;
    }

    private void RebuildDyn()
    {
        _dyn.RemoveAllViews();
        var kind = (QuestionKind)Math.Max(0, _kind.SelectedItemPosition);

        void Add(string label, View v)
        {
            _dyn.AddView(Label(label));
            _dyn.AddView(v, Match());
        }

        switch (kind)
        {
            case QuestionKind.TrueFalse:
                _trueFalse = new Spinner(this);
                var tfAdapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem,
                    new[] { "Верно", "Неверно" });
                tfAdapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
                _trueFalse.Adapter = tfAdapter;
                Add("Верный ответ", _trueFalse);
                break;

            case QuestionKind.Multi:
                _options = MultiEdit(4);
                _correctMulti = Edit("");
                Add("Варианты (по одному в строке)", _options);
                Add("Номера верных через запятую, напр. 1,3", _correctMulti);
                break;

            case QuestionKind.ShortText:
                _shortAnswers = MultiEdit(3);
                _ignoreCase = new CheckBox(this) { Text = "Не учитывать регистр и лишние пробелы", Checked = true };
                Add("Допустимые ответы (по одному в строке)", _shortAnswers);
                _dyn.AddView(_ignoreCase);
                break;

            case QuestionKind.Numeric:
                _number = Edit("");
                _number.InputType = InputTypes.ClassNumber | InputTypes.NumberFlagDecimal | InputTypes.NumberFlagSigned;
                _tolerance = Edit("0");
                _tolerance.InputType = InputTypes.ClassNumber | InputTypes.NumberFlagDecimal;
                Add("Верное число", _number);
                Add("Допуск ± (0 — точное совпадение)", _tolerance);
                break;

            case QuestionKind.Essay:
                var note = new TextView(this) { Text = "Автопроверка не выполняется — ответ оценивает учитель вручную." };
                note.SetTextSize(Android.Util.ComplexUnitType.Sp, 14);
                note.SetTextColor(Color.ParseColor("#5A6473"));
                note.SetPadding(0, Dp(6), 0, Dp(6));
                _dyn.AddView(note);
                break;

            case QuestionKind.Matching:
                _left = MultiEdit(4);
                _right = MultiEdit(4);
                Add("Левый столбец (по одному в строке)", _left);
                Add("Правый столбец (пара — та же строка)", _right);
                break;

            default: // Single
                _options = MultiEdit(4);
                _correct = Edit("1");
                _correct.InputType = InputTypes.ClassNumber;
                Add("Варианты ответа (по одному в строке)", _options);
                Add("Номер верного (1…N)", _correct);
                break;
        }

        Prefill();
    }

    private TestQuestion? _pending;

    private void LoadQuestion(TestQuestion? q)
    {
        _pending = q;
        var current = q ?? new TestQuestion();
        _kind.SetSelection((int)current.Kind);
        _text.Text = current.Text;
        _points.Text = Fmt(current.Points);
        _partial.Checked = current.PartialCredit;
        _explanation.Text = current.Explanation;
        _image.Text = current.ImagePath ?? "";
        RebuildDyn();
    }

    /// <summary>Заполняет динамические поля значениями редактируемого вопроса.</summary>
    private void Prefill()
    {
        var q = _pending;
        if (q is null) return;
        switch (q.Kind)
        {
            case QuestionKind.TrueFalse when _trueFalse is not null:
                _trueFalse.SetSelection(q.CorrectIndex == 1 ? 1 : 0);
                break;
            case QuestionKind.Multi when _options is not null:
                _options.Text = string.Join("\n", q.Options);
                _correctMulti.Text = string.Join(", ", q.CorrectIndices.Select(i => i + 1));
                break;
            case QuestionKind.ShortText when _shortAnswers is not null:
                _shortAnswers.Text = string.Join("\n", q.CorrectText);
                _ignoreCase.Checked = q.IgnoreCase;
                break;
            case QuestionKind.Numeric when _number is not null:
                _number.Text = Fmt(q.CorrectNumber);
                _tolerance.Text = Fmt(q.Tolerance);
                break;
            case QuestionKind.Matching when _left is not null:
                _left.Text = string.Join("\n", q.MatchLeft);
                _right.Text = string.Join("\n", q.MatchRight);
                break;
            case QuestionKind.Single when _options is not null:
                _options.Text = string.Join("\n", q.Options);
                _correct.Text = Math.Max(1, q.CorrectIndex + 1).ToString();
                break;
        }
        _pending = null;
    }

    private bool Build()
    {
        var kind = (QuestionKind)Math.Max(0, _kind.SelectedItemPosition);
        var q = new TestQuestion
        {
            Kind = kind,
            Text = _text.Text?.Trim() ?? "",
            Points = ParseDouble(_points.Text, 1),
            PartialCredit = _partial.Checked,
            Explanation = _explanation.Text?.Trim() ?? "",
            ImagePath = string.IsNullOrWhiteSpace(_image.Text) ? null : _image.Text!.Trim()
        };
        if (q.Text.Length == 0) { Warn("Введите текст вопроса."); return false; }
        if (q.Points <= 0) q.Points = 1;

        switch (kind)
        {
            case QuestionKind.TrueFalse:
                q.Options = new List<string> { "Верно", "Неверно" };
                q.CorrectIndex = _trueFalse.SelectedItemPosition == 1 ? 1 : 0;
                break;

            case QuestionKind.Multi:
            {
                var opts = Lines(_options.Text);
                if (opts.Count < 2) { Warn("Нужно минимум 2 варианта."); return false; }
                var correct = new List<int>();
                foreach (var part in (_correctMulti.Text ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                    if (int.TryParse(part.Trim(), out int n) && n >= 1 && n <= opts.Count) correct.Add(n - 1);
                if (correct.Count == 0) { Warn("Укажите хотя бы один верный номер."); return false; }
                q.Options = opts;
                q.CorrectIndices = correct.Distinct().OrderBy(i => i).ToList();
                break;
            }

            case QuestionKind.ShortText:
            {
                var answers = Lines(_shortAnswers.Text);
                if (answers.Count == 0) { Warn("Укажите хотя бы один допустимый ответ."); return false; }
                q.CorrectText = answers;
                q.IgnoreCase = _ignoreCase.Checked;
                break;
            }

            case QuestionKind.Numeric:
                q.CorrectNumber = ParseDouble(_number.Text, 0);
                q.Tolerance = Math.Abs(ParseDouble(_tolerance.Text, 0));
                break;

            case QuestionKind.Essay:
                break;

            case QuestionKind.Matching:
            {
                var left = Lines(_left.Text);
                var right = Lines(_right.Text);
                if (left.Count < 2 || left.Count != right.Count)
                {
                    Warn("Для сопоставления нужно ≥2 строк, и число строк слева и справа должно совпадать.");
                    return false;
                }
                q.MatchLeft = left;
                q.MatchRight = right;
                break;
            }

            default: // Single
            {
                var opts = Lines(_options.Text);
                if (opts.Count < 2) { Warn("Нужно минимум 2 варианта."); return false; }
                int correct = (int)ParseDouble(_correct.Text, 1) - 1;
                if (correct < 0 || correct >= opts.Count) { Warn("Номер верного вне диапазона вариантов."); return false; }
                q.Options = opts;
                q.CorrectIndex = correct;
                break;
            }
        }

        _built = q;
        return true;
    }

    private void Warn(string message) =>
        new AlertDialog.Builder(this)
            .SetTitle("Проверьте вопрос")
            ?.SetMessage(message)
            ?.SetPositiveButton("OK", (_, _) => { })
            ?.Show();

    private static List<string> Lines(string? text) =>
        (text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

    private static double ParseDouble(string? s, double fallback)
    {
        s = (s ?? "").Trim().Replace(',', '.');
        return double.TryParse(s, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : fallback;
    }

    private static string Fmt(double v) => v == Math.Floor(v)
        ? ((int)v).ToString()
        : v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    private static string KindLabel(QuestionKind k) => k switch
    {
        QuestionKind.Single => "Один вариант",
        QuestionKind.Multi => "Несколько вариантов",
        QuestionKind.TrueFalse => "Верно / Неверно",
        QuestionKind.ShortText => "Короткий текстовый ответ",
        QuestionKind.Numeric => "Числовой ответ",
        QuestionKind.Essay => "Развёрнутый ответ (эссе)",
        QuestionKind.Matching => "Сопоставление пар",
        _ => k.ToString()
    };
}
