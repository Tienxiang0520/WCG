using WcgWeb.Models;
using WcgWeb.Models.Battle;

namespace WcgWeb.Services;

public sealed class BattleBridge(GameEngine engine, DeckService decks)
{
    internal Func<BattleCommand, BattleResponse>? RankedSubmit { get; set; }
    internal Func<BattleResponse>? RankedAi { get; set; }
    private readonly Dictionary<Guid, BattleResponse> completed = new();
    private Guid cachedMatch;
    public BattleSnapshot Snapshot() => engine.ReadConsistent(BuildSnapshot);
    internal BattleResponse AiStep() => RankedAi?.Invoke() ?? AiStepCore();
    internal BattleResponse AiStepCore() => engine.ReadConsistent<BattleResponse>(() =>
    {
        var changed = engine.ExecuteAiStep();
        return new(changed, changed ? "ok" : "idle", "", Guid.NewGuid(), BuildSnapshot(),
            changed ? engine.PresentationEvents : []);
    });
    public BattleResponse Submit(BattleCommand command) => RankedSubmit?.Invoke(command) ?? SubmitCore(command);
    internal BattleResponse SubmitCore(BattleCommand command) => engine.ReadConsistent(() =>
    {
        if (cachedMatch != engine.MatchId) { completed.Clear(); cachedMatch = engine.MatchId; }
        if (command.CommandId == Guid.Empty) return Reject("command", "缺少操作識別碼。");
        if (completed.TryGetValue(command.CommandId, out var previous)) return previous;
        if (command.MatchId != engine.MatchId || command.ExpectedRevision != engine.Revision)
            return Reject("stale", "畫面已更新，已同步目前狀態，請重新操作。");
        try
        {
            var result = engine.ExecuteCommand("player", command.MatchId, command.ExpectedRevision,
                () => Dispatch(command), command.Type is "start" or "reset" or "surrender" || engine.TrainingMode && command.Type.StartsWith("training-"));
            var response = new BattleResponse(result.Success, result.Code, result.Success ? "" : string.IsNullOrWhiteSpace(result.Message) ? "操作已失效，請重新選擇卡牌或目標。" : result.Message, Guid.NewGuid(),
                BuildSnapshot(), result.Success ? engine.PresentationEvents : []);
            // A start/reset command changes MatchId. Keep its result for retries in the new match.
            if (cachedMatch != engine.MatchId) { completed.Clear(); cachedMatch = engine.MatchId; }
            completed[command.CommandId] = response;
            // Bound circuit memory; older retries still fail their stale revision check.
            if (completed.Count > 512) completed.Remove(completed.Keys.First());
            return response;
        }
        catch (ArgumentException ex) { return Reject("invalid", ex.Message); }
    });
    private BattleResponse Reject(string code, string reason) => new(false, code, reason,
        Guid.NewGuid(), BuildSnapshot(), []);
    private bool Dispatch(BattleCommand c)
    {
        if(c.Type.StartsWith("training-")) return engine.EditTraining(c.Type, c.Training, c.InstanceId);
        var hand = engine.Player.Hand.FirstOrDefault(x => x.InstanceId == c.InstanceId);
        var monster = engine.Player.Board.FirstOrDefault(x => x.InstanceId == c.InstanceId);
        var target = engine.Player.Board.Concat(engine.Computer.Board).FirstOrDefault(x => x.InstanceId == c.TargetId);
        switch (c.Type)
        {
            case "start":
                var p = decks.GetDeck(c.PlayerDeckId ?? ""); var ai = decks.GetDeck(c.ComputerDeckId ?? "");
                if (p == null || ai == null) throw new ArgumentException("找不到指定牌組。");
                engine.StartGame(p, ai, c.PlayerFirst); return true;
            case "reset": engine.ResetToNotStarted(); return true;
            case "surrender":
                if (engine.IsOver || engine.CurrentPhase == TurnPhase.NotStarted) return false;
                engine.Surrender(engine.Player); return true;
            case "energy": return hand != null && engine.PlayEnergy(engine.Player, hand);
            case "play": return hand != null && (c.TargetId != null ? target != null && engine.CastSpellAt(engine.Player, hand, target) : hand.Card.IsMonster
                ? engine.SummonMonster(engine.Player, hand) : hand.Card.IsEnchantment ? engine.PlayEnchantment(engine.Player,hand) : engine.CastSpell(engine.Player, hand));
            case "set": return hand != null && engine.SetCard(engine.Player,hand);
            case "activate": return monster != null && engine.Activate(engine.Player,monster);
            case "attack": return monster != null && (c.TargetId == null || target != null)
                && engine.Attack(engine.Player, monster, target);
            case "target": return target != null && engine.SelectTarget(target);
            case "choice":
                var option = engine.CurrentPendingChoice?.Options.FirstOrDefault(x => x.Id == c.OptionId);
                return option != null && engine.SelectChoice(option);
            case "cancel": return engine.CancelTargetOrChoice();
            case "end": return engine.EndTurn();
            default: throw new ArgumentException("不支援此操作。");
        }
    }
    private static GameEnginePreview? Preview(GameEngine.AttackPreview? p) => p == null ? null
        : new(p.AttackerDies, p.DefenderDies, p.AttackerShieldBreaks, p.DefenderShieldBreaks, p.PlayerDamage);
    private BattleMonster Monster(PlayerState owner, MonsterInstance m)
    {
        if(m.IsSet)return new(new(m.InstanceId,"SET","蓋牌","蓋牌","",0,null,null,"玩家被宣告攻擊時可翻開。",""),0,0,["蓋牌"],false,"蓋牌不能攻擊",[],null,m.Slot,false,true,false,[]);
        var status = new List<string>();
        if(m.IsTaunt)status.Add("嘲諷");if(m.HasShield)status.Add("聖盾資格（每次1能量）");status.Add(m.IsTapped?"橫置":"直立");
        if(m.Card.Arrows.Length>0)status.Add("箭頭："+string.Join(" ",m.Card.Arrows.Select(a=>a=="left"?"←":a=="right"?"→":"↑")));
        if (m.IsSilenced) status.Add("沉默"); if (m.HasPoison) status.Add("劇毒");
        if (m.HasTrample) status.Add("貫穿");
        var canAttack = owner == engine.Player && engine.CanAttack(m);
        var problem=canAttack?"":m.IsTapped?"橫置，不能攻擊":"目前不能攻擊";
        var targets = new List<BattleTarget>();
        if (canAttack)
        {
            targets.AddRange(engine.GetAttackTargets(m).Select(t => new BattleTarget(t.InstanceId.ToString(),
                t.Card.Name, Preview(engine.PreviewAttack(m, t)))));
            if (engine.CanAttackPlayer(m)) targets.Add(new("face", "電腦玩家", Preview(engine.PreviewAttack(m))));
        }
        var visible = m.IsSet ? new BattleCard(m.InstanceId,"SET","蓋牌","蓋牌","",0,null,null,"玩家被攻擊時可翻開。","") : GameEngine.VisibleCard(m.Card,m.InstanceId);
        return new(visible, m.CurrentPP, m.CurrentDP,
            status.ToArray(), canAttack, problem, targets.ToArray(),
            m.SilenceSpell == null ? null : GameEngine.VisibleCard(m.SilenceSpell),m.Slot,m.IsTapped,m.IsSet,owner==engine.Player&&engine.CanActivate(owner,m),m.Attachments.Select(a=>GameEngine.VisibleCard(a.Card)).ToArray());
    }
    private BattleSide Side(PlayerState p) => new(p.Id, p.Hp, p.Deck.Count, p.Hand.Count,
        p.AvailableEnergy, p.TotalEnergy, p.EnergyZone.Select(e => new BattleEnergy(e.InstanceId, e.IsTapped)).ToArray(),
        p.Board.OrderBy(m=>m.Slot).Select(m => Monster(p, m)).ToArray(), p.Graveyard.Select(GameEngine.VisibleCard).ToArray());
    private BattleSnapshot BuildSnapshot()
    {
        BattlePending? pending = null;
        if (engine.CurrentPendingChoice is { OwnerId: "player" } c)
            pending = new("choice", c.Title, c.Description, c.CanCancel,
                c.Options.Select(o => new BattleOption(o.Id, o.Title, o.Subtitle,
                    o.PreviewCard == null ? null : GameEngine.VisibleCard(o.PreviewCard, Guid.Empty))).ToArray(), [], c.SourceInstanceId);
        else if (engine.CurrentPendingTarget is { OwnerId: "player" } t)
            pending = new("target", t.Title, t.Description, t.CanCancel, [],
                engine.Player.Board.Concat(engine.Computer.Board).Where(m => t.Validator?.Invoke(m) != false)
                    .Select(m => m.InstanceId.ToString()).ToArray(), t.SourceInstanceId);
        return new(engine.MatchId, engine.Revision, engine.TurnNumber, engine.CurrentPhase.ToString(),
            engine.DecisionPlayerId, engine.IsOver, !engine.IsOver ? "" : engine.Player.HasLost
                ? $"本局敗北：{engine.Player.LossReason}" : $"本局獲勝：{engine.Computer.LossReason}",
            Side(engine.Player), Side(engine.Computer), engine.Player.Hand.Select(c => new BattleHand(
                GameEngine.VisibleCard(c) with { Cost=engine.ActualCost(engine.Player,c.Card) }, engine.CanPlayCard(engine.Player, c), engine.CanPlayEnergy(engine.Player, c),
                engine.GetPlayProblem(engine.Player, c), GameEngine.GetPlayPreparation(c.Card),
                engine.GetEnergyProblem(engine.Player, c), engine.GetDirectPlayTargets(engine.Player, c).Select(m => m.InstanceId.ToString()).ToArray(),
                c.Card.IsMonster&&engine.Player.AvailableEnergy<=engine.ActualCost(engine.Player,c.Card) ? "付款後沒有可用能量；有聖盾資格仍需留能量保命。" : "",
                engine.CurrentPhase==TurnPhase.MainPhase&&engine.CurrentTurnPlayerId==engine.Player.Id&&!engine.IsWaiting&&!engine.IsOver&&engine.Player.Occupied<5)).ToArray(), pending,
            // Old logs can contain an AI-only hand inspection. The new board uses public structured events.
            [], engine.RevealedCards.ToArray(), engine.RevealTitle, engine.CurrentTurnPlayerId);
    }
}
