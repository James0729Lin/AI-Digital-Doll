using System;
using System.Collections.Generic;
using DigitalDoll.Core;
using Godot;

namespace DigitalDoll.Animals;

/// <summary>
/// The parts of an animal model that move. The model faces +Z with +X on its left
/// and its feet at the origin; every part is a node whose rotation the animator sets.
/// </summary>
public sealed class AnimalRig
{
    public required Species Species;
    public required Node3D Model;

    /// <summary>Rotated as a whole (climbing a wall); child of <see cref="Model"/>.</summary>
    public required Node3D Root;
    public required Node3D Body;
    public required Node3D Head;

    /// <summary>Quadrupeds: front-left, front-right, back-left, back-right. Birds: left, right.</summary>
    public required Node3D[] Legs;
    public required Node3D[] Tail;
    public Node3D[] Ears = System.Array.Empty<Node3D>();
    public Node3D[] Wings = System.Array.Empty<Node3D>();
    public required Node3D[] Eyes;
    public Node3D[] Cheeks = System.Array.Empty<Node3D>();

    /// <summary>Scaled open while talking (a mouth, or the lower beak's hinge).</summary>
    public required Node3D Mouth;
    public Node3D? Tongue;

    public bool Biped;
    public float LegLength;
    public float BodyHeight;
    public float BodyRadiusY;
    public float Height;

    /// <summary>Rest transforms, so the animator can pose relative to them.</summary>
    public readonly Dictionary<Node3D, Transform3D> Rest = new();

    /// <summary>The meshes that make up the silhouette (for the mouse region).</summary>
    public readonly List<MeshInstance3D> Solids = new();

    public void Remember()
    {
        foreach (var node in new[] { Root, Body, Head, Mouth })
            Rest[node] = node.Transform;
        foreach (var group in new[] { Legs, Tail, Ears, Wings, Eyes, Cheeks })
        {
            foreach (var node in group)
                Rest[node] = node.Transform;
        }
        if (Tongue != null)
            Rest[Tongue] = Tongue.Transform;
    }
}

/// <summary>
/// Builds the bundled animals from rounded shapes: chibi proportions (big head, big
/// eyes, short legs), flat colours with anime shading and a thin outline.
/// </summary>
public static class AnimalBuilder
{
    private static readonly Color Ink = new(0.13f, 0.11f, 0.14f);
    private static readonly Color White = new(1f, 0.97f, 0.93f);
    private static readonly Color Blush = new(1f, 0.6f, 0.65f);
    private static readonly Color MouthRed = new(0.55f, 0.18f, 0.22f);
    private static readonly Color TonguePink = new(0.98f, 0.5f, 0.55f);

    private static Shader? _shader, _outline;
    private static readonly Dictionary<(Color, bool), Material> Materials = new();

    public static AnimalRig Build(Species species) => species switch
    {
        Species.Dog => Dog(),
        Species.Chicken => Chicken(),
        Species.Pig => Pig(),
        _ => Cat(),
    };

    // ---- Species ----------------------------------------------------------------

    private static AnimalRig Cat()
    {
        var orange = new Color(0.96f, 0.64f, 0.33f);
        var stripe = new Color(0.85f, 0.47f, 0.2f);
        var pink = new Color(0.98f, 0.62f, 0.68f);
        var (model, root, body) = Frame("Cat", 0.135f);
        Blob(body, Vector3.Zero, new(0.075f, 0.072f, 0.13f), orange);
        Blob(body, new(0f, -0.012f, 0.085f), new(0.055f, 0.055f, 0.055f), White, outline: false);

        var legs = Legs4(body, 0.045f, 0.08f, -0.035f, 0.1f, 0.021f, orange, White);
        var head = Node(body, "Head", new(0f, 0.045f, 0.11f));
        Blob(head, new(0f, 0.07f, 0.03f), new(0.106f, 0.088f, 0.08f), orange);
        // Fluffy cheeks widen the face like a real cat's.
        for (int s = -1; s <= 1; s += 2)
            Blob(head, new(0.072f * s, 0.035f, 0.045f), new(0.042f, 0.032f, 0.04f), orange, outline: false);
        Blob(head, new(0f, 0.038f, 0.088f), new(0.05f, 0.034f, 0.034f), White, outline: false);
        Blob(head, new(0f, 0.056f, 0.12f), new(0.011f, 0.008f, 0.006f), pink, solid: false);
        for (int i = -1; i <= 1; i++)
            Blob(head, new(0.025f * i, 0.142f, 0.06f - 0.012f * MathF.Abs(i)), new(0.007f, 0.012f, 0.012f), stripe, solid: false);
        var ears = new Node3D[2];
        for (int s = 0; s < 2; s++)
        {
            float x = s == 0 ? 1f : -1f;
            ears[s] = Node(head, s == 0 ? "EarL" : "EarR", new(0.056f * x, 0.13f, 0.03f), new(0.05f, 0f, -0.38f * x));
            Ear(ears[s], 0.036f, 0.062f, 0.02f, orange);
            Ear(ears[s], 0.022f, 0.042f, 0.008f, pink, front: 0.011f, solid: false);
        }
        var (eyes, cheeks, mouth) = Face(head, new(0f, 0.07f, 0.03f), new(0.106f, 0.088f, 0.08f), eyeX: 0.047f, eyeY: 0.08f, eyeSize: 1.15f, mouthAt: new(0f, 0.026f, 0.118f));
        var tail = Chain(body, new(0f, 0.035f, -0.115f), 5, 0.036f, 0.017f, orange, stripe, curl: 0.32f);
        return Finish(new AnimalRig
        {
            Species = Species.Cat, Model = model, Root = root, Body = body, Head = head, Legs = legs, Tail = tail,
            Ears = ears, Eyes = eyes, Cheeks = cheeks, Mouth = mouth,
            LegLength = 0.1f, BodyHeight = 0.135f, BodyRadiusY = 0.072f,
        });
    }

    private static AnimalRig Dog()
    {
        var tan = new Color(0.86f, 0.62f, 0.38f);
        var brown = new Color(0.55f, 0.35f, 0.22f);
        var (model, root, body) = Frame("Dog", 0.14f);
        Blob(body, Vector3.Zero, new(0.08f, 0.075f, 0.135f), tan);
        Blob(body, new(0f, -0.015f, 0.085f), new(0.058f, 0.058f, 0.058f), White, outline: false);
        var legs = Legs4(body, 0.048f, 0.082f, -0.035f, 0.105f, 0.024f, tan, White);
        var head = Node(body, "Head", new(0f, 0.045f, 0.11f));
        Blob(head, new(0f, 0.07f, 0.03f), new(0.104f, 0.088f, 0.08f), tan);
        Blob(head, new(0f, 0.04f, 0.1f), new(0.05f, 0.04f, 0.055f), White);
        Blob(head, new(0f, 0.066f, 0.152f), new(0.018f, 0.013f, 0.011f), Ink, solid: false);
        var ears = new Node3D[2];
        for (int s = 0; s < 2; s++)
        {
            float x = s == 0 ? 1f : -1f;
            ears[s] = Node(head, s == 0 ? "EarL" : "EarR", new(0.07f * x, 0.145f, 0.005f), new(0.3f, 0f, 0.3f * x));
            Blob(ears[s], new(0.01f * x, -0.05f, 0f), new(0.015f, 0.058f, 0.028f), brown);
        }
        var (eyes, cheeks, mouth) = Face(head, new(0f, 0.07f, 0.03f), new(0.104f, 0.088f, 0.08f), eyeX: 0.048f, eyeY: 0.088f, eyeSize: 1.15f, mouthAt: new(0f, 0.012f, 0.145f));
        var tongue = Node(mouth, "Tongue", new(0f, -0.008f, 0.006f));
        Blob(tongue, Vector3.Zero, new(0.011f, 0.008f, 0.012f), TonguePink, solid: false, outline: false);
        var tail = Chain(body, new(0f, 0.045f, -0.12f), 3, 0.034f, 0.018f, tan, White, curl: 0.55f);
        return Finish(new AnimalRig
        {
            Species = Species.Dog, Model = model, Root = root, Body = body, Head = head, Legs = legs, Tail = tail,
            Ears = ears, Eyes = eyes, Cheeks = cheeks, Mouth = mouth, Tongue = tongue,
            LegLength = 0.105f, BodyHeight = 0.14f, BodyRadiusY = 0.075f,
        });
    }

    private static AnimalRig Pig()
    {
        var pink = new Color(0.97f, 0.68f, 0.75f);
        var dark = new Color(0.9f, 0.52f, 0.62f);
        var snout = new Color(0.99f, 0.76f, 0.82f);
        var (model, root, body) = Frame("Pig", 0.125f);
        Blob(body, Vector3.Zero, new(0.095f, 0.085f, 0.13f), pink);
        var legs = Legs4(body, 0.055f, 0.075f, -0.045f, 0.08f, 0.026f, pink, dark);
        var head = Node(body, "Head", new(0f, 0.04f, 0.105f));
        Blob(head, new(0f, 0.065f, 0.035f), new(0.1f, 0.09f, 0.088f), pink);
        var snoutNode = Node(head, "Snout", new(0f, 0.045f, 0.118f), new(Mathf.Pi / 2f, 0f, 0f));
        Cylinder(snoutNode, Vector3.Zero, 0.036f, 0.028f, snout);
        for (int s = -1; s <= 1; s += 2)
            Blob(head, new(0.012f * s, 0.045f, 0.133f), new(0.006f, 0.01f, 0.004f), new Color(0.7f, 0.33f, 0.43f), solid: false);
        var ears = new Node3D[2];
        for (int s = 0; s < 2; s++)
        {
            float x = s == 0 ? 1f : -1f;
            ears[s] = Node(head, s == 0 ? "EarL" : "EarR", new(0.06f * x, 0.13f, 0.035f), new(0.55f, 0f, -0.35f * x));
            Cone(ears[s], new(0f, 0.022f, 0f), 0.03f, 0.048f, dark, segments: 4);
        }
        var (eyes, cheeks, mouth) = Face(head, new(0f, 0.065f, 0.035f), new(0.1f, 0.09f, 0.088f), eyeX: 0.046f, eyeY: 0.088f, eyeSize: 1f, mouthAt: new(0f, 0.012f, 0.112f));
        var tail = Chain(body, new(0f, 0.04f, -0.125f), 5, 0.014f, 0.007f, dark, dark, curl: 0.9f);
        return Finish(new AnimalRig
        {
            Species = Species.Pig, Model = model, Root = root, Body = body, Head = head, Legs = legs, Tail = tail,
            Ears = ears, Eyes = eyes, Cheeks = cheeks, Mouth = mouth,
            LegLength = 0.08f, BodyHeight = 0.125f, BodyRadiusY = 0.085f,
        });
    }

    private static AnimalRig Chicken()
    {
        var red = new Color(0.9f, 0.27f, 0.23f);
        var beak = new Color(0.98f, 0.72f, 0.24f);
        var leg = new Color(0.95f, 0.62f, 0.2f);
        var (model, root, body) = Frame("Chicken", 0.17f);
        Blob(body, Vector3.Zero, new(0.085f, 0.09f, 0.1f), White);
        var head = Node(body, "Head", new(0f, 0.07f, 0.05f));
        Blob(head, new(0f, 0.05f, 0.02f), new(0.068f, 0.068f, 0.066f), White);
        for (int i = 0; i < 3; i++)
            Blob(head, new(0f, 0.118f + 0.006f * (i == 1 ? 1 : 0), 0.025f - 0.022f * i), new(0.012f, 0.02f, 0.014f), red);
        // Upper beak, and the lower one on the mouth hinge.
        var upper = Node(head, "Beak", new(0f, 0.052f, 0.085f), new(Mathf.Pi / 2f, 0f, 0f));
        Cone(upper, new(0f, 0.015f, 0f), 0.02f, 0.03f, beak);
        var mouth = Node(head, "Mouth", new(0f, 0.044f, 0.078f));
        var lower = Node(mouth, "LowerBeak", Vector3.Zero, new(Mathf.Pi / 2f, 0f, 0f));
        Cone(lower, new(0f, 0.012f, 0f), 0.014f, 0.022f, beak);
        Blob(head, new(0f, 0.02f, 0.07f), new(0.011f, 0.017f, 0.01f), red);
        var (eyes, cheeks, _) = Face(head, new(0f, 0.05f, 0.02f), new(0.068f, 0.068f, 0.066f), eyeX: 0.034f, eyeY: 0.062f, eyeSize: 0.7f, mouthAt: null);

        var wings = new Node3D[2];
        for (int s = 0; s < 2; s++)
        {
            float x = s == 0 ? 1f : -1f;
            wings[s] = Node(body, s == 0 ? "WingL" : "WingR", new(0.072f * x, 0.025f, 0f));
            Blob(wings[s], new(0.004f * x, -0.022f, -0.016f), new(0.016f, 0.045f, 0.065f), new Color(0.95f, 0.93f, 0.88f));
        }
        var tail = new Node3D[1];
        tail[0] = Node(body, "Tail", new(0f, 0.04f, -0.085f), new(-0.6f, 0f, 0f));
        for (int i = -1; i <= 1; i++)
            Blob(tail[0], new(0.018f * i, 0.04f, -0.01f), new(0.014f, 0.045f, 0.02f), White);

        var legs = new Node3D[2];
        for (int s = 0; s < 2; s++)
        {
            float x = s == 0 ? 1f : -1f;
            legs[s] = Node(body, s == 0 ? "LegL" : "LegR", new(0.035f * x, -0.08f, 0.005f));
            Capsule(legs[s], Vector3.Zero, new(0f, -0.09f, 0f), 0.009f, leg);
            for (int t = -1; t <= 1; t++)
                Capsule(legs[s], new(0f, -0.088f, 0f), new(0.018f * t, -0.088f, 0.03f - 0.006f * MathF.Abs(t)), 0.006f, leg);
        }
        return Finish(new AnimalRig
        {
            Species = Species.Chicken, Model = model, Root = root, Body = body, Head = head, Legs = legs, Tail = tail,
            Wings = wings, Eyes = eyes, Cheeks = cheeks, Mouth = mouth,
            Biped = true, LegLength = 0.09f, BodyHeight = 0.17f, BodyRadiusY = 0.09f,
        });
    }

    // ---- Shared parts -------------------------------------------------------------

    private static (Node3D Model, Node3D Root, Node3D Body) Frame(string name, float bodyHeight)
    {
        var model = new Node3D { Name = name };
        var root = Node(model, "Root", Vector3.Zero);
        var body = Node(root, "Body", new(0f, bodyHeight, 0f));
        return (model, root, body);
    }

    private static Node3D[] Legs4(Node3D body, float x, float z, float y, float length, float radius, Color colour, Color paw)
    {
        var legs = new Node3D[4];
        string[] names = { "LegFL", "LegFR", "LegBL", "LegBR" };
        for (int i = 0; i < 4; i++)
        {
            float sx = i % 2 == 0 ? 1f : -1f, sz = i < 2 ? 1f : -1f;
            legs[i] = Node(body, names[i], new(x * sx, y, z * sz));
            // Back legs get a bigger thigh, like a sitting cat's haunch.
            float haunch = sz > 0 ? 1.45f : 1.8f;
            Blob(legs[i], new(0f, radius * 0.4f, 0f), new Vector3(radius, radius * 1.4f, radius * 1.15f) * haunch, colour, outline: false);
            Capsule(legs[i], Vector3.Zero, new(0f, -length + radius * 0.8f, 0f), radius, colour);
            Blob(legs[i], new(0f, -length + radius * 0.7f, radius * 0.35f), new(radius * 1.2f, radius * 0.8f, radius * 1.4f), paw);
        }
        return legs;
    }

    /// <summary>
    /// Big glossy eyes set into the head's surface, blush and a mouth that starts
    /// closed. <paramref name="skull"/> and <paramref name="radii"/> are the head blob.
    /// </summary>
    private static (Node3D[] Eyes, Node3D[] Cheeks, Node3D Mouth) Face(Node3D head, Vector3 skull, Vector3 radii,
        float eyeX, float eyeY, float eyeSize, Vector3? mouthAt)
    {
        var eyes = new Node3D[2];
        var cheeks = new Node3D[2];
        var iris = new Color(0.2f, 0.16f, 0.13f);
        for (int s = 0; s < 2; s++)
        {
            float x = s == 0 ? 1f : -1f;
            var at = OnSurface(skull, radii, eyeX * x, eyeY, 0.002f);
            float turn = Mathf.Atan2(at.X - skull.X, (at.Z - skull.Z) * radii.X / radii.Z);
            eyes[s] = Node(head, s == 0 ? "EyeL" : "EyeR", at, new(-0.15f, turn, 0f));
            // Dark eye with a brown iris glow at the bottom and two catch-lights.
            Blob(eyes[s], Vector3.Zero, new Vector3(0.018f, 0.025f, 0.008f) * eyeSize, Ink, solid: false, outline: false);
            Blob(eyes[s], new Vector3(0f, -0.008f, 0.004f) * eyeSize, new Vector3(0.012f, 0.01f, 0.005f) * eyeSize, iris, solid: false, outline: false);
            Blob(eyes[s], new Vector3(0.006f * x, 0.009f, 0.007f) * eyeSize, new Vector3(0.0065f, 0.0075f, 0.003f) * eyeSize, Colors.White, solid: false, outline: false);
            Blob(eyes[s], new Vector3(-0.006f * x, -0.009f, 0.007f) * eyeSize, new Vector3(0.0028f, 0.0028f, 0.002f) * eyeSize, Colors.White, solid: false, outline: false);
            var cheekAt = OnSurface(skull, radii, eyeX * 1.5f * x, eyeY - 0.033f, 0.001f);
            cheeks[s] = Node(head, s == 0 ? "CheekL" : "CheekR", cheekAt,
                new(0f, Mathf.Atan2(cheekAt.X - skull.X, (cheekAt.Z - skull.Z) * radii.X / radii.Z), 0f));
            Blob(cheeks[s], Vector3.Zero, new(0.016f, 0.009f, 0.004f), Blush, solid: false, outline: false);
        }
        var mouth = Node(head, "Mouth", mouthAt ?? Vector3.Zero);
        if (mouthAt != null)
        {
            Blob(mouth, Vector3.Zero, new(0.016f, 0.016f, 0.01f), MouthRed, solid: false, outline: false);
            mouth.Scale = new Vector3(1f, 0.05f, 1f);
        }
        return (eyes, cheeks, mouth);
    }

    /// <summary>The point on the front of an ellipsoid at (x, y), pushed out by <paramref name="lift"/>.</summary>
    private static Vector3 OnSurface(Vector3 centre, Vector3 radii, float x, float y, float lift)
    {
        float u = (x - centre.X) / radii.X, v = (y - centre.Y) / radii.Y;
        float z = centre.Z + radii.Z * MathF.Sqrt(MathF.Max(0f, 1f - u * u - v * v));
        return new Vector3(x, y, z + lift);
    }

    /// <summary>A tail: a chain of segments pointing backward, each one a child of the last.</summary>
    private static Node3D[] Chain(Node3D body, Vector3 start, int count, float length, float radius, Color colour, Color tip, float curl = 0f)
    {
        var chain = new Node3D[count];
        Node3D parent = body;
        var at = start;
        for (int i = 0; i < count; i++)
        {
            // Each segment bends a little further up than the last.
            chain[i] = Node(parent, $"Tail{i}", at, new(curl * (i == 0 ? 0.6f : 1f), 0f, 0f));
            float r = radius * (1f - 0.1f * i);
            Capsule(chain[i], Vector3.Zero, new(0f, 0f, -length), r, i == count - 1 ? tip : colour);
            parent = chain[i];
            at = new Vector3(0f, 0f, -length);
        }
        return chain;
    }

    private static AnimalRig Finish(AnimalRig rig)
    {
        foreach (var node in rig.Model.FindChildren("*", "MeshInstance3D", true, false))
        {
            var mesh = (MeshInstance3D)node;
            if (mesh.HasMeta("solid"))
                rig.Solids.Add(mesh);
        }
        rig.Height = rig.Species switch
        {
            Species.Chicken => 0.34f,
            Species.Pig => 0.33f,
            _ => 0.37f,
        };
        rig.Remember();
        return rig;
    }

    // ---- Primitives ---------------------------------------------------------------

    private static Node3D Node(Node3D parent, string name, Vector3 position, Vector3? rotation = null)
    {
        var node = new Node3D { Name = name, Position = position, Rotation = rotation ?? Vector3.Zero };
        parent.AddChild(node);
        return node;
    }

    private static MeshInstance3D Blob(Node3D parent, Vector3 position, Vector3 radii, Color colour, bool solid = true, bool outline = true)
    {
        var mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 32, Rings = 16 };
        return Add(parent, mesh, new Transform3D(Basis.Identity.Scaled(radii), position), colour, solid, outline);
    }

    private static MeshInstance3D Capsule(Node3D parent, Vector3 from, Vector3 to, float radius, Color colour)
    {
        var axis = to - from;
        float length = axis.Length();
        var mesh = new CapsuleMesh { Radius = radius, Height = length + 2f * radius, RadialSegments = 16, Rings = 4 };
        var up = axis / length;
        var basis = Basis.Identity;
        var cross = Vector3.Up.Cross(up);
        if (cross.LengthSquared() > 1e-8f)
            basis = new Basis(cross.Normalized(), Vector3.Up.AngleTo(up));
        else if (up.Y < 0f)
            basis = new Basis(Vector3.Right, Mathf.Pi);
        return Add(parent, mesh, new Transform3D(basis, (from + to) / 2f), colour, solid: true, outline: true);
    }

    private static MeshInstance3D Cone(Node3D parent, Vector3 position, float radius, float height, Color colour, bool solid = true, int segments = 12)
    {
        var mesh = new CylinderMesh { TopRadius = 0.001f, BottomRadius = radius, Height = height, RadialSegments = segments, Rings = 1 };
        return Add(parent, mesh, new Transform3D(Basis.Identity, position), colour, solid, outline: true);
    }

    /// <summary>A flat triangular ear standing up from its node: a three-sided cone squashed front to back.</summary>
    private static MeshInstance3D Ear(Node3D parent, float width, float height, float depth, Color colour, float front = 0f, bool solid = true)
    {
        var mesh = new CylinderMesh { TopRadius = 0.001f, BottomRadius = 1f, Height = 1f, RadialSegments = 3, Rings = 1 };
        // One flat face of the triangle toward the front (+Z).
        var basis = Basis.Identity.Scaled(new Vector3(width, height, depth)) * new Basis(Vector3.Up, Mathf.Pi);
        return Add(parent, mesh, new Transform3D(basis, new Vector3(0f, height * 0.45f, front)), colour, solid, outline: true);
    }

    private static MeshInstance3D Cylinder(Node3D parent, Vector3 position, float radius, float height, Color colour)
    {
        var mesh = new CylinderMesh { TopRadius = radius, BottomRadius = radius, Height = height, RadialSegments = 20, Rings = 1 };
        return Add(parent, mesh, new Transform3D(Basis.Identity, position), colour, solid: true, outline: true);
    }

    private static MeshInstance3D Add(Node3D parent, Mesh mesh, Transform3D transform, Color colour, bool solid, bool outline)
    {
        var instance = new MeshInstance3D
        {
            Mesh = mesh,
            Transform = transform,
            MaterialOverride = MaterialFor(colour, outline),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        if (solid)
            instance.SetMeta("solid", true);
        parent.AddChild(instance);
        return instance;
    }

    private static Material MaterialFor(Color colour, bool outline)
    {
        if (Materials.TryGetValue((colour, outline), out var cached))
            return cached;
        _shader ??= GD.Load<Shader>("res://assets/shaders/animal.gdshader");
        _outline ??= GD.Load<Shader>("res://assets/shaders/outline.gdshader");
        var material = new ShaderMaterial { Shader = _shader };
        material.SetShaderParameter("albedo", colour);
        if (outline)
        {
            var line = new ShaderMaterial { Shader = _outline };
            line.SetShaderParameter("outline_color", colour.Darkened(0.6f));
            line.SetShaderParameter("width_px", 1.2f);
            material.NextPass = line;
        }
        Materials[(colour, outline)] = material;
        return material;
    }
}
