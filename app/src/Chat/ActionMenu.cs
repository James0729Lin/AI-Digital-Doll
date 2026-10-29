using System;
using System.Collections.Generic;
using System.Linq;
using DigitalDoll.Core.Acting;
using Godot;

namespace DigitalDoll.Chat;

/// <summary>
/// The "Actions" menu shared by the right-click menu and the chat box: whole-body actions
/// (come, dance, sit…) and every pose from <see cref="PoseCatalog"/>, grouped by kind.
/// Picking one hands its action id (as the chat commands use) to the caller.
/// </summary>
public static class ActionMenu
{
    private static readonly (string Id, string Label)[] Body =
    {
        ("come", "BODY_COME"), ("dance", "BODY_DANCE"), ("sit", "BODY_SIT"), ("floor_sit", "BODY_FLOOR_SIT"),
        ("lie", "BODY_LIE"), ("get_up", "BODY_GET_UP"),
    };

    /// <summary>Builds the menu; <paramref name="tr"/> translates keys, <paramref name="picked"/> receives the action.</summary>
    public static PopupMenu Create(Func<string, string> tr, Action<string> picked)
    {
        bool chinese = TranslationServer.GetLocale().StartsWith("zh");
        var menu = new PopupMenu { Name = "Actions" };

        var body = new PopupMenu { Name = "Body" };
        for (int i = 0; i < Body.Length; i++)
            body.AddItem(tr(Body[i].Label), i);
        body.IdPressed += id => picked(Body[id].Id);
        menu.AddSubmenuNodeItem(tr("ACTION_GROUP_BODY"), body);
        menu.AddSeparator();

        foreach (var group in Enum.GetValues<PoseGroup>())
        {
            var poses = PoseCatalog.All.Where(p => p.Group == group).ToList();
            if (poses.Count == 0)
                continue;
            var sub = new PopupMenu { Name = group.ToString() };
            for (int i = 0; i < poses.Count; i++)
                sub.AddItem(chinese ? poses[i].Description : PoseCatalog.EnglishName(poses[i]), i);
            sub.IdPressed += id => picked(poses[(int)id].Id);
            menu.AddSubmenuNodeItem(tr("POSE_GROUP_" + group.ToString().ToUpperInvariant()), sub);
        }
        return menu;
    }
}
