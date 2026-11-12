using WcgWeb.Models;
namespace WcgWeb.Services;

// 新流派「誓劍決鬥」（秩序，WCG-264–272，2026-10-23）。These ids only exist from that catalog on, so older eras never reach this code.
// 決鬥 = the monster fights the chosen enemy monster through ResolveAttack: taunt can't stop it, it does not need to be
// upright and (unless the text says so) it is not tapped. Both monsters count as attacked, so each spends a 聖盾 source if it has one.
public partial class GameEngine
{
    internal const string DuelTitle = "選擇決鬥對象";
    internal const string ShieldGiftTitle = "決鬥見證官：選擇底下放 1 張手牌、獲得聖盾的己方怪物";
    internal const string ArenaFighterTitle = "選擇橫置並決鬥的己方怪物";
    internal const string ArenaTauntTitle = "選擇決鬥的敵方嘲諷怪物";
    internal const string ArenaFoeTitle = "選擇決鬥的敵方怪物";
    // 決鬥競技場 only targeted taunts before 2026-10-23; older catalogs keep that wording and rule.
    private static bool ArenaTauntOnly(MonsterInstance s) => ArenaTauntOnly(s.Card.Text);
    private static bool ArenaTauntOnly(string text) => text.Contains("嘲諷怪物", StringComparison.Ordinal);

    // protect: "此次交戰此怪物不會被消滅" (269, 272's first duel).
    private void Duel(PlayerState p, MonsterInstance m, MonsterInstance d, bool protect = false)
    {
        if (!Alive(m) || !Alive(d) || Owner(d) == p) return;
        Present("effect", p, m.InstanceId, d.InstanceId, card: m.Card, label: "決鬥");
        Log($"【{m.Card.Name}】與【{d.Card.Name}】決鬥" + (protect ? "（此次交戰不會被消滅）。" : "。"), "action");
        if (!protect) { EffectCombat(p, m, d); return; }
        m.CombatProtected = true;
        Resolve(() => EffectCombat(p, m, d), () => m.CombatProtected = false);
    }
    // Combats started by an effect (duels, 決鬥宣言) present as an ordinary attack so the client plays the shared lunge.
    // Presentation only: the rules still go straight to ResolveAttack (no tap, no attack triggers, LastAttack unchanged).
    private void EffectCombat(PlayerState p, MonsterInstance a, MonsterInstance d)
    {
        Present("attack", p, a.InstanceId, d.InstanceId, card: a.Card, label: "交戰");
        ResolveAttack(p, a, d, effect: true);
    }
    // "你可以令此怪物立刻與 1 隻敵方怪物交戰": an optional pick, skipped with SKIP.
    private void OfferDuel(PlayerState p, MonsterInstance m, IEnumerable<MonsterInstance> candidates, Action? after = null, bool protect = false)
    {
        var list = candidates.Where(Alive).ToArray();
        if (!Alive(m) || list.Length == 0) { after?.Invoke(); return; }
        Ask(p, DuelTitle, list.Select(d => new ChoiceOption { Id = d.InstanceId.ToString(), Title = d.Card.Name, PreviewCard = d.Card, Subtitle = $"PP {d.CurrentPP}" })
            .Append(new() { Id = "SKIP", Title = "不決鬥" }), o =>
        {
            var d = o.Id == "SKIP" ? null : list.FirstOrDefault(x => x.InstanceId.ToString() == o.Id);
            if (d != null) Resolve(() => Duel(p, m, d, protect), () => after?.Invoke()); else after?.Invoke();
        }, sourceInstanceId: m.InstanceId);
        CurrentPendingChoice!.SourceCard = m.Card;
    }

    private bool OathDeploy(PlayerState p, MonsterInstance m)
    {
        var e = GetOpponent(p);
        switch (m.Card.Id)
        {
            case "WCG-264":
                if (p.Hand.Count > 0) OptionalTarget(p, ShieldGiftTitle, p.Field.Where(x => x.IsUnit && CanShield(x) && !Arbitrated(p, x)), x => OfferShieldCard(p, x, m.Card));
                break;
            case "WCG-266": OfferDuel(p, m, e.Field); break;
            case "WCG-269": OfferDuel(p, m, e.Field, protect: true); break;
            case "WCG-272": OfferDuel(p, m, e.Field.Where(x => x.IsTaunt), () => { if (Alive(m) && !m.IsSilenced) OfferDuel(p, m, e.Field.Where(x => x.IsTaunt)); }, protect: true); break;
            default: return GapDeploy(p, m);
        }
        return true;
    }
    private List<MonsterInstance>? OathSpellTargets(PlayerState p, CardDefinition c)
    {
        var enemy = GetOpponent(p).Field;
        return c.Id switch
        {
            "WCG-267" => enemy.Count > 0 ? p.Field.Where(m => !m.IsTapped).ToList() : [],
            "WCG-271" => p.Field.Concat(enemy).ToList(),
            _ => GapSpellTargets(p, c)
        };
    }
    private static bool OathNeedsTarget(string id) => id is "WCG-267" or "WCG-271" || GapNeedsTarget(id);
    private bool OathSpell(PlayerState p, CardInstance source, MonsterInstance? t)
    {
        var e = GetOpponent(p);
        switch (source.Card.Id)
        {
            case "WCG-267":
                if (t == null || !Alive(t) || t.IsTapped) break;
                if (CanShield(t) && !Arbitrated(p, t)) Attach(p, source, t);
                t.IsTapped = true; Present("status", p, t.InstanceId, card: t.Card, label: "橫置");
                PickMonster(p, DuelTitle, SpellResolvePickable(p, e.Field), d => Duel(p, t, d));
                break;
            case "WCG-271": if (t != null && Alive(t)) Attach(p, source, t); break;
            default: return GapSpell(p, source, t);
        }
        return true;
    }
    private bool CanActivateOath(PlayerState p, MonsterInstance s) => s.Card.Id == "WCG-268" && p.Field.Any(m => !m.IsTapped) && GetOpponent(p).Field.Any(m => !ArenaTauntOnly(s) || m.IsTaunt);
    private void ActivateOath(PlayerState p, MonsterInstance s)
    {
        var e = GetOpponent(p);
        PickMonster(p, ArenaFighterTitle, p.Field.Where(m => !m.IsTapped), f => PickMonster(p, ArenaTauntOnly(s) ? ArenaTauntTitle : ArenaFoeTitle, e.Field.Where(x => !ArenaTauntOnly(s) || x.IsTaunt), d =>
        {
            if (!Alive(f) || f.IsTapped) return;
            s.IsTapped = true; f.IsTapped = true; Present("status", p, f.InstanceId, card: f.Card, label: "橫置");
            Duel(p, f, d);
        }));
    }
    // 誓約持旗官: you may put a hand card face up under another monster of yours that destroyed an enemy monster in combat and is still on the field;
    // each 持旗官 does this once per turn (TriggersThisTurn resets at every turn change).
    private void OathCombatWon(PlayerState p, MonsterInstance m, MonsterInstance other)
    {
        if (!Alive(m) || Alive(other) || !p.Field.Contains(m)) return;
        var flag = p.Field.FirstOrDefault(x => x != m && Active(x, "WCG-270") && x.TriggersThisTurn == 0);
        if (flag == null || !CanShield(m) || Arbitrated(p, m) || p.Hand.Count == 0) return;
        flag.TriggersThisTurn = 1;
        Present("trigger", p, flag.InstanceId, card: flag.Card, label: "誓約持旗官");
        OfferShieldCard(p, m, flag.Card);
    }
}
