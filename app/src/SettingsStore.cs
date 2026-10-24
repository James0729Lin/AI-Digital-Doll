using DigitalDoll.Core;
using Godot;

namespace DigitalDoll;

/// <summary>Loads and saves <see cref="AppSettings"/> in the user data folder.</summary>
public static class SettingsStore
{
    private const string Path = "user://settings.json";

    public static AppSettings Load() =>
        AppSettings.FromJson(FileAccess.FileExists(Path) ? FileAccess.GetFileAsString(Path) : null);

    public static void Save(AppSettings settings)
    {
        if (SelfTest.Enabled)
            return;
        using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        if (file == null)
            GD.PushWarning($"Could not save settings: {FileAccess.GetOpenError()}");
        else
            file.StoreString(settings.ToJson());
    }

    public static void UseCharacter(CharacterSource source)
    {
        var settings = Load();
        settings.Character = source;
        Save(settings);
    }
}
