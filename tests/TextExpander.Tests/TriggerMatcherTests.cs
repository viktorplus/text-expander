using TextExpander;

namespace TextExpander.Tests;

public class TriggerMatcherTests
{
    private static readonly Snippet Code = new("%%34code", "class Foo {}");

    private static Snippet? Type(TriggerMatcher matcher, string input)
    {
        Snippet? last = null;
        foreach (var c in input)
            last = matcher.OnChar(c);
        return last;
    }

    private static TriggerMatcher Create(params Snippet[] snippets)
    {
        var matcher = new TriggerMatcher();
        matcher.SetSnippets(snippets);
        return matcher;
    }

    [Fact]
    public void Trigger_fires_on_last_char_only()
    {
        var matcher = Create(Code);
        foreach (var c in "%%34cod")
            Assert.Null(matcher.OnChar(c));
        Assert.Equal(Code, matcher.OnChar('e'));
    }

    [Fact]
    public void Trigger_after_other_text_fires()
    {
        Assert.Equal(Code, Type(Create(Code), "x%%34code"));
    }

    [Fact]
    public void Incomplete_trigger_does_not_fire()
    {
        Assert.Null(Type(Create(Code), "%%34cod"));
    }

    [Fact]
    public void Tail_is_cleared_after_match()
    {
        var short1 = new Snippet("ab", "1");
        var matcher = Create(short1);
        Assert.Equal(short1, Type(matcher, "ab"));
        // If the tail were kept, "b" would complete "ab" again.
        Assert.Null(matcher.OnChar('b'));
    }

    [Fact]
    public void Longest_trigger_wins()
    {
        var shortOne = new Snippet("code", "short");
        var longOne = new Snippet("%%code", "long");
        Assert.Equal(longOne, Type(Create(shortOne, longOne), "%%code"));
        Assert.Equal(longOne, Type(Create(longOne, shortOne), "%%code"));
    }

    [Fact]
    public void Backspace_drops_last_char()
    {
        var matcher = Create(Code);
        Assert.Null(Type(matcher, "%%34codf"));
        matcher.OnBackspace();
        Assert.Equal(Code, matcher.OnChar('e'));
    }

    [Fact]
    public void Backspace_on_empty_tail_is_harmless()
    {
        var matcher = Create(Code);
        matcher.OnBackspace();
        Assert.Equal(Code, Type(matcher, "%%34code"));
    }

    [Fact]
    public void Reset_clears_tail()
    {
        var matcher = Create(Code);
        Type(matcher, "%%34cod");
        matcher.Reset();
        Assert.Null(matcher.OnChar('e'));
    }

    [Fact]
    public void Matching_is_case_sensitive()
    {
        Assert.Null(Type(Create(Code), "%%34CODE"));
    }

    [Fact]
    public void Cyrillic_trigger_fires()
    {
        var sig = new Snippet("%%подпись", "С уважением");
        Assert.Equal(sig, Type(Create(sig), "текст %%подпись"));
    }

    [Fact]
    public void Long_input_before_trigger_still_matches()
    {
        Assert.Equal(Code, Type(Create(Code), new string('z', 500) + "%%34code"));
    }

    [Fact]
    public void SetSnippets_replaces_previous_set()
    {
        var matcher = Create(Code);
        var hello = new Snippet("%%hello", "Привет");
        matcher.SetSnippets(new[] { hello });
        Assert.Null(Type(matcher, "%%34code"));
        Assert.Equal(hello, Type(matcher, "%%hello"));
    }
}
