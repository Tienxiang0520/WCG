using WcgWeb.Models;
namespace WcgWeb.Services;

// 新流派「墓誓軍團」（秩序＋深淵，WCG-346–354，2026-10-24）：自己磨牌、犧牲，墓地越厚亡者越強，最後整排從墓地站起來。
// The graveyard is a public zone, so no counter is needed. 349 shuffles 3 graveyard cards to the bottom of the deck as its
// cost (nothing is ever removed from the game). Only catalogs from that day contain these ids, so older eras never reach this code.
public partial class GameEngine
{
    internal const string GraveBottomTitle = "選擇洗回牌庫底的墓地牌";
    internal const string GraveRaiseTitle = "選擇從墓地免費召喚的怪物";
    private readonly Dictionary<string, int> graveReviveTurn = [];

    private static int Grave(PlayerState p) => p.Graveyard.Count;
    // Free summon from your graveyard into a slot you choose (no 進場); ready = it can attack this turn.
    private void GraveRaise(PlayerState p, CardInstance c, bool ready, Action? after = null)
    {
        if (p.Occupied >= 5 || !p.Graveyard.Contains(c)) { after?.Invoke(); return; }
        ChooseSlot(p, c.Card, slot =>
        {
            if (!p.Graveyard.Remove(c)) return;
            plannedSlots[c.InstanceId] = slot; Summon(p, c, false);
            Present("recover", p, c.InstanceId, card: c.Card, label: "從墓地免費召喚");
            if (ready && p.Field.FirstOrDefault(x => x.InstanceId == c.InstanceId) is { } m) { ReadyMonster(m); Present("status", p, m.InstanceId, card: m.Card, label: "本回合可以攻擊"); }
            after?.Invoke();
        });
    }
    private IEnumerable<CardInstance> GraveSmall(PlayerState p, int pp) => p.Graveyard.Where(c => c.Card.IsMonster && (c.Card.PP ?? 0) <= pp);

    // 347 列隊亡兵: +300 PP per 5 cards in your graveyard.
    private int GravePower(PlayerState p, MonsterInstance m) => m.Card.Id == "WCG-347" ? 300 * (Grave(p) / 5) : RootPower(p, m);
    // 350 冥誓審判官: the monster its ↑ arrow points at gets -200 PP per 5 cards in its controller's graveyard.
    // Arrow effects with a value that changes during play, one entry per source (also listed in the PP breakdown).
    private IEnumerable<(CardDefinition Source, int Amount)> ArchArrowSources(PlayerState owner, MonsterInstance unit) =>
        Player.Field.Concat(Computer.Field).Where(s => s != unit && Active(s, "WCG-350") && PointsAt(Owner(s), s, owner, unit)).Select(s => (s.Card, -200 * (Grave(Owner(s)) / 5)));
    private int GraveArrowPower(PlayerState owner, MonsterInstance unit) => ArchArrowSources(owner, unit).Sum(x => x.Amount);
    private int GraveCostReduction(PlayerState p, CardDefinition card) => card.Id == "WCG-353" ? p.Graveyard.Count(c => c.Card.IsMonster) : 0;

    private bool GraveDeploy(PlayerState p, MonsterInstance m)
    {
        var e = GetOpponent(p);
        switch (m.Card.Id)
        {
            case "WCG-346": Mill(p, 3); break;
            case "WCG-350":
                if (Grave(p) >= 10) PickMonster(p, "選擇消滅的敵方怪物", e.Field.Where(x => x.IsUnit), x => KillBatch([x], m.Card.Name));
                break;
            default: return RootDeploy(p, m);
        }
        return true;
    }
    // 351 不朽方陣兵: on death, with 8+ cards in your graveyard, it comes back from your graveyard (once per turn).
    private bool GraveDeath(PlayerState p, MonsterInstance m, CardInstance grave)
    {
        if (m.Card.Id != "WCG-351") return RootDeath(p, m, grave);
        if (Grave(p) < 8 || !p.Graveyard.Contains(grave) || p.Occupied >= 5 || graveReviveTurn.GetValueOrDefault(p.Id, -1) == TurnNumber) return true;
        graveReviveTurn[p.Id] = TurnNumber;
        Present("trigger", p, m.InstanceId, card: m.Card, label: "陣亡能力");
        GraveRaise(p, grave, false);
        return true;
    }
    private static List<MonsterInstance>? GraveSpellTargets(PlayerState p, CardDefinition c) => RootSpellTargets(p, c);
    private static bool GraveNeedsTarget(string id) => RootNeedsTarget(id);
    private bool GraveSpell(PlayerState p, CardInstance source, MonsterInstance? t)
    {
        var e = GetOpponent(p);
        switch (source.Card.Id)
        {
            case "WCG-348": Resolve(() => DrawMany(p, 2), () => Mill(p, 2)); break;
            case "WCG-352": SpellDamage(p, e, Math.Min(3, Grave(p) / 4), source.Card); break;
            case "WCG-354": GraveFill(p); break;
            default: return RootSpell(p, source, t);
        }
        RefreshBoard();
        return true;
    }
    // 354 萬骸歸列: one at a time until your slots are full or no printed-PP-1800-or-less monster is left.
    private void GraveFill(PlayerState p)
    {
        if (IsOver || p.Occupied >= 5 || !GraveSmall(p, 1800).Any()) return;
        CardChoice(p, GraveRaiseTitle, GraveSmall(p, 1800), c => GraveRaise(p, c, true, () => GraveFill(p)));
    }
    private string GravePlayProblem(PlayerState p, CardDefinition c) => c.Id switch
    {
        "WCG-348" when !p.Field.Any(x => x.IsUnit) => "需要犧牲 1 隻己方怪物。",
        "WCG-354" when p.Occupied >= 5 => "你場上沒有空格。",
        _ => RootPlayProblem(p, c)
    };
    // 349 骸骨軍旗: tap, shuffle 3 other cards of your graveyard to the bottom of your deck, free summon a printed-PP-1200-or-less monster.
    private bool GraveCanActivate(PlayerState p, MonsterInstance s) => s.Card.Id == "WCG-349" && p.Occupied < 5 && Grave(p) >= 4 && GraveSmall(p, 1200).Any() || RootCanActivate(p, s);
    private void GraveActivate(PlayerState p, MonsterInstance s)
    {
        CardChoice(p, GraveRaiseTitle, GraveSmall(p, 1200), pick =>
        {
            s.IsTapped = true;
            var chosen = new List<CardInstance>();
            void Next()
            {
                if (chosen.Count == 3) { GraveBottom(p, chosen); GraveRaise(p, pick, false); return; }
                CardChoice(p, GraveBottomTitle + $"（{chosen.Count + 1}/3）", p.Graveyard.Where(c => c != pick && !chosen.Contains(c)), c => { chosen.Add(c); Next(); });
            }
            Next();
        });
    }
    private void GraveBottom(PlayerState p, List<CardInstance> cards)
    {
        foreach (var c in cards.OrderBy(_ => _random.Next()).ToList()) { if (!p.Graveyard.Remove(c)) continue; p.Deck.Add(c); }
        Present("status", p, label: $"墓地 {cards.Count} 張洗回牌庫底");
        Log($"【{p.Name}】把墓地 {cards.Count} 張牌洗回牌庫底。", "action");
    }

    // ── 天梯電腦 ──
    private double? GravePlayValue(PlayerState p, CardInstance c)
    {
        var e = GetOpponent(p);
        switch (c.Card.Id)
        {
            case "WCG-348":
                var fodder = p.Field.Where(x => x.IsUnit).Select(BodyValue).DefaultIfEmpty(99).Min();
                return p.Deck.Count < 6 ? -5 : 4 - fodder * .4;
            case "WCG-352":
                var dmg = Math.Min(3, Grave(p) / 4);
                return dmg >= e.Hp ? 1000 : dmg == 0 ? -5 : dmg * 2 - 1;
            case "WCG-354":
                var n = Math.Min(5 - p.Occupied, GraveSmall(p, 1800).Count());
                return n <= 1 ? -3 : n * 3.5;
        }
        return RootPlayValue(p, c);
    }
    private static MonsterInstance? GraveTarget(PlayerState p, PendingTarget pending, List<MonsterInstance> legal) => RootTarget(p, pending, legal);
}
