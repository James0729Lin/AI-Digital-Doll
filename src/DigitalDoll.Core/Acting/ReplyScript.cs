using System;
using System.Collections.Generic;
using System.Text;

namespace DigitalDoll.Core.Acting;

/// <summary>
/// Collects a streamed reply as clean text plus the cues placed in it. When the model
/// writes no cues at all, each finished sentence gets one from <see cref="LocalDirector"/>,
/// so even a model that ignores the rules is acted out.
/// </summary>
public sealed class ReplyScript
{
    private static readonly char[] SentenceEnds = { '。', '！', '？', '!', '?', '\n', '；', '…' };

    private readonly CueParser _parser = new();
    private readonly StringBuilder _raw = new();
    private readonly StringBuilder _text = new();
    private readonly List<(int At, Cue Cue)> _cues = new();
    private int _sentenceStart;

    /// <summary>The reply as received (cues included), for the conversation history.</summary>
    public string Raw => _raw.ToString();

    /// <summary>The reply as shown to the user.</summary>
    public string Text => _text.ToString();

    /// <summary>Cues by character position in <see cref="Text"/>, in order.</summary>
    public IReadOnlyList<(int At, Cue Cue)> Cues => _cues;

    /// <summary>True once the model wrote a cue of its own.</summary>
    public bool Tagged { get; private set; }

    public bool Finished { get; private set; }

    public void Feed(string chunk)
    {
        _raw.Append(chunk);
        Apply(_parser.Feed(chunk));
    }

    public void Finish()
    {
        Apply(_parser.Flush());
        // A trailing sentence without an end mark.
        AnnotateSentence(_text.Length);
        var trimmed = _text.ToString().TrimEnd();
        _text.Length = trimmed.Length;
        Finished = true;
    }

    private void Apply(List<CueParser.Piece> pieces)
    {
        foreach (var piece in pieces)
        {
            if (piece.Cue is Cue cue)
            {
                Tagged = true;
                _cues.Add((_text.Length, cue));
                continue;
            }
            string text = piece.Text!;
            // No leading blank lines in the bubble.
            if (_text.Length == 0)
                text = text.TrimStart();
            foreach (char c in text)
            {
                _text.Append(c);
                if (Array.IndexOf(SentenceEnds, c) >= 0)
                    AnnotateSentence(_text.Length);
            }
        }
    }

    private void AnnotateSentence(int end)
    {
        if (end <= _sentenceStart)
            return;
        int start = _sentenceStart;
        _sentenceStart = end;
        if (Tagged)
            return;
        string sentence = _text.ToString(start, end - start);
        if (sentence.Trim().Length <= 1)
            return;
        if (LocalDirector.Annotate(sentence) is Cue cue)
        {
            // Keep the list ordered: a cue goes where its sentence starts.
            int index = _cues.FindLastIndex(c => c.At <= start) + 1;
            _cues.Insert(index, (start, cue));
        }
    }
}
