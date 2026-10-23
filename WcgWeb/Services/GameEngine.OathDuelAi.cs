using WcgWeb.Models;
namespace WcgWeb.Services;

// 天梯電腦學會「誓劍決鬥」（WCG-264–272）。Only current catalogs contain these cards, so older journals never reach this code.
public partial class GameEngine
{
    // Would a duel from a (with or without an extra counter) destroy d, and would a survive it?
    private bool DuelKills(MonsterInstance a, MonsterInstance d) => d.CurrentPP <= a.CurrentPP && !(d.HasShield && ShieldReady(d)) || a.HasPoison && !(d.HasShield && ShieldReady(d));
    private bool DuelSurvives(MonsterInstance a, MonsterInstance d, bool extraShield = false) => a.CurrentPP > d.CurrentPP && !d.HasPoison || extraShield || a.HasShield && ShieldReady(a);
    private double DuelValue(MonsterInstance a, MonsterInstance d, bool extraShield = false)
    {
        var kill = DuelKills(a, d); var live = DuelSurvives(a, d, extraShield);
        if (!kill) return live ? -2 : -BodyValue(a) - 2;
        return BodyValue(d) + (d.IsTaunt ? 3 : 0) - (live ? 0 : BodyValue(a));
    }
    // Duel value for a monster card that is about to enter (printed stats, native counter).
    private double EntryDuelValue(CardDefinition c, IEnumerable<MonsterInstance> enemies)
    {
        var pp = c.PP ?? 0; var shield = c.HasEnergyShield;
        return enemies.Select(d =>
        {
            var guarded = d.HasShield && ShieldReady(d);
            var kill = !guarded && d.CurrentPP <= pp; var live = shield || pp > d.CurrentPP && !d.HasPoison;
            return !kill ? -1 : BodyValue(d) + (d.IsTaunt ? 3 : 0) - (live ? 0 : pp / 450d + 2);
        }).Where(v => v > 0).DefaultIfEmpty(0).Max();
    }

    private double? OathPlayValue(PlayerState p, CardInstance c)
    {
        var e = GetOpponent(p);
        switch (c.Card.Id)
        {
            case "WCG-267":
                var best = p.Field.Where(a => !a.IsTapped && CanShield(a)).SelectMany(a => e.Field.Select(d => DuelValue(a, d, true))).DefaultIfEmpty(-6).Max();
                return best - 1.5;
            case "WCG-271": return p.Field.Any(CanAttack) && e.Field.Count > 0 ? 2 : -2;
            case "WCG-268": return e.Field.Any(m => m.IsTaunt) ? 2 : 1;
            case "WCG-264": return p.Field.Any(m => CanShield(m) && !m.HasShield) ? 2 : 1;
            case "WCG-266": case "WCG-269": return EntryDuelValue(c.Card, e.Field);
            case "WCG-272":
                return e.Field.Where(m => m.IsTaunt).Select(d => EntryDuelValue(c.Card, [d])).OrderByDescending(v => v).Take(2).Sum();
            case "WCG-270": return p.Field.Count >= 2 ? 1.5 : .5;
        }
        return GapPlayValue(p, c);
    }
    private MonsterInstance? OathTarget(PlayerState p, PendingTarget pending, List<MonsterInstance> legal)
    {
        var e = GetOpponent(p);
        switch (pending.Title)
        {
            case ShieldGiftTitle:
                return legal.Where(p.Field.Contains).OrderByDescending(m => (m.HasShield ? 0 : 3) + (CanAttack(m) ? 1 : 0) + BodyValue(m)).FirstOrDefault();
            case DuelTitle:
                // 榮譽決鬥: the duelist is the monster that was just tapped and given a counter.
                var a = p.Field.Where(m => m.IsTapped).OrderByDescending(m => m.CurrentPP).FirstOrDefault();
                return a == null ? null : legal.OrderByDescending(d => DuelValue(a, d)).First();
            case ArenaFighterTitle:
                var taunts = e.Field.Where(m => m.IsTaunt).ToArray();
                return legal.OrderByDescending(f => taunts.Select(d => DuelValue(f, d)).DefaultIfEmpty(-9).Max()).First();
            case ArenaTauntTitle:
                var f2 = p.Field.Where(m => !m.IsTapped).OrderByDescending(m => m.CurrentPP).FirstOrDefault();
                return f2 == null ? null : legal.OrderByDescending(d => DuelValue(f2, d)).First();
        }
        return null;
    }
    private ChoiceOption? OathChoice(PlayerState p, PendingChoice choice)
    {
        if (choice.Title != DuelTitle) return null;
        var a = p.Field.FirstOrDefault(m => m.InstanceId == choice.SourceInstanceId);
        if (a == null) return choice.Options.FirstOrDefault(o => o.Id == "SKIP");
        var e = GetOpponent(p);
        double Score(ChoiceOption o) => o.Id == "SKIP" ? 0 : e.Field.FirstOrDefault(d => d.InstanceId.ToString() == o.Id) is { } d ? DuelValue(a, d) : -99;
        return choice.Options.OrderByDescending(Score).ThenBy(o => o.Id == "SKIP" ? 0 : 1).First();
    }
}
