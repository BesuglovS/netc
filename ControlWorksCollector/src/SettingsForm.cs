using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using ControlWorksCollector.Services;

namespace ControlWorksCollector
{
    /// <summary>Настройки: учётные данные, папка, инвентарь ПК (таблица) и опции сбора.</summary>
    public sealed class SettingsForm : Form
    {
        private TextBox _txtDest;
        private TextBox _txtUser;
        private TextBox _txtPass;
        private CheckBox _chkSave;
        private NumericUpDown _numTimeout;
        private NumericUpDown _numParallel;
        private CheckBox _chkPublic;

        private DataGridView _gridPcs;
        private Button _btnAdd;
        private Button _btnRemove;
        private Button _btnPaste;
        private Button _btnDiscover;

        // Индексы колонок таблицы ПК.
        private const int ColLabel = 0;
        private const int ColHost = 1;
        private const int ColMac = 2;
        private const int ColIp = 3;
        private const int ColNote = 4;

        public AppConfig Config { get; private set; }

        public SettingsForm(AppConfig cfg)
        {
            Config = cfg;

            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7F, 15F);
            Text = "Настройки";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(760, 680);
            MinimumSize = new Size(640, 520);

            BuildUi();
            LoadValues();
        }

        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(10);
            root.ColumnCount = 1;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowCount = 4;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            root.Controls.Add(BuildConnectionGroup(), 0, 0);
            root.Controls.Add(BuildWhatGroup(), 0, 1);
            root.Controls.Add(BuildPcGroup(), 0, 2);
            root.Controls.Add(BuildButtonsPanel(), 0, 3);

            Controls.Add(root);
        }

        private GroupBox BuildConnectionGroup()
        {
            GroupBox g = new GroupBox();
            g.Text = "Подключение";
            g.Dock = DockStyle.Fill;
            g.AutoSize = true;
            g.AutoSizeMode = AutoSizeMode.GrowAndShrink;

            TableLayoutPanel t = NewGrid(3);
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _txtDest = new TextBox();
            _txtDest.Dock = DockStyle.Fill;
            Button btnBrowse = MakeSmallButton("Обзор...");
            btnBrowse.Click += OnBrowse;
            AddRow3(t, 0, MakeLabel("Папка для собранных работ:"), _txtDest, btnBrowse);

            _txtUser = new TextBox();
            _txtUser.Dock = DockStyle.Fill;
            AddRow(t, 1, MakeLabel("Пользователь-администратор:"), _txtUser);

            _txtPass = new TextBox();
            _txtPass.Dock = DockStyle.Fill;
            _txtPass.UseSystemPasswordChar = true;
            AddRow(t, 2, MakeLabel("Пароль:"), _txtPass);

            _chkSave = new CheckBox();
            _chkSave.Text = "Сохранять пароль (шифруется DPAPI)";
            _chkSave.AutoSize = true;
            AddRow(t, 3, null, _chkSave);

            _numTimeout = new NumericUpDown();
            _numTimeout.Minimum = 3;
            _numTimeout.Maximum = 300;
            _numTimeout.Width = 80;
            AddRow(t, 4, MakeLabel("Таймаут, сек:"), _numTimeout);

            _numParallel = new NumericUpDown();
            _numParallel.Minimum = 1;
            _numParallel.Maximum = 16;
            _numParallel.Width = 80;
            AddRow(t, 5, MakeLabel("ПК одновременно:"), _numParallel);

            g.Controls.Add(t);
            return g;
        }

        private GroupBox BuildWhatGroup()
        {
            GroupBox g = new GroupBox();
            g.Text = "Что собирать (только папки, созданные в выбранную дату)";
            g.Dock = DockStyle.Fill;
            g.AutoSize = true;
            g.AutoSizeMode = AutoSizeMode.GrowAndShrink;

            FlowLayoutPanel f = new FlowLayoutPanel();
            f.Dock = DockStyle.Fill;
            f.FlowDirection = FlowDirection.TopDown;
            f.WrapContents = false;
            f.AutoSize = true;
            f.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            f.Padding = new Padding(8);

            _chkPublic = new CheckBox();
            _chkPublic.Text = @"Общий рабочий стол (C:\Users\Public\Desktop)";
            _chkPublic.AutoSize = true;

            f.Controls.Add(_chkPublic);

            g.Controls.Add(f);
            return g;
        }

        private GroupBox BuildPcGroup()
        {
            GroupBox g = new GroupBox();
            g.Text = "Список компьютеров";
            g.Dock = DockStyle.Fill;

            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.Padding = new Padding(8);
            t.ColumnCount = 1;
            t.RowCount = 2;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Fill;
            bar.AutoSize = true;
            bar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            bar.WrapContents = true;

            _btnAdd = MakeSmallButton("Добавить");
            _btnAdd.Click += OnAddRow;
            _btnRemove = MakeSmallButton("Удалить выбранные");
            _btnRemove.Click += OnRemoveRows;
            _btnPaste = MakeSmallButton("Вставить список IP…");
            _btnPaste.Click += OnPasteIps;
            _btnDiscover = MakeSmallButton("Определить имя и MAC");
            _btnDiscover.Click += OnDiscover;

            bar.Controls.Add(_btnAdd);
            bar.Controls.Add(_btnRemove);
            bar.Controls.Add(_btnPaste);
            bar.Controls.Add(_btnDiscover);

            _gridPcs = new DataGridView();
            _gridPcs.Dock = DockStyle.Fill;
            _gridPcs.AllowUserToAddRows = true;
            _gridPcs.AllowUserToDeleteRows = true;
            _gridPcs.RowHeadersVisible = false;
            _gridPcs.MultiSelect = true;
            _gridPcs.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _gridPcs.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _gridPcs.BackgroundColor = Color.White;
            _gridPcs.Columns.Add("colLabel", "Название");
            _gridPcs.Columns.Add("colHost", "Имя ПК");
            _gridPcs.Columns.Add("colMac", "MAC");
            _gridPcs.Columns.Add("colIp", "IP");
            _gridPcs.Columns.Add("colNote", "Примечание");
            _gridPcs.Columns[ColLabel].FillWeight = 22;
            _gridPcs.Columns[ColHost].FillWeight = 18;
            _gridPcs.Columns[ColMac].FillWeight = 16;
            _gridPcs.Columns[ColIp].FillWeight = 16;
            _gridPcs.Columns[ColNote].FillWeight = 28;

            t.Controls.Add(bar, 0, 0);
            t.Controls.Add(_gridPcs, 0, 1);

            g.Controls.Add(t);
            return g;
        }

        private FlowLayoutPanel BuildButtonsPanel()
        {
            FlowLayoutPanel p = new FlowLayoutPanel();
            p.Dock = DockStyle.Fill;
            p.AutoSize = true;
            p.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            p.FlowDirection = FlowDirection.RightToLeft;
            p.WrapContents = false;
            p.Padding = new Padding(0, 6, 0, 0);

            Button btnOk = new Button();
            btnOk.Text = "Сохранить";
            btnOk.AutoSize = true;
            btnOk.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            btnOk.MinimumSize = new Size(100, 30);
            btnOk.Padding = new Padding(8, 0, 8, 0);
            btnOk.Margin = new Padding(6, 3, 3, 3);
            btnOk.Click += OnOk;

            Button btnCancel = new Button();
            btnCancel.Text = "Отмена";
            btnCancel.AutoSize = true;
            btnCancel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            btnCancel.MinimumSize = new Size(90, 30);
            btnCancel.Padding = new Padding(8, 0, 8, 0);
            btnCancel.Margin = new Padding(3, 3, 6, 3);
            btnCancel.DialogResult = DialogResult.Cancel;

            p.Controls.Add(btnOk);
            p.Controls.Add(btnCancel);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
            return p;
        }

        // ---------- helpers ----------

        private static TableLayoutPanel NewGrid(int cols)
        {
            TableLayoutPanel t = new TableLayoutPanel();
            t.Dock = DockStyle.Fill;
            t.AutoSize = true;
            t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            t.ColumnCount = cols;
            t.Padding = new Padding(8);
            return t;
        }

        private static Label MakeLabel(string text)
        {
            Label l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Anchor = AnchorStyles.Left;
            l.Margin = new Padding(3, 7, 6, 3);
            return l;
        }

        private static Button MakeSmallButton(string text)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Margin = new Padding(3);
            return b;
        }

        private static void AddRow(TableLayoutPanel t, int row, Control label, Control field)
        {
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowCount = row + 1;
            if (label != null)
            {
                t.Controls.Add(label, 0, row);
            }
            if (field != null)
            {
                t.Controls.Add(field, 1, row);
                if (t.ColumnCount > 2)
                {
                    t.SetColumnSpan(field, t.ColumnCount - 1);
                }
            }
        }

        private static void AddRow3(TableLayoutPanel t, int row, Control label, Control field, Control button)
        {
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowCount = row + 1;
            if (label != null)
            {
                t.Controls.Add(label, 0, row);
            }
            if (field != null)
            {
                t.Controls.Add(field, 1, row);
            }
            if (button != null)
            {
                button.Anchor = AnchorStyles.Left;
                t.Controls.Add(button, 2, row);
            }
        }

        private static string CellText(DataGridViewRow row, int col)
        {
            object v = row.Cells[col].Value;
            return v == null ? "" : v.ToString().Trim();
        }

        private PcRecord RowToRecord(DataGridViewRow row)
        {
            return new PcRecord
            {
                Label = CellText(row, ColLabel),
                Hostname = CellText(row, ColHost),
                Mac = CellText(row, ColMac),
                Ip = CellText(row, ColIp),
                Note = CellText(row, ColNote)
            };
        }

        private void LoadValues()
        {
            _txtDest.Text = Config.DestinationRoot;
            _txtUser.Text = Config.UserName;
            _txtPass.Text = Config.Password;
            _chkSave.Checked = !string.IsNullOrEmpty(Config.EncryptedPassword);
            _numTimeout.Value = Clamp(Config.ConnectTimeoutSeconds, _numTimeout);
            _numParallel.Value = Clamp(Config.MaxParallel, _numParallel);
            _chkPublic.Checked = Config.IncludePublicDesktop;

            if (Config.PcRecords != null)
            {
                foreach (PcRecord r in Config.PcRecords)
                {
                    _gridPcs.Rows.Add(r.Label, r.Hostname, r.Mac, r.Ip, r.Note);
                }
            }
        }

        private static decimal Clamp(int value, NumericUpDown control)
        {
            if (value < control.Minimum) return control.Minimum;
            if (value > control.Maximum) return control.Maximum;
            return value;
        }

        private void OnBrowse(object sender, EventArgs e)
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Выберите папку для сохранения собранных работ";
                dlg.SelectedPath = _txtDest.Text;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _txtDest.Text = dlg.SelectedPath;
                }
            }
        }

        private void OnAddRow(object sender, EventArgs e)
        {
            int i = _gridPcs.Rows.Add();
            _gridPcs.CurrentCell = _gridPcs.Rows[i].Cells[ColIp];
            _gridPcs.BeginEdit(true);
        }

        private void OnRemoveRows(object sender, EventArgs e)
        {
            List<DataGridViewRow> rows = new List<DataGridViewRow>();
            foreach (DataGridViewRow r in _gridPcs.SelectedRows)
            {
                if (!r.IsNewRow)
                {
                    rows.Add(r);
                }
            }
            foreach (DataGridViewRow r in rows)
            {
                _gridPcs.Rows.Remove(r);
            }
        }

        private void OnPasteIps(object sender, EventArgs e)
        {
            using (Form dlg = new Form())
            {
                dlg.Text = "Вставить список (по одному адресу в строке)";
                dlg.FormBorderStyle = FormBorderStyle.Sizable;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.ClientSize = new Size(440, 340);

                TextBox tb = new TextBox();
                tb.Multiline = true;
                tb.Dock = DockStyle.Fill;
                tb.ScrollBars = ScrollBars.Vertical;
                tb.Font = new Font("Consolas", 9.5F);

                FlowLayoutPanel buttons = new FlowLayoutPanel();
                buttons.Dock = DockStyle.Bottom;
                buttons.AutoSize = true;
                buttons.FlowDirection = FlowDirection.RightToLeft;

                Button ok = new Button();
                ok.Text = "Добавить";
                ok.DialogResult = DialogResult.OK;
                ok.AutoSize = true;
                ok.MinimumSize = new Size(100, 30);

                Button cancel = new Button();
                cancel.Text = "Отмена";
                cancel.DialogResult = DialogResult.Cancel;
                cancel.AutoSize = true;
                cancel.MinimumSize = new Size(90, 30);

                buttons.Controls.Add(ok);
                buttons.Controls.Add(cancel);

                dlg.Controls.Add(tb);
                dlg.Controls.Add(buttons);
                dlg.AcceptButton = ok;
                dlg.CancelButton = cancel;

                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    foreach (string line in tb.Lines)
                    {
                        string v = (line ?? "").Trim();
                        if (v.Length == 0 || v.StartsWith("#") || v.StartsWith("//"))
                        {
                            continue;
                        }
                        _gridPcs.Rows.Add("", "", "", v, "");
                    }
                }
            }
        }

        private async void OnDiscover(object sender, EventArgs e)
        {
            string user = _txtUser.Text.Trim();
            string pass = _txtPass.Text;
            if (string.IsNullOrWhiteSpace(user))
            {
                MessageBox.Show(this, "Сначала укажите пользователя-администратора.", "Нет данных",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrEmpty(pass))
            {
                using (PasswordPrompt p = new PasswordPrompt(user, "Пароль нужен для определения сведений о ПК."))
                {
                    if (p.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }
                    pass = p.Password;
                }
            }

            int timeout = (int)_numTimeout.Value;
            SetBusy(true);
            int ok = 0, fail = 0;
            try
            {
                foreach (DataGridViewRow row in _gridPcs.Rows)
                {
                    if (row.IsNewRow)
                    {
                        continue;
                    }
                    PcRecord rec = RowToRecord(row);
                    if (string.IsNullOrWhiteSpace(rec.Hostname) && string.IsNullOrWhiteSpace(rec.Ip))
                    {
                        continue;
                    }

                    DiscoverResult res = await System.Threading.Tasks.Task.Run(
                        () => PcDiscovery.Discover(rec, user, pass, timeout));

                    if (res.Ok)
                    {
                        if (!string.IsNullOrEmpty(res.Hostname)) row.Cells[ColHost].Value = res.Hostname;
                        if (!string.IsNullOrEmpty(res.Ip)) row.Cells[ColIp].Value = res.Ip;
                        if (!string.IsNullOrEmpty(res.Mac)) row.Cells[ColMac].Value = res.Mac;
                        ok++;
                    }
                    else
                    {
                        fail++;
                        if (string.IsNullOrWhiteSpace(CellText(row, ColNote)))
                        {
                            row.Cells[ColNote].Value = res.Error;
                        }
                    }
                }
            }
            finally
            {
                SetBusy(false);
            }

            MessageBox.Show(this, "Определение завершено.\r\nУспешно: " + ok + ", не удалось: " + fail + ".",
                "Сведения о ПК", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void SetBusy(bool busy)
        {
            _btnDiscover.Enabled = !busy;
            _btnAdd.Enabled = !busy;
            _btnRemove.Enabled = !busy;
            _btnPaste.Enabled = !busy;
            UseWaitCursor = busy;
        }

        private void OnOk(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_txtDest.Text))
            {
                MessageBox.Show(this, "Укажите папку для сохранения работ.", "Проверьте данные",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(_txtUser.Text))
            {
                MessageBox.Show(this, "Укажите пользователя-администратора.", "Проверьте данные",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Config.DestinationRoot = _txtDest.Text.Trim();
            Config.UserName = _txtUser.Text.Trim();
            Config.Password = _txtPass.Text;
            Config.ConnectTimeoutSeconds = (int)_numTimeout.Value;
            Config.MaxParallel = (int)_numParallel.Value;
            Config.IncludePublicDesktop = _chkPublic.Checked;

            List<PcRecord> records = new List<PcRecord>();
            foreach (DataGridViewRow row in _gridPcs.Rows)
            {
                if (row.IsNewRow)
                {
                    continue;
                }
                PcRecord r = RowToRecord(row);
                if (string.IsNullOrWhiteSpace(r.Hostname) && string.IsNullOrWhiteSpace(r.Ip))
                {
                    continue;
                }
                records.Add(r);
            }
            Config.PcRecords = records;

            try
            {
                ConfigStore.Save(Config, _chkSave.Checked);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось сохранить настройки:\n" + ex.Message, "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
