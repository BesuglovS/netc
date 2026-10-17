using Android.Graphics;

namespace NetcAndroid;

/// <summary>Декодирование кадров экрана (JPEG), приходящих от агентов.</summary>
public static class AndroidImage
{
    public static Bitmap? DecodeJpeg(byte[] data)
    {
        try
        {
            return BitmapFactory.DecodeByteArray(data, 0, data.Length);
        }
        catch
        {
            return null;
        }
    }
}
