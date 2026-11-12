using WcgWeb.Models;
namespace WcgTests;

// 2026-11-12 指示物改用牌組裡的卡: tests set up face-down padding the way a player would — cards taken from the bottom of
// the controller's deck and put under the card.
internal static class DeckCardsTestKit
{
    internal static MonsterInstance Pad(this MonsterInstance m, PlayerState from, UnderKind kind, int n)
    {
        for (var i = 0; i < n; i++)
        {
            if (from.Deck.Count == 0) from.Deck.Add(new(from.Hand.Concat(from.Graveyard).FirstOrDefault()?.Card ?? m.Card));
            var c = from.Deck[^1]; from.Deck.RemoveAt(from.Deck.Count - 1); m.Under.Add(new(c, from.Id, kind));
        }
        return m;
    }
}
