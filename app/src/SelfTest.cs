using System;
using Godot;

namespace DigitalDoll;

/// <summary>
/// <c>--selftest</c>: desktop → body editor → desktop, then quit with exit code 0.
/// Used by CI to exercise the window mode and scene switches of an exported build.
/// </summary>
public static class SelfTest
{
    private static int _stage;

    public static bool Enabled { get; } = Array.IndexOf(OS.GetCmdlineUserArgs(), "--selftest") >= 0;

    /// <summary>Called once per scene start; schedules the next step.</summary>
    public static void OnSceneReady(Node scene, string name)
    {
        if (!Enabled)
            return;
        GD.Print($"selftest: {name} ready (stage {_stage})");
        int stage = _stage++;
        scene.GetTree().CreateTimer(1.0).Timeout += () =>
        {
            switch (stage)
            {
                case 0:
                    AppWindow.OpenCreator(scene);
                    break;
                case 1:
                    AppWindow.OpenMascot(scene);
                    break;
                default:
                    GD.Print("selftest: OK");
                    scene.GetTree().Quit(0);
                    break;
            }
        };
    }
}
