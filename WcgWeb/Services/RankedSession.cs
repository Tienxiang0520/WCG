using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WcgWeb.Models;
using WcgWeb.Models.Battle;
using WcgWeb.Models.Ranked;
namespace WcgWeb.Services;

// One local player. Practice circuits never share this engine or its ranked journal.
public sealed partial class RankedSession
{
    private readonly object gate = new();
    private readonly RankedStore store;
    private readonly RankedDecks opponents;
    private readonly TimeProvider clock;
    private readonly string rules;
    private readonly string priorIdentifierRules;
    private readonly CardDatabase currentCards;
    // Earlier card catalogs, newest first. A saved match resumes and replays with the catalog and engine rules it started on.
    private sealed record CatalogEra(string Rules, string IdentifierRules, CardDatabase Cards, bool LimitFactionTriggers, bool LegacyCardRules);
    // Engine flags per CardBalanceHistory.EraSteps entry (same order, newest first). Matches started before the card-text
    // clarity update keep the old wording and engine behaviour (LegacyCardRules); matches started before the arrow-card
    // expansion keep their original cards (arrow effects are keyed to card arrows, so replays stay deterministic); the
    // oldest catalog also limits faction triggers.
    private static readonly (bool LimitFactionTriggers, bool LegacyCardRules)[] EraFlags =
        [.. Enumerable.Repeat((false, false), 43), (false, true), (false, true), (true, true)];
    // Built on demand: most visits only touch the current catalog, and a lookup stops at the first matching era,
    // building each older catalog from the previous one in a single step (no repeated history chains).
    private readonly object eraGate = new();
    private readonly List<CatalogEra> priorEras = [];
    private CatalogEra? PriorEra(string matchRules)
    {
        lock (eraGate)
        {
            foreach (var era in priorEras) if (era.Rules == matchRules || era.IdentifierRules == matchRules) return era;
            while (priorEras.Count < CardBalanceHistory.EraSteps.Length)
            {
                var era = BuildEra(priorEras.Count);
                priorEras.Add(era);
                if (era.Rules == matchRules || era.IdentifierRules == matchRules) return era;
            }
            return null;
        }
    }
    private CatalogEra BuildEra(int index)
    {
        var db = CardBalanceHistory.EraSteps[index](index == 0 ? currentCards : priorEras[index - 1].Cards);
        var json = JsonSerializer.Serialize(db.AllCards);
        var (limit, legacy) = EraFlags[index];
        return new(Fingerprint(json), Fingerprint(json.Replace(CardIdentifier.CurrentPrefix, CardIdentifier.LegacyPrefix, StringComparison.Ordinal)), db, limit, legacy);
    }
    // Tests: every era fingerprint, building all of them.
    internal IReadOnlyList<(string Rules, string IdentifierRules, bool LimitFactionTriggers, bool LegacyCardRules)> AllPriorEras()
    {
        PriorEra("");
        lock (eraGate) return priorEras.Select(e => (e.Rules, e.IdentifierRules, e.LimitFactionTriggers, e.LegacyCardRules)).ToList();
    }
    private RankedProfile profile = new();
    private readonly DeckService deckService;
    public GameEngine Engine { get; }
    public BattleBridge Bridge { get; }
    public BattleCoordinator Coordinator { get; }
    public string Error { get; private set; } = "";
    public string BalanceNotice { get; private set; } = "";
    public string ResetNotice { get; private set; } = "";
    public RankedSession(CardDatabase cards, DeckService decks, RankedStore store, RankedDecks opponents,
        ILogger<BattleCoordinator> logger, TimeProvider? clock = null)
    {
        this.store = store; this.opponents = opponents; deckService = decks; this.clock = clock ?? TimeProvider.System;
        var catalog = JsonSerializer.Serialize(cards.AllCards);
        rules = Fingerprint(catalog);
        priorIdentifierRules = Fingerprint(catalog.Replace(CardIdentifier.CurrentPrefix, CardIdentifier.LegacyPrefix, StringComparison.Ordinal));
        currentCards = cards;
        Engine = new(cards); Bridge = new(Engine, decks); Coordinator = new(Bridge, Engine, logger, this.clock);
        try
        {
            profile = store.Load();
            if (store.ResetOnLoad)
            {
                store.Save(profile);
                ResetNotice = "天梯電腦已改為依牌位分級。舊版進度已封存到歷季成績，本季從青銅 III 重新開始；未完成的舊對局不計勝負。";
            }
            Restore(); BackfillRecord(); RefreshSeason();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        { logger.LogWarning(ex, "Ranked save could not be restored"); Error = "天梯存檔暫時無法載入，原檔已保留。"; }
        Bridge.RankedSubmit = Submit; Bridge.RankedAi = Ai; Bridge.RankedAiAsync = AiAsync;
    }
    public RankedProfile Read()
    {
        lock (gate) { if (Error == "" && !thinking) RefreshSeason(); return RankedStore.Copy(profile); }
    }
    private void RefreshSeason()
    {
        var before = RankedStore.Copy(profile);
        RankedRules.Advance(profile, clock.GetUtcNow());
        if (profile.Season != before.Season)
            try { store.Save(profile); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { profile = before; Error = "天梯進度無法寫入，請確認本機儲存空間可用後重新載入。"; }
    }
    public BattleResponse Start(Deck deck)
    {
        lock (gate)
        {
            if (thinking) return Reject("電腦仍在思考。");
            if (Error != "") return Reject(Error);
            RefreshSeason();
            if (Error != "") return Reject(Error);
            if (profile.Match is { Settled: false }) return Reject("還有未完成的天梯對局，請先繼續或投降。");
            var before = RankedStore.Copy(profile);
            var tier = RankedRules.Tier(profile.Stars); var seed = Random.Shared.Next();
            var previousOpponent = profile.Match?.ComputerDeck?.Id;
            profile.Match = new() { Season = profile.Season, Seed = seed, Tier = tier, PlayerFirst = Random.Shared.Next(2) == 0,
                Rules = rules, StartRules = RankedRules.CurrentStartRules, PlayerDeck = DeckService.Copy(deck), ComputerDeck = opponents.Pick(tier, new Random(seed), previousOpponent), StartedAt = clock.GetUtcNow() };
            profile.SelectedDeck = deck.Id; profile.Result = null;
            try { Restore(); store.Save(profile); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            { profile = before; Restore(); return Reject("對局未建立：" + ex.Message); }
            return new(true, "start", "", Guid.NewGuid(), Bridge.Snapshot(), []);
        }
    }
    private RankedAction Encode(BattleCommand c)
    {
        var source = c.Type is "attack" or "activate" ? Engine.Player.Board.OrderBy(m=>m.Slot).ToList().FindIndex(m => m.InstanceId == c.InstanceId)
            : Engine.Player.Hand.FindIndex(m => m.InstanceId == c.InstanceId);
        var side = c.TargetId == GameEngine.PlayerTargetId ? 2 : c.TargetId == GameEngine.ComputerTargetId ? 3
            : Engine.Player.Board.Any(m => m.InstanceId == c.TargetId) ? 0 : Engine.Computer.Board.Any(m => m.InstanceId == c.TargetId) ? 1 : -1;
        var target = side is < 0 or >= 2 ? -1 : (side == 0 ? Engine.Player : Engine.Computer).Board.OrderBy(m=>m.Slot).ToList().FindIndex(m => m.InstanceId == c.TargetId);
        return new(c.Type, source, side, target, Engine.CurrentPendingChoice?.Options.FindIndex(o => o.Id == c.OptionId) ?? -1, RulesVersion: GameEngine.RankedAiVersion);
    }
    private BattleCommand Decode(RankedAction a) => Decode(Engine, a);
    private static BattleCommand Decode(GameEngine Engine, RankedAction a)
    {
        if (a.Source < -1 || a.TargetSide is < -1 or > 3 || a.Target < -1 || a.Choice < -1 ||
            a.Source >= (a.Type is "attack" or "activate" ? Engine.Player.Occupied : Engine.Player.Hand.Count) ||
            (a.TargetSide is 0 or 1 && (a.Target < 0 || a.Target >= (a.TargetSide == 0 ? Engine.Player : Engine.Computer).Occupied)) ||
            (a.TargetSide >= 2 && (a.Target != -1 || a.Type is not ("play" or "target"))) ||
            (a.Choice >= 0 && (Engine.CurrentPendingChoice == null || a.Choice >= Engine.CurrentPendingChoice.Options.Count)))
            throw new InvalidDataException("對局操作位置無效。");
        Guid? source = a.Source < 0 ? null : a.Type is "attack" or "activate" ? Engine.Player.Board.OrderBy(m=>m.Slot).ToArray()[a.Source].InstanceId : Engine.Player.Hand[a.Source].InstanceId;
        Guid? target = a.TargetSide switch { -1 => null, 2 => GameEngine.PlayerTargetId, 3 => GameEngine.ComputerTargetId,
            _ => (a.TargetSide == 0 ? Engine.Player : Engine.Computer).Board.OrderBy(m=>m.Slot).ToArray()[a.Target].InstanceId };
        var option = a.Choice < 0 ? null : Engine.CurrentPendingChoice!.Options[a.Choice].Id;
        return new(Guid.NewGuid(), Engine.MatchId, Engine.Revision, a.Type, source, target, option);
    }
    private void Restore()
    {
        Engine.ResetToNotStarted(); Engine.EqualStartingLife = false; Engine.SecondPlayerCoin = true;
        Engine.UseMatchCatalog(currentCards, false); BalanceNotice = "";
        if (profile.Match is not { } match) return;
        if (match.Settled && match.Rules != rules && match.Rules != priorIdentifierRules) return; // Completed results survive card balance updates.
        if (match.Rules != rules && match.Rules != priorIdentifierRules)
        {
            var era = PriorEra(match.Rules) ?? throw new InvalidOperationException("存檔對局使用不同規則版本。");
            Engine.UseMatchCatalog(era.Cards, era.LimitFactionTriggers, era.LegacyCardRules);
            BalanceNotice = "本局沿用原卡牌效果與費用，下一局套用新版。";
        }
        Engine.SetReplaySeed(match.Seed); Engine.AiLevel = match.Tier; Engine.EqualStartingLife = match.StartRules < 1; Engine.SecondPlayerCoin = match.StartRules >= 2;
        Engine.StartGame(match.PlayerDeck, match.ComputerDeck, match.PlayerFirst);
        foreach (var action in match.Actions)
        {
            Engine.StackableShields = action.RulesVersion >= 2; Engine.AiRevision = action.RulesVersion;
            var response = action.Type == "ai" ? Bridge.AiStepCore() : Bridge.SubmitCore(Decode(action));
            if (!response.Success) throw new InvalidOperationException("對局紀錄無法完整還原。");
        }
        Engine.StackableShields = true; Engine.AiRevision = GameEngine.RankedAiVersion;
        if (match.Settled != Engine.IsOver) throw new InvalidOperationException("對局結算紀錄不一致。");
    }
    private BattleResponse Reject(string reason) => new(false, "ranked", reason, Guid.NewGuid(), Bridge.Snapshot(), []);
    private static string Fingerprint(string catalog) => "ranked-v06:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(catalog)));
    private BattleResponse Submit(BattleCommand command)
    {
        lock (gate)
        {
            if (thinking) return Reject("電腦仍在思考。");
            if (Error != "") return Reject(Error);
            if (command.Type is "start" or "reset") return Reject("請使用天梯大廳開始或繼續對局。");
            if (profile.Match is not { Settled: false }) return Reject("此天梯對局已結算。");
            if (command.MatchId != Engine.MatchId || command.ExpectedRevision != Engine.Revision) return Reject("對局已由其他頁面更新，請同步後繼續。");
            var action = Encode(command);
            return Apply(action, () => Bridge.SubmitCore(command));
        }
    }
    private BattleResponse Ai()
    {
        lock (gate)
        {
            if (thinking) return Reject("電腦仍在思考。");
            if (Error != "" || profile.Match is not { Settled: false }) return Reject(Error == "" ? "此局已結算。" : Error);
            return Apply(new("ai", RulesVersion: GameEngine.RankedAiVersion), Bridge.AiStepCore);
        }
    }
    // The computer's step with pauses between search slices (BattleBridge.AiStepAsync). The profile stays untouched
    // while it thinks (`thinking` holds back season refresh and new matches), so the journal ends up exactly as with Ai.
    private bool thinking;
    private async Task<BattleResponse> AiAsync(Func<ValueTask> pause)
    {
        RankedProfile before; long revision;
        lock (gate)
        {
            if (thinking) return Reject("電腦仍在思考。");
            if (Error != "" || profile.Match is not { Settled: false }) return Reject(Error == "" ? "此局已結算。" : Error);
            before = RankedStore.Copy(profile); revision = Engine.Revision; thinking = true;
        }
        try
        {
            var response = await Bridge.AiStepCoreAsync(pause);
            lock (gate) return Record(new("ai", RulesVersion: GameEngine.RankedAiVersion), before, revision, response);
        }
        finally { lock (gate) thinking = false; }
    }
    private BattleResponse Apply(RankedAction action, Func<BattleResponse> apply)
    {
        var before = RankedStore.Copy(profile); var revision = Engine.Revision;
        return Record(action, before, revision, apply());
    }
    private BattleResponse Record(RankedAction action, RankedProfile before, long revision, BattleResponse response)
    {
        if (!response.Success || revision == Engine.Revision) return response;
        profile.Match!.Actions.Add(action);
        if (Engine.IsOver)
        {
            var won = !Engine.Player.HasLost; var previous = profile.Stars;
            profile.Stars = RankedRules.Apply(previous, won);
            profile.BestStars = Math.Max(profile.BestStars, profile.Stars); profile.SeasonBest = Math.Max(profile.SeasonBest, profile.Stars);
            if (won) profile.Wins++; else profile.Losses++;
            profile.Match.Settled = true;
            profile.Result = new(profile.Match.Id, profile.Match.Season, won, previous, profile.Stars, response.State.Outcome);
            AddRecord(profile.Match, profile.Result, Engine.TurnNumber, clock.GetUtcNow());
            RankedRules.Advance(profile, clock.GetUtcNow());
        }
        try { store.Save(profile); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            profile = before; Restore();
            Error = "存檔寫入失敗，這次操作已回復。請重新啟動後繼續。";
            return Reject(Error);
        }
        return response;
    }
}
