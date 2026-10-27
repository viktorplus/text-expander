using TextExpander;

namespace TextExpander.Tests;

public class PasterSelectionTests
{
    [Theory]
    [InlineData("ConsoleWindowClass")]
    [InlineData("CASCADIA_HOSTING_WINDOW_CLASS")]
    public void Console_windows_are_recognized(string windowClass)
    {
        Assert.True(Paster.IsConsoleWindow(windowClass));
    }

    [Theory]
    [InlineData("Notepad")]
    [InlineData("Chrome_WidgetWin_1")]
    [InlineData("")]
    public void Other_windows_are_not_consoles(string windowClass)
    {
        Assert.False(Paster.IsConsoleWindow(windowClass));
    }

    [Theory]
    [InlineData("var x = 1;\r\n")]
    [InlineData("var x = 1;\n")]
    public void Copied_line_ending_with_line_break_is_not_a_selection(string copied)
    {
        Assert.False(Paster.IsSelection(clipboardChanged: true, copied));
    }

    [Fact]
    public void Copied_text_is_a_selection()
    {
        Assert.True(Paster.IsSelection(clipboardChanged: true, "ghbdtn"));
    }

    [Theory]
    [InlineData(false, "ghbdtn")]
    [InlineData(true, "")]
    public void Unchanged_or_empty_clipboard_is_not_a_selection(bool changed, string copied)
    {
        Assert.False(Paster.IsSelection(changed, copied));
    }
}
