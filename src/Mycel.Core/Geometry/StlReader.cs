using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Mycel.Core.Geometry;

/// <summary>
/// High-performance binary and ASCII STL reader with automatic format detection,
/// spatial-hash vertex welding, degenerate triangle filtering, and manifold topology analysis.
/// </summary>
public static class StlReader
{
    /// <summary>
    /// Maximum supported triangles to prevent out-of-memory denial of service during welding and BVH acceleration.
    /// Files exceeding this limit are refused with a clear user message.
    /// </summary>
    public const int MaxSupportedTriangles = 2_500_000;

    public static Task<(IndexedMesh Mesh, MeshReport Report)> ReadAsync(
        string filePath,
        float scaleFactor = 1.0f,
        float relativeWeldTolerance = 1e-5f,
        IProgress<float>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return Task.Run(() =>
        {
            using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 65536);
            return Read(fs, scaleFactor, relativeWeldTolerance, progress, ct);
        }, ct);
    }

    public static (IndexedMesh Mesh, MeshReport Report) Read(
        Stream stream,
        float scaleFactor = 1.0f,
        float relativeWeldTolerance = 1e-5f,
        IProgress<float>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scaleFactor);

        Stopwatch sw = Stopwatch.StartNew();

        Stream seekableStream = stream;
        MemoryStream? tempCopy = null;
        if (!stream.CanSeek)
        {
            tempCopy = new MemoryStream();
            stream.CopyTo(tempCopy);
            tempCopy.Position = 0;
            seekableStream = tempCopy;
        }

        try
        {
            long startPos = seekableStream.Position;
            long remainingLength = seekableStream.Length - startPos;

            bool isBinary = false;
            uint binaryTriCount = 0;

            if (remainingLength >= 84)
            {
                Span<byte> header = stackalloc byte[84];
                seekableStream.ReadExactly(header);
                binaryTriCount = BinaryPrimitives.ReadUInt32LittleEndian(header[80..84]);

                // Strict 84 + 50*N check; never trust a "solid" ASCII header prefix
                long expectedBinLen = 84L + 50L * binaryTriCount;
                if (remainingLength == expectedBinLen)
                {
                    isBinary = true;
                }
                else
                {
                    // Check if the stream has binary content (nulls or non-ASCII control bytes)
                    bool hasBinaryBytes = false;
                    for (int i = 0; i < 80; i++)
                    {
                        byte b = header[i];
                        if (b == 0 || (b < 9) || (b > 13 && b < 32))
                        {
                            hasBinaryBytes = true;
                            break;
                        }
                    }

                    // Also check if it does NOT start with "solid"
                    string prefix = System.Text.Encoding.ASCII.GetString(header[..Math.Min(5, 80)]);
                    bool startsWithSolid = prefix.Equals("solid", StringComparison.OrdinalIgnoreCase);

                    if (hasBinaryBytes || !startsWithSolid)
                    {
                        // File is binary or corrupted
                        if (binaryTriCount > MaxSupportedTriangles)
                        {
                            throw new InvalidDataException(
                                $"STL file declares {binaryTriCount:N0} triangles, exceeding the maximum limit of {MaxSupportedTriangles:N0}.");
                        }

                        if (remainingLength < expectedBinLen)
                        {
                            throw new InvalidDataException(
                                $"Corrupted or truncated binary STL file: expected {expectedBinLen:N0} bytes for {binaryTriCount:N0} triangles, but stream has only {remainingLength:N0} bytes.");
                        }

                        // If it has extra trailing bytes but valid structure, we can still reject or accept
                        throw new InvalidDataException(
                            $"Corrupted binary STL file: stream length {remainingLength:N0} bytes does not match {expectedBinLen:N0} bytes for {binaryTriCount:N0} triangles.");
                    }

                    seekableStream.Position = startPos;
                }
            }

            if (isBinary)
            {
                if (binaryTriCount == 0)
                {
                    throw new InvalidDataException("STL file contains 0 triangles.");
                }
                if (binaryTriCount > MaxSupportedTriangles)
                {
                    throw new InvalidDataException(
                        $"STL file contains {binaryTriCount:N0} triangles, exceeding the maximum limit of {MaxSupportedTriangles:N0}. Please decimate the mesh before import.");
                }
            }

            Vector3[] rawVertices;
            Vector3 rawMin;
            Vector3 rawMax;

            if (isBinary)
            {
                (rawVertices, rawMin, rawMax) = ReadBinaryTriangles(seekableStream, (int)binaryTriCount, scaleFactor, progress, ct);
            }
            else
            {
                (rawVertices, rawMin, rawMax) = ReadAsciiTriangles(seekableStream, scaleFactor, progress, ct);
            }

            ct.ThrowIfCancellationRequested();

            int rawTriCount = rawVertices.Length / 3;
            if (rawTriCount == 0)
            {
                throw new InvalidDataException("STL file contains 0 valid triangles.");
            }

            float bboxDiag = Vector3.Distance(rawMin, rawMax);
            if (float.IsNaN(bboxDiag) || float.IsInfinity(bboxDiag) || bboxDiag > 1e7f || (bboxDiag < 1e-4f && bboxDiag > 0f))
            {
                throw new InvalidDataException(
                    $"STL file has an invalid or absurd scale (bounding box diagonal {bboxDiag:G4}). Expected dimensions between 0.1 mm and 10,000 mm.");
            }

            LengthUnit suspectedUnit = GuessUnitFromDiagonal(bboxDiag / scaleFactor);

            // Spatial-hash welding
            var (uniquePositions, indices, degenerateSkipped, verticesMerged) = WeldVertices(
                rawVertices, rawMin, bboxDiag, relativeWeldTolerance, ct);

            progress?.Report(0.85f);

            // Edge manifoldness & watertightness check
            var (openEdges, nonManifoldEdges) = AnalyzeTopology(indices);

            IndexedMesh mesh = new(uniquePositions, indices);
            bool isWatertight = mesh.TriangleCount > 0 && openEdges == 0 && nonManifoldEdges == 0;

            sw.Stop();
            progress?.Report(1.0f);

            MeshReport report = new(
                Triangles: mesh.TriangleCount,
                Vertices: mesh.VertexCount,
                DegenerateSkipped: degenerateSkipped,
                VerticesMerged: verticesMerged,
                OpenEdges: openEdges,
                NonManifoldEdges: nonManifoldEdges,
                IsWatertight: isWatertight,
                Volume: mesh.SignedVolume,
                BBox: mesh.BBox,
                SuspectedUnits: suspectedUnit,
                LoadDuration: sw.Elapsed);

            return (mesh, report);
        }
        finally
        {
            tempCopy?.Dispose();
        }
    }

    private static (Vector3[] RawVertices, Vector3 Min, Vector3 Max) ReadBinaryTriangles(
        Stream stream,
        int triCount,
        float scaleFactor,
        IProgress<float>? progress,
        CancellationToken ct)
    {
        Vector3[] rawVertices = new Vector3[triCount * 3];
        Vector3 min = new(float.PositiveInfinity);
        Vector3 max = new(float.NegativeInfinity);

        const int BatchTriangles = 2048;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BatchTriangles * 50);

        try
        {
            int readTris = 0;
            while (readTris < triCount)
            {
                ct.ThrowIfCancellationRequested();

                int currentBatch = Math.Min(BatchTriangles, triCount - readTris);
                int bytesToRead = currentBatch * 50;
                stream.ReadExactly(buffer.AsSpan(0, bytesToRead));

                ReadOnlySpan<byte> span = buffer.AsSpan(0, bytesToRead);
                int baseVertIdx = readTris * 3;

                for (int i = 0; i < currentBatch; i++)
                {
                    ReadOnlySpan<byte> rec = span.Slice(i * 50, 50);

                    Vector3 a = ReadVector3LittleEndian(rec[12..24]) * scaleFactor;
                    Vector3 b = ReadVector3LittleEndian(rec[24..36]) * scaleFactor;
                    Vector3 c = ReadVector3LittleEndian(rec[36..48]) * scaleFactor;

                    if (!float.IsFinite(a.X) || !float.IsFinite(a.Y) || !float.IsFinite(a.Z) ||
                        !float.IsFinite(b.X) || !float.IsFinite(b.Y) || !float.IsFinite(b.Z) ||
                        !float.IsFinite(c.X) || !float.IsFinite(c.Y) || !float.IsFinite(c.Z))
                    {
                        throw new InvalidDataException(
                            $"STL file contains NaN or Infinite vertex coordinates at triangle {readTris + i}.");
                    }

                    min = Vector3.Min(min, Vector3.Min(a, Vector3.Min(b, c)));
                    max = Vector3.Max(max, Vector3.Max(a, Vector3.Max(b, c)));

                    int vIdx = baseVertIdx + i * 3;
                    rawVertices[vIdx] = a;
                    rawVertices[vIdx + 1] = b;
                    rawVertices[vIdx + 2] = c;
                }

                readTris += currentBatch;
                if (progress is not null && (readTris & 0xFFFF) == 0)
                {
                    progress.Report(0.5f * readTris / triCount);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return (rawVertices, min, max);
    }

    private static (Vector3[] RawVertices, Vector3 Min, Vector3 Max) ReadAsciiTriangles(
        Stream stream,
        float scaleFactor,
        IProgress<float>? progress,
        CancellationToken ct)
    {
        List<Vector3> verts = new(1024);
        Vector3 min = new(float.PositiveInfinity);
        Vector3 max = new(float.NegativeInfinity);

        using StreamReader reader = new(stream, leaveOpen: true);
        string? line;
        int lineCount = 0;

        while ((line = reader.ReadLine()) is not null)
        {
            if ((++lineCount & 0x3FFF) == 0)
            {
                ct.ThrowIfCancellationRequested();
            }

            ReadOnlySpan<char> trimmed = line.AsSpan().TrimStart();
            if (!trimmed.StartsWith("vertex", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ReadOnlySpan<char> rest = trimmed[6..].Trim();
            if (rest.IsEmpty)
            {
                continue;
            }

            if (TryParseVector3(rest, out Vector3 v))
            {
                if (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z))
                {
                    throw new InvalidDataException(
                        $"STL file contains NaN or Infinite vertex coordinates at line {lineCount}.");
                }

                v *= scaleFactor;
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
                verts.Add(v);

                if (verts.Count / 3 > MaxSupportedTriangles)
                {
                    throw new InvalidDataException(
                        $"ASCII STL file exceeds the maximum limit of {MaxSupportedTriangles:N0} triangles. Please decimate the mesh before import.");
                }
            }
        }

        int usableCount = (verts.Count / 3) * 3;
        if (verts.Count != usableCount)
        {
            verts.RemoveRange(usableCount, verts.Count - usableCount);
        }

        progress?.Report(0.5f);
        return (verts.ToArray(), min, max);
    }

    private static bool TryParseVector3(ReadOnlySpan<char> span, out Vector3 result)
    {
        result = default;

        int i = 0;
        while (i < span.Length && !char.IsWhiteSpace(span[i])) i++;
        if (!float.TryParse(span[..i], NumberStyles.Float, CultureInfo.InvariantCulture, out float x))
        {
            return false;
        }

        span = span[i..].TrimStart();
        i = 0;
        while (i < span.Length && !char.IsWhiteSpace(span[i])) i++;
        if (!float.TryParse(span[..i], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
        {
            return false;
        }

        span = span[i..].TrimStart();
        i = 0;
        while (i < span.Length && !char.IsWhiteSpace(span[i])) i++;
        if (!float.TryParse(span[..i], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
        {
            return false;
        }

        result = new Vector3(x, y, z);
        return true;
    }

    private static (Vector3[] UniquePositions, int[] Indices, int DegenerateSkipped, int VerticesMerged) WeldVertices(
        Vector3[] rawVertices,
        Vector3 min,
        float bboxDiag,
        float relativeWeldTolerance,
        CancellationToken ct)
    {
        int rawVertCount = rawVertices.Length;
        int rawTriCount = rawVertCount / 3;

        float tol = relativeWeldTolerance > 0f
            ? MathF.Max(1e-7f, relativeWeldTolerance * MathF.Max(1e-4f, bboxDiag))
            : 0f;
        float tolSq = tol * tol;

        float cellSize = MathF.Max(1e-6f, tol * 2.0f);
        float invCell = 1.0f / cellSize;

        int bucketCount = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(64, rawVertCount / 2));
        int bucketMask = bucketCount - 1;

        int[] buckets = ArrayPool<int>.Shared.Rent(bucketCount);
        int[] next = ArrayPool<int>.Shared.Rent(rawVertCount);

        try
        {
            buckets.AsSpan(0, bucketCount).Fill(-1);

            Vector3[] uniquePos = new Vector3[Math.Max(16, rawVertCount / 3)];
            int uniqueCount = 0;

            int[] triIndices = new int[rawVertCount];
            int validTriCount = 0;
            int degenerateSkipped = 0;
            int verticesUsedInValidTris = 0;

            Span<int> triVertIds = stackalloc int[3];

            for (int t = 0; t < rawTriCount; t++)
            {
                if ((t & 0xFFFF) == 0)
                {
                    ct.ThrowIfCancellationRequested();
                }

                Vector3 p0 = rawVertices[t * 3];
                Vector3 p1 = rawVertices[t * 3 + 1];
                Vector3 p2 = rawVertices[t * 3 + 2];

                // Reject zero-area degenerate triangles before or after welding
                Vector3 cross = Vector3.Cross(p1 - p0, p2 - p0);
                if (cross.LengthSquared() <= 1e-24f)
                {
                    degenerateSkipped++;
                    continue;
                }

                for (int k = 0; k < 3; k++)
                {
                    Vector3 v = rawVertices[t * 3 + k];
                    float fx = (v.X - min.X) * invCell;
                    float fy = (v.Y - min.Y) * invCell;
                    float fz = (v.Z - min.Z) * invCell;

                    int cx = (int)MathF.Floor(fx);
                    int cy = (int)MathF.Floor(fy);
                    int cz = (int)MathF.Floor(fz);

                    int foundIdx = FindInBucket(cx, cy, cz, v, tolSq, buckets, bucketMask, next, uniquePos);

                    if (foundIdx < 0 && tol > 0f)
                    {
                        int sx = (fx - cx) < 0.5f ? -1 : 1;
                        int sy = (fy - cy) < 0.5f ? -1 : 1;
                        int sz = (fz - cz) < 0.5f ? -1 : 1;

                        ReadOnlySpan<(int dx, int dy, int dz)> offsets =
                        [
                            (sx, 0, 0), (0, sy, 0), (0, 0, sz),
                            (sx, sy, 0), (sx, 0, sz), (0, sy, sz),
                            (sx, sy, sz)
                        ];

                        foreach (var (dx, dy, dz) in offsets)
                        {
                            foundIdx = FindInBucket(cx + dx, cy + dy, cz + dz, v, tolSq, buckets, bucketMask, next, uniquePos);
                            if (foundIdx >= 0)
                            {
                                break;
                            }
                        }
                    }

                    if (foundIdx < 0)
                    {
                        foundIdx = uniqueCount++;
                        if (foundIdx == uniquePos.Length)
                        {
                            Array.Resize(ref uniquePos, uniquePos.Length * 2);
                        }
                        uniquePos[foundIdx] = v;

                        int h = HashCell(cx, cy, cz) & bucketMask;
                        next[foundIdx] = buckets[h];
                        buckets[h] = foundIdx;
                    }

                    triVertIds[k] = foundIdx;
                }

                int i0 = triVertIds[0];
                int i1 = triVertIds[1];
                int i2 = triVertIds[2];

                if (i0 == i1 || i1 == i2 || i2 == i0)
                {
                    degenerateSkipped++;
                    continue;
                }

                Vector3 wp0 = uniquePos[i0];
                Vector3 wp1 = uniquePos[i1];
                Vector3 wp2 = uniquePos[i2];
                if (Vector3.Cross(wp1 - wp0, wp2 - wp0).LengthSquared() <= 1e-24f)
                {
                    degenerateSkipped++;
                    continue;
                }

                int baseOut = validTriCount * 3;
                triIndices[baseOut] = i0;
                triIndices[baseOut + 1] = i1;
                triIndices[baseOut + 2] = i2;
                validTriCount++;
                verticesUsedInValidTris += 3;
            }

            if (uniqueCount != uniquePos.Length)
            {
                Array.Resize(ref uniquePos, uniqueCount);
            }

            int finalIndexLen = validTriCount * 3;
            if (finalIndexLen != triIndices.Length)
            {
                Array.Resize(ref triIndices, finalIndexLen);
            }

            int merged = Math.Max(0, verticesUsedInValidTris - uniqueCount);
            return (uniquePos, triIndices, degenerateSkipped, merged);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(buckets);
            ArrayPool<int>.Shared.Return(next);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FindInBucket(
        int cx, int cy, int cz,
        Vector3 v,
        float tolSq,
        int[] buckets,
        int bucketMask,
        int[] next,
        Vector3[] uniquePos)
    {
        int h = HashCell(cx, cy, cz) & bucketMask;
        int curr = buckets[h];
        while (curr >= 0)
        {
            Vector3 existing = uniquePos[curr];
            if (existing == v || Vector3.DistanceSquared(existing, v) <= tolSq)
            {
                return curr;
            }
            curr = next[curr];
        }
        return -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int HashCell(int x, int y, int z)
    {
        unchecked
        {
            uint h = (uint)x * 73856093u ^ (uint)y * 19349663u ^ (uint)z * 83492791u;
            h ^= h >> 16;
            return (int)h;
        }
    }

    private static (int OpenEdges, int NonManifoldEdges) AnalyzeTopology(ReadOnlySpan<int> indices)
    {
        int triCount = indices.Length / 3;
        if (triCount == 0)
        {
            return (0, 0);
        }

        // Open-addressed hash table over undirected edge keys: ((ulong)min << 32) | (uint)max
        int capacity = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(32, triCount * 3));
        int mask = capacity - 1;

        ulong[] keys = ArrayPool<ulong>.Shared.Rent(capacity);
        uint[] counts = ArrayPool<uint>.Shared.Rent(capacity);

        try
        {
            keys.AsSpan(0, capacity).Fill(ulong.MaxValue);
            counts.AsSpan(0, capacity).Clear();

            for (int t = 0; t < triCount; t++)
            {
                int a = indices[t * 3];
                int b = indices[t * 3 + 1];
                int c = indices[t * 3 + 2];

                InsertDirectedEdge(a, b, keys, counts, mask);
                InsertDirectedEdge(b, c, keys, counts, mask);
                InsertDirectedEdge(c, a, keys, counts, mask);
            }

            int openEdges = 0;
            int nonManifoldEdges = 0;

            for (int i = 0; i < capacity; i++)
            {
                if (keys[i] == ulong.MaxValue)
                {
                    continue;
                }

                uint c = counts[i];
                uint fwd = c & 0xFFFFu;
                uint bwd = c >> 16;
                uint total = fwd + bwd;

                if (total == 1)
                {
                    openEdges++;
                }
                else if (total != 2 || fwd != 1 || bwd != 1)
                {
                    nonManifoldEdges++;
                }
            }

            return (openEdges, nonManifoldEdges);
        }
        finally
        {
            ArrayPool<ulong>.Shared.Return(keys);
            ArrayPool<uint>.Shared.Return(counts);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void InsertDirectedEdge(int u, int v, ulong[] keys, uint[] counts, int mask)
    {
        bool isForward = u < v;
        uint min = isForward ? (uint)u : (uint)v;
        uint max = isForward ? (uint)v : (uint)u;
        ulong key = ((ulong)min << 32) | max;

        int slot = HashEdgeKey(key) & mask;
        while (true)
        {
            ulong existing = keys[slot];
            if (existing == key)
            {
                counts[slot] += isForward ? 1u : 0x10000u;
                return;
            }
            if (existing == ulong.MaxValue)
            {
                keys[slot] = key;
                counts[slot] = isForward ? 1u : 0x10000u;
                return;
            }
            slot = (slot + 1) & mask;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int HashEdgeKey(ulong key)
    {
        key ^= key >> 33;
        key *= 0xff51afd7ed558ccdUL;
        key ^= key >> 33;
        return (int)key;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector3 ReadVector3LittleEndian(ReadOnlySpan<byte> src)
    {
        float x = BinaryPrimitives.ReadSingleLittleEndian(src[0..4]);
        float y = BinaryPrimitives.ReadSingleLittleEndian(src[4..8]);
        float z = BinaryPrimitives.ReadSingleLittleEndian(src[8..12]);
        return new Vector3(x, y, z);
    }

    private static LengthUnit GuessUnitFromDiagonal(float rawDiagonal)
    {
        if (rawDiagonal < 1.0f)
        {
            return LengthUnit.Meter;
        }
        if (rawDiagonal < 12.0f)
        {
            return LengthUnit.Inch;
        }
        return LengthUnit.Millimeter;
    }
}

