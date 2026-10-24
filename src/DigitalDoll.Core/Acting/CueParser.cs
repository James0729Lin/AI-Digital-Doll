using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DigitalDoll.Core.Acting;

/// <summary>
/// Splits a streamed AI reply into clean text and performance cues. Cues are written
/// inline as <c>⟨happy:0.8 wave⟩</c>; the parser also accepts the brackets models tend
/// to substitute (〈〉, [[ ]], &lt;&gt;, 【】) and hides &lt;think&gt; blocks from reasoning
/// models. A bracket whose content does not look like a cue is kept as text.
/// </summary>
public sealed partial class CueParser
{
    private const int MaxTagLength = 64;

    private static readonly (string Open, string Close)[] Brackets =
    {
        ("⟨", "⟩"), ("〈", "〉"), ("[[", "]]"), ("【", "】"), ("<", ">"),
    };

    private readonly StringBuilder _pending = new();
    private bool _thinking;
    private bool _afterCue;

    /// <summary>One piece of the reply: text to show, or a cue to perform.</summary>
    public readonly record struct Piece(string? Text, Cue? Cue);

    /// <summary>Feeds a streamed chunk; returns what can be decided so far.</summary>
    public List<Piece> Feed(string chunk)
    {
        _pending.Append(chunk);
        var pieces = new List<Piece>();
        Drain(pieces, final: false);
        return pieces;
    }

    /// <summary>End of the reply: everything still held back is text.</summary>
    public List<Piece> Flush()
    {
        var pieces = new List<Piece>();
        Drain(pieces, final: true);
        if (_pending.Length > 0 && !_thinking)
            pieces.Add(new Piece(_pending.ToString(), null));
        _pending.Clear();
        _thinking = false;
        return pieces;
    }

    private void Drain(List<Piece> pieces, bool final)
    {
        var text = new StringBuilder();
        while (_pending.Length > 0)
        {
            if (_afterCue)
            {
                // The space that separated the tag from the sentence.
                _afterCue = false;
                if (_pending[0] == ' ')
                    _pending.Remove(0, 1);
                continue;
            }
            string s = _pending.ToString();
            if (_thinking)
            {
                int end = s.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
                if (end < 0)
                {
                    // Keep a possible partial "</think>" for the next chunk.
                    int keep = Math.Min(s.Length, 7);
                    _pending.Remove(0, s.Length - keep);
                    if (final)
                        _pending.Clear();
                    break;
                }
                _pending.Remove(0, end + 8);
                _thinking = false;
                continue;
            }

            int open = -1;
            (string Open, string Close) bracket = ("", "");
            foreach (var b in Brackets)
            {
                int at = s.IndexOf(b.Open, StringComparison.Ordinal);
                if (at >= 0 && (open < 0 || at < open))
                {
                    open = at;
                    bracket = b;
                }
            }

            if (open < 0)
            {
                // A trailing '[' may become "[[": hold it back.
                int hold = !final && s.EndsWith('[') ? 1 : 0;
                text.Append(s, 0, s.Length - hold);
                _pending.Remove(0, s.Length - hold);
                break;
            }

            text.Append(s, 0, open);
            _pending.Remove(0, open);
            s = _pending.ToString();

            int close = s.IndexOf(bracket.Close, bracket.Open.Length, StringComparison.Ordinal);
            if (close < 0)
            {
                if (!final && s.Length < MaxTagLength)
                    break; // Wait for the rest of the tag.
                // Too long to be a cue: the bracket is ordinary text.
                text.Append(bracket.Open);
                _pending.Remove(0, bracket.Open.Length);
                continue;
            }

            string inner = s.Substring(bracket.Open.Length, close - bracket.Open.Length);
            int length = close + bracket.Close.Length;
            if (bracket.Open == "<" && inner.Trim().Equals("think", StringComparison.OrdinalIgnoreCase))
            {
                FlushText(text, pieces);
                _pending.Remove(0, length);
                _thinking = true;
                continue;
            }
            if (bracket.Open == "<" && inner.Trim().Equals("/think", StringComparison.OrdinalIgnoreCase))
            {
                _pending.Remove(0, length);
                continue;
            }

            if (Parse(inner) is Cue cue)
            {
                FlushText(text, pieces);
                pieces.Add(new Piece(null, cue));
                _pending.Remove(0, length);
                _afterCue = true;
            }
            else
            {
                text.Append(bracket.Open);
                _pending.Remove(0, bracket.Open.Length);
            }
        }
        FlushText(text, pieces);
    }

    private static void FlushText(StringBuilder text, List<Piece> pieces)
    {
        if (text.Length == 0)
            return;
        pieces.Add(new Piece(text.ToString(), null));
        text.Clear();
    }

    /// <summary>
    /// Reads "emotion:0.8 action action:2". Every word must be a known emotion or action
    /// (unknown words in an otherwise valid tag are dropped); null when it is not a cue.
    /// </summary>
    public static Cue? Parse(string inner)
    {
        inner = SpaceAfterColon().Replace(inner.Trim(), "$1");
        if (inner.Length == 0 || !TagShape().IsMatch(inner))
            return null;

        Emotion? emotion = null;
        float intensity = 0.6f;
        var actions = new List<string>();
        int known = 0;
        foreach (var raw in inner.Split(new[] { ' ', ',', '，', '|', '/' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string word = raw, value = "";
            int colon = raw.IndexOfAny(new[] { ':', '：', '=' });
            if (colon >= 0)
            {
                word = raw[..colon];
                value = raw[(colon + 1)..];
            }
            string name = PerformanceDirector.Normalize(word);
            bool isEmotion = PerformanceDirector.IsEmotion(name);
            bool isAction = PerformanceDirector.IsAction(name);
            if (!isEmotion && !isAction)
                continue;
            known++;

            bool numberIsIntensity = value.Contains('.') || (isEmotion && !isAction);
            if (emotion == null && actions.Count == 0 && (isEmotion || numberIsIntensity) && PerformanceDirector.EmotionOf(name) is Emotion mood)
            {
                emotion = mood;
                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float level))
                    intensity = Math.Clamp(level, 0f, 1f);
                if (isAction && !isEmotion)
                    actions.Add(name);
                continue;
            }
            if (isAction)
                actions.Add(value.Length > 0 && int.TryParse(value, out _) ? $"{name}:{value}" : name);
            else
                emotion ??= PerformanceDirector.EmotionOf(name);
        }
        return known == 0 ? null : new Cue(emotion, intensity, actions);
    }

    [GeneratedRegex(@"^[A-Za-z_\-]+([:：=]\s*[0-9.]+)?([\s,，|/]+[A-Za-z_\-]+([:：=]\s*[0-9.]+)?)*$")]
    private static partial Regex TagShape();

    [GeneratedRegex(@"([:：=])\s+")]
    private static partial Regex SpaceAfterColon();
}
