using System;
using System.Globalization;
using Godot;

namespace DigitalDoll.Creator;

/// <summary>
/// Development aid for the body editor: applies slider values, renders the window to
/// a PNG and quits. Example:
/// <c>godot --path app -- --creator --snapshot out.png --view side --set breast-size=1 --tab chest</c>
/// </summary>
internal sealed class CreatorSnapshot
{
    private readonly System.Collections.Generic.List<(string Key, float Value)> _values = new();
    private string _output = "";
    private float _yaw;
    private string _frame = "full";
    private string? _tab;
    private bool _advanced;
    private int _frames;
    private bool _captured;

    public Vector2I WindowSize { get; private set; } = new(1280, 820);
    public string? Locale { get; private set; }

    public static CreatorSnapshot? FromCommandLine(string[] args)
    {
        int index = Array.IndexOf(args, "--snapshot");
        if (index < 0 || index + 1 >= args.Length)
            return null;

        var snapshot = new CreatorSnapshot { _output = args[index + 1] };
        for (int i = 0; i < args.Length; i++)
        {
            string? next = i + 1 < args.Length ? args[i + 1] : null;
            switch (args[i])
            {
                case "--view" when next != null:
                    snapshot._yaw = next switch { "side" => MathF.PI / 2f, "back" => MathF.PI, "quarter" => MathF.PI / 4f, _ => 0f };
                    break;
                case "--frame" when next != null:
                    snapshot._frame = next;
                    break;
                case "--tab" when next != null:
                    snapshot._tab = next;
                    break;
                case "--lang" when next != null:
                    snapshot.Locale = next;
                    break;
                case "--advanced":
                    snapshot._advanced = true;
                    break;
                case "--size" when next != null:
                    var parts = next.Split('x');
                    snapshot.WindowSize = new Vector2I(int.Parse(parts[0]), int.Parse(parts[1]));
                    break;
                case "--set" when next != null:
                    var kv = next.Split('=');
                    snapshot._values.Add((kv[0], float.Parse(kv[1], CultureInfo.InvariantCulture)));
                    break;
            }
        }
        return snapshot;
    }

    public void Apply(CreatorRoot root)
    {
        root.Shape.Reset();
        foreach (var (key, value) in _values)
            root.SetValue(key, value);
        root.SetAdvanced(_advanced);
    }

    public void Tick(CreatorRoot root)
    {
        _frames++;
        if (_frames == 2)
        {
            if (_tab != null)
                root.ShowTab(_tab);
            root.SetView(_yaw, _frame);
            root.SnapCamera();
        }
        if (_captured || _frames < 8)
            return;
        _captured = true;
        Action? capture = null;
        capture = () =>
        {
            RenderingServer.FramePostDraw -= capture;
            var error = root.GetViewport().GetTexture().GetImage().SavePng(_output);
            GD.Print($"creator snapshot -> {_output} ({error})");
            root.GetTree().Quit();
        };
        RenderingServer.FramePostDraw += capture;
    }
}
