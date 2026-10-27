namespace TextExpander;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(initiallyOwned: true, @"Local\TextExpander.SingleInstance", out var createdNew);
        if (!createdNew)
            return; // another instance is already running

        ApplicationConfiguration.Initialize();
        using var app = new TrayApp(SnippetStore.DefaultPath);
        Application.Run(app);
    }
}
