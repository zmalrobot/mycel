using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TopoOpt.Core.Optimization;

namespace TopoOpt.Core.Persistence;

/// <summary>
/// Single-writer persistence engine for a topology optimization run folder.
/// Uses atomic temp-file write + rename for all binary and JSON artifacts.
/// </summary>
public sealed class RunWriter : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true
    };

    private readonly string _runFolder;
    private readonly string _iterationsFolder;
    private readonly string _runJsonPath;
    private readonly string _metricsCsvPath;
    private readonly string _domainBinPath;
    private readonly string _checkpointPath;
    private readonly RunMetadata _metadata;
    private readonly int _keepEveryN;

    private StreamWriter? _metricsWriter;
    private long _runningDiskBytes;
    private int _writtenSnapshotCount;
    private uint _rollingStreamChecksum = 2166136261u;

    public string RunFolder => _runFolder;
    public RunMetadata Metadata => _metadata;
    public long RunningDiskBytes => Interlocked.Read(ref _runningDiskBytes);
    public int WrittenSnapshotCount => Volatile.Read(ref _writtenSnapshotCount);
    public uint RollingStreamChecksum => Volatile.Read(ref _rollingStreamChecksum);

    /// <summary>
    /// Optional delay per write used by backpressure tests.
    /// </summary>
    public TimeSpan SimulatedWriteDelay { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Optional crash simulation hook: when set to a positive iteration number,
    /// writes the .tmp file for that iteration and throws before atomic rename.
    /// </summary>
    public int SimulateCrashOnIteration { get; set; } = -1;

    public RunWriter(
        string runFolder,
        RunMetadata metadata,
        ReadOnlySpan<byte> domainMask,
        ReadOnlySpan<byte> passiveMask,
        bool isResume = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runFolder);
        ArgumentNullException.ThrowIfNull(metadata);

        _runFolder = Path.GetFullPath(runFolder);
        _iterationsFolder = Path.Combine(_runFolder, "iterations");
        _runJsonPath = Path.Combine(_runFolder, "run.json");
        _metricsCsvPath = Path.Combine(_runFolder, "metrics.csv");
        _domainBinPath = Path.Combine(_runFolder, "domain.bin.br");
        _checkpointPath = Path.Combine(_runFolder, "checkpoint_latest.bin.br");
        _metadata = metadata;
        _keepEveryN = Math.Max(1, metadata.Options.KeepEveryN);

        Directory.CreateDirectory(_runFolder);
        Directory.CreateDirectory(_iterationsFolder);

        // Clean up any leftover .tmp files from prior crash
        CleanupTempFiles(_runFolder);
        CleanupTempFiles(_iterationsFolder);

        if (!isResume || !File.Exists(_domainBinPath))
        {
            WriteDomainFile(domainMask, passiveMask);
        }

        bool writeCsvHeader = !isResume || !File.Exists(_metricsCsvPath) || new FileInfo(_metricsCsvPath).Length == 0;
        _metricsWriter = new StreamWriter(new FileStream(_metricsCsvPath, FileMode.Append, FileAccess.Write, FileShare.Read), Encoding.UTF8);
        if (writeCsvHeader)
        {
            _metricsWriter.WriteLine("iteration,compliance,volume_fraction,max_change,grey_level,solver_iterations,solver_residual,total_ms,disk_bytes");
            _metricsWriter.Flush();
        }

        _runningDiskBytes = ComputeDirectoryBytes(_runFolder);
        _metadata.Status = RunStatus.Running;
        _metadata.UpdatedAtUtc = DateTime.UtcNow;
        _metadata.TotalDiskBytes = _runningDiskBytes;
        WriteMetadataAtomic();
    }

    public static string CreateTimestampedRunFolder(string baseRunsDir, string modelName)
    {
        Directory.CreateDirectory(baseRunsDir);
        string safeName = SanitizeName(string.IsNullOrWhiteSpace(modelName) ? "model" : Path.GetFileNameWithoutExtension(modelName));
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
        string folderName = $"{timestamp}_{safeName}";
        string fullPath = Path.Combine(baseRunsDir, folderName);
        int suffix = 1;
        while (Directory.Exists(fullPath))
        {
            fullPath = Path.Combine(baseRunsDir, $"{folderName}_{suffix++}");
        }
        return fullPath;
    }

    public static long EstimateDiskBytes(int totalBoxElements, int maxIterations, int keepEveryN = 1)
    {
        keepEveryN = Math.Max(1, keepEveryN);
        int storedFrames = keepEveryN == 1
            ? maxIterations
            : 1 + (maxIterations / keepEveryN) + (maxIterations % keepEveryN != 0 ? 1 : 0);

        // Typical Brotli-compressed quantized 8-bit density is ~18-25% of raw box size + 72B header
        long bytesPerFrame = Math.Max(512L, (long)(totalBoxElements * 0.22) + 72L);
        long domainBytes = Math.Max(512L, (long)(totalBoxElements * 0.10));
        long checkpointBytes = (long)(totalBoxElements * 2.0);
        return domainBytes + checkpointBytes + storedFrames * bytesPerFrame + maxIterations * 80L;
    }

    public async Task WriteSnapshotAsync(IterationSnapshot snapshot, bool isFinal = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (SimulatedWriteDelay > TimeSpan.Zero)
        {
            await Task.Delay(SimulatedWriteDelay, ct).ConfigureAwait(false);
        }

        int iter = snapshot.Iteration;
        bool shouldKeepIterFile = (_keepEveryN <= 1) || (iter == 1) || (iter % _keepEveryN == 0) || isFinal;

        uint frameChecksum = ComputeQuantizedChecksum(snapshot.PhysicalDensity);

        long addedBytes = 0;
        if (shouldKeepIterFile)
        {
            addedBytes += WriteIterationFileAtomic(snapshot, frameChecksum);
        }

        // Write exact float checkpoint for bit-identical resume
        WriteCheckpointAtomic(snapshot);

        // Update rolling checksum and count
        _rollingStreamChecksum = (_rollingStreamChecksum ^ frameChecksum) * 16777619u;
        Interlocked.Increment(ref _writtenSnapshotCount);

        long totalBytes = ComputeDirectoryBytes(_runFolder);
        Interlocked.Exchange(ref _runningDiskBytes, totalBytes);

        // Append and flush metrics.csv row
        if (_metricsWriter != null)
        {
            string line = string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1:G17},{2:F6},{3:F6},{4:F4},{5},{6:E4},{7:F2},{8}",
                snapshot.Iteration,
                snapshot.Compliance,
                snapshot.VolumeFraction,
                snapshot.MaxChange,
                snapshot.GreyLevel,
                snapshot.SolverIterations,
                snapshot.SolverResidual,
                snapshot.TotalMs,
                totalBytes);

            await _metricsWriter.WriteLineAsync(line).ConfigureAwait(false);
            await _metricsWriter.FlushAsync(ct).ConfigureAwait(false);
        }

        _metadata.CompletedIterations = Math.Max(_metadata.CompletedIterations, iter);
        _metadata.UpdatedAtUtc = DateTime.UtcNow;
        _metadata.TotalDiskBytes = totalBytes;
        WriteMetadataAtomic();
    }

    public void FinalizeRun(RunStatus finalStatus, string? errorMessage = null)
    {
        _metricsWriter?.Flush();
        _metadata.Status = finalStatus;
        _metadata.ErrorMessage = errorMessage;
        _metadata.UpdatedAtUtc = DateTime.UtcNow;
        _metadata.TotalDiskBytes = ComputeDirectoryBytes(_runFolder);
        Interlocked.Exchange(ref _runningDiskBytes, _metadata.TotalDiskBytes);
        WriteMetadataAtomic();
    }

    private long WriteIterationFileAtomic(IterationSnapshot snapshot, uint checksum)
    {
        string fileName = $"it_{snapshot.Iteration:D6}.bin.br";
        string finalPath = Path.Combine(_iterationsFolder, fileName);
        string tmpPath = finalPath + ".tmp";

        int nx = _metadata.Grid.Nx;
        int ny = _metadata.Grid.Ny;
        int nz = _metadata.Grid.Nz;
        int totalElements = nx * ny * nz;

        float[] phys = snapshot.PhysicalDensity;
        byte[] quantized = new byte[totalElements];
        int copyCount = Math.Min(totalElements, phys.Length);
        for (int i = 0; i < copyCount; i++)
        {
            float clamped = Math.Clamp(phys[i], 0.0f, 1.0f);
            quantized[i] = (byte)MathF.Round(clamped * 255.0f, MidpointRounding.AwayFromZero);
        }

        using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            Span<byte> header = stackalloc byte[72];
            header.Clear();
            "TOPO"u8.CopyTo(header[0..4]);
            BinaryPrimitives.WriteInt32LittleEndian(header[4..8], 1); // Version 1
            BinaryPrimitives.WriteInt32LittleEndian(header[8..12], nx);
            BinaryPrimitives.WriteInt32LittleEndian(header[12..16], ny);
            BinaryPrimitives.WriteInt32LittleEndian(header[16..20], nz);
            BinaryPrimitives.WriteInt32LittleEndian(header[20..24], snapshot.Iteration);
            BinaryPrimitives.WriteDoubleLittleEndian(header[24..32], snapshot.Compliance);
            BinaryPrimitives.WriteDoubleLittleEndian(header[32..40], snapshot.VolumeFraction);
            BinaryPrimitives.WriteDoubleLittleEndian(header[40..48], snapshot.MaxChange);
            BinaryPrimitives.WriteDoubleLittleEndian(header[48..56], snapshot.GreyLevel);
            BinaryPrimitives.WriteInt32LittleEndian(header[56..60], snapshot.SolverIterations);
            BinaryPrimitives.WriteDoubleLittleEndian(header[60..68], snapshot.TotalMs);
            BinaryPrimitives.WriteUInt32LittleEndian(header[68..72], checksum);

            fs.Write(header);

            using (var brotli = new BrotliStream(fs, CompressionLevel.Fastest, leaveOpen: true))
            {
                brotli.Write(quantized);
                brotli.Flush();
            }

            fs.Flush(flushToDisk: true);
        }

        if (SimulateCrashOnIteration > 0 && snapshot.Iteration == SimulateCrashOnIteration)
        {
            throw new IOException($"Simulated crash before atomic rename on iteration {snapshot.Iteration}.");
        }

        File.Move(tmpPath, finalPath, overwrite: true);
        return new FileInfo(finalPath).Length;
    }

    private void WriteCheckpointAtomic(IterationSnapshot snapshot)
    {
        string tmpPath = _checkpointPath + ".tmp";
        float[] design = snapshot.DesignDensity;
        float[] phys = snapshot.PhysicalDensity;
        int len = design.Length;

        using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            Span<byte> hdr = stackalloc byte[16];
            "CHKP"u8.CopyTo(hdr[0..4]);
            BinaryPrimitives.WriteInt32LittleEndian(hdr[4..8], 1);
            BinaryPrimitives.WriteInt32LittleEndian(hdr[8..12], snapshot.Iteration);
            BinaryPrimitives.WriteInt32LittleEndian(hdr[12..16], len);
            fs.Write(hdr);

            using (var brotli = new BrotliStream(fs, CompressionLevel.Fastest, leaveOpen: true))
            {
                byte[] buf = new byte[len * sizeof(float)];
                Buffer.BlockCopy(design, 0, buf, 0, buf.Length);
                brotli.Write(buf);

                if (phys.Length == len)
                {
                    Buffer.BlockCopy(phys, 0, buf, 0, buf.Length);
                }
                else
                {
                    Array.Clear(buf);
                }
                brotli.Write(buf);
                brotli.Flush();
            }

            fs.Flush(flushToDisk: true);
        }

        File.Move(tmpPath, _checkpointPath, overwrite: true);
    }

    private void WriteDomainFile(ReadOnlySpan<byte> domainMask, ReadOnlySpan<byte> passiveMask)
    {
        string tmpPath = _domainBinPath + ".tmp";
        int nx = _metadata.Grid.Nx;
        int ny = _metadata.Grid.Ny;
        int nz = _metadata.Grid.Nz;
        int total = nx * ny * nz;

        using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            Span<byte> hdr = stackalloc byte[20];
            "DOMN"u8.CopyTo(hdr[0..4]);
            BinaryPrimitives.WriteInt32LittleEndian(hdr[4..8], 1);
            BinaryPrimitives.WriteInt32LittleEndian(hdr[8..12], nx);
            BinaryPrimitives.WriteInt32LittleEndian(hdr[12..16], ny);
            BinaryPrimitives.WriteInt32LittleEndian(hdr[16..20], nz);
            fs.Write(hdr);

            using (var brotli = new BrotliStream(fs, CompressionLevel.Fastest, leaveOpen: true))
            {
                byte[] dom = new byte[total];
                domainMask[..Math.Min(total, domainMask.Length)].CopyTo(dom);
                brotli.Write(dom);

                byte[] pas = new byte[total];
                if (!passiveMask.IsEmpty)
                {
                    passiveMask[..Math.Min(total, passiveMask.Length)].CopyTo(pas);
                }
                brotli.Write(pas);
                brotli.Flush();
            }

            fs.Flush(flushToDisk: true);
        }

        File.Move(tmpPath, _domainBinPath, overwrite: true);
    }

    private void WriteMetadataAtomic()
    {
        string tmpPath = _runJsonPath + ".tmp";
        string json = JsonSerializer.Serialize(_metadata, JsonOpts);
        File.WriteAllText(tmpPath, json, Encoding.UTF8);
        File.Move(tmpPath, _runJsonPath, overwrite: true);
    }

    public static uint ComputeQuantizedChecksum(ReadOnlySpan<float> physicalDensity)
    {
        uint hash = 2166136261u;
        for (int i = 0; i < physicalDensity.Length; i++)
        {
            float clamped = Math.Clamp(physicalDensity[i], 0.0f, 1.0f);
            byte q = (byte)MathF.Round(clamped * 255.0f, MidpointRounding.AwayFromZero);
            hash ^= q;
            hash *= 16777619u;
        }
        return hash;
    }

    private static void CleanupTempFiles(string folder)
    {
        if (!Directory.Exists(folder)) return;
        foreach (string tmp in Directory.EnumerateFiles(folder, "*.tmp"))
        {
            try { File.Delete(tmp); } catch { /* ignore */ }
        }
    }

    private static long ComputeDirectoryBytes(string folder)
    {
        if (!Directory.Exists(folder)) return 0;
        long sum = 0;
        foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                sum += new FileInfo(file).Length;
            }
            catch
            {
                // Ignore transient file access
            }
        }
        return sum;
    }

    private static string SanitizeName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                sb.Append(c);
            else if (char.IsWhiteSpace(c))
                sb.Append('_');
        }
        return sb.Length > 0 ? sb.ToString() : "run";
    }

    public void Dispose()
    {
        _metricsWriter?.Dispose();
        _metricsWriter = null;
    }
}

