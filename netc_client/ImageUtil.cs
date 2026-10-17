using System.Drawing;

namespace NetcClient;

public static class ImageUtil
{
    public static Bitmap? DecodeJpeg(byte[] data)
    {
        try
        {
            using var ms = new MemoryStream(data);
            using var img = Image.FromStream(ms);
            return new Bitmap(img);
        }
        catch
        {
            return null;
        }
    }
}
