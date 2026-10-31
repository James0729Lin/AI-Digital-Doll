using System;
using System.Collections.Generic;
using Godot;

namespace DigitalDoll.Animals;

/// <summary>
/// Eyes for a model that comes without any, lying flush on its face and carried by the
/// head bone: a cat's are almond-shaped and slanted with a slit pupil, a dog's rounder.
/// They blink, narrow when content, close in sleep and open wide (pupils too) when
/// startled or playing.
/// </summary>
public sealed class ModelAnimalEyes
{
    /// <summary>Where the eyes go on the head, in metres from the head bone, and their shape.</summary>
    /// <param name="At">The left eye (the right mirrors it about <paramref name="CentreX"/>).</param>
    /// <param name="CentreX">The middle of the face, which may be a little off the bone.</param>
    /// <param name="Width">Half the eye's width.</param>
    /// <param name="Height">Half the eye's height.</param>
    /// <param name="PupilWidth">The pupil's half width at rest.</param>
    /// <param name="Turn">How far each eye faces outward.</param>
    /// <param name="Tip">How far it faces upward.</param>
    /// <param name="Slant">How much the outer corner is raised.</param>
    /// <param name="Almond">Pointed at the corners; otherwise an oval.</param>
    public sealed record Shape(Vector3 At, float CentreX, float Width, float Height, float PupilWidth, float Turn, float Tip, float Slant, bool Almond);

    private readonly Node3D[] _eyes = new Node3D[2];
    private readonly Node3D[] _pupils = new Node3D[2];
    private readonly StandardMaterial3D _iris = Flat(Colors.Gold);
    private readonly float _restPupil, _roundPupil;
    private float _open = 1f, _wide, _dilate;

    /// <param name="head">Follows the head; its units are metres, Z ahead and X to the animal's left.</param>
    public ModelAnimalEyes(Node3D head, Shape shape)
    {
        _restPupil = shape.PupilWidth / shape.Height;
        _roundPupil = 0.72f * shape.Width / shape.Height;
        var dark = Flat(new Color(0.05f, 0.04f, 0.05f));
        var glint = Flat(new Color(1f, 1f, 1f));
        float w = shape.Width, h = shape.Height, bulge = h * 0.45f;
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? 1f : -1f;
            var eye = new Node3D
            {
                Name = i == 0 ? "EyeLeft" : "EyeRight",
                Position = new Vector3(shape.CentreX + side * shape.At.X, shape.At.Y, shape.At.Z),
                Rotation = new Vector3(-shape.Tip, side * shape.Turn, side * shape.Slant),
            };
            head.AddChild(eye);
            // Each layer a little in front of the one under it: lash line, iris, pupil, glint.
            eye.AddChild(Patch(w * 1.14f, h * 1.2f, bulge, 0f, shape.Almond, dark));
            eye.AddChild(Patch(w, h, bulge, h * 0.06f, shape.Almond, _iris));
            var pupil = new Node3D { Position = new Vector3(0f, 0f, h * 0.12f) };
            // Upright, a point at the top and bottom like a cat's; its width is the node's scale.
            pupil.AddChild(new MeshInstance3D { Mesh = PatchMesh(h, h, bulge, shape.Almond), MaterialOverride = dark, Rotation = new Vector3(0f, 0f, Mathf.Pi / 2f) });
            eye.AddChild(pupil);
            var spark = Patch(h * 0.2f, h * 0.2f, h * 0.03f, bulge + h * 0.16f, false, glint);
            spark.Position += new Vector3(-side * w * 0.22f, h * 0.32f, 0f);
            eye.AddChild(spark);
            _eyes[i] = eye;
            _pupils[i] = pupil;
        }
    }

    public void SetColour(Color colour) => _iris.AlbedoColor = colour;

    /// <param name="open">0 closed … 1 open.</param>
    /// <param name="wide">How startled: the eyes grow.</param>
    /// <param name="dilate">How wide the pupils are, 0 at rest … 1 round.</param>
    public void Pose(float dt, float open, float wide, float dilate)
    {
        // Lids are quick, everything else eases.
        _open = Mathf.Lerp(_open, Mathf.Clamp(open, 0f, 1f), 1f - MathF.Exp(-30f * dt));
        _wide = Mathf.Lerp(_wide, Mathf.Clamp(wide, 0f, 1f), 1f - MathF.Exp(-12f * dt));
        _dilate = Mathf.Lerp(_dilate, Mathf.Clamp(dilate, 0f, 1f), 1f - MathF.Exp(-6f * dt));
        float grow = 1f + 0.12f * _wide;
        for (int i = 0; i < 2; i++)
        {
            _eyes[i].Scale = new Vector3(grow, grow * (1f + 0.25f * _wide) * Mathf.Max(_open, 0.08f), 1f);
            _pupils[i].Scale = new Vector3(Mathf.Lerp(_restPupil, _roundPupil, _dilate), 1f, 1f);
        }
    }

    private static MeshInstance3D Patch(float width, float height, float bulge, float forward, bool almond, Material material) => new()
    {
        Mesh = PatchMesh(width, height, bulge, almond),
        MaterialOverride = material,
        Position = new Vector3(0f, 0f, forward),
    };

    /// <summary>A shallow dome over an almond or oval outline, facing +Z.</summary>
    private static ArrayMesh PatchMesh(float width, float height, float bulge, bool almond)
    {
        const int Around = 28, Rings = 4;
        var vertices = new List<Vector3> { new(0f, 0f, bulge) };
        var indices = new List<int>();
        for (int ring = 1; ring <= Rings; ring++)
        {
            float r = (float)ring / Rings;
            for (int i = 0; i < Around; i++)
            {
                float t = Mathf.Tau * i / Around;
                float x = MathF.Cos(t), y = MathF.Sin(t);
                // An almond narrows toward its corners: height follows a parabola along the width.
                float outline = almond ? (1f - x * x) * MathF.Sign(y) : y;
                vertices.Add(new Vector3(width * r * x, height * r * outline, bulge * (1f - r * r)));
            }
        }
        for (int i = 0; i < Around; i++)
        {
            int next = (i + 1) % Around;
            indices.AddRange(new[] { 0, 1 + next, 1 + i });
            for (int ring = 0; ring < Rings - 1; ring++)
            {
                int a = 1 + ring * Around + i, b = 1 + ring * Around + next;
                int c = a + Around, d = b + Around;
                indices.AddRange(new[] { a, b, c, b, d, c });
            }
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private static StandardMaterial3D Flat(Color colour) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoColor = colour,
    };
}
