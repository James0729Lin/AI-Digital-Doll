using System;
using System.Collections.Generic;
using System.Linq;

namespace DigitalDoll.Core.Acting;

/// <summary>What a pose rests on: standing ones end on the spot, the others get up first.</summary>
public enum PoseBase
{
    Stand,
    Kneel,
    Sit,
    Squat,
    Lie,
}

/// <summary>
/// A held pose the character strikes on request ("比個愛心") or when the AI picks it:
/// an id the AI uses, what it rests on, how long it is held, and the words that ask
/// for it (Chinese and English). The motion itself lives with the animator.
/// </summary>
public sealed record PoseInfo(string Id, PoseBase Base, float Seconds, string[] Words, string Description);

public static class PoseCatalog
{
    private static PoseInfo P(string id, PoseBase on, string description, params string[] words) =>
        new(id, on, on == PoseBase.Stand ? 4f : 7f, words, description);

    public static readonly IReadOnlyList<PoseInfo> All = new[]
    {
        // Cute gestures (standing).
        P("heart_hands", PoseBase.Stand, "胸前雙手比愛心", "比愛心", "比心", "愛心", "heart"),
        P("big_heart", PoseBase.Stand, "頭上雙臂比大愛心", "大愛心", "頭上愛心", "big heart"),
        P("finger_heart", PoseBase.Stand, "臉頰旁手指比心", "手指愛心", "指心", "finger heart"),
        P("double_peace", PoseBase.Stand, "下巴下雙手比 YA", "雙手ya", "雙手比ya", "雙ya", "double peace"),
        P("gal_peace", PoseBase.Stand, "手背比 YA", "比ya", "比耶", "peace sign"),
        P("cheek_fists", PoseBase.Stand, "雙拳貼臉頰裝可愛", "裝可愛", "賣萌", "撒嬌", "cute pose"),
        P("bunny_ears", PoseBase.Stand, "頭上兔耳朵", "兔耳", "兔子", "bunny"),
        P("cat_paw", PoseBase.Stand, "招財貓的貓爪", "招財貓", "貓爪", "喵", "cat paw"),
        P("rawr", PoseBase.Stand, "嗷嗚怪獸姿勢", "嗷嗚", "怪獸", "rawr"),
        P("shh", PoseBase.Stand, "食指放嘴唇噓", "噓", "安靜", "shh"),
        P("wink", PoseBase.Stand, "歪頭眨眼", "眨眼", "wink"),
        P("blow_kiss", PoseBase.Stand, "飛吻", "飛吻", "親一個", "blow a kiss"),
        P("flower_chin", PoseBase.Stand, "雙手托腮花朵", "托腮", "花朵", "flower pose"),
        P("poke_cheek", PoseBase.Stand, "食指戳臉頰", "戳臉", "戳臉頰", "poke cheek"),
        P("cover_mouth", PoseBase.Stand, "雙手遮嘴", "遮嘴", "害羞遮嘴", "cover mouth"),
        P("pray", PoseBase.Stand, "雙手合十拜託", "拜託", "合十", "please"),
        P("hug_self", PoseBase.Stand, "抱住自己害羞", "抱自己", "抱抱自己", "hug yourself"),
        P("finger_frame", PoseBase.Stand, "手指框", "手指框", "拍照框", "finger frame"),
        P("tuck_hair", PoseBase.Stand, "把頭髮撥到耳後", "頭髮撥到耳後", "撥到耳後", "tuck hair"),
        P("cheeks_hands", PoseBase.Stand, "雙手捧臉", "捧臉", "捧著臉", "hands on cheeks"),
        P("salute", PoseBase.Stand, "敬禮", "敬禮", "salute"),
        P("point_camera", PoseBase.Stand, "指著鏡頭", "指我", "指向我", "point at me"),
        P("heart_chest", PoseBase.Stand, "單手比心另一手放胸前", "單手愛心", "one hand heart"),
        P("peek_fingers", PoseBase.Stand, "從指縫偷看", "偷看", "指縫", "peek"),

        // Standing poses.
        P("hand_on_hip", PoseBase.Stand, "單手叉腰重心偏一邊", "叉腰", "單手叉腰", "hand on hip"),
        P("hands_on_hips", PoseBase.Stand, "雙手叉腰", "雙手叉腰", "hands on hips"),
        P("hair_up", PoseBase.Stand, "雙手撩起頭髮", "撩頭髮", "雙手撥頭髮", "hair flip"),
        P("look_back", PoseBase.Stand, "回頭看", "回頭", "回眸", "look back"),
        P("reach_up", PoseBase.Stand, "雙手往上伸展", "往上伸展", "往上伸", "reach up"),
        P("hand_behind_head", PoseBase.Stand, "單手放在後腦", "手放頭後", "hand behind head"),
        P("arms_behind", PoseBase.Stand, "雙手背在身後挺胸", "手背後", "雙手背後", "hands behind back"),
        P("leg_back", PoseBase.Stand, "單腳往後勾起", "勾腳", "抬腳", "kick up a heel"),
        P("tiptoe_v", PoseBase.Stand, "踮腳雙手舉成 V", "踮腳", "舉雙手", "tiptoe"),
    };

    /// <summary>
    /// Whole-body actions beside the poses: walking over, dancing, sitting, lying down,
    /// getting up. Like the poses, the AI may ask for them in its cues.
    /// </summary>
    public static readonly IReadOnlyList<string> BodyActions = new[] { "come", "dance", "sit", "floor_sit", "lie", "get_up" };

    public static bool IsBodyAction(string id) => BodyActions.Contains(id);

    private static readonly Dictionary<string, PoseInfo> ById = All.ToDictionary(p => p.Id);

    public static PoseInfo? Find(string id) => ById.TryGetValue(id, out var pose) ? pose : null;

    public static bool Has(string id) => ById.ContainsKey(id);

    /// <summary>The pose a short request asks for ("比個愛心"), if any; longer words win.</summary>
    public static PoseInfo? Match(string text)
    {
        var s = text.Trim().ToLowerInvariant();
        PoseInfo? best = null;
        int bestLength = 0;
        foreach (var pose in All)
        {
            foreach (var word in pose.Words)
            {
                if (word.Length > bestLength && s.Contains(word, StringComparison.Ordinal))
                {
                    best = pose;
                    bestLength = word.Length;
                }
            }
        }
        return best;
    }
}
