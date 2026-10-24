using System.Collections.Generic;
using System.Linq;

namespace DigitalDoll.Core.Body;

/// <summary>Where hair falls to, measured on the body it is grown on.</summary>
public enum HairEnd
{
    Ears,
    Chin,
    Shoulders,
    UpperBack,
    MidBack,
}

/// <summary>A procedural hair style: a scalp layer plus locks grown from the head.</summary>
public sealed record HairStyle(
    string Id,
    string Zh,
    string En,
    bool HasCap = true,
    HairEnd? Length = null,
    bool Ponytail = false,
    bool Twintails = false,
    bool FrontLocks = false,
    float Volume = 1f);

public sealed record HairBangs(string Id, string Zh, string En);

public static class HairCatalog
{
    public const string Default = "bob";
    public const string DefaultBangs = "straight";

    public static readonly IReadOnlyList<HairStyle> Styles = new[]
    {
        new HairStyle("none", "光頭", "Bald", HasCap: false),
        new HairStyle("buzz", "平頭", "Buzz cut", Volume: 0.4f),
        new HairStyle("short", "短髮", "Short", Length: HairEnd.Ears),
        new HairStyle("bob", "鮑伯頭", "Bob", Length: HairEnd.Chin, Volume: 1.15f),
        new HairStyle("medium", "及肩髮", "Shoulder length", Length: HairEnd.Shoulders),
        new HairStyle("long", "長直髮", "Long", Length: HairEnd.MidBack, FrontLocks: true),
        new HairStyle("ponytail", "馬尾", "Ponytail", Length: HairEnd.Ears, Ponytail: true, Volume: 0.8f),
        new HairStyle("twintails", "雙馬尾", "Twin tails", Length: HairEnd.Ears, Twintails: true, Volume: 0.8f),
    };

    public static readonly IReadOnlyList<HairBangs> Bangs = new[]
    {
        new HairBangs("straight", "齊瀏海", "Straight bangs"),
        new HairBangs("side", "旁分瀏海", "Side-swept"),
        new HairBangs("parted", "中分", "Middle part"),
        new HairBangs("none", "無瀏海", "No bangs"),
    };

    public static readonly IReadOnlyDictionary<string, HairStyle> ById = Styles.ToDictionary(s => s.Id);
    public static readonly IReadOnlyDictionary<string, HairBangs> BangsById = Bangs.ToDictionary(b => b.Id);
}
