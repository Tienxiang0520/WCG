using LcgWeb.Models;
namespace LcgWeb.Services;

public partial class GameEngine
{
    public record AttackPresentation(Guid AttackerId, Guid? TargetId, long ActionNumber);
    public AttackPresentation? LastAttack { get; private set; }
    public bool CanAttack(MonsterInstance m) => Main(ActivePlayer) && ActivePlayer.Field.Contains(m) &&
        !m.HasAttacked && !m.IsFrozen && (!m.HasSummoningSickness || m.HasCharge) && (m.IsSilenced || !m.Card.CannotAttack);
    public IReadOnlyList<MonsterInstance> GetAttackTargets(MonsterInstance attacker)
    {
        if (!CanAttack(attacker)) return Array.Empty<MonsterInstance>();
        var targets = OpponentPlayer.Field.Where(m => !m.IsStealthed).ToList();
        var taunts = targets.Where(m => m.IsTaunt && (attacker.IsSilenced || attacker.Card.Id != "LCG-059" || m.CurrentPP > 1325)).ToList();
        return taunts.Count > 0 ? taunts : targets;
    }
    public bool CanAttackPlayer(MonsterInstance attacker) => CanAttack(attacker) && !OpponentPlayer.Field.Any(m =>
        !m.IsStealthed && m.IsTaunt && (attacker.IsSilenced || attacker.Card.Id != "LCG-059" || m.CurrentPP > 1325));
    // Preview and resolution share this calculation; aftermath is deliberately excluded.
    public record AttackPreview(bool AttackerDies, bool DefenderDies, bool AttackerShieldBreaks, bool DefenderShieldBreaks, int PlayerDamage);
    private static AttackPreview CompareAttack(MonsterInstance a, MonsterInstance? d)
    {
        if (d == null) return new(false, false, false, false, a.CurrentDP);
        bool aHit = a.CurrentPP <= d.CurrentPP || d.HasPoison;
        bool dHit = d.CurrentPP <= a.CurrentPP || a.HasPoison;
        bool aDies = aHit && !a.HasShield, dDies = dHit && !d.HasShield;
        return new(aDies, dDies, aHit && a.HasShield, dHit && d.HasShield,
            !aDies && dDies && a.HasTrample && a.CurrentPP - d.CurrentPP >= 675 ? 1 : 0);
    }
    public AttackPreview? PreviewAttack(MonsterInstance attacker, MonsterInstance? defender = null) =>
        (defender == null ? CanAttackPlayer(attacker) : GetAttackTargets(attacker).Contains(defender)) ? CompareAttack(attacker, defender) : null;
    public bool Attack(PlayerState p, MonsterInstance attacker, MonsterInstance? target = null) => Change(() =>
    {
        if (!Main(p) || !CanAttack(attacker)) return Fail("此怪物現在不能攻擊。");
        if (target == null ? !CanAttackPlayer(attacker) : !GetAttackTargets(attacker).Contains(target))
            return Fail("攻擊目標不合法：須優先攻擊嘲諷，且不能攻擊潛伏怪物。");
        LastAttack = new(attacker.InstanceId, target?.InstanceId, Revision + 1);
        Present("attack", p, attacker.InstanceId, target?.InstanceId, card: attacker.Card, label: "攻擊");
        attacker.HasAttacked = true;
        if (attacker.IsStealthed) Present("status", p, attacker.InstanceId, card: attacker.Card, label: "解除潛伏");
        attacker.IsStealthed = false;
        var enemy = GetOpponent(p);
        Log($"【{p.Name}】以【{attacker.Card.Name}】攻擊【{(target == null ? enemy.Name : target.Card.Name)}】。", "combat");
        if (target?.IsTaunt == true)
            foreach (var priest in enemy.Field.Where(m => !m.IsSilenced && m.Card.Id == "LCG-071").ToArray()) Heal(enemy, 1);
        Resolve(() => ResolveAttack(p, attacker, target)); return true;
    });
    private void ResolveAttack(PlayerState p, MonsterInstance attacker, MonsterInstance? defender)
    {
        var enemy = GetOpponent(p);
        if (!p.Field.Contains(attacker)) return;
        if (defender == null) { Damage(enemy, attacker.CurrentDP); return; }
        if (!enemy.Field.Contains(defender)) return;
        var outcome = CompareAttack(attacker, defender);
        var victims = new List<MonsterInstance>();
        if (outcome.AttackerDies) victims.Add(attacker);
        if (outcome.DefenderDies) victims.Add(defender);
        foreach (var m in new[] { attacker, defender })
            if (m == attacker ? outcome.AttackerShieldBreaks : outcome.DefenderShieldBreaks)
            { ReleaseShield(m, all: false); Log($"【{m.Card.Name}】聖盾阻止交戰消滅，能量直立返回。", "combat"); }
        bool trample = outcome.PlayerDamage > 0;
        var after = new List<Action>();
        if (trample) after.Add(() => { if (Alive(attacker)) Damage(enemy, 1); });
        foreach (var m in new[] { attacker, defender })
        {
            var opponent = m == attacker ? defender : attacker; var owner = m == attacker ? p : enemy;
            after.Add(() =>
            {
                if (!Alive(m) || m.IsSilenced) return;
                if (m.Card.Id == "LCG-109") DrawMany(owner, 1);
                if (m.Card.Id == "LCG-029" && Alive(opponent)) Freeze(opponent);
            });
        }
        Resolve(after.ToArray()); KillBatch(victims, "交戰");
    }
    public bool EndTurn() => Change(() =>
    {
        if (!Main(ActivePlayer)) return Fail("請先完成效果選擇，再結束回合。");
        var p = ActivePlayer; CurrentPhase = TurnPhase.EndingTurn; var effects = new List<Action>();
        foreach (var m in p.Field.ToArray())
        {
            if (m.IsSilenced) continue;
            if (m.Card.Id == "LCG-120") effects.Add(() =>
            {
                if (!Alive(m) || m.IsSilenced) return; var enemy = GetOpponent(p);
                if (enemy.Field.Count == 0) Damage(enemy, 1); else PickLowest(p, enemy.Field, x => KillBatch(new[] { x }, m.Card.Name));
            });
            if (m.Card.Id == "LCG-060") effects.Add(() => { if (Alive(m) && !m.IsSilenced) FreeHand(p, 2, 1); });
        }
        effects.Add(() =>
        {
            foreach (var m in p.Field.Where(m => m.FrozenUntilTurn <= TurnNumber))
            { m.FrozenUntilTurn = null; Present("status", p, m.InstanceId, card: m.Card, label: "解凍"); }
            CurrentTurnPlayerId = GetOpponent(p).Id; TurnNumber++; CurrentPhase = TurnPhase.MainPhase;
            Present("turn", ActivePlayer, label: $"第 {TurnNumber} 回合");
            ActivePlayer.ResetEnergyForOwnTurn(); foreach (var m in ActivePlayer.Field) m.ResetTurnState();
            Draw(ActivePlayer); Log($"第 {TurnNumber} 回合：輪到【{ActivePlayer.Name}】行動。", "action");
        });
        Resolve(effects.ToArray()); return true;
    });
    public bool ExecuteAiStep() => Change(() =>
    {
        if (AiLevel >= 0) return ExecuteRankedAiStep();
        if (IsOver || CurrentPhase == TurnPhase.NotStarted || !PlayerById(DecisionPlayerId).IsAi) return false;
        var computer = PlayerById(DecisionPlayerId); var player = GetOpponent(computer);
        if (CurrentPendingChoice is { } choice)
        {
            var option = choice.Options.FirstOrDefault(o => o.Id == "KEEP") ?? choice.Options.FirstOrDefault(o => o.Id != "SKIP") ?? choice.Options[0];
            return SelectChoice(option);
        }
        if (CurrentPendingTarget is { } target)
        {
            var selected = player.Field.Concat(computer.Field).Where(m => target.Validator?.Invoke(m) != false)
                .OrderByDescending(m => player.Field.Contains(m) ? m.CurrentPP : -m.CurrentPP).FirstOrDefault();
            return selected != null && SelectTarget(selected);
        }
        if (!Main(computer)) return false;
        var lethal = computer.Field.FirstOrDefault(m => CanAttackPlayer(m) && m.CurrentDP >= player.Hp);
        if (lethal != null) return Attack(computer, lethal);
        if (!computer.HasFilledEnergyThisTurn && computer.Hand.Count > 0 && computer.TotalEnergy < computer.Hand.Max(c => c.Card.TotalCost) + 1 && (computer.Hand.Count > 1 || !computer.Hand.Any(c => CanPlayCard(computer, c))))
            return PlayEnergy(computer, computer.Hand.OrderBy(c => c.Card.TotalCost <= computer.AvailableEnergy + 1 ? 1 : 0).ThenByDescending(c => c.Card.TotalCost).First());
        var playable = computer.Hand.Where(c => CanPlayCard(computer, c))
            .Where(c => !(c.Card.Id is "LCG-050" or "LCG-070" && computer.Hp == PlayerState.MaxHp))
            .OrderByDescending(c => c.Card.IsMonster).ThenByDescending(c => c.Card.TotalCost).FirstOrDefault();
        if (playable != null) return playable.Card.IsMonster ? SummonMonster(computer, playable) : CastSpell(computer, playable);
        foreach (var attacker in computer.Field.Where(CanAttack).OrderByDescending(m => m.CurrentDP).ThenByDescending(m => m.CurrentPP).ToArray())
        {
            var targets = GetAttackTargets(attacker);
            var favorable = targets.Where(m => !m.HasShield && (attacker.HasPoison || attacker.CurrentPP > m.CurrentPP) && (!m.HasPoison || attacker.HasShield))
                .OrderByDescending(m => m.IsTaunt).ThenByDescending(m => m.CurrentDP).ThenByDescending(m => m.CurrentPP).FirstOrDefault();
            if (favorable != null) return Attack(computer, attacker, favorable);
            if (CanAttackPlayer(attacker) && attacker.CurrentDP > 0) return Attack(computer, attacker);
            var shield = targets.FirstOrDefault(m => m.HasShield && ((!m.HasPoison && attacker.CurrentPP > m.CurrentPP) || attacker.HasShield));
            if (shield != null) return Attack(computer, attacker, shield);
            var trade = targets.FirstOrDefault(m => attacker.HasPoison || attacker.CurrentPP == m.CurrentPP);
            if (trade != null) return Attack(computer, attacker, trade);
        }
        return EndTurn();
    });
    public void ExecuteAiTurn()
    { for (int i = 0; i < 200 && DecisionPlayerId == Computer.Id && !IsOver; i++) if (!ExecuteAiStep()) break; }
}
