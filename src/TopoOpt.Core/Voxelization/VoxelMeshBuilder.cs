using System;
using System.Collections.Generic;
using System.Numerics;
using TopoOpt.Core.Geometry;

namespace TopoOpt.Core.Voxelization;

public static class VoxelMeshBuilder
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

    /// <summary>
    /// Builds an indexed triangle mesh containing the outer exposed quads (2 triangles per quad)
    /// of all active solid voxels in the domain.
    /// </summary>
    public static IndexedMesh BuildExposedMesh(VoxelModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var grid = model.Grid;
        int nx = grid.Nx;
        int ny = grid.Ny;
        int nz = grid.Nz;
        float h = grid.H;
        byte[] domain = model.Domain;

        List<Vector3> positions = new();
        List<int> indices = new();

        // Corner offsets for 6 face directions: 4 vertices per face with outward counter-clockwise winding
        for (int iz = 0; iz < nz; iz++)
        {
            for (int iy = 0; iy < ny; iy++)
            {
                for (int ix = 0; ix < nx; ix++)
                {
                    int elemIdx = grid.ElementIndex(ix, iy, iz);
                    if (domain[elemIdx] == 0)
                        continue;

                    Vector3 b000 = grid.Origin + new Vector3(ix * h, iy * h, iz * h);
                    Vector3 b100 = b000 + new Vector3(h, 0f, 0f);
                    Vector3 b010 = b000 + new Vector3(0f, h, 0f);
                    Vector3 b110 = b000 + new Vector3(h, h, 0f);
                    Vector3 b001 = b000 + new Vector3(0f, 0f, h);
                    Vector3 b101 = b000 + new Vector3(h, 0f, h);
                    Vector3 b011 = b000 + new Vector3(0f, h, h);
                    Vector3 b111 = b000 + new Vector3(h, h, h);

                    // 0: -X face (b000, b001, b011, b010)
                    if (ix == 0 || domain[(ix - 1) + nx * (iy + ny * iz)] == 0)
                    {
                        AddQuad(positions, indices, b000, b001, b011, b010);
                    }

                    // 1: +X face (b100, b110, b111, b101)
                    if (ix == nx - 1 || domain[(ix + 1) + nx * (iy + ny * iz)] == 0)
                    {
                        AddQuad(positions, indices, b100, b110, b111, b101);
                    }

                    // 2: -Y face (b000, b100, b101, b001)
                    if (iy == 0 || domain[ix + nx * ((iy - 1) + ny * iz)] == 0)
                    {
                        AddQuad(positions, indices, b000, b100, b101, b001);
                    }

                    // 3: +Y face (b010, b011, b111, b110)
                    if (iy == ny - 1 || domain[ix + nx * ((iy + 1) + ny * iz)] == 0)
                    {
                        AddQuad(positions, indices, b010, b011, b111, b110);
                    }

                    // 4: -Z face (b000, b010, b110, b100)
                    if (iz == 0 || domain[ix + nx * (iy + ny * (iz - 1))] == 0)
                    {
                        AddQuad(positions, indices, b000, b010, b110, b100);
                    }

                    // 5: +Z face (b001, b101, b111, b011)
                    if (iz == nz - 1 || domain[ix + nx * (iy + ny * (iz + 1))] == 0)
                    {
                        AddQuad(positions, indices, b001, b101, b111, b011);
                    }
                }
            }
        }

        return new IndexedMesh(positions.ToArray(), indices.ToArray());
    }

    private static void AddQuad(List<Vector3> pos, List<int> ind, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3)
    {
        int baseIdx = pos.Count;
        pos.Add(v0);
        pos.Add(v1);
        pos.Add(v2);
        pos.Add(v3);

        // Tri 1: v0, v1, v2
        ind.Add(baseIdx);
        ind.Add(baseIdx + 1);
        ind.Add(baseIdx + 2);

        // Tri 2: v0, v2, v3
        ind.Add(baseIdx);
        ind.Add(baseIdx + 2);
        ind.Add(baseIdx + 3);
    }
}
