using System.Runtime.InteropServices;

namespace NetcServer.Agent;

/// <summary>
/// Блокировка «опасных» сочетаний клавиш (Alt+F4, Alt+Tab, Alt+Esc, Ctrl+Esc,
/// Ctrl+Shift+Esc, клавиша Windows) на время блокировки экрана или трансляции —
/// чтобы ученик не мог закрыть полноэкранное окно. Счётчик ссылок: хук снимается,
/// когда его отпустит последний пользователь.
/// </summary>
public static class KeyBlocker
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private const int VK_TAB = 0x09;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_F4 = 0x73;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_MENU = 0x12;   // Alt
    private const int VK_CONTROL = 0x11;
    private const int VK_SHIFT = 0x10;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdHookStruct
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private static readonly object Gate = new();
    private static int _refs;
    private static IntPtr _hook;
    private static LowLevelKeyboardProc? _proc;

    public static void Enable()
    {
        lock (Gate)
        {
            _refs++;
            if (_hook == IntPtr.Zero)
            {
                _proc = Callback;
                _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
            }
        }
    }

    public static void Disable()
    {
        lock (Gate)
        {
            if (_refs > 0) _refs--;
            if (_refs == 0 && _hook != IntPtr.Zero)
            {
                try { UnhookWindowsHookEx(_hook); } catch { }
                _hook = IntPtr.Zero;
                _proc = null;
            }
        }
    }

    private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private static IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            var data = Marshal.PtrToStructure<KbdHookStruct>(lParam);
            int vk = (int)data.vkCode;
            bool alt = Down(VK_MENU);
            bool ctrl = Down(VK_CONTROL);
            bool shift = Down(VK_SHIFT);

            bool blocked =
                (vk == VK_F4 && alt) ||
                (vk == VK_TAB && alt) ||
                (vk == VK_ESCAPE && (alt || ctrl)) ||
                (vk == VK_ESCAPE && ctrl && shift) ||
                vk == VK_LWIN || vk == VK_RWIN;

            if (blocked) return (IntPtr)1;
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }
}
