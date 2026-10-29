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

/// <summary>The kinds of pose, as the action menus list them.</summary>
public enum PoseGroup
{
    Cute,
    Standing,
    Kneeling,
    Sitting,
    Squatting,
    Lying,
    Bending,
    Stretching,
}

/// <summary>
/// A held pose the character strikes on request ("比個愛心") or when the AI picks it:
/// an id the AI uses, what it rests on, how long it is held, and the words that ask
/// for it (Chinese and English). The motion itself lives with the animator.
/// </summary>
public sealed record PoseInfo(string Id, PoseGroup Group, PoseBase Base, float Seconds, string[] Words, string Description);

public static class PoseCatalog
{
    private static PoseInfo P(PoseGroup group, string id, PoseBase on, string description, params string[] words) =>
        new(id, group, on, on == PoseBase.Stand ? 4f : 7f, words, description);

    /// <summary>The English name for a menu: its last (English) word, capitalised.</summary>
    public static string EnglishName(PoseInfo pose)
    {
        string word = pose.Words[^1];
        return word.Length == 0 ? pose.Id : char.ToUpperInvariant(word[0]) + word[1..];
    }

    public static readonly IReadOnlyList<PoseInfo> All = new[]
    {
        // Cute gestures (standing).
        P(PoseGroup.Cute, "heart_hands", PoseBase.Stand, "胸前雙手比愛心", "比愛心", "比心", "愛心", "heart"),
        P(PoseGroup.Cute, "big_heart", PoseBase.Stand, "頭上雙臂比大愛心", "大愛心", "頭上愛心", "big heart"),
        P(PoseGroup.Cute, "finger_heart", PoseBase.Stand, "臉頰旁手指比心", "手指愛心", "指心", "finger heart"),
        P(PoseGroup.Cute, "double_peace", PoseBase.Stand, "下巴下雙手比 YA", "雙手ya", "雙手比ya", "雙ya", "double peace"),
        P(PoseGroup.Cute, "gal_peace", PoseBase.Stand, "手背比 YA", "比ya", "比耶", "peace sign"),
        P(PoseGroup.Cute, "cheek_fists", PoseBase.Stand, "雙拳貼臉頰裝可愛", "裝可愛", "賣萌", "撒嬌", "cute pose"),
        P(PoseGroup.Cute, "bunny_ears", PoseBase.Stand, "頭上兔耳朵", "兔耳", "兔子", "bunny"),
        P(PoseGroup.Cute, "cat_paw", PoseBase.Stand, "招財貓的貓爪", "招財貓", "貓爪", "喵", "cat paw"),
        P(PoseGroup.Cute, "rawr", PoseBase.Stand, "嗷嗚怪獸姿勢", "嗷嗚", "怪獸", "rawr"),
        P(PoseGroup.Cute, "shh", PoseBase.Stand, "食指放嘴唇噓", "噓", "安靜", "shh"),
        P(PoseGroup.Cute, "wink", PoseBase.Stand, "歪頭眨眼", "眨眼", "wink"),
        P(PoseGroup.Cute, "blow_kiss", PoseBase.Stand, "飛吻", "飛吻", "親一個", "blow a kiss"),
        P(PoseGroup.Cute, "flower_chin", PoseBase.Stand, "雙手托腮花朵", "托腮", "花朵", "flower pose"),
        P(PoseGroup.Cute, "poke_cheek", PoseBase.Stand, "食指戳臉頰", "戳臉", "戳臉頰", "poke cheek"),
        P(PoseGroup.Cute, "cover_mouth", PoseBase.Stand, "雙手遮嘴", "遮嘴", "害羞遮嘴", "cover mouth"),
        P(PoseGroup.Cute, "pray", PoseBase.Stand, "雙手合十拜託", "拜託", "合十", "please"),
        P(PoseGroup.Cute, "hug_self", PoseBase.Stand, "雙手握在胸前害羞扭動", "害羞", "抱自己", "hug yourself"),
        P(PoseGroup.Cute, "finger_frame", PoseBase.Stand, "手指框", "手指框", "拍照框", "finger frame"),
        P(PoseGroup.Cute, "tuck_hair", PoseBase.Stand, "把頭髮撥到耳後", "頭髮撥到耳後", "撥到耳後", "tuck hair"),
        P(PoseGroup.Cute, "cheeks_hands", PoseBase.Stand, "雙手捧臉", "捧臉", "捧著臉", "hands on cheeks"),
        P(PoseGroup.Cute, "salute", PoseBase.Stand, "敬禮", "敬禮", "salute"),
        P(PoseGroup.Cute, "point_camera", PoseBase.Stand, "指著鏡頭", "指我", "指向我", "point at me"),
        P(PoseGroup.Cute, "heart_chest", PoseBase.Stand, "單手比心另一手放胸前", "單手愛心", "one hand heart"),
        P(PoseGroup.Cute, "peek_fingers", PoseBase.Stand, "從指縫偷看", "偷看", "指縫", "peek"),

        // Standing poses.
        P(PoseGroup.Standing, "hand_on_hip", PoseBase.Stand, "單手叉腰重心偏一邊", "叉腰", "單手叉腰", "hand on hip"),
        P(PoseGroup.Standing, "hands_on_hips", PoseBase.Stand, "雙手叉腰", "雙手叉腰", "hands on hips"),
        P(PoseGroup.Standing, "hair_up", PoseBase.Stand, "雙手撩起頭髮", "撩頭髮", "雙手撥頭髮", "hair flip"),
        P(PoseGroup.Standing, "look_back", PoseBase.Stand, "回頭看", "回頭", "回眸", "look back"),
        P(PoseGroup.Standing, "reach_up", PoseBase.Stand, "雙手往上伸展", "往上伸展", "往上伸", "reach up"),
        P(PoseGroup.Standing, "hand_behind_head", PoseBase.Stand, "單手放在後腦", "手放頭後", "hand behind head"),
        P(PoseGroup.Standing, "arms_behind", PoseBase.Stand, "雙手背在身後挺胸", "手背後", "雙手背後", "hands behind back"),
        P(PoseGroup.Standing, "leg_back", PoseBase.Stand, "單腳往後勾起", "勾腳", "抬腳", "kick up a heel"),
        P(PoseGroup.Standing, "tiptoe_v", PoseBase.Stand, "踮腳雙手舉成 V", "踮腳", "舉雙手", "tiptoe"),

        // Kneeling.
        P(PoseGroup.Kneeling, "kneel", PoseBase.Kneel, "跪立雙手放大腿", "跪著", "跪下", "kneel"),
        P(PoseGroup.Kneeling, "kneel_hands_head", PoseBase.Kneel, "跪立雙手放頭後", "跪著手放頭後", "kneel hands behind head"),
        P(PoseGroup.Kneeling, "kneel_arch", PoseBase.Kneel, "跪立上身往後仰", "跪著往後仰", "kneel and lean back"),
        P(PoseGroup.Kneeling, "kneel_reach", PoseBase.Kneel, "跪立雙手往上伸", "跪著伸展", "kneel and reach up"),
        P(PoseGroup.Kneeling, "kneel_hair", PoseBase.Kneel, "跪立一手撥髮一手叉腰", "跪著撥頭髮", "kneel hair"),
        P(PoseGroup.Kneeling, "one_knee", PoseBase.Kneel, "單膝跪地手放膝上", "單膝跪", "單膝跪地", "one knee"),
        P(PoseGroup.Kneeling, "one_knee_look_up", PoseBase.Kneel, "單膝跪地抬頭看", "單膝跪著看上面", "look up on one knee"),
        P(PoseGroup.Kneeling, "kneel_side_hand", PoseBase.Kneel, "跪立往側邊傾手撐地", "跪著側傾", "kneel lean to the side"),

        // Sitting.
        P(PoseGroup.Sitting, "seiza", PoseBase.Sit, "正座", "正座", "跪坐", "seiza"),
        P(PoseGroup.Sitting, "girl_sit", PoseBase.Sit, "鴨子坐雙手放大腿", "鴨子坐", "女生坐", "girl sit"),
        P(PoseGroup.Sitting, "girl_sit_hands_front", PoseBase.Sit, "鴨子坐雙手撐在前面", "鴨子坐手撐前面", "girl sit hands in front"),
        P(PoseGroup.Sitting, "girl_sit_hands_head", PoseBase.Sit, "鴨子坐雙手放頭後", "鴨子坐手放頭後", "girl sit hands behind head"),
        P(PoseGroup.Sitting, "girl_sit_lean_back", PoseBase.Sit, "鴨子坐雙手撐在後面", "鴨子坐往後撐", "girl sit lean back"),
        P(PoseGroup.Sitting, "side_sit", PoseBase.Sit, "側坐雙腳收在一邊", "側坐", "side sit"),
        P(PoseGroup.Sitting, "side_sit_cheek", PoseBase.Sit, "側坐托著臉頰", "側坐托腮", "side sit chin in hand"),
        P(PoseGroup.Sitting, "knees_hug_tilt", PoseBase.Sit, "抱膝坐膝蓋歪一邊", "抱膝", "抱膝坐", "hug knees"),

        // Squatting.
        P(PoseGroup.Squatting, "squat", PoseBase.Squat, "蹲下雙手放膝上", "蹲下", "蹲著", "squat"),
        P(PoseGroup.Squatting, "squat_hand_floor", PoseBase.Squat, "蹲下單手撐地", "蹲著手撐地", "squat hand on floor"),
        P(PoseGroup.Squatting, "squat_chin", PoseBase.Squat, "蹲下托腮", "蹲著托腮", "squat chin in hand"),
        P(PoseGroup.Squatting, "squat_hair", PoseBase.Squat, "蹲下撥頭髮", "蹲著撥頭髮", "squat hair"),
        P(PoseGroup.Squatting, "squat_tiptoe", PoseBase.Squat, "踮腳蹲雙手撐地", "踮腳蹲", "tiptoe squat"),
        P(PoseGroup.Squatting, "squat_look_back", PoseBase.Squat, "蹲著回頭", "蹲著回頭", "squat look back"),

        // Lying down.
        P(PoseGroup.Lying, "lie_back_arms_up", PoseBase.Lie, "仰躺雙手舉過頭", "仰躺", "躺平", "lie on your back"),
        P(PoseGroup.Lying, "lie_back_knee", PoseBase.Lie, "仰躺單膝立起", "仰躺立膝", "lie back one knee up"),
        P(PoseGroup.Lying, "lie_back_legs_up", PoseBase.Lie, "仰躺雙腳朝天", "腳朝天", "legs up"),
        P(PoseGroup.Lying, "lie_side_chin", PoseBase.Lie, "側躺托腮", "側躺托腮", "側躺", "lie on your side"),
        P(PoseGroup.Lying, "lie_side_arm_up", PoseBase.Lie, "側躺單手舉過頭", "側躺伸手", "lie on your side arm up"),
        P(PoseGroup.Lying, "lie_side_curl", PoseBase.Lie, "側躺彎膝放鬆", "側躺彎膝", "curl up on your side"),
        P(PoseGroup.Lying, "lie_front_kick", PoseBase.Lie, "趴著托腮翹腳", "趴著翹腳", "趴著", "lie on your stomach"),
        P(PoseGroup.Lying, "lie_front_pillow", PoseBase.Lie, "趴著手當枕頭", "趴著睡", "手當枕頭", "head on your arms"),

        // Bending, twisting, arching.
        P(PoseGroup.Bending, "lean_knees", PoseBase.Stand, "彎腰雙手放膝上", "彎腰", "手放膝蓋", "hands on knees"),
        P(PoseGroup.Bending, "touch_shins", PoseBase.Stand, "彎腰摸小腿", "摸小腿", "touch your shins"),
        P(PoseGroup.Bending, "lean_look_up", PoseBase.Stand, "彎腰叉腰抬頭看", "彎腰抬頭", "lean and look up"),
        P(PoseGroup.Bending, "lean_dangle", PoseBase.Stand, "彎腰雙手垂下", "手垂下", "雙手下垂", "let your arms hang"),
        P(PoseGroup.Bending, "lean_leg_back", PoseBase.Stand, "彎腰單腳往後抬", "彎腰抬腳", "lean with a leg back"),
        P(PoseGroup.Bending, "twist_open", PoseBase.Stand, "扭腰張開雙臂", "扭腰", "轉身張開手", "twist"),
        P(PoseGroup.Bending, "kneel_twist_back", PoseBase.Kneel, "跪著轉身回頭", "跪著回頭", "kneel and look back"),
        P(PoseGroup.Bending, "sit_twist", PoseBase.Sit, "坐著扭轉上身", "坐著扭腰", "seated twist"),
        P(PoseGroup.Bending, "stand_arch", PoseBase.Stand, "站著往後仰", "往後仰", "後仰", "lean back"),
        P(PoseGroup.Bending, "arms_up_arch", PoseBase.Stand, "雙手舉高往後仰", "舉手後仰", "reach back"),

        // Stretching, flexibility, gymnastics, yoga.
        P(PoseGroup.Stretching, "stretch_twist", PoseBase.Stand, "往上伸展並扭腰", "伸展扭腰", "stretch and twist"),
        P(PoseGroup.Stretching, "toe_touch", PoseBase.Stand, "站立前彎雙手碰地", "前彎", "摸腳尖", "碰地", "touch your toes"),
        P(PoseGroup.Stretching, "i_balance", PoseBase.Stand, "站立 I 字平衡", "i字平衡", "i字", "抬腿平衡", "i balance"),
        P(PoseGroup.Stretching, "v_leg", PoseBase.Stand, "側抬腿成 V 字", "側抬腿", "v字抬腿", "leg raise"),
        P(PoseGroup.Stretching, "front_split", PoseBase.Sit, "前後劈腿", "劈腿", "前後劈腿", "split"),
        P(PoseGroup.Stretching, "side_split", PoseBase.Sit, "左右劈腿", "一字馬", "左右劈腿", "side split"),
        P(PoseGroup.Stretching, "standing_split", PoseBase.Stand, "前彎單腳往後高舉", "站立劈腿", "standing split"),
        P(PoseGroup.Stretching, "bridge", PoseBase.Lie, "橋式", "橋式", "下腰", "bridge"),
        P(PoseGroup.Stretching, "cat_stretch", PoseBase.Kneel, "瑜珈貓式伸展", "貓式", "貓伸懶腰", "cat stretch"),
        P(PoseGroup.Stretching, "table_pose", PoseBase.Sit, "瑜珈桌式", "桌式", "反向桌式", "table pose"),
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
