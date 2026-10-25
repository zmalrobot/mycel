using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using TopoOpt.Core.Geometry;
using TopoOpt.Core.PostProcessing;
using TopoOpt.Core.Voxelization;

namespace TopoOpt.Core.Persistence;

/// <summary>
/// Thread-safe random-access reader for topology optimization run folders,
/// with an LRU cache of extracted iso-surface meshes.
/// </summary>
public sealed class RunReader
{
    private readonly string _runFolder;
    private readonly string _iterationsFolder;
    private readonly RunMetadata _metadata;
    private readonly GridSpec _grid;
    private readonly int _meshCacheCapacity;

    private readonly object _cacheLock = new();
    private readonly Dictionary<(int Iteration, int QuantizedThreshold), LinkedListNode<CacheEntry>> _cacheMap = new();
    private readonly LinkedList<CacheEntry> _cacheLru = new();

    private sealed record CacheEntry((int Iteration, int QuantizedThreshold) Key, IndexedMesh Mesh);

    public string RunFolder => _runFolder;
    public RunMetadata Metadata => _metadata;
    public GridSpec Grid => _grid;

    public int CachedMeshCount
    {
        get
        {
            lock (_cacheLock)
            {
                return _cacheMap.Count;
            }
        }
    }

    private RunReader(string runFolder, RunMetadata metadata, int meshCacheCapacity)
    {
        _runFolder = runFolder;
        _iterationsFolder = Path.Combine(runFolder, "iterations");
        _metadata = metadata;
        _grid = metadata.Grid.ToGrid();
        _meshCacheCapacity = Math.Max(1, meshCacheCapacity);
    }

    public static RunReader Open(string runFolder, bool isLiveActiveRun = false, int meshCacheCapacity = 16)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runFolder);
        string fullFolder = Path.GetFullPath(runFolder);
        if (!Directory.Exists(fullFolder))
            throw new DirectoryNotFoundException($"Run folder not found: {fullFolder}");

        string runJsonPath = Path.Combine(fullFolder, "run.json");
        if (!File.Exists(runJsonPath))
            throw new FileNotFoundException("run.json not found in run folder.", runJsonPath);

        string json = File.ReadAllText(runJsonPath, Encoding.UTF8);
        var metadata = JsonSerializer.Deserialize<RunMetadata>(json)
            ?? throw new InvalidDataException("Failed to deserialize run.json.");

        // If a run was left with Status == Running and no process owns it, mark it as Interrupted
        if (!isLiveActiveRun && metadata.Status == RunStatus.Running)
        {
            metadata.Status = RunStatus.Interrupted;
        }

        var reader = new RunReader(fullFolder, metadata, meshCacheCapacity);

        // Sync CompletedIterations with actual committed iteration files on disk if interrupted
        int[] available = reader.GetAvailableIterations();
        if (available.Length > 0 && metadata.CompletedIterations < available[^1])
        {
            metadata.CompletedIterations = available[^1];
        }

        return reader;
    }

    public static IReadOnlyList<(string FolderPath, RunMetadata Metadata)> ListRuns(string baseRunsDir)
    {
        var results = new List<(string FolderPath, RunMetadata Metadata)>();
        if (!Directory.Exists(baseRunsDir))
            return results;

        foreach (string dir in Directory.EnumerateDirectories(baseRunsDir).OrderByDescending(d => d))
        {
            string runJson = Path.Combine(dir, "run.json");
            if (!File.Exists(runJson))
                continue;

            try
            {
                string json = File.ReadAllText(runJson, Encoding.UTF8);
                var meta = JsonSerializer.Deserialize<RunMetadata>(json);
                if (meta != null)
                {
                    if (meta.Status == RunStatus.Running)
                        meta.Status = RunStatus.Interrupted;
                    results.Add((dir, meta));
                }
            }
            catch
            {
                // Skip malformed run folders
            }
        }

        return results;
    }

    /// <summary>
    /// Returns the sorted list of committed iteration numbers in <c>iterations/it_NNNNNN.bin.br</c>.
    /// Strictly ignores any <c>.tmp</c> files.
    /// </summary>
    public int[] GetAvailableIterations()
    {
        if (!Directory.Exists(_iterationsFolder))
            return [];

        var list = new List<int>();
        foreach (string file in Directory.EnumerateFiles(_iterationsFolder, "it_*.bin.br"))
        {
            string name = Path.GetFileName(file);
            // Must match exact "it_NNNNNN.bin.br" (length 16) and not end with .tmp
            if (name.StartsWith("it_", StringComparison.Ordinal) &&
                name.EndsWith(".bin.br", StringComparison.Ordinal) &&
                !name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                string numPart = name.Substring(3, name.Length - 3 - 7);
                if (int.TryParse(numPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iter))
                {
                    list.Add(iter);
                }
            }
        }

        list.Sort();
        return list.ToArray();
    }

    public (byte[] Domain, byte[] Passive) ReadDomainMasks()
    {
        string path = Path.Combine(_runFolder, "domain.bin.br");
        if (!File.Exists(path))
            throw new FileNotFoundException("domain.bin.br not found.", path);

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Span<byte> hdr = stackalloc byte[20];
        fs.ReadExactly(hdr);

        if (!hdr[0..4].SequenceEqual("DOMN"u8))
            throw new InvalidDataException("Invalid magic in domain.bin.br.");

        int nx = BinaryPrimitives.ReadInt32LittleEndian(hdr[8..12]);
        int ny = BinaryPrimitives.ReadInt32LittleEndian(hdr[12..16]);
        int nz = BinaryPrimitives.ReadInt32LittleEndian(hdr[16..20]);
        int total = nx * ny * nz;

        using var brotli = new BrotliStream(fs, CompressionMode.Decompress);
        byte[] domain = new byte[total];
        byte[] passive = new byte[total];
        ReadAllBytes(brotli, domain);
        ReadAllBytes(brotli, passive);

        return (domain, passive);
    }

    public (IterationHeader Header, float[] PhysicalDensity) ReadIteration(int iteration)
    {
        int resolvedIter = ResolveNearestCommittedIteration(iteration);
        string fileName = $"it_{resolvedIter:D6}.bin.br";
        string path = Path.Combine(_iterationsFolder, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Iteration file not found for iteration {iteration}.", path);

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Span<byte> hdr = stackalloc byte[72];
        fs.ReadExactly(hdr);

        if (!hdr[0..4].SequenceEqual("TOPO"u8))
            throw new InvalidDataException($"Invalid magic header in {fileName}.");

        int version = BinaryPrimitives.ReadInt32LittleEndian(hdr[4..8]);
        int nx = BinaryPrimitives.ReadInt32LittleEndian(hdr[8..12]);
        int ny = BinaryPrimitives.ReadInt32LittleEndian(hdr[12..16]);
        int nz = BinaryPrimitives.ReadInt32LittleEndian(hdr[16..20]);
        int iterNum = BinaryPrimitives.ReadInt32LittleEndian(hdr[20..24]);
        double compliance = BinaryPrimitives.ReadDoubleLittleEndian(hdr[24..32]);
        double vol = BinaryPrimitives.ReadDoubleLittleEndian(hdr[32..40]);
        double change = BinaryPrimitives.ReadDoubleLittleEndian(hdr[40..48]);
        double grey = BinaryPrimitives.ReadDoubleLittleEndian(hdr[48..56]);
        int solverIters = BinaryPrimitives.ReadInt32LittleEndian(hdr[56..60]);
        double iterMs = BinaryPrimitives.ReadDoubleLittleEndian(hdr[60..68]);
        uint checksum = BinaryPrimitives.ReadUInt32LittleEndian(hdr[68..72]);

        int totalElements = nx * ny * nz;
        byte[] quantized = new byte[totalElements];
        using (var brotli = new BrotliStream(fs, CompressionMode.Decompress))
        {
            ReadAllBytes(brotli, quantized);
        }

        // Verify checksum
        uint computedHash = 2166136261u;
        float[] density = new float[totalElements];
        const float inv255 = 1.0f / 255.0f;

        for (int i = 0; i < totalElements; i++)
        {
            byte q = quantized[i];
            computedHash ^= q;
            computedHash *= 16777619u;
            density[i] = q * inv255;
        }

        if (computedHash != checksum)
        {
            throw new InvalidDataException($"Checksum mismatch in {fileName}: expected {checksum}, got {computedHash}.");
        }

        var headerObj = new IterationHeader(
            version, nx, ny, nz, iterNum, compliance, vol, change, grey, solverIters, iterMs, checksum);

        return (headerObj, density);
    }

    public float[] ReadIterationDensity(int iteration) => ReadIteration(iteration).PhysicalDensity;

    public (int Iteration, float[] DesignDensity, float[] PhysicalDensity)? ReadLatestCheckpoint()
    {
        string path = Path.Combine(_runFolder, "checkpoint_latest.bin.br");
        if (File.Exists(path))
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                Span<byte> hdr = stackalloc byte[16];
                fs.ReadExactly(hdr);
                if (hdr[0..4].SequenceEqual("CHKP"u8))
                {
                    int iter = BinaryPrimitives.ReadInt32LittleEndian(hdr[8..12]);
                    int len = BinaryPrimitives.ReadInt32LittleEndian(hdr[12..16]);

                    using var brotli = new BrotliStream(fs, CompressionMode.Decompress);
                    byte[] rawDesign = new byte[len * sizeof(float)];
                    byte[] rawPhys = new byte[len * sizeof(float)];
                    ReadAllBytes(brotli, rawDesign);
                    ReadAllBytes(brotli, rawPhys);

                    float[] design = new float[len];
                    float[] phys = new float[len];
                    Buffer.BlockCopy(rawDesign, 0, design, 0, rawDesign.Length);
                    Buffer.BlockCopy(rawPhys, 0, phys, 0, rawPhys.Length);
                    return (iter, design, phys);
                }
            }
            catch
            {
                // Fall back to latest iteration file
            }
        }

        int[] avail = GetAvailableIterations();
        if (avail.Length == 0)
            return null;

        int lastIter = avail[^1];
        float[] d = ReadIterationDensity(lastIter);
        return (lastIter, (float[])d.Clone(), d);
    }

    public IReadOnlyList<IterationMetricRow> ReadMetrics()
    {
        string path = Path.Combine(_runFolder, "metrics.csv");
        var rows = new List<IterationMetricRow>();
        if (!File.Exists(path))
            return rows;

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs, Encoding.UTF8);

        string? line;
        bool isFirst = true;
        while ((line = sr.ReadLine()) != null)
        {
            if (isFirst)
            {
                isFirst = false;
                if (line.StartsWith("iteration", StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            if (string.IsNullOrWhiteSpace(line))
                continue;

            string[] parts = line.Split(',');
            if (parts.Length < 8)
                continue;

            if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int iter) &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double comp) &&
                double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double vol) &&
                double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double chg) &&
                double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out double grey) &&
                int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int sIter) &&
                double.TryParse(parts[6], NumberStyles.Float, CultureInfo.InvariantCulture, out double sRes) &&
                double.TryParse(parts[7], NumberStyles.Float, CultureInfo.InvariantCulture, out double ms))
            {
                long disk = parts.Length >= 9 && long.TryParse(parts[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out long d)
                    ? d
                    : 0L;
                rows.Add(new IterationMetricRow(iter, comp, vol, chg, grey, sIter, sRes, ms, disk));
            }
        }

        return rows;
    }

    public IndexedMesh GetOrExtractMesh(int iteration, float threshold = 0.5f, CancellationToken ct = default)
    {
        int resolvedIter = ResolveNearestCommittedIteration(iteration);
        int qThresh = (int)MathF.Round(threshold * 1000f);
        var key = (resolvedIter, qThresh);

        lock (_cacheLock)
        {
            if (_cacheMap.TryGetValue(key, out var node))
            {
                _cacheLru.Remove(node);
                _cacheLru.AddFirst(node);
                return node.Value.Mesh;
            }
        }

        float[] density = ReadIterationDensity(resolvedIter);
        ct.ThrowIfCancellationRequested();
        IndexedMesh mesh = SurfaceNetsExtractor.Extract(_grid, density, threshold, ct);

        lock (_cacheLock)
        {
            if (_cacheMap.TryGetValue(key, out var existing))
            {
                _cacheLru.Remove(existing);
                _cacheLru.AddFirst(existing);
                return existing.Value.Mesh;
            }

            if (_cacheMap.Count >= _meshCacheCapacity && _cacheLru.Last != null)
            {
                var evicted = _cacheLru.Last.Value;
                _cacheLru.RemoveLast();
                _cacheMap.Remove(evicted.Key);
            }

            var entry = new CacheEntry(key, mesh);
            var newNode = new LinkedListNode<CacheEntry>(entry);
            _cacheLru.AddFirst(newNode);
            _cacheMap[key] = newNode;
        }

        return mesh;
    }

    private int ResolveNearestCommittedIteration(int requestedIteration)
    {
        string exactPath = Path.Combine(_iterationsFolder, $"it_{requestedIteration:D6}.bin.br");
        if (File.Exists(exactPath))
            return requestedIteration;

        int[] available = GetAvailableIterations();
        if (available.Length == 0)
            return requestedIteration;

        int best = available[0];
        int bestDist = Math.Abs(best - requestedIteration);
        for (int i = 1; i < available.Length; i++)
        {
            int dist = Math.Abs(available[i] - requestedIteration);
            if (dist < bestDist)
            {
                best = available[i];
                bestDist = dist;
            }
        }
        return best;
    }

    private static void ReadAllBytes(Stream stream, byte[] buffer)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read == 0)
                throw new EndOfStreamException("Unexpected end of compressed stream.");
            offset += read;
        }
    }
}

