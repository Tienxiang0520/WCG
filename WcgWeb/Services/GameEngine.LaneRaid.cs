using WcgWeb.Models;
namespace WcgWeb.Services;

// 新流派「斷路突襲」（狂怒＋理智，WCG-319–327，2026-10-24）：清空對面那一格，從空的那條路直接打臉，嘲諷擋不住。
// 「正對面」 is the enemy slot 4 - Slot. It is empty when no enemy monster stands there; only monsters count (an
// enchantment in that slot doesn't close the lane).
// Only catalogs from that day contain these ids, so older eras never reach this code.
public partial class GameEngine
{
    internal const string LaneClearTitle = "選擇在你 ↑ 箭頭怪物正對面的敵方怪物移回手牌";
    internal const string LaneGaleTitle = "選擇 1 隻己方怪物 PP +300";

    // The enemy monster standing in the slot across from m (rows are physical: turned monsters stay in their owner's row).
    private MonsterInstance? Facing(MonsterInstance m) => m.Slot < 0 ? null
        : RowUnits(GetOpponent(RowOf(m))).FirstOrDefault(x => x.Slot == 4 - m.Slot);
    private bool LaneOpen(MonsterInstance m) => Alive(m) && m.Slot >= 0 && Facing(m) == null;
    private static bool UpArrow(MonsterInstance m) => !m.IsSilenced && m.Card.Arrows.Contains("up");
    // 319／320／326: taunt can't stop them while their lane is open.
    private bool LaneSlips(MonsterInstance a) => a.IsUnit && !a.IsSilenced && a.Card.Id is "WCG-319" or "WCG-320" or "WCG-326" && LaneOpen(a);
    private int LaneAttackDp(MonsterInstance a)
    {
        if (!Alive(a) || !LaneOpen(a)) return 0;
        return (a.Card.Id == "WCG-320" && !a.IsSilenced ? 1 : 0) + (Owner(a).LaneRushTurn == TurnNumber ? 1 : 0);
    }
    private int LanePower(PlayerState p, MonsterInstance m) => m.Card.Id == "WCG-324" && !m.IsSilenced && LaneOpen(m) ? 300 : 0;
    // Board badges: an open lane for the taunt-ignoring raiders, and 325's lock on the monster it faces.
    internal string? LaneStatus(MonsterInstance m) => !m.IsUnit || m.Slot < 0 ? null
        : !m.TurnUnblockable && LaneSlips(m) ? "空路：嘲諷擋不住" : BridgeLocked(m) ? "斷橋鎖住：回合開始不轉為直立" : null;
    // 325 斷橋術士: the enemy monster its ↑ arrow points at stays tapped at the start of its turn.
    private bool BridgeLocked(MonsterInstance m) =>
        Player.Field.Concat(Computer.Field).Any(s => s != m && Active(s, "WCG-325") && UpArrow(s) && Facing(s) == m);

    private bool LaneDeploy(PlayerState p, MonsterInstance m)
    {
        var facing = Facing(m);
        var unit = facing is { IsUnit: true } ? facing : null;
        switch (m.Card.Id)
        {
            case "WCG-321":
                if (facing == null) DrawMany(p, 1);
                else if (unit != null && unit.Card.TotalCost <= 2) PickMonster(p, "選擇正對面費用 2 以下的敵方怪物移回手牌", [unit], Bounce);
                break;
            case "WCG-325":
                if (facing == null) DrawMany(p, 1);
                else if (unit != null) PickMonster(p, "選擇正對面的敵方怪物橫置", [unit], x => { x.IsTapped = true; Present("status", Owner(x), x.InstanceId, card: x.Card, label: "橫置"); });
                break;
            case "WCG-326":
                if (unit != null && (unit.Card.PP ?? 0) <= 1800) PickMonster(p, "選擇正對面印刷 PP 1800 以下的敵方怪物消滅", [unit], x => KillBatch([x], m.Card.Name));
                break;
            default: return SoulDeploy(p, m);
        }
        return true;
    }

    private List<MonsterInstance>? LaneSpellTargets(PlayerState p, CardDefinition c) => c.Id switch
    {
        "WCG-322" => p.Field.Where(x => x.IsUnit).ToList(),
        "WCG-323" => ClearableLanes(p),
        _ => SoulSpellTargets(p, c)
    };
    private List<MonsterInstance> ClearableLanes(PlayerState p) =>
        p.Field.Where(x => x.IsUnit && UpArrow(x)).Select(Facing).OfType<MonsterInstance>().Where(x => x.IsUnit && !ReferenceEquals(Owner(x), p)).Distinct().ToList();
    private static bool LaneNeedsTarget(string id) => id is "WCG-322" or "WCG-323" || SoulNeedsTarget(id);
    private bool LaneSpell(PlayerState p, CardInstance source, MonsterInstance? t)
    {
        switch (source.Card.Id)
        {
            case "WCG-322":
                if (t == null || !Alive(t) || !p.Field.Contains(t)) break;
                AddTurnBonus(t, 300, source.Card);
                if (LaneOpen(t)) { ReadyMonster(t); Present("status", p, t.InstanceId, card: source.Card, label: "轉為直立"); }
                break;
            case "WCG-323":
                if (t == null || !Alive(t) || !ClearableLanes(p).Contains(t)) break;
                var taunt = t.IsTaunt; Bounce(t);
                if (taunt) DrawMany(p, 1);
                break;
            case "WCG-327":
                p.LaneRushTurn = TurnNumber;
                Log($"【{p.Name}】本回合正對面沒有敵方怪物的怪物攻擊敵方玩家時 DP +1。", "action");
                break;
            default: return SoulSpell(p, source, t);
        }
        RefreshBoard();
        return true;
    }
    private string LanePlayProblem(PlayerState p, CardDefinition c) => c.Id switch
    {
        "WCG-322" when !p.Field.Any(x => x.IsUnit) => "需要 1 隻己方怪物。",
        "WCG-323" when ClearableLanes(p).Count == 0 => "需要 1 隻在你 ↑ 箭頭怪物正對面的敵方怪物。",
        _ => SoulPlayProblem(p, c)
    };

    // ── 天梯電腦 ──
    private double? LanePlayValue(PlayerState p, CardInstance c)
    {
        var e = GetOpponent(p);
        switch (c.Card.Id)
        {
            case "WCG-322":
                var again = p.Field.Where(x => x.IsUnit && LaneOpen(x) && (x.IsTapped || x.HasAttacked) && !x.AttackLocked && (x.IsSilenced || !x.Card.CannotAttack)).ToList();
                return again.Count > 0 ? again.Max(x => x.CurrentDP) * 2.5 + 0.5 : p.Field.Any(CanAttack) ? 0.5 : -3;
            case "WCG-323":
                var lanes = ClearableLanes(p);
                return lanes.Count == 0 ? -5 : lanes.Max(x => BodyValue(x) * .4 + (x.IsTaunt ? 2.5 : 0) + x.CurrentDP) + 0.5;
            case "WCG-327":
                var dmg = p.Field.Count(x => x.IsUnit && LaneOpen(x) && CanAttack(x));
                var raw = p.Field.Where(x => x.IsUnit && CanAttack(x) && CanAttackPlayer(x)).Sum(x => x.CurrentDP);
                return dmg + raw >= e.Hp && dmg > 0 ? 1000 : dmg >= 2 ? dmg * 1.6 - 1 : -4;
        }
        return SoulPlayValue(p, c);
    }
    private MonsterInstance? LaneSpellTarget(PlayerState p, string id, IReadOnlyList<MonsterInstance> own) => id switch
    {
        "WCG-322" => own.OrderByDescending(m => (LaneOpen(m) && (m.IsTapped || m.HasAttacked) ? 10 : 0) + (CanAttack(m) ? 2 : 0) + m.CurrentDP).FirstOrDefault(),
        _ => null
    };
    // Slot choice: raiders want an open lane; 321／325／326 want the enemy their deploy hits.
    private ChoiceOption? LaneSlot(PlayerState p, PendingChoice choice)
    {
        if (choice.SourceCard is not { } card || card.Id is not ("WCG-319" or "WCG-320" or "WCG-321" or "WCG-324" or "WCG-325" or "WCG-326")) return null;
        var e = GetOpponent(p);
        double Score(ChoiceOption o)
        {
            var slot = int.Parse(o.Id[5..]);
            var f = e.Field.FirstOrDefault(m => m.Slot == 4 - slot);
            var u = f is { IsUnit: true } ? f : null;
            return card.Id switch
            {
                "WCG-321" => u != null && u.Card.TotalCost <= 2 ? 5 + BodyValue(u) * .3 : f == null ? 3 : 0,
                "WCG-325" => u != null ? 4 + u.CurrentDP + (u.IsTapped ? 0 : 1) : f == null ? 2 : 0,
                "WCG-326" => u != null && (u.Card.PP ?? 0) <= 1800 ? 6 + BodyValue(u) * .3 : f == null ? 4 : 0,
                _ => f == null ? 5 : 0,
            };
        }
        return choice.Options.Where(o => o.Id.StartsWith("SLOT:", StringComparison.Ordinal)).OrderByDescending(Score).ThenBy(o => o.Id, StringComparer.Ordinal).FirstOrDefault();
    }
}
