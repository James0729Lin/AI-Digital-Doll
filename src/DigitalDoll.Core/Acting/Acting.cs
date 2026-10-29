using System;
using System.Collections.Generic;
using System.Linq;

namespace DigitalDoll.Core.Acting;

/// <summary>Facial mood. The renderer maps each to the expressions a model has.</summary>
public enum Emotion
{
    Neutral,
    Happy,
    Angry,
    Sad,
    Relaxed,
    Surprised,
    Shy,
    Worried,
}

/// <summary>
/// The motions the character can perform on top of what it is doing (standing,
/// walking, sitting…). Each is drawn procedurally, so speed, size and side vary.
/// </summary>
public enum Gesture
{
    Nod,
    Shake,
    Tilt,
    Wave,
    Bow,
    Cheer,
    Clap,
    ThumbsUp,
    Think,
    Shrug,
    Point,
    Count,
    Explain,
    FistPump,
    Facepalm,
    Shy,
    Pout,
    Startle,
    Giggle,
    Hop,
    Droop,
    Sigh,
    Idea,

    // Idle fidgets.
    Stretch,
    LookAround,
    FixHair,
    Yawn,
    ShiftWeight,
    KickLegs,
}

/// <summary>Which part of the body a gesture needs.</summary>
public enum GestureReach
{
    /// <summary>Head and face only: possible lying down or climbing.</summary>
    Head,

    /// <summary>Arms, head and torso: possible while walking or sitting.</summary>
    Upper,

    /// <summary>Feet off the ground or a bow: only while standing still.</summary>
    Whole,

    /// <summary>Only while sitting (dangling legs).</summary>
    Seated,
}

/// <summary>One gesture with its variation already chosen.</summary>
public sealed record GestureCue(Gesture Gesture, float Duration, float Amplitude, bool Mirror, int Count = 1);

/// <summary>A moment in a performance: a mood and up to a few gestures.</summary>
public sealed record Cue(Emotion? Emotion, float Intensity, IReadOnlyList<string> Actions)
{
    public static Cue Of(Emotion emotion, float intensity, params string[] actions) => new(emotion, intensity, actions);
}

public static class GestureInfo
{
    public static GestureReach Reach(Gesture gesture) => gesture switch
    {
        Gesture.Nod or Gesture.Shake or Gesture.Tilt or Gesture.LookAround => GestureReach.Head,
        Gesture.Bow or Gesture.Hop or Gesture.Startle or Gesture.ShiftWeight => GestureReach.Whole,
        Gesture.KickLegs => GestureReach.Seated,
        _ => GestureReach.Upper,
    };

    /// <summary>Base length in seconds before speed variation.</summary>
    public static float Duration(Gesture gesture, int count = 1) => gesture switch
    {
        Gesture.Nod => 0.9f,
        Gesture.Shake => 1.1f,
        Gesture.Tilt => 1.6f,
        Gesture.Wave => 1.8f,
        Gesture.Bow => 1.8f,
        Gesture.Cheer => 1.6f,
        Gesture.Clap => 1.5f,
        Gesture.ThumbsUp => 1.6f,
        Gesture.Think => 2.4f,
        Gesture.Shrug => 1.4f,
        Gesture.Point => 1.6f,
        Gesture.Count => 0.7f + 0.55f * Math.Clamp(count, 1, 5),
        Gesture.Explain => 1.8f,
        Gesture.FistPump => 1.2f,
        Gesture.Facepalm => 2.0f,
        Gesture.Shy => 2.2f,
        Gesture.Pout => 2.0f,
        Gesture.Startle => 0.9f,
        Gesture.Giggle => 1.6f,
        Gesture.Hop => 1.1f,
        Gesture.Droop => 2.0f,
        Gesture.Sigh => 1.8f,
        Gesture.Idea => 1.4f,
        Gesture.Stretch => 3.0f,
        Gesture.LookAround => 3.2f,
        Gesture.FixHair => 2.2f,
        Gesture.Yawn => 2.6f,
        Gesture.ShiftWeight => 2.4f,
        Gesture.KickLegs => 2.6f,
        _ => 1.5f,
    };
}

/// <summary>
/// Turns semantic actions ("celebrate", "agree") into concrete gestures. Each action
/// has several variants; recently used ones are less likely, and every pick gets its
/// own speed, size and side so the same action never plays back exactly the same.
/// </summary>
public sealed class PerformanceDirector
{
    private static readonly Dictionary<string, Gesture[]> Actions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["agree"] = new[] { Gesture.Nod },
        ["nod"] = new[] { Gesture.Nod },
        ["yes"] = new[] { Gesture.Nod },
        ["disagree"] = new[] { Gesture.Shake },
        ["shake"] = new[] { Gesture.Shake },
        ["no"] = new[] { Gesture.Shake },
        ["unsure"] = new[] { Gesture.Tilt, Gesture.Shrug },
        ["confused"] = new[] { Gesture.Tilt, Gesture.Shrug, Gesture.Think },
        ["tilt_head"] = new[] { Gesture.Tilt },
        ["tilt"] = new[] { Gesture.Tilt },
        ["listen"] = new[] { Gesture.Tilt, Gesture.Nod },
        ["celebrate"] = new[] { Gesture.Cheer, Gesture.Clap, Gesture.Hop, Gesture.FistPump },
        ["cheer"] = new[] { Gesture.Cheer, Gesture.Hop },
        ["dance"] = new[] { Gesture.Hop, Gesture.Cheer, Gesture.Clap },
        ["laugh"] = new[] { Gesture.Giggle },
        ["giggle"] = new[] { Gesture.Giggle },
        ["shy"] = new[] { Gesture.Shy },
        ["embarrassed"] = new[] { Gesture.Shy, Gesture.FixHair },
        ["sad"] = new[] { Gesture.Droop },
        ["cry"] = new[] { Gesture.Droop },
        ["angry"] = new[] { Gesture.Pout },
        ["pout"] = new[] { Gesture.Pout },
        ["surprised"] = new[] { Gesture.Startle },
        ["startle"] = new[] { Gesture.Startle },
        ["relieved"] = new[] { Gesture.Sigh },
        ["sigh"] = new[] { Gesture.Sigh },
        ["explain"] = new[] { Gesture.Explain, Gesture.Explain, Gesture.Point },
        ["emphasize"] = new[] { Gesture.FistPump, Gesture.Explain },
        ["count"] = new[] { Gesture.Count },
        ["point"] = new[] { Gesture.Point },
        ["compare"] = new[] { Gesture.Explain, Gesture.Shrug },
        ["greet"] = new[] { Gesture.Wave, Gesture.Bow, Gesture.Wave },
        ["wave"] = new[] { Gesture.Wave },
        ["hello"] = new[] { Gesture.Wave },
        ["bye"] = new[] { Gesture.Wave },
        ["bow"] = new[] { Gesture.Bow },
        ["apologize"] = new[] { Gesture.Bow },
        ["sorry"] = new[] { Gesture.Bow },
        ["thank"] = new[] { Gesture.Bow, Gesture.Nod },
        ["thanks"] = new[] { Gesture.Bow, Gesture.Nod },
        ["encourage"] = new[] { Gesture.FistPump, Gesture.ThumbsUp },
        ["thumbs_up"] = new[] { Gesture.ThumbsUp },
        ["good"] = new[] { Gesture.ThumbsUp },
        ["fist_pump"] = new[] { Gesture.FistPump },
        ["think"] = new[] { Gesture.Think, Gesture.LookAround },
        ["remember"] = new[] { Gesture.Think, Gesture.LookAround },
        ["idea"] = new[] { Gesture.Idea },
        ["facepalm"] = new[] { Gesture.Facepalm },
        ["shrug"] = new[] { Gesture.Shrug },
        ["clap"] = new[] { Gesture.Clap },
        ["jump"] = new[] { Gesture.Hop },
        ["hop"] = new[] { Gesture.Hop },
        ["stretch"] = new[] { Gesture.Stretch },
        ["yawn"] = new[] { Gesture.Yawn },
        ["look_around"] = new[] { Gesture.LookAround },
        ["fix_hair"] = new[] { Gesture.FixHair },
    };

    private static readonly Dictionary<string, Emotion> Emotions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["neutral"] = Emotion.Neutral,
        ["calm"] = Emotion.Relaxed,
        ["happy"] = Emotion.Happy,
        ["joy"] = Emotion.Happy,
        ["glad"] = Emotion.Happy,
        ["excited"] = Emotion.Happy,
        ["fun"] = Emotion.Happy,
        ["proud"] = Emotion.Happy,
        ["love"] = Emotion.Happy,
        ["angry"] = Emotion.Angry,
        ["mad"] = Emotion.Angry,
        ["annoyed"] = Emotion.Angry,
        ["sad"] = Emotion.Sad,
        ["sorrow"] = Emotion.Sad,
        ["cry"] = Emotion.Sad,
        ["sorry"] = Emotion.Sad,
        ["relaxed"] = Emotion.Relaxed,
        ["relieved"] = Emotion.Relaxed,
        ["content"] = Emotion.Relaxed,
        ["gentle"] = Emotion.Relaxed,
        ["surprised"] = Emotion.Surprised,
        ["shock"] = Emotion.Surprised,
        ["shocked"] = Emotion.Surprised,
        ["amazed"] = Emotion.Surprised,
        ["shy"] = Emotion.Shy,
        ["embarrassed"] = Emotion.Shy,
        ["blush"] = Emotion.Shy,
        ["worried"] = Emotion.Worried,
        ["nervous"] = Emotion.Worried,
        ["anxious"] = Emotion.Worried,
        ["confused"] = Emotion.Worried,
    };

    /// <summary>Moods implied by actions written in the emotion slot ("encourage:0.7").</summary>
    private static readonly Dictionary<string, Emotion> ActionMoods = new(StringComparer.OrdinalIgnoreCase)
    {
        ["encourage"] = Emotion.Happy,
        ["celebrate"] = Emotion.Happy,
        ["cheer"] = Emotion.Happy,
        ["laugh"] = Emotion.Happy,
        ["greet"] = Emotion.Happy,
        ["thank"] = Emotion.Happy,
        ["apologize"] = Emotion.Sad,
        ["explain"] = Emotion.Neutral,
        ["think"] = Emotion.Neutral,
    };

    /// <summary>The action names offered to the AI (one per meaning).</summary>
    public static readonly IReadOnlyList<string> PromptActions = new[]
    {
        "agree", "disagree", "tilt_head", "listen",
        "celebrate", "laugh", "shy", "sad", "angry", "surprised", "relieved",
        "explain", "count", "point", "emphasize",
        "greet", "wave", "bow", "apologize", "thank", "encourage", "thumbs_up",
        "think", "idea", "facepalm", "shrug", "clap", "jump", "stretch",
    };

    public static readonly IReadOnlyList<string> PromptEmotions = new[]
    {
        "neutral", "happy", "sad", "angry", "surprised", "relaxed", "shy", "worried",
    };

    private readonly Random _random;
    private readonly List<Gesture> _recent = new();

    public PerformanceDirector(Random random) => _random = random;

    public static bool IsEmotion(string word) => Emotions.ContainsKey(Normalize(word));
    public static bool IsAction(string word)
    {
        string name = Normalize(word);
        return Actions.ContainsKey(name) || PoseCatalog.Has(name) || PoseCatalog.IsBodyAction(name);
    }

    public static Emotion? EmotionOf(string word)
    {
        word = Normalize(word);
        if (Emotions.TryGetValue(word, out var emotion))
            return emotion;
        return ActionMoods.TryGetValue(word, out var mood) ? mood : null;
    }

    /// <summary>Lower case, '-' and ' ' as '_', a few common aliases ("wave_hand" → "wave").</summary>
    public static string Normalize(string word)
    {
        word = word.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
        if (Actions.ContainsKey(word) || Emotions.ContainsKey(word) || PoseCatalog.Has(word) || PoseCatalog.IsBodyAction(word))
            return word;
        foreach (var suffix in new[] { "_hand", "_hands", "_head", "_up", "s", "ing", "ed" })
        {
            if (word.Length > suffix.Length + 2 && word.EndsWith(suffix, StringComparison.Ordinal))
            {
                var stem = word[..^suffix.Length];
                if (Actions.ContainsKey(stem) || Emotions.ContainsKey(stem))
                    return stem;
            }
        }
        return word;
    }

    /// <summary>
    /// Picks the gesture for an action ("count:3" counts to three). Returns null for
    /// unknown actions, which are ignored.
    /// </summary>
    public GestureCue? Pick(string action, float intensity = 0.6f)
    {
        int count = 1;
        int colon = action.IndexOfAny(new[] { ':', '：' });
        if (colon >= 0)
        {
            if (int.TryParse(action[(colon + 1)..].Trim(), out int n))
                count = Math.Clamp(n, 1, 5);
            action = action[..colon];
        }
        if (!Actions.TryGetValue(Normalize(action), out var variants))
            return null;

        // Variants used in the last few picks are much less likely.
        var weights = variants.Select(v => _recent.Contains(v) ? 0.25f : 1f).ToArray();
        float pick = (float)_random.NextDouble() * weights.Sum();
        var gesture = variants[^1];
        for (int i = 0; i < variants.Length; i++)
        {
            pick -= weights[i];
            if (pick <= 0f)
            {
                gesture = variants[i];
                break;
            }
        }
        return Vary(gesture, intensity, count);
    }

    /// <summary>A gesture with random speed (±15 %), size scaled by intensity and a random side.</summary>
    public GestureCue Vary(Gesture gesture, float intensity = 0.6f, int count = 1)
    {
        _recent.Add(gesture);
        if (_recent.Count > 6)
            _recent.RemoveAt(0);
        float speed = 0.85f + 0.3f * (float)_random.NextDouble();
        float amplitude = Math.Clamp(0.65f + 0.5f * intensity, 0.5f, 1.15f) * (0.9f + 0.2f * (float)_random.NextDouble());
        return new GestureCue(gesture, GestureInfo.Duration(gesture, count) / speed, amplitude, _random.Next(2) == 0, count);
    }
}
