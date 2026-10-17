using System.Drawing;

namespace NetcServer.Agent;

/// <summary>Полноэкранная блокировка/затемнение экрана ученика.</summary>
public sealed class LockForm : Form
{
    public LockForm(bool blank, string text)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = SystemInformation.VirtualScreen;
        TopMost = true;
        BackColor = blank ? Color.Black : Color.FromArgb(20, 22, 32);
        ShowInTaskbar = false;
        KeyPreview = true;
        System.Windows.Forms.Cursor.Hide();

        // Блокируем «опасные» сочетания (Alt+F4, Alt+Tab, Win) — иначе ученик закроет блокировку.
        KeyBlocker.Enable();
        FormClosed += (_, _) =>
        {
            KeyBlocker.Disable();
            System.Windows.Forms.Cursor.Show();
        };

        if (!blank)
        {
            var lbl = new Label
            {
                Text = text,
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI", 28F, FontStyle.Regular),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };
            Controls.Add(lbl);
        }

        KeyDown += (_, e) => e.SuppressKeyPress = true;
        KeyPress += (_, e) => e.Handled = true;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) => true;
}
