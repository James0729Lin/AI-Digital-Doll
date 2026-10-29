using DigitalDoll.Core.Ai;
using Godot;

namespace DigitalDoll.Chat;

/// <summary>Loads and saves the <see cref="ChatLog"/> in the user data folder (never sent anywhere).</summary>
public static class ChatLogStore
{
    private const string Path = "user://chat_log.json";

    public static ChatLog Load() =>
        ChatLog.FromJson(FileAccess.FileExists(Path) ? FileAccess.GetFileAsString(Path) : null);

    public static void Save(ChatLog log)
    {
        if (SelfTest.Enabled)
            return;
        using var file = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
        if (file == null)
            GD.PushWarning($"Could not save the chat history: {FileAccess.GetOpenError()}");
        else
            file.StoreString(log.ToJson());
    }
}
