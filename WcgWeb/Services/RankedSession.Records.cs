using WcgWeb.Models;
using WcgWeb.Models.Battle;
using WcgWeb.Models.Ranked;
namespace WcgWeb.Services;

// 對戰紀錄 and 對局回顧. Records are optional profile entries; replays re-simulate on a private engine
// with the recorded seed and actions, so the live ranked engine, its journal and save versions are untouched.
public sealed partial class RankedSession
{
    private void AddRecord(RankedMatch match, RankedResult result, int turns, DateTimeOffset? endedAt)
    {
        var records = profile.Records ??= [];
        if (records.Any(r => r.Id == match.Id)) return;
        records.Add(new()
        {
            Id = match.Id, Season = match.Season, StartedAt = match.StartedAt, EndedAt = endedAt, Tier = match.Tier, Seed = match.Seed,
            PlayerFirst = match.PlayerFirst, Rules = match.Rules, StartRules = match.StartRules, PlayerDeck = DeckService.Copy(match.PlayerDeck), ComputerDeck = DeckService.Copy(match.ComputerDeck),
            Archetype = RankedDecks.ArchetypeName(match.ComputerDeck), Won = result.Won, Before = result.Before, After = result.After,
            Reason = result.Reason, Turns = turns, Actions = RankedRecord.Encode(match.Actions)
        });
        if (records.Count > RankedRules.MaxRecords) records.RemoveRange(0, records.Count - RankedRules.MaxRecords);
    }
    // A match settled before 對戰紀錄 existed is still in Profile.Match: list it (persisted with the next save).
    private void BackfillRecord()
    {
        if (profile.Match is { Settled: true } match && profile.Result is { } result && result.MatchId == match.Id && Engine.IsOver)
            AddRecord(match, result, Engine.TurnNumber, null);
    }
    public RankedReplay BuildReplay(Guid id)
    {
        RankedRecord? record;
        lock (gate) record = profile.Records?.LastOrDefault(r => r.Id == id) is { } found ? RankedStore.Copy(new RankedProfile { Records = [found] }).Records![0] : null;
        if (record == null) return new(null, [], "找不到這場對局。");
        return BuildReplay(record);
    }
    // 對局代碼 for a finished record, or null when it is not in the history.
    public string? MatchCodeFor(Guid id)
    {
        lock (gate) return profile.Records?.LastOrDefault(r => r.Id == id) is { } r ? MatchCode.Encode(r) : null;
    }
    // 對局代碼 for the unfinished match in progress (its journal so far), or null when there is none.
    public string? CurrentMatchCode()
    {
        lock (gate)
        {
            if (profile.Match is not { Settled: false } m) return null;
            return MatchCode.Encode(new RankedRecord { Id = m.Id, Season = m.Season, Tier = m.Tier, Seed = m.Seed, PlayerFirst = m.PlayerFirst, Rules = m.Rules, StartRules = m.StartRules,
                PlayerDeck = m.PlayerDeck, ComputerDeck = m.ComputerDeck, Archetype = RankedDecks.ArchetypeName(m.ComputerDeck), Before = profile.Stars,
                Turns = Engine.TurnNumber, Actions = RankedRecord.Encode(m.Actions) }, inProgress: true);
        }
    }
    // Re-simulates any record, including one decoded from a pasted 對局代碼 (never stored in the profile).
    public RankedReplay BuildReplay(RankedRecord record) => BuildReplay(record, null);
    // trace (debug, tools/RankedCheck --explain) receives (action index, AI decision text) for every computer step.
    internal RankedReplay BuildReplay(RankedRecord record, Action<int, string>? trace) => BuildReplay(record, trace, int.MaxValue, out _);
    // Tests: the replayed engine after the first actionCount journal actions, positioned before the next one.
    internal GameEngine? ReplayEngine(RankedRecord record, int actionCount) { var r = BuildReplay(record, null, actionCount, out var engine); return r.Error == "" ? engine : null; }
    private RankedReplay BuildReplay(RankedRecord record, Action<int, string>? trace, int actionCount, out GameEngine? replayed)
    {
        replayed = null;
        var steps = new List<RankedReplayStep>();
        try
        {
            var actions = RankedRecord.Decode(record.Actions);
            CardDatabase catalog; bool limit, legacy;
            if (record.Rules == rules || record.Rules == priorIdentifierRules) { catalog = currentCards; limit = false; legacy = false; }
            else if (PriorEra(record.Rules) is { } era) { catalog = era.Cards; limit = era.LimitFactionTriggers; legacy = era.LegacyCardRules; }
            else return new(record, [], "這場對局使用的卡牌版本已更新，無法重新模擬回顧。");
            var engine = new GameEngine(currentCards); var bridge = new BattleBridge(engine, deckService);
            engine.ResetToNotStarted(); engine.UseMatchCatalog(catalog, limit, legacy);
            engine.SetReplaySeed(record.Seed); engine.AiLevel = record.Tier; engine.EqualStartingLife = record.StartRules < 1; engine.SecondPlayerCoin = record.StartRules >= 2;
            engine.StartGame(record.PlayerDeck, record.ComputerDeck, record.PlayerFirst);
            if (trace != null) engine.AiTrace = text => trace(steps.Count - 1, text);
            steps.Add(new(0, "start", "start", engine.TurnNumber, bridge.Snapshot(), engine.PresentationEvents));
            replayed = engine;
            foreach (var action in actions.Take(actionCount))
            {
                engine.StackableShields = action.RulesVersion >= 2; engine.AiRevision = action.RulesVersion;
                var response = action.Type == "ai" ? bridge.AiStepCore() : bridge.SubmitCore(Decode(engine, action));
                if (!response.Success) return new(record, steps, $"回顧在第 {steps.Count} 步無法還原，只顯示到這裡。");
                steps.Add(new(steps.Count, action.Type == "ai" ? "computer" : "player", action.Type, response.State.Turn, response.State, response.Events));
            }
            return new(record, steps, "");
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or ArgumentException or IndexOutOfRangeException)
        { return new(record, steps, "回顧資料無法還原：" + ex.Message); }
    }
}
