using System.Reflection;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var assembly=Path.Combine(AppContext.BaseDirectory,"PoteHunter.dll");
            if(!File.Exists(assembly)) throw new FileNotFoundException("Keep PoteHunter.dll and the runtime files beside this launcher.",assembly);
            var entry=Assembly.LoadFrom(assembly).GetType("PoteHunter.Entry",throwOnError:true)!;
            var main=entry.GetMethod("Main",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)
                ?? throw new MissingMethodException("PoteHunter.Entry.Main was not found.");
            var result=main.Invoke(null,[args]);
            return result is int code?code:0;
        }
        catch(Exception ex)
        {
            var error=ex is TargetInvocationException {InnerException:not null} invocation ? invocation.InnerException : ex;
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"launcher-error.txt"),error!.ToString());
            return 1;
        }
    }
}
