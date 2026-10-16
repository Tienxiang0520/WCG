using System.Text.Json;
using WcgWeb.Models;

namespace WcgWeb.Services;

internal static class CardBalanceHistory
{
    // Catalog before the rules simplification (Holy Shield refresh, fixed 200–204 effects, 3 PP thresholds, attached 143/165/168).
    // Catalog before the PP/cost deflation (PR B); only PP and cost changed, effects and DP did not.
    // Catalog before the new cards 205–257 (PR D); existing cards are unchanged, so the old catalog is the current one without them.
    internal static CardDatabase BeforeNewCards(CardDatabase current) => !current.AllCards.Any(c => IsNewCard(c.Id)) ? current
        : new(JsonSerializer.Serialize(current.AllCards.Where(c => !IsNewCard(c.Id))), PresetsBeforeNewCards(current.PresetDecks));
    // PR E put new cards into the v0.6 深淵 preset; the older catalogs keep its original list (249 was 128, 250 and 212 were 169).
    private static readonly Dictionary<string, string> PresetCardBeforeNewCards = new() { ["WCG-249"] = "WCG-128", ["WCG-250"] = "WCG-169", ["WCG-212"] = "WCG-169" };
    internal static string PresetsBeforeNewCards(IEnumerable<Deck> presets) => JsonSerializer.Serialize(presets.Select(d =>
    {
        var copy = JsonSerializer.Deserialize<Deck>(JsonSerializer.Serialize(d))!;
        copy.CardIds = copy.CardIds.Select(id => PresetCardBeforeNewCards.GetValueOrDefault(id, id)).ToList();
        return copy;
    }));
    internal static bool IsNewCard(string id) => id.Length == 7 && id.StartsWith("WCG-", StringComparison.Ordinal) && int.TryParse(id.AsSpan(4), out var n) && n >= 205;
    internal static CardDatabase BeforeDeflation(CardDatabase current) { current = BeforeNewCards(current); return DeflatedCatalog(current)
        ? Override(current, "WcgWeb.Balance.BeforeDeflation") : current; }
    internal static bool DeflatedCatalog(CardDatabase cards) => cards.GetCard("WCG-161")?.PP < 4000;
    internal static CardDatabase BeforeRulesSimplification(CardDatabase current) { current = BeforeDeflation(current); return SimplifiedCatalog(current)
        ? Override(current, "WcgWeb.Balance.BeforeRulesSimplification") : current; }
    internal static bool SimplifiedCatalog(CardDatabase cards) => cards.GetCard("WCG-200")?.Text.StartsWith("你受到傷害時", StringComparison.Ordinal) == true;
    internal static CardDatabase BeforeEnergyKeywords(CardDatabase current) { current = BeforeRulesSimplification(current); return current.GetCard("WCG-201") == null ? current
        : new(JsonSerializer.Serialize(current.AllCards.Where(c => c.Id is not ("WCG-201" or "WCG-202" or "WCG-203" or "WCG-204"))), JsonSerializer.Serialize(current.PresetDecks)); }
    internal static CardDatabase BeforeHeroGuard(CardDatabase current)
    {
        current=BeforeEnergyKeywords(current);
        return current.GetCard("WCG-200") == null ? current
            : new(JsonSerializer.Serialize(current.AllCards.Where(c => c.Id != "WCG-200")), JsonSerializer.Serialize(current.PresetDecks));
    }
    internal static CardDatabase BeforeStrongBuildAdjustment(CardDatabase current)
    {
        current = BeforeHeroGuard(current);
        return current.GetCard("WCG-194")?.TotalCost == 5 && current.GetCard("WCG-079")?.PP == 3600
            ? Override(current, "WcgWeb.Balance.BeforeStrongBuildAdjustment") : current;
    }
    // The 7-cost molten giant identifies this numeric update; older catalogs keep their own values.
    internal static CardDatabase BeforeOctoberBalance(CardDatabase current)
    {
        current = BeforeStrongBuildAdjustment(current);
        return current.GetCard("WCG-160")?.TotalCost == 7
            ? Override(current, "WcgWeb.Balance.BeforeOctoberBalance") : current;
    }
    internal static CardDatabase BeforeEnergyShield(CardDatabase current)
    {
        current = BeforeOctoberBalance(current);
        return current.AllCards.Any(c => c.Text.Contains("能量護盾", StringComparison.Ordinal) || c.Keywords.Contains("能量護盾"))
            ? Override(current, "WcgWeb.Balance.BeforeEnergyShield") : current;
    }
    internal static CardDatabase BeforeDamageCounters(CardDatabase current) => Override(BeforeEnergyShield(current), "WcgWeb.Balance.BeforeDamageCounters");
    internal static CardDatabase BeforePlayerTargets(CardDatabase current) => Override(BeforeDamageCounters(current), "WcgWeb.Balance.BeforePlayerTargets");
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
