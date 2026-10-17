using System.Drawing;
using System.Drawing.Imaging;

namespace NetcClient;

/// <summary>Захват экрана учителя для трансляции.</summary>
public static class ClientScreenCapture
{
    private static readonly ImageCodecInfo? JpegCodec =
        ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);

    public static byte[] CaptureJpeg(int maxWidth, int quality)
    {
        Rectangle bounds = SystemInformation.VirtualScreen;
        if (bounds.Width <= 0 || bounds.Height <= 0) return Array.Empty<byte>();

        using var full = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(full))
        {
            g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        }

        int w = bounds.Width, h = bounds.Height;
        if (maxWidth > 0 && w > maxWidth)
        {
            h = Math.Max(1, (int)Math.Round(h * (maxWidth / (double)w)));
            w = maxWidth;
        }

        using var small = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
            g.DrawImage(full, 0, 0, w, h);
        }

        using var ms = new MemoryStream();
        if (JpegCodec is null)
        {
            small.Save(ms, ImageFormat.Jpeg);
        }
        else
        {
            using var pars = new EncoderParameters(1);
            pars.Param[0] = new EncoderParameter(Encoder.Quality, (long)Math.Clamp(quality, 10, 100));
            small.Save(ms, JpegCodec, pars);
        }
        return ms.ToArray();
    }
}
