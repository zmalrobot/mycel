using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using TopoOpt.Core.Geometry;
using TopoOpt.Core.Tests.Fixtures;
using TopoOpt.Core.Voxelization;
using Xunit;

namespace TopoOpt.Core.Tests.Voxelization;

public sealed class VoxeliserTests
{
    [Fact]
    public void Voxelise_BoxAlignedWithGrid_MatchesExactVoxelCountAndVolume()
    {
        Vector3 size = new(10f, 12f, 14f);
        IndexedMesh box = CreateMesh(StlFixtures.Box(size));

        float h = 1.0f;
        GridSpec grid = GridSpec.FromVoxelSize(box, h, padding: 0);

        var (model, report) = Voxeliser.Voxelise(box, grid);

        int expectedVoxels = 10 * 12 * 14; // 1680
        double expectedVolume = size.X * size.Y * size.Z;

        Assert.Equal(expectedVoxels, model.SolidCount);
        Assert.Equal(expectedVolume, model.SolidVolume, 1e-4);
        Assert.Single(model.Components);
        Assert.Equal(expectedVoxels, model.Components[0].VoxelCount);
        Assert.Empty(model.Warnings);
    }

    [Fact]
    public void Voxelise_AxisAlignedBoxOnExactVoxelBoundaries_SolvesHardTieBreakExactly()
    {
        // Box of 6x8x10 mm with h = 1 mm, origin at (-3, -4, -5) so faces lie exactly on integer voxel borders
        Vector3 size = new(6f, 8f, 10f);
        IndexedMesh box = CreateMesh(StlFixtures.Box(size));

        GridSpec grid = new(new Vector3(-3f, -4f, -5f), 1.0f, 6, 8, 10);
        var (model, _) = Voxeliser.Voxelise(box, grid);

        Assert.Equal(480, model.SolidCount);
        Assert.Equal(480.0, model.SolidVolume, 1e-4);
    }

    [Fact]
    public void Voxelise_Sphere_VolumeErrorWithinOnePointFivePercent()
    {
        float r = 20.0f;
        float h = 1.0f;
        IndexedMesh sphere = CreateMesh(StlFixtures.Sphere(r, 64, 64));

        GridSpec grid = GridSpec.FromVoxelSize(sphere, h, padding: 1);
        var (model, _) = Voxeliser.Voxelise(sphere, grid);

        double analyticalSphereVol = (4.0 / 3.0) * Math.PI * r * r * r;
        double relativeError = Math.Abs(model.SolidVolume - analyticalSphereVol) / analyticalSphereVol;

        // Specified tolerance: volume error <= 1.5% for r = 20 mm, h = 1 mm
        Assert.True(relativeError <= 0.015,
            $"Sphere volume relative error {relativeError * 100:F2}% exceeds 1.5% limit. Actual: {model.SolidVolume}, Analytical: {analyticalSphereVol}");
    }

    [Fact]
    public void Voxelise_CylinderWithHole_MatchesAnalyticalVolumeWithinTwoPercent()
    {
        float rOut = 15.0f;
        float rIn = 6.0f;
        float height = 24.0f;
        int n = 48;
        float h = 0.5f; // Boundary layer error O(h / rOut) <= 0.5 / 15 = 3.3%

        IndexedMesh hollowCyl = CreateMesh(StlFixtures.Cylinder(rOut, height, n, rIn));
        GridSpec grid = GridSpec.FromVoxelSize(hollowCyl, h, padding: 1);

        var (model, _) = Voxeliser.Voxelise(hollowCyl, grid);

        double analyticalVol = 0.5 * n * (rOut * rOut - rIn * rIn) * Math.Sin(2.0 * Math.PI / n) * height;
        double relativeError = Math.Abs(model.SolidVolume - analyticalVol) / analyticalVol;

        Assert.True(relativeError <= 0.02,
            $"Cylinder with hole volume relative error {relativeError * 100:F2}% exceeds 2.0% limit. Actual: {model.SolidVolume}, Analytical: {analyticalVol}");
    }

    [Fact]
    public void Voxelise_LBracket_MatchesAnalyticalVolumeWithinTwoPercent()
    {
        float armL = 30f;
        float armW = 8f;
        float thickness = 10f;
        float h = 0.5f;

        IndexedMesh bracket = CreateMesh(StlFixtures.LBracket(armL, armW, thickness));
        GridSpec grid = GridSpec.FromVoxelSize(bracket, h, padding: 1);

        var (model, _) = Voxeliser.Voxelise(bracket, grid);

        double analyticalVol = (double)thickness * armW * (2.0 * armL - armW);
        double relativeError = Math.Abs(model.SolidVolume - analyticalVol) / analyticalVol;

        Assert.True(relativeError <= 0.02,
            $"L-Bracket volume relative error {relativeError * 100:F2}% exceeds 2.0% limit. Actual: {model.SolidVolume}, Analytical: {analyticalVol}");
    }

    [Fact]
    public void Voxelise_MeshRotatedByOddAngle_PreservesVolumeAndContainsNoInternalHoles()
    {
        Vector3 size = new(20f, 20f, 20f);
        List<Tri> boxTris = StlFixtures.Box(size);

        Quaternion rot = Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(0.6435f, -0.412f, 0.915f));
        Matrix4x4 m = Matrix4x4.CreateFromQuaternion(rot);
        List<Tri> rotTris = StlFixtures.Transform(boxTris, m);

        IndexedMesh rotBox = CreateMesh(rotTris);
        float h = 0.5f;
        GridSpec grid = GridSpec.FromVoxelSize(rotBox, h, padding: 1);

        var (model, _) = Voxeliser.Voxelise(rotBox, grid);

        double analyticalVol = size.X * size.Y * size.Z;
        double relativeError = Math.Abs(model.SolidVolume - analyticalVol) / analyticalVol;

        Assert.True(relativeError <= 0.02,
            $"Rotated box volume error {relativeError * 100:F2}% exceeds 2%.");

        // Connected component check: convex solid must have exactly 1 connected component (no holes)
        Assert.Single(model.Components);

        // Check for isolated internal voids: every void voxel must have a path to the boundary of the bounding box
        AssertNoEnclosedCavities(model);
    }

    [Fact]
    public void Voxelise_NonWatertightMesh_UsesThreeAxisVotingAndEmitsWarning()
    {
        List<Tri> openBoxTris = StlFixtures.RemoveTriangle(StlFixtures.Box(new Vector3(10f, 10f, 10f)), 0);
        IndexedMesh openBox = CreateMesh(openBoxTris);

        GridSpec grid = GridSpec.FromVoxelSize(openBox, 1.0f, padding: 1);
        var (model, _) = Voxeliser.Voxelise(openBox, grid, isWatertight: false);

        Assert.NotEmpty(model.Warnings);
        Assert.Contains(model.Warnings, w => w.Contains("voting", StringComparison.OrdinalIgnoreCase) || w.Contains("approximate", StringComparison.OrdinalIgnoreCase));

        // Volume should still approximate 1000 mm^3 within 5%
        double relativeError = Math.Abs(model.SolidVolume - 1000.0) / 1000.0;
        Assert.True(relativeError < 0.05, $"Non-watertight box volume {model.SolidVolume} deviated by {relativeError * 100:F2}%.");
    }

    [Fact]
    public void Voxelise_Determinism_TwoRunsProduceIdenticalDomainHash()
    {
        IndexedMesh cyl = CreateMesh(StlFixtures.Cylinder(12f, 20f, 32, 4f));
        GridSpec grid = GridSpec.FromVoxelSize(cyl, 0.75f, padding: 1);

        var (model1, _) = Voxeliser.Voxelise(cyl, grid);
        var (model2, _) = Voxeliser.Voxelise(cyl, grid);

        byte[] hash1 = SHA256.HashData(model1.Domain);
        byte[] hash2 = SHA256.HashData(model2.Domain);

        Assert.Equal(hash1, hash2);
        Assert.Equal(model1.SolidCount, model2.SolidCount);
    }

    [Fact]
    public void Voxelise_OverlappingBoxes_UnionHasNoInternalHole()
    {
        // Box 1: [-5, 5] x [-5, 5] x [-5, 5] (size 10x10x10)
        List<Tri> box1 = StlFixtures.Box(new Vector3(10f, 10f, 10f));
        // Box 2: [0, 10] x [-5, 5] x [-5, 5] (shifted by +5 along X)
        Vector3 shift = new(5f, 0f, 0f);
        List<Tri> box2 = StlFixtures.Box(new Vector3(10f, 10f, 10f))
            .ConvertAll(t => new Tri(t.A + shift, t.B + shift, t.C + shift));

        List<Tri> combined = new(box1.Count + box2.Count);
        combined.AddRange(box1);
        combined.AddRange(box2);

        IndexedMesh mesh = CreateMesh(combined);
        float h = 1.0f;
        GridSpec grid = GridSpec.FromVoxelSize(mesh, h, padding: 1);

        var (model, _) = Voxeliser.Voxelise(mesh, grid);

        // Expected union is 15 x 10 x 10 mm = 1500 mm^3 (1500 voxels with h = 1.0).
        // If parity (XOR) was used, the overlap [0, 5] would be empty (1000 voxels).
        Assert.Equal(1500, model.SolidCount);
        Assert.Equal(1500.0, model.SolidVolume, 1e-4);
        AssertNoEnclosedCavities(model);

        // Verify that the overlap region [0, 5] x [-5, 5] x [-5, 5] is solid throughout
        int overlapVoxelCount = 0;
        for (int iz = 0; iz < grid.Nz; iz++)
        {
            for (int iy = 0; iy < grid.Ny; iy++)
            {
                for (int ix = 0; ix < grid.Nx; ix++)
                {
                    Vector3 center = grid.ElementCenter(ix, iy, iz);
                    if (center.X > 0f && center.X < 5f &&
                        center.Y > -5f && center.Y < 5f &&
                        center.Z > -5f && center.Z < 5f)
                    {
                        int e = grid.ElementIndex(ix, iy, iz);
                        Assert.Equal(1, model.Domain[e]);
                        overlapVoxelCount++;
                    }
                }
            }
        }
        Assert.Equal(500, overlapVoxelCount);
    }

    private static void AssertNoEnclosedCavities(VoxelModel model)
    {
        GridSpec g = model.Grid;
        byte[] domain = model.Domain;
        int total = g.Nx * g.Ny * g.Nz;
        byte[] visited = new byte[total];

        Queue<(int X, int Y, int Z)> q = new();

        // Seed with all boundary void voxels
        for (int iz = 0; iz < g.Nz; iz++)
        {
            for (int iy = 0; iy < g.Ny; iy++)
            {
                for (int ix = 0; ix < g.Nx; ix++)
                {
                    bool isBoundary = (ix == 0 || ix == g.Nx - 1 || iy == 0 || iy == g.Ny - 1 || iz == 0 || iz == g.Nz - 1);
                    if (isBoundary)
                    {
                        int e = g.ElementIndex(ix, iy, iz);
                        if (domain[e] == 0 && visited[e] == 0)
                        {
                            visited[e] = 1;
                            q.Enqueue((ix, iy, iz));
                        }
                    }
                }
            }
        }

        // BFS through exterior void
        while (q.Count > 0)
        {
            var (cx, cy, cz) = q.Dequeue();
            ReadOnlySpan<(int dx, int dy, int dz)> neighbors =
            [
                (1, 0, 0), (-1, 0, 0),
                (0, 1, 0), (0, -1, 0),
                (0, 0, 1), (0, 0, -1)
            ];

            foreach (var (dx, dy, dz) in neighbors)
            {
                int nx = cx + dx;
                int ny = cy + dy;
                int nz = cz + dz;
                if ((uint)nx < (uint)g.Nx && (uint)ny < (uint)g.Ny && (uint)nz < (uint)g.Nz)
                {
                    int ne = g.ElementIndex(nx, ny, nz);
                    if (domain[ne] == 0 && visited[ne] == 0)
                    {
                        visited[ne] = 1;
                        q.Enqueue((nx, ny, nz));
                    }
                }
            }
        }

        // Check if any void voxel was unvisited (which would indicate an internal trapped cavity)
        int trappedVoids = 0;
        for (int i = 0; i < total; i++)
        {
            if (domain[i] == 0 && visited[i] == 0)
            {
                trappedVoids++;
            }
        }

        Assert.Equal(0, trappedVoids);
    }

    private static IndexedMesh CreateMesh(List<Tri> tris)
    {
        using MemoryStream ms = new();
        StlFixtures.WriteBinary(ms, tris);
        ms.Position = 0;
        return StlReader.Read(ms).Mesh;
    }
}

