using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Mycel.Core.Geometry;
using Mycel.Core.Tests.Fixtures;
using Xunit;

namespace Mycel.Core.Tests.Geometry;

public sealed class StlReaderWriterTests
{
    [Fact]
    public void ReadBinary_And_ReadAscii_RoundTrip_ProducesIdenticalGeometry()
    {
        List<Tri> bracket = StlFixtures.LBracket(30f, 8f, 6f);

        using MemoryStream binStream = new();
        StlFixtures.WriteBinary(binStream, bracket);
        binStream.Position = 0;

        using MemoryStream asciiStream = new();
        StlFixtures.WriteAscii(asciiStream, bracket);
        asciiStream.Position = 0;

        var (binMesh, binReport) = StlReader.Read(binStream);
        var (asciiMesh, asciiReport) = StlReader.Read(asciiStream);

        Assert.Equal(20, binMesh.TriangleCount);
        Assert.Equal(12, binMesh.VertexCount);
        Assert.True(binReport.IsWatertight);
        Assert.Equal(0, binReport.OpenEdges);
        Assert.Equal(0, binReport.NonManifoldEdges);

        Assert.Equal(binMesh.TriangleCount, asciiMesh.TriangleCount);
        Assert.Equal(binMesh.VertexCount, asciiMesh.VertexCount);
        Assert.True(asciiReport.IsWatertight);
        Assert.Equal(binMesh.SignedVolume, asciiMesh.SignedVolume, 1e-3);
        Assert.Equal(binMesh.SurfaceArea, asciiMesh.SurfaceArea, 1e-3);

        // Test StlWriter binary round-trip
        using MemoryStream outStream = new();
        StlWriter.WriteBinary(outStream, binMesh);
        outStream.Position = 0;

        var (reloadedMesh, reloadedReport) = StlReader.Read(outStream);
        Assert.Equal(binMesh.TriangleCount, reloadedMesh.TriangleCount);
        Assert.Equal(binMesh.VertexCount, reloadedMesh.VertexCount);
        Assert.True(reloadedReport.IsWatertight);
        Assert.Equal(binMesh.SignedVolume, reloadedMesh.SignedVolume, 1e-3);
    }

    [Fact]
    public void Read_BinaryWithSolidHeaderPrefix_DetectsAsBinaryUsingLengthCheck()
    {
        List<Tri> cyl = StlFixtures.Cylinder(12f, 25f, 32, 4f);

        using MemoryStream ms = new();
        StlFixtures.WriteBinary(ms, cyl, headerStartsWithSolid: true);
        ms.Position = 0;

        var (mesh, report) = StlReader.Read(ms);

        Assert.Equal(cyl.Count, mesh.TriangleCount);
        Assert.Equal(cyl.Count, report.Triangles);
        Assert.True(report.IsWatertight);
        Assert.Equal(0, report.OpenEdges);
        Assert.Equal(0, report.NonManifoldEdges);
    }

    [Fact]
    public void Read_Box_WeldsVerticesTo8AndReports28Merged()
    {
        Vector3 size = new(20f, 30f, 40f);
        List<Tri> box = StlFixtures.Box(size);

        using MemoryStream ms = new();
        StlFixtures.WriteBinary(ms, box);
        ms.Position = 0;

        var (mesh, report) = StlReader.Read(ms);

        Assert.Equal(12, mesh.TriangleCount);
        Assert.Equal(8, mesh.VertexCount);
        Assert.Equal(12, report.Triangles);
        Assert.Equal(8, report.Vertices);
        Assert.Equal(36 - 8, report.VerticesMerged);
        Assert.Equal(0, report.DegenerateSkipped);
        Assert.Equal(0, report.OpenEdges);
        Assert.Equal(0, report.NonManifoldEdges);
        Assert.True(report.IsWatertight);

        double expectedVolume = (double)size.X * size.Y * size.Z;
        Assert.True(Math.Abs(mesh.SignedVolume - expectedVolume) / expectedVolume < 1e-6);
        Assert.True(Math.Abs(report.Volume - expectedVolume) / expectedVolume < 1e-6);

        double expectedArea = 2.0 * (size.X * size.Y + size.Y * size.Z + size.Z * size.X);
        Assert.True(Math.Abs(mesh.SurfaceArea - expectedArea) / expectedArea < 1e-6);
    }

    [Fact]
    public void Read_Sphere_VolumeMatchesAnalyticalWithinTolerance()
    {
        float r = 15f;
        List<Tri> sphere = StlFixtures.Sphere(r, 64, 64);

        using MemoryStream ms = new();
        StlFixtures.WriteBinary(ms, sphere);
        ms.Position = 0;

        var (mesh, report) = StlReader.Read(ms);

        double trueSphere = (4.0 / 3.0) * Math.PI * r * r * r;
        double fixtureVolume = StlFixtures.SignedVolume(sphere);

        Assert.True(report.IsWatertight);
        Assert.True(Math.Abs(mesh.SignedVolume - fixtureVolume) / fixtureVolume < 1e-5);
        Assert.True(mesh.SignedVolume < trueSphere);
        Assert.True((trueSphere - mesh.SignedVolume) / trueSphere < 0.01);
    }

    [Fact]
    public void Read_BoxWithOneTriangleRemoved_ReportsThreeOpenEdgesAndNotWatertight()
    {
        List<Tri> box = StlFixtures.Box(new Vector3(10f, 10f, 10f));
        List<Tri> openBox = StlFixtures.RemoveTriangle(box, 0);

        using MemoryStream ms = new();
        StlFixtures.WriteBinary(ms, openBox);
        ms.Position = 0;

        var (mesh, report) = StlReader.Read(ms);

        Assert.Equal(11, mesh.TriangleCount);
        Assert.Equal(8, mesh.VertexCount);
        Assert.Equal(3, report.OpenEdges);
        Assert.Equal(0, report.NonManifoldEdges);
        Assert.False(report.IsWatertight);
    }

    [Fact]
    public void Read_SkipsAndCountsDegenerateTriangles()
    {
        List<Tri> box = StlFixtures.Box(new Vector3(10f, 10f, 10f));
        // Add 2 degenerate collinear/zero-area triangles
        box.Add(new Tri(new Vector3(1f, 1f, 1f), new Vector3(1f, 1f, 1f), new Vector3(2f, 2f, 2f)));
        box.Add(new Tri(new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(2f, 0f, 0f)));

        using MemoryStream ms = new();
        StlFixtures.WriteBinary(ms, box);
        ms.Position = 0;

        var (mesh, report) = StlReader.Read(ms);

        Assert.Equal(12, mesh.TriangleCount);
        Assert.Equal(2, report.DegenerateSkipped);
        Assert.True(report.IsWatertight);
    }
}

