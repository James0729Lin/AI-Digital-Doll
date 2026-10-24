using System.Collections.Generic;
using Godot;

namespace DigitalDoll.Character;

/// <summary>
/// Drives VRM expressions (blink, happy, aa…) by weight. godot-vrm imports each
/// expression as an animation of blend-shape tracks; we read those tracks once and
/// then set the blend shapes directly so several expressions can be mixed freely.
/// </summary>
public sealed class ExpressionRig
{
    private readonly record struct Binding(MeshInstance3D Mesh, int BlendShape, float Value);

    private readonly Dictionary<string, List<Binding>> _expressions = new();
    private readonly Dictionary<(MeshInstance3D, int), float> _frame = new();

    private ExpressionRig()
    {
    }

    public IEnumerable<string> Names => _expressions.Keys;

    public static ExpressionRig Create(Node root)
    {
        var rig = new ExpressionRig();
        var player = root.GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
        if (player == null)
            return rig;

        var animationRoot = player.GetNodeOrNull(player.RootNode) ?? root;
        foreach (var name in player.GetAnimationList())
        {
            if (name == "RESET")
                continue;
            var bindings = ReadBindings(player.GetAnimation(name), animationRoot);
            if (bindings.Count > 0)
                rig._expressions[name] = bindings;
        }
        return rig;
    }

    public bool Has(string expression) => _expressions.ContainsKey(expression);

    /// <summary>Starts a frame; blend shapes that are not set again return to zero.</summary>
    public void Begin()
    {
        foreach (var key in new List<(MeshInstance3D, int)>(_frame.Keys))
            _frame[key] = 0f;
    }

    public void Set(string expression, float weight)
    {
        if (weight <= 0f || !_expressions.TryGetValue(expression, out var bindings))
            return;
        foreach (var b in bindings)
        {
            var key = (b.Mesh, b.BlendShape);
            _frame[key] = Mathf.Clamp(_frame.GetValueOrDefault(key) + b.Value * weight, 0f, 1f);
        }
    }

    public void Commit()
    {
        foreach (var ((mesh, shape), value) in _frame)
        {
            if (GodotObject.IsInstanceValid(mesh))
                mesh.SetBlendShapeValue(shape, value);
        }
    }

    private static List<Binding> ReadBindings(Animation animation, Node animationRoot)
    {
        var bindings = new List<Binding>();
        for (int track = 0; track < animation.GetTrackCount(); track++)
        {
            if (animation.TrackGetKeyCount(track) == 0)
                continue;

            var path = animation.TrackGetPath(track);
            string? shapeName = null;
            if (animation.TrackGetType(track) == Animation.TrackType.BlendShape)
                shapeName = path.GetConcatenatedSubNames();
            else if (animation.TrackGetType(track) == Animation.TrackType.Value)
            {
                var property = path.GetConcatenatedSubNames();
                if (property.StartsWith("blend_shapes/"))
                    shapeName = property["blend_shapes/".Length..];
            }
            if (string.IsNullOrEmpty(shapeName))
                continue;

            var nodePath = new NodePath(path.GetConcatenatedNames());
            if (animationRoot.GetNodeOrNull(nodePath) is not MeshInstance3D mesh)
                continue;
            int shape = mesh.FindBlendShapeByName(shapeName);
            if (shape < 0)
                continue;

            float value = 0f;
            for (int key = 0; key < animation.TrackGetKeyCount(track); key++)
                value = Mathf.Max(value, (float)animation.TrackGetKeyValue(track, key));
            if (value > 0f)
                bindings.Add(new Binding(mesh, shape, value));
        }
        return bindings;
    }
}
