using DigitalDoll.Core.Body;
using Godot;

namespace DigitalDoll.Body;

/// <summary>
/// Builds a rigged character from a body made in the editor: a humanoid Skeleton3D
/// (VRM bone names, joints placed on the morphed body) and the skinned mesh. The
/// desktop mode animates it exactly like an imported VRM.
/// </summary>
public static class BodyCharacter
{
    public static Node3D Create(BodyShape shape)
    {
        var builder = new BodyMeshBuilder();
        var mesh = new ArrayMesh();
        builder.Build(shape, mesh, skinned: true);

        var pack = builder.Engine.Pack;
        var heads = builder.Engine.BoneHeads(builder.Positions);
        Vector3 Head(int bone) => new(heads[bone * 3], heads[bone * 3 + 1], heads[bone * 3 + 2]);

        var root = new Node3D { Name = "BodyCharacter" };
        var skeleton = new Skeleton3D { Name = "Skeleton3D" };
        root.AddChild(skeleton);

        // Rest poses are pure translations: joint offsets from the parent joint. The
        // procedural animation works in the character's frame, so bone axes need no roll.
        var skin = new Skin();
        for (int b = 0; b < pack.Bones.Count; b++)
        {
            var bone = pack.Bones[b];
            skeleton.AddBone(bone.Name);
            if (bone.Parent >= 0)
                skeleton.SetBoneParent(b, bone.Parent);
            var local = bone.Parent >= 0 ? Head(b) - Head(bone.Parent) : Head(b);
            skeleton.SetBoneRest(b, new Transform3D(Basis.Identity, local));
            skin.AddNamedBind(bone.Name, new Transform3D(Basis.Identity, -Head(b)));
        }
        skeleton.ResetBonePoses();

        var body = new MeshInstance3D { Name = "Body", Mesh = mesh, Skin = skin };
        skeleton.AddChild(body);
        body.Skeleton = new NodePath("..");
        BodyJiggle.Attach(skeleton, body, builder.JiggleMaterials, shape);
        return root;
    }
}
