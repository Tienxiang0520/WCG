using WcgWeb.Models;
namespace WcgWeb.Services;

// Ranked AI scaled by tier (AiLevel 0 青銅 … 5 大師). Practice keeps ExecuteV06Ai (AiLevel -1).
// Only public information is used: both boards, HP, hand/deck counts and the AI's own hand.
// Randomness comes from a hash of the replay seed and public game counters, so replays are exact
// and the game's shuffle/discard RNG is never consumed by AI decisions.
public partial class GameEngine
{
    // Bump when ranked AI decisions change; RankedSession stores it with every journal action.
    public const int RankedAiVersion = 4;
    private int _aiSeed;
    // Ranked replays seed this through SetReplaySeed; simulations may seed it directly.
    public void SeedRankedAi(int seed) => _aiSeed = seed;
    // Blunder: chance per decision to play a random legal move. LethalSight: chance to look for board lethal.
    public sealed record AiProfile(double Blunder, double LethalSight, bool BurnLethal, bool ScoredPlays, bool SmartTargets,
        bool SmartEnergy, bool UseCounters, bool ThreatAware, double MonsterTempo, int PlanDepth, int PlanBudget);
    // Calibrated with tools/RankedCheck (aiLadder): each tier plays the five presets against the practice AI.
    public static AiProfile ProfileFor(int level) => Math.Clamp(level, 0, 5) switch
    {
        0 => new(.30, .5, false, false, false, false, false, false, 0, 0, 0),  // 青銅：常隨機出牌與選目標、半數看漏斬殺、不蓋反擊、會硬撞
        1 => new(.20, .8, false, false, false, false, true, false, 0, 0, 0),   // 白銀：怪物優先的基本順序，偶爾失誤或看漏斬殺
        2 => new(.12, 1, true, true, true, false, true, false, 0, 0, 0),       // 黃金：出牌與攻擊一起評分、會挑目標、用直傷斬殺
        3 => new(.05, 1, true, true, true, false, true, true, 3, 0, 0),        // 白金：注意對手反撲、優先鋪場、會避開蓋牌
        4 => new(.08, 1, true, true, true, true, true, true, 0, 2, 400),       // 鑽石：聰明填能量、兩步攻擊規劃
        _ => new(0, 1, true, true, true, true, true, true, 3, 3, 1500),        // 大師：不失誤、三步攻擊規劃、先解嘲諷再斬殺
    };
    private sealed record AiMove(string Kind, double Score, CardInstance? Card = null, MonsterInstance? Attacker = null, MonsterInstance? Target = null);

    private double Noise(int salt)
    {
        unchecked
        {
            ulong x = (ulong)(uint)_aiSeed * 0x9E3779B97F4A7C15UL ^ (ulong)Revision * 0xBF58476D1CE4E5B9UL
                ^ (ulong)(uint)TurnNumber * 0x94D049BB133111EBUL ^ (ulong)(uint)salt * 0xD6E8FEB86659FD93UL ^ (ulong)(uint)AiLevel;
            x ^= x >> 30; x *= 0xBF58476D1CE4E5B9UL; x ^= x >> 27; x *= 0x94D049BB133111EBUL; x ^= x >> 31;
            return (x >> 11) * (1.0 / (1UL << 53));
        }
    }
    private T Pick<T>(IReadOnlyList<T> items, int salt) => items[(int)(Noise(salt) * items.Count) % items.Count];

    private static double BodyValue(MonsterInstance m) => m.CurrentPP / 450d + m.CurrentDP * 2 + (m.IsTaunt ? 1.5 : 0) + (m.HasShield ? 2 : 0) + (m.HasPoison ? 3 : 0)
        + (m.Card.Id is "WCG-120" or "WCG-060" or "WCG-177" or "WCG-099" or "WCG-185" or "WCG-190" ? 3 : 0);
    private static double CardValue(CardDefinition c) => c.IsMonster ? (c.PP ?? 0) / 450d + (c.DP ?? 1) * 2 + (c.HasTaunt ? 1.5 : 0) + (c.HasCharge ? 2 : 0) + (c.HasDivineShield ? 2 : 0)
        : 3 + c.TotalCost * .6;

    private bool ExecuteTieredAi()
    {
        if (IsOver || CurrentPhase == TurnPhase.NotStarted || !PlayerById(DecisionPlayerId).IsAi) return false;
        var p = PlayerById(DecisionPlayerId); var e = GetOpponent(p); var prof = ProfileFor(AiLevel);
        if (CurrentPendingChoice is { } choice) return TieredChoice(p, e, choice, prof);
        if (CurrentPendingTarget is { } pending) return TieredTarget(p, e, pending, prof);
        if (!Main(p)) return false;

        if (Noise(1) < prof.LethalSight && TryLethal(p, e, prof)) return true;

        if (!p.HasFilledEnergyThisTurn && p.Hand.Count > 0 && (p.Hand.Count > 1 || !p.Hand.Any(c => CanPlayCard(p, c)))
            && p.TotalEnergy < Math.Max(5, p.Hand.Max(c => ActualCost(p, c.Card))))
        {
            var energy = prof.SmartEnergy
                ? p.Hand.OrderBy(c => (c.Card.IsCounter ? 3 : 0) + HandValue(p, c) + (p.Hand.Count(x => x.Card.Id == c.Card.Id) > 1 ? -2 : 0)).ThenBy(c => c.Card.Id, StringComparer.Ordinal).First()
                : AiLevel == 0 ? Pick(p.Hand, 2)
                : p.Hand.OrderBy(c => c.Card.IsCounter ? 10 : 0).ThenByDescending(c => c.Card.TotalCost).First();
            return PlayEnergy(p, energy);
        }
        if (prof.UseCounters && p.Occupied < 5 && !p.Structures.Any(x => x.IsSet) && (!prof.ScoredPlays || e.Field.Count > 0 || TurnNumber > 4))
        {
            var reaction = p.Hand.FirstOrDefault(c => c.Card.IsCounter && p.AvailableEnergy >= c.Card.TotalCost);
            if (reaction != null) return SetCard(p, reaction);
        }
        var active = p.Structures.FirstOrDefault(x => CanActivate(p, x) && (!prof.ScoredPlays || StructureWorthIt(p, e, x)));
        if (active != null) return Activate(p, active);

        var moves = prof.ScoredPlays ? ScoredMoves(p, e, prof) : SimpleMoves(p, prof);
        if (moves.Count > 0 && Noise(3) < prof.Blunder)
        {
            // A beginner sometimes plays something random, or simply stops early.
            var blunder = AiLevel == 0 && Noise(4) < .25 ? null : Pick(moves, 5);
            return blunder == null ? EndTurn() : Perform(p, e, blunder, prof) || ExecuteV06Ai();
        }
        var best = moves.OrderByDescending(m => m.Score).FirstOrDefault();
        if (best == null || best.Score <= 0) return EndTurn();
        return Perform(p, e, best, prof) || ExecuteV06Ai();
    }

    private bool Perform(PlayerState p, PlayerState e, AiMove move, AiProfile prof)
    {
        if (move.Kind == "attack") return Attack(p, move.Attacker!, move.Target);
        var card = move.Card!;
        if (card.Card.IsMonster) return SummonMonster(p, card);
        if (card.Card.IsEnchantment) return PlayEnchantment(p, card);
        var targets = GetDirectPlayTargets(p, card);
        if (targets.Count == 0) return CastSpell(p, card);
        var target = move.Target != null && targets.Contains(move.Target) ? move.Target
            : prof.SmartTargets ? BestSpellTarget(p, e, card.Card.Id, targets)
            : AiLevel == 0 ? Pick(targets, 6) : targets.OrderByDescending(m => (e.Field.Contains(m) ? 20 : 0) + m.CurrentPP / 500d).First();
        return target != null ? CastSpellAt(p, card, target) : CastSpell(p, card);
    }

    // 青銅／白銀：沿用試玩電腦的出牌與攻擊順序（怪物優先、低費優先），青銅改成隨機順序。
    private List<AiMove> SimpleMoves(PlayerState p, AiProfile prof)
    {
        var moves = new List<AiMove>();
        var playable = p.Hand.Where(c => CanPlayCard(p, c) && !SelfHarmful(p, c)).ToList();
        var ordered = AiLevel == 0 ? playable.OrderBy(c => Noise(10 + p.Hand.IndexOf(c))).ToList()
            : playable.OrderByDescending(c => c.Card.IsMonster).ThenBy(c => ActualCost(p, c.Card)).ToList();
        for (int i = 0; i < ordered.Count; i++) moves.Add(new("play", 100 - i, ordered[i]));
        foreach (var m in p.Field.Where(CanAttack).OrderByDescending(x => x.CurrentDP))
        {
            var victim = GetAttackTargets(m).FirstOrDefault(x => { var pr = CompareAttack(m, x); return pr.DefenderDies && !pr.AttackerDies; });
            if (victim != null) moves.Add(new("attack", 50 + BodyValue(victim), Attacker: m, Target: victim));
            else if (CanAttackPlayer(m) && m.CurrentDP > 0) moves.Add(new("attack", 40 + m.CurrentDP, Attacker: m));
            else if (GetAttackTargets(m).FirstOrDefault(x => m.HasPoison || m.CurrentPP == x.CurrentPP) is { } trade) moves.Add(new("attack", 30, Attacker: m, Target: trade));
            else if (AiLevel == 0 && GetAttackTargets(m).FirstOrDefault() is { } bad) moves.Add(new("attack", 1, Attacker: m, Target: bad)); // 青銅會硬撞
        }
        return moves;
    }

    // 黃金以上：所有出牌與攻擊一起評分。
    private List<AiMove> ScoredMoves(PlayerState p, PlayerState e, AiProfile prof)
    {
        var moves = new List<AiMove>();
        foreach (var c in p.Hand.Where(c => CanPlayCard(p, c)))
        {
            var score = c.Card.IsEnchantment ? 3 + c.Card.TotalCost * .3 : PlayValue(p, c);
            if (prof.ThreatAware) score += ActualCost(p, c.Card) * .3; // use the turn's energy
            if (c.Card.IsMonster) score += prof.MonsterTempo;
            if (p.Field.Any(m => m.HasShield) && p.AvailableEnergy - ActualCost(p, c.Card) < 1 && p.Hand.Count >= 3) score -= 2;
            moves.Add(new("play", score, c));
        }
        var dangerous = e.Field.Where(m => !m.AttackLocked && (m.IsSilenced || !m.Card.CannotAttack)).Sum(m => m.CurrentDP) >= p.Hp;
        foreach (var a in p.Field.Where(CanAttack))
        {
            if (CanAttackPlayer(a) && a.CurrentDP > 0)
                moves.Add(new("attack", a.CurrentDP * (prof.ThreatAware ? 4 : 3) + (a.CurrentDP >= e.Hp ? 1000 : 0)
                    - (prof.ThreatAware && dangerous ? 6 : 0) - (prof.ThreatAware && e.Structures.Any(x => x.IsSet) ? 1 : 0), Attacker: a));
            foreach (var d in GetAttackTargets(a))
            {
                var pr = CompareAttack(a, d);
                var score = (pr.DefenderDies ? BodyValue(d) : pr.DefenderShieldBreaks ? 1.5 : 0)
                    - (pr.AttackerDies ? BodyValue(a) : pr.AttackerShieldBreaks ? 1 : 0) + pr.PlayerDamage * 4;
                if (d.IsTaunt && pr.DefenderDies) score += 3;
                if (prof.ThreatAware && dangerous && pr.DefenderDies) score += d.CurrentDP * 5;
                moves.Add(new("attack", score, Attacker: a, Target: d));
            }
        }
        if (prof.PlanDepth >= 2 && p.Field.Any(CanAttack) && PlanAttacks(p, e, prof) is { } plan) moves.Add(plan);
        return moves;
    }

    private bool TryLethal(PlayerState p, PlayerState e, AiProfile prof)
    {
        var face = p.Field.Where(m => CanAttackPlayer(m) && m.CurrentDP > 0).ToArray();
        var damage = face.Sum(m => m.CurrentDP);
        if (damage >= e.Hp && face.Length > 0) return Attack(p, face.OrderByDescending(m => m.CurrentDP).First());
        if (!prof.BurnLethal) return false;
        int Burn(CardInstance c) => !CanPlayCard(p, c) ? 0 : c.Card.Id switch
        { "WCG-014" => 1, "WCG-006" => 2, "WCG-017" or "WCG-123" => 1, "WCG-013" => p.Hp > 1 ? 1 : 0, "WCG-170" => e.Field.All(m => m.CurrentPP <= 1500) ? 2 : 0, _ => 0 };
        var burn = p.Hand.Select(c => (card: c, dmg: Burn(c))).Where(x => x.dmg > 0).OrderByDescending(x => x.dmg).FirstOrDefault();
        if (burn.card != null && damage + burn.dmg >= e.Hp)
        {
            // Swing first when the burn spell does not need the attackers (怒意沸騰 sacrifices one).
            if (face.Length > 0 && burn.card.Card.Id != "WCG-006") return Attack(p, face.OrderByDescending(m => m.CurrentDP).First());
            var targets = GetDirectPlayTargets(p, burn.card);
            return targets.Count > 0 ? CastSpellAt(p, burn.card, BestSpellTarget(p, e, burn.card.Card.Id, targets) ?? targets[0])
                : burn.card.Card.IsMonster ? SummonMonster(p, burn.card) : CastSpell(p, burn.card);
        }
        if (prof.PlanDepth >= 3)
        {
            // 大師：場上傷害足夠但被嘲諷擋住時，先用法術解掉唯一的嘲諷。
            var taunts = e.Field.Where(m => m.IsTaunt).ToArray();
            var ready = p.Field.Where(m => CanAttack(m) && m.CurrentDP > 0).ToArray();
            if (taunts.Length == 1 && ready.Sum(m => m.CurrentDP) >= e.Hp)
                foreach (var c in p.Hand.Where(c => c.Card.IsSpell && !c.Card.IsCounter && CanPlayCard(p, c) && c.Card.Id is not ("WCG-024" or "WCG-137" or "WCG-168")))
                    if (GetDirectPlayTargets(p, c).Contains(taunts[0]) && c.Card.Id is not ("WCG-141" or "WCG-165" or "WCG-143")) return CastSpellAt(p, c, taunts[0]);
        }
        return false;
    }

    private bool StructureWorthIt(PlayerState p, PlayerState e, MonsterInstance s) => s.Card.Id switch
    {
        "WCG-128" => p.Field.Any(m => BodyValue(m) < 5 || m.Card.Id is "WCG-003" or "WCG-083" or "WCG-043" or "WCG-093" or "WCG-097" or "WCG-016") && p.Deck.Count > 3,
        "WCG-151" => e.Field.Any(m => m.CurrentPP <= 800),
        _ => true,
    };

    private static readonly HashSet<string> FriendlySpells = ["WCG-135", "WCG-141", "WCG-165", "WCG-143", "WCG-167", "WCG-062", "WCG-074", "WCG-036", "WCG-164", "WCG-169"];
    private MonsterInstance? BestSpellTarget(PlayerState p, PlayerState e, string id, IReadOnlyList<MonsterInstance> targets)
    {
        var own = targets.Where(p.Field.Contains).ToArray(); var enemy = targets.Where(e.Field.Contains).ToArray();
        if (id is "WCG-036" or "WCG-164" or "WCG-169") return own.OrderBy(BodyValue).FirstOrDefault();
        if (FriendlySpells.Contains(id))
            return own.OrderByDescending(m => (CanAttack(m) || m.IsTapped && id is "WCG-143" or "WCG-167" ? 5 : 0) + BodyValue(m)).FirstOrDefault();
        if (id is "WCG-024" or "WCG-137" or "WCG-184") return enemy.OrderByDescending(m => (m.IsTapped ? 0 : m.CurrentDP * 3) + BodyValue(m)).FirstOrDefault();
        return enemy.OrderByDescending(m => BodyValue(m) + (m.IsTaunt ? 3 : 0)).FirstOrDefault() ?? (id == "WCG-072" ? null : own.FirstOrDefault());
    }

    private bool SelfHarmful(PlayerState p, CardInstance c) => c.Card.Id switch
    {
        "WCG-019" => p.Hp <= 2, "WCG-165" => p.Hp <= 1, "WCG-085" => p.Hp <= 1, "WCG-013" => p.Hp <= 1, "WCG-096" => p.Hp <= 1,
        "WCG-098" or "WCG-161" => AiLevel > 0 && GetOpponent(p).Field.Count <= p.Field.Count,
        _ => false,
    };

    private bool TieredChoice(PlayerState p, PlayerState e, PendingChoice choice, AiProfile prof)
    {
        var shield = choice.Options.FirstOrDefault(o => o.Id == "SHIELD");
        if (shield != null) return SelectChoice(AiLevel == 0 && Noise(20) < .3 ? choice.Options.First(o => o.Id != "SHIELD") : shield);
        if (choice.Title.Contains("翻開")) return SelectChoice(choice.Options.FirstOrDefault(o => o.Id != "SKIP") ?? choice.Options[0]);
        if (choice.Title.Contains("固定進場格位") && AiLevel >= 1) return ExecuteV06Ai();
        if (AiLevel == 0) return SelectChoice(Pick(choice.Options.Where(o => o.Id != "SKIP" || choice.Options.Count == 1).ToList(), 21));
        if (!prof.SmartTargets) return ExecuteV06Ai();
        var discard = choice.Title.Contains("棄") || choice.Title.Contains("牌庫底") || choice.Title.Contains("置底");
        double Score(ChoiceOption o)
        {
            if (o.Id == "KEEP") return 100;
            if (o.Id == "SKIP") return -50;
            if (o.Id is "HEAL") return p.Hp <= 3 ? 12 : p.Hp <= 5 ? 5 : -10;
            if (o.Id is "KILL") return 10;
            if (o.Id is "SUMMON") return p.Occupied < 5 ? 9 : -10;
            if (o.Id is "LOOT" or "DRAW") return p.Deck.Count > 3 ? 4 : -90;
            if (o.Id is "YES") return p.Deck.Count > 4 ? 6 : -10;
            if (o.Id is "DISPEL") return e.Field.Any(m => m.Attachments.Count > 0) ? 6 : -1;
            if (o.Id is "READY") return 4;
            if (o.PreviewCard is not { } c) return 0;
            var value = CardValue(c) - (c.TotalCost > p.TotalEnergy + 2 ? 3 : 0);
            return discard ? -value : value;
        }
        return SelectChoice(choice.Options.OrderByDescending(Score).ThenBy(o => o.Id, StringComparer.Ordinal).First());
    }

    private bool TieredTarget(PlayerState p, PlayerState e, PendingTarget pending, AiProfile prof)
    {
        var legal = p.Board.Concat(e.Board).Where(m => pending.Validator?.Invoke(m) != false).ToList();
        if (legal.Count == 0) return false;
        if (AiLevel == 0) return SelectTarget(Pick(legal, 30));
        if (!prof.SmartTargets) return ExecuteV06Ai();
        var cost = pending.Title.Contains("犧牲") || pending.Title.Contains("祭品") || pending.Title.Contains("代價");
        var friendly = cost || pending.Title.Contains("附著聖盾") || pending.Title.Contains("己方") || pending.Title.Contains("可附著");
        MonsterInstance target;
        if (cost) target = legal.Where(p.Board.Contains).OrderBy(m => BodyValue(m) - (m.Card.Id is "WCG-003" or "WCG-083" or "WCG-043" or "WCG-093" or "WCG-097" or "WCG-016" ? 3 : 0)).FirstOrDefault() ?? legal[0];
        else if (friendly) target = legal.OrderByDescending(m => (p.Board.Contains(m) ? 20 : 0) + BodyValue(m)).First();
        else target = legal.OrderByDescending(m => (e.Board.Contains(m) ? 20 : 0) + BodyValue(m) + (m.IsTaunt ? 3 : 0)).First();
        return SelectTarget(target);
    }

    private double HandValue(PlayerState p, CardInstance c)
    {
        var value = CardValue(c.Card);
        if (ActualCost(p, c.Card) > p.TotalEnergy + 2) value -= 4;
        if (c.Card.IsSpell && !c.Card.IsCounter && PlayValue(p, c) <= 0) value -= 5;
        if (c.Card.HasCharge && GetOpponent(p).Hp <= (c.Card.DP ?? 1)) value += 15;
        return value;
    }

    private double PlayValue(PlayerState p, CardInstance c)
    {
        var e = GetOpponent(p); var id = c.Card.Id;
        if (SelfHarmful(p, c)) return -100;
        double Sum(IEnumerable<MonsterInstance> ms) => ms.Sum(BodyValue);
        if (c.Card.IsMonster)
        {
            var score = CardValue(c.Card) - ActualCost(p, c.Card) * .35;
            if (c.Card.HasCharge) score += (c.Card.DP ?? 1) * 2;
            if (c.Card.HasTaunt && e.Field.Sum(m => m.CurrentDP) >= p.Hp) score += 8;
            score += id switch
            {
                "WCG-013" => e.Hp == 1 ? 1000 : 0,
                "WCG-035" => e.Field.Count(m => !m.IsTapped) * 2,
                "WCG-020" => Sum(e.Field.Where(m => m.CurrentPP <= 1300)),
                "WCG-091" => Sum(e.Field.Where(m => m.CurrentPP <= 500)),
                "WCG-105" => Sum(e.Field.Where(m => m.CurrentPP <= 500)) - Sum(p.Field.Where(m => m.CurrentPP <= 500)),
                "WCG-161" => Sum(e.Field) - Sum(p.Field) - p.Hand.Count * 2,
                "WCG-095" => -4,
                "WCG-047" or "WCG-027" or "WCG-115" => p.Hand.Any(x => x != c && x.Card.IsMonster && x.Card.TotalCost <= 1) ? 4 : 0,
                "WCG-001" or "WCG-007" or "WCG-102" => e.Field.Any(m => m.CurrentPP <= 500) ? 3 : 0,
                "WCG-077" => e.Field.Any(m => m.CurrentPP >= 2300) ? 6 : 0,
                "WCG-191" => p.Field.Any(m => m.Card.Will == "秩序") && e.Field.Any(m => m.CurrentPP >= 2000) ? 6 : 0,
                "WCG-117" => e.Field.Any(m => m.CurrentPP <= 1000) ? 3 : 0,
                _ => 0,
            };
            return score;
        }
        var targets = SpellTargets(p, c.Card);
        if (FriendlySpells.Contains(id))
        {
            var own = targets.Where(p.Field.Contains).ToArray();
            if (own.Length == 0) return id is "WCG-062" or "WCG-074" ? -1 : -5;
            return id switch
            {
                "WCG-062" or "WCG-074" => (p.AvailableEnergy - c.Card.TotalCost >= 1 ? 4 : 1) + (id == "WCG-074" ? 2 : 0),
                "WCG-135" => 3 + (own.Any(CanAttack) ? 2 : 0),
                "WCG-141" => own.Any(m => m.IsTapped && m.CurrentDP >= 1) && e.Hp <= 3 ? 6 : -2,
                "WCG-165" or "WCG-143" or "WCG-167" => own.Any(m => CanAttack(m) || m.IsTapped) && e.Field.Count > 0 ? 2 : -2,
                "WCG-164" or "WCG-169" or "WCG-036" => p.Hand.Count < 3 && own.Any(m => BodyValue(m) < 4) ? 2 : -3,
                _ => 1,
            };
        }
        var removal = targets.Where(e.Field.Contains).ToArray();
        if (removal.Length > 0)
        {
            var score = removal.Max(m => BodyValue(m) + (m.IsTaunt ? 3 : 0));
            if (id is "WCG-024" or "WCG-168") score *= .5;
            if (id is "WCG-026" or "WCG-139" or "WCG-137" or "WCG-184") score *= .75;
            if (id == "WCG-140") score -= 2;
            if (id is "WCG-017" or "WCG-123") score += e.Hp == 1 ? 1000 : 4;
            if (id is "WCG-048" or "WCG-086" or "WCG-004" or "WCG-072") score += 2;
            if (id == "WCG-088") score -= p.Field.Select(BodyValue).DefaultIfEmpty(20).Min();
            return score - c.Card.TotalCost * .5;
        }
        if (NeedsSpellTarget(id)) return -5;
        return id switch
        {
            "WCG-014" => e.Hp == 1 ? 1000 : 3,
            "WCG-006" => e.Hp <= 2 ? 1000 : 6 - p.Field.Select(BodyValue).DefaultIfEmpty(20).Min(),
            "WCG-050" or "WCG-066" or "WCG-070" => p.Hp == 7 ? -10 : (7 - p.Hp) * 2 + (id == "WCG-066" ? 2 : 0),
            "WCG-098" => Sum(e.Field) - Sum(p.Field) - 2,
            "WCG-068" or "WCG-078" => Sum(e.Field.Where(m => m.CurrentPP <= 700)) + (id == "WCG-078" && p.Hp < 7 ? 2 : 0) - 2,
            "WCG-092" => Sum(e.Field.Where(m => m.CurrentPP <= 800)) - Sum(p.Field.Where(m => m.CurrentPP <= 800)) - 2,
            "WCG-008" => Sum(e.Field.Where(m => m.CurrentPP <= 500)) - Sum(p.Field.Where(m => m.CurrentPP <= 500)) - 2,
            "WCG-038" => Sum(e.Field.Where(m => m.Card.TotalCost <= 2)) - Sum(p.Field.Where(m => m.Card.TotalCost <= 2)) - 2,
            "WCG-170" => e.Field.Count == 0 ? 6 : Sum(e.Field.Where(m => m.CurrentPP <= 1500)) - 2,
            "WCG-176" => Sum(e.Field.Where(m => m.CurrentPP >= 1500)) - Sum(p.Field.Where(m => m.CurrentPP >= 1500)) + (p.Hp < 7 ? 2 : 0) - 2,
            "WCG-173" => Sum(e.Field) - Sum(p.Field) + 2,
            "WCG-044" => p.TotalEnergy < 6 && p.Deck.Count > 3 ? 5 : -1,
            "WCG-052" => Math.Min(2, p.Hand.Count(x => x.Card.IsMonster && x.Card.TotalCost <= 2)) * 5 - 3,
            "WCG-010" => p.Field.Count == 0 ? -5 : p.Hand.Count < 4 ? 6 : 2,
            "WCG-058" or "WCG-090" => p.Graveyard.Any(x => x.Card.IsMonster) ? 4 : -2,
            "WCG-019" or "WCG-096" => p.Hand.Count < 3 ? 4 : -1,
            _ => p.Deck.Count <= 3 ? -10 : p.Hand.Count < 4 ? 5 : 1,
        };
    }

    // Bounded public-board attack search (鑽石 depth 2, 大師 depth 3). Shields spend the owner's
    // untapped energy; death/entry triggers and hidden cards are deliberately not simulated.
    private sealed record PlanUnit(Guid Id, int PP, int DP, bool Taunt, bool Poison, bool Shield, bool Trample, bool Ready, bool IgnoresLowTaunt, bool Attacks, double Value);
    private AiMove? PlanAttacks(PlayerState p, PlayerState e, AiProfile prof)
    {
        int budget = prof.PlanBudget;
        PlanUnit Unit(MonsterInstance m) => new(m.InstanceId, m.CurrentPP, m.CurrentDP, m.IsTaunt, m.HasPoison, m.HasShield, m.HasTrample, CanAttack(m),
            !m.IsSilenced && m.Card.Id == "WCG-059", !m.AttackLocked && (m.IsSilenced || !m.Card.CannotAttack), BodyValue(m));
        double Evaluate(List<PlanUnit> own, List<PlanUnit> opp, int hp)
        {
            if (hp <= 0) return 10000;
            var score = own.Sum(u => u.Value) - opp.Sum(u => u.Value) + (e.Hp - hp) * 4;
            var incoming = opp.Where(u => u.Attacks).Sum(u => u.DP);
            if (!own.Any(u => u.Taunt)) score -= incoming >= p.Hp ? 35 : incoming * 1.5;
            else score -= incoming >= p.Hp ? 8 : incoming * .5;
            return score;
        }
        var mine = p.Field.Select(Unit).ToList(); var theirs = e.Field.Select(Unit).ToList();
        var baseline = Evaluate(mine, theirs, e.Hp); AiMove? best = null; double bestValue = baseline;
        void Search(List<PlanUnit> own, List<PlanUnit> opp, int hp, int myEnergy, int oppEnergy, int left, (Guid a, Guid? d)? first)
        {
            if (--budget < 0) return;
            var score = Evaluate(own, opp, hp);
            if (first is { } f && score > bestValue + .01)
            {
                bestValue = score;
                best = new("attack", score - baseline + .2, Attacker: p.Field.First(m => m.InstanceId == f.a), Target: f.d is { } did ? e.Field.First(m => m.InstanceId == did) : null);
            }
            if (left == 0 || hp <= 0) return;
            foreach (var a in own.Where(u => u.Ready).ToArray())
            {
                var taunts = opp.Where(u => u.Taunt && (!a.IgnoresLowTaunt || u.PP > 1300)).ToList();
                var targets = taunts.Count > 0 ? taunts.Cast<PlanUnit?>().ToList() : opp.Cast<PlanUnit?>().Append(null).ToList();
                foreach (var d in targets)
                {
                    var aa = own.ToList(); var dd = opp.ToList(); int me = myEnergy, them = oppEnergy, nhp = hp;
                    var ai = aa.FindIndex(u => u.Id == a.Id);
                    if (d == null) { if (a.DP <= 0) continue; nhp -= a.DP; aa[ai] = a with { Ready = false }; }
                    else
                    {
                        bool ah = a.PP <= d.PP || d.Poison, dh = d.PP <= a.PP || a.Poison;
                        bool asaved = ah && a.Shield && me > 0, dsaved = dh && d.Shield && them > 0;
                        if (asaved) me--; if (dsaved) them--;
                        if (ah && !asaved) aa.RemoveAt(ai); else aa[ai] = a with { Ready = false };
                        var di = dd.FindIndex(u => u.Id == d.Id);
                        if (dh && !dsaved) { dd.RemoveAt(di); if ((!ah || asaved) && a.Trample && a.PP - d.PP >= 700) nhp--; }
                    }
                    Search(aa, dd, nhp, me, them, left - 1, first ?? (a.Id, d?.Id));
                }
            }
        }
        Search(mine, theirs, e.Hp, p.AvailableEnergy, e.AvailableEnergy, prof.PlanDepth, null);
        return best;
    }
}
