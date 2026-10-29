using System;
using System.Linq;
using System.Threading;
using DigitalDoll.Core.Acting;
using DigitalDoll.Core.Ai;
using DigitalDoll.Mascot;
using Godot;

namespace DigitalDoll.Chat;

/// <summary>
/// Runs the conversation: sends the question to the chosen AI, streams the reply
/// into the speech bubble with a typewriter effect, and performs each cue (mood and
/// gestures) when the text reaches it. Errors become lines the character says.
/// </summary>
public partial class ChatController : Node
{
    private const float RevealRate = 22f;

    private MascotRoot _root = null!;
    private SpeechBubble _bubble = null!;
    private ChatInput _input = null!;
    private AiSettingsWindow _settingsWindow = null!;
    private AiSettings _settings = new();
    private readonly Conversation _conversation = new();
    private ChatLog _log = new();

    /// <summary>The first message of the AI's current context (later ones are sent along as history).</summary>
    private long _contextSince;
    private PerformanceDirector _director = null!;

    private string? _name, _persona;
    private CancellationTokenSource? _request;
    private ReplyScript? _script;
    private string _source = "";
    private float _revealed;
    private int _nextCue;
    private float _linger;
    private bool _waiting;
    private float _waitTime;

    public void Attach(MascotRoot root)
    {
        _root = root;
        _director = new PerformanceDirector(new Random());
        _settings = AiStore.Load();

        _bubble = new SpeechBubble();
        AddChild(_bubble);
        _input = new ChatInput();
        AddChild(_input);
        _input.Submitted += Ask;
        _input.StopRequested += Stop;
        _input.ActionPicked += action => _root.PerformCommand(action);
        _input.Species = () => _root.AnimalSpecies;
        _log = ChatLogStore.Load();
        _contextSince = _log.NextId;
        _log.Changed += () =>
        {
            ChatLogStore.Save(_log);
            _input.ShowHistory(_log.Entries);
        };
        _input.DeleteRequested += id =>
        {
            if (_log.Remove(id))
                _conversation.Replace(_log.Context(_contextSince));
        };
        _input.ClearRequested += () =>
        {
            _log.Clear();
            _conversation.Clear();
        };
        _input.ShowHistory(_log.Entries);
        _settingsWindow = new AiSettingsWindow();
        AddChild(_settingsWindow);
        _settingsWindow.Saved += saved => _settings = saved;
        CharacterChanged(root.Body?.Name, root.Body?.Persona);

        // Development aid: --ask "question" asks the default AI shortly after start,
        // --ai-settings opens the settings window.
        var args = OS.GetCmdlineUserArgs();
        int ask = Array.IndexOf(args, "--ask");
        if (ask >= 0 && ask + 1 < args.Length)
            GetTree().CreateTimer(1.5).Timeout += () => Ask(args[ask + 1], null);
        if (Array.IndexOf(args, "--ai-settings") >= 0)
            GetTree().CreateTimer(1.0).Timeout += OpenSettings;
    }

    /// <summary>A different character: animals talk as themselves; the history starts afresh.</summary>
    public void CharacterChanged(string? name, string? persona)
    {
        if (name == _name && persona == _persona)
            return;
        _name = name;
        _persona = persona;
        // The history is kept; the AI's context starts again for the new character.
        _conversation.Clear();
        _contextSince = _log.NextId;
    }

    public void OpenChat() => _input.Open(_settings, _root.HeadScreenPosition() + new Vector2I(0, (int)(_root.CharacterHeightPixels * 1.05f)));

    public void OpenSettings() => _settingsWindow.Open(_settings);

    /// <summary>Shows a line in the bubble without asking any AI.</summary>
    public void Say(string text, float seconds = 5f)
    {
        _script = null;
        _source = "";
        _bubble.Say(text, "", _root.HeadScreenPosition());
        _linger = seconds;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        var animator = _root.Animator;
        var brain = _root.Brain;
        if (animator == null || brain == null)
            return;

        bool active = _bubble.Visible;
        if (_waiting)
        {
            // Thinking while the first words are on their way.
            _waitTime += dt;
            int dots = 1 + (int)(_waitTime * 2.5f) % 3;
            _bubble.Say(new string('・', dots), _source, _root.HeadScreenPosition());
        }
        else if (_script != null)
        {
            var text = _script.Text;
            float backlog = text.Length - _revealed;
            _revealed = Mathf.Min(text.Length, _revealed + dt * (RevealRate + MathF.Max(0f, backlog - 40f) * 0.6f));
            int shown = (int)_revealed;
            while (_nextCue < _script.Cues.Count && _script.Cues[_nextCue].At <= shown)
                Perform(_script.Cues[_nextCue++].Cue);
            _bubble.Say(text[..shown], _source, _root.HeadScreenPosition());

            bool speaking = shown < text.Length || !_script.Finished;
            animator.Talking = speaking && shown < text.Length;
            if (!speaking)
            {
                _script = null;
                _linger = Mathf.Clamp(3f + text.Length / 10f, 4f, 30f);
                _input.SetBusy(false);
            }
        }
        else if (_bubble.Visible)
        {
            _linger -= dt;
            if (_linger <= 0f)
                _bubble.Hide();
            _bubble.Follow(_root.HeadScreenPosition());
        }

        _root.FaceUser = active || _input.Visible;
        if (active)
            brain.Hold(1f);
    }

    private async void Ask(string input, string? providerId)
    {
        Stop();
        var animator = _root.Animator!;

        // Requests the character can act on right away ("揮揮手", "坐下").
        foreach (var action in LocalDirector.Command(input, _root.AnimalSpecies))
            _root.PerformCommand(action);

        var (routed, text) = _settings.Route(input);
        var config = providerId != null ? _settings.Providers.Find(p => p.Id == providerId) ?? routed : routed;
        if (config == null)
        {
            animator.Play(_director.Vary(Gesture.Shrug), now: true);
            Say(Tr("CHAT_SETUP_FIRST"), 8f);
            OpenSettings();
            return;
        }
        if (config.Model.Length == 0)
        {
            Say(string.Format(Tr("CHAT_NEED_MODEL"), config.Name), 8f);
            OpenSettings();
            return;
        }
        if (text.Length == 0)
            return;

        _conversation.AddUser(text);
        _log.AddUser(text, DateTimeOffset.Now);
        var request = new ChatRequest(config.Model, Conversation.SystemPrompt(_name == null ? _settings : WithName(_settings, _name), _persona, _root.AnimalSpecies), _conversation.Turns.ToList());
        var cancel = _request = new CancellationTokenSource();
        var script = new ReplyScript();
        _source = config.Name;
        _waiting = true;
        _waitTime = 0f;
        _input.SetBusy(true);
        animator.Play(_director.Vary(Gesture.Think, 0.5f));

        try
        {
            var provider = AiServices.Create(config, KeyStore.Get(config.Id));
            await foreach (var part in provider.StreamAsync(request, cancel.Token))
            {
                if (cancel.IsCancellationRequested)
                    break;
                script.Feed(part);
                if (_waiting && script.Text.Length > 0)
                    BeginReply(script);
            }
            script.Finish();
            if (cancel.IsCancellationRequested)
                return;
            if (_waiting)
                BeginReply(script);
            if (script.Text.Length == 0)
            {
                _conversation.DropUnanswered();
                Fail(new ChatException(ChatErrorKind.Other, Tr("CHAT_EMPTY")));
                return;
            }
            _conversation.AddAssistant(script.Raw);
            _log.AddAssistant(script.Text, script.Raw, config.Name, DateTimeOffset.Now);
        }
        catch (OperationCanceledException)
        {
            _conversation.DropUnanswered();
        }
        catch (Exception e)
        {
            _conversation.DropUnanswered();
            if (!cancel.IsCancellationRequested)
                Fail(e);
        }
        finally
        {
            if (_request == cancel)
                _request = null;
        }
    }

    private static AiSettings WithName(AiSettings settings, string name) => new()
    {
        CharacterName = name,
        Personality = settings.Personality,
        Providers = settings.Providers,
        DefaultProvider = settings.DefaultProvider,
    };

    private void BeginReply(ReplyScript script)
    {
        _waiting = false;
        _script = script;
        _revealed = 0f;
        _nextCue = 0;
        if (_root.Animator?.CurrentGesture == Gesture.Think)
            _root.Animator.StopGestures();
    }

    private void Stop()
    {
        _request?.Cancel();
        _request = null;
        _waiting = false;
        if (_script != null)
        {
            _script = null;
            _linger = 2f;
        }
        if (_root.Animator != null)
            _root.Animator.Talking = false;
        _input.SetBusy(false);
    }

    private void Perform(Cue cue)
    {
        var animator = _root.Animator!;
        if (cue.Emotion is Emotion emotion)
            animator.Feel(emotion, cue.Intensity, 6f);
        foreach (var action in cue.Actions)
        {
            // Poses and whole-body actions move the character; the rest are gestures.
            if (PoseCatalog.Has(action) || PoseCatalog.IsBodyAction(action) || ActivityCatalog.Has(action))
                _root.PerformCommand(action);
            else if (_director.Pick(action, cue.Intensity) is GestureCue gesture)
                animator.Play(gesture);
        }
    }

    private void Fail(Exception e)
    {
        _waiting = false;
        _script = null;
        _input.SetBusy(false);
        var animator = _root.Animator!;
        var kind = (e as ChatException)?.Kind ?? ChatErrorKind.Other;
        var (gesture, emotion) = kind switch
        {
            ChatErrorKind.Unauthorized => (Gesture.Facepalm, Emotion.Worried),
            ChatErrorKind.RateLimited => (Gesture.Sigh, Emotion.Sad),
            ChatErrorKind.Network => (Gesture.Shrug, Emotion.Worried),
            ChatErrorKind.Refused => (Gesture.Shake, Emotion.Worried),
            _ => (Gesture.Tilt, Emotion.Worried),
        };
        animator.Feel(emotion, 0.7f, 5f);
        animator.Play(_director.Vary(gesture), now: true);
        Say(Describe(e, this), 10f);
    }

    /// <summary>A friendly sentence for a failed request.</summary>
    public static string Describe(Exception e, Node translator)
    {
        if (e is not ChatException chat)
            return string.Format(translator.Tr("ERR_OTHER"), e.Message);
        string key = chat.Kind switch
        {
            ChatErrorKind.Unauthorized => "ERR_KEY",
            ChatErrorKind.RateLimited => "ERR_RATE",
            ChatErrorKind.ServerError => "ERR_SERVER",
            ChatErrorKind.Network => "ERR_NETWORK",
            ChatErrorKind.Refused => "ERR_REFUSED",
            _ => "ERR_OTHER",
        };
        string detail = chat.Message.Length > 160 ? chat.Message[..160] + "…" : chat.Message;
        return string.Format(translator.Tr(key), detail);
    }
}
