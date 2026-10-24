using WcgWeb.Models;

namespace WcgWeb.Services;

// PP/DP breakdown for the enlarged card view and the 「顯示原始數值與加成」 setting.
// Display only: it reads state and never changes it, so rules results, the AI and replays are unaffected.
// Every term mirrors the Power lambda in RefreshBoard (GameEngine.V06.cs); tests check the sum matches CurrentPP.
public partial class GameEngine
{
    private static void AddTurnBonus(MonsterInstance m, int amount, CardDefinition source)
    {
        // TurnBonus returns to 0 at every turn change, so a fresh bonus starts a fresh source list.
        if (m.TurnBonus == 0) m.TurnBonusSources.Clear();
        m.TurnBonus += amount; m.TurnBonusSources.Add(new(source, source.Name, amount));
    }
    private static void AddNextCombatBonus(MonsterInstance m, int amount, CardDefinition source)
    {
        if (m.NextCombatBonus == 0) m.NextCombatSources.Clear();
        m.NextCombatBonus += amount; m.NextCombatSources.Add(new(source, source.Name, amount));
    }

    // Modifiers on PP, one entry per source (same source summed), zero entries left out. Printed PP is m.Card.PP.
    public IReadOnlyList<StatSource> PowerModifiers(MonsterInstance m)
    {
        var list = new List<StatSource>();
        var owner = Owner(m);
        if (m.IsSet) return list;
        void Add(CardDefinition? source, string label, int amount) { if (amount != 0) list.Add(new(source, label, amount)); }
        var basePP = m.BasePP - (m.Card.PP ?? 0);
        Add(null, "其他", basePP);
        foreach (var a in m.Attachments) Add(a.Card.Card, a.Card.Card.Name, AttachmentPower(a.Card.Card.Id) + NewAttachmentPower(a.Card.Card.Id));
        Tracked(m.NextCombatSources, m.NextCombatBonus, "下次戰鬥加成");
        Tracked(m.TurnBonusSources, m.TurnBonus, "本回合加成");
        Add(null, "戰鬥加成", m.CombatPowerBonus);
        if (!m.IsSilenced && m.Card.Id == "WCG-009") Add(m.Card, m.Card.Name, GetOpponent(owner).Field.Count * 200);
        if (!m.IsSilenced && m.Card.Id == "WCG-182") Add(m.Card, m.Card.Name, owner.Field.Count(x => x != m && x.Card.HasWill("狂怒")) * 300);
        foreach (var source in Player.Field.Concat(Computer.Field).Where(source => source != m && !source.IsSilenced && source.Card.Arrows.Length > 0 && ArrowAura(source.Card.Id) != (0, 0)))
        {
            var sourceOwner = Owner(source); if (!PointsAt(sourceOwner, source, owner, m)) continue;
            var aura = ArrowAura(source.Card.Id); Add(source.Card, source.Card.Name, Across(source, m) ? aura.Enemy : aura.Ally);
        }
        foreach (var (source, amount) in ArchArrowSources(owner, m)) Add(source, source.Name, amount);
        foreach (var bog in GetOpponent(owner).Structures.Where(x => !x.IsSet && x.Card.Id == "WCG-289")) if (m.IsTaunt) Add(bog.Card, bog.Card.Name, -300);
        if (owner.Occupied >= 5) foreach (var x in owner.Field.Where(x => Active(x, "WCG-219"))) Add(x.Card, x.Card.Name, 300);
        // Self abilities of NewPower: the remainder after the shared 219 aura.
        foreach (var (source, amount) in BeaconPowerSources(owner, m)) Add(source, source.Name, amount);
        var self = NewPower(owner, m) - (owner.Occupied >= 5 ? 300 * owner.Field.Count(x => Active(x, "WCG-219")) : 0) - BeaconPower(owner, m);
        Add(m.Card, m.Card.Name, self);
        // One line per card name: repeated buffs from the same card (or copies of it) are summed.
        return list.GroupBy(x => (x.Source?.Id, x.Label)).Select(g => new StatSource(g.First().Source, g.Key.Label, g.Sum(x => x.Amount)))
            .Where(x => x.Amount != 0).ToList();

        void Tracked(List<StatSource> sources, int total, string fallback)
        {
            if (total == 0) return;
            var known = sources.Sum(x => x.Amount);
            if (known == total) { foreach (var s in sources) Add(s.Source, s.Label, s.Amount); }
            else Add(null, fallback, total);
        }
    }
    // Modifiers on DP (printed DP is m.Card.DP ?? 1).
    public IReadOnlyList<StatSource> DamageModifiers(MonsterInstance m)
    {
        var list = new List<StatSource>();
        if (m.IsSet) return list;
        var baseDp = m.BaseDP - (m.Card.DP ?? 1);
        if (baseDp != 0) list.Add(new(null, "其他", baseDp));
        if (m.DpCounters != 0) list.Add(new(null, "DP 指示物", m.DpCounters));
        foreach (var a in m.Attachments.Where(a => a.Card.Card.Id == "WCG-338")) list.Add(new(a.Card.Card, a.Card.Card.Name, 1));
        foreach (var s in Player.Field.Concat(Computer.Field).Where(s => s != m && Active(s, "WCG-357") && PointsAt(Owner(s), s, Owner(m), m))) list.Add(new(s.Card, s.Card.Name, 1));
        if (BloodDp(Owner(m), m) is var blood && blood != 0) list.Add(new(m.Card, m.Card.Name, blood));
        if (m.DpBonus - m.Attachments.Count(a => a.Card.Card.Id == "WCG-338") - RootAndBloodDp(Owner(m), m) is var other && other != 0) list.Add(new(null, "其他", other));
        if (m.TurnDpBonus != 0) list.Add(new(null, "本回合加成", m.TurnDpBonus));
        if (m.AttackDamageBonus != 0) list.Add(new(null, "攻擊加成", m.AttackDamageBonus));
        return list;
    }
}
