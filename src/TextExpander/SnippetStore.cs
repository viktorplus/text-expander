using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TextExpander;

public sealed record Snippet(string Trigger, string Text);

public sealed class SnippetFormatException : Exception
{
    public SnippetFormatException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Owns the snippets.json file format: load, save, defaults, validation.</summary>
public static class SnippetStore
{
    private sealed class FileDto
    {
        [JsonPropertyName("snippets")] public List<SnippetDto>? Snippets { get; set; }
    }

    private sealed class SnippetDto
    {
        [JsonPropertyName("trigger")] public string? Trigger { get; set; }
        [JsonPropertyName("text")] public string? Text { get; set; }
    }

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly Snippet[] Defaults =
    {
        new("%%34code",
            "public class MyClass\n" +
            "{\n" +
            "    public MyClass()\n" +
            "    {\n" +
            "    }\n" +
            "\n" +
            "    public void Run()\n" +
            "    {\n" +
            "    }\n" +
            "}"),
        new("%%hello", "Здравствуйте! Спасибо за письмо, отвечу в ближайшее время."),
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TextExpander", "snippets.json");

    public static IReadOnlyList<Snippet> Load(string path)
    {
        var json = File.ReadAllText(path, Encoding.UTF8);

        FileDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<FileDto>(json, ReadOptions);
        }
        catch (JsonException ex)
        {
            var line = (ex.LineNumber ?? 0) + 1;
            var position = (ex.BytePositionInLine ?? 0) + 1;
            throw new SnippetFormatException($"строка {line}, позиция {position}: неверный JSON", ex);
        }

        if (dto?.Snippets is null)
            throw new SnippetFormatException("нет списка \"snippets\"");

        var snippets = new List<Snippet>(dto.Snippets.Count);
        for (var i = 0; i < dto.Snippets.Count; i++)
        {
            var item = dto.Snippets[i];
            if (item is null || item.Trigger is null || item.Text is null)
                throw new SnippetFormatException($"шаблон №{i + 1}: нужны поля \"trigger\" и \"text\"");
            snippets.Add(new Snippet(item.Trigger, item.Text));
        }

        var reason = Validate(snippets);
        if (reason is not null)
            throw new SnippetFormatException(reason);

        return snippets;
    }

    public static void Save(string path, IReadOnlyList<Snippet> snippets)
    {
        var reason = Validate(snippets);
        if (reason is not null)
            throw new SnippetFormatException(reason);

        var dto = new FileDto
        {
            Snippets = snippets.Select(s => new SnippetDto { Trigger = s.Trigger, Text = s.Text }).ToList(),
        };
        var json = JsonSerializer.Serialize(dto, WriteOptions);

        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        // Write to a temp file and move it in, so a file watcher never sees a half-written file.
        var tempPath = fullPath + ".tmp";
        File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(tempPath, fullPath, overwrite: true);
    }

    public static void EnsureExists(string path)
    {
        if (!File.Exists(path))
            Save(path, Defaults);
    }

    public static string? Validate(IReadOnlyList<Snippet> snippets)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < snippets.Count; i++)
        {
            var trigger = snippets[i].Trigger;
            if (string.IsNullOrEmpty(trigger))
                return $"Шаблон №{i + 1}: пустое кодовое слово";
            if (trigger.Any(char.IsWhiteSpace))
                return $"Кодовое слово «{trigger}» содержит пробел или перевод строки";
            if (!seen.Add(trigger))
                return $"Кодовое слово «{trigger}» встречается дважды";
        }
        return null;
    }

    public static string? FindPrefixConflict(IReadOnlyList<Snippet> snippets)
    {
        foreach (var shorter in snippets)
        {
            foreach (var longer in snippets)
            {
                if (shorter.Trigger.Length > 0
                    && longer.Trigger.Length > shorter.Trigger.Length
                    && longer.Trigger.StartsWith(shorter.Trigger, StringComparison.Ordinal))
                {
                    return $"«{shorter.Trigger}» — начало «{longer.Trigger}», длинное никогда не сработает";
                }
            }
        }
        return null;
    }
}
