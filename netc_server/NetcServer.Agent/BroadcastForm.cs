using System.Drawing;

namespace NetcServer.Agent;

/// <summary>Полноэкранное окно трансляции экрана учителя.</summary>
public sealed class BroadcastForm : Form
{
    private readonly PictureBox _pic;

    public BroadcastForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = SystemInformation.VirtualScreen;
        TopMost = true;
        BackColor = Color.Black;
        ShowInTaskbar = false;

        _pic = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Black
        };
        Controls.Add(_pic);

        // Блокируем «опасные» сочетания (Alt+F4, Alt+Tab, Win), чтобы трансляцию нельзя было закрыть.
        KeyBlocker.Enable();
        FormClosed += (_, _) => KeyBlocker.Disable();
    }

    public void SetFrame(byte[] jpeg)
    {
        var bmp = Decode(jpeg);
        if (bmp is null) return;
        var old = _pic.Image;
        _pic.Image = bmp;
        old?.Dispose();
    }

    private static Bitmap? Decode(byte[] data)
    {
        try
        {
            using var ms = new MemoryStream(data);
            using var img = Image.FromStream(ms);
            return new Bitmap(img);
        }
        catch { return null; }
    }
}
