using System.Text.Json;
using WcgWeb.Models;

namespace WcgWeb.Services;

internal static class CardBalanceHistory
{
    internal static CardDatabase BeforeUnlimitedFactionTriggers(CardDatabase current)
    {
        using var source = typeof(CardBalanceHistory).Assembly.GetManifestResourceStream("WcgWeb.Balance.BeforeUnlimitedFactionTriggers")
            ?? throw new InvalidDataException("舊卡牌平衡資料缺失。");
        var prior = JsonSerializer.Deserialize<List<CardDefinition>>(source)!
            .ToDictionary(c => c.Id);
        // Clone definitions: a resumed old match must never alter the live gallery or new matches.
        return new(JsonSerializer.Serialize(current.AllCards.Select(c => prior.GetValueOrDefault(c.Id, c))),
            JsonSerializer.Serialize(current.PresetDecks));
    }
}
