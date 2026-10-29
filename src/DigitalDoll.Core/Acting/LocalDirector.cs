using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DigitalDoll.Core.Acting;

/// <summary>
/// Keyword rules for replies that come without cues (models that ignore the
/// performance rules) and for requests the character can act on immediately
/// ("揮揮手", "坐下"), in Chinese and English.
/// </summary>
public static partial class LocalDirector
{
    private sealed record Rule(string[] Words, Emotion Emotion, float Intensity, string? Action);

    private static readonly Rule[] Rules =
    {
        new(new[] { "抱歉", "對不起", "不好意思", "sorry", "apolog" }, Emotion.Sad, 0.5f, "apologize"),
        new(new[] { "謝謝", "感謝", "多謝", "thank" }, Emotion.Happy, 0.6f, "thank"),
        new(new[] { "再見", "掰掰", "拜拜", "晚安", "bye", "good night" }, Emotion.Happy, 0.6f, "wave"),
        new(new[] { "哈哈", "呵呵", "嘻嘻", "好笑", "haha", "lol" }, Emotion.Happy, 0.8f, "laugh"),
        new(new[] { "太好了", "恭喜", "好棒", "太棒", "成功", "耶", "great", "awesome", "congrat", "yay" }, Emotion.Happy, 0.8f, "celebrate"),
        new(new[] { "你好", "您好", "哈囉", "嗨", "早安", "午安", "hello", "hi ", "hey" }, Emotion.Happy, 0.6f, "greet"),
        new(new[] { "加油", "你可以", "沒問題", "相信你", "you can", "good luck" }, Emotion.Happy, 0.7f, "encourage"),
        new(new[] { "害羞", "臉紅", "人家", "blush" }, Emotion.Shy, 0.7f, "shy"),
        new(new[] { "難過", "傷心", "可惜", "遺憾", "哭", "sad", "unfortunately" }, Emotion.Sad, 0.6f, "sad"),
        new(new[] { "生氣", "可惡", "哼", "討厭", "angry", "annoying" }, Emotion.Angry, 0.6f, "angry"),
        new(new[] { "哇", "咦", "真的嗎", "居然", "竟然", "wow", "really?", "whoa" }, Emotion.Surprised, 0.7f, "surprised"),
        new(new[] { "擔心", "緊張", "小心", "注意", "worr", "careful" }, Emotion.Worried, 0.5f, "tilt_head"),
        new(new[] { "讓我想", "想想", "嗯…", "嗯...", "hmm", "let me think" }, Emotion.Neutral, 0.5f, "think"),
        new(new[] { "原來", "我知道了", "有了", "i see", "got it" }, Emotion.Happy, 0.5f, "idea"),
    };

    private static readonly string[] Ordinals = { "第一", "第二", "第三", "第四", "第五" };

    /// <summary>A cue for one sentence of a reply, or null to leave it to the speaking rhythm.</summary>
    public static Cue? Annotate(string sentence)
    {
        var s = sentence.Trim();
        if (s.Length == 0)
            return null;
        var lower = s.ToLowerInvariant();

        for (int i = 0; i < Ordinals.Length; i++)
        {
            if (s.StartsWith(Ordinals[i], StringComparison.Ordinal))
                return Cue.Of(Emotion.Neutral, 0.5f, $"count:{i + 1}");
        }
        if (ListItem().Match(s) is { Success: true } item)
            return Cue.Of(Emotion.Neutral, 0.5f, $"count:{Math.Clamp(int.Parse(item.Groups[1].Value), 1, 5)}");
        if (s.StartsWith("首先", StringComparison.Ordinal) || lower.StartsWith("first", StringComparison.Ordinal))
            return Cue.Of(Emotion.Neutral, 0.5f, "count:1");

        foreach (var rule in Rules)
        {
            if (rule.Words.Any(w => lower.Contains(w, StringComparison.Ordinal)))
                return Cue.Of(rule.Emotion, rule.Intensity, rule.Action is string a ? new[] { a } : Array.Empty<string>());
        }

        if (StartsWithAny(s, "不是", "不對", "不行", "不會", "沒有") || lower.StartsWith("no,", StringComparison.Ordinal) || lower.StartsWith("no ", StringComparison.Ordinal))
            return Cue.Of(Emotion.Neutral, 0.5f, "disagree");
        if (StartsWithAny(s, "對", "是的", "沒錯", "好啊", "好的", "可以", "當然") || StartsWithAny(lower, "yes", "sure", "right", "of course"))
            return Cue.Of(Emotion.Happy, 0.5f, "agree");
        if (s.EndsWith('？') || s.EndsWith('?'))
            return Cue.Of(Emotion.Neutral, 0.5f, "tilt_head");
        if (s.EndsWith('！') || s.EndsWith('!'))
            return Cue.Of(Emotion.Happy, 0.6f, "emphasize");
        return null;
    }

    /// <summary>
    /// Actions the user asked for directly in a short message ("跳一下", "比個愛心").
    /// Besides the performance actions these can be whole-body actions
    /// (<see cref="PoseCatalog.BodyActions"/>) and pose ids (<see cref="PoseCatalog"/>).
    /// </summary>
    public static IReadOnlyList<string> Command(string userText)
    {
        var s = userText.Trim().ToLowerInvariant();
        if (s.Length == 0 || s.Length > 40)
            return Array.Empty<string>();
        var result = new List<string>();
        void Check(string action, params string[] words)
        {
            if (words.Any(w => s.Contains(w, StringComparison.Ordinal)) && !result.Contains(action))
                result.Add(action);
        }
        // A named pose takes precedence over the gestures that share its words.
        if (PoseCatalog.Match(s) is PoseInfo pose)
            return new[] { pose.Id };
        Check("come", "過來", "來這裡", "來這邊", "come here", "come over");
        Check("get_up", "起來", "起床", "站起來", "get up", "stand up");
        Check("floor_sit", "坐地上", "坐在地上", "sit on the floor");
        Check("dance", "跳舞", "跳支舞", "dance");
        if (!result.Contains("dance"))
            Check("jump", "跳一下", "跳跳", "跳起來", "jump");
        Check("wave", "揮手", "揮揮手", "wave");
        Check("bow", "鞠躬", "bow");
        Check("agree", "點頭", "nod");
        Check("disagree", "搖頭", "shake your head");
        Check("clap", "拍手", "鼓掌", "clap");
        Check("stretch", "伸懶腰", "stretch");
        Check("celebrate", "歡呼", "cheer");
        if (!result.Contains("floor_sit"))
            Check("sit", "坐下", "坐著", "休息一下", "sit down");
        Check("lie", "躺下", "睡覺", "去睡", "lie down", "go to sleep");
        return result;
    }

    private static bool StartsWithAny(string s, params string[] prefixes) =>
        prefixes.Any(p => s.StartsWith(p, StringComparison.Ordinal));

    [GeneratedRegex(@"^\s*([1-9])[\.、．)）]")]
    private static partial Regex ListItem();
}
