using System.Runtime.InteropServices;
using NetcShared;

namespace NetcServer.Agent;

/// <summary>Инъекция мыши и клавиатуры в сессию ученика через SendInput.
/// Координаты мыши приходят нормированными (0..65535) и передаются как абсолютные
/// по всему виртуальному рабочему столу (Multi-Monitor).</summary>
public static class InputInjector
{
    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;

    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    public static void Handle(InputEvent e)
    {
        if (e.Type == "key")
        {
            SendKey((ushort)e.Vk, e.Down);
            return;
        }

        switch (e.Action)
        {
            case "move":
                MoveTo(e.X, e.Y);
                break;
            case "down":
                MoveTo(e.X, e.Y);
                SendMouseButton(ButtonFlag(e.Button, true));
                break;
            case "up":
                SendMouseButton(ButtonFlag(e.Button, false));
                break;
            case "wheel":
                SendMouseWheel(e.Delta);
                break;
        }
    }

    private static void MoveTo(int nx, int ny)
    {
        var input = new INPUT { type = INPUT_MOUSE };
        input.U.mi = new MOUSEINPUT
        {
            dx = Math.Clamp(nx, 0, 65535),
            dy = Math.Clamp(ny, 0, 65535),
            dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK
        };
        Send(input);
    }

    private static void SendMouseButton(uint flag)
    {
        var input = new INPUT { type = INPUT_MOUSE };
        input.U.mi = new MOUSEINPUT { dwFlags = flag };
        Send(input);
    }

    private static void SendMouseWheel(int delta)
    {
        var input = new INPUT { type = INPUT_MOUSE };
        input.U.mi = new MOUSEINPUT { mouseData = unchecked((uint)delta), dwFlags = MOUSEEVENTF_WHEEL };
        Send(input);
    }

    private static void SendKey(ushort vk, bool down)
    {
        uint flags = down ? 0u : KEYEVENTF_KEYUP;
        if (IsExtendedKey(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
        var input = new INPUT { type = INPUT_KEYBOARD };
        input.U.ki = new KEYBDINPUT { wVk = vk, dwFlags = flags };
        Send(input);
    }

    private static void Send(INPUT input) => SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());

    private static uint ButtonFlag(string button, bool down) => button switch
    {
        "right" => down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP,
        "middle" => down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP,
        _ => down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP
    };

    /// <summary>Клавиши, для которых SendInput требует флаг EXTENDEDKEY (навигация, Win и т.п.).</summary>
    private static bool IsExtendedKey(ushort vk) => vk switch
    {
        0x21 or 0x22 or 0x23 or 0x24  // PgUp / PgDn / End / Home
            or 0x25 or 0x26 or 0x27 or 0x28 => true, // ← ↑ → ↓
        0x2D or 0x2E => true,         // Insert / Delete
        0x5B or 0x5C or 0x5D => true, // LWin / RWin / Apps
        0x6F => true,                 // Numpad /
        0xA3 or 0xA5 => true,         // RCtrl / RAlt
        _ => false
    };
}
