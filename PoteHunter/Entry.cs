namespace PoteHunter;

static class Entry
{
    [STAThread]
    static int Main(string[] args)
    {
        if (CommandLine.Run(args) is int exitCode) return exitCode;

        // A separate, user-started GUI launch opts into the same verified reader.
        // Diagnostic modes above keep their existing bounded-test opt-in rules.
        bool inputCompatibility = args.Length == 2 && args.Contains("--native-read-compat") && args.Contains("--native-input-compat");
        PoteMemoryProbe.WindowsClientRead.Enabled = (args.Length == 1 && args[0] == "--native-read-compat") || inputCompatibility;
        WindowsClientInput.Enabled = inputCompatibility;
        void Startup(string step) => File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "startup-trace.txt"), DateTime.UtcNow.ToString("O") + " " + step + Environment.NewLine);
        Startup("Acquiring instance mutex");
        using var mutex = new Mutex(true, "Local\\PoteHunter.SingleInstance", out bool created);
        if (!created) return 0;
        Startup("Initializing Windows Forms");
        ApplicationConfiguration.Initialize();
        Startup("Constructing hunter window");
        Application.ThreadException += (_, e) => { Input.Release(); MessageBox.Show(e.Exception.Message, "POTE Hunter"); };
        var form = new HunterForm(); Startup("Running hunter window");
        Application.Run(form); return 0;
    }
}
