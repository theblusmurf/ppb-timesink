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
        bool gateCheck=args.Length==1 && args[0]=="--patch-startup-check";
        Mutex instance; bool created;
        // Startup and patch installation share a gate. Only the running GUI owns
        // the installer AppMutex; the patch worker must not create that mutex.
        using (var gate = new Mutex(true, AutoPatcher.GateName, out bool gateCreated))
        {
            if (!gateCreated) return gateCheck ? 23 : 0;
            try { instance = new Mutex(true, "Local\\PoteHunter.SingleInstance", out created); }
            finally { gate.ReleaseMutex(); }
        }
        using var mutex = instance;
        if (!created) return gateCheck ? 24 : 0;
        if (gateCheck) return 0; // No GUI, client access or input in packaging checks.
        Startup("Initializing Windows Forms");
        ApplicationConfiguration.Initialize();
        Startup("Constructing hunter window");
        Application.ThreadException += (_, e) => { Input.Release(); MessageBox.Show(e.Exception.Message, "POTE Hunter"); };
        var form = new HunterForm();form.ConfigureUpdates(); Startup("Running hunter window");
        Application.Run(form); return 0;
    }
}
