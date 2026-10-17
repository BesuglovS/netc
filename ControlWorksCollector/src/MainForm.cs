using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ControlWorksCollector.Services;

namespace ControlWorksCollector
{
    /// <summary>Главное окно: список ПК, кнопка сбора, прогресс и журнал.</summary>
    public sealed class MainForm : Form
    {
        private AppConfig _config;

        private DataGridView _grid;
        private DateTimePicker _date;
        private Button _btnCollect;
        private Button _btnStop;
        private Button _btnSettings;
        private Button _btnOpen;
        private ProgressBar _progress;
        private TextBox _log;
        private ToolStripStatusLabel _statusLabel;
        private SplitContainer _split;

        private readonly List<PcTarget> _targets = new List<PcTarget>();
        private readonly Dictionary<PcTarget, DataGridViewRow> _rows = new Dictionary<PcTarget, DataGridViewRow>();
        private CancellationTokenSource _cts;
        private bool _busy;

        public MainForm()
        {
            _config = ConfigStore.Load();

            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7F, 15F);
            Text = "Сбор контрольных работ";
            ClientSize = new Size(1000, 640);
            MinimumSize = new Size(820, 520);
            StartPosition = FormStartPosition.CenterScreen;

            BuildUi();
            ReloadGrid();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            try
            {
                int desired = 380;
                int max = _split.Height - _split.Panel2MinSize - _split.SplitterWidth;
                if (desired > max)
                {
                    desired = max;
                }
                if (desired < _split.Panel1MinSize)
                {
                    desired = _split.Panel1MinSize;
                }
                if (desired > 0)
                {
                    _split.SplitterDistance = desired;
                }
            }
            catch
            {
                // оставляем значение по умолчанию
            }
        }

        private void BuildUi()
        {
            FlowLayoutPanel top = new FlowLayoutPanel();
            top.Dock = DockStyle.Top;
            top.AutoSize = true;
            top.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            top.WrapContents = false;
            top.Padding = new Padding(8, 8, 8, 4);

            Label lblDate = new Label();
            lblDate.Text = "Дата сбора:";
            lblDate.AutoSize = true;
            lblDate.Margin = new Padding(3, 9, 6, 3);
            top.Controls.Add(lblDate);

            _date = new DateTimePicker();
            _date.Format = DateTimePickerFormat.Custom;
            _date.CustomFormat = "dd.MM.yyyy";
            _date.Width = 110;
            _date.Value = DateTime.Today;
            _date.Margin = new Padding(3, 6, 12, 3);
            top.Controls.Add(_date);

            _btnCollect = MakeButton("Собрать сейчас");
            _btnCollect.Click += OnCollect;
            top.Controls.Add(_btnCollect);

            _btnStop = MakeButton("Остановить");
            _btnStop.Enabled = false;
            _btnStop.Click += OnStop;
            top.Controls.Add(_btnStop);

            _btnSettings = MakeButton("Настройки");
            _btnSettings.Click += OnSettings;
            top.Controls.Add(_btnSettings);

            _btnOpen = MakeButton("Открыть папку сборов");
            _btnOpen.Click += OnOpenFolder;
            top.Controls.Add(_btnOpen);

            _grid = new DataGridView();
            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.ReadOnly = true;
            _grid.RowHeadersVisible = false;
            _grid.MultiSelect = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.BackgroundColor = Color.White;
            _grid.AllowUserToResizeRows = false;

            AddColumn("ПК", 16);
            AddColumn("Адрес", 12);
            AddColumn("Статус", 10);
            AddColumn("Найдено", 7);
            AddColumn("Скопир.", 7);
            AddColumn("Файлов", 7);
            AddColumn("Время", 8);
            AddColumn("Сообщение", 33);

            _log = new TextBox();
            _log.Dock = DockStyle.Fill;
            _log.Multiline = true;
            _log.ReadOnly = true;
            _log.ScrollBars = ScrollBars.Vertical;
            _log.Font = new Font("Consolas", 9F);
            _log.BackColor = Color.FromArgb(30, 30, 30);
            _log.ForeColor = Color.Gainsboro;

            _split = new SplitContainer();
            _split.Dock = DockStyle.Fill;
            _split.Orientation = Orientation.Horizontal;
            _split.Panel1MinSize = 120;
            _split.Panel2MinSize = 90;
            _split.Panel1.Controls.Add(_grid);
            _split.Panel2.Controls.Add(_log);

            StatusStrip status = new StatusStrip();
            _progress = new ProgressBar();
            _progress.Width = 260;
            _statusLabel = new ToolStripStatusLabel("Готово");
            status.Items.Add(new ToolStripStatusLabel("Прогресс:"));
            status.Items.Add(new ToolStripControlHost(_progress));
            status.Items.Add(new ToolStripStatusLabel("   "));
            status.Items.Add(_statusLabel);

            Controls.Add(_split);
            Controls.Add(top);
            Controls.Add(status);
        }

        private static Button MakeButton(string text)
        {
            Button b = new Button();
            b.Text = text;
            b.AutoSize = true;
            b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.MinimumSize = new Size(92, 30);
            b.Padding = new Padding(8, 0, 8, 0);
            b.Margin = new Padding(3, 6, 3, 3);
            return b;
        }

        private void AddColumn(string header, int fillWeight)
        {
            DataGridViewTextBoxColumn col = new DataGridViewTextBoxColumn();
            col.HeaderText = header;
            col.FillWeight = fillWeight;
            col.SortMode = DataGridViewColumnSortMode.NotSortable;
            col.Resizable = DataGridViewTriState.True;
            _grid.Columns.Add(col);
        }

        private static string AddressOf(PcTarget t)
        {
            if (!string.IsNullOrWhiteSpace(t.Hostname) && !string.IsNullOrWhiteSpace(t.Ip))
            {
                return t.Hostname + " / " + t.Ip;
            }
            if (!string.IsNullOrWhiteSpace(t.Hostname))
            {
                return t.Hostname;
            }
            return t.Ip;
        }

        private void ReloadGrid()
        {
            _rows.Clear();
            _targets.Clear();
            _grid.Rows.Clear();

            if (_config.PcRecords != null)
            {
                foreach (PcRecord rec in _config.PcRecords)
                {
                    if (string.IsNullOrWhiteSpace(rec.Hostname) && string.IsNullOrWhiteSpace(rec.Ip))
                    {
                        continue;
                    }
                    PcTarget t = PcTarget.FromRecord(rec);
                    _targets.Add(t);
                    int i = _grid.Rows.Add(t.Name, AddressOf(t), "—", "", "", "", "", "");
                    _rows[t] = _grid.Rows[i];
                }
            }

            _statusLabel.Text = "Компьютеров: " + _targets.Count;
        }

        private void OnSettings(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }
            using (SettingsForm f = new SettingsForm(_config))
            {
                if (f.ShowDialog(this) == DialogResult.OK)
                {
                    _config = f.Config;
                    ReloadGrid();
                }
            }
        }

        private void OnOpenFolder(object sender, EventArgs e)
        {
            try
            {
                Directory.CreateDirectory(_config.DestinationRoot);
                Process.Start("explorer.exe", "\"" + _config.DestinationRoot + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Не удалось открыть папку",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnStop(object sender, EventArgs e)
        {
            if (_cts != null)
            {
                _cts.Cancel();
                Log("Запрошена остановка сбора.");
            }
        }

        private async void OnCollect(object sender, EventArgs e)
        {
            if (_busy)
            {
                return;
            }

            ReloadGrid();
            if (_targets.Count == 0)
            {
                MessageBox.Show(this, "Список ПК пуст. Откройте «Настройки» и добавьте компьютеры.",
                    "Нет компьютеров", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(_config.UserName))
            {
                MessageBox.Show(this, "Не указан пользователь-администратор. Откройте «Настройки».",
                    "Нет данных", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (string.IsNullOrEmpty(_config.Password))
            {
                using (PasswordPrompt p = new PasswordPrompt(_config.UserName,
                    "Пароль используется только для сбора файлов с ученических ПК."))
                {
                    if (p.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }
                    _config.Password = p.Password;
                }
            }

            try
            {
                Directory.CreateDirectory(_config.DestinationRoot);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось создать папку сохранения:\n" + ex.Message,
                    "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _busy = true;
            _btnCollect.Enabled = false;
            _btnSettings.Enabled = false;
            _btnStop.Enabled = true;
            _log.Clear();
            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            DateTime day = _date.Value.Date;
            List<PcTarget> pcs = new List<PcTarget>(_targets);
            int total = pcs.Count;
            int done = 0;
            _progress.Maximum = Math.Max(1, total);
            _progress.Value = 0;
            _statusLabel.Text = "Сбор... 0/" + total;

            DesktopCollector collector = new DesktopCollector(_config, Log);
            int parallelism = Math.Max(1, _config.MaxParallel);

            using (SemaphoreSlim sem = new SemaphoreSlim(parallelism))
            {
                List<Task> tasks = pcs.Select(pc => (Task)(CollectOneAsync(pc, sem, token, collector, day,
                    () => Interlocked.Increment(ref done), total))).ToList();

                await Task.WhenAll(tasks);
            }

            _busy = false;
            _btnCollect.Enabled = true;
            _btnSettings.Enabled = true;
            _btnStop.Enabled = false;
            _cts.Dispose();
            _cts = null;

            // Сохраняем обогащённый инвентарь (актуальные IP/имена).
            try { ConfigStore.Save(_config); }
            catch { /* не критично */ }

            int ok = 0;
            foreach (DataGridViewRow r in _grid.Rows)
            {
                if (Convert.ToString(r.Cells[2].Value) == "Готово")
                {
                    ok++;
                }
            }
            _statusLabel.Text = "Готово. Успешно: " + ok + " из " + total + ".";
            Log("Сбор завершён. Успешно: " + ok + " из " + total + ".");

            MessageBox.Show(this,
                "Сбор завершён.\r\nУспешно: " + ok + " из " + total +
                ".\r\n\r\nПапка: " + _config.DestinationRoot,
                "Готово", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private async Task CollectOneAsync(PcTarget pc, SemaphoreSlim sem, CancellationToken token,
            DesktopCollector collector, DateTime day, Func<int> bumpDone, int total)
        {
            await sem.WaitAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                SetRowRunning(pc);
                CollectResult res = await Task.Run(() => collector.Collect(pc, day), token).ConfigureAwait(false);
                ApplyResult(pc, res);
            }
            catch (OperationCanceledException)
            {
                Ui(() =>
                {
                    DataGridViewRow r;
                    if (_rows.TryGetValue(pc, out r))
                    {
                        r.Cells[2].Value = "Отменено";
                    }
                });
            }
            catch (Exception ex)
            {
                Ui(() =>
                {
                    DataGridViewRow r;
                    if (_rows.TryGetValue(pc, out r))
                    {
                        r.Cells[2].Value = "Ошибка";
                        r.Cells[7].Value = ex.Message;
                    }
                });
            }
            finally
            {
                sem.Release();
                int d = bumpDone();
                Ui(() =>
                {
                    _progress.Value = Math.Min(_progress.Maximum, d);
                    _statusLabel.Text = "Сбор... " + d + "/" + total;
                    DataGridViewRow r;
                    if (_rows.TryGetValue(pc, out r))
                    {
                        r.Cells[1].Value = AddressOf(pc);
                    }
                });
            }
        }

        private static string StatusText(PcStatus s)
        {
            switch (s)
            {
                case PcStatus.Pending: return "—";
                case PcStatus.Running: return "Работа...";
                case PcStatus.Offline: return "Недоступен";
                case PcStatus.Ok: return "Готово";
                case PcStatus.Error: return "Ошибка";
                case PcStatus.Cancelled: return "Отменено";
                default: return "?";
            }
        }

        private void SetRowRunning(PcTarget pc)
        {
            Ui(() =>
            {
                DataGridViewRow r;
                if (_rows.TryGetValue(pc, out r))
                {
                    r.Cells[2].Value = "Работа...";
                    r.Cells[7].Value = "";
                    r.DefaultCellStyle.ForeColor = Color.Black;
                }
            });
        }

        private void ApplyResult(PcTarget pc, CollectResult res)
        {
            Ui(() =>
            {
                DataGridViewRow row;
                if (!_rows.TryGetValue(pc, out row))
                {
                    return;
                }

                row.Cells[2].Value = StatusText(res.Status);
                row.Cells[3].Value = res.FoldersFound > 0 ? res.FoldersFound.ToString() : "";
                row.Cells[4].Value = res.FoldersCopied > 0 ? res.FoldersCopied.ToString() : "";
                row.Cells[5].Value = res.FilesCopied > 0 ? res.FilesCopied.ToString() : "";
                row.Cells[6].Value = res.FinishedAt > res.StartedAt
                    ? (res.FinishedAt - res.StartedAt).TotalSeconds.ToString("0.0") + " с"
                    : "";
                row.Cells[7].Value = res.Message;

                row.DefaultCellStyle.ForeColor =
                    res.Status == PcStatus.Error ? Color.Firebrick :
                    res.Status == PcStatus.Offline ? Color.DarkOrange :
                    Color.Black;
            });
        }

        private void Ui(Action action)
        {
            if (IsDisposed)
            {
                return;
            }
            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(action);
                }
                else
                {
                    action();
                }
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void Log(string line)
        {
            Logger.Write(line);
            Ui(() => _log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + line + Environment.NewLine));
        }
    }
}
