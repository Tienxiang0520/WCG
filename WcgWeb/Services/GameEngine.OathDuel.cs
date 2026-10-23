using WcgWeb.Models;
namespace WcgWeb.Services;

// 新流派「誓劍決鬥」（秩序，WCG-264–272，2026-10-23）。These ids only exist from that catalog on, so older eras never reach this code.
// 決鬥 = the monster fights the chosen enemy monster through ResolveAttack: taunt can't stop it, it does not need to be
// upright and (unless the text says so) it is not tapped. With 聖盾交戰必扣 the duelist spends 1 counter per duel but survives it.
public partial class GameEngine
{
    internal const string DuelTitle = "選擇決鬥對象";
    internal const string ShieldGiftTitle = "選擇獲得聖盾指示物的己方怪物";
    internal const string ArenaFighterTitle = "選擇橫置並決鬥的己方怪物";
    internal const string ArenaTauntTitle = "選擇決鬥的敵方嘲諷怪物";

    private void GainShieldCounter(MonsterInstance m, CardDefinition source)
    {
        if (!CanShield(m)) return;
        m.ShieldCounters++; m.ShieldQualified = true; m.ShieldSpent = false;
        Present("status", Owner(m), m.InstanceId, card: m.Card, label: "獲得 1 個聖盾指示物");
        Log($"【{m.Card.Name}】因【{source.Name}】獲得 1 個聖盾指示物。", "action");
    }
    private void Duel(PlayerState p, MonsterInstance m, MonsterInstance d)
    {
        if (!Alive(m) || !Alive(d) || Owner(d) == p) return;
        Present("effect", p, m.InstanceId, d.InstanceId, card: m.Card, label: "決鬥");
        Log($"【{m.Card.Name}】與【{d.Card.Name}】決鬥。", "action");
        ResolveAttack(p, m, d);
    }
    // "你可以令此怪物立刻與 1 隻敵方怪物交戰": an optional pick, skipped with SKIP.
    private void OfferDuel(PlayerState p, MonsterInstance m, IEnumerable<MonsterInstance> candidates, Action? after = null)
    {
        var list = candidates.Where(Alive).ToArray();
        if (!Alive(m) || list.Length == 0) { after?.Invoke(); return; }
        Ask(p, DuelTitle, list.Select(d => new ChoiceOption { Id = d.InstanceId.ToString(), Title = d.Card.Name, PreviewCard = d.Card, Subtitle = $"PP {d.CurrentPP}" })
            .Append(new() { Id = "SKIP", Title = "不決鬥" }), o =>
        {
            var d = o.Id == "SKIP" ? null : list.FirstOrDefault(x => x.InstanceId.ToString() == o.Id);
            if (d != null) Resolve(() => Duel(p, m, d), () => after?.Invoke()); else after?.Invoke();
        }, sourceInstanceId: m.InstanceId);
        CurrentPendingChoice!.SourceCard = m.Card;
    }

    private bool OathDeploy(PlayerState p, MonsterInstance m)
    {
        var e = GetOpponent(p);
        switch (m.Card.Id)
        {
            case "WCG-264": PickMonster(p, ShieldGiftTitle, p.Field.Where(CanShield), x => GainShieldCounter(x, m.Card)); break;
            case "WCG-266": case "WCG-269": OfferDuel(p, m, e.Field); break;
            case "WCG-272": OfferDuel(p, m, e.Field.Where(x => x.IsTaunt), () => { if (Alive(m) && !m.IsSilenced) OfferDuel(p, m, e.Field.Where(x => x.IsTaunt)); }); break;
            default: return false;
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
            _ => null
        };
    }
    private static bool OathNeedsTarget(string id) => id is "WCG-267" or "WCG-271";
    private bool OathSpell(PlayerState p, CardInstance source, MonsterInstance? t)
    {
        var e = GetOpponent(p);
        switch (source.Card.Id)
        {
            case "WCG-267":
                if (t == null || !Alive(t) || t.IsTapped) break;
                GainShieldCounter(t, source.Card);
                t.IsTapped = true; Present("status", p, t.InstanceId, card: t.Card, label: "橫置");
                PickMonster(p, DuelTitle, e.Field, d => Duel(p, t, d));
                break;
            case "WCG-271": if (t != null && Alive(t)) Attach(p, source, t); break;
            default: return false;
        }
        return true;
    }
    private bool CanActivateOath(PlayerState p, MonsterInstance s) => s.Card.Id == "WCG-268" && p.Field.Any(m => !m.IsTapped) && GetOpponent(p).Field.Any(m => m.IsTaunt);
    private void ActivateOath(PlayerState p, MonsterInstance s)
    {
        var e = GetOpponent(p);
        PickMonster(p, ArenaFighterTitle, p.Field.Where(m => !m.IsTapped), f => PickMonster(p, ArenaTauntTitle, e.Field.Where(x => x.IsTaunt), d =>
        {
            if (!Alive(f) || f.IsTapped) return;
            s.IsTapped = true; f.IsTapped = true; Present("status", p, f.InstanceId, card: f.Card, label: "橫置");
            Duel(p, f, d);
        }));
    }
    // 誓約持旗官: another monster of yours that destroyed an enemy monster in combat and is still on the field gains 1 counter;
    // each 持旗官 does this once per turn (TriggersThisTurn resets at every turn change).
    private void OathCombatWon(PlayerState p, MonsterInstance m, MonsterInstance other)
    {
        if (!Alive(m) || Alive(other) || !p.Field.Contains(m)) return;
        var flag = p.Field.FirstOrDefault(x => x != m && Active(x, "WCG-270") && x.TriggersThisTurn == 0);
        if (flag == null || !CanShield(m)) return;
        flag.TriggersThisTurn = 1;
        Present("trigger", p, flag.InstanceId, card: flag.Card, label: "誓約持旗官");
        GainShieldCounter(m, flag.Card);
    }
}
