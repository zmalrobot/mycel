using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using TopoOpt.Core.Geometry;

namespace TopoOpt.Core.Voxelization;

public static class Voxeliser
{
    private const double PhiFrac = 0.618033988749895; // (sqrt(5) - 1) / 2
    private const double Sqrt3Frac = 0.366025403784439; // (sqrt(3) - 1) / 2

    public static (VoxelModel Model, MeshReport? Report) Voxelise(
        IndexedMesh mesh,
        GridSpec grid,
        bool? isWatertight = null,
        MeshReport? report = null,
        bool keepOnlyLargestComponent = true,
        IProgress<float>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(grid);

        List<string> warnings = new();
        bool useThreeAxisVote = isWatertight == false || (report.HasValue && !report.Value.IsWatertight);

        byte[] domain;

        if (useThreeAxisVote)
        {
            warnings.Add("Mesh is not watertight. Employing robust 3-axis ray parity voting (2-of-3); result is approximate.");

            progress?.Report(0.1f);
            byte[] domainX = RayParityFillX(mesh, grid, cancellationToken);
            progress?.Report(0.4f);
            byte[] domainY = RayParityFillY(mesh, grid, cancellationToken);
            progress?.Report(0.7f);
            byte[] domainZ = RayParityFillZ(mesh, grid, cancellationToken);
            progress?.Report(0.85f);

            int total = (int)grid.TotalElements;
            domain = new byte[total];
            for (int i = 0; i < total; i++)
            {
                int vote = domainX[i] + domainY[i] + domainZ[i];
                domain[i] = (byte)(vote >= 2 ? 1 : 0);
            }
        }
        else
        {
            progress?.Report(0.2f);
            domain = RayParityFillX(mesh, grid, cancellationToken);
            progress?.Report(0.8f);
        }

        cancellationToken.ThrowIfCancellationRequested();

        // 6-connectivity analysis
        var (components, connWarnings) = Connectivity.AnalyzeAndFilter(grid, domain, keepOnlyLargestComponent);
        warnings.AddRange(connWarnings);

        // Count solid elements and build active elements index array
        int solidCount = 0;
        int totalElements = (int)grid.TotalElements;
        for (int i = 0; i < totalElements; i++)
        {
            if (domain[i] == 1)
                solidCount++;
        }

        int[] activeElements = new int[solidCount];
        int writeIdx = 0;
        for (int i = 0; i < totalElements; i++)
        {
            if (domain[i] == 1)
                activeElements[writeIdx++] = i;
        }

        double voxelVolume = Math.Pow(grid.H, 3);
        double solidVolume = solidCount * voxelVolume;

        progress?.Report(1.0f);

        var model = new VoxelModel(
            grid,
            domain,
            solidCount,
            solidVolume,
            components,
            warnings,
            activeElements);

        return (model, report);
    }

    private static byte[] RayParityFillX(IndexedMesh mesh, GridSpec grid, CancellationToken cancellationToken)
    {
        int nx = grid.Nx;
        int ny = grid.Ny;
        int nz = grid.Nz;
        int numRows = ny * nz;
        float h = grid.H;
        Vector3 origin = grid.Origin;

        float deltaY = (float)(PhiFrac * 1e-5 * h);
        float deltaZ = (float)(Sqrt3Frac * 1e-5 * h);

        int triCount = mesh.TriangleCount;
        int[] rowCounts = new int[numRows];
        (int iyMin, int iyMax, int izMin, int izMax)[] triBounds = new (int, int, int, int)[triCount];

        for (int t = 0; t < triCount; t++)
        {
            mesh.GetTriangle(t, out Vector3 v0, out Vector3 v1, out Vector3 v2);

            float minY = Math.Min(v0.Y, Math.Min(v1.Y, v2.Y));
            float maxY = Math.Max(v0.Y, Math.Max(v1.Y, v2.Y));
            float minZ = Math.Min(v0.Z, Math.Min(v1.Z, v2.Z));
            float maxZ = Math.Max(v0.Z, Math.Max(v1.Z, v2.Z));

            int iyMin = Math.Clamp((int)Math.Floor((minY - origin.Y) / h), 0, ny - 1);
            int iyMax = Math.Clamp((int)Math.Floor((maxY - origin.Y) / h), 0, ny - 1);
            int izMin = Math.Clamp((int)Math.Floor((minZ - origin.Z) / h), 0, nz - 1);
            int izMax = Math.Clamp((int)Math.Floor((maxZ - origin.Z) / h), 0, nz - 1);

            triBounds[t] = (iyMin, iyMax, izMin, izMax);

            for (int iz = izMin; iz <= izMax; iz++)
            {
                int baseRow = iz * ny;
                for (int iy = iyMin; iy <= iyMax; iy++)
                {
                    rowCounts[baseRow + iy]++;
                }
            }
        }

        int[] rowOffsets = new int[numRows + 1];
        int totalEntries = 0;
        for (int r = 0; r < numRows; r++)
        {
            rowOffsets[r] = totalEntries;
            totalEntries += rowCounts[r];
        }
        rowOffsets[numRows] = totalEntries;

        int[] rowTriangles = new int[totalEntries];
        int[] rowFillCursor = (int[])rowOffsets.Clone();

        for (int t = 0; t < triCount; t++)
        {
            var (iyMin, iyMax, izMin, izMax) = triBounds[t];
            for (int iz = izMin; iz <= izMax; iz++)
            {
                int baseRow = iz * ny;
                for (int iy = iyMin; iy <= iyMax; iy++)
                {
                    int r = baseRow + iy;
                    rowTriangles[rowFillCursor[r]++] = t;
                }
            }
        }

        byte[] domain = new byte[(long)nx * ny * nz];

        Parallel.For(0, numRows, r =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            int iz = r / ny;
            int iy = r % ny;

            float rayY = origin.Y + (iy + 0.5f) * h + deltaY;
            float rayZ = origin.Z + (iz + 0.5f) * h + deltaZ;

            int start = rowOffsets[r];
            int count = rowOffsets[r + 1] - start;
            if (count == 0)
                return;

            List<float> crossings = new(count);

            for (int i = 0; i < count; i++)
            {
                int t = rowTriangles[start + i];
                mesh.GetTriangle(t, out Vector3 v0, out Vector3 v1, out Vector3 v2);

                float e1Y = v1.Y - v0.Y;
                float e1Z = v1.Z - v0.Z;
                float e2Y = v2.Y - v0.Y;
                float e2Z = v2.Z - v0.Z;

                float det = e1Y * e2Z - e1Z * e2Y;
                if (Math.Abs(det) < 1e-12f)
                    continue;

                float invDet = 1.0f / det;
                float dy = rayY - v0.Y;
                float dz = rayZ - v0.Z;

                float u = (dy * e2Z - dz * e2Y) * invDet;
                if (u < 0f || u > 1f)
                    continue;

                float v = (e1Y * dz - e1Z * dy) * invDet;
                if (v < 0f || u + v > 1f)
                    continue;

                float xHit = v0.X + u * (v1.X - v0.X) + v * (v2.X - v0.X);
                crossings.Add(xHit);
            }

            if (crossings.Count == 0)
                return;

            crossings.Sort();

            int baseElemIndex = nx * (iy + ny * iz);

            int cCount = crossings.Count;
            for (int k = 0; k + 1 < cCount; k += 2)
            {
                float xIn = crossings[k];
                float xOut = crossings[k + 1];

                int ixStart = (int)Math.Floor((xIn - origin.X) / h);
                int ixEnd = (int)Math.Floor((xOut - origin.X) / h);

                ixStart = Math.Clamp(ixStart, 0, nx - 1);
                ixEnd = Math.Clamp(ixEnd, 0, nx - 1);

                for (int ix = ixStart; ix <= ixEnd; ix++)
                {
                    float xCenter = origin.X + (ix + 0.5f) * h;
                    if (xCenter >= xIn && xCenter <= xOut)
                    {
                        domain[baseElemIndex + ix] = 1;
                    }
                }
            }
        });

        return domain;
    }

    private static byte[] RayParityFillY(IndexedMesh mesh, GridSpec grid, CancellationToken cancellationToken)
    {
        int nx = grid.Nx;
        int ny = grid.Ny;
        int nz = grid.Nz;
        int numRows = nx * nz;
        float h = grid.H;
        Vector3 origin = grid.Origin;

        float deltaX = (float)(PhiFrac * 1e-5 * h);
        float deltaZ = (float)(Sqrt3Frac * 1e-5 * h);

        int triCount = mesh.TriangleCount;
        int[] rowCounts = new int[numRows];
        (int ixMin, int ixMax, int izMin, int izMax)[] triBounds = new (int, int, int, int)[triCount];

        for (int t = 0; t < triCount; t++)
        {
            mesh.GetTriangle(t, out Vector3 v0, out Vector3 v1, out Vector3 v2);

            float minX = Math.Min(v0.X, Math.Min(v1.X, v2.X));
            float maxX = Math.Max(v0.X, Math.Max(v1.X, v2.X));
            float minZ = Math.Min(v0.Z, Math.Min(v1.Z, v2.Z));
            float maxZ = Math.Max(v0.Z, Math.Max(v1.Z, v2.Z));

            int ixMin = Math.Clamp((int)Math.Floor((minX - origin.X) / h), 0, nx - 1);
            int ixMax = Math.Clamp((int)Math.Floor((maxX - origin.X) / h), 0, nx - 1);
            int izMin = Math.Clamp((int)Math.Floor((minZ - origin.Z) / h), 0, nz - 1);
            int izMax = Math.Clamp((int)Math.Floor((maxZ - origin.Z) / h), 0, nz - 1);

            triBounds[t] = (ixMin, ixMax, izMin, izMax);

            for (int iz = izMin; iz <= izMax; iz++)
            {
                int baseRow = iz * nx;
                for (int ix = ixMin; ix <= ixMax; ix++)
                {
                    rowCounts[baseRow + ix]++;
                }
            }
        }

        int[] rowOffsets = new int[numRows + 1];
        int totalEntries = 0;
        for (int r = 0; r < numRows; r++)
        {
            rowOffsets[r] = totalEntries;
            totalEntries += rowCounts[r];
        }
        rowOffsets[numRows] = totalEntries;

        int[] rowTriangles = new int[totalEntries];
        int[] rowFillCursor = (int[])rowOffsets.Clone();

        for (int t = 0; t < triCount; t++)
        {
            var (ixMin, ixMax, izMin, izMax) = triBounds[t];
            for (int iz = izMin; iz <= izMax; iz++)
            {
                int baseRow = iz * nx;
                for (int ix = ixMin; ix <= ixMax; ix++)
                {
                    int r = baseRow + ix;
                    rowTriangles[rowFillCursor[r]++] = t;
                }
            }
        }

        byte[] domain = new byte[(long)nx * ny * nz];

        Parallel.For(0, numRows, r =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            int iz = r / nx;
            int ix = r % nx;

            float rayX = origin.X + (ix + 0.5f) * h + deltaX;
            float rayZ = origin.Z + (iz + 0.5f) * h + deltaZ;

            int start = rowOffsets[r];
            int count = rowOffsets[r + 1] - start;
            if (count == 0)
                return;

            List<float> crossings = new(count);

            for (int i = 0; i < count; i++)
            {
                int t = rowTriangles[start + i];
                mesh.GetTriangle(t, out Vector3 v0, out Vector3 v1, out Vector3 v2);

                float e1X = v1.X - v0.X;
                float e1Z = v1.Z - v0.Z;
                float e2X = v2.X - v0.X;
                float e2Z = v2.Z - v0.Z;

                float det = e1X * e2Z - e1Z * e2X;
                if (Math.Abs(det) < 1e-12f)
                    continue;

                float invDet = 1.0f / det;
                float dx = rayX - v0.X;
                float dz = rayZ - v0.Z;

                float u = (dx * e2Z - dz * e2X) * invDet;
                if (u < 0f || u > 1f)
                    continue;

                float v = (e1X * dz - e1Z * dx) * invDet;
                if (v < 0f || u + v > 1f)
                    continue;

                float yHit = v0.Y + u * (v1.Y - v0.Y) + v * (v2.Y - v0.Y);
                crossings.Add(yHit);
            }

            if (crossings.Count == 0)
                return;

            crossings.Sort();

            int cCount = crossings.Count;
            for (int k = 0; k + 1 < cCount; k += 2)
            {
                float yIn = crossings[k];
                float yOut = crossings[k + 1];

                int iyStart = Math.Clamp((int)Math.Floor((yIn - origin.Y) / h), 0, ny - 1);
                int iyEnd = Math.Clamp((int)Math.Floor((yOut - origin.Y) / h), 0, ny - 1);

                for (int iy = iyStart; iy <= iyEnd; iy++)
                {
                    float yCenter = origin.Y + (iy + 0.5f) * h;
                    if (yCenter >= yIn && yCenter <= yOut)
                    {
                        int elemIdx = ix + nx * (iy + ny * iz);
                        domain[elemIdx] = 1;
                    }
                }
            }
        });

        return domain;
    }

    private static byte[] RayParityFillZ(IndexedMesh mesh, GridSpec grid, CancellationToken cancellationToken)
    {
        int nx = grid.Nx;
        int ny = grid.Ny;
        int nz = grid.Nz;
        int numRows = nx * ny;
        float h = grid.H;
        Vector3 origin = grid.Origin;

        float deltaX = (float)(PhiFrac * 1e-5 * h);
        float deltaY = (float)(Sqrt3Frac * 1e-5 * h);

        int triCount = mesh.TriangleCount;
        int[] rowCounts = new int[numRows];
        (int ixMin, int ixMax, int iyMin, int iyMax)[] triBounds = new (int, int, int, int)[triCount];

        for (int t = 0; t < triCount; t++)
        {
            mesh.GetTriangle(t, out Vector3 v0, out Vector3 v1, out Vector3 v2);

            float minX = Math.Min(v0.X, Math.Min(v1.X, v2.X));
            float maxX = Math.Max(v0.X, Math.Max(v1.X, v2.X));
            float minY = Math.Min(v0.Y, Math.Min(v1.Y, v2.Y));
            float maxY = Math.Max(v0.Y, Math.Max(v1.Y, v2.Y));

            int ixMin = Math.Clamp((int)Math.Floor((minX - origin.X) / h), 0, nx - 1);
            int ixMax = Math.Clamp((int)Math.Floor((maxX - origin.X) / h), 0, nx - 1);
            int iyMin = Math.Clamp((int)Math.Floor((minY - origin.Y) / h), 0, ny - 1);
            int iyMax = Math.Clamp((int)Math.Floor((maxY - origin.Y) / h), 0, ny - 1);

            triBounds[t] = (ixMin, ixMax, iyMin, iyMax);

            for (int iy = iyMin; iy <= iyMax; iy++)
            {
                int baseRow = iy * nx;
                for (int ix = ixMin; ix <= ixMax; ix++)
                {
                    rowCounts[baseRow + ix]++;
                }
            }
        }

        int[] rowOffsets = new int[numRows + 1];
        int totalEntries = 0;
        for (int r = 0; r < numRows; r++)
        {
            rowOffsets[r] = totalEntries;
            totalEntries += rowCounts[r];
        }
        rowOffsets[numRows] = totalEntries;

        int[] rowTriangles = new int[totalEntries];
        int[] rowFillCursor = (int[])rowOffsets.Clone();

        for (int t = 0; t < triCount; t++)
        {
            var (ixMin, ixMax, iyMin, iyMax) = triBounds[t];
            for (int iy = iyMin; iy <= iyMax; iy++)
            {
                int baseRow = iy * nx;
                for (int ix = ixMin; ix <= ixMax; ix++)
                {
                    int r = baseRow + ix;
                    rowTriangles[rowFillCursor[r]++] = t;
                }
            }
        }

        byte[] domain = new byte[(long)nx * ny * nz];

        Parallel.For(0, numRows, r =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            int iy = r / nx;
            int ix = r % nx;

            float rayX = origin.X + (ix + 0.5f) * h + deltaX;
            float rayY = origin.Y + (iy + 0.5f) * h + deltaY;

            int start = rowOffsets[r];
            int count = rowOffsets[r + 1] - start;
            if (count == 0)
                return;

            List<float> crossings = new(count);

            for (int i = 0; i < count; i++)
            {
                int t = rowTriangles[start + i];
                mesh.GetTriangle(t, out Vector3 v0, out Vector3 v1, out Vector3 v2);

                float e1X = v1.X - v0.X;
                float e1Y = v1.Y - v0.Y;
                float e2X = v2.X - v0.X;
                float e2Y = v2.Y - v0.Y;

                float det = e1X * e2Y - e1Y * e2X;
                if (Math.Abs(det) < 1e-12f)
                    continue;

                float invDet = 1.0f / det;
                float dx = rayX - v0.X;
                float dy = rayY - v0.Y;

                float u = (dx * e2Y - dy * e2X) * invDet;
                if (u < 0f || u > 1f)
                    continue;

                float v = (e1X * dy - e1Y * dx) * invDet;
                if (v < 0f || u + v > 1f)
                    continue;

                float zHit = v0.Z + u * (v1.Z - v0.Z) + v * (v2.Z - v0.Z);
                crossings.Add(zHit);
            }

            if (crossings.Count == 0)
                return;

            crossings.Sort();

            int cCount = crossings.Count;
            for (int k = 0; k + 1 < cCount; k += 2)
            {
                float zIn = crossings[k];
                float zOut = crossings[k + 1];

                int izStart = Math.Clamp((int)Math.Floor((zIn - origin.Z) / h), 0, nz - 1);
                int izEnd = Math.Clamp((int)Math.Floor((zOut - origin.Z) / h), 0, nz - 1);

                for (int iz = izStart; iz <= izEnd; iz++)
                {
                    float zCenter = origin.Z + (iz + 0.5f) * h;
                    if (zCenter >= zIn && zCenter <= zOut)
                    {
                        int elemIdx = ix + nx * (iy + ny * iz);
                        domain[elemIdx] = 1;
                    }
                }
            }
        });

        return domain;
    }
}

