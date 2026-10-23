using WcgWeb.Models;
namespace WcgWeb.Services;

// 新流派「腐萎侵蝕」（深淵＋中立，WCG-283–291），2026-10-23：用減益把敵方嘲諷縮小，讓中型怪打得贏。
// The debuffs change current PP only; printed-PP removal thresholds (「印刷 PP」) are unaffected.
// Only catalogs from that day contain these ids, so older eras never reach this code.
public partial class GameEngine
{
    internal const string DecayTitle = "選擇本回合 PP -300 的敵方怪物";
    // 289 瘴氣沼澤: each unset copy the opponent controls gives this taunt -300.
    private int BogPower(PlayerState owner, MonsterInstance m) =>
        m.IsTaunt ? -300 * GetOpponent(owner).Structures.Count(x => !x.IsSet && x.Card.Id == "WCG-289") : 0;
    // 286 枯萎獵手: the monster it attacks gets -400 for that combat.
    private static int RotCombatPower(MonsterInstance m, MonsterInstance other, bool attacking) =>
        !attacking && other.IsUnit && !other.IsSilenced && other.Card.Id == "WCG-286" ? -400 : 0;
    // 290 深淵穿顱者: enemy taunts with lower current PP can't block it.
    private static bool PiercesTaunt(MonsterInstance a, MonsterInstance m) =>
        !a.IsSilenced && a.Card.Id == "WCG-290" && m.CurrentPP < a.CurrentPP;
    private bool RotDeploy(PlayerState p, MonsterInstance m)
    {
        var e = GetOpponent(p);
        switch (m.Card.Id)
        {
            case "WCG-285": foreach (var x in e.Field.Where(x => x.IsUnit && x.IsTaunt)) AddTurnBonus(x, -400, m.Card); break;
            case "WCG-291": PickMonster(p, DecayTitle, e.Field.Where(x => x.IsUnit), x => AddTurnBonus(x, -300, m.Card)); break;
            default: return BeaconDeploy(p, m);
        }
        return true;
    }
    private List<MonsterInstance>? RotSpellTargets(PlayerState p, CardDefinition c) =>
        c.Id is "WCG-284" or "WCG-288" ? GetOpponent(p).Field.Where(x => x.IsUnit).ToList() : BeaconSpellTargets(p, c);
    private static bool RotNeedsTarget(string id) => id is "WCG-284" or "WCG-288" || BeaconNeedsTarget(id);
    private bool RotSpell(PlayerState p, CardInstance source, MonsterInstance? t)
    {
        switch (source.Card.Id)
        {
            case "WCG-284": if (t != null && Alive(t)) Attach(p, source, t); break;
            case "WCG-288": if (t != null && Alive(t)) { var taunt = t.IsTaunt; Attach(p, source, t); if (taunt) DrawMany(p, 1); } break;
            default: return BeaconSpell(p, source, t);
        }
        return true;
    }
    private double? RotPlayValue(PlayerState p, CardInstance c)
    {
        var e = GetOpponent(p);
        var ready = p.Field.Any(CanAttack);
        switch (c.Card.Id)
        {
            case "WCG-284": return ready && e.Field.Count > 0 ? 2 + (e.Field.Any(m => m.IsTaunt) ? 1 : 0) : -2;
            case "WCG-288": return e.Field.Count == 0 ? -5 : 1.5 + (e.Field.Any(m => m.IsTaunt) ? 2 : 0);
            case "WCG-289": return e.Field.Any(m => m.IsTaunt) ? 2 : .5;
            case "WCG-285": return e.Field.Count(m => m.IsTaunt) * 400 / 450d;
            case "WCG-291": return e.Field.Count > 0 ? 300 / 450d : 0;
        }
        return BeaconPlayValue(p, c);
    }
    private MonsterInstance? RotTarget(PlayerState p, PendingTarget pending, List<MonsterInstance> legal) =>
        pending.Title == DecayTitle ? legal.OrderByDescending(m => (m.IsTaunt ? 3 : 0) + BodyValue(m)).FirstOrDefault() : BeaconTarget(p, pending, legal);
}
