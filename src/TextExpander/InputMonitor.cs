using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TextExpander;

public enum KeyKind { Ignore, Char, Backspace, Reset }

/// <summary>
/// Global low-level keyboard and mouse hooks. Never swallows input, except the layout-fix hotkey
/// (Pause or Ctrl+Alt+Space) while <see cref="ConvertLayoutEnabled"/> is set.
/// Must be started on a thread with a message loop; events are raised on that thread.
/// </summary>
public sealed class InputMonitor : IDisposable
{
    public const nuint SyntheticMarker = 0x54455850; // dwExtraInfo of our own SendInput events — ignored

    public event Action<char>? CharTyped;
    public event Action? BackspacePressed;
    public event Action? ResetRequested;
    /// <summary>Layout-fix hotkey pressed. Raised inside the hook: handlers must only post work.</summary>
    public event Action? ConvertLayoutRequested;

    /// <summary>When false the hotkey is not swallowed and the event is not raised.</summary>
    public bool ConvertLayoutEnabled { get; set; } = true;

    private const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
    private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    private const int WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207, WM_XBUTTONDOWN = 0x020B;
    private const int VK_BACK = 0x08, VK_TAB = 0x09, VK_RETURN = 0x0D, VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
    private const int VK_PAUSE = 0x13, VK_SPACE = 0x20;
    private const int VK_CAPITAL = 0x14, VK_ESCAPE = 0x1B, VK_PRIOR = 0x21, VK_DOWN = 0x28, VK_INSERT = 0x2D, VK_DELETE = 0x2E;
    private const int VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_NUMLOCK = 0x90, VK_SCROLL = 0x91;
    private const int VK_LSHIFT = 0xA0, VK_RMENU = 0xA5;
    private const uint ToUnicodeNoStateChange = 0x4; // Windows 10 1607+: do not alter keyboard state (keeps dead keys intact)

    private readonly HookProc _keyboardProc;
    private readonly HookProc _mouseProc;
    private IntPtr _keyboardHook, _mouseHook;
    private IntPtr _lastForeground;
    private int _swallowedVk; // hotkey key that is down and swallowed; its repeats and key-up are swallowed too

    public InputMonitor()
    {
        // Delegates are kept in fields so the GC does not collect them while hooks are installed.
        _keyboardProc = KeyboardCallback;
        _mouseProc = MouseCallback;
    }

    public void Start()
    {
        if (_keyboardHook != IntPtr.Zero)
            return;
        var module = GetModuleHandle(null);
        _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, module, 0);
        if (_keyboardHook == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, module, 0);
        if (_mouseHook == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            Dispose();
            throw new Win32Exception(error);
        }
        _lastForeground = GetForegroundWindow();
    }

    public void Dispose()
    {
        if (_keyboardHook != IntPtr.Zero) { UnhookWindowsHookEx(_keyboardHook); _keyboardHook = IntPtr.Zero; }
        if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
    }

    /// <summary>Decides what a key-down means for the typed tail. Pure; no WinAPI.</summary>
    public static KeyKind Classify(int vk, bool ctrl, bool alt, bool win)
    {
        switch (vk)
        {
            case VK_SHIFT or VK_CONTROL or VK_MENU or VK_CAPITAL or VK_NUMLOCK or VK_SCROLL or VK_LWIN or VK_RWIN:
            case >= VK_LSHIFT and <= VK_RMENU:
                return KeyKind.Ignore;
        }

        // Ctrl+Alt together is how Windows reports AltGr, which types characters.
        var altGr = ctrl && alt;
        if (win || (ctrl && !altGr) || (alt && !altGr))
            return KeyKind.Reset;

        return vk switch
        {
            VK_BACK => altGr ? KeyKind.Reset : KeyKind.Backspace,
            VK_TAB or VK_RETURN or VK_ESCAPE => KeyKind.Reset,
            >= VK_PRIOR and <= VK_DOWN => KeyKind.Reset, // PgUp, PgDn, End, Home, arrows
            VK_INSERT or VK_DELETE => KeyKind.Reset,
            _ => KeyKind.Char,
        };
    }

    private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var swallow = false;
            try { swallow = OnKeyboard((int)wParam, Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam)); }
            catch { /* never let an exception escape a hook callback */ }
            if (swallow)
                return 1; // the hotkey never reaches the window
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var message = (int)wParam;
            if (message is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_XBUTTONDOWN)
            {
                try { ResetRequested?.Invoke(); }
                catch { /* never let an exception escape a hook callback */ }
            }
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    /// <returns>true to swallow the key.</returns>
    private bool OnKeyboard(int message, KBDLLHOOKSTRUCT data)
    {
        if (data.dwExtraInfo == SyntheticMarker)
            return false;
        var vk = (int)data.vkCode;
        if (message is WM_KEYUP or WM_SYSKEYUP)
        {
            if (vk != _swallowedVk)
                return false;
            _swallowedVk = 0;
            return true;
        }
        if (message is not (WM_KEYDOWN or WM_SYSKEYDOWN))
            return false;

        var foreground = GetForegroundWindow();
        if (foreground != _lastForeground)
        {
            _lastForeground = foreground;
            ResetRequested?.Invoke();
        }

        var ctrl = IsDown(VK_CONTROL);
        var alt = IsDown(VK_MENU);
        var win = IsDown(VK_LWIN) || IsDown(VK_RWIN);

        if (ConvertLayoutEnabled && (vk == VK_PAUSE || (vk == VK_SPACE && ctrl && alt && !win)))
        {
            if (vk != _swallowedVk) // auto-repeat while held raises nothing
            {
                _swallowedVk = vk;
                ConvertLayoutRequested?.Invoke();
            }
            return true;
        }

        switch (Classify(vk, ctrl, alt, win))
        {
            case KeyKind.Reset:
                ResetRequested?.Invoke();
                break;
            case KeyKind.Backspace:
                BackspacePressed?.Invoke();
                break;
            case KeyKind.Char:
                var c = Translate(data, foreground, altGr: ctrl && alt);
                if (c is char ch)
                    CharTyped?.Invoke(ch);
                break;
        }
        return false;
    }

    private static char? Translate(KBDLLHOOKSTRUCT data, IntPtr foreground, bool altGr)
    {
        var state = new byte[256];
        if (IsDown(VK_SHIFT)) state[VK_SHIFT] = 0x80;
        if (altGr) { state[VK_CONTROL] = 0x80; state[VK_MENU] = 0x80; }
        if ((GetKeyState(VK_CAPITAL) & 1) != 0) state[VK_CAPITAL] = 0x01;

        var layout = GetKeyboardLayout(GetWindowThreadProcessId(foreground, out _));
        var buffer = new char[8];
        var count = ToUnicodeEx(data.vkCode, data.scanCode, state, buffer, buffer.Length, ToUnicodeNoStateChange, layout);
        // count < 0: dead key pending; 0: no character; take a single printable character only.
        if (count != 1 || char.IsControl(buffer[0]))
            return null;
        return buffer[0];
    }

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(uint wVirtKey, uint wScanCode, byte[] lpKeyState,
        [Out, MarshalAs(UnmanagedType.LPArray)] char[] pwszBuff, int cchBuff, uint wFlags, IntPtr dwhkl);
}
