using System;
using System.Collections.Generic;
using System.Linq;

namespace DigitalDoll.Core.Acting;

/// <summary>The thing that appears beside an animal for an activity (and goes away after).</summary>
public enum ActivityProp
{
    None,
    YarnBall,
    FoodBowl,
    Trough,
    Grain,
    Ball,
    Bone,
    Mud,
}

/// <summary>
/// Something an animal does on the spot for a while, often with a prop: playing with a
/// ball of yarn, eating from a bowl. Held like the doll's poses (<see cref="Mascot.MascotState.Pose"/>
/// with the activity's id).
/// </summary>
/// <param name="Grounded">Done lying or crouched down: it gets up afterwards.</param>
public sealed record ActivityInfo(string Id, string Description, string English, float Seconds, bool Grounded, Species[] Species)
{
    public bool For(Species species) => Species.Contains(species);

    /// <summary>The prop this species uses for it (one activity, "eat", has a different one for each).</summary>
    public ActivityProp Prop(Species species) => Id switch
    {
        "yarn" => ActivityProp.YarnBall,
        "eat" => species switch
        {
            Core.Species.Pig => ActivityProp.Trough,
            Core.Species.Chicken => ActivityProp.Grain,
            _ => ActivityProp.FoodBowl,
        },
        "ball" => ActivityProp.Ball,
        "bone" => ActivityProp.Bone,
        "roll" => ActivityProp.Mud,
        _ => ActivityProp.None,
    };
}

public static class ActivityCatalog
{
    private static readonly Species[] Everyone = Enum.GetValues<Species>();

    public static readonly IReadOnlyList<ActivityInfo> All = new ActivityInfo[]
    {
        new("eat", "吃飯", "Eat", 9f, false, Everyone),
        new("yarn", "玩毛線球", "Play with yarn", 10f, false, new[] { Species.Cat }),
        new("wash_face", "洗臉", "Wash face", 6f, false, new[] { Species.Cat }),
        new("ball", "玩球", "Play ball", 10f, false, new[] { Species.Dog }),
        new("bone", "啃骨頭", "Chew a bone", 10f, true, new[] { Species.Dog }),
        new("roll", "在泥巴裡打滾", "Roll in the mud", 8f, true, new[] { Species.Pig }),
    };

    private static readonly Dictionary<string, ActivityInfo> ById = All.ToDictionary(a => a.Id);

    public static ActivityInfo? Find(string id) => ById.TryGetValue(id, out var a) ? a : null;

    public static bool Has(string id) => ById.ContainsKey(id);

    public static IReadOnlyList<ActivityInfo> For(Species species) => All.Where(a => a.For(species)).ToList();

    /// <summary>Words that ask for an activity ("去吃飯", "玩毛線"), Chinese and English.</summary>
    private static readonly Dictionary<string, string[]> Words = new()
    {
        ["eat"] = new[] { "吃飯", "吃東西", "吃點東西", "肚子餓", "餓了", "飼料", "貓糧", "狗糧", "啄米", "go eat", "time to eat", "hungry" },
        ["yarn"] = new[] { "毛線", "yarn" },
        ["wash_face"] = new[] { "洗臉", "洗洗臉", "wash your face" },
        ["ball"] = new[] { "玩球", "丟球", "撿球", "接球", "fetch", "play ball" },
        ["bone"] = new[] { "骨頭", "bone" },
        ["roll"] = new[] { "打滾", "滾一滾", "泥巴", "roll around" },
    };

    /// <summary>The activity a short request asks this species for, if any.</summary>
    public static ActivityInfo? Match(string text, Species species)
    {
        var s = text.ToLowerInvariant();
        foreach (var activity in For(species))
        {
            if (Words.TryGetValue(activity.Id, out var words) && words.Any(w => s.Contains(w, StringComparison.Ordinal)))
                return activity;
        }
        return null;
    }
}
