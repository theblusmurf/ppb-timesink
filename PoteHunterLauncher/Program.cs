internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var application=Path.Combine(AppContext.BaseDirectory,"PoteHunter.exe");
            if(!File.Exists(application)) throw new FileNotFoundException("Keep PoteHunter.exe beside this launcher.",application);
            var startInfo=new System.Diagnostics.ProcessStartInfo(application)
            {
                WorkingDirectory=AppContext.BaseDirectory,
                UseShellExecute=true
            };
            foreach(var argument in args)startInfo.ArgumentList.Add(argument);
            if(!args.Contains("--native-read-compat",StringComparer.OrdinalIgnoreCase))startInfo.ArgumentList.Add("--native-read-compat");
            if(!args.Contains("--native-input-compat",StringComparer.OrdinalIgnoreCase))startInfo.ArgumentList.Add("--native-input-compat");
            using var process=System.Diagnostics.Process.Start(startInfo) ?? throw new InvalidOperationException("PoteHunter.exe could not be started.");
            process.WaitForExit();
            return process.ExitCode;
        }
        catch(Exception ex)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"launcher-error.txt"),ex.ToString());
            return 1;
        }
    }
}

