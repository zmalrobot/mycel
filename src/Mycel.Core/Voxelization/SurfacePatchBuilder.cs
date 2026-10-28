using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Mycel.Core.Geometry;

namespace Mycel.Core.Voxelization;

public static class SurfacePatchBuilder
{
    private static readonly Vector3[] FaceNormals =
    {
        new(-1f, 0f, 0f), // 0: -X
        new( 1f, 0f, 0f), // 1: +X
        new(0f, -1f, 0f), // 2: -Y
        new(0f,  1f, 0f), // 3: +Y
        new(0f, 0f, -1f), // 4: -Z
        new(0f, 0f,  1f)  // 5: +Z
    };

    public static SurfacePatch Build(
        VoxelModel model,
        IndexedMesh mesh,
        int[]? selectedTriangles,
        double tau = 1.0,
        double minNormalDot = 0.5)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(mesh);

        if (selectedTriangles is null || selectedTriangles.Length == 0)
        {
            return new SurfacePatch(
                Array.Empty<VoxelFace>(),
                Array.Empty<int>(),
                Array.Empty<double>(),
                (Vector3.Zero, Vector3.Zero),
                false);
        }

        var grid = model.Grid;
        int nx = grid.Nx;
        int ny = grid.Ny;
        int nz = grid.Nz;
        float h = grid.H;
        byte[] domain = model.Domain;

        var subBvh = new Bvh(mesh, selectedTriangles);
        float maxDist = (float)(tau * h);
        float maxDistSq = maxDist * maxDist;
        float minDot = (float)minNormalDot;

        List<VoxelFace> acceptedFaces = new();
        Dictionary<int, double> nodeAreaMap = new();

        double faceArea = h * h;
        double tributaryAreaPerNode = faceArea * 0.25;

        // Extract triangle normals for fast lookup
        ReadOnlySpan<Vector3> triNormals = mesh.TriangleNormals.Span;

        for (int iz = 0; iz < nz; iz++)
        {
            for (int iy = 0; iy < ny; iy++)
            {
                for (int ix = 0; ix < nx; ix++)
                {
                    int elemIdx = grid.ElementIndex(ix, iy, iz);
                    if (domain[elemIdx] == 0)
                        continue;

                    // Test all 6 faces for exposure (neighbour is void or out of bounds)
                    for (int dir = 0; dir < 6; dir++)
                    {
                        if (!IsFaceExposed(domain, nx, ny, nz, ix, iy, iz, dir))
                            continue;

                        Vector3 faceCenter = GetFaceCenter(grid, ix, iy, iz, dir);
                        Vector3 faceNormal = FaceNormals[dir];

                        var hit = subBvh.ClosestPoint(faceCenter);
                        if (hit.DistanceSquared > maxDistSq)
                            continue;

                        Vector3 triNormal = triNormals[hit.TriangleIndex];
                        float dot = Vector3.Dot(faceNormal, triNormal);
                        if (dot < minDot)
                            continue;

                        // Face accepted
                        acceptedFaces.Add(new VoxelFace(elemIdx, (FaceDirection)dir, faceCenter, faceNormal));

                        // 4 corner nodes of the face
                        GetFaceNodes(grid, ix, iy, iz, dir, out int n0, out int n1, out int n2, out int n3);

                        AccumulateNodeArea(nodeAreaMap, n0, tributaryAreaPerNode);
                        AccumulateNodeArea(nodeAreaMap, n1, tributaryAreaPerNode);
                        AccumulateNodeArea(nodeAreaMap, n2, tributaryAreaPerNode);
                        AccumulateNodeArea(nodeAreaMap, n3, tributaryAreaPerNode);
                    }
                }
            }
        }

        if (acceptedFaces.Count == 0)
        {
            return new SurfacePatch(
                Array.Empty<VoxelFace>(),
                Array.Empty<int>(),
                Array.Empty<double>(),
                (Vector3.Zero, Vector3.Zero),
                false);
        }

        // Sort nodes for deterministic ordering
        int[] sortedNodes = nodeAreaMap.Keys.OrderBy(k => k).ToArray();
        double[] nodeAreas = new double[sortedNodes.Length];

        Vector3 minBound = new(float.MaxValue);
        Vector3 maxBound = new(float.MinValue);

        for (int i = 0; i < sortedNodes.Length; i++)
        {
            int node = sortedNodes[i];
            nodeAreas[i] = nodeAreaMap[node];

            grid.NodeCoords(node, out int ni, out int nj, out int nk);
            Vector3 pos = grid.NodePosition(ni, nj, nk);

            minBound = Vector3.Min(minBound, pos);
            maxBound = Vector3.Max(maxBound, pos);
        }

        var bbox = (minBound, maxBound);
        bool touchesLargestComponent = model.SolidCount > 0 && acceptedFaces.Count > 0;

        return new SurfacePatch(
            acceptedFaces,
            sortedNodes,
            nodeAreas,
            bbox,
            touchesLargestComponent);
    }

    private static void AccumulateNodeArea(Dictionary<int, double> map, int node, double delta)
    {
        if (map.TryGetValue(node, out double existing))
            map[node] = existing + delta;
        else
            map[node] = delta;
    }

    private static bool IsFaceExposed(byte[] domain, int nx, int ny, int nz, int ix, int iy, int iz, int dir)
    {
        return dir switch
        {
            0 => ix == 0 || domain[(ix - 1) + nx * (iy + ny * iz)] == 0,      // -X
            1 => ix == nx - 1 || domain[(ix + 1) + nx * (iy + ny * iz)] == 0,  // +X
            2 => iy == 0 || domain[ix + nx * ((iy - 1) + ny * iz)] == 0,      // -Y
            3 => iy == ny - 1 || domain[ix + nx * ((iy + 1) + ny * iz)] == 0,  // +Y
            4 => iz == 0 || domain[ix + nx * (iy + ny * (iz - 1))] == 0,      // -Z
            5 => iz == nz - 1 || domain[ix + nx * (iy + ny * (iz + 1))] == 0,  // +Z
            _ => false
        };
    }

    private static Vector3 GetFaceCenter(GridSpec grid, int ix, int iy, int iz, int dir)
    {
        Vector3 origin = grid.Origin;
        float h = grid.H;

        return dir switch
        {
            0 => origin + new Vector3(ix * h, (iy + 0.5f) * h, (iz + 0.5f) * h),       // -X
            1 => origin + new Vector3((ix + 1) * h, (iy + 0.5f) * h, (iz + 0.5f) * h), // +X
            2 => origin + new Vector3((ix + 0.5f) * h, iy * h, (iz + 0.5f) * h),       // -Y
            3 => origin + new Vector3((ix + 0.5f) * h, (iy + 1) * h, (iz + 0.5f) * h), // +Y
            4 => origin + new Vector3((ix + 0.5f) * h, (iy + 0.5f) * h, iz * h),       // -Z
            5 => origin + new Vector3((ix + 0.5f) * h, (iy + 0.5f) * h, (iz + 1) * h), // +Z
            _ => Vector3.Zero
        };
    }

    private static void GetFaceNodes(GridSpec grid, int ix, int iy, int iz, int dir, out int n0, out int n1, out int n2, out int n3)
    {
        switch (dir)
        {
            case 0: // -X (i = ix)
                n0 = (int)grid.NodeIndex(ix, iy, iz);
                n1 = (int)grid.NodeIndex(ix, iy + 1, iz);
                n2 = (int)grid.NodeIndex(ix, iy + 1, iz + 1);
                n3 = (int)grid.NodeIndex(ix, iy, iz + 1);
                break;
            case 1: // +X (i = ix + 1)
                n0 = (int)grid.NodeIndex(ix + 1, iy, iz);
                n1 = (int)grid.NodeIndex(ix + 1, iy + 1, iz);
                n2 = (int)grid.NodeIndex(ix + 1, iy + 1, iz + 1);
                n3 = (int)grid.NodeIndex(ix + 1, iy, iz + 1);
                break;
            case 2: // -Y (j = iy)
                n0 = (int)grid.NodeIndex(ix, iy, iz);
                n1 = (int)grid.NodeIndex(ix + 1, iy, iz);
                n2 = (int)grid.NodeIndex(ix + 1, iy, iz + 1);
                n3 = (int)grid.NodeIndex(ix, iy, iz + 1);
                break;
            case 3: // +Y (j = iy + 1)
                n0 = (int)grid.NodeIndex(ix, iy + 1, iz);
                n1 = (int)grid.NodeIndex(ix + 1, iy + 1, iz);
                n2 = (int)grid.NodeIndex(ix + 1, iy + 1, iz + 1);
                n3 = (int)grid.NodeIndex(ix, iy + 1, iz + 1);
                break;
            case 4: // -Z (k = iz)
                n0 = (int)grid.NodeIndex(ix, iy, iz);
                n1 = (int)grid.NodeIndex(ix + 1, iy, iz);
                n2 = (int)grid.NodeIndex(ix + 1, iy + 1, iz);
                n3 = (int)grid.NodeIndex(ix, iy + 1, iz);
                break;
            case 5: // +Z (k = iz + 1)
                n0 = (int)grid.NodeIndex(ix, iy, iz + 1);
                n1 = (int)grid.NodeIndex(ix + 1, iy, iz + 1);
                n2 = (int)grid.NodeIndex(ix + 1, iy + 1, iz + 1);
                n3 = (int)grid.NodeIndex(ix, iy + 1, iz + 1);
                break;
            default:
                n0 = n1 = n2 = n3 = 0;
                break;
        }
    }
}
