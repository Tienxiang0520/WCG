using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using WcgWeb.Models;
namespace WcgWeb.Services;

// AI v7 card roles, hold values and the opponent hand model (ai-design.md §2.4–2.6).
// Roles are read from card text, so new cards are understood without per-id tables.
// The opponent model uses only public information: cards the opponent has shown (board, face-up structures,
// graveyard) select the wills it plays, the catalog gives how common each role is, and the opponent's
// hand count gives how many draws it has. It never reads the opponent's hand or deck.
public partial class GameEngine
{
    [Flags] internal enum AiRole { None = 0, Removal = 1, Wipe = 2, Burn = 4, Counter = 8, Draw = 16, Heal = 32 }
    private static readonly ConcurrentDictionary<string, AiRole> _roles = new(StringComparer.Ordinal);
    internal static AiRole RoleOf(CardDefinition c) => _roles.GetOrAdd(c.Id + "\u0001" + c.Type + "\u0001" + c.Text, _ =>
    {
        var t = c.Text; var r = AiRole.None;
        if (Regex.IsMatch(t, "消滅(雙方)?所有")) r |= AiRole.Wipe;
        else if (c.IsSpell && !c.IsCounter && Regex.IsMatch(t, @"消滅\s*1\s*隻")) r |= AiRole.Removal;
        if (CardReach(c) > 0 && !c.IsMonster) r |= AiRole.Burn;
        if (c.IsCounter) r |= AiRole.Counter;
        if (Regex.IsMatch(t, @"抽\s*\d+\s*張")) r |= AiRole.Draw;
        if (Regex.IsMatch(t, @"回復(己方|其)?\s*\d+\s*點生命")) r |= AiRole.Heal;
        return r;
    });

    // Value of keeping a card in hand (ai-design.md §2.4): playing it must beat keeping it.
    internal double HoldValue(PlayerState p, CardInstance c)
    {
        var e = GetOpponent(p); var card = c.Card; var role = RoleOf(card);
        double value = CardValue(card) * .55;
        bool lateGame = e.Hand.Count + e.Deck.Count <= 8;
        if (role.HasFlag(AiRole.Removal)) value = Math.Max(value, (4.2 + card.TotalCost * .3) * (lateGame ? .6 : 1));
        if (role.HasFlag(AiRole.Wipe) && card.IsSpell) value = Math.Max(value, 4.5 + card.TotalCost * .3);
        if (role.HasFlag(AiRole.Counter)) value = Math.Max(value, 3.5 + (e.Field.Any(m => m.CurrentDP > 0) ? 1.5 : 0));
        if (role.HasFlag(AiRole.Burn)) { var reach = CardReach(card); value = Math.Max(value, reach * (e.Hp <= reach + 2 ? 4 : 1.5) + 1); }
        if (card.IsMonster && card.HasCharge) value += 1;
        if (ActualCost(p, card) > p.TotalEnergy + 2) value -= 1;
        return value;
    }

    internal sealed record OpponentOdds(double Wipe, double Burn, double Counter, double Removal);
    private static readonly ConcurrentDictionary<(int, string), (double wipe, double burn, double counter, double removal)> _poolRates = new();

    // Chance that the opponent's hidden hand holds each role, from the wills it has shown and its hand size.
    internal OpponentOdds OpponentModel(PlayerState e)
    {
        var shown = e.Field.Select(m => m.Card).Concat(e.Structures.Where(s => !s.IsSet).Select(s => s.Card)).Concat(e.Graveyard.Select(g => g.Card));
        var wills = shown.SelectMany(c => c.FactionWills).Distinct().OrderBy(w => w, StringComparer.Ordinal).ToArray();
        var key = (System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_cardDb), string.Join(",", wills));
        var rates = _poolRates.GetOrAdd(key, _ =>
        {
            var pool = _cardDb.AllCards.Where(c => wills.Length == 0 || c.IsNeutral || c.FactionWills.All(w => wills.Contains(w))).ToList();
            if (pool.Count == 0) return (0, 0, 0, 0);
            double Rate(AiRole r) => (double)pool.Count(c => RoleOf(c).HasFlag(r)) / pool.Count;
            return (Rate(AiRole.Wipe), pool.Where(c => RoleOf(c).HasFlag(AiRole.Burn)).Sum(c => (double)CardReach(c)) / pool.Count, Rate(AiRole.Counter), Rate(AiRole.Removal));
        });
        int n = e.Hand.Count;
        double Any(double q) => 1 - Math.Pow(1 - Math.Clamp(q, 0, 1), n);
        return new(Any(rates.wipe), Math.Min(3, rates.burn * n * .6), Any(rates.counter), Any(rates.removal));
    }
}
