using LcgWeb.Models;
namespace LcgWeb.Services;

public partial class GameEngine
{
    private sealed record AiMove(string Kind, double Score, CardInstance? Card = null, MonsterInstance? Attacker = null, MonsterInstance? Target = null);
    private static double BodyValue(MonsterInstance m) => m.CurrentPP / 450d + m.CurrentDP * 2 + (m.IsTaunt ? 1.5 : 0) + m.ShieldCount * 2 + (m.HasPoison ? 3 : 0);
    private static double CardValue(CardDefinition c) => c.IsMonster ? (c.PP ?? 0) / 450d + (c.DP ?? 1) * 2 + (c.HasTaunt ? 1.5 : 0) + (c.HasCharge ? 2 : 0) + (c.HasDivineShield ? 2 : 0) : 3 + c.TotalCost * .6;
    private bool ExecuteRankedAiStep()
    {
        if (IsOver || CurrentPhase == TurnPhase.NotStarted || !PlayerById(DecisionPlayerId).IsAi) return false;
        var p = PlayerById(DecisionPlayerId); var enemy = GetOpponent(p);
        if (CurrentPendingChoice is { } choice)
        {
            if (AiLevel < 2) return SelectChoice(choice.Options.FirstOrDefault(o => o.Id != "SKIP") ?? choice.Options[0]);
            double Score(ChoiceOption o)
            {
                if (o.Id == "KEEP") return 100;
                if (o.Id == "SKIP") return -100;
                if (o.Id == "HEAL") return p.Hp < 5 ? 12 : p.Hp < 7 ? 5 : -10;
                if (o.Id == "KILL") return 10;
                if (o.Id == "SUMMON") return p.Field.Count < 4 ? 9 : -10;
                if (o.Id == "LOOT" || o.Id == "DRAW") return p.Deck.Count > 0 ? 4 : -90;
                var c = o.PreviewCard;
                return c == null ? 0 : CardValue(c) * (choice.Title.Contains("棄掉的手牌") || choice.Title.Contains("牌庫底") ? -1 : 1);
            }
            return SelectChoice(choice.Options.OrderByDescending(Score).First());
        }
        if (CurrentPendingTarget is { } pending)
        {
            var legal = enemy.Field.Concat(p.Field).Where(m => pending.Validator?.Invoke(m) != false).ToArray();
            var sacrifice = pending.Title.Contains("犧牲");
            var target = AiLevel == 0 ? legal.FirstOrDefault() : legal.OrderByDescending(m => BodyValue(m) * (sacrifice ? -1 : 1)
                + (enemy.Field.Contains(m) && m.IsTaunt ? 3 : 0)).FirstOrDefault();
            return target != null && SelectTarget(target);
        }
        if (!Main(p)) return false;
        var attackers = p.Field.Where(CanAttack).ToArray();
        // Only public board damage is used. This does not consult enemy hand identities or deck order.
        if (AiLevel >= 2)
        {
            var face = attackers.Where(CanAttackPlayer).ToArray();
            if (face.Sum(m => m.CurrentDP) >= enemy.Hp && face.Any(m => m.CurrentDP > 0)) return Attack(p, face.First(m => m.CurrentDP > 0));
            var burn = p.Hand.FirstOrDefault(c => CanPlayCard(p, c) && (c.Card.Id == "LCG-014" && enemy.Hp <= 1 || c.Card.Id == "LCG-006" && enemy.Hp <= 2));
            if (burn != null) return CastSpell(p, burn);
        }
        if (!p.HasFilledEnergyThisTurn && p.Hand.Count > 0 && p.TotalEnergy < p.Hand.Max(c => c.Card.TotalCost) + 1 && (p.Hand.Count > 1 || !p.Hand.Any(c => CanPlayCard(p, c))))
        {
            var energy = AiLevel < 2 ? p.Hand.OrderByDescending(c => c.Card.TotalCost).First()
                : p.Hand.OrderBy(c => HandValue(p, c) + (p.Hand.Count(x => x.Card.Id == c.Card.Id) > 1 ? -2 : 0)).First();
            return PlayEnergy(p, energy);
        }
        var playable = p.Hand.Where(c => CanPlayCard(p, c)).ToArray();
        if (AiLevel == 0)
        {
            var c = playable.FirstOrDefault();
            if (c != null) return c.Card.IsMonster ? SummonMonster(p, c) : CastSpell(p, c);
            foreach (var m in attackers)
            {
                if (CanAttackPlayer(m) && m.CurrentDP > 0) return Attack(p, m);
                var target = GetAttackTargets(m).FirstOrDefault(); if (target != null) return Attack(p, m, target);
            }
            return EndTurn();
        }
        var moves = new List<AiMove>();
        foreach (var c in playable)
        {
            var score = PlayValue(p, c);
            if (AiLevel == 1) score = c.Card.IsMonster ? 10 + c.Card.TotalCost : score;
            moves.Add(new("play", score, c));
        }
        foreach (var a in attackers)
        {
            if (CanAttackPlayer(a)) moves.Add(new("attack", a.CurrentDP * (AiLevel >= 3 ? 4 : 3) + (a.CurrentDP >= enemy.Hp ? 1000 : 0), Attacker: a));
            foreach (var d in GetAttackTargets(a))
            {
                var preview = CompareAttack(a, d);
                var score = (preview.DefenderDies ? BodyValue(d) : preview.DefenderShieldBreaks ? 2 : 0)
                    - (preview.AttackerDies ? BodyValue(a) : preview.AttackerShieldBreaks ? 2 : 0) + preview.PlayerDamage * 4;
                if (d.IsTaunt && preview.DefenderDies) score += 4;
                if (AiLevel >= 3 && enemy.Field.Where(m => !m.IsFrozen).Sum(m => m.CurrentDP) >= p.Hp && preview.DefenderDies) score += d.CurrentDP * 5;
                moves.Add(new("attack", score, Attacker: a, Target: d));
            }
        }
        if (AiLevel >= 4 && attackers.Length > 0)
        {
            var plan = PlanPublicAttacks(p, enemy, AiLevel == 5 ? 3 : 2);
            if (plan is { } chosen) moves.Add(chosen);
        }
        var best = moves.OrderByDescending(m => m.Score).FirstOrDefault();
        if (best == null || best.Score <= 0) return EndTurn();
        if (best.Kind == "attack") return Attack(p, best.Attacker!, best.Target);
        var card = best.Card!;
        if (card.Card.IsMonster) return SummonMonster(p, card);
        if (AiLevel >= 2 && GetDirectPlayTargets(p, card).Count > 0)
        {
            var targets = GetDirectPlayTargets(p, card);
            var friendlyBounce = card.Card.Id == "LCG-036";
            var target = targets.OrderByDescending(m => BodyValue(m) * (friendlyBounce ? -1 : 1) + (m.IsTaunt ? 3 : 0)).First();
            return CastSpellAt(p, card, target);
        }
        return CastSpell(p, card);
    }
    private double HandValue(PlayerState p, CardInstance c)
    {
        var value = CardValue(c.Card);
        if (c.Card.TotalCost > p.TotalEnergy + 2) value -= 4;
        if (c.Card.IsSpell && PlayValue(p, c) <= 0) value -= 5;
        if (c.Card.HasCharge && GetOpponent(p).Hp <= (c.Card.DP ?? 1)) value += 15;
        return value;
    }
    private double PlayValue(PlayerState p, CardInstance c)
    {
        var e = GetOpponent(p); var id = c.Card.Id;
        if (c.Card.IsMonster)
        {
            if (id == "LCG-085" && p.Hp <= 1 || id == "LCG-013" && p.Hp <= 1 && e.Hp > 1) return -100;
            var score = CardValue(c.Card) - c.Card.TotalCost * .35;
            if (c.Card.HasCharge) score += (c.Card.DP ?? 1) * 2;
            if (c.Card.HasTaunt && e.Field.Sum(m => m.CurrentDP) >= p.Hp) score += 8;
            if (id == "LCG-013" && e.Hp == 1) score += 1000;
            if (id == "LCG-035") score += e.Field.Count(m => !m.IsFrozen) * 2;
            if (id == "LCG-020") score += e.Field.Where(m => m.CurrentPP <= 1325).Sum(BodyValue);
            if (id == "LCG-095") score -= 4;
            if (id == "LCG-047" || id == "LCG-027") score += p.Hand.Any(x => x != c && x.Card.IsMonster && x.Card.TotalCost <= 1) ? 4 : 0;
            return score;
        }
        if (id == "LCG-019" && p.Hp <= 2 || id == "LCG-096" && p.Hp <= 1) return -100;
        var targets = SpellTargets(p, c.Card);
        var removal = targets.Where(e.Field.Contains).ToArray();
        if (removal.Length > 0)
        {
            var score = removal.Max(m => BodyValue(m) + (m.IsTaunt ? 3 : 0));
            if (id == "LCG-024") score *= .55;
            if (id == "LCG-026") score *= .75;
            if (id == "LCG-017" || id == "LCG-123") score += e.Hp == 1 ? 1000 : 4;
            if (id == "LCG-048" || id == "LCG-086" || id == "LCG-004") score += 2;
            if (id == "LCG-088") score -= p.Field.Select(BodyValue).DefaultIfEmpty(20).Min();
            return score - c.Card.TotalCost * .5;
        }
        return id switch
        {
            "LCG-014" => e.Hp == 1 ? 1000 : 4,
            "LCG-006" => e.Hp <= 2 ? 1000 : 8 - p.Field.Select(BodyValue).DefaultIfEmpty(20).Min(),
            "LCG-050" or "LCG-066" or "LCG-070" => p.Hp == 7 ? -10 : (7 - p.Hp) * 2 + (id == "LCG-066" ? 2 : 0),
            "LCG-098" => e.Field.Sum(BodyValue) - p.Field.Sum(BodyValue) - 2,
            "LCG-068" or "LCG-078" => e.Field.Where(m => m.CurrentPP <= 675).Sum(BodyValue) + (id == "LCG-078" && p.Hp < 7 ? 2 : 0) - 2,
            "LCG-092" => e.Field.Where(m => m.CurrentPP <= 825).Sum(BodyValue) - p.Field.Where(m => m.CurrentPP <= 825).Sum(BodyValue) - 2,
            "LCG-008" => e.Field.Where(m => m.CurrentPP <= 500).Sum(BodyValue) - p.Field.Where(m => m.CurrentPP <= 500).Sum(BodyValue) - 2,
            "LCG-038" => e.Field.Where(m => m.Card.TotalCost <= 2).Sum(BodyValue) - p.Field.Where(m => m.Card.TotalCost <= 2).Sum(BodyValue) - 2,
            "LCG-044" => p.TotalEnergy < 6 && p.Deck.Count > 0 ? 5 : -1,
            "LCG-052" => Math.Min(2, p.Hand.Count(x => x.Card.IsMonster && x.Card.TotalCost <= 2)) * 5 - 3,
            "LCG-062" or "LCG-074" => targets.Count > 0 ? 4 + (id == "LCG-074" ? 2 : 0) : -1,
            "LCG-036" => targets.Any(m => m.IsFrozen || m.IsSilenced) ? 5 : -1,
            "LCG-010" => p.Field.Count == 0 ? -5 : p.Hand.Count < 4 ? 6 : 2,
            "LCG-058" or "LCG-090" => p.Graveyard.Any(x => x.Card.IsMonster) ? 4 : -2,
            _ => p.Deck.Count <= 1 ? -10 : p.Hand.Count < 4 ? 5 : 1
        };
    }
    private static MonsterInstance CopyForPlan(MonsterInstance m) => new(m.Card) { InstanceId = m.InstanceId, CurrentPP = m.CurrentPP, CurrentDP = m.CurrentDP,
        HasAttacked = m.HasAttacked, HasSummoningSickness = m.HasSummoningSickness, IsSilenced = m.IsSilenced, IsStealthed = m.IsStealthed,
        FrozenUntilTurn = m.FrozenUntilTurn, ShieldEnergies = new(m.ShieldEnergies), ShieldOwnerId = m.ShieldOwnerId };
    // Bounded public-board search. It scores exchanges/face damage and exposed counterattacks;
    // hidden cards and unresolved death/entry effects are not simulated as known information.
    private AiMove? PlanPublicAttacks(PlayerState p, PlayerState enemy, int depth)
    {
        int budget = AiLevel == 5 ? 1600 : 350;
        double Evaluate(List<MonsterInstance> own, List<MonsterInstance> opp, int hp)
        {
            if (hp <= 0) return 10000;
            var score = own.Sum(BodyValue) - opp.Sum(BodyValue) + (enemy.Hp - hp) * 4;
            if (!own.Any(m => m.IsTaunt && !m.IsStealthed))
            {
                var incoming = opp.Where(m => !m.IsFrozen && !m.Card.CannotAttack).Sum(m => m.CurrentDP);
                score -= incoming >= p.Hp ? 35 : incoming * 1.5;
            }
            return score;
        }
        var mine = p.Field.Select(CopyForPlan).ToList(); var theirs = enemy.Field.Select(CopyForPlan).ToList();
        var baseline = Evaluate(mine, theirs, enemy.Hp); AiMove? best = null; double bestValue = baseline;
        void Search(List<MonsterInstance> own, List<MonsterInstance> opp, int hp, int left, AiMove? first)
        {
            if (--budget < 0) return;
            var score = Evaluate(own, opp, hp);
            if (first != null && score > bestValue) { bestValue = score; best = first with { Score = score - baseline + .2 }; }
            if (left == 0 || hp <= 0) return;
            foreach (var a in own.Where(m => !m.HasAttacked && !m.IsFrozen && (!m.HasSummoningSickness || m.HasCharge) && (m.IsSilenced || !m.Card.CannotAttack)).ToArray())
            {
                var legal = opp.Where(m => !m.IsStealthed).ToList();
                var taunts = legal.Where(m => m.IsTaunt && (a.IsSilenced || a.Card.Id != "LCG-059" || m.CurrentPP > 1325)).ToList();
                var targets = taunts.Count > 0 ? taunts.Cast<MonsterInstance?>().ToList() : legal.Cast<MonsterInstance?>().Append(null).ToList();
                foreach (var d in targets)
                {
                    var outcome = CompareAttack(a, d);
                    var aa = own.Select(CopyForPlan).ToList(); var dd = opp.Select(CopyForPlan).ToList();
                    var ac = aa.First(m => m.InstanceId == a.InstanceId); ac.HasAttacked = true; ac.IsStealthed = false;
                    if (outcome.AttackerDies) aa.Remove(ac); else if (outcome.AttackerShieldBreaks) ac.ShieldEnergies.RemoveAt(0);
                    if (d != null)
                    {
                        var dc = dd.First(m => m.InstanceId == d.InstanceId);
                        if (outcome.DefenderDies) dd.Remove(dc); else if (outcome.DefenderShieldBreaks) dc.ShieldEnergies.RemoveAt(0);
                    }
                    var initial = first ?? new AiMove("attack", 0, Attacker: p.Field.First(m => m.InstanceId == a.InstanceId), Target: d == null ? null : enemy.Field.First(m => m.InstanceId == d.InstanceId));
                    Search(aa, dd, hp - outcome.PlayerDamage, left - 1, initial);
                }
            }
        }
        Search(mine, theirs, enemy.Hp, depth, null); return best;
    }
}
