using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace DigitalDoll.Core.Body;

/// <summary>A sparse set of vertex movements (one MakeHuman target).</summary>
public sealed class MorphTarget
{
    public MorphTarget(string name, int[] indices, float[] deltas)
    {
        Name = name;
        Indices = indices;
        Deltas = deltas;
    }

    public string Name { get; }
    public int[] Indices { get; }

    /// <summary>Interleaved x, y, z movement in metres for each entry of <see cref="Indices"/>.</summary>
    public float[] Deltas { get; }
}

/// <summary>
/// Base mesh and morph targets converted from the MakeHuman CC0 assets by
/// <c>tools/makehuman/convert.py</c> (see that file for the binary layout).
/// </summary>
public sealed class BodyPack
{
    private const uint Version = 1;

    private BodyPack(float[] positions, Dictionary<string, int[]> surfaces,
        Dictionary<string, int[]> groups, Dictionary<string, MorphTarget> targets)
    {
        BasePositions = positions;
        Surfaces = surfaces;
        Groups = groups;
        Targets = targets;
    }

    public int VertexCount => BasePositions.Length / 3;

    /// <summary>Interleaved x, y, z of the neutral body in metres.</summary>
    public float[] BasePositions { get; }

    /// <summary>Triangle index lists by material: body, eyes.</summary>
    public IReadOnlyDictionary<string, int[]> Surfaces { get; }

    /// <summary>Vertex groups marking joint centres (pelvis, l-knee…).</summary>
    public IReadOnlyDictionary<string, int[]> Groups { get; }

    public IReadOnlyDictionary<string, MorphTarget> Targets { get; }

    public static BodyPack Load(Stream gzipStream)
    {
        using var gzip = new GZipStream(gzipStream, CompressionMode.Decompress);
        using var buffer = new MemoryStream();
        gzip.CopyTo(buffer);
        buffer.Position = 0;
        using var reader = new BinaryReader(buffer);

        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "DDBP")
            throw new InvalidDataException("Not a body pack");
        uint version = reader.ReadUInt32();
        if (version != Version)
            throw new InvalidDataException($"Unsupported body pack version {version}");

        int vertexCount = (int)reader.ReadUInt32();
        var positions = ReadFloats(reader, vertexCount * 3);

        var surfaces = new Dictionary<string, int[]>();
        for (uint i = reader.ReadUInt32(); i > 0; i--)
        {
            string name = ReadString(reader);
            surfaces[name] = ReadIndices(reader, (int)reader.ReadUInt32());
        }

        var groups = new Dictionary<string, int[]>();
        for (uint i = reader.ReadUInt32(); i > 0; i--)
        {
            string name = ReadString(reader);
            groups[name] = ReadIndices(reader, (int)reader.ReadUInt32());
        }

        var targets = new Dictionary<string, MorphTarget>();
        for (uint i = reader.ReadUInt32(); i > 0; i--)
        {
            string name = ReadString(reader);
            int count = (int)reader.ReadUInt32();
            float scale = reader.ReadSingle();
            var indices = new int[count];
            int index = 0;
            for (int k = 0; k < count; k++)
            {
                index += reader.ReadUInt16();
                indices[k] = index;
            }
            var deltas = new float[count * 3];
            for (int axis = 0; axis < 3; axis++)
            {
                for (int k = 0; k < count; k++)
                    deltas[k * 3 + axis] = reader.ReadInt16() * scale;
            }
            targets[name] = new MorphTarget(name, indices, deltas);
        }

        return new BodyPack(positions, surfaces, groups, targets);
    }

    private static float[] ReadFloats(BinaryReader reader, int count)
    {
        var values = new float[count];
        for (int i = 0; i < count; i++)
            values[i] = reader.ReadSingle();
        return values;
    }

    private static int[] ReadIndices(BinaryReader reader, int count)
    {
        var values = new int[count];
        for (int i = 0; i < count; i++)
            values[i] = (int)reader.ReadUInt32();
        return values;
    }

    private static string ReadString(BinaryReader reader) =>
        Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadUInt16()));
}
