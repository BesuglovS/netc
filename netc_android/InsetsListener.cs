using Android.OS;
using Android.Views;

namespace NetcAndroid;

/// <summary>Отступ содержимого от системных панелей в режиме edge-to-edge (Android 15+).</summary>
internal sealed class InsetsListener : Java.Lang.Object, View.IOnApplyWindowInsetsListener
{
    public WindowInsets OnApplyWindowInsets(View v, WindowInsets insets)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
        {
            var bars = insets.GetInsets(WindowInsets.Type.SystemBars());
            v.SetPadding(bars.Left, bars.Top, bars.Right, bars.Bottom);
        }
        else
        {
            v.SetPadding(insets.SystemWindowInsetLeft, insets.SystemWindowInsetTop,
                insets.SystemWindowInsetRight, insets.SystemWindowInsetBottom);
        }
        return insets;
    }
}
