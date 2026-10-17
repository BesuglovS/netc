using System.Globalization;
using NetcShared;

namespace NetcClient;

/// <summary>Диалог редактирования одного вопроса теста: поля меняются в зависимости от типа.</summary>
public sealed class QuestionEditorForm : Form
{
    private readonly ComboBox _kind = new();
    private readonly TextBox _text = new();
    private readonly FlowLayoutPanel _dyn = new();

    private readonly TextBox _options = new();
    private readonly NumericUpDown _correct = new();
    private readonly TextBox _correctMulti = new();
    private readonly ComboBox _trueFalse = new();
    private readonly TextBox _shortAnswers = new();
    private readonly CheckBox _ignoreCase = new();
    private readonly TextBox _number = new();
    private readonly TextBox _tolerance = new();
    private readonly TextBox _left = new();
    private readonly TextBox _right = new();

    private readonly NumericUpDown _points = new();
    private readonly CheckBox _partial = new();
    private readonly TextBox _explanation = new();
    private readonly TextBox _image = new();

    public TestQuestion Result { get; private set; } = new();

    public QuestionEditorForm(TestQuestion? existing = null)
    {
        Text = existing is null ? "Новый вопрос" : "Редактирование вопроса";
        ClientSize = new Size(620, 640);
        MinimumSize = new Size(560, 520);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 10F);

        foreach (var k in Enum.GetValues<QuestionKind>()) _kind.Items.Add(KindLabel(k));
        _kind.DropDownStyle = ComboBoxStyle.DropDownList;
        _kind.SelectedIndexChanged += (_, _) => RebuildDyn();

        _text.Multiline = true;
        _text.Height = 74;
        _text.ScrollBars = ScrollBars.Vertical;

        _options.Multiline = true;
        _options.Height = 130;
        _options.ScrollBars = ScrollBars.Vertical;

        _correct.Minimum = 1;
        _correct.Maximum = 50;
        _correct.Width = 90;

        _correctMulti.Text = "1";

        _trueFalse.DropDownStyle = ComboBoxStyle.DropDownList;
        _trueFalse.Items.AddRange(new object[] { "Верно", "Неверно" });
        _trueFalse.SelectedIndex = 0;
        _trueFalse.Width = 160;

        _shortAnswers.Multiline = true;
        _shortAnswers.Height = 90;
        _shortAnswers.ScrollBars = ScrollBars.Vertical;
        _ignoreCase.Text = "Не учитывать регистр и лишние пробелы";
        _ignoreCase.Checked = true;
        _ignoreCase.AutoSize = true;

        _number.Width = 160;
        _tolerance.Width = 120;
        _tolerance.Text = "0";

        _left.Multiline = true;
        _left.Height = 110;
        _left.ScrollBars = ScrollBars.Vertical;
        _right.Multiline = true;
        _right.Height = 110;
        _right.ScrollBars = ScrollBars.Vertical;

        _points.DecimalPlaces = 2;
        _points.Minimum = 0.5m;
        _points.Maximum = 1000m;
        _points.Value = 1m;
        _points.Width = 90;

        _partial.Text = "Начислять частичный балл за неполный ответ";
        _partial.AutoSize = true;

        _explanation.Multiline = true;
        _explanation.Height = 60;
        _explanation.ScrollBars = ScrollBars.Vertical;

        _image.Width = 560;

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(12)
        };
        _dyn.FlowDirection = FlowDirection.TopDown;
        _dyn.WrapContents = false;
        _dyn.AutoSize = true;
        _dyn.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _dyn.Margin = new Padding(0);

        AddField(flow, "Тип вопроса", _kind, 240);
        AddField(flow, "Текст вопроса", _text, 560);
        flow.Controls.Add(_dyn);
        AddField(flow, "Баллы за вопрос", _points, 90);
        flow.Controls.Add(_partial);
        AddField(flow, "Пояснение к ответу (показывается после сдачи, необязательно)", _explanation, 560);
        AddField(flow, "Картинка к вопросу — путь на ученическом ПК (необязательно)", _image, 560);

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(12, 6, 12, 12)
        };
        var ok = new Button { Text = "OK", Size = new Size(110, 34) };
        var cancel = new Button { Text = "Отмена", Size = new Size(110, 34), DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) =>
        {
            if (Build()) { DialogResult = DialogResult.OK; Close(); }
        };
        bottom.Controls.Add(ok);
        bottom.Controls.Add(cancel);

        Controls.Add(flow);
        Controls.Add(bottom);
        AcceptButton = ok;
        CancelButton = cancel;

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        LoadQuestion(existing);
    }

    private static void AddField(FlowLayoutPanel flow, string label, Control input, int width)
    {
        flow.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 6, 0, 2) });
        input.Width = width;
        flow.Controls.Add(input);
    }

    private void RebuildDyn()
    {
        _dyn.Controls.Clear();
        var kind = (QuestionKind)Math.Max(0, _kind.SelectedIndex);
        switch (kind)
        {
            case QuestionKind.TrueFalse:
                AddField(_dyn, "Верный ответ", _trueFalse, 160);
                break;
            case QuestionKind.Multi:
                AddField(_dyn, "Варианты (по одному в строке)", _options, 560);
                AddField(_dyn, "Номера верных через запятую, напр. 1,3", _correctMulti, 200);
                break;
            case QuestionKind.ShortText:
                AddField(_dyn, "Допустимые ответы (по одному в строке)", _shortAnswers, 560);
                _dyn.Controls.Add(_ignoreCase);
                break;
            case QuestionKind.Numeric:
                AddField(_dyn, "Верное число", _number, 160);
                AddField(_dyn, "Допуск ± (0 — точное совпадение)", _tolerance, 120);
                break;
            case QuestionKind.Essay:
                _dyn.Controls.Add(new Label
                {
                    Text = "Автопроверка не выполняется — ответ оценивает учитель вручную.",
                    AutoSize = true,
                    ForeColor = Color.FromArgb(90, 100, 115),
                    Margin = new Padding(0, 6, 0, 6)
                });
                break;
            case QuestionKind.Matching:
                AddField(_dyn, "Левый столбец (по одному в строке)", _left, 560);
                AddField(_dyn, "Правый столбец (по одной строке; пара — та же строка)", _right, 560);
                break;
            default:
                AddField(_dyn, "Варианты ответа (по одному в строке)", _options, 560);
                AddField(_dyn, "Номер верного (1…N)", _correct, 90);
                break;
        }
        _dyn.Visible = _dyn.Controls.Count > 0;
    }

    private void LoadQuestion(TestQuestion? q)
    {
        q ??= new TestQuestion();
        _kind.SelectedIndex = (int)q.Kind;
        _text.Text = q.Text;
        _options.Lines = q.Options.ToArray();
        _correct.Value = Math.Clamp(q.CorrectIndex + 1, 1, 50);
        _correctMulti.Text = string.Join(", ", q.CorrectIndices.Select(i => i + 1));
        _trueFalse.SelectedIndex = q.CorrectIndex == 1 ? 1 : 0;
        _shortAnswers.Lines = q.CorrectText.ToArray();
        _ignoreCase.Checked = q.IgnoreCase;
        _number.Text = q.CorrectNumber.ToString("0.######", CultureInfo.InvariantCulture);
        _tolerance.Text = q.Tolerance.ToString("0.######", CultureInfo.InvariantCulture);
        _left.Lines = q.MatchLeft.ToArray();
        _right.Lines = q.MatchRight.ToArray();
        _points.Value = (decimal)Math.Clamp(q.Points, 0.5, 1000);
        _partial.Checked = q.PartialCredit;
        _explanation.Text = q.Explanation;
        _image.Text = q.ImagePath ?? "";

        RebuildDyn();
    }

    private static string[] Lines(TextBox box) =>
        box.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();

    private static double ParseDouble(string s)
    {
        s = (s ?? "").Trim().Replace(',', '.');
        return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double v) ? v : 0;
    }

    private bool Build()
    {
        var kind = (QuestionKind)Math.Max(0, _kind.SelectedIndex);
        var q = new TestQuestion
        {
            Kind = kind,
            Text = _text.Text.Trim(),
            Points = (double)_points.Value,
            PartialCredit = _partial.Checked,
            Explanation = _explanation.Text.Trim(),
            ImagePath = string.IsNullOrWhiteSpace(_image.Text) ? null : _image.Text.Trim()
        };

        if (q.Text.Length == 0) { Warn("Введите текст вопроса."); return false; }

        switch (kind)
        {
            case QuestionKind.TrueFalse:
                q.Options = new List<string> { "Верно", "Неверно" };
                q.CorrectIndex = _trueFalse.SelectedIndex == 1 ? 1 : 0;
                break;

            case QuestionKind.Multi:
            {
                var opts = Lines(_options);
                if (opts.Length < 2) { Warn("Нужно минимум 2 варианта."); return false; }
                var correct = new List<int>();
                foreach (var part in _correctMulti.Text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (int.TryParse(part.Trim(), out int n) && n >= 1 && n <= opts.Length) correct.Add(n - 1);
                }
                if (correct.Count == 0) { Warn("Укажите хотя бы один верный номер."); return false; }
                q.Options = opts.ToList();
                q.CorrectIndices = correct.Distinct().OrderBy(i => i).ToList();
                break;
            }

            case QuestionKind.ShortText:
            {
                var answers = Lines(_shortAnswers);
                if (answers.Length == 0) { Warn("Укажите хотя бы один допустимый ответ."); return false; }
                q.CorrectText = answers.ToList();
                q.IgnoreCase = _ignoreCase.Checked;
                break;
            }

            case QuestionKind.Numeric:
                q.CorrectNumber = ParseDouble(_number.Text);
                q.Tolerance = Math.Abs(ParseDouble(_tolerance.Text));
                break;

            case QuestionKind.Essay:
                break;

            case QuestionKind.Matching:
            {
                var left = Lines(_left);
                var right = Lines(_right);
                if (left.Length < 2 || left.Length != right.Length)
                {
                    Warn("Для сопоставления нужно ≥2 строк, и число строк слева и справа должно совпадать.");
                    return false;
                }
                q.MatchLeft = left.ToList();
                q.MatchRight = right.ToList();
                break;
            }

            default: // Single
            {
                var opts = Lines(_options);
                if (opts.Length < 2) { Warn("Нужно минимум 2 варианта."); return false; }
                int correct = (int)_correct.Value - 1;
                if (correct < 0 || correct >= opts.Length) { Warn("Номер верного вне диапазона вариантов."); return false; }
                q.Options = opts.ToList();
                q.CorrectIndex = correct;
                break;
            }
        }

        Result = q;
        return true;
    }

    private void Warn(string message) =>
        MessageBox.Show(this, message, "Проверьте вопрос", MessageBoxButtons.OK, MessageBoxIcon.Warning);

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
