using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DigitalDoll.Core.Acting;
using DigitalDoll.Core.Ai;
using Xunit;

namespace DigitalDoll.Core.Tests;

public class ActingTests
{
    private static ReplyScript Stream(string reply, int chunk)
    {
        var script = new ReplyScript();
        for (int i = 0; i < reply.Length; i += chunk)
            script.Feed(reply.Substring(i, Math.Min(chunk, reply.Length - i)));
        script.Finish();
        return script;
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(1000)]
    public void CuesAreRemovedFromTheTextInAnyChunking(int chunk)
    {
        var script = Stream("⟨happy:0.8 wave⟩ 嗨！今天好嗎？⟨worried:0.4 tilt_head⟩ 你看起來有點累。", chunk);
        Assert.Equal("嗨！今天好嗎？你看起來有點累。", script.Text);
        Assert.True(script.Tagged);
        Assert.Equal(2, script.Cues.Count);
        Assert.Equal(0, script.Cues[0].At);
        Assert.Equal(Emotion.Happy, script.Cues[0].Cue.Emotion);
        Assert.Equal(0.8f, script.Cues[0].Cue.Intensity, 3);
        Assert.Equal(new[] { "wave" }, script.Cues[0].Cue.Actions);
        Assert.Equal("嗨！今天好嗎？".Length, script.Cues[1].At);
        Assert.Equal(Emotion.Worried, script.Cues[1].Cue.Emotion);
    }

    [Theory]
    [InlineData("[[happy:0.5 clap]] 好耶")]
    [InlineData("<happy:0.5 clap> 好耶")]
    [InlineData("〈happy: 0.5 clap〉好耶")]
    [InlineData("【Happy:0.5 Clap】 好耶")]
    public void AcceptsOtherBrackets(string reply)
    {
        var script = Stream(reply, 2);
        Assert.Equal("好耶", script.Text);
        Assert.Equal(Emotion.Happy, script.Cues.Single().Cue.Emotion);
        Assert.Equal(new[] { "clap" }, script.Cues.Single().Cue.Actions);
    }

    [Fact]
    public void OrdinaryBracketsStayText()
    {
        var script = Stream("1 < 2 而且 【重點】在這裡，還有 <b>粗體</b>。", 4);
        Assert.Equal("1 < 2 而且 【重點】在這裡，還有 <b>粗體</b>。", script.Text);
        Assert.False(script.Tagged);
    }

    [Fact]
    public void ReasoningBlocksAreHidden()
    {
        var script = Stream("<think>使用者在打招呼，我要回應。</think>⟨happy wave⟩哈囉！", 3);
        Assert.Equal("哈囉！", script.Text);
        Assert.Contains("<think>", script.Raw);
    }

    [Fact]
    public void ActionsInTheEmotionSlotStillWork()
    {
        var cue = CueParser.Parse("encourage:0.7 fist_pump")!;
        Assert.Equal(Emotion.Happy, cue.Emotion);
        Assert.Equal(0.7f, cue.Intensity, 3);
        Assert.Contains("encourage", cue.Actions);
        Assert.Contains("fist_pump", cue.Actions);

        cue = CueParser.Parse("explain count:2")!;
        Assert.Equal(new[] { "explain", "count:2" }, cue.Actions);

        // Misspelt and unknown words are tolerated.
        cue = CueParser.Parse("happy wave_hand dab")!;
        Assert.Equal(new[] { "wave" }, cue.Actions);
        Assert.Null(CueParser.Parse("totally unknown"));
    }

    [Fact]
    public void UntaggedRepliesGetCuesFromTheLocalDirector()
    {
        var script = Stream("對不起，我搞錯了。第一，先存檔。第二，重新開機。加油！", 2);
        Assert.False(script.Tagged);
        var actions = script.Cues.SelectMany(c => c.Cue.Actions).ToList();
        Assert.Equal(new[] { "apologize", "count:1", "count:2", "encourage" }, actions);
        Assert.Equal(0, script.Cues[0].At);
        Assert.True(script.Cues.Select(c => c.At).SequenceEqual(script.Cues.Select(c => c.At).OrderBy(a => a)));
    }

    [Fact]
    public void DirectorVariesGestures()
    {
        var director = new PerformanceDirector(new Random(3));
        var picks = Enumerable.Range(0, 12).Select(_ => director.Pick("celebrate")!.Gesture).ToList();
        Assert.True(picks.Distinct().Count() >= 3);
        var a = director.Pick("wave")!;
        var b = director.Pick("wave")!;
        Assert.NotEqual(a.Duration, b.Duration);
        Assert.Equal(3, director.Pick("count:3")!.Count);
        Assert.Null(director.Pick("moonwalk"));
        foreach (var action in PerformanceDirector.PromptActions)
            Assert.NotNull(director.Pick(action));
    }

    [Fact]
    public void ShortRequestsAreActedOnDirectly()
    {
        Assert.Equal(new[] { "wave" }, LocalDirector.Command("揮揮手"));
        Assert.Equal(new[] { "lie" }, LocalDirector.Command("去睡覺吧"));
        Assert.Equal(new[] { "dance" }, LocalDirector.Command("跳舞給我看"));
        Assert.Empty(LocalDirector.Command("今天天氣如何"));
    }

    [Fact]
    public void PosesAndBodyActionsAreAskedForInWords()
    {
        Assert.Equal(new[] { "heart_hands" }, LocalDirector.Command("比個愛心給我"));
        Assert.Equal(new[] { "big_heart" }, LocalDirector.Command("比一個大愛心"));
        Assert.Equal(new[] { "salute" }, LocalDirector.Command("敬禮！"));
        Assert.Equal(new[] { "come" }, LocalDirector.Command("過來這裡"));
        Assert.Equal(new[] { "floor_sit" }, LocalDirector.Command("坐在地上休息"));
        Assert.Equal(new[] { "get_up" }, LocalDirector.Command("快起床"));
        Assert.Equal(new[] { "dance" }, LocalDirector.Command("可以跳支舞給我看嗎"));
    }

    [Fact]
    public void TheAiCanAskForPosesAndBodyActions()
    {
        var cue = CueParser.Parse("happy:0.8 heart_hands")!;
        Assert.Equal(Emotion.Happy, cue.Emotion);
        Assert.Equal(new[] { "heart_hands" }, cue.Actions);
        Assert.Equal(new[] { "dance" }, CueParser.Parse("happy dance")!.Actions);
        Assert.Equal(new[] { "floor_sit" }, CueParser.Parse("relaxed floor_sit")!.Actions);
        Assert.Contains("heart_hands", Conversation.SystemPrompt(new AiSettings()));
        Assert.Contains("floor_sit", Conversation.SystemPrompt(new AiSettings()));
    }

    [Fact]
    public void PoseIdsAreUniqueAndAskable()
    {
        Assert.Equal(PoseCatalog.All.Count, PoseCatalog.All.Select(p => p.Id).Distinct().Count());
        foreach (var pose in PoseCatalog.All)
        {
            Assert.NotEmpty(pose.Words);
            Assert.True(PerformanceDirector.IsAction(pose.Id), pose.Id);
            Assert.Equal(pose.Id, PerformanceDirector.Normalize(pose.Id));
        }
    }

    [Fact]
    public void ConversationKeepsRecentTurnsStartingWithTheUser()
    {
        var chat = new Conversation(maxTurns: 4);
        for (int i = 0; i < 5; i++)
        {
            chat.AddUser($"q{i}");
            chat.AddAssistant($"a{i}");
        }
        Assert.Equal(new[] { "q3", "a3", "q4", "a4" }, chat.Turns.Select(t => t.Text));
        chat.AddUser("lost");
        chat.DropUnanswered();
        Assert.Equal("a4", chat.Turns[^1].Text);

        var prompt = Conversation.SystemPrompt(new AiSettings { CharacterName = "小雪" });
        Assert.Contains("小雪", prompt);
        Assert.Contains("⟨", prompt);
        Assert.Contains("celebrate", prompt);
    }

    [Fact]
    public void RoutesByHandle()
    {
        var settings = new AiSettings();
        settings.Providers.Add(AiServices.FromPreset(AiServices.Preset("anthropic")!));
        settings.Providers.Add(AiServices.FromPreset(AiServices.Preset("openai")!));
        settings.DefaultProvider = settings.Providers[1].Id;
        Assert.Equal("ChatGPT", settings.Route("你好").Provider!.Name);
        var (provider, text) = settings.Route("@claude 幫我潤稿");
        Assert.Equal("Claude", provider!.Name);
        Assert.Equal("幫我潤稿", text);
        Assert.Equal(settings.ToJson(), AiSettings.FromJson(settings.ToJson()).ToJson());
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request;
        public string? Body;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Response = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(Status) { Content = new StringContent(Response, Encoding.UTF8) };
        }
    }

    [Fact]
    public async Task StreamsOpenAiCompatibleReplies()
    {
        var handler = new FakeHandler
        {
            Response = "data: {\"choices\":[{\"delta\":{\"role\":\"assistant\"}}]}\n\n" +
                       "data: {\"choices\":[{\"delta\":{\"content\":\"⟨happy wave⟩ 嗨\"}}]}\n\n" +
                       ": keep-alive\n\n" +
                       "data: {\"choices\":[{\"delta\":{\"content\":\"！\"}}]}\n\n" +
                       "data: [DONE]\n\n",
        };
        var provider = new OpenAiCompatibleProvider("http://localhost:11434/v1/", "key", new HttpClient(handler));
        var request = new ChatRequest("llama3", "system", new[] { new ChatTurn(ChatRole.User, "hi") });
        var parts = new List<string>();
        await foreach (var part in provider.StreamAsync(request, CancellationToken.None))
            parts.Add(part);

        Assert.Equal(new[] { "⟨happy wave⟩ 嗨", "！" }, parts);
        Assert.Equal("http://localhost:11434/v1/chat/completions", handler.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer key", handler.Request.Headers.Authorization!.ToString());
        Assert.Contains("\"stream\":true", handler.Body);
        Assert.Contains("\"role\":\"system\"", handler.Body);
    }

    [Fact]
    public async Task MapsServiceErrors()
    {
        var handler = new FakeHandler { Status = HttpStatusCode.Unauthorized, Response = "{\"error\":{\"message\":\"bad key\"}}" };
        var provider = new OpenAiCompatibleProvider("https://api.example.com/v1", "x", new HttpClient(handler));
        var error = await Assert.ThrowsAsync<ChatException>(async () =>
        {
            await foreach (var _ in provider.StreamAsync(new ChatRequest("m", "", Array.Empty<ChatTurn>()), CancellationToken.None))
            {
            }
        });
        Assert.Equal(ChatErrorKind.Unauthorized, error.Kind);
        Assert.Contains("bad key", error.Message);
    }
}
