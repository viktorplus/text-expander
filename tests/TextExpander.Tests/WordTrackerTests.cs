using TextExpander;

namespace TextExpander.Tests;

public class WordTrackerTests
{
    private static WordTracker Typed(string input)
    {
        var tracker = new WordTracker();
        foreach (var c in input)
            tracker.OnChar(c);
        return tracker;
    }

    [Fact]
    public void Collects_typed_chars()
    {
        Assert.Equal("ghbdtn", Typed("ghbdtn").LastWord);
    }

    [Theory]
    [InlineData("hello ghbdtn", "ghbdtn")]
    [InlineData("hello\tghbdtn", "ghbdtn")]
    [InlineData("hello ", "")]
    public void Whitespace_starts_a_new_word(string input, string expected)
    {
        Assert.Equal(expected, Typed(input).LastWord);
    }

    [Fact]
    public void Punctuation_is_part_of_the_word()
    {
        Assert.Equal(",f,f", Typed("a ,f,f").LastWord);
    }

    [Fact]
    public void Backspace_drops_the_last_char()
    {
        var tracker = Typed("ghbdtnn");
        tracker.OnBackspace();
        Assert.Equal("ghbdtn", tracker.LastWord);
    }

    [Fact]
    public void Backspace_on_empty_word_does_nothing()
    {
        var tracker = new WordTracker();
        tracker.OnBackspace();
        Assert.Equal("", tracker.LastWord);
    }

    [Fact]
    public void Reset_and_Clear_empty_the_word()
    {
        var reset = Typed("ghbdtn");
        reset.Reset();
        Assert.Equal("", reset.LastWord);

        var cleared = Typed("ghbdtn");
        cleared.Clear();
        Assert.Equal("", cleared.LastWord);
        cleared.OnChar('a');
        Assert.Equal("a", cleared.LastWord);
    }

    [Fact]
    public void Word_keeps_the_last_200_chars()
    {
        var input = new string('a', 50) + new string('b', 200);
        Assert.Equal(new string('b', 200), Typed(input).LastWord);
    }
}
