using System;
using System.Collections.Generic;
using System.Linq;
using DigitalDoll.Core.Acting;

namespace DigitalDoll.Core.Ai;

/// <summary>
/// The running chat: builds the system prompt (persona + performance rules) and keeps
/// a short history, dropping the oldest turns past a size limit.
/// </summary>
public sealed class Conversation
{
    private readonly List<ChatTurn> _turns = new();

    public Conversation(int maxTurns = 24, int maxCharacters = 16000)
    {
        MaxTurns = maxTurns;
        MaxCharacters = maxCharacters;
    }

    public int MaxTurns { get; }
    public int MaxCharacters { get; }
    public IReadOnlyList<ChatTurn> Turns => _turns;

    public void AddUser(string text) => Add(new ChatTurn(ChatRole.User, text));
    public void AddAssistant(string text) => Add(new ChatTurn(ChatRole.Assistant, text));

    /// <summary>Removes a question that got no answer (error or cancel), so turns keep alternating.</summary>
    public void DropUnanswered()
    {
        if (_turns.Count > 0 && _turns[^1].Role == ChatRole.User)
            _turns.RemoveAt(_turns.Count - 1);
    }

    public void Clear() => _turns.Clear();

    private void Add(ChatTurn turn)
    {
        // Two user turns in a row (an earlier question failed): keep the newer one.
        if (_turns.Count > 0 && _turns[^1].Role == turn.Role)
            _turns.RemoveAt(_turns.Count - 1);
        _turns.Add(turn);
        while (_turns.Count > MaxTurns || (_turns.Count > 1 && _turns.Sum(t => t.Text.Length) > MaxCharacters))
        {
            // Drop a whole exchange so the history still starts with the user.
            _turns.RemoveAt(0);
            if (_turns.Count > 0 && _turns[0].Role == ChatRole.Assistant)
                _turns.RemoveAt(0);
        }
    }

    /// <param name="creature">Who the character is, for animals (see <see cref="Creatures.Persona"/>); null for the doll.</param>
    public static string SystemPrompt(AiSettings settings, string? creature = null)
    {
        string name = string.IsNullOrWhiteSpace(settings.CharacterName) ? "小桃" : settings.CharacterName.Trim();
        string personality = string.IsNullOrWhiteSpace(settings.Personality) ? "活潑、溫柔" : settings.Personality.Trim();
        return $"""
            你是「{name}」。{creature ?? "你是住在使用者電腦桌面上的動漫風小人偶，會在螢幕上走動、爬視窗。"}回答會顯示在你頭上的對話泡泡裡。
            個性：{personality}。
            用使用者的語言回答；中文一律使用台灣繁體中文與台灣用語。回答要口語、簡短（通常 1–4 句），除非使用者要求詳細內容。不要使用 Markdown 標題或表格。

            表演規則：在每句話（或語氣轉折處）前面加一個表演標記 ⟨情緒:強度 動作⟩，例如：
            ⟨happy:0.8 wave⟩ 嗨！今天過得怎麼樣？
            ⟨worried:0.4 tilt_head⟩ 會緊張很正常啦。⟨happy:0.7 encourage⟩ 你一定沒問題！
            - 情緒只能是：{string.Join(", ", PerformanceDirector.PromptEmotions)}；強度 0–1。
            - 動作 0–2 個，只能是：{string.Join(", ", PerformanceDirector.PromptActions)}（count 可加數字，如 count:2）。
            - 使用者請你做全身動作或擺姿勢時（例如「過來」「跳支舞」「坐下休息」「比個愛心」），在標記裡放對應的動作，一則回答最多一個：
              全身動作：{string.Join(", ", PoseCatalog.BodyActions)}（come 走過來、floor_sit 坐地上、get_up 起來）
              姿勢：{string.Join(", ", PoseCatalog.All.Select(p => $"{p.Id}（{p.Description}）"))}
            - 依內容挑最貼切的動作，同一則回答不要重複同一個動作。
            - 標記只用來表演，使用者看不到；不要解釋或提到標記。
            """;
    }
}
