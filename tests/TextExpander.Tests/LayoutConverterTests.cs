using TextExpander;

namespace TextExpander.Tests;

public class LayoutConverterTests
{
    [Theory]
    [InlineData("ghbdtn", "привет")]
    [InlineData("Ghbdtn", "Привет")]
    [InlineData(";tkfnm", "желать")]
    [InlineData(",f,f", "баба")]
    [InlineData("ntcn.", "тест.")] // '.' ending a word after a letter is sentence punctuation, not 'ю'
    [InlineData("ghbdtn, vbh", "привет, мир")] // same for ',' before whitespace
    [InlineData("k.,k.nt", "люблюте")] // '.' and ',' inside a word are the keys of 'ю' and 'б'
    [InlineData("f`[]';", "аёхъэж")]
    [InlineData("f~{}:\"<>", "аЁХЪЖЭБЮ")]
    [InlineData("ntcn/", "тест.")] // '/' is the key of the Russian '.'
    [InlineData("xnj&", "что?")]
    [InlineData("f@#$^|", "а\"№;:/")]
    public void Latin_typed_on_russian_keys_becomes_cyrillic(string input, string expected)
    {
        Assert.Equal(expected, LayoutConverter.Convert(input, out var toCyrillic));
        Assert.True(toCyrillic);
    }

    [Theory]
    [InlineData("руддщ", "hello")]
    [InlineData("руддщ цщкдв", "hello world")]
    [InlineData("Руддщ", "Hello")]
    [InlineData("ёхъжэбю", "`[];',.")]
    [InlineData("ЁХЪЖЭБЮ", "~{}:\"<>")]
    [InlineData("руддщ.", "hello.")] // to Latin only letters are converted, punctuation stays
    [InlineData("руддщ, 123!", "hello, 123!")]
    public void Cyrillic_typed_on_english_keys_becomes_latin(string input, string expected)
    {
        Assert.Equal(expected, LayoutConverter.Convert(input, out var toCyrillic));
        Assert.False(toCyrillic);
    }

    [Fact]
    public void Mixed_text_follows_the_prevailing_script()
    {
        Assert.Equal("hello d", LayoutConverter.Convert("руддщ d", out var toCyrillic));
        Assert.False(toCyrillic);
        Assert.Equal("при в", LayoutConverter.Convert("ghb в", out toCyrillic));
        Assert.True(toCyrillic);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123 !?")]
    [InlineData(".,;")]
    public void Text_without_letters_is_unchanged(string input)
    {
        Assert.Equal(input, LayoutConverter.Convert(input, out _));
    }

    [Fact]
    public void Line_breaks_and_digits_are_kept()
    {
        Assert.Equal("при\r\nвет 42", LayoutConverter.Convert("ghb\r\ndtn 42", out _));
    }
}
