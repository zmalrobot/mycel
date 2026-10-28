using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Threading;

namespace Mycel.App.Diagnostics;

/// <summary>
/// Monitors Avalonia UI thread responsiveness by measuring inter-tick wall clock intervals.
/// Detects and logs any interval &gt; 100 ms as a UI gap / freeze.
/// </summary>
public sealed class UiResponsivenessMonitor : IDisposable
{
    private static readonly object SyncLock = new();
    private static UiResponsivenessMonitor? _instance;

    private readonly DispatcherTimer _timer;
    private long _lastTickTimestamp;
    private readonly double _tickIntervalMs;
    private readonly string _logFilePath;
    private StreamWriter? _logWriter;

    public static UiResponsivenessMonitor Instance
    {
        get
        {
            lock (SyncLock)
            {
                return _instance ??= new UiResponsivenessMonitor();
            }
        }
    }

    public static string CurrentOperation { get; set; } = "Idle";

    public double MaxGapMs { get; private set; }
    public double LastGapMs { get; private set; }
    public long TotalGapCount { get; private set; }

    public event Action<double, double, string>? UiGapDetected;
    public event Action<double>? MaxGapUpdated;

    public UiResponsivenessMonitor(double tickIntervalMs = 25.0)
    {
        _tickIntervalMs = tickIntervalMs;
        _logFilePath = Path.Combine(CrashLogger.LogDir, "ui_gaps.log");

        try
        {
            Directory.CreateDirectory(CrashLogger.LogDir);
            _logWriter = new StreamWriter(new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            {
                AutoFlush = true
            };
            _logWriter.WriteLine($"[{DateTime.UtcNow:O}] UiResponsivenessMonitor started (tick={tickIntervalMs:F1}ms, threshold=100ms)");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[UiResponsivenessMonitor] Failed to open log file '{_logFilePath}': {ex.Message}");
        }

        _lastTickTimestamp = Stopwatch.GetTimestamp();
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(_tickIntervalMs), DispatcherPriority.Send, OnTimerTick);
        _timer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        long now = Stopwatch.GetTimestamp();
        double elapsedMs = (now - _lastTickTimestamp) * 1000.0 / Stopwatch.Frequency;
        _lastTickTimestamp = now;

        LastGapMs = elapsedMs;

        // Gap threshold is 100 ms
        if (elapsedMs > 100.0)
        {
            TotalGapCount++;
            if (elapsedMs > MaxGapMs)
            {
                MaxGapMs = elapsedMs;
                MaxGapUpdated?.Invoke(MaxGapMs);
            }

            string op = CurrentOperation;
            string message = $"[{DateTime.UtcNow:O}] [UI GAP] {elapsedMs:F1} ms (threshold 100 ms) during operation '{op}'";

            try
            {
                _logWriter?.WriteLine(message);
            }
            catch
            {
                // Ignore logging write failures
            }

            Trace.WriteLine(message);
            Console.WriteLine(message);

            UiGapDetected?.Invoke(elapsedMs, MaxGapMs, op);
        }
    }

    public static IDisposable BeginOperation(string operationName)
    {
        string prev = CurrentOperation;
        CurrentOperation = operationName;
        return new OperationScope(prev);
    }

    private sealed class OperationScope(string previousOperation) : IDisposable
    {
        public void Dispose()
        {
            CurrentOperation = previousOperation;
        }
    }

    public void ResetMaxGap()
    {
        MaxGapMs = 0;
        TotalGapCount = 0;
        MaxGapUpdated?.Invoke(0);
    }

    public void Dispose()
    {
        _timer.Stop();
        try
        {
            _logWriter?.Dispose();
            _logWriter = null;
        }
        catch
        {
        }
    }
}

