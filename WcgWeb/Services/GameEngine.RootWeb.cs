using WcgWeb.Models;
namespace WcgWeb.Services;

// 新流派「根脈連營」（生機＋中立，WCG-355–363，2026-10-24）：五格排滿，用 ← → 箭頭串成一條根脈，整排互相餵養。
// Everything is read from the arrows on the table plus the existing DP／聖盾指示物, so no new counter is needed.
// Only catalogs from that day contain these ids, so older eras never reach this code.
public partial class GameEngine
{
    internal const string RootReturnTitle = "可選擇 1 隻己方怪物移回手牌";

    // Arrows (one per source card) pointing at this monster, whoever controls the source.
    private int ArrowsAt(PlayerState owner, MonsterInstance unit) =>
        Player.Field.Concat(Computer.Field).Count(s => s != unit && s.IsUnit && !s.IsSilenced && s.Card.Arrows.Length > 0 && PointsAt(Owner(s), s, owner, unit));
    private bool PointedBy(PlayerState owner, MonsterInstance unit, string id) =>
        Player.Field.Concat(Computer.Field).Any(s => s != unit && Active(s, id) && PointsAt(Owner(s), s, owner, unit));
    // 357 根脈樞紐: +400 PP for each arrow pointing at it.
    private int RootPower(PlayerState p, MonsterInstance m) => m.Card.Id == "WCG-357" ? 400 * ArrowsAt(p, m) : BloodPower(p, m);
    // 357's arrows: the monsters they point at get +1 DP while pointed at (no counter).
    private int RootDp(PlayerState p, MonsterInstance unit) =>
        Player.Field.Concat(Computer.Field).Count(s => s != unit && Active(s, "WCG-357") && PointsAt(Owner(s), s, p, unit));
    // 356 連枝守衛: pointed at by 2 or more arrows is a 聖盾 source (1 counter each time it starts applying).
    private bool RootShield(PlayerState p, MonsterInstance unit) => Active(unit, "WCG-356") && ArrowsAt(p, unit) >= 2;
    // 362 擠枝新芽: played from hand, it only goes into an empty slot between two of your monsters.
    private bool RootSlotAllowed(PlayerState p, CardDefinition card, int slot) => card.Id != "WCG-362" ||
        p.Field.Any(x => x.IsUnit && !x.Turned && x.Slot == slot - 1) && p.Field.Any(x => x.IsUnit && !x.Turned && x.Slot == slot + 1);
    private bool RootSqueezeSlot(PlayerState p) => Enumerable.Range(0, 5).Any(i => !RowTaken(p, i) && RootSlotAllowed(p, cards362, i));
    private static readonly CardDefinition cards362 = new() { Id = "WCG-362" };
    // 363 世界樹之心: a monster entering a slot its arrows point at enters upright (it can attack that turn).
    private void RootEntered(PlayerState p, MonsterInstance m)
    {
        if (!m.IsUnit || !m.IsTapped || !PointedBy(p, m, "WCG-363")) return;
        m.IsTapped = false; Present("status", p, m.InstanceId, card: m.Card, label: "直立進場");
    }

    private bool RootDeploy(PlayerState p, MonsterInstance m)
    {
        switch (m.Card.Id)
        {
            case "WCG-361": OptionalTarget(p, RootReturnTitle, p.Field.Where(x => x != m && x.IsUnit), Bounce); break;
            case "WCG-363":
                foreach (var x in p.Field.Where(x => x.IsUnit && ArrowsAt(p, x) > 0).ToArray()) AddDpCounter(x, m.Card);
                break;
            default: return BloodDeploy(p, m);
        }
        return true;
    }
    // 359 斷根反噬: the monsters its arrows pointed at each get 1 DP 指示物, and the enemy player takes 1 damage.
    private bool RootDeath(PlayerState p, MonsterInstance m, CardInstance grave)
    {
        if (m.Card.Id != "WCG-359" || m.IsSilenced) return MireDeath(p, m, grave);
        var row = m.Turned ? GetOpponent(p) : p;
        Present("trigger", p, m.InstanceId, card: m.Card, label: "陣亡能力");
        foreach (var x in RowUnits(row).Where(x => x.IsUnit && (x.Slot == m.Slot - 1 || x.Slot == m.Slot + 1)).ToArray()) AddDpCounter(x, m.Card);
        Damage(GetOpponent(p), 1, new(p, m.Card, null, "death"));
        return true;
    }
    // 360 林冠滿營: at the start of your turn, if your five slots are full, your monsters get +300 PP this turn and you draw 1.
    private void RootStartTurn(PlayerState p)
    {
        foreach (var s in p.Structures.Where(x => !x.IsSet && x.Card.Id == "WCG-360").ToArray())
            _effects.AddLast(() =>
            {
                if (!p.Structures.Contains(s) || p.Occupied < 5) return;
                Present("trigger", p, s.InstanceId, card: s.Card, label: s.Card.Name);
                foreach (var x in p.Field.Where(x => x.IsUnit)) AddTurnBonus(x, 300, s.Card);
                DrawMany(p, 1);
            });
    }
    private List<MonsterInstance>? RootSpellTargets(PlayerState p, CardDefinition c) => BloodSpellTargets(p, c);
    private static bool RootNeedsTarget(string id) => BloodNeedsTarget(id);
    // 358 地脈回響: links = your monsters' ← → arrows pointing at another monster of yours (at most 4).
    private int RootLinks(PlayerState p) => Math.Min(4, p.Field.Where(s => s.IsUnit && !s.IsSilenced && (s.Card.Arrows.Contains("left") || s.Card.Arrows.Contains("right")))
        .Sum(s => p.Field.Count(t => t != s && t.IsUnit && ReferenceEquals(RowOf(s), RowOf(t)) && PointsAt(p, s, p, t))));
    private bool RootSpell(PlayerState p, CardInstance source, MonsterInstance? t)
    {
        switch (source.Card.Id)
        {
            case "WCG-358": RootEcho(p, RootLinks(p)); break;
            default: return BloodSpell(p, source, t);
        }
        RefreshBoard();
        return true;
    }
    private void RootEcho(PlayerState p, int left)
    {
        if (left <= 0 || IsOver) return;
        Ask(p, $"地脈回響：選擇回復 1 點生命或抽 1 張牌（還有 {left} 次）", [new() { Id = "HEAL", Title = "回復 1 點生命" }, new() { Id = "DRAW", Title = "抽 1 張牌" }], o =>
        {
            if (o.Id == "HEAL") Heal(p, 1); else DrawMany(p, 1);
            RootEcho(p, left - 1);
        });
    }
    private string RootPlayProblem(PlayerState p, CardDefinition c) => c.Id switch
    {
        "WCG-362" when !RootSqueezeSlot(p) => "只能放在兩隻己方怪物之間的空格。",
        _ => BloodPlayProblem(p, c)
    };
    private static bool RootCanActivate(PlayerState p, MonsterInstance s) => false;
    private int RootAndBloodDp(PlayerState p, MonsterInstance unit) => RootDp(p, unit) + BloodDp(p, unit);

    // ── 天梯電腦 ──
    private double? RootPlayValue(PlayerState p, CardInstance c)
    {
        switch (c.Card.Id)
        {
            case "WCG-358": var n = RootLinks(p); return n == 0 ? -5 : n * 1.8 - 1;
        }
        return BloodPlayValue(p, c);
    }
    private static MonsterInstance? RootTarget(PlayerState p, PendingTarget pending, List<MonsterInstance> legal) => BloodTarget(p, pending, legal);
    // 361: only pick up a tapped cheap monster whose 進場 can be used again; otherwise keep the board.
    private ChoiceOption? ArchChoice(PlayerState p, PendingChoice choice)
    {
        if (choice.Title != RootReturnTitle) return BloodChoice(p, choice);
        var reuse = p.Field.Where(x => x.IsUnit && x.IsTapped && x.Card.TotalCost <= 3 && x.Card.Text.Contains("進場：", StringComparison.Ordinal) && !CardBalanceHistory.IsRootWebCard(x.Card.Id))
            .OrderByDescending(x => x.Card.TotalCost).FirstOrDefault();
        return choice.Options.FirstOrDefault(o => reuse != null && o.Id == reuse.InstanceId.ToString()) ?? choice.Options.First(o => o.Id == "SKIP");
    }
}
