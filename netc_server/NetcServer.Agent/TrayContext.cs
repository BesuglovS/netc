namespace NetcServer.Agent;

/// <summary>Иконка агента в трее (процесс в сессии ученика).</summary>
public sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly Form _host;

    public TrayContext()
    {
        // Скрытый UI-хост для маршалинга вызовов из фонового канала в UI-поток.
        _host = new Form
        {
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000),
            Size = new Size(1, 1)
        };
        _ = _host.Handle;
        AgentUi.Initialize(_host);

        var menu = new ContextMenuStrip();
        menu.Items.Add("О программе", null, (_, _) => ShowAbout());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ExitThread());

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "netc_server (агент)",
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => ShowAbout();
        _icon.ShowBalloonTip(3000, "netc_server", "Агент управления классом запущен.", ToolTipIcon.Info);
    }

    private void ShowAbout()
    {
        MessageBox.Show(
            $"netc_server — агент управления классом.\nПК: {Environment.MachineName}\nПользователь: {Environment.UserName}",
            "netc_server", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _icon.Visible = false;
            _icon.Dispose();
            _host.Dispose();
        }
        base.Dispose(disposing);
    }
}
