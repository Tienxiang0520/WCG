using WcgWeb.Models;
namespace WcgWeb.Services;

// AI v7 position evaluation (ai-design.md §2.3). Scores a settled position for one side using only what
// that side may know: both boards, HP, energy, graveyards, its own hand, and the opponent's hand/deck counts.
public partial class GameEngine
{
    internal const double Win = 1_000_000;

    // HP is worth more as it runs low: the last points decide the game.
    private static double HpValue(int hp) => hp <= 0 ? -200 : hp * 4 + (hp <= 3 ? -(4 - hp) * 3 : 0);

    internal double Evaluate(string meId)
    {
        var p = PlayerById(meId); var e = GetOpponent(p);
        if (e.HasLost && !p.HasLost) return Win;
        if (p.HasLost) return -Win;
        double score = HpValue(p.Hp) - HpValue(e.Hp);
        score += p.Field.Sum(BodyValue) - e.Field.Sum(BodyValue);
        score += p.Structures.Sum(StructureValue) - e.Structures.Sum(StructureValue);
        score += p.Hand.Sum(c => HoldValue(p, c)) - e.Hand.Count * 1.6;
        score += (p.TotalEnergy - e.TotalEnergy) * .8;
        if (p.Deck.Count == 0) score -= 12;
        if (e.Deck.Count == 0) score += 12;
        score -= Exposure(p, e);
        score += Pressure(p, e);
        return score;
    }

    private double StructureValue(MonsterInstance m) => m.IsSet ? 2.5 : 2 + m.Card.TotalCost * .4;

    // Value of keeping a card in hand. Phase 2 uses a flat share of the card's value; later phases refine it.
    internal double HoldValue(PlayerState p, CardInstance c)
    {
        var value = CardValue(c.Card) * .55;
        if (ActualCost(p, c.Card) > p.TotalEnergy + 2) value -= 1;
        return value;
    }

    // Damage the opponent's current board can deal to us next turn, after our taunts absorb attackers.
    internal int Incoming(PlayerState p, PlayerState e)
    {
        var attackers = e.Field.Where(m => !m.AttackLocked && (m.IsSilenced || !m.Card.CannotAttack) && m.CurrentDP > 0).Select(m => m.CurrentDP).OrderBy(x => x).ToList();
        int taunts = p.Field.Count(m => m.IsTaunt);
        var through = attackers.Skip(taunts).Sum();
        return Math.Max(0, through - GuardReduction(p, through));
    }

    private double Exposure(PlayerState p, PlayerState e)
    {
        var incoming = Incoming(p, e) + ExpectedBurn(p, e);
        if (incoming >= p.Hp) return 30;
        return incoming * 1.2;
    }

    // Our own threat for next turn: board damage the opponent must answer.
    private double Pressure(PlayerState p, PlayerState e)
    {
        var mine = p.Field.Where(m => !m.AttackLocked && (m.IsSilenced || !m.Card.CannotAttack) && m.CurrentDP > 0).Select(m => m.CurrentDP).OrderBy(x => x).ToList();
        var through = mine.Skip(e.Field.Count(m => m.IsTaunt)).Sum();
        return through >= e.Hp ? 6 : through * .5;
    }

    // Expected burn from the opponent's hidden hand; refined by the opponent model in phase 5.
    internal double ExpectedBurn(PlayerState p, PlayerState e) => 0;
}
