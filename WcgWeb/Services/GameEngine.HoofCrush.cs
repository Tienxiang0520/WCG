using WcgWeb.Models;
namespace WcgWeb.Services;

// 新流派「重蹄碾壓」（生機＋狂怒，WCG-337–345，2026-10-24）：讓怪物的 DP 越長越大，一下打 3～4 點，兩下就贏。
// DP 指示物 (+1 DP each) are physical counters on the card: they stack, stay when the monster is silenced, and are
// removed when it leaves play (a monster coming back is a new instance with none). Only catalogs from that day contain
// these ids, so older eras never reach this code.
public partial class GameEngine
{
    internal const string HoofCounterTitle = "選擇放置 1 個 DP 指示物的己方怪物";
    internal const string HoofRamTitle = "選擇本回合嘲諷擋不住的 DP 3 以上己方怪物";

    private void AddDpCounter(MonsterInstance m, CardDefinition source, int n = 1)
    {
        if (!Alive(m) || n <= 0) return;
        m.DpCounters += n;
        Present("status", Owner(m), m.InstanceId, card: source, label: n == 1 ? "放置 1 個 DP 指示物" : $"放置 {n} 個 DP 指示物");
        Log($"【{m.Card.Name}】獲得 {n} 個 DP 指示物（DP {m.CurrentDP}）。", "action");
    }
    private void MakeUnblockable(PlayerState p, MonsterInstance m)
    {
        if (!Alive(m)) return;
        m.TurnUnblockable = true;
        Present("status", p, m.InstanceId, card: m.Card, label: "本回合嘲諷擋不住");
    }

    // 337 角鬥幼犀: each combat it survives puts 1 DP 指示物 on it.
    private void HoofSurvived(PlayerState p, MonsterInstance m)
    {
        if (Active(m, "WCG-337")) AddDpCounter(m, m.Card);
    }
    // 342 荒野角力場: your monster destroyed an enemy monster in combat and is still on the field.
    private void HoofCombatWon(PlayerState p, MonsterInstance m, MonsterInstance other)
    {
        if (!Alive(m) || Alive(other) || !p.Field.Contains(m)) return;
        foreach (var s in p.Structures.Where(x => !x.IsSet && x.Card.Id == "WCG-342").ToArray())
        {
            Present("trigger", p, s.InstanceId, card: s.Card, label: s.Card.Name);
            AddDpCounter(m, s.Card);
        }
    }
    // 339 岩甲犀王: at the end of your turn, if it is upright, put 1 DP 指示物 on it.
    private void HoofEndTurn(PlayerState p, List<Action> effects)
    {
        foreach (var m in p.Field.Where(x => Active(x, "WCG-339")).ToArray())
            effects.Add(() => { if (Alive(m) && Active(m, "WCG-339") && Upright(m)) { Present("trigger", p, m.InstanceId, card: m.Card, label: m.Card.Name); AddDpCounter(m, m.Card); } });
    }
    // 341 怒蹄戰士: attacking the enemy player, + the number of your other monsters with DP 2 or more.
    private int HoofAttackDp(MonsterInstance a)
    {
        if (!Active(a, "WCG-341") || !Alive(a)) return 0;
        return Owner(a).Field.Count(x => x != a && x.IsUnit && x.CurrentDP - x.AttackDamageBonus >= 2);
    }
    // 343 鐵皮巨獸: when it attacks with DP 3 or more, the monster it fights gets -500 PP.
    private static int HoofCombatPower(MonsterInstance m, MonsterInstance other, bool attacking) =>
        !attacking && other.IsUnit && !other.IsSilenced && other.Card.Id == "WCG-343" && other.CurrentDP - other.AttackDamageBonus >= 3 ? -500 : 0;

    private List<MonsterInstance>? HoofSpellTargets(PlayerState p, CardDefinition c) =>
        c.Id is "WCG-338" or "WCG-345" ? p.Field.Where(x => x.IsUnit).ToList() : LaneSpellTargets(p, c);
    private static bool HoofNeedsTarget(string id) => id is "WCG-338" or "WCG-345" || LaneNeedsTarget(id);
    private bool HoofSpell(PlayerState p, CardInstance source, MonsterInstance? t)
    {
        switch (source.Card.Id)
        {
            case "WCG-338": if (t != null && Alive(t) && p.Field.Contains(t)) Attach(p, source, t); break;
            case "WCG-340":
                var big = p.Field.Where(x => x.IsUnit && x.CurrentDP >= 3).ToList();
                if (big.Count > 0) PickMonster(p, HoofRamTitle, big, x => MakeUnblockable(p, x));
                else PickMonster(p, HoofCounterTitle, p.Field.Where(x => x.IsUnit), x => AddDpCounter(x, source.Card));
                break;
            case "WCG-344":
                Resolve(() => { foreach (var m in p.Field.Where(x => x.IsUnit).ToArray()) AddDpCounter(m, source.Card); }, () => DrawMany(p, 1));
                break;
            case "WCG-345":
                if (t == null || !Alive(t) || !p.Field.Contains(t)) break;
                var dp = t.CurrentDP - t.AttackDamageBonus; t.TurnDpBonus += dp;
                Present("status", p, t.InstanceId, card: source.Card, label: $"本回合 DP ×2（{t.CurrentDP}）");
                if (t.CurrentDP - t.AttackDamageBonus >= 4) MakeUnblockable(p, t);
                break;
            default: return LaneSpell(p, source, t);
        }
        RefreshBoard();
        return true;
    }
    private string HoofPlayProblem(PlayerState p, CardDefinition c) => c.Id switch
    {
        "WCG-338" or "WCG-340" or "WCG-345" when !p.Field.Any(x => x.IsUnit) => "需要 1 隻己方怪物。",
        _ => LanePlayProblem(p, c)
    };

    // ── 天梯電腦 ──
    private static int Dp(MonsterInstance m) => m.CurrentDP - m.AttackDamageBonus;
    // Face damage this monster could deal now if nothing blocked it.
    private double? HoofPlayValue(PlayerState p, CardInstance c)
    {
        var e = GetOpponent(p);
        var units = p.Field.Where(x => x.IsUnit).ToList();
        var ready = units.Where(CanAttack).ToList();
        switch (c.Card.Id)
        {
            case "WCG-338": return units.Count == 0 ? -5 : 2.5 + (ready.Count > 0 ? 1 : 0);
            case "WCG-340":
                var big = ready.Where(x => Dp(x) >= 3 && !CanAttackPlayer(x)).ToList();
                if (big.Count > 0) return big.Max(Dp) >= e.Hp ? 1000 : big.Max(Dp) * 2.5;
                return units.Count == 0 ? -5 : 1.5;
            case "WCG-344": return units.Count * 2.2 + 1 - 2;
            case "WCG-345":
                if (ready.Count == 0) return -3;
                var best = ready.Max(x => Dp(x) * 2 >= 4 || CanAttackPlayer(x) ? Dp(x) * 2 : 0);
                return best >= e.Hp ? 1000 : best >= 4 ? best * 1.5 : -2;
        }
        return LanePlayValue(p, c);
    }
    private MonsterInstance? HoofSpellTarget(PlayerState p, string id, IReadOnlyList<MonsterInstance> own) => id switch
    {
        "WCG-338" => own.OrderByDescending(m => (CanAttack(m) ? 3 : 0) + (m.Card.Id is "WCG-337" or "WCG-339" or "WCG-343" ? 2 : 0) + Dp(m) + BodyValue(m) * .2).FirstOrDefault(),
        "WCG-345" => own.Where(CanAttack).OrderByDescending(Dp).FirstOrDefault() ?? own.OrderByDescending(Dp).FirstOrDefault(),
        _ => LaneSpellTarget(p, id, own)
    };
    private MonsterInstance? HoofTarget(PlayerState p, PendingTarget pending, List<MonsterInstance> legal) => pending.Title switch
    {
        HoofRamTitle => legal.OrderByDescending(m => (CanAttack(m) ? 10 : 0) + Dp(m)).FirstOrDefault(),
        HoofCounterTitle => legal.OrderByDescending(m => (CanAttack(m) ? 3 : 0) + Dp(m) + BodyValue(m) * .2).FirstOrDefault(),
        _ => null
    };
}
