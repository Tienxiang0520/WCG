using WcgWeb.Models;
namespace WcgWeb.Services;

// 新流派「星律倒數」（理智＋中立，WCG-409–417，2026-10-25）：卡上放倒數指示物，歸零那一刻才觸發大效果。
// 倒數指示物 (physical counters on a monster or structure): at the start of your turn remove 1 from each of your cards;
// removing the last one triggers the card's 「移除最後 1 個倒數指示物時」 effect. Silence removes them all without
// triggering; leaving play removes them (a returning card is a new instance). They are never put on enemy cards.
// Only catalogs from that day contain these ids, so older eras never reach this code.
public partial class GameEngine
{
    internal const string StarClockTitle = "倒懸時鐘：選擇消滅的敵方怪物";
    internal const string StarCatalystTitle = "時隙催化：選擇移除 2 個倒數指示物的己方卡";
    internal const string StarRealignTitle = "星軌重排：選擇放置 2 個倒數指示物的己方卡";

    // Cards whose text has 「移除最後 1 個倒數指示物時」.
    internal static bool StarCard(string id) => id is "WCG-409" or "WCG-410" or "WCG-413" or "WCG-414";
    private static bool StarLive(MonsterInstance m) => !m.IsSet && !(m.IsUnit && m.IsSilenced);
    private void PlaceCountdown(MonsterInstance m, int n, CardDefinition source)
    {
        if (!Alive(m) || n <= 0) return;
        m.Countdown += n; RefreshBoard();
        Present("status", Owner(m), m.InstanceId, card: source, label: $"放置 {n} 個倒數指示物");
        Log($"【{m.Card.Name}】放置 {n} 個倒數指示物（共 {m.Countdown} 個）。", "action");
    }
    // Remove n counters; removing the last one triggers the card (for the player who controls it).
    private void RemoveCountdown(MonsterInstance m, int n, Action? fired = null)
    {
        if (!Alive(m) || m.Countdown <= 0 || n <= 0) return;
        m.Countdown = Math.Max(0, m.Countdown - n); RefreshBoard();
        if (m.Countdown > 0) return;
        fired?.Invoke();
        StarFire(Owner(m), m);
    }
    private void StarFire(PlayerState p, MonsterInstance m)
    {
        var e = GetOpponent(p);
        Present("trigger", p, m.InstanceId, card: m.Card, label: "倒數歸零");
        Log($"【{m.Card.Name}】移除最後 1 個倒數指示物，觸發效果。", "action");
        var actions = new List<Action>();
        switch (m.Card.Id)
        {
            case "WCG-409": actions.Add(() => DrawMany(p, 2)); break;
            case "WCG-410":
                actions.Add(() => PickMonster(p, StarClockTitle, e.Field.Where(x => x.IsUnit), x => KillBatch([x], m.Card.Name)));
                actions.Add(() => PlaceCountdown(m, 3, m.Card));
                break;
            case "WCG-413": actions.Add(() => { if (!Alive(m)) return; AddDpCounter(m, m.Card, 2); GainShieldCounter(m, m.Card); }); break;
            case "WCG-414":
                actions.Add(() => Damage(e, 2, new(p, m.Card, m, "passive")));
                actions.Add(() => KillBatch(e.Field.Where(x => x.IsUnit && (x.Card.PP ?? 0) <= 900).ToArray(), m.Card.Name));
                break;
        }
        // 416 永恆鐘塔: draw 1 and heal 1 each time one of your cards loses its last counter.
        foreach (var tower in p.Structures.Where(x => !x.IsSet && x.Card.Id == "WCG-416").ToArray())
            actions.Add(() => { if (!p.Structures.Contains(tower)) return; Present("trigger", p, tower.InstanceId, card: tower.Card, label: tower.Card.Name); DrawMany(p, 1); Heal(p, 1); });
        Resolve(actions.ToArray());
    }
    private bool StarDeploy(PlayerState p, MonsterInstance m)
    {
        switch (m.Card.Id)
        {
            case "WCG-409": PlaceCountdown(m, 1, m.Card); break;
            case "WCG-413": case "WCG-414": PlaceCountdown(m, 2, m.Card); break;
            default: return FateDeploy(p, m);
        }
        return true;
    }
    // 410 倒懸時鐘 enters with 3 counters.
    private void StarStructureEnter(PlayerState p, MonsterInstance s) { if (s.Card.Id == "WCG-410" && !s.IsSet) PlaceCountdown(s, 3, s.Card); }
    // Start of your turn: each of your cards loses 1 counter, plus 1 for each of your 411 觀星術士 whose arrows point at it.
    private void StarStartTurn(PlayerState p)
    {
        var adepts = p.Field.Where(x => Active(x, "WCG-411")).ToArray();
        var cards = Player.Board.Concat(Computer.Board).Where(x => x.Countdown > 0 && (p.Board.Contains(x) || adepts.Any(a => PointsAt(p, a, Owner(x), x)))).ToArray();
        foreach (var c in cards.OrderBy(x => p.Board.Contains(x) ? 0 : 1).ThenBy(x => x.Slot))
            _effects.AddLast(() =>
            {
                if (!Alive(c) || c.Countdown <= 0 || IsOver) return;
                var n = (p.Board.Contains(c) ? 1 : 0) + adepts.Count(a => Alive(a) && Active(a, "WCG-411") && PointsAt(p, a, Owner(c), c));
                RemoveCountdown(c, n);
            });
    }
    // 413 封時守衛: can't be targeted by spells while it has counters.
    private bool StarWarded(MonsterInstance m) => Active(m, "WCG-413") && m.Countdown > 0;

    private bool StarSpell(PlayerState p, CardInstance source, MonsterInstance? t)
    {
        var e = GetOpponent(p);
        switch (source.Card.Id)
        {
            case "WCG-412":
                var ticking = SpellResolvePickable(p, p.Board.Where(x => x.Countdown > 0));
                if (ticking.Count == 0) DrawMany(p, 1);
                else PickMonster(p, StarCatalystTitle, ticking, x => RemoveCountdown(x, 2));
                break;
            case "WCG-415":
                var spent = StarRealignable(p);
                if (spent.Count == 0) DrawMany(p, 1);
                else PickMonster(p, StarRealignTitle, spent, x => PlaceCountdown(x, 2, source.Card));
                break;
            case "WCG-417":
                var all = p.Board.Where(x => x.Countdown > 0).OrderBy(x => x.Slot).ToArray();
                var fired = 0;
                var steps = all.Select(x => (Action)(() => { if (Alive(x) && x.Countdown > 0) { x.Countdown = 1; RemoveCountdown(x, 1, () => fired++); } })).ToList();
                steps.Add(() => { if (fired > 0) Damage(e, fired, new(p, source.Card, null, "spell")); else DrawMany(p, 3); });
                Resolve(steps.ToArray());
                break;
            default: return FateSpell(p, source, t);
        }
        RefreshBoard();
        return true;
    }
    private List<MonsterInstance> StarRealignable(PlayerState p) => p.Board.Where(x => StarCard(x.Card.Id) && StarLive(x) && x.Countdown == 0).ToList();

    // ── 天梯電腦 ──
    private double StarFireValue(PlayerState p, MonsterInstance m) => m.Card.Id switch
    {
        "WCG-409" => 2, "WCG-410" => GetOpponent(p).Field.Where(x => x.IsUnit).Select(BodyValue).DefaultIfEmpty(0).Max(),
        "WCG-413" => 2, "WCG-414" => 2 + GetOpponent(p).Field.Count(x => x.IsUnit && (x.Card.PP ?? 0) <= 900) * 1.5, _ => 0
    };
    private double? StarPlayValue(PlayerState p, CardInstance c)
    {
        var e = GetOpponent(p);
        switch (c.Card.Id)
        {
            case "WCG-412":
                var near = p.Board.Where(x => x.Countdown is > 0 and <= 2).ToList();
                return near.Count > 0 ? near.Max(x => StarFireValue(p, x)) : p.Board.Any(x => x.Countdown > 0) ? 0.5 : 1;
            case "WCG-415":
                var spent = StarRealignable(p);
                return spent.Count > 0 ? spent.Max(x => StarFireValue(p, x)) * .7 : 1;
            case "WCG-417":
                var all = p.Board.Where(x => x.Countdown > 0).ToList();
                if (all.Count == 0) return 2;
                return all.Count + (all.Any(x => x.Card.Id == "WCG-414") ? 2 : 0) >= e.Hp ? 1000 : all.Sum(x => StarFireValue(p, x)) + all.Count - 2;
        }
        return FatePlayValue(p, c);
    }
    private static int StarRank(string id) => id switch { "WCG-414" => 4, "WCG-410" => 3, "WCG-409" => 2, "WCG-413" => 1, _ => 0 };
    private static MonsterInstance? StarTarget(PlayerState p, PendingTarget pending, List<MonsterInstance> legal) => pending.Title switch
    {
        StarClockTitle => legal.OrderByDescending(x => x.CurrentPP).FirstOrDefault(),
        StarCatalystTitle => legal.OrderBy(x => x.Countdown).ThenByDescending(x => StarRank(x.Card.Id)).FirstOrDefault(),
        StarRealignTitle => legal.OrderByDescending(x => StarRank(x.Card.Id)).FirstOrDefault(),
        _ => FateTarget(p, pending, legal)
    };
}
