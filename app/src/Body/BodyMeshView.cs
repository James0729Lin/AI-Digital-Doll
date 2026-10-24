using DigitalDoll.Core.Body;
using Godot;

namespace DigitalDoll.Body;

/// <summary>Unrigged body preview for the editor, rebuilt on every change.</summary>
public partial class BodyMeshView : MeshInstance3D
{
    private readonly ArrayMesh _mesh = new();
    private BodyMeshBuilder _builder = null!;

    public MorphEngine Engine => _builder.Engine;

    /// <summary>Height of the current body in metres (feet to crown).</summary>
    public float BodyHeight => _builder.BodyHeight;

    public override void _Ready()
    {
        _builder = new BodyMeshBuilder();
        Mesh = _mesh;
    }

    public void Show(BodyShape shape) => _builder.Build(shape, _mesh, skinned: false);
}
