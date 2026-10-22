using WcgWeb.Models;
namespace WcgWeb.Services;

// 新卡 205–257（設計 v2 第 5 節）。These ids only exist in current catalogs, so older eras never reach this code.
public partial class GameEngine
{
    private static bool IsNew(string id) => string.CompareOrdinal(id, "WCG-205") >= 0 && id.Length == 7;
    private bool Active(MonsterInstance m, string id) => m.IsUnit && !m.IsSilenced && m.Card.Id == id;
    private static bool HasArrows(MonsterInstance m) => m.Card.Arrows.Length > 0;
    // "其他怪物都是 X": every other monster you control has that will (dual cards count). True when there are none.
    private static bool OthersAre(PlayerState p, MonsterInstance self, Func<CardDefinition, bool> rule) => p.Field.Where(x => x != self).All(x => rule(x.Card));
    private int GraveMonsters(PlayerState p) => p.Graveyard.Count(c => c.Card.IsMonster);

    // 磨牌: the top cards of a deck go to its owner's graveyard. Running out only loses when that player must draw.
    private void Mill(PlayerState target, int count)
    {
        var milled = target.Deck.Take(Math.Max(0, count)).ToList(); if (milled.Count == 0) return;
        target.Deck.RemoveRange(0, milled.Count); target.Graveyard.AddRange(milled);
        foreach (var c in milled) Present("discard", target, c.InstanceId, card: c.Card, label: "牌庫頂送入墓地");
        Log($"【{target.Name}】牌庫頂 {milled.Count} 張送入墓地，牌庫剩 {target.Deck.Count} 張。", "action");
    }
    // 205 碎盾狂戰士 removes all 聖盾指示物 (its whole identity); 209 破甲工匠 is a cheap utility body and removes 1.
    private void BreakShield(PlayerState p, bool all = true)
    {
        var counters = ShieldCounterRules;
        PickMonster(p, counters ? "選擇要移除聖盾指示物的敵方怪物" : "選擇本回合失去聖盾的敵方怪物", GetOpponent(p).Field.Where(x => x.HasShield && !x.ShieldSpent), x =>
        {
            if (counters) { x.ShieldCounters = all ? 0 : Math.Max(0, x.ShieldCounters - 1); x.ShieldQualified = x.ShieldCounters > 0; x.ShieldSpent = x.ShieldCounters == 0; }
            else x.ShieldSpent = true;
            Present("status", Owner(x), x.InstanceId, card: x.Card, label: counters ? (all ? "移除所有聖盾指示物" : "移除 1 個聖盾指示物") : "本回合失去聖盾");
        });
    }

    // Continuous board effects, recomputed by RefreshBoard.
    private int NewPower(PlayerState p, MonsterInstance m)
    {
        var bonus = 0;
        if (p.Occupied >= 5) bonus += 300 * p.Field.Count(x => Active(x, "WCG-219"));
        if (m.IsSilenced) return bonus;
        switch (m.Card.Id)
        {
            case "WCG-222": bonus += 500 * m.Attachments.Count; break;
            case "WCG-228": bonus += 300 * Math.Max(0, 5 - p.Occupied); break;
            case "WCG-238": bonus += 200 * p.Field.Count(x => x != m && x.Card.IsNeutral); break;
            case "WCG-262": bonus += 100 * Math.Min(7, p.Hand.Count); break;
            case "WCG-241": if (p.Field.All(x => x.Card.HasWill("狂怒"))) bonus += 400; break;
        }
        return bonus;
    }
    private bool NewTaunt(PlayerState p, MonsterInstance m)
    {
        if (m.IsSilenced) return false;
        return m.Card.Id switch
        {
            "WCG-215" => p.Field.Any(x => x.Slot == m.Slot - 1) && p.Field.Any(x => x.Slot == m.Slot + 1),
            "WCG-224" => p.Hand.Count >= 5,
            _ => false
        };
    }
    private bool NewTrample(PlayerState p, MonsterInstance m) =>
        m.Attachments.Any(a => a.Card.Card.Id == "WCG-220")
        || p.Field.Any(x => x != m && Active(x, "WCG-218") && HasArrows(x) && PointsAt(p, x, p, m));
    // 前鋒盾衛: the enemy monster straight ahead of its up arrow can't attack.
    private bool Pinned(MonsterInstance m)
    {
        if (!Alive(m)) return false; var owner = Owner(m);
        return GetOpponent(owner).Field.Any(x => Active(x, "WCG-216") && HasArrows(x) && PointsAt(GetOpponent(owner), x, owner, m));
    }
    // 虛空隱士: while it is your only monster, spells can't target it.
    // After the 理智 buff: enemy spells can never target it.
    private bool Hermit(PlayerState caster, MonsterInstance m) => Alive(m) && Active(m, "WCG-229") && (ReasonBuff ? Owner(m) != caster : Owner(m).Field.Count == 1);
    private int AttackDpBonus(MonsterInstance a) => !SimplifiedRules || !a.IsUnit ? 0 : (FixedAttackBonus(a) ? 1 : 0) + a.Attachments.Count(x => x.Card.Card.Id == "WCG-221");
    private static int NewAttachmentPower(string id) => id == "WCG-221" ? 300 : 0;
    private int NewCostReduction(PlayerState p, CardDefinition card) => card.Id == "WCG-234" ? Math.Min(card.TotalCost - 2, GraveMonsters(p) / 3) : 0;

    private List<MonsterInstance>? NewSpellTargets(PlayerState p, CardDefinition c)
    {
        var enemy = GetOpponent(p).Field;
        return c.Id switch
        {
            "WCG-206" => enemy.Where(m => m.IsTaunt && !m.IsSilenced).ToList(),
            "WCG-208" when c.IsSpell => enemy.Where(m => !m.IsSilenced).ToList(),
            "WCG-207" => enemy.Where(m => m.IsTaunt && ThresholdPP(m) >= 1800).ToList(),
            "WCG-220" or "WCG-221" => p.Field.ToList(),
            "WCG-230" => enemy.Count > 0 ? p.Field.Where(m => !m.IsTapped).ToList() : [],
            "WCG-246" => enemy.Where(m => ThresholdPP(m) <= Low(1100)).ToList(),
            "WCG-248" => p.Field.Where(m => m.Card.HasWill("生機") && enemy.Any(x => x.CurrentPP < m.CurrentPP)).ToList(),
            _ => null
        };
    }
    private static bool NewNeedsTarget(string id) => id is "WCG-206" or "WCG-207" or "WCG-220" or "WCG-221" or "WCG-230" or "WCG-246" or "WCG-248";
    private string NewPlayProblem(PlayerState p, CardDefinition c) => c.Id switch
    {
        "WCG-233" when p.Field.Count == 0 => "需要犧牲 1 隻己方怪物。",
        "WCG-207" when p.Hp < 1 => "生命不足以支付。",
        _ => ""
    };
    private MonsterInstance? lastSacrifice;

    private bool NewDeploy(PlayerState p, MonsterInstance m)
    {
        var e = GetOpponent(p);
        switch (m.Card.Id)
        {
            case "WCG-205": BreakShield(p); break; case "WCG-209": BreakShield(p, !ShieldCounterRules); break;
            // Before the silence-spell change (2026-10-16) 寂靜學者 was a monster; old-era matches keep its entry effect.
            case "WCG-208": PickMonster(p, "選擇要沉默的敵方怪物", e.Field.Where(x => !x.IsSilenced), x => { x.IsSilenced = true; RefreshBoard(); Present("status", e, x.InstanceId, card: x.Card, label: "沉默"); }); break;
            case "WCG-212": Mill(e, 2); break;
            case "WCG-227": if (p.Hand.Count > e.Hand.Count) DrawMany(p, 1); break;
            case "WCG-236": if (OthersAre(p, m, c => c.IsNeutral)) DrawMany(p, 1); break;
            case "WCG-245": if (OthersAre(p, m, c => c.HasWill("生機"))) DrawMany(p, 1); break;
            case "WCG-251" when ReasonBuff: DrawMany(p, 1); break;
            case "WCG-251": if (OthersAre(p, m, c => c.HasWill("理智"))) DrawMany(p, 1); break;
            default: return false;
        }
        return true;
    }
    private bool NewSpell(PlayerState p, CardInstance source, MonsterInstance? t)
    {
        var e = GetOpponent(p); var c = source.Card;
        bool Valid() => t != null && Alive(t) && SpellTargets(p, c).Contains(t);
        switch (c.Id)
        {
            case "WCG-206": case "WCG-208": if (Valid()) Silence(p, source, t!); break;
            case "WCG-207": case "WCG-246": if (Valid()) KillBatch([t!], c.Name); break;
            case "WCG-211": Mill(e, 3); break;
            case "WCG-214": Mill(e, Math.Min(8, GraveMonsters(p))); break;
            case "WCG-217": DrawMany(p, Math.Min(3, p.Field.Count(HasArrows))); break;
            case "WCG-220": case "WCG-221": if (t != null && Alive(t)) Attach(p, source, t); break;
            case "WCG-230":
                if (t == null || !Alive(t) || t.IsTapped) break;
                t.IsTapped = true; Present("status", p, t.InstanceId, card: t.Card, label: "橫置");
                PickMonster(p, "選擇與它交戰的敵方怪物", e.Field, d => { if (Alive(t)) ResolveAttack(p, t, d); });
                break;
            case "WCG-233":
                var nature = lastSacrifice?.Card.HasWill("生機") == true; lastSacrifice = null;
                Resolve(() => DrawMany(p, 1), () => { if (nature) Heal(p, 1); }); break;
            case "WCG-237": Search(p, 3, x => x.Card.IsMonster && x.Card.IsNeutral); break;
            case "WCG-242": Resolve(() => DrawMany(p, 2), () => PayLife(p, 1)); break;
            case "WCG-248":
                if (t == null || !Alive(t)) break;
                PickMonster(p, $"選擇 PP 比【{t.Card.Name}】低的敵方怪物", e.Field.Where(x => x.CurrentPP < t.CurrentPP), x => KillBatch([x], c.Name));
                break;
            case "WCG-250": DrawMany(p, GraveMonsters(p) >= 5 ? 2 : 1); break;
            default: return false;
        }
        return true;
    }
    private void NewDeath(PlayerState p, MonsterInstance m, CardInstance grave)
    {
        var e = GetOpponent(p);
        void Badge() => Present("trigger", p, m.InstanceId, card: m.Card, label: "陣亡能力");
        switch (m.Card.Id)
        {
            case "WCG-223":
                var mine = m.LostAttachments.Where(p.Graveyard.Contains).ToArray();
                if (mine.Length > 0) { Badge(); CardChoice(p, "選擇附著過的卡加入手牌", mine, c => { if (p.Graveyard.Remove(c)) p.Hand.Add(c); }); }
                break;
            case "WCG-232": Badge(); TopEnergy(p); break;
            case "WCG-235": Badge(); FreeFaction(p, 1, "生機", true); break;
            case "WCG-239": case "WCG-243": Badge(); Damage(e, 1, new(p, m.Card, m, "death")); break;
            case "WCG-249": Badge(); CardChoice(p, "選擇墓地的另 1 隻深淵怪物加入手牌", p.Graveyard.Where(c => c != grave && c.Card.IsMonster && c.Card.HasWill("深淵")), c => { if (p.Graveyard.Remove(c)) { p.Hand.Add(c); Present("recover", p, c.InstanceId, card: c.Card, label: "墓地回收"); } }); break;
        }
    }
    private void NewEndTurn(PlayerState p, List<Action> effects)
    {
        foreach (var m in p.Field.Where(x => Active(x, "WCG-213")).ToArray())
            effects.Add(() => { if (Alive(m) && !m.IsSilenced && !m.IsTapped) Mill(GetOpponent(p), 1); });
        foreach (var s in p.Structures.Where(x => !x.IsSet && x.Card.Id == "WCG-244").ToArray())
            effects.Add(() => { if (p.Structures.Contains(s) && p.Field.Count > 0 && p.Field.All(x => x.Card.HasWill("狂怒"))) Damage(GetOpponent(p), 1, new(p, s.Card, s, "passive")); });
    }
    private void NewStartTurn(PlayerState p)
    {
        foreach (var s in p.Structures.Where(x => !x.IsSet).ToArray())
        {
            if (s.Card.Id == "WCG-231" && AltarReady(p)) _effects.AddLast(() => { if (p.Structures.Contains(s) && AltarReady(p)) DrawMany(p, 1); });
            if (s.Card.Id == "WCG-247" && p.Field.Count(x => x.Card.HasWill("生機")) >= 3) _effects.AddLast(() => { if (p.Structures.Contains(s) && p.Field.Count(x => x.Card.HasWill("生機")) >= 3) DrawMany(p, 1); });
        }
    }
    private void NewSpellCast(PlayerState p)
    {
        foreach (var m in p.Field.Where(x => Active(x, "WCG-252"))) m.TurnBonus += 300;
    }
}
