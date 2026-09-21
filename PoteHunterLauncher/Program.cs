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
            System.Diagnostics.Process? process;
            try { process=System.Diagnostics.Process.Start(startInfo); }
            catch(System.ComponentModel.Win32Exception)
            {
                // Some Windows installations reject the native single-file apphost
                // (0xc0000142). Fall back to the installed .NET host and the app DLL.
                var managed=Path.Combine(AppContext.BaseDirectory,"PoteHunter.dll");
                if(!File.Exists(managed))throw;
                var fallback=new System.Diagnostics.ProcessStartInfo("dotnet")
                {
                    WorkingDirectory=AppContext.BaseDirectory,
                    UseShellExecute=false
                };
                fallback.ArgumentList.Add(managed);
                foreach(var argument in args)fallback.ArgumentList.Add(argument);
                fallback.ArgumentList.Add("--native-read-compat");
                fallback.ArgumentList.Add("--native-input-compat");
                process=System.Diagnostics.Process.Start(fallback);
            }
            if(process is null)throw new InvalidOperationException("PoteHunter could not be started.");
            using(process)
            {
                process.WaitForExit();
                return process.ExitCode;
            }
        }
        catch(Exception ex)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"launcher-error.txt"),ex.ToString());
            return 1;
        }
    }
}

