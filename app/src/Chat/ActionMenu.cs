using System;
using System.Collections.Generic;
using System.Linq;
using DigitalDoll.Core;
using DigitalDoll.Core.Acting;
using Godot;

namespace DigitalDoll.Chat;

/// <summary>
/// The "Actions" menu shared by the right-click menu and the chat box: whole-body actions
/// (come, dance, sit…) and, for the doll, every pose from <see cref="PoseCatalog"/>
/// grouped by kind; for an animal, its own activities (<see cref="ActivityCatalog"/>).
/// Picking one hands its action id (as the chat commands use) to the caller. The items
/// are filled in each time it opens, for whoever is on the desktop then.
/// </summary>
public static class ActionMenu
{
    private static readonly (string Id, string Label)[] Body =
    {
        ("come", "BODY_COME"), ("dance", "BODY_DANCE"), ("sit", "BODY_SIT"), ("floor_sit", "BODY_FLOOR_SIT"),
        ("lie", "BODY_LIE"), ("get_up", "BODY_GET_UP"),
    };

    // Animals sit one way only, and sleep when they lie down.
    private static readonly (string Id, string Label)[] AnimalBody =
    {
        ("come", "BODY_COME"), ("dance", "BODY_DANCE"), ("sit", "BODY_SIT"), ("lie", "BODY_SLEEP"),
        ("get_up", "BODY_GET_UP"), ("stretch", "BODY_STRETCH"),
    };

    /// <summary>Builds the menu; <paramref name="tr"/> translates keys, <paramref name="picked"/> receives the action.</summary>
    /// <param name="species">The animal shown when the menu opens, or null for the doll.</param>
    public static PopupMenu Create(Func<string, string> tr, Action<string> picked, Func<Species?> species)
    {
        var menu = new PopupMenu { Name = "Actions" };
        Species? filledFor = null;
        bool filled = false;
        menu.AboutToPopup += () =>
        {
            var now = species();
            if (filled && now == filledFor)
                return;
            filled = true;
            filledFor = now;
            Fill(menu, tr, picked, now);
        };
        Fill(menu, tr, picked, species());
        filled = true;
        filledFor = species();
        return menu;
    }

    private static void Fill(PopupMenu menu, Func<string, string> tr, Action<string> picked, Species? species)
    {
        menu.Clear();
        foreach (var child in menu.GetChildren())
        {
            if (child is PopupMenu)
            {
                menu.RemoveChild(child);
                child.QueueFree();
            }
        }
        bool chinese = TranslationServer.GetLocale().StartsWith("zh");

        var bodyItems = species == null ? Body : AnimalBody;
        var body = new PopupMenu { Name = "Body" };
        for (int i = 0; i < bodyItems.Length; i++)
            body.AddItem(tr(bodyItems[i].Label), i);
        body.IdPressed += id => picked(bodyItems[id].Id);
        menu.AddSubmenuNodeItem(tr("ACTION_GROUP_BODY"), body);

        if (species is Species animal)
        {
            // An animal's own things to do, straight in the menu.
            menu.AddSeparator();
            var activities = ActivityCatalog.For(animal);
            var group = new PopupMenu { Name = "Activities" };
            for (int i = 0; i < activities.Count; i++)
                group.AddItem(chinese ? activities[i].Description : activities[i].English, i);
            group.IdPressed += id => picked(activities[(int)id].Id);
            menu.AddSubmenuNodeItem(tr("ACTION_GROUP_ACTIVITIES"), group);
            return;
        }

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
    }
}
