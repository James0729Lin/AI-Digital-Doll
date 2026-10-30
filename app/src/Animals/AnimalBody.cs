using System;
using System.Collections.Generic;
using System.Linq;
using DigitalDoll.Character;
using DigitalDoll.Core;
using DigitalDoll.Core.Acting;
using DigitalDoll.Core.Mascot;
using Godot;

namespace DigitalDoll.Animals;

/// <summary>An animal on the desktop: built by <see cref="AnimalBuilder"/>, moved by <see cref="AnimalAnimator"/>.</summary>
public sealed class AnimalBody : IMascotBody
{
    private static readonly Vector3[] Directions = BuildDirections();
    private static readonly Vector3[] Corners =
    {
        new(-1, -1, -1), new(1, -1, -1), new(-1, 1, -1), new(1, 1, -1),
        new(-1, -1, 1), new(1, -1, 1), new(-1, 1, 1), new(1, 1, 1),
    };

    private readonly AnimalRig _rig;
    private readonly AnimalAnimator _animator;
    private readonly List<Vector2> _head = new();
    private readonly List<Vector2> _rest = new();

    private AnimalBody(AnimalRig rig, Random random)
    {
        _rig = rig;
        _animator = new AnimalAnimator(rig, random);
    }

    public static AnimalBody Create(Species species, Random random) => new(AnimalBuilder.Build(species), random);

    public Species Species => _rig.Species;
    public Node3D Model => _rig.Model;
    public IPerformer Performer => _animator;

    /// <summary>About half the doll's height on screen (a chicken a little less).</summary>
    public float SizeFactor => _rig.Species == Species.Chicken ? 0.42f : 0.48f;

    // Small animals scurry: fast for their size.
    public float WalkSpeed => _rig.Height * 0.85f;
    public float ClimbSpeed => _rig.Height * 0.55f;

    public string? Persona => Creatures.Persona(_rig.Species);
    public string? Name => Creatures.DefaultName(_rig.Species, TranslationServer.GetLocale().StartsWith("zh"));

    public Vector3 HeadPosition => _rig.Head.ToGlobal(new Vector3(0f, 0.06f, 0.03f));
    public Vector3 NeckPosition => _rig.Head.GlobalPosition;

    public void LookAt(Vector3 target)
    {
        var local = _rig.Model.GlobalBasis.Inverse() * (target - HeadPosition);
        _animator.LookYaw = Mathf.Atan2(local.X, Mathf.Max(local.Z, 0.05f));
        _animator.LookPitch = Mathf.Atan2(local.Y, new Vector2(local.X, local.Z).Length());
    }

    public float Yaw(MascotState state, int facing, int wallSide, bool faceUser) => state switch
    {
        // Animals walk mostly side-on but turned a little toward the viewer, so the face
        // still shows (a round head seen in pure profile is just a ball).
        MascotState.Walk => facing * 1.15f,
        MascotState.Sprawl => facing * Mathf.Pi / 2f,
        MascotState.Falling or MascotState.Jump => facing * Mathf.Pi / 2f * 0.8f,
        MascotState.Climb or MascotState.Cling => wallSide * Mathf.Pi / 2f,
        MascotState.Lie or MascotState.Landing or MascotState.GetUp => facing * 1.0f,
        MascotState.Sit or MascotState.Tumble => facing * 0.5f,
        MascotState.Dragged => facing * 0.4f,
        // Side-on enough for what it is doing in front of it (a bowl, a ball) to show.
        MascotState.Pose => facing * 0.95f,
        _ when faceUser => facing * 0.35f,
        _ => facing * 0.7f,
    };

    public Vector2[] Outline(Camera3D camera, float margin)
    {
        // Two convex pieces, the head (with ears, snout, beak) and the rest (body,
        // legs, tail), merged: close-fitting, and two convex shapes always merge
        // cleanly, unlike many small overlapping ones.
        _head.Clear();
        _rest.Clear();
        foreach (var mesh in _rig.Solids)
        {
            if (!mesh.IsVisibleInTree())
                continue;
            var target = _rig.Head.IsAncestorOf(mesh) ? _head : _rest;
            var box = mesh.GetAabb();
            var centre = box.GetCenter();
            var half = box.Size / 2f;
            var toWorld = mesh.GlobalTransform;
            // Spheres: points on the ellipsoid; capsules and cones fill their box's
            // corners, so use the box itself.
            foreach (var d in mesh.Mesh is SphereMesh ? Directions : Corners)
                target.Add(camera.UnprojectPosition(toWorld * (centre + d * half)));
        }
        var head = CharacterHitShape.Grow(Geometry2D.ConvexHull(_head.ToArray()), margin);
        var rest = CharacterHitShape.Grow(Geometry2D.ConvexHull(_rest.ToArray()), margin);
        if (head.Length < 3)
            return rest;
        if (rest.Length < 3)
            return head;
        return CharacterHitShape.Union(head, rest) ?? Geometry2D.ConvexHull(head.Concat(rest).ToArray());
    }

    /// <summary>
    /// Points around the unit sphere (a 5×5×5 grid's surface, normalised), pushed out a
    /// little so the polygon through them still contains the sphere between points.
    /// </summary>
    private static Vector3[] BuildDirections()
    {
        var directions = new List<Vector3>();
        for (int x = -2; x <= 2; x++)
        {
            for (int y = -2; y <= 2; y++)
            {
                for (int z = -2; z <= 2; z++)
                {
                    if (Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z))) == 2)
                        directions.Add(new Vector3(x, y, z).Normalized() * 1.04f);
                }
            }
        }
        return directions.ToArray();
    }
}
