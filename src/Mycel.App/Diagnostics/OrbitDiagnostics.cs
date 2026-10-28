using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;

namespace Mycel.App.Diagnostics;

/// <summary>
/// Diagnostic logger and heartbeat watchdog for 3D Viewport input and render loop.
/// Activated via --debug-orbit flag or MYCEL_DEBUG_ORBIT=1 environment variable.
/// Logs to console and to %LOCALAPPDATA%/Mycel/logs/orbit_diag.log (or Linux equivalent).
/// </summary>
public static class OrbitDiagnostics
{
    private static readonly object SyncLock = new();
    private static StreamWriter? _logWriter;
    private static DispatcherTimer? _heartbeatTimer;
    private static long _lastHeartbeatTimestamp;
    private static Stopwatch? _uptimeWatch;

    public static bool IsEnabled { get; private set; }

    public static string LogFilePath { get; } = Path.Combine(CrashLogger.LogDir, "orbit_diag.log");

    public static void Initialize(string[]? args)
    {
        bool flagInArgs = false;
        if (args != null)
        {
            foreach (string arg in args)
            {
                if (string.Equals(arg, "--debug-orbit", StringComparison.OrdinalIgnoreCase))
                {
                    flagInArgs = true;
                    break;
                }
            }
        }

        string? env = Environment.GetEnvironmentVariable("MYCEL_DEBUG_ORBIT")
                      ?? Environment.GetEnvironmentVariable("TOPOOPT_DEBUG_ORBIT");
        IsEnabled = flagInArgs || string.Equals(env, "1", StringComparison.Ordinal) || string.Equals(env, "true", StringComparison.OrdinalIgnoreCase);

        if (!IsEnabled)
        {
            return;
        }

        _uptimeWatch = Stopwatch.StartNew();
        try
        {
            Directory.CreateDirectory(CrashLogger.LogDir);
            _logWriter = new StreamWriter(new FileStream(LogFilePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            {
                AutoFlush = true
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OrbitDiag] Failed to open log file '{LogFilePath}': {ex.Message}");
        }

        Log($"Diagnostics initialized at {DateTime.UtcNow:O}. Log file: {LogFilePath}");

        // Start UI thread heartbeat monitor (every 25 ms)
        _lastHeartbeatTimestamp = Stopwatch.GetTimestamp();
        _heartbeatTimer = new DispatcherTimer(DispatcherPriority.Send)
        {
            Interval = TimeSpan.FromMilliseconds(25)
        };
        _heartbeatTimer.Tick += OnHeartbeatTick;
        _heartbeatTimer.Start();
    }

    private static void OnHeartbeatTick(object? sender, EventArgs e)
    {
        long now = Stopwatch.GetTimestamp();
        double gapMs = (now - _lastHeartbeatTimestamp) * 1000.0 / Stopwatch.Frequency;
        _lastHeartbeatTimestamp = now;

        if (gapMs > 100.0)
        {
            Log($"[HeartbeatGap] UI thread blocked/delayed! Measured gap: {gapMs:F1} ms (threshold > 100 ms)");
        }
    }

    public static void Log(string message)
    {
        if (!IsEnabled) return;

        double elapsedMs = _uptimeWatch?.Elapsed.TotalMilliseconds ?? 0.0;
        string line = $"[{elapsedMs,10:F2} ms] {message}";

        Console.WriteLine(line);

        lock (SyncLock)
        {
            try
            {
                _logWriter?.WriteLine(line);
            }
            catch
            {
                // Ignore file write errors in logging
            }
        }
    }

    public static void RecordPointerPressed(PointerPoint point, KeyModifiers modifiers, bool isOrbiting, bool isPanning)
    {
        if (!IsEnabled) return;
        var props = point.Properties;
        Log($"[PointerPressed] Pos=({point.Position.X:F1},{point.Position.Y:F1}), Kind={props.PointerUpdateKind}, Left={props.IsLeftButtonPressed}, Right={props.IsRightButtonPressed}, Middle={props.IsMiddleButtonPressed}, Modifiers={modifiers} -> IsOrbiting={isOrbiting}, IsPanning={isPanning}");
    }

    public static void RecordPointerMoved(Point pos, Avalonia.Vector delta, bool isOrbiting, bool isPanning)
    {
        if (!IsEnabled) return;
        Log($"[PointerMoved] Pos=({pos.X:F1},{pos.Y:F1}), Delta=({delta.X:F1},{delta.Y:F1}) -> IsOrbiting={isOrbiting}, IsPanning={isPanning}");
    }

    public static void RecordPointerReleased(MouseButton button, bool wasOrbiting, bool wasPanning)
    {
        if (!IsEnabled) return;
        Log($"[PointerReleased] Button={button}, WasOrbiting={wasOrbiting}, WasPanning={wasPanning}");
    }

    public static void RecordPointerWheel(Point pos, double deltaY)
    {
        if (!IsEnabled) return;
        Log($"[PointerWheel] Pos=({pos.X:F1},{pos.Y:F1}), DeltaY={deltaY:F2}");
    }

    public static void RecordCameraOrbit(float deltaX, float deltaY, float azimuthRad, float elevationRad)
    {
        if (!IsEnabled) return;
        float azDeg = azimuthRad * (180.0f / MathF.PI);
        float elDeg = elevationRad * (180.0f / MathF.PI);
        Log($"[CameraOrbit] Delta=({deltaX:F1},{deltaY:F1}) -> Azimuth={azDeg:F1}°, Elevation={elDeg:F1}°");
    }

    public static void RecordCameraPan(float deltaX, float deltaY, float targetX, float targetY, float targetZ)
    {
        if (!IsEnabled) return;
        Log($"[CameraPan] Delta=({deltaX:F1},{deltaY:F1}) -> Target=({targetX:F1},{targetY:F1},{targetZ:F1})");
    }

    public static void RecordRenderRequested(string caller)
    {
        if (!IsEnabled) return;
        Log($"[RenderRequested] by {caller}");
    }

    public static void RecordRenderBegin(int frameNumber, int fbWidth, int fbHeight)
    {
        if (!IsEnabled) return;
        Log($"[RenderBegin] Frame #{frameNumber}, Size={fbWidth}x{fbHeight}");
    }

    public static void RecordRenderEnd(int frameNumber, double durationMs)
    {
        if (!IsEnabled) return;
        Log($"[RenderEnd] Frame #{frameNumber}, Duration={durationMs:F2} ms");
    }

    public static void RecordRenderException(Exception ex)
    {
        if (!IsEnabled) return;
        Log($"[RenderException] EXCEPTION IN RENDER: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
    }

    public static void RecordRenderSkipped(string reason)
    {
        if (!IsEnabled) return;
        Log($"[RenderSkipped] Reason: {reason}");
    }
}

