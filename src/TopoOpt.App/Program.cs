using System;
using Avalonia;
using TopoOpt.App.Diagnostics;

namespace TopoOpt.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        CrashLogger.Initialize();

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            CrashLogger.LogException(ex, "Program.Main");
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}

