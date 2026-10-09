using System.Text.Json;
using WcgWeb.Models;

namespace WcgWeb.Services;

internal static class CardBalanceHistory
{
    // Catalog before the arrow-card expansion (9 cards gained positional arrows and some PP changed).
    internal static CardDatabase BeforeArrowCards(CardDatabase current) => Override(current, "WcgWeb.Balance.BeforeArrowCards");

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
