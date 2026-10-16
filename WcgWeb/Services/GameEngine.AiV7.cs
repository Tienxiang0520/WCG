using WcgWeb.Models;
namespace WcgWeb.Services;

// Ranked AI v7 (ai-design.md). Journals record RulesVersion; steps recorded with version 6 or earlier replay
// with ExecuteTieredAi, so v6 stays untouched. v7 is deterministic: budgets count simulated nodes, never time,
// and every simulation runs on a clone without the opponent's hidden cards (GameEngine.Simulation.cs).
public partial class GameEngine
{
    // LethalSight: chance per turn decision to run the lethal search; LethalBudget/LethalDepth bound it.
    internal sealed record V7Profile(double LethalSight, int LethalBudget, int LethalDepth);
    internal static V7Profile V7ProfileFor(int level) => Math.Clamp(level, 0, 5) switch
    {
        0 => new(.6, 60, 3),
        1 => new(.9, 150, 4),
        2 => new(1, 400, 5),
        3 => new(1, 800, 6),
        4 => new(1, 1500, 6),
        _ => new(1, 2000, 7),
    };

    internal sealed record SimMove(string Kind, Guid Source, Guid? Target = null)
    {
        public override string ToString() => $"{Kind}:{Source}{(Target is { } t ? ">" + t : "")}";
    }

    private int _simCounter;
    private int SimSeed() { unchecked { return _aiSeed * 31 + (int)Revision * 7919 + TurnNumber * 104729 + ++_simCounter * 15485863; } }

    private bool ExecuteV7Ai()
    {
        if (IsOver || CurrentPhase == TurnPhase.NotStarted || !PlayerById(DecisionPlayerId).IsAi) return false;
        var p = PlayerById(DecisionPlayerId);
        // Choices and targets raised by our own actions keep the v6 logic, the same logic simulations use.
        if (IsWaiting || !Main(p) || !IsQuiescent) return ExecuteTieredAi();
        var prof = V7ProfileFor(AiLevel); _simCounter = 0;
        if (Noise(101) < prof.LethalSight && FindLethal(p, prof) is { } lethal && ApplyMove(this, p.Id, lethal)) return true;
        return ExecuteTieredAi();
    }

    internal bool ApplyMove(GameEngine g, string pid, SimMove m)
    {
        var p = g.PlayerById(pid); var e = g.GetOpponent(p);
        MonsterInstance? Find(Guid? id) => id is { } x ? p.Board.Concat(e.Board).FirstOrDefault(u => u.InstanceId == x) : null;
        switch (m.Kind)
        {
            case "energy": return p.Hand.FirstOrDefault(c => c.InstanceId == m.Source) is { } en && g.PlayEnergy(p, en);
            case "set": return p.Hand.FirstOrDefault(c => c.InstanceId == m.Source) is { } st && g.SetCard(p, st);
            case "activate": return p.Structures.FirstOrDefault(s => s.InstanceId == m.Source) is { } s && g.Activate(p, s);
            case "attack":
                var a = p.Field.FirstOrDefault(u => u.InstanceId == m.Source); if (a == null) return false;
                var d = m.Target == null ? null : e.Field.FirstOrDefault(u => u.InstanceId == m.Target); if (m.Target != null && d == null) return false;
                return g.Attack(p, a, d);
            case "play":
                var c = p.Hand.FirstOrDefault(x => x.InstanceId == m.Source); if (c == null) return false;
                if (c.Card.IsMonster) return g.SummonMonster(p, c);
                if (c.Card.IsEnchantment) return g.PlayEnchantment(p, c);
                if (m.Target != null) return Find(m.Target) is { } t && g.CastSpellAt(p, c, t);
                return g.CastSpell(p, c);
        }
        return false;
    }

    // Legal moves in a settled main phase, in a fixed order (hand, board and target order) so ties never
    // depend on instance GUIDs, which differ between a match and its replay.
    internal List<SimMove> GenerateMoves(PlayerState p, bool energy = true, bool sets = true, int targetCap = 4)
    {
        var moves = new List<SimMove>(); if (!Main(p)) return moves;
        var e = GetOpponent(p);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in p.Field.Where(CanAttack))
        {
            if (!seen.Add($"A{a.Card.Id}/{a.CurrentPP}/{a.CurrentDP}/{a.HasShield}/{a.HasCharge}")) continue;
            if (CanAttackPlayer(a) && a.CurrentDP > 0) moves.Add(new("attack", a.InstanceId));
            foreach (var d in GetAttackTargets(a)) moves.Add(new("attack", a.InstanceId, d.InstanceId));
        }
        seen.Clear();
        foreach (var c in p.Hand)
        {
            if (!CanPlayCard(p, c) || SelfHarmful(p, c) || !seen.Add(c.Card.Id)) continue;
            var targets = c.Card.IsSpell ? GetDirectPlayTargets(p, c) : [];
            if (targets.Count == 0) { moves.Add(new("play", c.InstanceId)); continue; }
            var best = BestSpellTarget(p, e, c.Card.Id, targets);
            var ordered = targets.OrderByDescending(m => (m == best ? 100 : 0) + (e.Field.Contains(m) ? 20 : 0) + BodyValue(m) + (m.IsTaunt ? 3 : 0)).Take(targetCap);
            foreach (var t in ordered) moves.Add(new("play", c.InstanceId, t.InstanceId));
        }
        foreach (var s in p.Structures.Where(s => CanActivate(p, s))) moves.Add(new("activate", s.InstanceId));
        if (sets && !DamageCounters && p.Occupied < 5 && !p.Structures.Any(x => x.IsSet))
            foreach (var c in p.Hand.Where(c => c.Card.IsCounter && p.AvailableEnergy >= c.Card.TotalCost).DistinctBy(c => c.Card.Id)) moves.Add(new("set", c.InstanceId));
        if (energy && !p.HasFilledEnergyThisTurn)
            foreach (var c in p.Hand.Where(c => GetEnergyProblem(p, c) == "").OrderBy(c => HoldValue(p, c)).DistinctBy(c => c.Card.Id).Take(2)) moves.Add(new("energy", c.InstanceId));
        return moves;
    }

    // Damage a card can add this turn according to its text: direct player damage and DP boosts.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _cardReach = new(StringComparer.Ordinal);
    internal static int CardReach(CardDefinition c) => _cardReach.GetOrAdd(c.Id + "\u0001" + c.Text, _ =>
    {
        int reach = 0;
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(c.Text, @"(?:敵方玩家|任一玩家|玩家)造成\s*(\d+)\s*點傷害"))
            reach += int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(c.Text, @"DP\s*\+\s*(\d+)"))
            reach += int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        if (c.IsMonster && c.HasCharge) reach += c.DP ?? 1;
        return reach;
    });

    // Cheap upper bound on damage this turn; skips the lethal search when the opponent is clearly out of reach.
    // Taunts are ignored (removal may clear them), so the bound only prunes positions that can never be lethal.
    internal int DamageBound(PlayerState p, PlayerState e)
    {
        int energy = p.AvailableEnergy + (p.HasFilledEnergyThisTurn ? 0 : 1);
        int bound = p.Field.Where(m => !m.IsTapped && !m.AttackLocked && (m.IsSilenced || !m.Card.CannotAttack)).Sum(m => m.CurrentDP + (m.HasRage ? energy : 0));
        bound += p.Structures.Where(s => !s.IsSet).Sum(s => CardReach(s.Card));
        bound += p.Hand.Where(c => ActualCost(p, c.Card) <= energy).Sum(c => CardReach(c.Card));
        return bound;
    }

    // Lethal search (ai-design.md §2.8): depth-first over every move sequence this turn on hidden-information
    // clones; returns the first move of a sequence that defeats the opponent, or null.
    internal SimMove? FindLethal(PlayerState p, V7Profile prof)
    {
        var e = GetOpponent(p);
        if (DamageBound(p, e) < e.Hp) return null;
        int budget = prof.LethalBudget;
        SimMove? Search(GameEngine g, int depth)
        {
            var me = g.PlayerById(p.Id);
            foreach (var m in g.GenerateMoves(me, energy: !me.HasFilledEnergyThisTurn, sets: false))
            {
                if (--budget < 0) return null;
                var child = g.CloneQuiescent(SimSeed(), p.Id);
                if (!child.SimApply(x => ApplyMove(x, p.Id, m))) continue;
                if (child.GetOpponent(child.PlayerById(p.Id)).HasLost && !child.PlayerById(p.Id).HasLost) return m;
                if (depth > 1 && !child.IsOver && child.CurrentTurnPlayerId == p.Id && Search(child, depth - 1) != null) return m;
            }
            return null;
        }
        return Search(CloneQuiescent(SimSeed(), p.Id), prof.LethalDepth);
    }
}
