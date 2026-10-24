using Godot;

namespace DigitalDoll.Character;

/// <summary>
/// Loads .vrm / .glb files at runtime through the godot-vrm glTF extension, so bundled
/// and user-provided characters go through the same code path.
/// </summary>
public static class VrmLoader
{
    private const string ExtensionScript = "res://addons/vrm/vrm_extension.gd";

    // EditorSceneFormatImporter.IMPORT_GENERATE_TANGENT_ARRAYS; the editor enum is not
    // available in exported builds. Tangents are required for meshes with blend shapes.
    private const uint GenerateTangentArrays = 8;

    public static Node3D? Load(string path)
    {
        var script = GD.Load<GDScript>(ExtensionScript);
        var extension = script.New().AsGodotObject() as GltfDocumentExtension;
        if (extension == null)
        {
            GD.PushError($"VRM extension could not be created from {ExtensionScript}");
            return null;
        }

        GltfDocument.RegisterGltfDocumentExtension(extension, true);
        try
        {
            var document = new GltfDocument();
            var state = new GltfState();
            var error = document.AppendFromFile(path, state, GenerateTangentArrays);
            if (error != Error.Ok)
            {
                GD.PushError($"Failed to read character {path}: {error}");
                return null;
            }
            return document.GenerateScene(state) as Node3D;
        }
        finally
        {
            GltfDocument.UnregisterGltfDocumentExtension(extension);
        }
    }
}
