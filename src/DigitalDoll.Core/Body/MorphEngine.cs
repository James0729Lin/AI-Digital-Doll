using System;
using System.Collections.Generic;

namespace DigitalDoll.Core.Body;

/// <summary>Applies weighted targets to the base mesh and derives what the editor needs from it.</summary>
public sealed class MorphEngine
{
    private readonly BodyPack _pack;
    private readonly int[] _bodyVertices;

    public MorphEngine(BodyPack pack)
    {
        _pack = pack;
        var used = new HashSet<int>(pack.Surfaces["body"]);
        _bodyVertices = new int[used.Count];
        used.CopyTo(_bodyVertices);
        Array.Sort(_bodyVertices);
    }

    public BodyPack Pack => _pack;

    /// <summary>Targets referenced by the weights but missing from the pack (should be empty).</summary>
    public List<string> MissingTargets(IReadOnlyDictionary<string, float> weights)
    {
        var missing = new List<string>();
        foreach (var name in weights.Keys)
        {
            if (!_pack.Targets.ContainsKey(name))
                missing.Add(name);
        }
        return missing;
    }

    /// <summary>Writes the morphed positions (x, y, z interleaved) into <paramref name="positions"/>.</summary>
    public void Apply(IReadOnlyDictionary<string, float> weights, float[] positions)
    {
        if (positions.Length != _pack.BasePositions.Length)
            throw new ArgumentException("Position buffer has the wrong size", nameof(positions));
        Array.Copy(_pack.BasePositions, positions, positions.Length);

        foreach (var (name, weight) in weights)
        {
            if (!_pack.Targets.TryGetValue(name, out var target))
                continue;
            var indices = target.Indices;
            var deltas = target.Deltas;
            for (int k = 0; k < indices.Length; k++)
            {
                int v = indices[k] * 3;
                positions[v] += deltas[k * 3] * weight;
                positions[v + 1] += deltas[k * 3 + 1] * weight;
                positions[v + 2] += deltas[k * 3 + 2] * weight;
            }
        }
    }

    public float[] Apply(BodyShape shape)
    {
        var positions = new float[_pack.BasePositions.Length];
        Apply(MorphWeights.Compute(shape), positions);
        return positions;
    }

    /// <summary>Lowest and highest y of the visible body (feet to crown).</summary>
    public (float Bottom, float Top) VerticalExtent(float[] positions)
    {
        float bottom = float.MaxValue, top = float.MinValue;
        foreach (int v in _bodyVertices)
        {
            float y = positions[v * 3 + 1];
            bottom = Math.Min(bottom, y);
            top = Math.Max(top, y);
        }
        return (bottom, top);
    }

    /// <summary>Joint position of every bone (x, y, z interleaved, in <see cref="BodyPack.Bones"/> order).</summary>
    public float[] BoneHeads(float[] positions)
    {
        var heads = new float[_pack.Bones.Count * 3];
        for (int b = 0; b < _pack.Bones.Count; b++)
        {
            var vertices = _pack.Bones[b].HeadVertices;
            foreach (int v in vertices)
            {
                heads[b * 3] += positions[v * 3];
                heads[b * 3 + 1] += positions[v * 3 + 1];
                heads[b * 3 + 2] += positions[v * 3 + 2];
            }
            for (int axis = 0; axis < 3; axis++)
                heads[b * 3 + axis] /= vertices.Length;
        }
        return heads;
    }

    /// <summary>Centre of a joint helper group (for example "pelvis" or "l-knee").</summary>
    public (float X, float Y, float Z) JointCentre(string joint, float[] positions)
    {
        var group = _pack.Groups[joint];
        float x = 0, y = 0, z = 0;
        foreach (int v in group)
        {
            x += positions[v * 3];
            y += positions[v * 3 + 1];
            z += positions[v * 3 + 2];
        }
        return (x / group.Length, y / group.Length, z / group.Length);
    }
}
