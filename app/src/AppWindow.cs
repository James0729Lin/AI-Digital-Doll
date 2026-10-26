using DigitalDoll.Platform;
using Godot;

namespace DigitalDoll;

/// <summary>Switches between the transparent desktop mode and the normal editor window.</summary>
public static class AppWindow
{
    public const string MascotScene = "res://scenes/Mascot.tscn";
    public const string CreatorScene = "res://scenes/Creator.tscn";

    public static void OpenCreator(Node from) => from.GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, CreatorScene);

    public static void OpenMascot(Node from) => from.GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, MascotScene);

    public static void UseDesktopMode(Window window)
    {
        window.Borderless = true;
        window.AlwaysOnTop = true;
        window.Transparent = true;
        window.TransparentBg = true;
        window.Unresizable = true;
        window.ContentScaleFactor = 1f;
        window.GetTree().AutoAcceptQuit = true;
    }

    public static void UseEditorMode(Window window)
    {
        DisplayServer.WindowSetMousePassthrough(System.Array.Empty<Vector2>());
        window.MousePassthrough = false;
        window.Transparent = false;
        window.TransparentBg = false;
        window.AlwaysOnTop = false;
        window.Borderless = false;
        window.Unresizable = false;
        window.MinSize = new Vector2I(960, 640);

        int screen = window.CurrentScreen;
        var usable = DisplayServer.ScreenGetUsableRect(screen);
        float scale = Mathf.Clamp(DisplayServer.ScreenGetDpi(screen) / 96f, 1f, 3f);
        var size = new Vector2I(
            Mathf.Min((int)(1280 * scale), usable.Size.X - 40),
            Mathf.Min((int)(820 * scale), usable.Size.Y - 60));
        window.Size = size;
        window.Position = usable.Position + (usable.Size - size) / 2;
        window.ContentScaleFactor = scale;

        WindowsNative.ShowInTaskbar(DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle));
    }
}
