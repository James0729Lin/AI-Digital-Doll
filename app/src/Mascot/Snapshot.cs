using System;
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

    public Vector2I WindowSize { get; private set; } = new(360, 560);
    public float? Yaw { get; private set; }
    public string? CharacterPath { get; private set; }

    /// <summary>Set by <c>--body</c>: show an edited body (adjusted with <c>--set key=value</c>).</summary>
    public BodyShape? Body { get; private set; }

    public static Snapshot? FromCommandLine(string[] args)
    {
        int index = Array.IndexOf(args, "--snapshot");
        if (index < 0 || index + 1 >= args.Length)
            return null;

        var snapshot = new Snapshot { _output = args[index + 1] };
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
                brain.BeginDrag();
                brain.DragTo(4f, window.Y * 0.9f);
                brain.EndDrag(0f, 0f);
                brain.ClimbTo(window.Y * 0.2f);
                break;
            case "tumble":
                brain.BeginDrag();
                brain.DragTo(centre, window.Y * 0.05f);
                brain.EndDrag(0f, 0f);
                break;
            case "sit":
                // A ledge in the middle of the frame so the dangling legs are visible.
                float ledge = window.Y * 0.62f;
                brain.SetSurroundings(new Surroundings(window.X, window.Y,
                    new[] { new DigitalDoll.Core.Mascot.Platform(Surroundings.FloorId, 0f, window.X, ledge) }, Array.Empty<Wall>()));
                brain.PlaceAt(centre);
                brain.SitDown();
                break;
            case "land":
                brain.BeginDrag();
                brain.DragTo(centre, window.Y - 2f);
                brain.EndDrag(0f, 400f);
                break;
        }
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
