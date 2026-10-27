using System.Text;
using TextExpander;

namespace TextExpander.Tests;

public sealed class SnippetStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "TextExpanderTests-" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_dir, "sub", "snippets.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private void WriteRaw(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, content, new UTF8Encoding(false));
    }

    [Fact]
    public void DefaultPath_is_under_appdata()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TextExpander", "snippets.json");
        Assert.Equal(expected, SnippetStore.DefaultPath);
    }

    [Fact]
    public void Cyrillic_and_multiline_survive_roundtrip_and_stay_readable()
    {
        var snippets = new[]
        {
            new Snippet("%%подпись", "С уважением,\r\nИван"),
            new Snippet("%%unix", "line1\nline2\n\ttabbed \"quoted\""),
        };

        SnippetStore.Save(FilePath, snippets);
        var loaded = SnippetStore.Load(FilePath);

        Assert.Equal(snippets, loaded);
        var bytes = File.ReadAllBytes(FilePath);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "BOM written");
        var raw = Encoding.UTF8.GetString(bytes);
        Assert.Contains("%%подпись", raw);
        Assert.Contains("С уважением", raw);
        Assert.DoesNotContain("\\u", raw);
    }

    [Fact]
    public void Load_reads_documented_format()
    {
        WriteRaw("{ \"snippets\": [ { \"trigger\": \"%%a\", \"text\": \"Привет\\nмир\" } ] }");
        var loaded = SnippetStore.Load(FilePath);
        Assert.Equal(new[] { new Snippet("%%a", "Привет\nмир") }, loaded);
    }

    [Fact]
    public void Broken_json_throws_russian_message_with_position()
    {
        WriteRaw("{\n  \"snippets\": [\n    { \"trigger\": \"%%a\", \"text\": \"x\" \n  ]\n}");
        var ex = Assert.Throws<SnippetFormatException>(() => SnippetStore.Load(FilePath));
        Assert.Contains("строка", ex.Message);
        Assert.Contains("позиция", ex.Message);
        Assert.Matches("строка \\d+", ex.Message);
    }

    [Fact]
    public void Invalid_content_on_load_throws_format_exception()
    {
        WriteRaw("{ \"snippets\": [ { \"trigger\": \"\", \"text\": \"x\" } ] }");
        var ex = Assert.Throws<SnippetFormatException>(() => SnippetStore.Load(FilePath));
        Assert.Contains("пуст", ex.Message);
    }

    [Fact]
    public void Missing_snippets_array_throws_format_exception()
    {
        WriteRaw("{ }");
        Assert.Throws<SnippetFormatException>(() => SnippetStore.Load(FilePath));
    }

    [Fact]
    public void Validate_accepts_valid_list()
    {
        Assert.Null(SnippetStore.Validate(new[] { new Snippet("%%a", "1"), new Snippet("%%b", "") }));
    }

    [Fact]
    public void Validate_rejects_empty_trigger()
    {
        var reason = SnippetStore.Validate(new[] { new Snippet("", "1") });
        Assert.NotNull(reason);
        Assert.Contains("пуст", reason);
    }

    [Fact]
    public void Validate_rejects_duplicate_trigger()
    {
        var reason = SnippetStore.Validate(new[] { new Snippet("%%a", "1"), new Snippet("%%a", "2") });
        Assert.NotNull(reason);
        Assert.Contains("%%a", reason);
    }

    [Theory]
    [InlineData("%% a")]
    [InlineData("%%a\t")]
    [InlineData("%%a\n")]
    public void Validate_rejects_whitespace_in_trigger(string trigger)
    {
        var reason = SnippetStore.Validate(new[] { new Snippet(trigger, "1") });
        Assert.NotNull(reason);
        Assert.Contains("пробел", reason);
    }

    [Fact]
    public void Save_with_invalid_list_throws_and_writes_nothing()
    {
        var ex = Assert.Throws<SnippetFormatException>(() =>
            SnippetStore.Save(FilePath, new[] { new Snippet("%%a", "1"), new Snippet("%%a", "2") }));
        Assert.Contains("%%a", ex.Message);
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void Save_with_invalid_list_keeps_existing_file()
    {
        WriteRaw("original");
        var ex = Assert.Throws<SnippetFormatException>(() => SnippetStore.Save(FilePath, new[] { new Snippet("", "1") }));
        Assert.Contains("пуст", ex.Message);
        Assert.Equal("original", File.ReadAllText(FilePath));
    }

    [Fact]
    public void FindPrefixConflict_finds_prefix_pair()
    {
        var warning = SnippetStore.FindPrefixConflict(new[] { new Snippet("%%ab", "2"), new Snippet("%%a", "1") });
        Assert.Equal("«%%a» — начало «%%ab», длинное никогда не сработает", warning);
    }

    [Fact]
    public void FindPrefixConflict_returns_null_without_prefix_pairs()
    {
        // Suffix overlap ("code" / "%%code") is resolved by longest-wins, not a prefix conflict.
        Assert.Null(SnippetStore.FindPrefixConflict(new[]
        {
            new Snippet("%%a", "1"), new Snippet("%%b", "2"), new Snippet("code", "3"), new Snippet("%%code", "4"),
        }));
    }

    [Fact]
    public void EnsureExists_creates_defaults()
    {
        SnippetStore.EnsureExists(FilePath);

        var loaded = SnippetStore.Load(FilePath);
        Assert.Equal(new[] { "%%34code", "%%hello" }, loaded.Select(s => s.Trigger));
        Assert.Contains("\n", loaded[0].Text);
        Assert.Contains("class", loaded[0].Text);
        Assert.NotEmpty(loaded[1].Text);
    }

    [Fact]
    public void EnsureExists_does_not_touch_existing_file()
    {
        WriteRaw("not even json");
        SnippetStore.EnsureExists(FilePath);
        Assert.Equal("not even json", File.ReadAllText(FilePath));
    }
}
