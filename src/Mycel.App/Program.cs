using System;
using Avalonia;
using Mycel.App.Diagnostics;

namespace Mycel.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        CrashLogger.Initialize();
        OrbitDiagnostics.Initialize(args);

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

