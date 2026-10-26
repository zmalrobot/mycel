using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace TopoOpt.App.Diagnostics;

/// <summary>
/// Crash logger that catches unhandled exceptions, logs them with full system diagnostics
/// into %LOCALAPPDATA%/TopoOpt/logs (Windows) or ~/.local/share/TopoOpt/logs (Linux),
/// and never swallows errors silently.
/// </summary>
public static class CrashLogger
{
    private static readonly object SyncLock = new();

    public static string LogDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TopoOpt",
        "logs");

    public static void Initialize()
    {
        try
        {
            Directory.CreateDirectory(LogDir);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to create log directory '{LogDir}': {ex.Message}");
        }

        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            Exception? ex = args.ExceptionObject as Exception;
            LogException(ex, "AppDomain.UnhandledException");
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            LogException(args.Exception, "TaskScheduler.UnobservedTaskException");
            args.SetObserved();
        };
    }

    public static string LogException(Exception? ex, string context = "Error")
    {
        lock (SyncLock)
        {
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string logPath = Path.Combine(LogDir, $"crash_{timestamp}.log");

            try
            {
                Directory.CreateDirectory(LogDir);

                using StreamWriter writer = new(logPath, append: false);
                writer.WriteLine("================================================================================");
                writer.WriteLine("TopoOpt Crash Diagnostics Report");
                writer.WriteLine("================================================================================");
                writer.WriteLine($"Timestamp (UTC):   {DateTime.UtcNow:O}");
                writer.WriteLine($"Timestamp (Local): {DateTime.Now:O}");
                writer.WriteLine($"Context:           {context}");
                writer.WriteLine($"Version:           {Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0"}");
                writer.WriteLine($"OS Description:    {RuntimeInformation.OSDescription}");
                writer.WriteLine($"Architecture:      {RuntimeInformation.ProcessArchitecture}");
                writer.WriteLine($"Framework:         {RuntimeInformation.FrameworkDescription}");
                writer.WriteLine($"Process Bitness:   {(Environment.Is64BitProcess ? "64-bit" : "32-bit")}");
                writer.WriteLine($"Available Memory:  {GetMemInfoMb():F1} MB");
                writer.WriteLine("--------------------------------------------------------------------------------");
                writer.WriteLine("Exception Details:");
                if (ex is null)
                {
                    writer.WriteLine("No exception object provided.");
                }
                else
                {
                    writer.WriteLine($"Type:       {ex.GetType().FullName}");
                    writer.WriteLine($"Message:    {ex.Message}");
                    writer.WriteLine($"Source:     {ex.Source}");
                    writer.WriteLine($"StackTrace:\n{ex.StackTrace}");

                    int innerDepth = 1;
                    Exception? inner = ex.InnerException;
                    while (inner != null)
                    {
                        writer.WriteLine($"--- Inner Exception #{innerDepth} ({inner.GetType().FullName}) ---");
                        writer.WriteLine($"Message:    {inner.Message}");
                        writer.WriteLine($"StackTrace:\n{inner.StackTrace}");
                        inner = inner.InnerException;
                        innerDepth++;
                    }
                }
                writer.WriteLine("================================================================================");

                Console.Error.WriteLine($"[TopoOpt Crash] Logged to: {logPath}");
                Trace.WriteLine($"[TopoOpt Crash] Logged to: {logPath}");
            }
            catch (Exception ioEx)
            {
                Console.Error.WriteLine($"[TopoOpt Crash] Failed to write crash log: {ioEx.Message}");
            }

            return logPath;
        }
    }

    private static double GetMemInfoMb()
    {
        try
        {
            return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024.0 * 1024.0);
        }
        catch
        {
            return -1.0;
        }
    }
}

