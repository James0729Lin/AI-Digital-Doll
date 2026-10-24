using System;
using System.Collections.Generic;
using DigitalDoll.Core.Body;
using Godot;

namespace DigitalDoll.Body;

/// <summary>
/// Turns a <see cref="BodyShape"/> into mesh surfaces (body with underwear, eyes) with
/// smooth normals and, optionally, skin weights for the humanoid skeleton. The body
/// stands on y = 0.
/// </summary>
public sealed class BodyMeshBuilder
{
    private const string PackPath = "res://assets/body/body.ddbp.gz";
    private static BodyPack? _cachedPack;

    private readonly List<SurfaceData> _surfaces = new();
    private readonly float[] _positions;
    private readonly float[] _normals;
    private readonly ShaderMaterial _bodyMaterial;
    private readonly ShaderMaterial _eyes;
    private readonly ShaderMaterial _hair;
    private readonly ShaderMaterial _bodyOutline;
    private readonly ShaderMaterial _hairOutline;
    private readonly int _headBone;
    private readonly int _chestBone;

    public BodyMeshBuilder()
    {
        Engine = new MorphEngine(LoadPack());
        _positions = new float[Engine.Pack.BasePositions.Length];
        _normals = new float[_positions.Length];

        _bodyMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/body.gdshader") };
        var cut = UnderwearCut.Compute(Engine.Pack);
        _bodyMaterial.SetShaderParameter("top_low", cut.TopLow);
        _bodyMaterial.SetShaderParameter("top_high", cut.TopHigh);
        _bodyMaterial.SetShaderParameter("top_half_width", cut.TopHalfWidth);
        _bodyMaterial.SetShaderParameter("shorts_low", cut.ShortsLow);
        _bodyMaterial.SetShaderParameter("shorts_high", cut.ShortsHigh);
        _bodyMaterial.SetShaderParameter("shorts_half_width", cut.ShortsHalfWidth);
        _eyes = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/eye.gdshader") };
        _hair = new ShaderMaterial { Shader = GD.Load<Shader>("res://assets/shaders/hair.gdshader") };
        var outline = GD.Load<Shader>("res://assets/shaders/outline.gdshader");
        _bodyOutline = new ShaderMaterial { Shader = outline };
        _hairOutline = new ShaderMaterial { Shader = outline };

        // Face landmarks on the neutral base mesh, for the painted brows, lips and scalp.
        var pack = Engine.Pack;
        var (skullCentre, skullRadii) = HairGenerator.Skull(pack, pack.BasePositions);
        var (ex, ey, ez) = Engine.JointCentre("l-eye", pack.BasePositions);
        var (_, my, mz) = Engine.JointCentre("mouth", pack.BasePositions);
        _bodyMaterial.SetShaderParameter("head_centre", new Vector3(skullCentre.X, skullCentre.Y, skullCentre.Z));
        _bodyMaterial.SetShaderParameter("head_radii", new Vector3(skullRadii.X, skullRadii.Y, skullRadii.Z));
        _bodyMaterial.SetShaderParameter("hairline_front", HairGenerator.HairlineFront);
        _bodyMaterial.SetShaderParameter("hairline_side", HairGenerator.HairlineSide);
        _bodyMaterial.SetShaderParameter("hairline_back", HairGenerator.HairlineBack);
        _bodyMaterial.SetShaderParameter("eye_centre", new Vector3(ex, ey, ez));
        // MakeHuman's "mouth" joint sits 2.5 cm above the lip line (measured on the
        // base mesh's centre-line profile).
        _bodyMaterial.SetShaderParameter("mouth_centre", new Vector3(0f, my - 0.025f, mz));
        _bodyMaterial.SetShaderParameter("face_front", ez - 0.005f);

        _headBone = BoneIndex(pack, "Head");
        _chestBone = BoneIndex(pack, "UpperChest");

        // Where the body jiggles; the offsets are set by BodyJiggle on the desktop.
        var (bust, buttocks) = JiggleRegions.Find(Engine.Pack);
        foreach (var material in JiggleMaterials)
        {
            material.SetShaderParameter("bust_centre", ToGodot(bust.Centre));
            material.SetShaderParameter("bust_radii", ToGodot(bust.Radii));
            material.SetShaderParameter("buttocks_centre", ToGodot(buttocks.Centre));
            material.SetShaderParameter("buttocks_radii", ToGodot(buttocks.Radii));
        }

        foreach (var name in new[] { "body", "eyes" })
        {
            if (Engine.Pack.Surfaces.TryGetValue(name, out var tris))
                _surfaces.Add(new SurfaceData(name, tris, Engine.Pack));
        }
    }

    public MorphEngine Engine { get; }

    /// <summary>The body and its outline: both move the jiggling regions.</summary>
    public ShaderMaterial[] JiggleMaterials => new[] { _bodyMaterial, _bodyOutline };

    private static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>Height of the last built body in metres (feet to crown).</summary>
    public float BodyHeight { get; private set; }

    /// <summary>Morphed positions of the last build (x, y, z interleaved, feet on y = 0).</summary>
    public float[] Positions => _positions;

    public static BodyPack LoadPack()
    {
        if (_cachedPack != null)
            return _cachedPack;
        var bytes = FileAccess.GetFileAsBytes(PackPath);
        if (bytes.Length == 0)
            throw new InvalidOperationException($"Body data missing: {PackPath} ({FileAccess.GetOpenError()})");
        using var stream = new System.IO.MemoryStream(bytes);
        _cachedPack = BodyPack.Load(stream);
        return _cachedPack;
    }

    /// <summary>Morphs the body and refills <paramref name="mesh"/>.</summary>
    public void Build(BodyShape shape, ArrayMesh mesh, bool skinned)
    {
        Engine.Apply(MorphWeights.Compute(shape), _positions);
        var (bottom, top) = Engine.VerticalExtent(_positions);
        BodyHeight = top - bottom;
        for (int i = 1; i < _positions.Length; i += 3)
            _positions[i] -= bottom;

        ComputeNormals();
        _bodyMaterial.SetShaderParameter("skin_color", Color.FromHtml(shape.SkinColor));
        _bodyMaterial.SetShaderParameter("underwear_color", Color.FromHtml(shape.UnderwearColor));
        _bodyMaterial.SetShaderParameter("wears_top", UnderwearCut.WearsTop(shape));

        bool toon = shape.IsAnime;
        var hairColour = Color.FromHtml(shape.HairColor);
        var skinColour = Color.FromHtml(shape.SkinColor);
        _bodyMaterial.SetShaderParameter("toon", toon);
        _bodyMaterial.SetShaderParameter("hair_color", hairColour);
        _bodyMaterial.SetShaderParameter("scalp_on", HairCatalog.ById[shape.Hair].HasCap);
        _eyes.SetShaderParameter("toon", toon);
        _eyes.SetShaderParameter("iris_color", Color.FromHtml(shape.EyeColor));
        _hair.SetShaderParameter("toon", toon);
        _hair.SetShaderParameter("hair_color", hairColour);
        // Anime style: outlines a darker shade of the surface they surround.
        _bodyOutline.SetShaderParameter("outline_color", skinColour.Darkened(0.55f));
        _hairOutline.SetShaderParameter("outline_color", hairColour.Darkened(0.5f));
        _bodyMaterial.NextPass = toon ? _bodyOutline : null;
        _hair.NextPass = toon ? _hairOutline : null;

        mesh.ClearSurfaces();
        foreach (var surface in _surfaces)
        {
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surface.Build(_positions, _normals, skinned));
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, surface.Name == "eyes" ? _eyes : _bodyMaterial);
        }

        var hair = HairGenerator.Generate(Engine.Pack, Engine, _positions, _normals, shape);
        if (!hair.IsEmpty)
        {
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, HairArrays(hair, skinned));
            mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, _hair);
        }
    }

    private Godot.Collections.Array HairArrays(HairMesh hair, bool skinned)
    {
        int count = hair.Positions.Count;
        var vertices = new Vector3[count];
        var normals = new Vector3[count];
        var uvs = new Vector2[count];
        for (int i = 0; i < count; i++)
        {
            var p = hair.Positions[i];
            var n = hair.Normals[i];
            vertices[i] = new Vector3(p.X, p.Y, p.Z);
            normals[i] = new Vector3(n.X, n.Y, n.Z);
            uvs[i] = new Vector2(hair.Uvs[i].X, hair.Uvs[i].Y);
        }
        var indices = new int[hair.Triangles.Count];
        for (int i = 0; i < indices.Length; i += 3)
        {
            // Same winding flip as the body (MakeHuman counter-clockwise -> Godot clockwise).
            indices[i] = hair.Triangles[i + 2];
            indices[i + 1] = hair.Triangles[i + 1];
            indices[i + 2] = hair.Triangles[i];
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        if (skinned)
        {
            var bones = new int[count * 4];
            var weights = new float[count * 4];
            for (int i = 0; i < count; i++)
            {
                bones[i * 4] = _headBone;
                bones[i * 4 + 1] = _chestBone;
                weights[i * 4] = hair.HeadWeights[i];
                weights[i * 4 + 1] = 1f - hair.HeadWeights[i];
            }
            arrays[(int)Mesh.ArrayType.Bones] = bones;
            arrays[(int)Mesh.ArrayType.Weights] = weights;
        }
        return arrays;
    }

    private static int BoneIndex(BodyPack pack, string name)
    {
        for (int i = 0; i < pack.Bones.Count; i++)
        {
            if (pack.Bones[i].Name == name)
                return i;
        }
        return 0;
    }

    private void ComputeNormals()
    {
        Array.Clear(_normals);
        foreach (var surface in _surfaces)
        {
            var tris = surface.Triangles;
            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = tris[t] * 3, b = tris[t + 1] * 3, c = tris[t + 2] * 3;
                float abx = _positions[b] - _positions[a], aby = _positions[b + 1] - _positions[a + 1], abz = _positions[b + 2] - _positions[a + 2];
                float acx = _positions[c] - _positions[a], acy = _positions[c + 1] - _positions[a + 1], acz = _positions[c + 2] - _positions[a + 2];
                // Area-weighted face normal.
                float nx = aby * acz - abz * acy, ny = abz * acx - abx * acz, nz = abx * acy - aby * acx;
                Accumulate(a, nx, ny, nz);
                Accumulate(b, nx, ny, nz);
                Accumulate(c, nx, ny, nz);
            }
        }
        for (int v = 0; v < _normals.Length; v += 3)
        {
            float len = MathF.Sqrt(_normals[v] * _normals[v] + _normals[v + 1] * _normals[v + 1] + _normals[v + 2] * _normals[v + 2]);
            if (len > 0f)
            {
                _normals[v] /= len;
                _normals[v + 1] /= len;
                _normals[v + 2] /= len;
            }
        }
    }

    private void Accumulate(int v, float x, float y, float z)
    {
        _normals[v] += x;
        _normals[v + 1] += y;
        _normals[v + 2] += z;
    }

    /// <summary>A surface keeps only the vertices it uses, remapped to local indices.</summary>
    private sealed class SurfaceData
    {
        private readonly int[] _globalVertices;
        private readonly int[] _localIndices;
        private readonly Vector3[] _vertexBuffer;
        private readonly Vector3[] _normalBuffer;
        private readonly Vector2[] _restBuffer;
        private readonly Vector2[] _restDepthBuffer;
        private readonly int[] _bones;
        private readonly float[] _weights;

        public SurfaceData(string name, int[] triangles, BodyPack pack)
        {
            Name = name;
            Triangles = triangles;
            var map = new Dictionary<int, int>();
            var vertices = new List<int>();
            _localIndices = new int[triangles.Length];
            for (int i = 0; i < triangles.Length; i++)
            {
                if (!map.TryGetValue(triangles[i], out int local))
                {
                    local = vertices.Count;
                    map[triangles[i]] = local;
                    vertices.Add(triangles[i]);
                }
                // Godot expects clockwise front faces; MakeHuman is counter-clockwise.
                _localIndices[i - i % 3 + (2 - i % 3)] = local;
            }
            _globalVertices = vertices.ToArray();
            _vertexBuffer = new Vector3[_globalVertices.Length];
            _normalBuffer = new Vector3[_globalVertices.Length];
            _restBuffer = new Vector2[_globalVertices.Length];
            _restDepthBuffer = new Vector2[_globalVertices.Length];
            _bones = new int[_globalVertices.Length * 4];
            _weights = new float[_globalVertices.Length * 4];
            var rest = pack.BasePositions;
            for (int i = 0; i < _globalVertices.Length; i++)
            {
                int g = _globalVertices[i];
                _restBuffer[i] = new Vector2(rest[g * 3], rest[g * 3 + 1]);
                _restDepthBuffer[i] = new Vector2(rest[g * 3 + 2], 0f);
                for (int k = 0; k < 4; k++)
                {
                    _bones[i * 4 + k] = pack.BoneIndices[g * 4 + k];
                    _weights[i * 4 + k] = pack.BoneWeights[g * 4 + k] / 255f;
                }
            }
        }

        public string Name { get; }
        public int[] Triangles { get; }

        public Godot.Collections.Array Build(float[] positions, float[] normals, bool skinned)
        {
            for (int i = 0; i < _globalVertices.Length; i++)
            {
                int g = _globalVertices[i] * 3;
                _vertexBuffer[i] = new Vector3(positions[g], positions[g + 1], positions[g + 2]);
                _normalBuffer[i] = new Vector3(normals[g], normals[g + 1], normals[g + 2]);
            }
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = _vertexBuffer;
            arrays[(int)Mesh.ArrayType.Normal] = _normalBuffer;
            arrays[(int)Mesh.ArrayType.TexUV] = _restDepthBuffer;
            arrays[(int)Mesh.ArrayType.TexUV2] = _restBuffer;
            arrays[(int)Mesh.ArrayType.Index] = _localIndices;
            if (skinned)
            {
                arrays[(int)Mesh.ArrayType.Bones] = _bones;
                arrays[(int)Mesh.ArrayType.Weights] = _weights;
            }
            return arrays;
        }
    }
}
