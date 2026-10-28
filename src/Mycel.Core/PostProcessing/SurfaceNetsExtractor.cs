using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Mycel.Core.Geometry;
using Mycel.Core.Voxelization;

namespace Mycel.Core.PostProcessing;

/// <summary>
/// Extracts a closed, watertight, 2-manifold triangle mesh (<see cref="IndexedMesh"/>)
/// from a 3D voxel density field using Constrained Elastic Surface Nets (Gibson 1998)
/// with 1-voxel zero-padding around the bounding box.
/// Parallelized over Z-slabs with deterministic vertex/triangle ordering.
/// </summary>
public static class SurfaceNetsExtractor
{
    // The 12 edges of a 2x2x2 cube cell: pairs of corner indices in [0..7]
    // where corner index c = dx + 2*dy + 4*dz (dx, dy, dz in {0, 1})
    private static readonly (int A, int B)[] CubeEdges =
    [
        // 4 X-directed edges
        (0, 1), (2, 3), (4, 5), (6, 7),
        // 4 Y-directed edges
        (0, 2), (1, 3), (4, 6), (5, 7),
        // 4 Z-directed edges
        (0, 4), (1, 5), (2, 6), (3, 7)
    ];

    public static IndexedMesh Extract(
        GridSpec grid,
        ReadOnlySpan<float> physicalDensity,
        float threshold = 0.5f,
        CancellationToken ct = default)
    {
        int nx = grid.Nx;
        int ny = grid.Ny;
        int nz = grid.Nz;
        long expectedLen = (long)nx * ny * nz;
        if (physicalDensity.Length < expectedLen)
        {
            throw new ArgumentException(
                $"physicalDensity length ({physicalDensity.Length}) is smaller than grid element count ({expectedLen}).",
                nameof(physicalDensity));
        }

        // Padded grid dimensions: +2 along each axis so a 1-voxel zero border surrounds the domain
        int pxCount = nx + 2;
        int pyCount = ny + 2;
        int pzCount = nz + 2;
        int pTotal = pxCount * pyCount * pzCount;

        float[] paddedRho = new float[pTotal];
        bool[] paddedSolid = new bool[pTotal];

        int pStrideY = pxCount;
        int pStrideZ = pxCount * pyCount;

        // Copy interior densities into [1..nx, 1..ny, 1..nz]
        for (int iz = 0; iz < nz; iz++)
        {
            ct.ThrowIfCancellationRequested();
            int pzOffset = (iz + 1) * pStrideZ;
            int zOffset = iz * ny * nx;
            for (int iy = 0; iy < ny; iy++)
            {
                int pyOffset = pzOffset + (iy + 1) * pStrideY;
                int yOffset = zOffset + iy * nx;
                for (int ix = 0; ix < nx; ix++)
                {
                    float val = physicalDensity[yOffset + ix];
                    int pIdx = pyOffset + (ix + 1);
                    paddedRho[pIdx] = val;
                    paddedSolid[pIdx] = val >= threshold;
                }
            }
        }

        // Enforce 3D digital topology well-composedness in the interior [1..nx, 1..ny, 1..nz]
        // so that diagonal edge/vertex kiss configurations never create non-manifold edges or pinch vertices.
        MakeWellComposed(paddedSolid, paddedRho, threshold, nx, ny, nz, pxCount, pStrideY, pStrideZ);

        // Dual cells sit at (cx, cy, cz) in [0..nx] x [0..ny] x [0..nz]
        int cxCount = nx + 1;
        int cyCount = ny + 1;
        int czCount = nz + 1;
        int cellTotal = cxCount * cyCount * czCount;
        int cStrideY = cxCount;
        int cStrideZ = cxCount * cyCount;

        int[] cellVertexIndex = new int[cellTotal];
        Array.Fill(cellVertexIndex, -1);

        // Per-Z-slab vertex buffers for lock-free parallel extraction
        Vector3[][] slabVertices = new Vector3[czCount][];
        int[][] slabCellIndices = new int[czCount][];

        float h = (float)grid.H;
        Vector3 origin = grid.Origin;

        Parallel.For(0, czCount, new ParallelOptions { CancellationToken = ct }, cz =>
        {
            var localVerts = new List<Vector3>(cxCount * 4);
            var localCells = new List<int>(cxCount * 4);

            Span<float> cornerRho = stackalloc float[8];
            Span<bool> cornerSolid = stackalloc bool[8];
            Span<Vector3> cornerOffset =
            [
                new Vector3(-0.5f, -0.5f, -0.5f),
                new Vector3(+0.5f, -0.5f, -0.5f),
                new Vector3(-0.5f, +0.5f, -0.5f),
                new Vector3(+0.5f, +0.5f, -0.5f),
                new Vector3(-0.5f, -0.5f, +0.5f),
                new Vector3(+0.5f, -0.5f, +0.5f),
                new Vector3(-0.5f, +0.5f, +0.5f),
                new Vector3(+0.5f, +0.5f, +0.5f)
            ];

            for (int cy = 0; cy < cyCount; cy++)
            {
                for (int cx = 0; cx < cxCount; cx++)
                {
                    int mask = 0;
                    for (int dz = 0; dz < 2; dz++)
                    {
                        int pz = (cz + dz) * pStrideZ;
                        for (int dy = 0; dy < 2; dy++)
                        {
                            int py = pz + (cy + dy) * pStrideY;
                            for (int dx = 0; dx < 2; dx++)
                            {
                                int c = dx | (dy << 1) | (dz << 2);
                                int pIdx = py + (cx + dx);
                                cornerRho[c] = paddedRho[pIdx];
                                bool s = paddedSolid[pIdx];
                                cornerSolid[c] = s;
                                if (s) mask |= (1 << c);
                            }
                        }
                    }

                    if (mask == 0 || mask == 255)
                        continue;

                    // Compute average zero-crossing across the 12 cell edges
                    Vector3 sumCross = Vector3.Zero;
                    int crossCount = 0;

                    for (int e = 0; e < 12; e++)
                    {
                        var (a, b) = CubeEdges[e];
                        if (cornerSolid[a] == cornerSolid[b])
                            continue;

                        float va = cornerRho[a];
                        float vb = cornerRho[b];
                        float denom = vb - va;
                        float alpha = MathF.Abs(denom) > 1e-12f
                            ? Math.Clamp((threshold - va) / denom, 0.05f, 0.95f)
                            : 0.5f;

                        Vector3 posLocal = cornerOffset[a] + alpha * (cornerOffset[b] - cornerOffset[a]);
                        sumCross += posLocal;
                        crossCount++;
                    }

                    Vector3 cellLocalOffset = crossCount > 0 ? (sumCross / crossCount) : Vector3.Zero;
                    Vector3 worldPos = origin + new Vector3(
                        (cx + cellLocalOffset.X) * h,
                        (cy + cellLocalOffset.Y) * h,
                        (cz + cellLocalOffset.Z) * h);

                    int cIdx = cx + cy * cStrideY + cz * cStrideZ;
                    localCells.Add(cIdx);
                    localVerts.Add(worldPos);
                }
            }

            slabVertices[cz] = localVerts.ToArray();
            slabCellIndices[cz] = localCells.ToArray();
        });

        // Flatten slab vertices deterministically
        int totalVertices = 0;
        for (int cz = 0; cz < czCount; cz++)
        {
            totalVertices += slabVertices[cz].Length;
        }

        if (totalVertices == 0)
        {
            return new IndexedMesh([], []);
        }

        Vector3[] positions = new Vector3[totalVertices];
        int vCursor = 0;
        for (int cz = 0; cz < czCount; cz++)
        {
            Vector3[] sv = slabVertices[cz];
            int[] sc = slabCellIndices[cz];
            for (int i = 0; i < sv.Length; i++)
            {
                cellVertexIndex[sc[i]] = vCursor;
                positions[vCursor] = sv[i];
                vCursor++;
            }
        }

        // Emit dual quads (2 triangles per quad) for every sign-changing grid edge
        var indices = new List<int>(totalVertices * 6);

        // 1. X-directed edges between padded (px, py, pz) and (px+1, py, pz)
        //    px in [0..nx], py in [1..ny], pz in [1..nz]
        for (int pz = 1; pz <= nz; pz++)
        {
            ct.ThrowIfCancellationRequested();
            int pzOff = pz * pStrideZ;
            for (int py = 1; py <= ny; py++)
            {
                int pyOff = pzOff + py * pStrideY;
                for (int px = 0; px <= nx; px++)
                {
                    bool s0 = paddedSolid[pyOff + px];
                    bool s1 = paddedSolid[pyOff + px + 1];
                    if (s0 == s1) continue;

                    int v0 = cellVertexIndex[px + (py - 1) * cStrideY + (pz - 1) * cStrideZ];
                    int v1 = cellVertexIndex[px + py * cStrideY + (pz - 1) * cStrideZ];
                    int v2 = cellVertexIndex[px + py * cStrideY + pz * cStrideZ];
                    int v3 = cellVertexIndex[px + (py - 1) * cStrideY + pz * cStrideZ];

                    EmitQuad(indices, positions, v0, v1, v2, v3, flip: !s0);
                }
            }
        }

        // 2. Y-directed edges between padded (px, py, pz) and (px, py+1, pz)
        //    px in [1..nx], py in [0..ny], pz in [1..nz]
        for (int pz = 1; pz <= nz; pz++)
        {
            ct.ThrowIfCancellationRequested();
            int pzOff = pz * pStrideZ;
            for (int py = 0; py <= ny; py++)
            {
                int pyOff0 = pzOff + py * pStrideY;
                int pyOff1 = pzOff + (py + 1) * pStrideY;
                for (int px = 1; px <= nx; px++)
                {
                    bool s0 = paddedSolid[pyOff0 + px];
                    bool s1 = paddedSolid[pyOff1 + px];
                    if (s0 == s1) continue;

                    int v0 = cellVertexIndex[(px - 1) + py * cStrideY + (pz - 1) * cStrideZ];
                    int v1 = cellVertexIndex[(px - 1) + py * cStrideY + pz * cStrideZ];
                    int v2 = cellVertexIndex[px + py * cStrideY + pz * cStrideZ];
                    int v3 = cellVertexIndex[px + py * cStrideY + (pz - 1) * cStrideZ];

                    EmitQuad(indices, positions, v0, v1, v2, v3, flip: !s0);
                }
            }
        }

        // 3. Z-directed edges between padded (px, py, pz) and (px, py, pz+1)
        //    px in [1..nx], py in [1..ny], pz in [0..nz]
        for (int pz = 0; pz <= nz; pz++)
        {
            ct.ThrowIfCancellationRequested();
            int pzOff0 = pz * pStrideZ;
            int pzOff1 = (pz + 1) * pStrideZ;
            for (int py = 1; py <= ny; py++)
            {
                int pyOff0 = pzOff0 + py * pStrideY;
                int pyOff1 = pzOff1 + py * pStrideY;
                for (int px = 1; px <= nx; px++)
                {
                    bool s0 = paddedSolid[pyOff0 + px];
                    bool s1 = paddedSolid[pyOff1 + px];
                    if (s0 == s1) continue;

                    int v0 = cellVertexIndex[(px - 1) + (py - 1) * cStrideY + pz * cStrideZ];
                    int v1 = cellVertexIndex[px + (py - 1) * cStrideY + pz * cStrideZ];
                    int v2 = cellVertexIndex[px + py * cStrideY + pz * cStrideZ];
                    int v3 = cellVertexIndex[(px - 1) + py * cStrideY + pz * cStrideZ];

                    EmitQuad(indices, positions, v0, v1, v2, v3, flip: !s0);
                }
            }
        }

        return new IndexedMesh(positions, indices.ToArray());
    }

    private static void EmitQuad(List<int> indices, Vector3[] positions, int v0, int v1, int v2, int v3, bool flip)
    {
        if (v0 < 0 || v1 < 0 || v2 < 0 || v3 < 0)
            return;

        if (flip)
        {
            (v1, v3) = (v3, v1);
        }

        // Split quad along the shorter diagonal to avoid sliver triangles
        float diag02Sq = Vector3.DistanceSquared(positions[v0], positions[v2]);
        float diag13Sq = Vector3.DistanceSquared(positions[v1], positions[v3]);

        if (diag02Sq <= diag13Sq)
        {
            indices.Add(v0);
            indices.Add(v1);
            indices.Add(v2);

            indices.Add(v0);
            indices.Add(v2);
            indices.Add(v3);
        }
        else
        {
            indices.Add(v0);
            indices.Add(v1);
            indices.Add(v3);

            indices.Add(v1);
            indices.Add(v2);
            indices.Add(v3);
        }
    }

    /// <summary>
    /// Ensures the binary padded voxel grid is 3D well-composed (Latecki 1995) by resolving
    /// 2D face diagonal saddles and 3D corner-only kissing configurations inside [1..nx, 1..ny, 1..nz].
    /// </summary>
    private static void MakeWellComposed(
        bool[] solid,
        float[] rho,
        float threshold,
        int nx,
        int ny,
        int nz,
        int pxCount,
        int pStrideY,
        int pStrideZ)
    {
        bool changed = true;
        int maxPasses = 8;
        int pass = 0;

        while (changed && pass < maxPasses)
        {
            changed = false;
            pass++;

            // 1. Check 2x2 faces in XY, XZ, YZ planes within interior [1..nx, 1..ny, 1..nz]
            for (int pz = 1; pz <= nz; pz++)
            {
                for (int py = 1; py <= ny; py++)
                {
                    for (int px = 1; px <= nx; px++)
                    {
                        // XY 2x2 face
                        if (px < nx && py < ny)
                        {
                            int i00 = px + py * pStrideY + pz * pStrideZ;
                            int i10 = i00 + 1;
                            int i01 = i00 + pStrideY;
                            int i11 = i01 + 1;
                            if (Resolve2x2Saddle(solid, rho, threshold, i00, i10, i01, i11))
                                changed = true;
                        }

                        // XZ 2x2 face
                        if (px < nx && pz < nz)
                        {
                            int i00 = px + py * pStrideY + pz * pStrideZ;
                            int i10 = i00 + 1;
                            int i01 = i00 + pStrideZ;
                            int i11 = i01 + 1;
                            if (Resolve2x2Saddle(solid, rho, threshold, i00, i10, i01, i11))
                                changed = true;
                        }

                        // YZ 2x2 face
                        if (py < ny && pz < nz)
                        {
                            int i00 = px + py * pStrideY + pz * pStrideZ;
                            int i10 = i00 + pStrideY;
                            int i01 = i00 + pStrideZ;
                            int i11 = i01 + pStrideY;
                            if (Resolve2x2Saddle(solid, rho, threshold, i00, i10, i01, i11))
                                changed = true;
                        }
                    }
                }
            }

            // 2. Check 2x2x2 opposite-corner pinch configurations
            for (int pz = 1; pz < nz; pz++)
            {
                for (int py = 1; py < ny; py++)
                {
                    for (int px = 1; px < nx; px++)
                    {
                        int baseIdx = px + py * pStrideY + pz * pStrideZ;
                        int c000 = baseIdx;
                        int c100 = baseIdx + 1;
                        int c010 = baseIdx + pStrideY;
                        int c110 = baseIdx + pStrideY + 1;
                        int c001 = baseIdx + pStrideZ;
                        int c101 = baseIdx + pStrideZ + 1;
                        int c011 = baseIdx + pStrideZ + pStrideY;
                        int c111 = baseIdx + pStrideZ + pStrideY + 1;

                        if (Resolve3DCornerKiss(solid, rho, threshold, c000, c111, c100, c010, c001, c110, c101, c011) ||
                            Resolve3DCornerKiss(solid, rho, threshold, c100, c011, c000, c110, c101, c010, c001, c111) ||
                            Resolve3DCornerKiss(solid, rho, threshold, c010, c101, c000, c110, c011, c100, c001, c111) ||
                            Resolve3DCornerKiss(solid, rho, threshold, c001, c110, c000, c101, c011, c100, c010, c111))
                        {
                            changed = true;
                        }
                    }
                }
            }
        }
    }

    private static bool Resolve2x2Saddle(bool[] solid, float[] rho, float threshold, int i00, int i10, int i01, int i11)
    {
        bool s00 = solid[i00], s10 = solid[i10], s01 = solid[i01], s11 = solid[i11];
        if (s00 == s11 && s10 == s01 && s00 != s10)
        {
            // Fill the higher-density zero voxel to bridge the diagonal
            int candA = s00 ? i10 : i00;
            int candB = s00 ? i01 : i11;
            int pick = rho[candA] >= rho[candB] ? candA : candB;
            solid[pick] = true;
            if (rho[pick] < threshold) rho[pick] = threshold;
            return true;
        }
        return false;
    }

    private static bool Resolve3DCornerKiss(
        bool[] solid,
        float[] rho,
        float threshold,
        int diagA,
        int diagB,
        int nA1,
        int nA2,
        int nA3,
        int nB1,
        int nB2,
        int nB3)
    {
        if (solid[diagA] && solid[diagB])
        {
            // Check if there is already a 6-connected solid path of length 3 between diagA and diagB inside the 2x2x2 cube
            bool connected =
                (solid[nA1] && (solid[nB1] || solid[nB2])) ||
                (solid[nA2] && (solid[nB1] || solid[nB3])) ||
                (solid[nA3] && (solid[nB2] || solid[nB3]));

            if (!connected)
            {
                solid[nA1] = true;
                solid[nB1] = true;
                if (rho[nA1] < threshold) rho[nA1] = threshold;
                if (rho[nB1] < threshold) rho[nB1] = threshold;
                return true;
            }
        }
        return false;
    }
}

