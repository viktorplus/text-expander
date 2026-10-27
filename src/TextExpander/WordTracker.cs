using System.Text;

namespace TextExpander;

/// <summary>The word being typed (chars since the last whitespace or reset). No WinAPI; hook thread only.</summary>
public sealed class WordTracker
{
    private const int MaxLength = 200;
    private readonly StringBuilder _word = new();

    public string LastWord => _word.ToString();

    public void OnChar(char c)
    {
        if (char.IsWhiteSpace(c))
        {
            _word.Clear();
            return;
        }
        _word.Append(c);
        if (_word.Length > MaxLength)
            _word.Remove(0, _word.Length - MaxLength);
    }

    public void OnBackspace()
    {
        if (_word.Length > 0)
            _word.Length--;
    }

    public void Reset() => _word.Clear();

    public void Clear() => _word.Clear();
}
