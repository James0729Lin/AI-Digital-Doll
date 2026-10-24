using DigitalDoll.Core.Ai;
using Godot;

namespace DigitalDoll.Chat;

/// <summary>Loads and saves <see cref="AiSettings"/> in the user data folder.</summary>
public static class AiStore
{
    private const string Path = "user://ai.json";

    public static AiSettings Load() =>
        AiSettings.FromJson(FileAccess.FileExists(Path) ? FileAccess.GetFileAsString(Path) : null);

    public static void Save(AiSettings settings)
    {
        if (SelfTest.Enabled)
            return;
        using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        file?.StoreString(settings.ToJson());
    }
}
