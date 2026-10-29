using System;
using System.Collections.Generic;
using DigitalDoll.Core;
using DigitalDoll.Core.Acting;
using DigitalDoll.Core.Mascot;
using Godot;

namespace DigitalDoll.Animals;

/// <summary>
/// The things that appear beside an animal for its activities (a ball of yarn, a bowl of
/// food…) and the "Zzz" over it while it sleeps. Built from simple shapes, placed in the
/// animal's own frame (in front of its nose, on the ground), popped in when the activity
/// starts and shrunk away after it. Motions shared with the animal's own (where the ball
/// is, how much food is left) are worked out here from the activity's time, so both
/// performers can follow them.
/// </summary>
public sealed class ActivityProps
{
    private const float PopSeconds = 0.35f;

    private readonly Node3D _stage;
    private readonly Species _species;
    private readonly float _height, _length;
    private readonly Dictionary<ActivityProp, Node3D> _props = new();
    private readonly Dictionary<ActivityProp, List<MeshInstance3D>> _food = new();
    private List<MeshInstance3D> _building = new();
    private readonly Label3D[] _zs = new Label3D[3];
    private Node3D? _shown;
    private ActivityProp _prop;
    private float _scale, _sleep, _time;

    /// <param name="parent">The node turned with the animal (props stay in front of it).</param>
    /// <param name="height">The animal's height, which every prop is sized by.</param>
    /// <param name="length">Nose to tail, to put props just in front of the nose.</param>
    public ActivityProps(Node3D parent, Species species, float height, float length)
    {
        _species = species;
        _height = height;
        _length = length;
        _stage = new Node3D { Name = "Props" };
        parent.AddChild(_stage);
        for (int i = 0; i < _zs.Length; i++)
        {
            _zs[i] = new Label3D
            {
                Text = "Z",
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
                FontSize = 64,
                OutlineSize = 10,
                PixelSize = height * 0.0035f * (1f + 0.25f * i),
                Modulate = new Color(0.55f, 0.7f, 1f),
                OutlineModulate = new Color(1f, 1f, 1f, 0.9f),
                Visible = false,
            };
            _stage.AddChild(_zs[i]);
        }
    }

    /// <summary>The activity's time so far (seconds); the animal's motions follow it.</summary>
    public float Time => _time;

    /// <summary>Where the ball (yarn or ball) is now: ahead of the nose, in the animal's frame.</summary>
    public Vector3 BallPosition { get; private set; }

    /// <summary>How much of the meal is left, 1 to 0.</summary>
    public float FoodLeft { get; private set; } = 1f;

    /// <summary>Where the prop sits (in front of the nose).</summary>
    public float Front => _length * 0.5f + _height * 0.28f;

    /// <param name="activity">The activity being done, or null.</param>
    /// <param name="sleeping">Lying down asleep: the "Zzz" rises over it.</param>
    public void Update(float dt, string? activity, MascotState state)
    {
        var prop = activity != null && ActivityCatalog.Find(activity) is ActivityInfo info ? info.Prop(_species) : ActivityProp.None;
        if (prop != _prop)
        {
            // A new activity: start its clock and its prop afresh.
            if (prop != ActivityProp.None)
            {
                _time = 0f;
                FoodLeft = 1f;
            }
            _prop = prop;
        }
        if (activity != null)
            _time += dt;

        // The old prop shrinks away before the new one pops in.
        var wanted = prop == ActivityProp.None ? null : Get(prop);
        if (_shown != wanted)
        {
            _scale = MathF.Max(0f, _scale - dt / PopSeconds);
            if (_scale <= 0f)
            {
                if (_shown != null)
                    _shown.Visible = false;
                _shown = wanted;
                if (_shown != null)
                {
                    _shown.Visible = true;
                    foreach (var piece in _food[_prop])
                        piece.Visible = true;
                }
            }
        }
        else if (_shown != null)
        {
            _scale = MathF.Min(1f, _scale + dt / PopSeconds);
        }
        if (_shown != null)
        {
            float pop = _scale < 1f ? Back(_scale) : 1f;
            _shown.Scale = Vector3.One * MathF.Max(pop, 0.001f);
            Animate(_shown == wanted ? prop : ActivityProp.None);
        }

        // Sleeping: Zs drift up one after another, growing and fading.
        bool asleep = state == MascotState.Lie;
        _sleep = asleep ? _sleep + dt : 0f;
        for (int i = 0; i < _zs.Length; i++)
        {
            var z = _zs[i];
            float t = (_sleep - 2.5f) / 3f - i / 3f;
            if (!asleep || t < 0f)
            {
                z.Visible = false;
                continue;
            }
            float u = t % 1f;
            z.Visible = true;
            z.Position = new Vector3(_height * (0.1f + 0.35f * u) + _height * 0.08f * MathF.Sin(u * 6f), _height * (0.75f + 0.8f * u), _length * 0.3f);
            z.Scale = Vector3.One * (0.6f + 0.6f * u);
            z.Modulate = new Color(0.55f, 0.7f, 1f, MathF.Min(1f, 4f * (1f - u)) * MathF.Min(1f, 6f * u));
            z.OutlineModulate = new Color(1f, 1f, 1f, 0.9f * z.Modulate.A);
        }
    }

    // ---- Motion -------------------------------------------------------------------

    /// <summary>
    /// The yarn rolls away from the paws and back (batted); the dog's ball is nosed
    /// along, bounced up now and then. Eating empties the bowl bit by bit.
    /// </summary>
    private void Animate(ActivityProp prop)
    {
        float t = _time;
        switch (prop)
        {
            case ActivityProp.YarnBall:
            {
                float r = _height * 0.13f;
                float along = _height * 0.22f * MathF.Sin(t * 1.7f) + _height * 0.05f * MathF.Sin(t * 4.3f);
                float side = _height * 0.12f * MathF.Sin(t * 0.9f);
                BallPosition = new Vector3(side, r, Front + along);
                var ball = _props[prop];
                ball.Position = BallPosition;
                // Rolling: turned by the distance it has gone.
                ball.Rotation = new Vector3(along / r, 0f, -side / r);
                break;
            }
            case ActivityProp.Ball:
            {
                float r = _height * 0.1f;
                float along = _height * 0.3f * MathF.Sin(t * 1.3f);
                float hop = _height * 0.35f * MathF.Max(0f, MathF.Sin(t * 2.6f)) * (MathF.Sin(t * 0.7f) > 0.3f ? 1f : 0f);
                BallPosition = new Vector3(0f, r + hop, Front + _height * 0.1f + along);
                var ball = _props[prop];
                ball.Position = BallPosition;
                ball.Rotation = new Vector3(along / r, 0f, 0f);
                break;
            }
            case ActivityProp.FoodBowl:
            case ActivityProp.Trough:
            case ActivityProp.Grain:
            {
                // Eats for most of the time, licking the bowl clean at the end.
                FoodLeft = Mathf.Clamp(1f - (t - 0.8f) / 6.5f, 0f, 1f);
                var food = _food[prop];
                int left = (int)MathF.Ceiling(FoodLeft * food.Count);
                for (int i = 0; i < food.Count; i++)
                    food[i].Visible = i < left;
                break;
            }
            case ActivityProp.Mud:
            {
                // The puddle shimmers a little as it is rolled in.
                _props[prop].Scale *= new Vector3(1f + 0.03f * MathF.Sin(t * 7f), 1f, 1f + 0.03f * MathF.Cos(t * 6f));
                break;
            }
        }
    }

    // ---- Building -------------------------------------------------------------------

    private Node3D Get(ActivityProp prop)
    {
        if (_props.TryGetValue(prop, out var node))
            return node;
        _building = new List<MeshInstance3D>();
        node = prop switch
        {
            ActivityProp.YarnBall => YarnBall(),
            ActivityProp.FoodBowl => Bowl(),
            ActivityProp.Trough => Trough(),
            ActivityProp.Grain => Grain(),
            ActivityProp.Ball => Ball(),
            ActivityProp.Bone => Bone(),
            ActivityProp.Mud => Mud(),
            _ => new Node3D(),
        };
        node.Name = prop.ToString();
        node.Visible = false;
        _stage.AddChild(node);
        _props[prop] = node;
        _food[prop] = _building;
        return node;
    }

    private Node3D YarnBall()
    {
        float r = _height * 0.13f;
        var root = new Node3D();
        var pink = Material(new Color(0.93f, 0.36f, 0.5f));
        var dark = Material(new Color(0.78f, 0.22f, 0.37f));
        root.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = r, Height = r * 2f, RadialSegments = 20, Rings = 10 }, MaterialOverride = pink });
        // Wraps of yarn round it in different directions.
        for (int i = 0; i < 4; i++)
        {
            var wrap = new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = r * 0.96f, OuterRadius = r * 1.04f, Rings = 24, RingSegments = 6 },
                MaterialOverride = dark,
                Rotation = new Vector3(0.6f + i * 0.9f, i * 1.3f, 0.4f * i),
            };
            root.AddChild(wrap);
        }
        // A loose end trailing on the ground.
        var end = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = r * 0.05f, BottomRadius = r * 0.05f, Height = r * 1.6f, RadialSegments = 6 },
            MaterialOverride = pink,
            Position = new Vector3(r * 0.2f, -r * 0.9f, -r * 1.3f),
            Rotation = new Vector3(Mathf.Pi / 2f, 0f, 0.3f),
        };
        root.AddChild(end);
        return root;
    }

    private Node3D Bowl()
    {
        float r = _height * 0.2f, h = _height * 0.1f;
        var root = new Node3D { Position = new Vector3(0f, 0f, Front) };
        var colour = _species == Species.Dog ? new Color(0.85f, 0.25f, 0.22f) : new Color(0.3f, 0.55f, 0.9f);
        root.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = r, BottomRadius = r * 0.75f, Height = h, RadialSegments = 24 },
            MaterialOverride = Material(colour),
            Position = new Vector3(0f, h / 2f, 0f),
        });
        // The inside, a little darker, just under the rim.
        root.AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = r * 0.85f, BottomRadius = r * 0.85f, Height = h * 0.05f, RadialSegments = 24 },
            MaterialOverride = Material(colour.Darkened(0.35f)),
            Position = new Vector3(0f, h * 1.005f, 0f),
        });
        // Kibble heaped up in it, eaten from the top down.
        var kibble = Material(new Color(0.62f, 0.4f, 0.2f));
        var fish = Material(new Color(0.85f, 0.55f, 0.3f));
        var random = new Random(7);
        var pieces = new List<(float Y, MeshInstance3D Mesh)>();
        for (int i = 0; i < 26; i++)
        {
            float a = (float)random.NextDouble() * Mathf.Tau;
            float d = MathF.Sqrt((float)random.NextDouble()) * r * 0.7f;
            float y = h + h * 0.55f * (1f - d / (r * 0.7f)) * (float)random.NextDouble() + h * 0.05f;
            float size = _height * (0.028f + 0.01f * (float)random.NextDouble());
            var piece = new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = size, Height = size * 1.4f, RadialSegments = 8, Rings = 4 },
                MaterialOverride = i % 3 == 0 ? fish : kibble,
                Position = new Vector3(MathF.Cos(a) * d, y, MathF.Sin(a) * d),
                Rotation = new Vector3(a, a * 2f, 0f),
            };
            root.AddChild(piece);
            pieces.Add((y, piece));
        }
        AddFood(pieces);
        return root;
    }

    private Node3D Trough()
    {
        float w = _height * 0.7f, d = _height * 0.26f, h = _height * 0.13f;
        var root = new Node3D { Position = new Vector3(0f, 0f, Front + d * 0.1f) };
        var wood = Material(new Color(0.55f, 0.36f, 0.2f));
        float wall = h * 0.15f;
        // Bottom, two long sides and two ends.
        root.AddChild(Box(new Vector3(w, wall, d), new Vector3(0f, wall / 2f, 0f), wood));
        root.AddChild(Box(new Vector3(w, h, wall), new Vector3(0f, h / 2f, d / 2f - wall / 2f), wood));
        root.AddChild(Box(new Vector3(w, h, wall), new Vector3(0f, h / 2f, -d / 2f + wall / 2f), wood));
        root.AddChild(Box(new Vector3(wall, h, d), new Vector3(w / 2f - wall / 2f, h / 2f, 0f), wood));
        root.AddChild(Box(new Vector3(wall, h, d), new Vector3(-w / 2f + wall / 2f, h / 2f, 0f), wood));
        // Feed in layers heaped over the rim (so it shows from the side), going down as it is eaten.
        var feed = Material(new Color(0.85f, 0.72f, 0.3f));
        var greens = Material(new Color(0.45f, 0.68f, 0.3f));
        var pieces = new List<(float Y, MeshInstance3D Mesh)>();
        for (int i = 0; i < 5; i++)
        {
            float y = wall + h * 0.25f * (i + 0.5f);
            float shrink = i < 4 ? 1f : 0.8f;
            var layer = Box(new Vector3((w - 2.2f * wall) * shrink, h * 0.25f, (d - 2.2f * wall) * shrink), new Vector3(0f, y, 0f), i >= 3 ? greens : feed);
            root.AddChild(layer);
            pieces.Add((y, layer));
        }
        AddFood(pieces);
        return root;
    }

    private Node3D Grain()
    {
        var root = new Node3D { Position = new Vector3(0f, 0f, Front * 0.9f) };
        var corn = Material(new Color(0.98f, 0.82f, 0.3f));
        var random = new Random(11);
        var pieces = new List<(float Y, MeshInstance3D Mesh)>();
        for (int i = 0; i < 18; i++)
        {
            float size = _height * 0.022f;
            var grain = new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = size, Height = size * 1.3f, RadialSegments = 6, Rings = 3 },
                MaterialOverride = corn,
                Position = new Vector3(((float)random.NextDouble() - 0.5f) * _height * 0.5f, size * 0.6f, ((float)random.NextDouble() - 0.5f) * _height * 0.3f),
            };
            root.AddChild(grain);
            pieces.Add(((float)random.NextDouble(), grain));
        }
        AddFood(pieces);
        return root;
    }

    private Node3D Ball()
    {
        float r = _height * 0.1f;
        var root = new Node3D();
        root.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = r, Height = r * 2f, RadialSegments = 20, Rings = 10 }, MaterialOverride = Material(new Color(0.95f, 0.3f, 0.25f)) });
        root.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = r * 0.97f, OuterRadius = r * 1.03f, Rings = 24, RingSegments = 6 },
            MaterialOverride = Material(new Color(0.98f, 0.95f, 0.85f)),
            Rotation = new Vector3(0f, 0f, Mathf.Pi / 2f),
        });
        return root;
    }

    private Node3D Bone()
    {
        float l = _height * 0.34f, r = _height * 0.035f;
        // Held between the front paws as it lies.
        var root = new Node3D { Position = new Vector3(0f, r, _length * 0.5f + l * 0.25f), Rotation = new Vector3(0f, 1.2f, 0f) };
        var bone = Material(new Color(0.97f, 0.94f, 0.85f));
        root.AddChild(new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = r, Height = l },
            MaterialOverride = bone,
            Rotation = new Vector3(0f, 0f, Mathf.Pi / 2f),
        });
        foreach (float end in new[] { -1f, 1f })
        {
            foreach (float side in new[] { -1f, 1f })
            {
                root.AddChild(new MeshInstance3D
                {
                    Mesh = new SphereMesh { Radius = r * 1.5f, Height = r * 3f, RadialSegments = 12, Rings = 6 },
                    MaterialOverride = bone,
                    Position = new Vector3(end * l / 2f, 0f, side * r * 1.1f),
                });
            }
        }
        return root;
    }

    private Node3D Mud()
    {
        var root = new Node3D();
        var mud = Material(new Color(0.42f, 0.3f, 0.2f));
        // A low mound of mud (a flat puddle would not show from the side) under the animal.
        root.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = _height * 0.75f, Height = _height * 0.16f, RadialSegments = 32, Rings = 8 },
            MaterialOverride = mud,
            Scale = new Vector3(1.3f, 1f, 0.8f),
        });
        // A few splats around it.
        var random = new Random(5);
        for (int i = 0; i < 5; i++)
        {
            float a = i * 1.3f, d = _height * (0.85f + 0.2f * (float)random.NextDouble());
            float size = _height * (0.06f + 0.05f * (float)random.NextDouble());
            root.AddChild(new MeshInstance3D
            {
                Mesh = new SphereMesh { Radius = size, Height = size * 0.8f, RadialSegments = 12, Rings = 4 },
                MaterialOverride = mud,
                Position = new Vector3(MathF.Cos(a) * d * 1.3f, 0f, MathF.Sin(a) * d * 0.8f),
            });
        }
        return root;
    }

    /// <summary>The food pieces, lowest first: the last ones shown go first, so the meal goes down from the top.</summary>
    private void AddFood(List<(float Y, MeshInstance3D Mesh)> pieces)
    {
        pieces.Sort((a, b) => a.Y.CompareTo(b.Y));
        foreach (var piece in pieces)
            _building.Add(piece.Mesh);
    }

    private static MeshInstance3D Box(Vector3 size, Vector3 position, Material material) =>
        new() { Mesh = new BoxMesh { Size = size }, MaterialOverride = material, Position = position };

    private static StandardMaterial3D Material(Color colour) => new() { AlbedoColor = colour, Roughness = 0.85f };

    /// <summary>Overshoots a little and settles: a pop.</summary>
    private static float Back(float x)
    {
        const float s = 1.7f;
        x -= 1f;
        return x * x * ((s + 1f) * x + s) + 1f;
    }
}
