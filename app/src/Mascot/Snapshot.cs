using System;
using DigitalDoll.Character;
using DigitalDoll.Core;
using DigitalDoll.Core.Acting;
using DigitalDoll.Core.Body;
using DigitalDoll.Core.Mascot;
using Godot;

namespace DigitalDoll.Mascot;

/// <summary>
/// Development aid: renders one frame of a given behaviour to a PNG and quits, so
/// poses can be checked without a desktop session. Example:
/// <c>godot --path app -- --snapshot out.png --pose walk --time 1.2 --yaw 1.57</c>
/// </summary>
internal sealed class Snapshot
{
    private string _output = "";
    private string _pose = "idle";
    private float _time = 1f;
    private int _frame;
    private bool _captured;

    private string? _gesture;
    private string? _strike;
    private string? _emotion;
    private bool _talking, _petting;

    public Vector2I WindowSize { get; private set; } = new(360, 560);
    public float? Yaw { get; private set; }
    public string? CharacterPath { get; private set; }

    /// <summary>Set by <c>--animal cat|dog|chicken|pig</c>.</summary>
    public Species? Animal { get; private set; }

    /// <summary>Set by <c>--style classic|cube|lowpoly</c> (the animal's version).</summary>
    public AnimalStyle AnimalStyle { get; private set; }

    /// <summary>Set by <c>--coat n</c>: the animal wears this ready-made coat.</summary>
    public int? CoatPreset { get; private set; }

    /// <summary>Set by <c>--body</c>: show an edited body (adjusted with <c>--set key=value</c>).</summary>
    public BodyShape? Body { get; private set; }

    public static Snapshot? FromCommandLine(string[] args)
    {
        int index = Array.IndexOf(args, "--snapshot");
        if (index < 0 || index + 1 >= args.Length)
            return null;

        var snapshot = new Snapshot
        {
            _output = args[index + 1],
            _talking = Array.IndexOf(args, "--talking") >= 0,
            _petting = Array.IndexOf(args, "--petting") >= 0,
        };
        if (Array.IndexOf(args, "--body") >= 0)
            snapshot.Body = new BodyShape();
        for (int i = 0; i + 1 < args.Length; i++)
        {
            string value = args[i + 1];
            switch (args[i])
            {
                case "--pose":
                    snapshot._pose = value;
                    break;
                case "--time":
                    snapshot._time = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--yaw":
                    snapshot.Yaw = float.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--size":
                    var parts = value.Split('x');
                    snapshot.WindowSize = new Vector2I(int.Parse(parts[0]), int.Parse(parts[1]));
                    break;
                case "--gesture":
                    snapshot._gesture = value;
                    break;
                case "--emotion":
                    snapshot._emotion = value;
                    break;
                case "--animal":
                    snapshot.Animal = Enum.Parse<Species>(value, ignoreCase: true);
                    break;
                case "--strike":
                    snapshot._strike = value;
                    break;
                case "--coat":
                    snapshot.CoatPreset = int.Parse(value);
                    break;
                case "--style":
                    snapshot.AnimalStyle = Enum.Parse<AnimalStyle>(value, ignoreCase: true);
                    break;
                case "--character":
                    snapshot.CharacterPath = value;
                    break;
                case "--set":
                    var kv = value.Split('=');
                    snapshot.Body ??= new BodyShape();
                    snapshot.Body.Set(kv[0], float.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture));
                    break;
            }
        }
        return snapshot;
    }

    public void Begin(MascotBrain brain, Vector2I window)
    {
        float centre = window.X * 0.5f;
        brain.PlaceAt(centre);
        switch (_pose)
        {
            case "walk":
                // Endless floor; the root keeps the character centred in the frame.
                float floorY = brain.Surroundings.Floor.Y;
                brain.SetSurroundings(new Surroundings(2e6f, window.Y,
                    new[] { new DigitalDoll.Core.Mascot.Platform(Surroundings.FloorId, 0f, 2e6f, floorY) }, Array.Empty<Wall>()));
                brain.PlaceAt(1e6f);
                brain.WalkTo(2e6f);
                break;
            case "drag":
                brain.BeginDrag();
                brain.DragTo(centre, window.Y * 0.85f);
                break;
            case "fall":
                brain.BeginDrag();
                brain.DragTo(centre, window.Y * 0.45f);
                brain.EndDrag(0f, 0f);
                break;
            case "climb":
            case "slip":
            case "hang":
                brain.BeginDrag();
                brain.DragTo(4f, window.Y * 0.9f);
                brain.EndDrag(0f, 0f);
                brain.ClimbTo(window.Y * 0.2f);
                // A slip lasts 0.45 s: "slip" is shot during it, "hang" just after.
                if (_pose != "climb")
                    brain.Slip();
                break;
            case "tumble":
                brain.BeginDrag();
                brain.DragTo(centre, window.Y * 0.05f);
                brain.EndDrag(0f, 0f);
                break;
            case "sit":
                // A ledge (a window top, not the floor, where it would sit on the ground) in
                // the middle of the frame so the dangling legs are visible.
                float ledge = window.Y * 0.62f;
                brain.SetSurroundings(new Surroundings(window.X, window.Y,
                    new[]
                    {
                        new DigitalDoll.Core.Mascot.Platform(Surroundings.FloorId, 0f, window.X, window.Y - 1f),
                        new DigitalDoll.Core.Mascot.Platform(1, 0f, window.X, ledge),
                    }, Array.Empty<Wall>()));
                brain.BeginDrag();
                brain.DragTo(centre, ledge - 1f);
                brain.EndDrag(0f, 0f);
                for (int i = 0; i < 60 && brain.State == MascotState.Falling; i++)
                    brain.Update(1f / 60f);
                brain.SitDown();
                break;
            case "lie":
            case "sprawl":
            case "getup":
            case "floorsit":
            case "dance":
                brain.Preview(_pose switch
                {
                    "lie" => MascotState.Lie,
                    "sprawl" => MascotState.Sprawl,
                    "floorsit" => MascotState.FloorSit,
                    "dance" => MascotState.Dance,
                    _ => MascotState.GetUp,
                });
                break;
            case "strike":
                // A pose from the catalog, held (see --strike), on a ledge in the middle of
                // the frame so anything poking through the surface (skirt, legs) shows.
                if (_strike != null && PoseCatalog.Find(_strike) is PoseInfo pose)
                {
                    float shelf = window.Y * 0.9f;
                    brain.SetSurroundings(new Surroundings(window.X, window.Y,
                        new[]
                        {
                            new DigitalDoll.Core.Mascot.Platform(Surroundings.FloorId, 0f, window.X, window.Y - 1f),
                            new DigitalDoll.Core.Mascot.Platform(1, 0f, window.X, shelf),
                        }, Array.Empty<Wall>()));
                    brain.BeginDrag();
                    brain.DragTo(centre, shelf - 1f);
                    brain.EndDrag(0f, 0f);
                    for (int i = 0; i < 60 && brain.State == MascotState.Falling; i++)
                        brain.Update(1f / 60f);
                    brain.StrikePose(pose.Base != PoseBase.Stand, float.MaxValue, pose.Id);
                }
                // An animal's activity (eating, playing with yarn…), on the floor.
                else if (_strike != null && ActivityCatalog.Find(_strike) is ActivityInfo activity)
                {
                    brain.StrikePose(activity.Grounded, float.MaxValue, activity.Id);
                }
                break;
            case "land":
                brain.BeginDrag();
                brain.DragTo(centre, window.Y - 2f);
                brain.EndDrag(0f, 400f);
                break;
        }
    }

    /// <summary>Gesture, mood, talking and petting requested on the command line.</summary>
    public void Act(IPerformer animator)
    {
        animator.Fidgets = false;
        if (_strike != null)
            animator.StrikePose(_strike);
        animator.Talking = _talking;
        animator.Petted = _petting;
        if (_emotion != null && PerformanceDirector.EmotionOf(_emotion) is Emotion emotion)
            animator.Feel(emotion, 1f, 100f);
        if (_gesture == null)
            return;
        var director = new PerformanceDirector(new Random(1));
        var cue = Enum.TryParse<Gesture>(_gesture.Replace("_", ""), ignoreCase: true, out var gesture)
            ? director.Vary(gesture)
            : director.Pick(_gesture);
        if (cue != null)
            animator.Play(cue);
    }

    public void Tick(MascotRoot root)
    {
        _frame++;
        if (_captured || _frame < _time * 60f)
            return;
        _captured = true;
        Action? capture = null;
        capture = () =>
        {
            RenderingServer.FramePostDraw -= capture;
            var error = root.CaptureFrame().SavePng(_output);
            var outline = root.HitOutline();
            GD.Print("hit outline: " + string.Join(" ", Array.ConvertAll(outline, p => $"{p.X:F0},{p.Y:F0}")));
            GD.Print($"snapshot {_pose} t={_time} -> {_output} ({error})");
            root.GetTree().Quit();
        };
        RenderingServer.FramePostDraw += capture;
    }
}
