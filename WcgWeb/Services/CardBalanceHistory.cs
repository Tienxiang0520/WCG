using System.Text.Json;
using WcgWeb.Models;

namespace WcgWeb.Services;

internal static class CardBalanceHistory
{
    internal static CardDatabase BeforePlayerTargets(CardDatabase current) => Override(current, "WcgWeb.Balance.BeforePlayerTargets");
    internal static CardDatabase BeforeDiversity(CardDatabase current) => Override(BeforePlayerTargets(current), "WcgWeb.Balance.BeforeDiversity");
    // Catalog before the card-text clarity update (152 cards reworded; 147 136 178 141 changed effect, 147 PP 1200→1000).
    internal static CardDatabase BeforeCardText(CardDatabase current) => Override(BeforeDiversity(current), "WcgWeb.Balance.BeforeCardText");

    // Catalog before the arrow-card expansion (9 cards gained positional arrows and some PP changed).
    // Applied on top of the pre-card-text catalog so its fingerprint matches the original release.
    internal static CardDatabase BeforeArrowCards(CardDatabase current) => Override(BeforeCardText(current), "WcgWeb.Balance.BeforeArrowCards");

    // Catalog before faction triggers became unlimited. Applied on top of the pre-arrow catalog so its fingerprint matches the original release.
    internal static CardDatabase BeforeUnlimitedFactionTriggers(CardDatabase current)
        => Override(BeforeArrowCards(current), "WcgWeb.Balance.BeforeUnlimitedFactionTriggers");

    private static CardDatabase Override(CardDatabase current, string resource)
    {
        using var source = typeof(CardBalanceHistory).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidDataException("舊卡牌平衡資料缺失。");
        var prior = JsonSerializer.Deserialize<List<CardDefinition>>(source)!
            .ToDictionary(c => c.Id);
        // Clone definitions: a resumed old match must never alter the live gallery or new matches.
        return new(JsonSerializer.Serialize(current.AllCards.Select(c => prior.GetValueOrDefault(c.Id, c))),
            JsonSerializer.Serialize(current.PresetDecks));
    }
}
