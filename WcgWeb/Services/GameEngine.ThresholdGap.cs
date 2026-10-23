using WcgWeb.Models;
namespace WcgWeb.Services;

// 新流派「翠語夾縫」（理智＋生機，WCG-273–281）與反制解牌 夾縫狙擊（中立，WCG-282），2026-10-23。
// The monsters print PP 1300–1700, between the 印刷 PP ≤1200 and ≥1800 removal thresholds, and grow in play.
// Only catalogs from that day contain these ids, so older eras never reach this code.
public partial class GameEngine
{
    internal static bool InPrintedGap(MonsterInstance m) => m.Card.PP is >= 1300 and <= 1700;
    private int GapPower(PlayerState p, MonsterInstance m) => m.Card.Id switch
    {
        "WCG-273" => 100 * Math.Min(5, p.Hand.Count),
        "WCG-275" => 200 * p.Field.Count(x => x != m),
        "WCG-276" => p.Hand.Count >= 4 ? 400 : 0,
        "WCG-279" => p.Hand.Count >= 5 ? 500 : 0,
        "WCG-281" => 200 * p.Field.Count(x => x != m && x.IsUnit && InPrintedGap(x)),
        _ => 0
    };
    // Combat-only modifiers added by later archetypes; 274's own +300 is in CombatPower.
    private int ArchetypeCombatPower(MonsterInstance m, MonsterInstance other, bool attacking) => RotCombatPower(m, other, attacking);
    private bool GapDeploy(PlayerState p, MonsterInstance m)
    {
        switch (m.Card.Id)
        {
            case "WCG-278": DrawMany(p, 1); break;
            default: return RotDeploy(p, m);
        }
        return true;
    }
    private List<MonsterInstance>? GapSpellTargets(PlayerState p, CardDefinition c)
    {
        var enemy = GetOpponent(p).Field;
        return c.Id switch
        {
            "WCG-277" => p.Field.Concat(enemy).ToList(),
            "WCG-282" => enemy.Where(InPrintedGap).ToList(),
            _ => RotSpellTargets(p, c)
        };
    }
    private static bool GapNeedsTarget(string id) => id is "WCG-277" or "WCG-282" || RotNeedsTarget(id);
    private bool GapSpell(PlayerState p, CardInstance source, MonsterInstance? t)
    {
        switch (source.Card.Id)
        {
            case "WCG-277": if (t != null && Alive(t)) Attach(p, source, t); break;
            case "WCG-280": var more = p.Field.Any(m => m.IsUnit && (m.Card.PP ?? 0) >= 1300); DrawMany(p, more ? 2 : 1); break;
            case "WCG-282": if (t != null && Alive(t) && InPrintedGap(t)) KillBatch([t], source.Card.Name); break;
            default: return RotSpell(p, source, t);
        }
        return true;
    }
    private double? GapPlayValue(PlayerState p, CardInstance c)
    {
        var e = GetOpponent(p);
        double Draw(int n) => p.Deck.Count <= n + 2 ? -10 : n * 2.2;
        switch (c.Card.Id)
        {
            case "WCG-277": return p.Field.Any(CanAttack) && e.Field.Count > 0 ? 2 : -2;
            case "WCG-280": return Draw(p.Field.Any(m => (m.Card.PP ?? 0) >= 1300) ? 2 : 1) - 1;
            case "WCG-273": return Math.Min(5, p.Hand.Count - 1) * 100 / 450d;
            case "WCG-275": return p.Field.Count * 200 / 450d;
            case "WCG-276": return p.Hand.Count - 1 >= 4 ? 400 / 450d : 0;
            case "WCG-279": return p.Hand.Count - 1 >= 5 ? 500 / 450d : 0;
            case "WCG-281": return p.Field.Count(InPrintedGap) * 200 / 450d;
            case "WCG-278": return Draw(1);
        }
        return RotPlayValue(p, c);
    }
}
