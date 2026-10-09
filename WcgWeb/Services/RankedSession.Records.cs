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
            PlayerFirst = match.PlayerFirst, Rules = match.Rules, PlayerDeck = DeckService.Copy(match.PlayerDeck), ComputerDeck = DeckService.Copy(match.ComputerDeck),
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
        var steps = new List<RankedReplayStep>();
        try
        {
            var actions = RankedRecord.Decode(record.Actions);
            CardDatabase catalog; bool limit;
            if (record.Rules == rules || record.Rules == priorIdentifierRules) { catalog = currentCards; limit = false; }
            else if (record.Rules == priorBalanceRules || record.Rules == priorBalanceIdentifierRules) { catalog = priorBalanceCards; limit = true; }
            else return new(record, [], "這場對局使用的卡牌版本已更新，無法重新模擬回顧。");
            var engine = new GameEngine(currentCards); var bridge = new BattleBridge(engine, deckService);
            engine.ResetToNotStarted(); engine.UseMatchCatalog(catalog, limit);
            engine.SetReplaySeed(record.Seed); engine.AiLevel = record.Tier;
            engine.StartGame(record.PlayerDeck, record.ComputerDeck, record.PlayerFirst);
            steps.Add(new(0, "start", "start", engine.TurnNumber, bridge.Snapshot(), engine.PresentationEvents));
            foreach (var action in actions)
            {
                engine.StackableShields = action.RulesVersion >= 2;
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
