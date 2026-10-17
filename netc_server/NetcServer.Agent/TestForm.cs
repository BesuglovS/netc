using System.Drawing.Drawing2D;
using System.Globalization;
using System.Text.Json;
using NetcShared;

namespace NetcServer.Agent;

/// <summary>
/// Полноэкранный киоск-тест для ученика: вопросы разного типа, таймер, перемешивание,
/// автосохранение черновика и защита от закрытия/переключения приложений (см. <see cref="KioskGuard"/>).
/// </summary>
public sealed class TestForm : Form
{
    private const int CardPad = 48;

    private readonly TestDefinition _test;
    private readonly Func<TestResult, Task>? _submit;
    private readonly Action? _onClosed;

    private readonly int[] _qOrder;      // экранный порядок → исходный индекс вопроса
    private readonly int[][] _optOrder;  // [экранный вопрос] → экранный порядок вариантов → исходный
    private readonly int[][] _matchOrder;
    private readonly TestAnswer[] _answers;
    private readonly string _draftPath;

    private int _index;                  // текущий экранный вопрос; == N — экран проверки
    private DateTime _startedUtc;
    private DateTime _questionShownUtc;
    private int _prevScreen = -1;
    private DateTime _deadlineUtc;
    private DateTime _hardCapUtc;
    private bool _hasDeadline;
    private bool _finished;
    private bool _allowClose;

    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };

    // UI
    private Label _meta = null!;
    private Label _timer = null!;
    private ProgressStrip _progress = null!;
    private Panel _content = null!;
    private Button _prev = null!, _next = null!, _submitBtn = null!;

    private static readonly Color Accent = Color.FromArgb(37, 99, 235);
    private static readonly Color Ink = Color.FromArgb(28, 32, 38);
    private static readonly Color Muted = Color.FromArgb(120, 130, 145);

    public TestForm(TestDefinition test, Func<TestResult, Task>? submit, Action? onClosed)
    {
        _test = test;
        _submit = submit;
        _onClosed = onClosed;

        int n = Math.Max(1, test.Questions.Count);
        _qOrder = Enumerable.Range(0, test.Questions.Count).ToArray();
        if (test.ShuffleQuestions) Shuffle(_qOrder);
        _optOrder = new int[test.Questions.Count][];
        _matchOrder = new int[test.Questions.Count][];
        for (int s = 0; s < test.Questions.Count; s++)
        {
            var q = test.Questions[_qOrder[s]];
            var oo = Enumerable.Range(0, q.Options.Count).ToArray();
            if (test.ShuffleOptions && q.Kind != QuestionKind.Matching) Shuffle(oo);
            _optOrder[s] = oo;

            var mo = Enumerable.Range(0, q.MatchRight.Count).ToArray();
            if (test.ShuffleOptions && q.Kind == QuestionKind.Matching) Shuffle(mo);
            _matchOrder[s] = mo;
        }
        _answers = Enumerable.Range(0, test.Questions.Count)
            .Select(i => new TestAnswer { QuestionIndex = i }).ToArray();

        _draftPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "netc", "tests", (_test.Id.Length > 0 ? _test.Id : "draft") + ".draft.json");

        BuildUi();
        LoadDraft();
        _ = n; // сохраняем согласованность подписи

        _startedUtc = DateTime.UtcNow;
        _questionShownUtc = _startedUtc;
        _hardCapUtc = _startedUtc.AddHours(4);
        if (_test.DurationSeconds > 0)
        {
            _hasDeadline = true;
            _deadlineUtc = _startedUtc.AddSeconds(_test.DurationSeconds);
        }
        _tick.Tick += (_, _) => OnTick();
        _tick.Start();

        ShowQuestion(0);
        UpdateTimerLabel();
    }

    // ------------------------------------------------------------------ UI

    private void BuildUi()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = SystemInformation.VirtualScreen;
        TopMost = true;
        ShowInTaskbar = false;
        KeyPreview = true;
        BackColor = Color.FromArgb(18, 24, 38);
        Font = new Font("Segoe UI", 12F);
        // Форма собирается вручную и растягивается на весь экран — авто-масштаб только мешает.
        AutoScaleMode = AutoScaleMode.None;

        // Шапка
        var header = new Panel { Dock = DockStyle.Top, Height = S(110), BackColor = Color.FromArgb(24, 34, 58) };
        var title = new Label
        {
            Text = _test.Title,
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI Semibold", 24F, FontStyle.Bold),
            AutoEllipsis = true,
            Bounds = new Rectangle(S(CardPad), S(22), S(900), S(44))
        };
        _meta = new Label
        {
            ForeColor = Color.FromArgb(168, 182, 205),
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 13F),
            AutoSize = true,
            Location = new Point(S(CardPad + 2), S(72))
        };
        _timer = new Label
        {
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleRight,
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        header.Controls.Add(title);
        header.Controls.Add(_meta);
        header.Controls.Add(_timer);
        _timer.Bounds = new Rectangle(ClientSize.Width - S(CardPad) - S(260), S(30), S(260), S(44));
        _timer.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        _progress = new ProgressStrip { Dock = DockStyle.Top, Height = S(6) };
        _progress.BackColor = Color.FromArgb(30, 40, 64);

        // Центральная белая область
        var center = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        _content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(S(CardPad), S(28), S(CardPad), S(20))
        };
        center.Controls.Add(_content);

        // Футер
        var footer = new Panel { Dock = DockStyle.Bottom, Height = S(92), BackColor = Color.FromArgb(12, 17, 29) };
        var nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = S(520),
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, S(22), S(CardPad), 0)
        };
        _submitBtn = NavButton("Сдать тест", Accent);
        _submitBtn.Click += (_, _) => _ = FinishAsync("submit", completed: true);
        _next = NavButton("Далее", Accent);
        _next.Click += (_, _) => GoNext();
        _prev = NavButton("Назад", Color.FromArgb(96, 106, 124));
        _prev.Click += (_, _) => GoPrev();
        nav.Controls.Add(_submitBtn);
        nav.Controls.Add(_next);
        nav.Controls.Add(_prev);
        _submitBtn.Visible = false;
        footer.Controls.Add(nav);

        Controls.Add(center);
        Controls.Add(footer);
        Controls.Add(_progress);
        Controls.Add(header);

        Resize += (_, _) => { _timer.Left = ClientSize.Width - S(CardPad) - _timer.Width; };

        FormClosing += (_, e) => { if (!_allowClose) e.Cancel = true; };
        FormClosed += (_, _) => { try { _tick.Stop(); _tick.Dispose(); } catch { } _onClosed?.Invoke(); };
        KeyDown += OnKeyDown;
    }

    private Button NavButton(string text, Color back)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = false,
            Size = new Size(S(150), S(48)),
            BackColor = back,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold),
            Margin = new Padding(S(10), 0, 0, 0),
            Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    private void ShowQuestion(int screenIndex)
    {
        _index = screenIndex;
        _content.SuspendLayout();
        _content.Controls.Clear();
        _content.AutoScrollPosition = new Point(0, 0);

        int shown = Math.Min(screenIndex + 1, _test.Questions.Count);
        _meta.Text = $"Вопрос {shown} из {_test.Questions.Count}";
        _progress.Value = _test.Questions.Count == 0 ? 0 : (float)screenIndex / _test.Questions.Count;

        var q = _test.Questions[_qOrder[screenIndex]];
        int orig = _qOrder[screenIndex];

        // FlowLayoutPanel: порядок добавления = порядок сверху вниз.
        var ordered = new List<Control>();
        ordered.AddRange(AddHeading(q));
        ordered.AddRange(BuildAnswerControls(q, screenIndex, orig));
        foreach (var c in ordered) _content.Controls.Add(c);

        _prev.Enabled = screenIndex > 0;
        _next.Visible = true;
        _submitBtn.Visible = false;
        _next.Text = screenIndex >= _test.Questions.Count - 1 ? "Проверить" : "Далее";

        _content.ResumeLayout();
        _questionShownUtc = DateTime.UtcNow;
        _prevScreen = screenIndex;
    }

    private List<Control> AddHeading(TestQuestion q)
    {
        var lbl = new Label
        {
            Text = q.Text,
            Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold),
            ForeColor = Ink,
            AutoSize = false,
            Width = InnerWidth,
            Padding = new Padding(0, 0, 0, S(10))
        };
        lbl.Height = Math.Max(S(46), TextRenderer.MeasureText(
            q.Text, lbl.Font, new Size(Math.Max(S(120), InnerWidth - S(20)), int.MaxValue),
            TextFormatFlags.WordBreak).Height + S(12));
        var hint = new Label
        {
            Text = KindHint(q),
            Font = new Font("Segoe UI", 11F),
            ForeColor = Muted,
            AutoSize = false,
            Width = InnerWidth,
            Height = S(26)
        };

        var controls = new List<Control> { lbl, hint };
        var pic = BuildImageControl(q);
        if (pic is not null) controls.Add(pic);
        return controls;
    }

    /// <summary>Картинка к вопросу (если путь задан и файл есть на ученическом ПК).</summary>
    private PictureBox? BuildImageControl(TestQuestion q)
    {
        if (string.IsNullOrWhiteSpace(q.ImagePath)) return null;
        try
        {
            if (!File.Exists(q.ImagePath)) return null;
            using var img = Image.FromFile(q.ImagePath);
            if (img.Width <= 0) return null;
            int w = InnerWidth;
            int h = Math.Max(S(80), Math.Min(S(460), (int)Math.Round((double)img.Height / img.Width * w)));
            return new PictureBox
            {
                Width = w,
                Height = h,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.White,
                Image = new Bitmap(img),
                Margin = new Padding(0, 0, 0, S(10))
            };
        }
        catch { return null; }
    }

    private static string KindHint(TestQuestion q) => q.Kind switch
    {
        QuestionKind.Multi => "Выберите один или несколько вариантов · баллов: " + FmtPoints(q.Points),
        QuestionKind.TrueFalse => "Верно или неверно · баллов: " + FmtPoints(q.Points),
        QuestionKind.ShortText => "Введите краткий ответ · баллов: " + FmtPoints(q.Points),
        QuestionKind.Numeric => "Введите число · баллов: " + FmtPoints(q.Points),
        QuestionKind.Essay => "Развёрнутый ответ (проверяет учитель) · баллов: " + FmtPoints(q.Points),
        QuestionKind.Matching => "Сопоставьте пары · баллов: " + FmtPoints(q.Points),
        _ => "Выберите один вариант · баллов: " + FmtPoints(q.Points)
    };

    private static string FmtPoints(double p) => p == Math.Floor(p) ? ((int)p).ToString() : p.ToString("0.##");

    /// <summary>Масштаб интерфейса относительно 96 DPI (шрифты в пунктах масштабируются системой сами).</summary>
    private float UiScale => DeviceDpi / 96f;

    /// <summary>Логические пиксели (при 96 DPI) → пиксели устройства.</summary>
    private int S(int value) => Math.Max(1, LogicalToDeviceUnits(value));

    /// <summary>Доступная ширина внутри области вопроса (с учётом прокрутки и отступов).</summary>
    private int InnerWidth => Math.Max(S(420), ClientSize.Width - S(CardPad * 2) - S(40));

    private List<Control> BuildAnswerControls(TestQuestion q, int screenIndex, int orig)
    {
        var ans = _answers[orig];
        var list = new List<Control>();

        switch (q.Kind)
        {
            case QuestionKind.ShortText:
            {
                var tb = MakeTextBox(multiline: false, height: 54, fontSize: 18F);
                tb.Text = ans.TextAnswer;
                tb.TextChanged += (_, _) => { ans.TextAnswer = tb.Text; SaveDraft(); };
                list.Add(tb);
                break;
            }

            case QuestionKind.Numeric:
            {
                var tb = MakeTextBox(multiline: false, height: 54, fontSize: 18F);
                tb.Text = ans.NumberSet ? ans.NumberAnswer.ToString("0.######", CultureInfo.InvariantCulture) : "";
                tb.TextChanged += (_, _) =>
                {
                    string s = tb.Text.Trim().Replace(',', '.');
                    if (s.Length == 0) { ans.NumberSet = false; }
                    else if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                    {
                        ans.NumberAnswer = v; ans.NumberSet = true;
                    }
                    SaveDraft();
                };
                list.Add(tb);
                if (q.Tolerance > 0)
                {
                    list.Add(MakeNote("Допуск ответа: ±" + q.Tolerance.ToString("0.####")));
                }
                break;
            }

            case QuestionKind.Essay:
            {
                var tb = MakeTextBox(multiline: true, height: 320, fontSize: 15F);
                tb.Text = ans.TextAnswer;
                tb.TextChanged += (_, _) => { ans.TextAnswer = tb.Text; SaveDraft(); };
                list.Add(tb);
                break;
            }

            case QuestionKind.Matching:
            {
                int width = InnerWidth;
                int pairs = q.MatchLeft.Count;
                var mo = _matchOrder[screenIndex];
                for (int i = 0; i < pairs; i++)
                {
                    var row = new Panel { Width = width, Height = S(60), BackColor = Color.White };
                    var left = new Label
                    {
                        Text = $"{i + 1}. {q.MatchLeft[i]}",
                        Font = new Font("Segoe UI Semibold", 13F, FontStyle.Bold),
                        ForeColor = Ink,
                        AutoSize = false,
                        TextAlign = ContentAlignment.MiddleLeft,
                        Bounds = new Rectangle(S(6), S(6), width / 2 - S(20), S(48))
                    };
                    var combo = new ComboBox
                    {
                        DropDownStyle = ComboBoxStyle.DropDownList,
                        Font = new Font("Segoe UI", 13F),
                        Bounds = new Rectangle(width / 2, S(12), width / 2 - S(20), S(36))
                    };
                    foreach (var origRight in mo) combo.Items.Add(q.MatchRight[origRight]);
                    int current = i < ans.MatchPicks.Count ? ans.MatchPicks[i] : -1;
                    int screenPos = Array.IndexOf(mo, current);
                    combo.SelectedIndex = screenPos;
                    int leftIdx = i;
                    combo.SelectedIndexChanged += (_, _) =>
                    {
                        int pos = combo.SelectedIndex;
                        if (pos >= 0 && pos < mo.Length)
                        {
                            while (ans.MatchPicks.Count <= leftIdx) ans.MatchPicks.Add(-1);
                            ans.MatchPicks[leftIdx] = mo[pos];
                            SaveDraft();
                        }
                    };
                    row.Controls.Add(left);
                    row.Controls.Add(combo);
                    list.Add(row);
                }
                break;
            }

            default: // Single / Multi / TrueFalse
            {
                bool multi = q.Kind == QuestionKind.Multi;
                int width = InnerWidth;
                var rows = new List<OptionRow>();
                foreach (var origOpt in _optOrder[screenIndex])
                {
                    var row = new OptionRow(q.Options[origOpt], multi, UiScale);
                    row.SetWidth(width);
                    int capture = origOpt;
                    bool selected = multi
                        ? ans.SelectedIndices.Contains(origOpt)
                        : ans.SelectedIndices.Count > 0 && ans.SelectedIndices[0] == origOpt;
                    row.Selected = selected;
                    row.Toggled += () =>
                    {
                        if (multi)
                        {
                            if (ans.SelectedIndices.Contains(capture)) ans.SelectedIndices.Remove(capture);
                            else ans.SelectedIndices.Add(capture);
                            row.Selected = ans.SelectedIndices.Contains(capture);
                        }
                        else
                        {
                            ans.SelectedIndices.Clear();
                            ans.SelectedIndices.Add(capture);
                            foreach (var r in rows) { r.Selected = ReferenceEquals(r, row); r.Invalidate(); }
                        }
                        row.Invalidate();
                        SaveDraft();
                    };
                    rows.Add(row);
                    list.Add(row);
                }
                break;
            }
        }
        return list;
    }

    private TextBox MakeTextBox(bool multiline, int height, float fontSize)
    {
        var tb = new TextBox
        {
            Multiline = multiline,
            Width = InnerWidth,
            Height = S(height),
            Font = new Font("Segoe UI", fontSize),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(250, 251, 253)
        };
        if (multiline) tb.ScrollBars = ScrollBars.Vertical;
        return tb;
    }

    private Label MakeNote(string text) => new()
    {
        Text = text,
        Width = InnerWidth,
        AutoSize = false,
        Height = S(28),
        ForeColor = Muted,
        Font = new Font("Segoe UI", 10F)
    };

    // ------------------------------------------------------------------ Навигация

    private void GoNext()
    {
        SaveTimeSpent();
        if (_index >= _test.Questions.Count - 1) ShowReview();
        else ShowQuestion(_index + 1);
    }

    private void GoPrev()
    {
        SaveTimeSpent();
        if (_index == _test.Questions.Count) { ShowQuestion(_test.Questions.Count - 1); return; }
        if (_index > 0) ShowQuestion(_index - 1);
    }

    private void ShowReview()
    {
        _index = _test.Questions.Count;
        _content.SuspendLayout();
        _content.Controls.Clear();
        _meta.Text = "Проверка ответов";
        _progress.Value = 1f;

        var ordered = new List<Control>();
        ordered.Add(new Label
        {
            Text = "Проверьте ответы перед сдачей",
            Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold),
            ForeColor = Ink,
            AutoSize = false,
            Width = InnerWidth,
            Height = S(50)
        });

        for (int s = 0; s < _test.Questions.Count; s++)
        {
            var q = _test.Questions[_qOrder[s]];
            int orig = _qOrder[s];
            bool answered = IsAnswered(q, _answers[orig]);
            var row = new Panel { Width = InnerWidth, Height = S(52), BackColor = Color.White };
            int stateWidth = S(150);
            int stateX = InnerWidth - stateWidth - S(8);
            var num = new Label
            {
                Text = (s + 1).ToString(),
                Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = answered ? Accent : Color.FromArgb(196, 132, 42),
                TextAlign = ContentAlignment.MiddleCenter,
                Bounds = new Rectangle(S(6), S(8), S(34), S(34))
            };
            var txt = new Label
            {
                Text = Shorten(q.Text, 90),
                Font = new Font("Segoe UI", 12F),
                ForeColor = Ink,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Bounds = new Rectangle(S(52), S(8), stateX - S(52) - S(12), S(36))
            };
            var state = new Label
            {
                Text = answered ? "отвечен" : "нет ответа",
                ForeColor = answered ? Color.FromArgb(22, 130, 93) : Color.FromArgb(196, 132, 42),
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                Bounds = new Rectangle(stateX, S(12), stateWidth, S(28))
            };
            row.Controls.Add(num);
            row.Controls.Add(txt);
            row.Controls.Add(state);
            ordered.Add(row);
        }

        foreach (var c in ordered) _content.Controls.Add(c);

        _next.Visible = false;
        _submitBtn.Visible = true;
        _content.ResumeLayout();
    }

    private static bool IsAnswered(TestQuestion q, TestAnswer a) => q.Kind switch
    {
        QuestionKind.ShortText => !string.IsNullOrWhiteSpace(a.TextAnswer),
        QuestionKind.Essay => !string.IsNullOrWhiteSpace(a.TextAnswer),
        QuestionKind.Numeric => a.NumberSet,
        QuestionKind.Matching => a.MatchPicks.Count > 0 && a.MatchPicks.Any(p => p >= 0),
        _ => a.SelectedIndices.Count > 0
    };

    private static string Shorten(string s, int max)
    {
        s = s.Replace("\r", " ").Replace("\n", " ").Trim();
        return s.Length <= max ? s : s[..max] + "…";
    }

    // ------------------------------------------------------------------ Завершение

    private async Task FinishAsync(string reason, bool completed)
    {
        if (_finished) return;
        _finished = true;
        try { _tick.Stop(); } catch { }
        SaveTimeSpent();

        var result = TestScoring.Evaluate(
            _test, _answers.ToList(), completed, reason,
            (long)(DateTime.UtcNow - _startedUtc).TotalMilliseconds, Environment.UserName);

        if (_submit is not null)
        {
            try { await _submit(result); } catch { }
        }

        if (completed && _test.ShowResultToStudent)
        {
            await ShowResultOverlayAsync(result);
        }

        try { if (File.Exists(_draftPath)) File.Delete(_draftPath); } catch { }
        _allowClose = true;
        Close();
    }

    private async Task ShowResultOverlayAsync(TestResult res)
    {
        var tcs = new TaskCompletionSource();
        var overlay = new Panel
        {
            BackColor = Color.FromArgb(16, 22, 36),
            Bounds = ClientRectangle,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        var card = new Panel { BackColor = Color.White, Size = new Size(S(560), S(440)) };

        string title = res.Passed && res.ManualCount == 0 ? "Тест сдан" : "Тест завершён";
        var info = new List<string> { $"Баллы: {Fmt2(res.Score)} из {Fmt2(res.MaxScore)}   ({res.Percent}%)" };
        if (res.PendingManual > 0) info.Add($"На ручную проверку: {Fmt2(res.PendingManual)} балл(ов), вопросов: {res.ManualCount}");
        if (!res.Completed) info.Add("Тест завершён досрочно.");

        var lblTitle = new Label
        {
            Text = title,
            Font = new Font("Segoe UI Semibold", 22F, FontStyle.Bold),
            ForeColor = Ink,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Bounds = new Rectangle(S(20), S(20), S(520), S(50))
        };
        var lblInfo = new Label
        {
            Text = string.Join("\n", info),
            Font = new Font("Segoe UI", 13F),
            ForeColor = Color.FromArgb(70, 80, 95),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Bounds = new Rectangle(S(20), S(72), S(520), S(70))
        };

        // Разбор ответов и пояснения к вопросам (показываются ученику после сдачи).
        var details = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(248, 249, 252),
            ForeColor = Color.FromArgb(60, 70, 85),
            Font = new Font("Segoe UI", 11F),
            Bounds = new Rectangle(S(28), S(150), S(504), S(220))
        };
        details.Text = BuildDetails();

        var ok = new Button
        {
            Text = "Готово",
            Bounds = new Rectangle(0, S(380), S(560), S(60)),
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            BackColor = Accent,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter
        };
        ok.FlatAppearance.BorderSize = 0;
        ok.Click += (_, _) => tcs.TrySetResult();

        card.Controls.Add(lblTitle);
        card.Controls.Add(lblInfo);
        card.Controls.Add(details);
        card.Controls.Add(ok);
        overlay.Controls.Add(card);

        void Center()
        {
            card.Left = Math.Max(S(16), (overlay.ClientSize.Width - card.Width) / 2);
            card.Top = Math.Max(S(16), (overlay.ClientSize.Height - card.Height) / 2);
        }
        overlay.Resize += (_, _) => Center();

        Controls.Add(overlay);
        overlay.BringToFront();
        Center();

        var prevAccept = AcceptButton;
        AcceptButton = ok;
        try { await tcs.Task; } catch { }
        AcceptButton = prevAccept;
    }

    private static string Fmt2(double v) => v == Math.Floor(v) ? ((int)v).ToString() : v.ToString("0.##");

    /// <summary>Разбор ответов: для каждого вопроса — итог и пояснение (если задано).</summary>
    private string BuildDetails()
    {
        var sb = new System.Text.StringBuilder();
        for (int s = 0; s < _test.Questions.Count; s++)
        {
            var q = _test.Questions[_qOrder[s]];
            var a = _answers[_qOrder[s]];
            string mark = a.Manual ? "на проверку" : (a.Correct ? "верно" : "неверно");
            sb.Append(s + 1).Append(". ").Append(Shorten(q.Text, 70)).Append(" — ").Append(mark);
            if (!string.IsNullOrWhiteSpace(q.Explanation)) sb.Append(": ").Append(q.Explanation);
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Досрочно завершить тест (снять учителем/по аварийному выходу). Вызывать из UI-потока.</summary>
    public void ForceFinish(string reason)
    {
        if (_finished) return;
        _ = FinishAsync(reason, completed: false);
    }

    // ------------------------------------------------------------------ Таймер

    private void OnTick()
    {
        if (_finished) return;
        if (_hasDeadline)
        {
            if (DateTime.UtcNow >= _deadlineUtc) { ForceFinish("timeout"); return; }
        }
        else if (DateTime.UtcNow >= _hardCapUtc)
        {
            // Жёсткий предел, чтобы ПК не остался заблокированным навсегда.
            ForceFinish("timeout");
            return;
        }
        UpdateTimerLabel();
    }

    private void UpdateTimerLabel()
    {
        if (_hasDeadline)
        {
            var left = _deadlineUtc - DateTime.UtcNow;
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;
            _timer.ForeColor = left.TotalMinutes < 1 ? Color.FromArgb(255, 138, 128) : Color.White;
            _timer.Text = "⏱ " + left.ToString(@"mm\:ss");
        }
        else
        {
            var t = DateTime.UtcNow - _startedUtc;
            _timer.ForeColor = Color.FromArgb(200, 210, 228);
            _timer.Text = "⏱ " + t.ToString(@"mm\:ss");
        }
    }

    private void SaveTimeSpent()
    {
        if (_prevScreen >= 0 && _prevScreen < _test.Questions.Count)
        {
            int orig = _qOrder[_prevScreen];
            _answers[orig].TimeSpentMs += (long)(DateTime.UtcNow - _questionShownUtc).TotalMilliseconds;
        }
        _questionShownUtc = DateTime.UtcNow;
    }

    // ------------------------------------------------------------------ Черновик

    private void SaveDraft()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_draftPath)!);
            var payload = new TestDraft { TestId = _test.Id, Answers = _answers.ToList() };
            File.WriteAllText(_draftPath, JsonSerializer.Serialize(payload, Json.Options));
        }
        catch { }
    }

    private void LoadDraft()
    {
        try
        {
            if (!File.Exists(_draftPath)) return;
            var payload = JsonSerializer.Deserialize<TestDraft>(File.ReadAllText(_draftPath), Json.Options);
            if (payload?.Answers is null) return;
            foreach (var a in payload.Answers)
            {
                if (a.QuestionIndex >= 0 && a.QuestionIndex < _answers.Length) _answers[a.QuestionIndex] = a;
            }
        }
        catch { }
    }

    private sealed class TestDraft
    {
        public string TestId { get; set; } = "";
        public List<TestAnswer> Answers { get; set; } = new();
    }

    // ------------------------------------------------------------------ Аварийный выход

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.Shift && e.KeyCode == Keys.F10)
        {
            e.Handled = true;
            if (!string.IsNullOrEmpty(_test.EscapePin) && AskPin()) ForceFinish("escape");
        }
    }

    private bool AskPin()
    {
        using var dlg = new Form
        {
            Text = "Аварийный выход",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            ClientSize = new Size(340, 130),
            MaximizeBox = false,
            MinimizeBox = false,
            TopMost = true,
            ShowInTaskbar = false
        };
        var lbl = new Label { Text = "Введите PIN учителя:", Location = new Point(12, 15), AutoSize = true };
        var txt = new TextBox { Location = new Point(12, 42), Width = 316, PasswordChar = '●' };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(170, 84), Width = 75 };
        var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Location = new Point(253, 84), Width = 75 };
        dlg.Controls.AddRange(new Control[] { lbl, txt, ok, cancel });
        dlg.AcceptButton = ok;
        dlg.CancelButton = cancel;
        return dlg.ShowDialog(this) == DialogResult.OK && txt.Text == _test.EscapePin;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Не даём форме закрыться по Esc/Alt+F4 изнутри (киоск).
        if (keyData == Keys.Escape) return true;
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ------------------------------------------------------------------ Утилиты

    private static void Shuffle(int[] a)
    {
        var rnd = Random.Shared;
        for (int i = a.Length - 1; i > 0; i--)
        {
            int j = rnd.Next(i + 1);
            (a[i], a[j]) = (a[j], a[i]);
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var brush = new LinearGradientBrush(ClientRectangle,
            Color.FromArgb(24, 34, 58), Color.FromArgb(11, 16, 28), 90f);
        e.Graphics.FillRectangle(brush, ClientRectangle);
    }

    // ------------------------------------------------------------------ Вложенные контролы

    /// <summary>Тонкая полоса прогресса.</summary>
    private sealed class ProgressStrip : Control
    {
        private float _value;

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public float Value { get => _value; set { _value = Math.Clamp(value, 0f, 1f); Invalidate(); } }

        public ProgressStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.FromArgb(30, 40, 64));
            int w = (int)(Width * _value);
            if (w > 0)
            {
                using var brush = new SolidBrush(Accent);
                e.Graphics.FillRectangle(brush, 0, 0, w, Height);
            }
        }
    }

    /// <summary>Крупная кликабельная строка-вариант с галочкой/радио и подсветкой.</summary>
    private sealed class OptionRow : Control
    {
        public bool Selected;
        public bool Multi;
        private bool _hover;
        private readonly float _scale;
        public event Action? Toggled;

        public OptionRow(string text, bool multi, float scale)
        {
            Text = text;
            Multi = multi;
            _scale = scale <= 0 ? 1f : scale;
            Height = S(54);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        private int S(int v) => Math.Max(1, (int)Math.Round(v * _scale));

        public void SetWidth(int width)
        {
            Width = width;
            int textWidth = Math.Max(S(120), width - S(80));
            var size = TextRenderer.MeasureText(Text, Font, new Size(textWidth, int.MaxValue), TextFormatFlags.WordBreak);
            Height = Math.Max(S(54), size.Height + S(26));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            int radius = S(10);
            Color fill = Selected ? Color.FromArgb(232, 240, 254) : (_hover ? Color.FromArgb(246, 249, 253) : Color.White);
            Color border = Selected ? Accent : Color.FromArgb(214, 221, 230);
            using (var path = Rounded(rect, radius))
            using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            using (var path = Rounded(rect, radius))
            using (var p = new Pen(border, Selected ? S(2) : 1f)) g.DrawPath(p, path);

            int gs = S(20), gx = S(16), gy = Height / 2 - gs / 2;
            if (Multi)
            {
                var box = new Rectangle(gx, gy, gs, gs);
                using (var p = new Pen(Selected ? Accent : Color.FromArgb(150, 158, 170), S(2)))
                using (var path = Rounded(box, S(5))) g.DrawPath(p, path);
                if (Selected)
                {
                    g.SmoothingMode = SmoothingMode.None;
                    using var b = new SolidBrush(Accent);
                    g.FillRectangle(b, gx + S(4), gy + S(4), gs - S(8), gs - S(8));
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                }
            }
            else
            {
                using var p = new Pen(Selected ? Accent : Color.FromArgb(150, 158, 170), S(2));
                g.DrawEllipse(p, gx, gy, gs, gs);
                if (Selected)
                {
                    using var b = new SolidBrush(Accent);
                    g.FillEllipse(b, gx + S(6), gy + S(6), gs - S(12), gs - S(12));
                }
            }

            var textRect = new Rectangle(gx + gs + S(16), 0, Width - (gx + gs + S(16)) - S(16), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, Ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnClick(EventArgs e) { Toggled?.Invoke(); base.OnClick(e); }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
