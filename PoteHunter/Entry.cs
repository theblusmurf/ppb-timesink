namespace PoteHunter;

static class Entry
{
    internal static bool NativeInputForGui(string[] args)=>args.Length==0 ||
        args.Length==2 && args.Contains("--native-read-compat") && args.Contains("--native-input-compat");
    [STAThread]
    static int Main(string[] args)
    {
        // The copied patch worker must run before game access or the GUI mutex.
        if (args.Length > 0 && args[0] == "--apply-patch")
            return args.Length == 2 ? AutoPatcher.Run(args[1]).GetAwaiter().GetResult() : 2;
        if (args.Length > 0 && args[0] == "--patch-install-check")
            return AutoPatcherTestSupport.InstallCheck(args);
        if (CommandLine.Run(args) is int exitCode) return exitCode;

        // Normal executable launches use the same verified compatibility path
        // as the packaged CMD/PowerShell launchers.
        // Diagnostic modes above keep their existing bounded-test opt-in rules.
        bool inputCompatibility = NativeInputForGui(args);
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
        var form = new HunterForm();form.ConfigureUpdates(); Startup("Running hunter window");
        Application.Run(form); return 0;
    }
}
