namespace TextExpander.Tests;

public class InputMonitorClassifyTests
{
    // Virtual-key codes from the Win32 documentation (WinUser.h).
    private const int VkBack = 0x08, VkTab = 0x09, VkReturn = 0x0D, VkEscape = 0x1B;
    private const int VkCapital = 0x14, VkHome = 0x24, VkLeft = 0x25, VkEnd = 0x23, VkDelete = 0x2E;
    private const int VkLShift = 0xA0, VkRShift = 0xA1, VkLControl = 0xA2, VkRMenu = 0xA5, VkLWin = 0x5B;
    private const int VkShift = 0x10, VkA = 0x41, VkDigit3 = 0x33, VkOem5 = 0xDC;

    [Theory]
    [InlineData(VkLShift, false, false, false)]
    [InlineData(VkRShift, false, false, false)]
    [InlineData(VkShift, false, false, false)]
    [InlineData(VkLControl, true, false, false)]
    [InlineData(VkRMenu, true, true, false)]
    [InlineData(VkLWin, false, false, true)]
    [InlineData(VkCapital, false, false, false)]
    public void PureModifierPress_IsIgnored_NotReset(int vk, bool ctrl, bool alt, bool win)
    {
        Assert.Equal(KeyKind.Ignore, InputMonitor.Classify(vk, ctrl, alt, win));
    }

    [Theory]
    [InlineData(VkA)]
    [InlineData(VkDigit3)]
    [InlineData(VkOem5)]
    public void PlainKey_IsChar(int vk)
    {
        Assert.Equal(KeyKind.Char, InputMonitor.Classify(vk, false, false, false));
    }

    [Fact]
    public void AltGr_Key_IsChar()
    {
        // AltGr is reported by Windows as Ctrl+Alt.
        Assert.Equal(KeyKind.Char, InputMonitor.Classify(VkA, ctrl: true, alt: true, win: false));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Chord_WithCtrlAltOrWin_IsReset(bool ctrl, bool alt, bool win)
    {
        Assert.Equal(KeyKind.Reset, InputMonitor.Classify(VkA, ctrl, alt, win));
    }

    [Fact]
    public void Backspace_IsBackspace()
    {
        Assert.Equal(KeyKind.Backspace, InputMonitor.Classify(VkBack, false, false, false));
    }

    [Fact]
    public void CtrlBackspace_IsReset()
    {
        Assert.Equal(KeyKind.Reset, InputMonitor.Classify(VkBack, ctrl: true, alt: false, win: false));
    }

    [Theory]
    [InlineData(VkReturn)]
    [InlineData(VkTab)]
    [InlineData(VkEscape)]
    [InlineData(VkLeft)]
    [InlineData(VkHome)]
    [InlineData(VkEnd)]
    [InlineData(VkDelete)]
    public void NavigationAndControlKeys_AreReset(int vk)
    {
        Assert.Equal(KeyKind.Reset, InputMonitor.Classify(vk, false, false, false));
    }
}
