using WcgWeb.Models;
namespace WcgWeb.Services;

// 新流派「怒濤連擊」（狂怒＋中立，WCG-391–399，2026-10-25）：攻擊完轉回直立再打，一隻怪一回合打好幾次。
// Everything lasts only this turn (attack counts reset at every turn change), so no counter is needed.
// A readied monster still can't attack while 誓言枷鎖 (142) or another lock applies. Only catalogs from that day
// contain these ids, so older eras never reach this code.
public partial class GameEngine
{
    internal const string SurgeReadyTitle = "選擇轉為直立的己方怪物";
    internal const string SurgeForgeTitle = "不歇鍛爐：選擇轉為直立的己方已橫置怪物";

    private void SurgeReady(PlayerState p, MonsterInstance m, CardDefinition source)
    {
        if (!Alive(m) || !m.IsTapped) return;
        ReadyMonster(m);
        Present("status", p, m.InstanceId, card: source, label: "轉為直立");
        Log($"【{m.Card.Name}】轉為直立（{source.Name}）。", "action");
    }
    // Called once an attack is over (the attacker has just been tapped). d is the monster it attacked (null = the player).
    private void SurgeAfterAttack(PlayerState p, MonsterInstance a, MonsterInstance? d)
    {
        if (!Alive(a) || !a.IsUnit) return;
        a.SurgeAttacks++;
        RefreshBoard();
        // 393 戰鼓傳令: the monsters its ← → arrows point at get +200 PP after they attack.
        foreach (var s in Player.Field.Concat(Computer.Field).Where(s => s != a && Active(s, "WCG-393") && PointsAt(Owner(s), s, p, a)).ToArray())
            AddTurnBonus(a, 200, s.Card);
        CardDefinition? by = null;
        if (Active(a, "WCG-391") && a.SurgeAttacks == 1) by = a.Card;
        if (Active(a, "WCG-394") && d != null && !Alive(d) && a.SurgeReadies < 2) { a.SurgeReadies++; by = a.Card; }
        if (Active(a, "WCG-399") && a.SurgeReadies < 3) { a.SurgeReadies++; by = a.Card; }
        // 398 戰場號角: on your turn, the first of your monsters still on the field after attacking (once per turn per horn).
        if (ReferenceEquals(p, ActivePlayer))
            foreach (var horn in p.Field.Where(x => Active(x, "WCG-398") && !x.SurgeHornUsed).ToArray()) { horn.SurgeHornUsed = true; by ??= horn.Card; }
        if (by != null) SurgeReady(p, a, by);
    }
    // 397 血浪劍豪: +400 PP for each attack it has made this turn.
    private static int SurgePower(PlayerState p, MonsterInstance m) => m.Card.Id == "WCG-397" ? 400 * m.SurgeAttacks : 0;

    private List<MonsterInstance>? SurgeSpellTargets(PlayerState p, CardDefinition c) => c.Id == "WCG-392" ? p.Field.Where(x => x.IsUnit).ToList() : null;
    private static bool SurgeNeedsTarget(string id) => id == "WCG-392";
    private bool SurgeSpell(PlayerState p, CardInstance source, MonsterInstance? t)
    {
        switch (source.Card.Id)
        {
            case "WCG-392":
                if (t == null || !Alive(t) || !p.Field.Contains(t)) break;
                if (t.IsTapped) SurgeReady(p, t, source.Card);
                AddTurnBonus(t, 300, source.Card);
                break;
            case "WCG-396":
                foreach (var x in p.Field.Where(x => x.IsUnit && x.IsTapped).ToArray()) { SurgeReady(p, x, source.Card); x.SurgeNoFace = true; }
                break;
            default: return SpringSpell(p, source, t);
        }
        RefreshBoard();
        return true;
    }
    private string SurgePlayProblem(PlayerState p, CardDefinition c) => c.Id switch
    {
        "WCG-392" when !p.Field.Any(x => x.IsUnit) => "需要 1 隻己方怪物。",
        "WCG-396" when !p.Field.Any(x => x.IsUnit && x.IsTapped) => "你沒有已橫置的怪物。",
        _ => ""
    };
    // 395 不歇鍛爐: tap it to ready 1 of your tapped monsters.
    private static bool SurgeCanActivate(PlayerState p, MonsterInstance s) => s.Card.Id == "WCG-395" && p.Field.Any(x => x.IsUnit && x.IsTapped) || SpringCanActivate(p, s);
    private void SurgeActivate(PlayerState p, MonsterInstance s)
    {
        if (s.Card.Id != "WCG-395") { SpringActivate(p, s); return; }
        PickMonster(p, SurgeForgeTitle, p.Field.Where(x => x.IsUnit && x.IsTapped), x => { s.IsTapped = true; SurgeReady(p, x, s.Card); });
    }

    // ── 天梯電腦 ──
    private double? SurgePlayValue(PlayerState p, CardInstance c)
    {
        var e = GetOpponent(p);
        var spent = p.Field.Where(x => x.IsUnit && x.IsTapped && x.SurgeAttacks > 0 && !x.AttackLocked).ToList();
        switch (c.Card.Id)
        {
            case "WCG-392":
                if (spent.Count == 0) return -3;
                var best = spent.Max(x => x.CurrentDP);
                return best >= e.Hp && !e.Field.Any(x => x.IsTaunt) ? 1000 : 2 + best;
            case "WCG-396":
                var foes = e.Field.Count(x => x.IsUnit);
                return spent.Count == 0 || foes == 0 ? -5 : Math.Min(spent.Count, foes) * 1.8 - 1;
        }
        return SpringPlayValue(p, c);
    }
    private MonsterInstance? SurgeSpellTarget(PlayerState p, string id, IReadOnlyList<MonsterInstance> own) => id switch
    {
        "WCG-392" => own.Where(x => x.IsTapped && !x.AttackLocked).OrderByDescending(x => x.CurrentDP * 1000 + x.CurrentPP).FirstOrDefault() ?? own.OrderByDescending(x => x.CurrentPP).FirstOrDefault(),
        _ => null
    };
    private static MonsterInstance? SurgeTarget(PlayerState p, PendingTarget pending, List<MonsterInstance> legal) => pending.Title switch
    {
        SurgeForgeTitle => legal.Where(x => !x.AttackLocked).OrderByDescending(x => x.CurrentDP * 1000 + x.CurrentPP).FirstOrDefault() ?? legal.FirstOrDefault(),
        _ => SpringTarget(p, pending, legal)
    };
}
