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
        P("hug_self", PoseBase.Stand, "雙手握在胸前害羞扭動", "害羞", "抱自己", "hug yourself"),
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

        // Kneeling.
        P("kneel", PoseBase.Kneel, "跪立雙手放大腿", "跪著", "跪下", "kneel"),
        P("kneel_hands_head", PoseBase.Kneel, "跪立雙手放頭後", "跪著手放頭後", "kneel hands behind head"),
        P("kneel_arch", PoseBase.Kneel, "跪立上身往後仰", "跪著往後仰", "kneel and lean back"),
        P("kneel_reach", PoseBase.Kneel, "跪立雙手往上伸", "跪著伸展", "kneel and reach up"),
        P("kneel_hair", PoseBase.Kneel, "跪立一手撥髮一手叉腰", "跪著撥頭髮", "kneel hair"),
        P("one_knee", PoseBase.Kneel, "單膝跪地手放膝上", "單膝跪", "單膝跪地", "one knee"),
        P("one_knee_look_up", PoseBase.Kneel, "單膝跪地抬頭看", "單膝跪著看上面", "look up on one knee"),
        P("kneel_side_hand", PoseBase.Kneel, "跪立往側邊傾手撐地", "跪著側傾", "kneel lean to the side"),

        // Sitting.
        P("seiza", PoseBase.Sit, "正座", "正座", "跪坐", "seiza"),
        P("girl_sit", PoseBase.Sit, "鴨子坐雙手放大腿", "鴨子坐", "女生坐", "girl sit"),
        P("girl_sit_hands_front", PoseBase.Sit, "鴨子坐雙手撐在前面", "鴨子坐手撐前面", "girl sit hands in front"),
        P("girl_sit_hands_head", PoseBase.Sit, "鴨子坐雙手放頭後", "鴨子坐手放頭後", "girl sit hands behind head"),
        P("girl_sit_lean_back", PoseBase.Sit, "鴨子坐雙手撐在後面", "鴨子坐往後撐", "girl sit lean back"),
        P("side_sit", PoseBase.Sit, "側坐雙腳收在一邊", "側坐", "side sit"),
        P("side_sit_cheek", PoseBase.Sit, "側坐托著臉頰", "側坐托腮", "side sit chin in hand"),
        P("knees_hug_tilt", PoseBase.Sit, "抱膝坐膝蓋歪一邊", "抱膝", "抱膝坐", "hug knees"),

        // Squatting.
        P("squat", PoseBase.Squat, "蹲下雙手放膝上", "蹲下", "蹲著", "squat"),
        P("squat_hand_floor", PoseBase.Squat, "蹲下單手撐地", "蹲著手撐地", "squat hand on floor"),
        P("squat_chin", PoseBase.Squat, "蹲下托腮", "蹲著托腮", "squat chin in hand"),
        P("squat_hair", PoseBase.Squat, "蹲下撥頭髮", "蹲著撥頭髮", "squat hair"),
        P("squat_tiptoe", PoseBase.Squat, "踮腳蹲雙手撐地", "踮腳蹲", "tiptoe squat"),
        P("squat_look_back", PoseBase.Squat, "蹲著回頭", "蹲著回頭", "squat look back"),

        // Lying down.
        P("lie_back_arms_up", PoseBase.Lie, "仰躺雙手舉過頭", "仰躺", "躺平", "lie on your back"),
        P("lie_back_knee", PoseBase.Lie, "仰躺單膝立起", "仰躺立膝", "lie back one knee up"),
        P("lie_back_legs_up", PoseBase.Lie, "仰躺雙腳朝天", "腳朝天", "legs up"),
        P("lie_side_chin", PoseBase.Lie, "側躺托腮", "側躺托腮", "側躺", "lie on your side"),
        P("lie_side_arm_up", PoseBase.Lie, "側躺單手舉過頭", "側躺伸手", "lie on your side arm up"),
        P("lie_side_curl", PoseBase.Lie, "側躺彎膝放鬆", "側躺彎膝", "curl up on your side"),
        P("lie_front_kick", PoseBase.Lie, "趴著托腮翹腳", "趴著翹腳", "趴著", "lie on your stomach"),
        P("lie_front_pillow", PoseBase.Lie, "趴著手當枕頭", "趴著睡", "手當枕頭", "head on your arms"),

        // Bending, twisting, arching.
        P("lean_knees", PoseBase.Stand, "彎腰雙手放膝上", "彎腰", "手放膝蓋", "hands on knees"),
        P("touch_shins", PoseBase.Stand, "彎腰摸小腿", "摸小腿", "touch your shins"),
        P("lean_look_up", PoseBase.Stand, "彎腰叉腰抬頭看", "彎腰抬頭", "lean and look up"),
        P("lean_dangle", PoseBase.Stand, "彎腰雙手垂下", "手垂下", "雙手下垂", "let your arms hang"),
        P("lean_leg_back", PoseBase.Stand, "彎腰單腳往後抬", "彎腰抬腳", "lean with a leg back"),
        P("twist_open", PoseBase.Stand, "扭腰張開雙臂", "扭腰", "轉身張開手", "twist"),
        P("kneel_twist_back", PoseBase.Kneel, "跪著轉身回頭", "跪著回頭", "kneel and look back"),
        P("sit_twist", PoseBase.Sit, "坐著扭轉上身", "坐著扭腰", "seated twist"),
        P("stand_arch", PoseBase.Stand, "站著往後仰", "往後仰", "後仰", "lean back"),
        P("arms_up_arch", PoseBase.Stand, "雙手舉高往後仰", "舉手後仰", "reach back"),

        // Stretching, flexibility, gymnastics, yoga.
        P("stretch_twist", PoseBase.Stand, "往上伸展並扭腰", "伸展扭腰", "stretch and twist"),
        P("toe_touch", PoseBase.Stand, "站立前彎雙手碰地", "前彎", "摸腳尖", "碰地", "touch your toes"),
        P("i_balance", PoseBase.Stand, "站立 I 字平衡", "i字平衡", "i字", "抬腿平衡", "i balance"),
        P("v_leg", PoseBase.Stand, "側抬腿成 V 字", "側抬腿", "v字抬腿", "leg raise"),
        P("front_split", PoseBase.Sit, "前後劈腿", "劈腿", "前後劈腿", "split"),
        P("side_split", PoseBase.Sit, "左右劈腿", "一字馬", "左右劈腿", "side split"),
        P("standing_split", PoseBase.Stand, "前彎單腳往後高舉", "站立劈腿", "standing split"),
        P("bridge", PoseBase.Lie, "橋式", "橋式", "下腰", "bridge"),
        P("cat_stretch", PoseBase.Kneel, "瑜珈貓式伸展", "貓式", "貓伸懶腰", "cat stretch"),
        P("table_pose", PoseBase.Sit, "瑜珈桌式", "桌式", "反向桌式", "table pose"),
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
