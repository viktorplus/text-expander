using System.Runtime.InteropServices;
using System.Text;

namespace TextExpander;

/// <summary>Replaces the typed trigger with text via the clipboard. UI (STA) thread only. Never throws.</summary>
public static class Paster
{
    private const int ClipboardRetries = 5;
    private const int ClipboardRetryDelayMs = 50;
    private const int RestoreDelayMs = 300;
    private const int CopyDelayMs = 150;

    private const ushort VK_BACK = 0x08, VK_CONTROL = 0x11, VK_C = 0x43, VK_V = 0x56;
    private const ushort VK_MENU_MASK = 0xE8; // unassigned key: tapped so releasing a held Alt/Win opens no menu
    private const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
    private const int LangRussian = 0x0419, LangEnglishUs = 0x0409;
    private static readonly ushort[] Modifiers =
    {
        0xA0, 0xA1, // LShift, RShift
        0xA2, 0xA3, // LControl, RControl
        0xA4, 0xA5, // LMenu, RMenu
        0x5B, 0x5C, // LWin, RWin
    };

    public static async Task ReplaceAsync(int eraseCount, string text)
    {
        try
        {
            var saved = SaveClipboard();
            try
            {
                Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, text.ReplaceLineEndings("\r\n")),
                    true, ClipboardRetries, ClipboardRetryDelayMs);
            }
            catch (ExternalException)
            {
                return; // clipboard locked by another process: nothing erased, the trigger stays as typed
            }

            var inputs = new List<INPUT>();
            AddModifierReleases(inputs);
            for (var i = 0; i < eraseCount; i++)
            {
                inputs.Add(Key(VK_BACK, up: false));
                inputs.Add(Key(VK_BACK, up: true));
            }
            Send(inputs);
            SendChord(VK_V);

            if (saved is null)
                return; // the user's clipboard could not be read: leave the template there, never clear it

            await Task.Delay(RestoreDelayMs);
            RestoreClipboard(saved);
        }
        catch
        {
            // Failures never reach the user; the clipboard may simply keep the template.
        }
    }

    /// <summary>
    /// Layout fix: converts the selection (taken with Ctrl+C) or, without one, <paramref name="lastWord"/>,
    /// pastes the result, restores the clipboard and switches the foreground window to the result's layout.
    /// </summary>
    public static async Task ConvertSelectionOrWordAsync(string lastWord)
    {
        DataObject? saved = null;
        try
        {
            saved = SaveClipboard();
            if (saved is null)
                return; // clipboard unreadable: cannot detect a selection or restore the user's data

            var release = new List<INPUT>();
            AddModifierReleases(release); // the hotkey's Ctrl+Alt are still held
            Send(release);

            var copied = false;
            var selection = "";
            if (!IsConsoleWindow(GetForegroundWindowClass()))
            {
                var sequence = GetClipboardSequenceNumber();
                SendChord(VK_C);
                await Task.Delay(CopyDelayMs);
                copied = GetClipboardSequenceNumber() != sequence;
                var text = copied && Clipboard.ContainsText() ? Clipboard.GetText() : "";
                if (IsSelection(copied, text))
                    selection = text;
            }

            var source = selection.Length > 0 ? selection : lastWord;
            var converted = LayoutConverter.Convert(source, out var toCyrillic);
            if (converted == source)
            {
                if (copied)
                    RestoreClipboard(saved);
                return;
            }

            try
            {
                Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, converted), true,
                    ClipboardRetries, ClipboardRetryDelayMs);
            }
            catch (ExternalException)
            {
                if (copied)
                    RestoreClipboard(saved);
                return; // nothing erased yet
            }

            if (selection.Length == 0)
            {
                var erase = new List<INPUT>();
                for (var i = 0; i < lastWord.Length; i++)
                {
                    erase.Add(Key(VK_BACK, up: false));
                    erase.Add(Key(VK_BACK, up: true));
                }
                Send(erase);
            }
            SendChord(VK_V); // replaces the selection, or inserts where the word was erased

            await Task.Delay(RestoreDelayMs);
            RestoreClipboard(saved);
            SwitchLayout(toCyrillic ? LangRussian : LangEnglishUs);
        }
        catch
        {
            // Failures never reach the user; Ctrl+C may have replaced the user's clipboard, so put it back.
            try { if (saved is not null) RestoreClipboard(saved); }
            catch { /* keep whatever is there */ }
        }
    }

    /// <summary>Console windows treat Ctrl+C as an interrupt, so no copy is attempted there.</summary>
    public static bool IsConsoleWindow(string windowClass) =>
        windowClass is "ConsoleWindowClass" or "CASCADIA_HOSTING_WINDOW_CLASS";

    /// <summary>
    /// Whether the text Ctrl+C put on the clipboard is a real selection. Code editors copy the whole line
    /// (ending with a line break) when nothing is selected — that is not a selection.
    /// </summary>
    public static bool IsSelection(bool clipboardChanged, string copiedText) =>
        clipboardChanged && copiedText.Length > 0 && !copiedText.EndsWith('\n');

    private static string GetForegroundWindowClass()
    {
        var name = new StringBuilder(256);
        return GetClassName(GetForegroundWindow(), name, name.Capacity) > 0 ? name.ToString() : "";
    }

    private static void SwitchLayout(int languageId)
    {
        var count = GetKeyboardLayoutList(0, null);
        if (count <= 0)
            return;
        var layouts = new IntPtr[count];
        count = GetKeyboardLayoutList(count, layouts);
        for (var i = 0; i < count; i++)
        {
            if (((long)layouts[i] & 0xFFFF) == languageId)
            {
                PostMessage(GetForegroundWindow(), WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, layouts[i]);
                return;
            }
        }
        // Layout not installed: leave the current one.
    }

    /// <summary>
    /// Releases modifiers the user still holds (Shift of the last trigger char, AltGr, the hotkey's Ctrl+Alt),
    /// so Backspace and Ctrl+C/V are not turned into other shortcuts.
    /// </summary>
    private static void AddModifierReleases(List<INPUT> inputs)
    {
        var held = Modifiers.Where(vk => (GetAsyncKeyState(vk) & 0x8000) != 0).ToList();
        if (held.Any(vk => vk is 0xA4 or 0xA5 or 0x5B or 0x5C))
        {
            inputs.Add(Key(VK_MENU_MASK, up: false));
            inputs.Add(Key(VK_MENU_MASK, up: true));
        }
        foreach (var vk in held)
            inputs.Add(Key(vk, up: true));
    }

    private static void SendChord(ushort vk) => Send(new List<INPUT>
    {
        Key(VK_CONTROL, up: false), Key(vk, up: false),
        Key(vk, up: true), Key(VK_CONTROL, up: true),
    });

    /// <summary>Copies every readable format of the current clipboard into a detached DataObject; null if it cannot be read.</summary>
    private static DataObject? SaveClipboard()
    {
        try
        {
            var current = Clipboard.GetDataObject();
            var copy = new DataObject();
            if (current is null)
                return copy;
            foreach (var format in current.GetFormats(autoConvert: false))
            {
                try
                {
                    var data = current.GetData(format, autoConvert: false);
                    if (data is not null)
                        copy.SetData(format, autoConvert: false, data);
                }
                catch
                {
                    // Some formats cannot be read back (private or delayed-rendered); skip them.
                }
            }
            return copy;
        }
        catch (ExternalException)
        {
            return null;
        }
    }

    private static void RestoreClipboard(DataObject saved)
    {
        try
        {
            if (saved.GetFormats(autoConvert: false).Length == 0)
                Clipboard.Clear();
            else
                Clipboard.SetDataObject(saved, true, ClipboardRetries, ClipboardRetryDelayMs);
        }
        catch (ExternalException)
        {
            // Could not restore: the clipboard keeps the template.
        }
    }

    private static INPUT Key(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                dwFlags = up ? KEYEVENTF_KEYUP : 0,
                dwExtraInfo = InputMonitor.SyntheticMarker,
            },
        },
    };

    private static void Send(List<INPUT> inputs)
    {
        if (inputs.Count == 0)
            return;
        var array = inputs.ToArray();
        SendInput((uint)array.Length, array, Marshal.SizeOf<INPUT>());
    }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    // The union must include MOUSEINPUT so INPUT has the size SendInput expects.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public nuint dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    private static extern int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[]? lpList);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
