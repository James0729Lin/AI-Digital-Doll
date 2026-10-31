using System;
using Godot;

namespace DigitalDoll.Animals;

/// <summary>
/// Eyes for a model that comes without any: an iris with a pupil and a glint on each side
/// of the head, carried by the head bone. They blink, narrow when content, close in sleep
/// and open wide (pupils too) when startled or playing.
/// </summary>
public sealed class ModelAnimalEyes
{
    private readonly Node3D[] _eyes = new Node3D[2];
    private readonly Node3D[] _pupils = new Node3D[2];
    private readonly StandardMaterial3D _iris = new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };
    private readonly float _pupilWidth;
    private float _open = 1f, _wide, _dilate;

    /// <param name="head">Follows the head; its units are metres, Z ahead and X to the animal's left.</param>
    /// <param name="at">The left eye from the head's origin (the right one mirrors it).</param>
    /// <param name="radius">The eye's half width in metres.</param>
    /// <param name="pupilWidth">The pupil's width: a slit for a cat, nearly the full height for a dog.</param>
    public ModelAnimalEyes(Node3D head, Vector3 at, float radius, float pupilWidth, float turn)
    {
        _pupilWidth = pupilWidth;
        var dark = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(0.05f, 0.04f, 0.05f) };
        var glint = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = Colors.White };
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? 1f : -1f;
            var eye = new Node3D
            {
                Name = i == 0 ? "EyeLeft" : "EyeRight",
                Position = new Vector3(side * at.X, at.Y, at.Z),
                // Turned outward and tipped up with the slope of the forehead.
                Rotation = new Vector3(-0.35f, side * turn, 0f),
            };
            head.AddChild(eye);
            // A dark rim, the iris, then the pupil and glint just in front of it.
            eye.AddChild(Disc(radius * 1.14f, radius * 0.34f, 0f, dark));
            eye.AddChild(Disc(radius, radius * 0.4f, 0f, _iris));
            // Held by a node of its own, whose width changes as it dilates.
            var pupil = new Node3D();
            pupil.AddChild(Disc(radius * 0.92f, radius * 0.45f, 0f, dark));
            eye.AddChild(pupil);
            var spark = Disc(radius * 0.22f, radius * 0.5f, radius * 0.08f, glint);
            spark.Position += new Vector3(-side * radius * 0.28f, radius * 0.35f, 0f);
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
        float grow = 1f + 0.18f * _wide;
        for (int i = 0; i < 2; i++)
        {
            _eyes[i].Scale = new Vector3(grow, grow * Mathf.Max(_open, 0.07f), 1f);
            _pupils[i].Scale = new Vector3(Mathf.Lerp(_pupilWidth, 0.85f, _dilate), 1f, 1f);
        }
    }

    /// <summary>A flattened ball facing ahead, pushed forward by <paramref name="forward"/>.</summary>
    private static MeshInstance3D Disc(float radius, float depth, float forward, Material material) => new()
    {
        Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 20, Rings = 10 },
        MaterialOverride = material,
        Position = new Vector3(0f, 0f, forward),
        Basis = Basis.Identity.Scaled(new Vector3(radius, radius * 1.1f, depth)),
    };
}
