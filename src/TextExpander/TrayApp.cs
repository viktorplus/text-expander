using System.ComponentModel;
using System.Diagnostics;

namespace TextExpander;

/// <summary>Tray application: wires InputMonitor → TriggerMatcher / WordTracker → Paster, watches snippets.json.</summary>
public sealed class TrayApp : ApplicationContext
{
    private const string AppTitle = "Text Expander";

    // NotifyIcon.Text is limited to 127 characters.
    private const string TrayTooltip = AppTitle + "\nPause или Ctrl+Alt+Space — исправить раскладку";

    internal const string HotkeysHelp =
        "Шаблоны\n" +
        "Наберите кодовое слово (например, %%34code) — оно сразу заменится текстом шаблона.\n\n" +
        "Исправление раскладки\n" +
        "Pause — исправить выделенный текст или последнее набранное слово (ghbdtn → привет).\n" +
        "Ctrl+Alt+Space — то же самое, если на клавиатуре нет Pause.\n\n" +
        "Пункт «Включено» в меню отключает и шаблоны, и исправление раскладки.\n\n" +
        "github.com/viktorplus";

    internal static readonly Icon AppIcon =
        new(typeof(TrayApp).Assembly.GetManifestResourceStream("TextExpander.app.ico")!);

    private const int ReloadDebounceMs = 200;
    private const int MaxReloadRetries = 5;

    private readonly string _path;
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _enabledItem;
    private readonly ToolStripMenuItem _autostartItem;
    private readonly SynchronizationContext _ui;
    private readonly TriggerMatcher _matcher = new();
    private readonly WordTracker _tracker = new();
    private readonly InputMonitor _monitor = new();
    private readonly FileSystemWatcher? _watcher;
    private readonly System.Windows.Forms.Timer _reloadTimer = new() { Interval = ReloadDebounceMs };
    private int _reloadRetries;
    private bool _pasting;
    private SnippetsForm? _snippetsForm;

    public TrayApp(string path)
    {
        _path = Path.GetFullPath(path);

        _enabledItem = new ToolStripMenuItem("Включено") { Checked = true, CheckOnClick = true };
        _enabledItem.CheckedChanged += (_, _) =>
        {
            _matcher.Reset();
            _tracker.Reset();
            _monitor.ConvertLayoutEnabled = _enabledItem.Checked;
        };
        _autostartItem = new ToolStripMenuItem("Автозапуск с Windows", null, (_, _) => ToggleAutostart());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_enabledItem);
        menu.Items.Add(new ToolStripMenuItem("Шаблоны…", null, (_, _) => ShowSnippetsWindow()));
        menu.Items.Add(new ToolStripMenuItem("Открыть файл шаблонов", null, (_, _) => OpenSnippetsFile()));
        menu.Items.Add(_autostartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Горячие клавиши…", null, (_, _) =>
            MessageBox.Show(HotkeysHelp, AppTitle + " — горячие клавиши", MessageBoxButtons.OK, MessageBoxIcon.Information)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Выход", null, (_, _) => ExitThread()));
        menu.Opening += (_, _) => _autostartItem.Checked = SafeIsAutostartEnabled();

        // Creating the menu (a Control) installs the WinForms synchronization context on this thread.
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        _icon = new NotifyIcon
        {
            Icon = AppIcon,
            Text = TrayTooltip,
            ContextMenuStrip = menu,
            Visible = true,
        };

        _reloadTimer.Tick += (_, _) => { _reloadTimer.Stop(); Reload(); };

        try
        {
            SnippetStore.EnsureExists(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SnippetFormatException)
        {
            ShowError(ex.Message);
        }
        Reload();

        try
        {
            _watcher = new FileSystemWatcher(Path.GetDirectoryName(_path)!, Path.GetFileName(_path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
            };
            _watcher.Changed += OnFileEvent;
            _watcher.Created += OnFileEvent;
            _watcher.Renamed += OnFileEvent;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            _watcher = null; // folder missing: hand edits are not picked up until restart
        }

        _monitor.CharTyped += OnCharTyped;
        _monitor.BackspacePressed += () => { _matcher.OnBackspace(); _tracker.OnBackspace(); };
        _monitor.ResetRequested += () => { _matcher.Reset(); _tracker.Reset(); };
        _monitor.ConvertLayoutRequested += OnConvertLayoutRequested;
        _monitor.Start();
    }

    private void OnCharTyped(char c)
    {
        // Runs inside the keyboard hook: must return fast, so never paste here.
        if (!_enabledItem.Checked || _pasting)
            return;
        _tracker.OnChar(c);
        var snippet = _matcher.OnChar(c);
        if (snippet is null)
            return;
        _pasting = true;
        // Posted work runs after the hook returns, so the trigger's last key reaches the window first.
        _ui.Post(async _ =>
        {
            try { await Paster.ReplaceAsync(snippet.Trigger.Length, snippet.Text); }
            finally { _pasting = false; _matcher.Reset(); _tracker.Clear(); }
        }, null);
    }

    private void OnConvertLayoutRequested()
    {
        // Runs inside the keyboard hook: only post the work.
        if (!_enabledItem.Checked || _pasting)
            return;
        var word = _tracker.LastWord;
        _pasting = true;
        _ui.Post(async _ =>
        {
            try { await Paster.ConvertSelectionOrWordAsync(word); }
            finally { _pasting = false; _matcher.Reset(); _tracker.Clear(); }
        }, null);
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        // Watcher events come on a thread-pool thread; debounce on the UI thread.
        _ui.Post(_ => { _reloadRetries = 0; _reloadTimer.Stop(); _reloadTimer.Start(); }, null);
    }

    private void Reload()
    {
        try
        {
            var snippets = SnippetStore.Load(_path);
            _matcher.SetSnippets(snippets);
            _reloadRetries = 0;
        }
        catch (SnippetFormatException ex)
        {
            ShowError(ex.Message); // previous snippets keep working
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The editor may still hold the file: retry a few times before reporting.
            if (++_reloadRetries <= MaxReloadRetries)
                _reloadTimer.Start();
            else
                ShowError(ex.Message);
        }
    }

    private void ShowError(string message) =>
        _icon.ShowBalloonTip(5000, AppTitle, "Ошибка в snippets.json: " + message, ToolTipIcon.Warning);

    private void ShowSnippetsWindow()
    {
        if (_snippetsForm is { IsDisposed: false })
        {
            if (_snippetsForm.WindowState == FormWindowState.Minimized)
                _snippetsForm.WindowState = FormWindowState.Normal;
            _snippetsForm.Activate();
            return;
        }
        IReadOnlyList<Snippet> snippets;
        try
        {
            snippets = SnippetStore.Load(_path);
        }
        catch (Exception ex) when (ex is SnippetFormatException or IOException or UnauthorizedAccessException)
        {
            // Opening an empty editor over a broken file would overwrite it on save.
            MessageBox.Show("Не удалось прочитать snippets.json: " + ex.Message, AppTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        _snippetsForm = new SnippetsForm(_path, snippets, Reload);
        _snippetsForm.Show();
        _snippetsForm.Activate();
    }

    private void OpenSnippetsFile()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_path) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            // No program is associated with .json: fall back to Notepad.
            try { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{_path}\"") { UseShellExecute = true }); }
            catch (Win32Exception ex) { _icon.ShowBalloonTip(5000, AppTitle, ex.Message, ToolTipIcon.Error); }
        }
    }

    private void ToggleAutostart()
    {
        try
        {
            Autostart.Set(!Autostart.IsEnabled());
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            _icon.ShowBalloonTip(5000, AppTitle, "Не удалось изменить автозапуск: " + ex.Message, ToolTipIcon.Error);
        }
        _autostartItem.Checked = SafeIsAutostartEnabled();
    }

    private static bool SafeIsAutostartEnabled()
    {
        try { return Autostart.IsEnabled(); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException) { return false; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _monitor.Dispose();
            _watcher?.Dispose();
            _reloadTimer.Dispose();
            _icon.Visible = false;
            _icon.ContextMenuStrip?.Dispose();
            _icon.Dispose();
        }
        base.Dispose(disposing);
    }
}
