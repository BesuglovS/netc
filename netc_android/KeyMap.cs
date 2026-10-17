namespace NetcAndroid;

/// <summary>
/// Соответствие символов и Android-клавиш виртуальным кодам Windows (VK),
/// которые ожидает агент в <c>InputEvent.Vk</c>. Раскладка — US QWERTY.
/// </summary>
public static class KeyMap
{
    public const int Shift = 0x10;
    public const int Ctrl = 0x11;
    public const int Alt = 0x12;
    public const int Win = 0x5B;
    public const int Escape = 0x1B;
    public const int Tab = 0x09;
    public const int Return = 0x0D;
    public const int Back = 0x08;
    public const int Delete = 0x2E;
    public const int Left = 0x25;
    public const int Up = 0x26;
    public const int Right = 0x27;
    public const int Down = 0x28;
    public const int Home = 0x24;
    public const int End = 0x23;
    public const int PageUp = 0x21;
    public const int PageDown = 0x22;
    public const int F5 = 0x74;

    /// <summary>Виртуальный код Windows и необходимость Shift для символа (null — неизвестно).</summary>
    public static (int Vk, bool Shift)? Char(char c)
    {
        if (c >= 'a' && c <= 'z') return (c - 32, false);
        if (c >= 'A' && c <= 'Z') return (c, true);
        if (c >= '0' && c <= '9') return (c, false);

        switch (c)
        {
            case ' ': return (0x20, false);
            case '\n': return (Return, false);
            case '\r': return (Return, false);
            case '\t': return (Tab, false);
            case '!': return (0x31, true);
            case '@': return (0x32, true);
            case '#': return (0x33, true);
            case '$': return (0x34, true);
            case '%': return (0x35, true);
            case '^': return (0x36, true);
            case '&': return (0x37, true);
            case '*': return (0x38, true);
            case '(': return (0x39, true);
            case ')': return (0x30, true);
            case '-': return (0xBD, false);
            case '_': return (0xBD, true);
            case '=': return (0xBB, false);
            case '+': return (0xBB, true);
            case '[': return (0xDB, false);
            case '{': return (0xDB, true);
            case ']': return (0xDD, false);
            case '}': return (0xDD, true);
            case '\\': return (0xDC, false);
            case '|': return (0xDC, true);
            case ';': return (0xBA, false);
            case ':': return (0xBA, true);
            case '\'': return (0xDE, false);
            case '"': return (0xDE, true);
            case ',': return (0xBC, false);
            case '<': return (0xBC, true);
            case '.': return (0xBE, false);
            case '>': return (0xBE, true);
            case '/': return (0xBF, false);
            case '?': return (0xBF, true);
            case '`': return (0xC0, false);
            case '~': return (0xC0, true);
            default: return null;
        }
    }

    /// <summary>Виртуальный код Windows для клавиши физической/экранной клавиатуры Android.</summary>
    public static int? VkFromAndroidKey(Android.Views.Keycode keycode)
    {
        int k = (int)keycode;
        // Наши пробелы: A..Z идут подряд (29..54), 0..9 — (7..16).
        if (k >= (int)Android.Views.Keycode.A && k <= (int)Android.Views.Keycode.Z)
            return 0x41 + (k - (int)Android.Views.Keycode.A);
        if (k >= (int)Android.Views.Keycode.Num0 && k <= (int)Android.Views.Keycode.Num9)
            return 0x30 + (k - (int)Android.Views.Keycode.Num0);

        return keycode switch
        {
            Android.Views.Keycode.Space => 0x20,
            Android.Views.Keycode.Enter => Return,
            Android.Views.Keycode.Tab => Tab,
            Android.Views.Keycode.Del => Back,
            Android.Views.Keycode.ForwardDel => Delete,
            Android.Views.Keycode.Escape => Escape,
            Android.Views.Keycode.DpadLeft => Left,
            Android.Views.Keycode.DpadUp => Up,
            Android.Views.Keycode.DpadRight => Right,
            Android.Views.Keycode.DpadDown => Down,
            Android.Views.Keycode.MoveHome => Home,
            Android.Views.Keycode.MoveEnd => End,
            Android.Views.Keycode.PageUp => PageUp,
            Android.Views.Keycode.PageDown => PageDown,
            Android.Views.Keycode.ShiftLeft => Shift,
            Android.Views.Keycode.ShiftRight => Shift,
            Android.Views.Keycode.CtrlLeft => Ctrl,
            Android.Views.Keycode.CtrlRight => Ctrl,
            Android.Views.Keycode.AltLeft => Alt,
            Android.Views.Keycode.AltRight => Alt,
            Android.Views.Keycode.MetaLeft => Win,
            Android.Views.Keycode.MetaRight => Win,
            Android.Views.Keycode.Minus => 0xBD,
            Android.Views.Keycode.Equals => 0xBB,
            Android.Views.Keycode.LeftBracket => 0xDB,
            Android.Views.Keycode.RightBracket => 0xDD,
            Android.Views.Keycode.Backslash => 0xDC,
            Android.Views.Keycode.Semicolon => 0xBA,
            Android.Views.Keycode.Apostrophe => 0xDE,
            Android.Views.Keycode.Comma => 0xBC,
            Android.Views.Keycode.Period => 0xBE,
            Android.Views.Keycode.Slash => 0xBF,
            Android.Views.Keycode.Grave => 0xC0,
            Android.Views.Keycode.F1 => 0x70,
            Android.Views.Keycode.F2 => 0x71,
            Android.Views.Keycode.F3 => 0x72,
            Android.Views.Keycode.F4 => 0x73,
            Android.Views.Keycode.F5 => 0x74,
            Android.Views.Keycode.F6 => 0x75,
            Android.Views.Keycode.F7 => 0x76,
            Android.Views.Keycode.F8 => 0x77,
            Android.Views.Keycode.F9 => 0x78,
            Android.Views.Keycode.F10 => 0x79,
            Android.Views.Keycode.F11 => 0x7A,
            Android.Views.Keycode.F12 => 0x7B,
            _ => null
        };
    }
}
