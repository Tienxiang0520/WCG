using WcgWeb.Models;
namespace WcgWeb.Services;

// Ranked AI v7 (ai-design.md). Journals record RulesVersion; steps recorded with version 6 or earlier replay
// with ExecuteTieredAi, so v6 stays untouched. v7 is deterministic: budgets count simulated nodes, never time,
// and every simulation runs on a clone without the opponent's hidden cards (GameEngine.Simulation.cs).
public partial class GameEngine
{
    // LethalSight: chance per turn decision to run the lethal search; LethalBudget/LethalDepth bound it.
    // Width/Depth/Budget bound the turn search (beam over move sequences); Sigma is evaluation noise and
    // Blunder the chance of a random legal move, both deterministic (Noise()). Replies: end-of-turn candidates checked against the opponent's reply.
    // Calibrated with tools/RankedCheck --ai-vs/--v7-ladder: each tier beats v6 at the same tier and the v7 tier below.
    internal sealed record V7Profile(double LethalSight, int LethalBudget, int LethalDepth, int Width, int Depth, int Budget, double Sigma, double Blunder, int Replies = 0, int ReplyPlies = 1);
    internal static V7Profile V7ProfileFor(int level) => Math.Clamp(level, 0, 5) switch
    {
        0 => new(.6, 60, 3, 2, 2, 30, 7.0, .32),
        1 => new(.9, 150, 4, 3, 3, 80, 5.0, .20),
        2 => new(1, 250, 5, 4, 3, 160, 3.5, .12),
        3 => new(1, 400, 6, 5, 4, 300, 2.5, .08, 1),
        4 => new(1, 600, 6, 6, 5, 500, 1.5, .05, 4),
        _ => new(1, 1200, 8, 8, 6, 1000, 0, 0, 20, 2),
    };

    // v8 (RankedAiVersion 8, tools/RankedCheck --v8-vs-v7): Diamond and Master never play random "blunder"
    // moves, and lower tiers' blunders never throw a monster away; the turn and lethal searches skip pointless
    // attacks that throw the attacker away for nothing (the lethal search also skips free shield bumps); a move that draws a card
    // is a search leaf priced over several sampled draws, so no plan relies on one lucky simulated draw; the
    // lethal search tries the shortest sequences first. Steps recorded as v7 replay with the v7 rules above.
    private bool V8 => AiRevision >= 8;
    private const int DrawSamples = 3;
    internal V7Profile ProfileForDecision() { var prof = V7ProfileFor(AiLevel); return V8 && AiLevel >= 4 ? prof with { Blunder = 0 } : prof; }

    // Public facts a move can change, minus the moving monster itself. Equal keys before and after an attack mean
    // the attack achieved nothing: no enemy monster, shield, tap or life changed and nothing was drawn or triggered.
    private static string EffectKey(GameEngine g, string pid, Guid exclude)
    {
        var p = g.PlayerById(pid); var e = g.GetOpponent(p);
        string Side(PlayerState s) => $"{s.Hp}/{s.AvailableEnergy}/{s.Hand.Count}/{s.Deck.Count}/{s.Structures.Count}:" +
            string.Join(",", s.Field.Where(m => m.InstanceId != exclude).Select(m => $"{m.InstanceId}:{m.CurrentPP}:{m.CurrentDP}:{(m.IsTapped ? 1 : 0)}:{ShieldMark(g, m)}:{m.Attachments.Count}:{(m.IsSilenced ? 1 : 0)}"));
        return Side(p) + "|" + Side(e);
    }
    // v10 (RankedAiVersion 10): with 聖盾指示物 a used counter never comes back, so a spent counter is a real change
    // in the keys and the body value; earlier steps replay with the older keys.
    private bool V10 => AiRevision >= 10;
    private static int ShieldMark(GameEngine g, MonsterInstance m) => g.V10 && g.ShieldCounterRules ? 10 + m.ShieldCounters : m.HasShield ? 1 : 0;
    internal static bool PointlessAttack(GameEngine before, GameEngine after, string pid, SimMove m) =>
        m.Kind == "attack" && !after.IsOver && EffectKey(before, pid, m.Source) == EffectKey(after, pid, m.Source);
    // A pointless attack that also loses the attacker. Shield bumps that cost nothing stay in the turn search:
    // pruning them as well measured weaker (41% vs v7 at Diamond, tools/RankedCheck --v8-vs-v7).
    internal static bool SuicideAttack(GameEngine before, GameEngine after, string pid, SimMove m) =>
        PointlessAttack(before, after, pid, m) && !after.PlayerById(pid).Field.Any(u => u.InstanceId == m.Source);
    private static bool Drew(GameEngine before, GameEngine after, string pid) => after.PlayerById(pid).Deck.Count < before.PlayerById(pid).Deck.Count;

    internal sealed record SimMove(string Kind, Guid Source, Guid? Target = null)
    {
        public override string ToString() => $"{Kind}:{Source}{(Target is { } t ? ">" + t : "")}";
    }

    // Debug only (tools/RankedCheck --explain): receives a description of each v7 decision. Never cloned into
    // simulations and never consulted by the AI, so tracing cannot change a decision or a replay.
    internal Action<string>? AiTrace { get; set; }
    private void Trace(string text) => AiTrace?.Invoke(text);
    private string Describe(SimMove? m)
    {
        if (m == null) return "end turn";
        string Name(Guid id) { var all = new[] { Player, Computer }; foreach (var pl in all) { if (pl.Board.FirstOrDefault(x => x.InstanceId == id) is { } b) return $"{b.Card.Name}({b.CurrentPP})"; if (pl.Hand.FirstOrDefault(x => x.InstanceId == id) is { } h) return h.Card.Name; } return id.ToString()[..8]; }
        return $"{m.Kind} {Name(m.Source)}{(m.Target is { } t ? " -> " + (t == Guid.Empty ? "hero" : Name(t)) : "")}";
    }
    private int _simCounter;
    private int SimSeed() { unchecked { return _aiSeed * 31 + (int)Revision * 7919 + TurnNumber * 104729 + ++_simCounter * 15485863; } }

    // The searches below are iterators: each `yield return` marks a point between simulated positions where the
    // browser may render (GameEngine.AiSlices.cs). They only touch clones there, so running them in one go
    // (ExecuteV7Ai) or in slices (BeginAiStep) computes exactly the same decision.
    private bool ExecuteV7Ai() { var r = new Out<bool>(); RunSlices(ExecuteV7AiSlices(r)); return r.Value; }
    private IEnumerable<int> ExecuteV7AiSlices(Out<bool> result)
    {
        if (IsOver || CurrentPhase == TurnPhase.NotStarted || !PlayerById(DecisionPlayerId).IsAi) { result.Value = false; yield break; }
        var p = PlayerById(DecisionPlayerId);
        // Choices and targets raised by our own actions keep the v6 logic, the same logic simulations use.
        if (IsWaiting || !Main(p) || !IsQuiescent) { result.Value = ExecuteTieredAi(); yield break; }
        if (p.Id != _planOwner) { _plan = null; _planOwner = p.Id; }
        var prof = ProfileForDecision(); _simCounter = 0;
        // Following a plan whose simulated position still matches skips both searches.
        var next = NextPlannedMove(p);
        if (next != null) Trace($"plan: {Describe(next)}");
        if (next == null)
        {
            if (Noise(101) < prof.LethalSight)
            {
                var found = new Out<SimMove?>();
                foreach (var slice in FindLethalSlices(p, prof, found)) yield return slice;
                if (found.Value is { } lethal) { Trace($"lethal: {Describe(lethal)}"); if (ApplyMove(this, p.Id, lethal)) { result.Value = true; yield break; } }
            }
            if (FillEnergyV6(p)) { Trace("energy (v6 rule)"); result.Value = true; yield break; }
            if (prof.Blunder > 0 && Noise(102) < prof.Blunder)
            {
                var legal = GenerateMoves(p, energy: false);
                if (V8) legal = legal.Where(m => m.Kind != "attack" || (CloneQuiescent(SimSeed(), p.Id) is var c && c.SimApply(x => ApplyMove(x, p.Id, m)) && !PointlessAttack(this, c, p.Id, m))).ToList();
                if (legal.Count > 0) { var pick = Pick(legal, 103); Trace($"blunder: {Describe(pick)}"); if (ApplyMove(this, p.Id, pick)) { result.Value = true; yield break; } }
            }
            var searched = new Out<SimMove?>();
            foreach (var slice in SearchTurnSlices(p, prof, searched)) yield return slice;
            next = searched.Value;
        }
        if (next == null) { _plan = null; result.Value = EndTurn(); yield break; }
        if (ApplyMove(this, p.Id, next)) { result.Value = true; yield break; }
        _plan = null; result.Value = ExecuteTieredAi();
    }

    // Energy keeps the v6 rule and choice (fill while short of the hand's costs, never the last playable card).
    private bool FillEnergyV6(PlayerState p)
    {
        var fill = EnergyFillCandidates(p);
        if (p.HasFilledEnergyThisTurn || fill.Count == 0 || !(p.Hand.Count > 1 || !p.Hand.Any(c => CanPlayCard(p, c)))) return false;
        if (p.TotalEnergy >= Math.Max(5, p.Hand.Max(c => ActualCost(p, c.Card)))) return false;
        var energy = fill.OrderBy(c => (c.Card.IsCounter ? 3 : 0) + HandValue(p, c) + (p.Hand.Count(x => x.Card.Id == c.Card.Id) > 1 ? -2 : 0)).ThenBy(c => c.Card.Id, StringComparer.Ordinal).First();
        return PlayEnergy(p, energy);
    }

    // Turn plan cache: the best sequence from the last search, followed while the real position matches
    // the simulated one; any difference (random draws, hidden counters) triggers a fresh search.
    private sealed record PlanStep(SimMove Move, string ExpectedAfter);
    private List<PlanStep>? _plan;
    private string _planBefore = "", _planOwner = "";
    internal static string PositionKey(GameEngine g, string pid)
    {
        var p = g.PlayerById(pid); var e = g.GetOpponent(p);
        string Side(PlayerState s) => $"{s.Hp}/{s.AvailableEnergy}/{s.TotalEnergy}/{s.Hand.Count}/{s.Deck.Count}/{s.Graveyard.Count}:" +
            string.Join(",", s.Board.Select(m => $"{m.InstanceId}:{m.CurrentPP}:{m.CurrentDP}:{(m.IsTapped ? 1 : 0)}:{ShieldMark(g, m)}:{m.Attachments.Count}"));
        return $"{g.TurnNumber}|{g.CurrentTurnPlayerId}|{Side(p)}|{string.Join(",", p.Hand.Select(c => c.InstanceId))}|{Side(e)}";
    }
    private SimMove? NextPlannedMove(PlayerState p)
    {
        if (_plan == null || _plan.Count == 0) return null;
        if (PositionKey(this, p.Id) != _planBefore) { _plan = null; return null; }
        var step = _plan[0]; _plan.RemoveAt(0); _planBefore = step.ExpectedAfter;
        return step.Move;
    }

    private sealed class SearchNode(GameEngine g, SearchNode? parent, SimMove? move, double score)
    {
        public GameEngine G = g; public SearchNode? Parent = parent; public SimMove? Move = move; public double Score = score;
    }

    // Opponent reply (ai-design.md §2.5): the best few end-of-turn positions are played forward through the
    // opponent's next turn with the v6 master policy. The opponent's hand stays unknown, so it can only use its
    // board; its hidden cards are priced by the opponent model in Evaluate. Dealing it sampled hands from the
    // public pool was tried and measured weaker (tools/RankedCheck), so it is not used.
    // Share of the reply score in the final choice (tuned with tools/RankedCheck; .6 was weaker).
    private const double ReplyWeight = .85;
    private IEnumerable<int> BestAfterReplySlices(PlayerState p, V7Profile prof, List<SearchNode> all, Out<SearchNode> result)
    {
        var candidates = all.Where(n => n.Move == null || n.G.IsOver || n.G.IsQuiescent).OrderByDescending(n => n.Score).Take(prof.Replies).ToList();
        if (!candidates.Contains(all[0])) candidates.Add(all[0]);
        SearchNode best = candidates[0]; double bestValue = double.MinValue;
        foreach (var n in candidates)
        {
            double value;
            if (n.G.IsOver) value = n.Score;
            else
            {
                var reply = new Out<double>();
                foreach (var slice in ReplySlices(n.G, p.Id, prof.ReplyPlies, reply)) yield return slice;
                value = n.Score * (1 - ReplyWeight) + reply.Value * ReplyWeight;
            }
            if (AiTrace != null) { var first = n; while (first.Parent?.Parent != null) first = first.Parent; Trace($"  reply {value,8:F2} (turn-end {n.Score:F2}) first {(first.Move == null ? "end turn" : Describe(first.Move))}"); }
            if (value > bestValue + 1e-9) { bestValue = value; best = n; }
        }
        result.Value = best;
    }

    private IEnumerable<int> ReplySlices(GameEngine leaf, string pid, int plies, Out<double> result)
    {
        if (leaf.IsOver) { result.Value = leaf.Evaluate(pid); yield break; }
        var g = leaf.CloneQuiescent(SimSeed(), pid);
        if (!g.SimApply(x => x.EndTurn())) { result.Value = leaf.Evaluate(pid); yield break; }
        g.AiLevel = 5; g.AiRevision = 6;
        for (int i = 0; i < 40 && !g.IsOver && g.CurrentTurnPlayerId != pid; i++)
        {
            if (!g.ExecuteAiStep()) break;
            yield return 0;
        }
        // Two plies (Master): also play our own next turn with the v6 master policy and average both positions,
        // so lines that leave a good follow-up beat lines that only survive the reply.
        if (plies >= 2 && !g.IsOver && g.CurrentTurnPlayerId == pid)
        {
            var mid = g.Evaluate(pid);
            for (int i = 0; i < 40 && !g.IsOver && g.CurrentTurnPlayerId == pid; i++)
            {
                if (!g.ExecuteAiStep()) break;
                yield return 0;
            }
            result.Value = (mid + g.Evaluate(pid)) / 2; yield break;
        }
        result.Value = g.Evaluate(pid);
    }

    // Turn search (ai-design.md §2.2–2.3): beam search over this turn's move sequences on hidden-information
    // clones, scoring each position with Evaluate. Returns the first move of the best sequence, or null to end
    // the turn. Ending the turn now is always a candidate.
    internal SimMove? SearchTurn(PlayerState p, V7Profile prof) { var r = new Out<SimMove?>(); RunSlices(SearchTurnSlices(p, prof, r)); return r.Value; }
    private IEnumerable<int> SearchTurnSlices(PlayerState p, V7Profile prof, Out<SimMove?> result)
    {
        int budget = prof.Budget, salt = 200;
        var root = new SearchNode(CloneQuiescent(SimSeed(), p.Id), null, null, 0);
        root.Score = root.G.Evaluate(p.Id);
        var best = root; var frontier = new List<SearchNode> { root }; var all = new List<SearchNode> { root };
        for (int depth = 0; depth < prof.Depth && frontier.Count > 0 && budget > 0; depth++)
        {
            var children = new List<SearchNode>();
            foreach (var node in frontier)
            {
                var me = node.G.PlayerById(p.Id);
                foreach (var m in node.G.GenerateMoves(me, energy: false))
                {
                    if (--budget < 0) break;
                    yield return 0;
                    var child = node.G.CloneQuiescent(SimSeed(), p.Id);
                    if (!child.SimApply(x => ApplyMove(x, p.Id, m), Math.Max(AiLevel, 0))) continue;
                    bool leaf = false; var value = child.Evaluate(p.Id);
                    if (V8)
                    {
                        if (SuicideAttack(node.G, child, p.Id, m)) continue;
                        if (!child.IsOver && Drew(node.G, child, p.Id))
                        {
                            // Chance node: average over sampled draws and stop here; the real draw triggers a new search.
                            leaf = true;
                            for (int k = 1; k < DrawSamples; k++)
                            {
                                var sample = node.G.CloneQuiescent(SimSeed(), p.Id);
                                value += sample.SimApply(x => ApplyMove(x, p.Id, m), Math.Max(AiLevel, 0)) ? sample.Evaluate(p.Id) : child.Evaluate(p.Id);
                            }
                            value /= DrawSamples;
                        }
                    }
                    var score = value + (prof.Sigma > 0 ? (Noise(salt++) - .5) * 2 * prof.Sigma : 0);
                    var c = new SearchNode(child, node, m, score);
                    if (score > best.Score + 1e-9) best = c;
                    all.Add(c);
                    if (!leaf && !child.IsOver && child.CurrentTurnPlayerId == p.Id && child.IsQuiescent) children.Add(c);
                }
                if (budget < 0) break;
            }
            frontier = children.OrderByDescending(c => c.Score).Take(prof.Width).ToList();
        }
        if (prof.Replies > 0)
        {
            var replied = new Out<SearchNode>();
            foreach (var slice in BestAfterReplySlices(p, prof, all, replied)) yield return slice;
            best = replied.Value;
        }
        var path = new List<SearchNode>();
        for (var n = best; n != root; n = n.Parent!) path.Insert(0, n);
        if (AiTrace != null)
        {
            Trace($"search: {all.Count} nodes, root {root.Score:F2}, chosen {best.Score:F2}: " + (path.Count == 0 ? "end turn" : string.Join(" ; ", path.Select(n => n.Move!.Kind + (n.Move.Target is { } t ? ">" + (t == Guid.Empty ? "hero" : "unit") : "")))));
            foreach (var c in all.Where(n => n.Parent == root).OrderByDescending(n => n.Score).Take(12))
                Trace($"  first move {c.Score,8:F2}{(c == path.FirstOrDefault() ? " *" : "  ")} {Describe(c.Move)}");
        }
        if (best == root) { _plan = null; result.Value = null; yield break; }
        _plan = path.Skip(1).Select((n, i) => new PlanStep(n.Move!, PositionKey(n.G, p.Id))).ToList();
        _planBefore = PositionKey(path[0].G, p.Id);
        result.Value = path[0].Move;
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
    internal SimMove? FindLethal(PlayerState p, V7Profile prof) { var r = new Out<SimMove?>(); RunSlices(FindLethalSlices(p, prof, r)); return r.Value; }
    private IEnumerable<int> FindLethalSlices(PlayerState p, V7Profile prof, Out<SimMove?> result)
    {
        result.Value = null;
        var e = GetOpponent(p);
        if (DamageBound(p, e) < e.Hp) yield break;
        var budget = new[] { prof.LethalBudget };
        IEnumerable<int> Search(GameEngine g, int depth, Out<SimMove?> found)
        {
            found.Value = null;
            var me = g.PlayerById(p.Id);
            foreach (var m in g.GenerateMoves(me, energy: !me.HasFilledEnergyThisTurn, sets: false))
            {
                if (--budget[0] < 0) yield break;
                var child = g.CloneQuiescent(SimSeed(), p.Id);
                var applied = child.SimApply(x => ApplyMove(x, p.Id, m));
                yield return 0;
                if (!applied) continue;
                if (child.GetOpponent(child.PlayerById(p.Id)).HasLost && !child.PlayerById(p.Id).HasLost) { found.Value = m; yield break; }
                if (V8 && (PointlessAttack(g, child, p.Id, m) || Drew(g, child, p.Id))) continue;
                if (depth > 1 && !child.IsOver && child.CurrentTurnPlayerId == p.Id)
                {
                    var deeper = new Out<SimMove?>();
                    foreach (var slice in Search(child, depth - 1, deeper)) yield return slice;
                    if (deeper.Value != null) { found.Value = m; yield break; }
                }
            }
        }
        if (!V8)
        {
            foreach (var slice in Search(CloneQuiescent(SimSeed(), p.Id), prof.LethalDepth, result)) yield return slice;
            yield break;
        }
        // Shortest first: a lethal that needs fewer moves never throws monsters at a taunt it does not have to.
        for (int depth = 1; depth <= prof.LethalDepth; depth++)
        {
            budget[0] = prof.LethalBudget;
            foreach (var slice in Search(CloneQuiescent(SimSeed(), p.Id), depth, result)) yield return slice;
            if (result.Value != null) yield break;
        }
    }
}
