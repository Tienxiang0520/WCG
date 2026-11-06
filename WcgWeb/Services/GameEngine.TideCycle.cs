using WcgWeb.Models;
namespace WcgWeb.Services;

// 新流派「潮汐輪迴」（理智＋生機，WCG-373–381，2026-10-25）：把自己的怪收回手牌再付費召喚，同一張牌的進場效果用好幾次。
// Every discount lasts only until the end of the turn, so no counter is needed. Returning to hand never triggers 陣亡;
// attachments go to the graveyard. Only catalogs from that day contain these ids, so older eras never reach this code.
public partial class GameEngine
{
    internal const string TideReturnTitle = "可選擇另 1 隻己方怪物移回手牌（本回合下一張付費召喚的怪物費用 -1）";

    // 374 回潮術士: your next paid monster this turn costs 1 less. 381 萬潮回歸: every paid monster this turn costs 2 less.
    // 379 漲潮巨龜: costs 2 less in your hand once you have returned one of your monsters this turn.
    private int TideCostReduction(PlayerState p, CardDefinition card)
    {
        if (!card.IsMonster) return 0;
        var n = 0;
        if (p.TideDiscountTurn == TurnNumber) n += 1;
        if (p.TideSurgeTurn == TurnNumber) n += 2;
        if (card.Id == "WCG-379" && p.TideBounceTurn == TurnNumber) n += 2;
        return n;
    }
    // Called when a monster is paid for from hand: 374's discount is used up.
    private void TidePaidSummon(PlayerState p) { if (p.TideDiscountTurn == TurnNumber) p.TideDiscountTurn = -1; }
    // You returned one of your own monsters to your hand (on your turn): 379's discount, 378 輪迴之環 draws (at most twice per turn each).
    private void TideBounced(PlayerState home, MonsterInstance m)
    {
        if (!ReferenceEquals(home, ActivePlayer)) return;
        home.TideBounceTurn = TurnNumber;
        foreach (var ring in home.Structures.Where(s => !s.IsSet && s.Card.Id == "WCG-378" && s.TriggersThisTurn < 2).ToArray())
        {
            ring.TriggersThisTurn++;
            _effects.AddLast(() => { if (!home.Structures.Contains(ring)) return; Present("trigger", home, ring.InstanceId, card: ring.Card, label: ring.Card.Name); DrawMany(home, 1); });
        }
    }

    private bool TideDeploy(PlayerState p, MonsterInstance m)
    {
        var e = GetOpponent(p);
        switch (m.Card.Id)
        {
            case "WCG-373": Resolve(() => Heal(p, 1), () => DrawMany(p, 1)); break;
            case "WCG-374": OptionalTarget(p, TideReturnTitle, p.Field.Where(x => x != m && x.IsUnit), x => { Bounce(x); p.TideDiscountTurn = TurnNumber; }); break;
            case "WCG-376":
                foreach (var x in Player.Field.Concat(Computer.Field).Where(x => x != m && x.IsUnit && PointsAt(p, m, Owner(x), x)).ToArray()) AddDpCounter(x, m.Card);
                break;
            case "WCG-377": Search(p, 3, _ => true); break;
            case "WCG-379": Resolve(() => Heal(p, 2), () => DrawMany(p, 1)); break;
            case "WCG-380": PickMonster(p, "選擇移回手牌的敵方怪物", e.Field.Where(x => x.IsUnit), Bounce); break;
            default: return MireDeploy(p, m);
        }
        return true;
    }
    private List<MonsterInstance>? TideSpellTargets(PlayerState p, CardDefinition c) => c.Id == "WCG-375" ? p.Field.Where(x => x.IsUnit).ToList() : SurgeSpellTargets(p, c);
    private static bool TideNeedsTarget(string id) => id == "WCG-375" || SurgeNeedsTarget(id);
    private bool TideSpell(PlayerState p, CardInstance source, MonsterInstance? t)
    {
        switch (source.Card.Id)
        {
            case "WCG-375":
                if (t == null || !Alive(t) || !p.Field.Contains(t)) break;
                var big = t.Card.TotalCost >= 3;
                Resolve(() => Bounce(t), () => { if (big) DrawMany(p, 1); });
                break;
            case "WCG-381":
                p.TideSurgeTurn = TurnNumber;
                var mine = p.Field.Where(x => x.IsUnit).ToArray();
                Resolve(() => { foreach (var x in mine) Bounce(x); }, () => DrawMany(p, mine.Length));
                break;
            default: return MireSpell(p, source, t);
        }
        RefreshBoard();
        return true;
    }
    private string TidePlayProblem(PlayerState p, CardDefinition c) => c.Id switch
    {
        "WCG-375" when !p.Field.Any(x => x.IsUnit) => "需要 1 隻己方怪物。",
        _ => MirePlayProblem(p, c)
    };

    // ── 天梯電腦 ──
    // Worth picking up again: a cheap-enough monster whose 進場 does something.
    private static bool Reusable(MonsterInstance m) => m.IsUnit && m.Card.Text.Contains("進場：", StringComparison.Ordinal);
    private double? TidePlayValue(PlayerState p, CardInstance c)
    {
        var units = p.Field.Where(x => x.IsUnit).ToList();
        switch (c.Card.Id)
        {
            case "WCG-375":
                var pick = units.Where(Reusable).Where(x => x.IsTapped || !CanAttack(x)).OrderByDescending(x => x.Card.TotalCost).FirstOrDefault();
                return pick == null || p.AvailableEnergy < pick.Card.TotalCost ? -5 : 1.5 + (pick.Card.TotalCost >= 3 ? 1.5 : 0);
            case "WCG-381":
                var n = units.Count;
                return n < 3 || units.Count(x => CanAttack(x)) >= 3 ? -5 : n * 1.8 - 2;
        }
        return MirePlayValue(p, c);
    }
    private MonsterInstance? TideSpellTarget(PlayerState p, string id, IReadOnlyList<MonsterInstance> own) => id switch
    {
        "WCG-375" => own.Where(Reusable).OrderByDescending(m => (m.IsTapped ? 5 : 0) + m.Card.TotalCost).FirstOrDefault() ?? own.OrderBy(BodyValue).FirstOrDefault(),
        _ => SurgeSpellTarget(p, id, own)
    };
    // 374: pick up a tapped monster whose 進場 can be used again; otherwise keep the board.
    private ChoiceOption? TideChoice(PlayerState p, PendingChoice choice)
    {
        if (choice.Title != TideReturnTitle) return FateChoice(p, choice);
        var reuse = p.Field.Where(x => Reusable(x) && x.IsTapped && x.Card.TotalCost <= 3 && x.Card.Id != "WCG-374")
            .OrderByDescending(x => x.Card.TotalCost).FirstOrDefault();
        return choice.Options.FirstOrDefault(o => reuse != null && o.Id == reuse.InstanceId.ToString()) ?? choice.Options.First(o => o.Id == "SKIP");
    }
}
