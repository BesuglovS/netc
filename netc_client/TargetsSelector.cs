using System.Drawing;
using NetcShared;

namespace NetcClient;

/// <summary>
/// Полоса выбора целевых ПК для окон действий: кнопка выбора (диалог со списком),
/// быстрые «Все»/«Никто» и сводка. <see cref="Selected"/> — текущий выбор.
/// </summary>
public sealed class TargetsSelector : Panel
{
    private readonly string[] _all;
    private readonly string[] _names;
    private readonly List<string> _selected = new();
    private readonly object _gate = new();
    private readonly Label _summary;

    public TargetsSelector(TargetSet set)
    {
        _all = set.All.ToArray();
        _names = set.Names.ToArray();
        foreach (var a in set.Initial)
        {
            if (_all.Contains(a, StringComparer.OrdinalIgnoreCase)) _selected.Add(a);
        }
        if (_selected.Count == 0) _selected.AddRange(_all);

        Dock = DockStyle.Top;
        Height = 40;
        Padding = new Padding(8, 6, 8, 6);
        BackColor = Color.FromArgb(245, 245, 245);

        var choose = new Button { Text = "Выбрать компьютеры…", Size = new Size(220, 28) };
        choose.Click += (_, _) => ChooseDialog();
        var all = new Button { Text = "Все", Size = new Size(70, 28), Margin = new Padding(6, 3, 3, 3) };
        all.Click += (_, _) => { lock (_gate) { _selected.Clear(); _selected.AddRange(_all); } UpdateSummary(); };
        var none = new Button { Text = "Никто", Size = new Size(80, 28), Margin = new Padding(3, 3, 3, 3) };
        none.Click += (_, _) => { lock (_gate) _selected.Clear(); UpdateSummary(); };
        _summary = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Padding = new Padding(12, 0, 0, 0) };

        var flow = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false };
        flow.Controls.Add(choose);
        flow.Controls.Add(all);
        flow.Controls.Add(none);
        Controls.Add(_summary);
        Controls.Add(flow);
        UpdateSummary();
    }

    /// <summary>Текущий набор выбранных адресов (копия, безопасно читать из фонового потока).</summary>
    public IReadOnlyList<string> Selected
    {
        get { lock (_gate) return _selected.ToList(); }
    }

    private void UpdateSummary()
    {
        int count;
        lock (_gate) count = _selected.Count;
        _summary.Text = count == _all.Length
            ? $"выбраны все ({_all.Length})"
            : $"выбрано {count} из {_all.Length}";
    }

    private void ChooseDialog()
    {
        using var dlg = new TargetsDialog(_all, _names, _selected);
        if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
        {
            lock (_gate) { _selected.Clear(); _selected.AddRange(dlg.Selected); }
            UpdateSummary();
        }
    }
}

/// <summary>Модальный диалог выбора компьютеров (список с флажками).</summary>
public sealed class TargetsDialog : Form
{
    private readonly string[] _all;
    private readonly CheckedListBox _list;

    public TargetsDialog(IReadOnlyList<string> all, IReadOnlyList<string> names, IReadOnlyList<string> selected)
    {
        _all = all.ToArray();

        Text = "Выбор компьютеров";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(380, 540);
        MinimumSize = new Size(300, 320);
        ShowInTaskbar = false;

        _list = new CheckedListBox
        {
            Dock = DockStyle.Fill,
            CheckOnClick = true,
            IntegralHeight = false
        };
        for (int i = 0; i < _all.Length; i++)
        {
            string name = i < names.Count ? names[i] : "";
            string label = string.IsNullOrWhiteSpace(name) ? _all[i] : name + "  (" + _all[i] + ")";
            _list.Items.Add(label, selected.Contains(_all[i], StringComparer.OrdinalIgnoreCase));
        }

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(6) };
        var bAll = new Button { Text = "Все", Size = new Size(90, 30) };
        bAll.Click += (_, _) => SetAll(true);
        var bNone = new Button { Text = "Никто", Size = new Size(100, 30) };
        bNone.Click += (_, _) => SetAll(false);
        top.Controls.Add(bAll);
        top.Controls.Add(bNone);

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(6) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Size = new Size(100, 30) };
        var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Size = new Size(100, 30) };
        bottom.Controls.Add(ok);
        bottom.Controls.Add(cancel);

        Controls.Add(_list);
        Controls.Add(top);
        Controls.Add(bottom);
        AcceptButton = ok;
        CancelButton = cancel;

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
    }

    /// <summary>Отмеченные адреса.</summary>
    public IReadOnlyList<string> Selected
    {
        get
        {
            var res = new List<string>();
            for (int i = 0; i < _list.Items.Count; i++)
            {
                if (_list.GetItemChecked(i)) res.Add(_all[i]);
            }
            return res;
        }
    }

    private void SetAll(bool value)
    {
        for (int i = 0; i < _list.Items.Count; i++) _list.SetItemChecked(i, value);
    }
}

/// <summary>Вспомогательное: добавляет полосу выбора ПК на форму действий.</summary>
public static class TargetsHelper
{
    public static TargetsSelector AddTargetsBar(Form form, TargetSet set)
    {
        var bar = new TargetsSelector(set);
        int shift = bar.Height;

        bool hasDockedFill = false;
        foreach (Control c in form.Controls)
        {
            if (c.Dock == DockStyle.Fill) { hasDockedFill = true; break; }
        }

        if (!hasDockedFill)
        {
            // Абсолютные/якорные контролы не сдвигаются автоматически — сдвигаем вручную,
            // добавив ту же высоту к форме, чтобы нижние якоря (журналы) сохранили размер.
            foreach (Control c in form.Controls) c.Top += shift;
        }

        form.Controls.Add(bar);
        form.ClientSize = new Size(form.ClientSize.Width, form.ClientSize.Height + shift);
        return bar;
    }
}
