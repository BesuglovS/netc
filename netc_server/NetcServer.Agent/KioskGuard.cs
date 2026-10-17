using System.Runtime.InteropServices;
using Microsoft.Win32;
using NetcShared;

namespace NetcServer.Agent;

/// <summary>
/// Режим «киоска» на время теста: полный перехват клавиш переключения/закрытия,
/// скрытие панели задач, удержание окна теста поверх остальных, запрет диспетчера задач
/// и (опционально) белый список приложений. Работает в сессии ученика без прав администратора.
/// </summary>
public static class KioskGuard
{
    public static bool Enabled { get; private set; }

    private static Form? _target;
    private static IntPtr _hook;
    private static LowLevelKeyboardProc? _proc;
    private static System.Windows.Forms.Timer? _focusTimer;
    private static readonly List<IntPtr> _taskbars = new();

    // Прежнее состояние того, что мы временно меняем.
    private static bool _hadPolicy;
    private static AppControlMode _prevMode;
    private static List<string> _prevNames = new();
    private static bool _hadTaskMgr;
    private static object? _prevTaskMgr;

    /// <summary>Включить киоск для указанного окна (вызывать из UI-потока).</summary>
    public static void Enable(Form target)
    {
        if (Enabled)
        {
            // Тест вытеснен другим (или выдан повторно) — переводим удержание фокуса на новое окно,
            // не переустанавливая хуки и политики.
            _target = target;
            return;
        }
        Enabled = true;

        // Предохранитель для локальной отладки: не захватывать машину разработчика.
        if (Environment.GetEnvironmentVariable("NETC_TEST_NOKIOSK") == "1")
        {
            AgentJournal.Add("test", "Киоск отключён переменной NETC_TEST_NOKIOSK");
            return;
        }

        _target = target;

        try { CatchKeys(); } catch { }
        try { HideTaskbars(); } catch { }
        try { BlockTaskManager(true); } catch { }
        try { SaveAppPolicy(); AppControl.SetPolicy(AppControlMode.Whitelist, Array.Empty<string>()); } catch { }
        try { StartFocusTimer(); } catch { }

        AgentJournal.Add("test", "Включён режим киоска (тест)");
    }

    /// <summary>Выключить киоск и вернуть прежние настройки (вызывать из UI-потока).</summary>
    public static void Disable()
    {
        if (!Enabled && _hook == IntPtr.Zero) { return; }
        Enabled = false;
        _target = null;

        try { _focusTimer?.Stop(); _focusTimer?.Dispose(); } catch { }
        _focusTimer = null;

        try { ReleaseKeys(); } catch { }
        try { RestoreTaskbars(); } catch { }
        try { BlockTaskManager(false); } catch { }
        try { RestoreAppPolicy(); } catch { }

        AgentJournal.Add("test", "Режим киоска выключен");
    }

    /// <summary>
    /// Восстановление после падения/перезапуска агента: показать панель задач и разрешить
    /// диспетчер задач, если настройки остались с прошлого запуска (иначе ПК «залипнет» в киоске).
    /// </summary>
    public static void RecoverOnStart()
    {
        try { ShowAllTaskbars(); } catch { }

        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(PolicyPath, writable: true);
            if (k is not null && k.GetValue("DisableTaskMgr") is not null)
            {
                try { k.DeleteValue("DisableTaskMgr", throwOnMissingValue: false); } catch { }
                AgentJournal.Add("test", "Киоск: восстановлен доступ к диспетчеру задач после перезапуска агента");
            }
        }
        catch { }

        Enabled = false;
        _target = null;
    }

    // ---- Перехват клавиатуры ----

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private const int VK_TAB = 0x09;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_F4 = 0x73;
    private const int VK_F10 = 0x79;
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

    private static void CatchKeys()
    {
        if (_hook != IntPtr.Zero) return;
        _proc = HookCallback;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
    }

    private static void ReleaseKeys()
    {
        if (_hook == IntPtr.Zero) return;
        try { UnhookWindowsHookEx(_hook); } catch { }
        _hook = IntPtr.Zero;
        _proc = null;
    }

    private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            var data = Marshal.PtrToStructure<KbdHookStruct>(lParam);
            int vk = (int)data.vkCode;
            bool alt = Down(VK_MENU);
            bool ctrl = Down(VK_CONTROL);
            bool shift = Down(VK_SHIFT);

            // Аварийный выход учителя на месте: Ctrl+Shift+F10 — пропускаем дальше.
            if (ctrl && shift && vk == VK_F10) return CallNextHookEx(_hook, nCode, wParam, lParam);

            bool blocked =
                (vk == VK_F4 && alt) ||                                   // Alt+F4
                (vk == VK_TAB && alt) ||                                  // Alt+Tab
                (vk == VK_ESCAPE && (alt || ctrl)) ||                     // Alt+Esc / Ctrl+Esc
                (vk == VK_ESCAPE && ctrl && shift) ||                     // Ctrl+Shift+Esc (диспетчер задач)
                vk == VK_LWIN || vk == VK_RWIN;                           // клавиша Windows

            if (blocked) return (IntPtr)1;
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    // ---- Панель задач ----

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private static void HideTaskbars()
    {
        _taskbars.Clear();
        EnumWindows((h, _) =>
        {
            var sb = new System.Text.StringBuilder(64);
            GetClassName(h, sb, sb.Capacity);
            string cls = sb.ToString();
            if (cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd")
            {
                _taskbars.Add(h);
                ShowWindow(h, SW_HIDE);
            }
            return true;
        }, IntPtr.Zero);
    }

    private static void RestoreTaskbars()
    {
        foreach (var h in _taskbars) { try { ShowWindow(h, SW_SHOW); } catch { } }
        _taskbars.Clear();
    }

    /// <summary>Показать все панели задач (в т.ч. те, что остались скрытыми от прошлого запуска).</summary>
    private static void ShowAllTaskbars()
    {
        EnumWindows((h, _) =>
        {
            var sb = new System.Text.StringBuilder(64);
            GetClassName(h, sb, sb.Capacity);
            string cls = sb.ToString();
            if (cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") ShowWindow(h, SW_SHOW);
            return true;
        }, IntPtr.Zero);
    }

    // ---- Удержание фокуса ----

    private static void StartFocusTimer()
    {
        _focusTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _focusTimer.Tick += (_, _) =>
        {
            var f = _target;
            if (f is null || f.IsDisposed) return;
            try
            {
                if (GetForegroundWindow() != f.Handle)
                {
                    f.TopMost = false;
                    f.TopMost = true;
                    SetForegroundWindow(f.Handle);
                    f.Activate();
                }
            }
            catch { }
        };
        _focusTimer.Start();
    }

    // ---- Диспетчер задач ----

    private const string PolicyPath = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";

    private static void BlockTaskManager(bool block)
    {
        if (block)
        {
            using var k = Registry.CurrentUser.CreateSubKey(PolicyPath);
            if (k is null) return;
            _prevTaskMgr = k.GetValue("DisableTaskMgr");
            _hadTaskMgr = _prevTaskMgr is not null;
            k.SetValue("DisableTaskMgr", 1, RegistryValueKind.DWord);
        }
        else
        {
            using var k = Registry.CurrentUser.OpenSubKey(PolicyPath, writable: true);
            if (k is null) return;
            if (_hadTaskMgr) k.SetValue("DisableTaskMgr", _prevTaskMgr!, RegistryValueKind.DWord);
            else { try { k.DeleteValue("DisableTaskMgr", throwOnMissingValue: false); } catch { } }
        }
    }

    // ---- Белый список приложений ----

    private static void SaveAppPolicy()
    {
        _prevMode = AppControl.Mode;
        _prevNames = AppControl.Names.ToList();
        _hadPolicy = true;
    }

    private static void RestoreAppPolicy()
    {
        if (!_hadPolicy) return;
        _hadPolicy = false;
        try { AppControl.SetPolicy(_prevMode, _prevNames); } catch { }
    }
}
