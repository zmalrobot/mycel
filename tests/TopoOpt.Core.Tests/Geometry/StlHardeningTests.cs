using System;
using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using System.Text;
using TopoOpt.Core.Geometry;
using Xunit;

namespace TopoOpt.Core.Tests.Geometry;

public sealed class StlHardeningTests
{
    [Fact]
    public void Read_CorruptedTruncatedBinaryStl_ThrowsInvalidDataException()
    {
        // Declares 100 triangles (requires 84 + 5000 = 5084 bytes), but only has 200 bytes
        byte[] truncated = new byte[200];
        BinaryPrimitives.WriteUInt32LittleEndian(truncated.AsSpan(80, 4), 100);

        using MemoryStream ms = new(truncated);
        var ex = Assert.Throws<InvalidDataException>(() => StlReader.Read(ms));
        Assert.Contains("truncated", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_ZeroTrianglesBinary_ThrowsInvalidDataException()
    {
        byte[] zeroTri = new byte[84];
        BinaryPrimitives.WriteUInt32LittleEndian(zeroTri.AsSpan(80, 4), 0);

        using MemoryStream ms = new(zeroTri);
        var ex = Assert.Throws<InvalidDataException>(() => StlReader.Read(ms));
        Assert.Contains("0 triangles", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_ZeroTrianglesAscii_ThrowsInvalidDataException()
    {
        string emptyAscii = "solid empty\nendsolid empty\n";
        using MemoryStream ms = new(Encoding.ASCII.GetBytes(emptyAscii));

        var ex = Assert.Throws<InvalidDataException>(() => StlReader.Read(ms));
        Assert.Contains("0 valid triangles", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_BinaryStlExceedingMaxLimit_ThrowsInvalidDataException()
    {
        // 5 million triangles
        byte[] oversized = new byte[84];
        BinaryPrimitives.WriteUInt32LittleEndian(oversized.AsSpan(80, 4), 5_000_000);

        using MemoryStream ms = new(oversized);
        var ex = Assert.Throws<InvalidDataException>(() => StlReader.Read(ms));
        Assert.Contains("exceeding the maximum limit", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_BinaryStlWithNanCoordinates_ThrowsInvalidDataException()
    {
        // 1 triangle = 84 + 50 = 134 bytes
        byte[] data = new byte[134];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(80, 4), 1);

        // Put NaN into vertex A.X (byte 84 + 12 = 96)
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(96, 4), float.NaN);

        using MemoryStream ms = new(data);
        var ex = Assert.Throws<InvalidDataException>(() => StlReader.Read(ms));
        Assert.Contains("NaN or Infinite", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_AsciiStlWithInfinity_ThrowsInvalidDataException()
    {
        string infAscii = @"solid inf
  facet normal 0 0 1
    outer loop
      vertex 0 0 0
      vertex 10 Infinity 0
      vertex 0 10 0
    endloop
  endfacet
endsolid inf";

        using MemoryStream ms = new(Encoding.ASCII.GetBytes(infAscii));
        var ex = Assert.Throws<InvalidDataException>(() => StlReader.Read(ms));
        Assert.Contains("NaN or Infinite", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_AbsurdScale_ThrowsInvalidDataException()
    {
        // Bounding box diagonal > 1e7
        string hugeAscii = @"solid huge
  facet normal 0 0 1
    outer loop
      vertex 0 0 0
      vertex 20000000 0 0
      vertex 0 20000000 0
    endloop
  endfacet
endsolid huge";

        using MemoryStream ms = new(Encoding.ASCII.GetBytes(hugeAscii));
        var ex = Assert.Throws<InvalidDataException>(() => StlReader.Read(ms));
        Assert.Contains("absurd scale", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}

