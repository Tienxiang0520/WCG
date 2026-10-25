using WcgWeb.Models;
namespace WcgWeb.Services;

// 新流派「血誓反擊」（秩序＋狂怒，WCG-364–372，2026-10-24）：故意挨打，每受一次傷害就在 364 血誓祭壇疊血誓指示物，
// 生命 3 以下整套暴走，最後用 370／372 把指示物換成清場或傷害。
// Only catalogs from that day contain these ids, so older eras never reach this code.
public partial class GameEngine
{
    internal const string BloodSettleTitle = "血帳清算：選擇消滅的敵方怪物（移除 2 個血誓指示物）";
    private static bool LowLife(PlayerState p) => p.Hp <= 3;
    private static int BloodTotal(PlayerState p) => p.Structures.Where(s => !s.IsSet && s.Card.Id == "WCG-364").Sum(s => s.BloodCounters);
    private void AddBlood(PlayerState p, MonsterInstance altar, int n = 1)
    {
        altar.BloodCounters += n;
        Present("status", p, altar.InstanceId, amount: n, card: altar.Card, label: "血誓指示物");
    }
    // Removes n counters from your altars (fullest first). Returns how many were removed.
    private int RemoveBlood(PlayerState p, int n)
    {
        var removed = 0;
        foreach (var s in p.Structures.Where(s => !s.IsSet && s.Card.Id == "WCG-364" && s.BloodCounters > 0).OrderByDescending(s => s.BloodCounters))
        {
            var take = Math.Min(n - removed, s.BloodCounters); s.BloodCounters -= take; removed += take;
            if (removed >= n) break;
        }
        return removed;
    }
    // Every damage event you take, whatever its source (also 369 hitting yourself): 364 gets 1 血誓指示物, 365 gets 1 DP 指示物.
    // Paying life is not damage and never comes here.
    private void BloodHurt(PlayerState p)
    {
        foreach (var altar in p.Structures.Where(s => !s.IsSet && s.Card.Id == "WCG-364")) AddBlood(p, altar);
        foreach (var guard in p.Field.Where(x => Active(x, "WCG-365")).ToArray()) AddDpCounter(guard, guard.Card);
    }
    // 367 誓言之怒: +600 PP and +1 DP while your life is 3 or less.
    private int BloodPower(PlayerState p, MonsterInstance m) => m.Card.Id == "WCG-367" ? (LowLife(p) ? 600 : 0) : SurgePower(p, m);
    private int BloodDp(PlayerState p, MonsterInstance m) => Active(m, "WCG-367") && LowLife(p) ? 1 : 0;
    // 371 誓血軍旗手: its ← → arrows give another +200 for each life its controller has less than the opponent.
    private IEnumerable<(CardDefinition Source, int Amount)> BloodArrowSources(PlayerState owner, MonsterInstance unit) =>
        Player.Field.Concat(Computer.Field).Where(s => s != unit && Active(s, "WCG-371") && PointsAt(Owner(s), s, owner, unit) && !Across(s, unit))
            .Select(s => (s.Card, 200 * Math.Max(0, GetOpponent(Owner(s)).Hp - Owner(s).Hp))).Where(x => x.Item2 != 0);

    private bool BloodDeploy(PlayerState p, MonsterInstance m)
    {
        if (m.Card.Id != "WCG-369") return TideDeploy(p, m);
        // Written as damage on purpose: it opens a counter window and feeds 364／365.
        Resolve(() => Damage(p, 1, new(p, m.Card, m, "deploy")), () => DrawMany(p, 1));
        return true;
    }
    private List<MonsterInstance>? BloodSpellTargets(PlayerState p, CardDefinition c) => TideSpellTargets(p, c);
    private static bool BloodNeedsTarget(string id) => TideNeedsTarget(id);
    private bool BloodSpell(PlayerState p, CardInstance source, MonsterInstance? t)
    {
        var e = GetOpponent(p);
        switch (source.Card.Id)
        {
            case "WCG-370": BloodSettle(p, source.Card); break;
            case "WCG-372":
                var n = RemoveBlood(p, BloodTotal(p));
                Log($"【{p.Name}】移除 {n} 個血誓指示物。", "action");
                if (n > 0) SpellDamage(p, e, n, source.Card);
                break;
            default: return TideSpell(p, source, t);
        }
        RefreshBoard();
        return true;
    }
    // 370: one enemy monster at a time, 2 counters each; the player may stop whenever they like.
    private void BloodSettle(PlayerState p, CardDefinition card)
    {
        var e = GetOpponent(p);
        if (IsOver || BloodTotal(p) < 2) return;
        OptionalTarget(p, BloodSettleTitle, e.Field.Where(x => x.IsUnit), x =>
        {
            RemoveBlood(p, 2);
            Resolve(() => KillBatch([x], card.Name), () => BloodSettle(p, card));
        });
    }
    private string BloodPlayProblem(PlayerState p, CardDefinition c) => c.Id switch
    {
        "WCG-370" when BloodTotal(p) < 2 => "你場上的血誓指示物不足 2 個。",
        "WCG-370" when !GetOpponent(p).Field.Any(x => x.IsUnit) => "敵方場上沒有怪物。",
        _ => TidePlayProblem(p, c)
    };
    // Counter cards (反擊) of this archetype; called from CanReact / ResolveDamageCounter.
    private static bool BloodCounterCard(string id) => id is "WCG-366" or "WCG-368";
    private bool BloodCanReact(PlayerState p, CardInstance card, DamageContext damage) => card.Card.Id switch
    {
        "WCG-366" => true,
        "WCG-368" => damage.Source is { Kind: "attack" } s && s.Owner != p && s.Unit != null,
        _ => false
    };
    private void BloodResolveCounter(PlayerState p, CardInstance card, DamageContext damage)
    {
        var enemy = GetOpponent(p); var source = damage.Source?.Unit;
        if (card.Card.Id == "WCG-366") { SpellDamage(p, enemy, LowLife(p) ? 2 : 1, card.Card); return; }
        Resolve(() => { if (source != null && CanDestroyDamageSource(damage)) KillBatch([source], card.Card.Name); },
            () => { if (p.Structures.FirstOrDefault(s => !s.IsSet && s.Card.Id == "WCG-364") is { } altar) AddBlood(p, altar); });
    }

    // ── 天梯電腦 ──
    private double? BloodPlayValue(PlayerState p, CardInstance c)
    {
        var e = GetOpponent(p);
        switch (c.Card.Id)
        {
            case "WCG-364": return p.Structures.Any(s => s.Card.Id == "WCG-364") ? 1 : 4;
            case "WCG-369": return p.Hp <= 1 ? -1000 : p.Hp <= 2 && e.Field.Any(x => x.IsUnit) ? -5 : 2.5;
            case "WCG-370":
                var kills = Math.Min(BloodTotal(p) / 2, e.Field.Count(x => x.IsUnit));
                return kills == 0 ? -5 : kills * 3.5 - 1;
            case "WCG-372":
                var dmg = BloodTotal(p);
                return dmg >= e.Hp ? 1000 : dmg < 3 ? -5 : dmg * 1.5;
        }
        return TidePlayValue(p, c);
    }
    private static MonsterInstance? BloodTarget(PlayerState p, PendingTarget pending, List<MonsterInstance> legal) => MireTarget(p, pending, legal);
    // 370: kill the strongest enemy monsters first.
    private ChoiceOption? BloodChoice(PlayerState p, PendingChoice choice)
    {
        if (choice.Title != BloodSettleTitle) return TideChoice(p, choice);
        var best = GetOpponent(p).Field.Where(x => x.IsUnit).OrderByDescending(x => x.CurrentPP).FirstOrDefault();
        return choice.Options.FirstOrDefault(o => best != null && o.Id == best.InstanceId.ToString()) ?? choice.Options.First(o => o.Id == "SKIP");
    }
}
