using System.Diagnostics;

namespace TextExpander;

/// <summary>Window for editing snippets: list on the left, trigger + text on the right.</summary>
public sealed class SnippetsForm : Form
{
    private sealed class Entry
    {
        public string Trigger = "";
        public string Text = "";
        public override string ToString() => Trigger.Length == 0 ? "(без кодового слова)" : Trigger;
    }

    private readonly string _path;
    private readonly Action _onSaved;
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly TextBox _trigger = new() { Dock = DockStyle.Top };
    private readonly TextBox _text = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        AcceptsTab = true,
        AcceptsReturn = true,
        WordWrap = false,
        ScrollBars = ScrollBars.Both,
        Font = new Font("Consolas", 10f),
    };
    private readonly Button _add = new() { Text = "Добавить", AutoSize = true };
    private readonly Button _delete = new() { Text = "Удалить", AutoSize = true };
    private readonly Button _save = new() { Text = "Сохранить", AutoSize = true };
    private bool _loading;
    private bool _dirty;

    public SnippetsForm(string path, IReadOnlyList<Snippet> snippets, Action onSaved)
    {
        _path = path;
        _onSaved = onSaved;

        Text = "Шаблоны — Text Expander";
        Icon = TrayApp.AppIcon;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(820, 480);
        MinimumSize = new Size(600, 320);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
        buttons.Controls.AddRange(new Control[] { _add, _delete, _save });

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 220 };
        split.Panel1.Controls.Add(_list);
        split.Panel1.Controls.Add(new Label { Text = "Кодовые слова", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 4) });

        var textLabel = new Label { Text = "Текст шаблона", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 6, 0, 4) };
        split.Panel2.Controls.Add(_text);
        split.Panel2.Controls.Add(textLabel);
        split.Panel2.Controls.Add(_trigger);
        split.Panel2.Controls.Add(new Label { Text = "Кодовое слово", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 4) });

        var hint = new Label
        {
            Text = "Кодовое слово срабатывает сразу после набора.  Pause или Ctrl+Alt+Space — исправить раскладку выделенного текста или последнего слова.",
            Dock = DockStyle.Bottom,
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Padding = new Padding(6, 0, 6, 6),
        };

        var link = new LinkLabel
        {
            Text = "github.com/viktorplus",
            Dock = DockStyle.Bottom,
            Height = 18,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font(Font.FontFamily, 7.5f),
            LinkColor = SystemColors.GrayText,
            ActiveLinkColor = SystemColors.GrayText,
            LinkBehavior = LinkBehavior.HoverUnderline,
            Padding = new Padding(0, 0, 6, 2),
        };
        link.LinkClicked += (_, _) =>
            Process.Start(new ProcessStartInfo("https://github.com/viktorplus") { UseShellExecute = true });

        Controls.Add(split);
        Controls.Add(buttons);
        Controls.Add(hint);
        Controls.Add(link);

        foreach (var s in snippets)
            _list.Items.Add(new Entry { Trigger = s.Trigger, Text = s.Text });

        _list.SelectedIndexChanged += (_, _) => ShowSelected();
        _trigger.TextChanged += (_, _) => OnEdited();
        _text.TextChanged += (_, _) => OnEdited();
        _add.Click += (_, _) => AddEntry();
        _delete.Click += (_, _) => DeleteEntry();
        _save.Click += (_, _) => TrySave();
        FormClosing += OnFormClosing;

        if (_list.Items.Count > 0)
            _list.SelectedIndex = 0;
        else
            ShowSelected();
    }

    private Entry? Selected => _list.SelectedItem as Entry;

    private void ShowSelected()
    {
        _loading = true;
        var entry = Selected;
        _trigger.Enabled = _text.Enabled = _delete.Enabled = entry is not null;
        _trigger.Text = entry?.Trigger ?? "";
        _text.Text = entry?.Text ?? "";
        _loading = false;
    }

    // Edits go straight into the selected entry, so switching rows never loses them.
    private void OnEdited()
    {
        if (_loading || Selected is not { } entry)
            return;
        entry.Trigger = _trigger.Text;
        entry.Text = _text.Text;
        _dirty = true;
        var index = _list.SelectedIndex;
        _loading = true;
        _list.Items[index] = entry; // refreshes the row caption
        _loading = false;
    }

    private void AddEntry()
    {
        var index = _list.Items.Add(new Entry());
        _list.SelectedIndex = index;
        _dirty = true;
        _trigger.Focus();
    }

    private void DeleteEntry()
    {
        var index = _list.SelectedIndex;
        if (index < 0)
            return;
        _list.Items.RemoveAt(index);
        _dirty = true;
        if (_list.Items.Count > 0)
            _list.SelectedIndex = Math.Min(index, _list.Items.Count - 1);
        else
            ShowSelected();
    }

    private List<Snippet> CurrentSnippets() =>
        _list.Items.Cast<Entry>().Select(e => new Snippet(e.Trigger, e.Text)).ToList();

    /// <summary>Validates and saves. Returns false (file untouched) when the list is invalid or writing fails.</summary>
    private bool TrySave()
    {
        var snippets = CurrentSnippets();
        var error = SnippetStore.Validate(snippets);
        if (error is not null)
        {
            MessageBox.Show(this, error, "Нельзя сохранить", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        var warning = SnippetStore.FindPrefixConflict(snippets);
        try
        {
            SnippetStore.Save(_path, snippets);
        }
        catch (Exception ex) when (ex is SnippetFormatException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Не удалось сохранить", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        _dirty = false;
        _onSaved();
        if (warning is not null)
            MessageBox.Show(this, warning, "Предупреждение", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return true;
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_dirty)
            return;
        var answer = MessageBox.Show(this, "Сохранить изменения?", "Шаблоны",
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel || (answer == DialogResult.Yes && !TrySave()))
            e.Cancel = true;
    }
}
