using System.Text;

namespace TextExpander;

/// <summary>Tracks the typed tail and detects a trigger at its end. Not thread-safe.</summary>
public sealed class TriggerMatcher
{
    private readonly StringBuilder _tail = new();
    private Snippet[] _byLengthDesc = Array.Empty<Snippet>();
    private int _maxLength;

    public void SetSnippets(IEnumerable<Snippet> snippets)
    {
        _byLengthDesc = snippets
            .Where(s => s.Trigger.Length > 0)
            .OrderByDescending(s => s.Trigger.Length)
            .ToArray();
        _maxLength = _byLengthDesc.Length > 0 ? _byLengthDesc[0].Trigger.Length : 0;
        Reset();
    }

    public Snippet? OnChar(char c)
    {
        if (_maxLength == 0)
            return null;

        _tail.Append(c);
        if (_tail.Length > _maxLength)
            _tail.Remove(0, _tail.Length - _maxLength);

        var tail = _tail.ToString();
        foreach (var snippet in _byLengthDesc)
        {
            if (tail.EndsWith(snippet.Trigger, StringComparison.Ordinal))
            {
                _tail.Clear();
                return snippet;
            }
        }
        return null;
    }

    public void OnBackspace()
    {
        if (_tail.Length > 0)
            _tail.Length--;
    }

    public void Reset() => _tail.Clear();
}
