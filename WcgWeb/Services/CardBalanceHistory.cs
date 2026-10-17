using System.Text.Json;
using WcgWeb.Models;

namespace WcgWeb.Services;

internal static class CardBalanceHistory
{
    // Catalog before the rules simplification (Holy Shield refresh, fixed 200–204 effects, 3 PP thresholds, attached 143/165/168).
    // Catalog before the PP/cost deflation (PR B); only PP and cost changed, effects and DP did not.
    // Catalog before the new cards 205–257 (PR D); existing cards are unchanged, so the old catalog is the current one without them.
    // Catalog before ability monsters were lowered below the vanilla PP curve (2026-10-16); only PP and design notes changed.
    // Catalog before 寂靜學者 (WCG-208, monster with an entry silence) became the attached spell 寂靜禁咒 (2026-10-16).
    // Catalog before seven single-will cards (027, 031, 058, 067, 211, 213, 234) became dual-will (2026-10-16); only wills and design notes changed.
    // Catalog before the 理智 buff (2026-10-17): ten 理智 cards had other numbers or text, and 258–262 did not exist.
    // Catalog before 聖盾 became the one-time 聖盾指示物 (2026-10-17): holy-shield cards had the per-turn wording.
    // Catalog before four cards cost 1 more (2026-10-17): 013, 018, 100 and 182 had their old cost and PP; text and DP did not change.
    // Catalog before the 聖盾 PP restore (2026-10-17): eleven holy-shield cards still carried the per-turn-shield PP cut; only PP changed.
    internal static bool ShieldCounterCatalog(CardDatabase cards) => cards.GetCard("WCG-205")?.Text.Contains("聖盾指示物", StringComparison.Ordinal) == true;
    internal static bool IsReasonBuffCard(string id) => id.Length == 7 && id.StartsWith("WCG-", StringComparison.Ordinal) && int.TryParse(id.AsSpan(4), out var n) && n >= 258;
    // PR E put new cards into the v0.6 深淵 preset; the older catalogs keep its original list (249 was 128, 250 and 212 were 169).
    private static readonly Dictionary<string, string> PresetCardBeforeNewCards = new() { ["WCG-249"] = "WCG-128", ["WCG-250"] = "WCG-169", ["WCG-212"] = "WCG-169" };
    internal static string PresetsBeforeNewCards(IEnumerable<Deck> presets) => JsonSerializer.Serialize(presets.Select(d =>
    {
        var copy = JsonSerializer.Deserialize<Deck>(JsonSerializer.Serialize(d))!;
        copy.CardIds = copy.CardIds.Select(id => PresetCardBeforeNewCards.GetValueOrDefault(id, id)).ToList();
        return copy;
    }));
    internal static bool IsNewCard(string id) => id.Length == 7 && id.StartsWith("WCG-", StringComparison.Ordinal) && int.TryParse(id.AsSpan(4), out var n) && n >= 205;
    internal static bool DeflatedCatalog(CardDatabase cards) => cards.GetCard("WCG-161")?.PP < 4000;
    internal static bool SimplifiedCatalog(CardDatabase cards) => cards.GetCard("WCG-200")?.Text.StartsWith("你受到傷害時", StringComparison.Ordinal) == true;

    // One step per balance update, newest first: each turns the catalog after that update into the catalog before it.
    // Before*(current) applies every step from the newest down to its own, so EraSteps[k] applied to the result for
    // EraSteps[k-1] gives exactly Before*(current) for era k (RankedSession builds its eras this way, one step each).
    // 7-cost 瑪爾加尼斯 identifies the cost-up update (地獄戰狂 also cost 5 before the October 13 balance, so it cannot).
    // 500-PP 聖印侍從 together with 2400-PP 日耀聖堂巨像 identifies the 聖盾 PP restore (063 was 300 from the deflation on, 800 before it).
    internal static bool ShieldPpCatalog(CardDatabase cards) => cards.GetCard("WCG-063")?.PP == 500 && cards.GetCard("WCG-079")?.PP == 2400;
    private static CardDatabase ShieldPpStep(CardDatabase c) => ShieldPpCatalog(c) ? Override(c, "WcgWeb.Balance.BeforeShieldPp") : c;
    internal static bool CostUpCatalog(CardDatabase cards) => cards.GetCard("WCG-100")?.TotalCost == 7;
    private static CardDatabase CostUpStep(CardDatabase c) => CostUpCatalog(c) ? Override(c, "WcgWeb.Balance.BeforeCostUp") : c;
    private static CardDatabase ShieldCounterStep(CardDatabase c) => ShieldCounterCatalog(c) ? Override(c, "WcgWeb.Balance.BeforeShieldCounter") : c;
    private static CardDatabase ReasonBuffStep(CardDatabase c)
    {
        if (c.GetCard("WCG-251")?.HasTaunt != true) return c;
        var old = Override(c, "WcgWeb.Balance.BeforeReasonBuff");
        return new(JsonSerializer.Serialize(old.AllCards.Where(x => !IsReasonBuffCard(x.Id))), JsonSerializer.Serialize(old.PresetDecks));
    }
    private static CardDatabase DualWillStep(CardDatabase c) => c.GetCard("WCG-031")?.IsDual == true ? Override(c, "WcgWeb.Balance.BeforeDualWill") : c;
    private static CardDatabase SilenceSpellStep(CardDatabase c) => c.GetCard("WCG-208")?.IsSpell == true ? Override(c, "WcgWeb.Balance.BeforeSilenceSpell") : c;
    private static CardDatabase AbilityPpStep(CardDatabase c) => c.GetCard("WCG-113")?.PP < 2000 ? Override(c, "WcgWeb.Balance.BeforeAbilityPp") : c;
    private static CardDatabase NewCardsStep(CardDatabase c) => !c.AllCards.Any(x => IsNewCard(x.Id)) ? c
        : new(JsonSerializer.Serialize(c.AllCards.Where(x => !IsNewCard(x.Id))), PresetsBeforeNewCards(c.PresetDecks));
    private static CardDatabase DeflationStep(CardDatabase c) => DeflatedCatalog(c) ? Override(c, "WcgWeb.Balance.BeforeDeflation") : c;
    private static CardDatabase RulesSimplificationStep(CardDatabase c) => SimplifiedCatalog(c) ? Override(c, "WcgWeb.Balance.BeforeRulesSimplification") : c;
    private static CardDatabase EnergyKeywordsStep(CardDatabase c) => c.GetCard("WCG-201") == null ? c
        : new(JsonSerializer.Serialize(c.AllCards.Where(x => x.Id is not ("WCG-201" or "WCG-202" or "WCG-203" or "WCG-204"))), JsonSerializer.Serialize(c.PresetDecks));
    private static CardDatabase HeroGuardStep(CardDatabase c) => c.GetCard("WCG-200") == null ? c
        : new(JsonSerializer.Serialize(c.AllCards.Where(x => x.Id != "WCG-200")), JsonSerializer.Serialize(c.PresetDecks));
    private static CardDatabase StrongBuildAdjustmentStep(CardDatabase c) => c.GetCard("WCG-194")?.TotalCost == 5 && c.GetCard("WCG-079")?.PP == 3600
        ? Override(c, "WcgWeb.Balance.BeforeStrongBuildAdjustment") : c;
    // The 7-cost molten giant identifies this numeric update; older catalogs keep their own values.
    private static CardDatabase OctoberBalanceStep(CardDatabase c) => c.GetCard("WCG-160")?.TotalCost == 7 ? Override(c, "WcgWeb.Balance.BeforeOctoberBalance") : c;
    private static CardDatabase EnergyShieldStep(CardDatabase c) => c.AllCards.Any(x => x.Text.Contains("能量護盾", StringComparison.Ordinal) || x.Keywords.Contains("能量護盾"))
        ? Override(c, "WcgWeb.Balance.BeforeEnergyShield") : c;
    private static CardDatabase DamageCountersStep(CardDatabase c) => Override(c, "WcgWeb.Balance.BeforeDamageCounters");
    private static CardDatabase PlayerTargetsStep(CardDatabase c) => Override(c, "WcgWeb.Balance.BeforePlayerTargets");
    private static CardDatabase DiversityStep(CardDatabase c) => Override(c, "WcgWeb.Balance.BeforeDiversity");
    // Catalog before the card-text clarity update (152 cards reworded; 147 136 178 141 changed effect, 147 PP 1200→1000).
    private static CardDatabase CardTextStep(CardDatabase c) => Override(c, "WcgWeb.Balance.BeforeCardText");
    // Catalog before the arrow-card expansion (9 cards gained positional arrows and some PP changed).
    // Applied on top of the pre-card-text catalog so its fingerprint matches the original release.
    private static CardDatabase ArrowCardsStep(CardDatabase c) => Override(c, "WcgWeb.Balance.BeforeArrowCards");
    // Catalog before faction triggers became unlimited. Applied on top of the pre-arrow catalog so its fingerprint matches the original release.
    private static CardDatabase UnlimitedFactionTriggersStep(CardDatabase c) => Override(c, "WcgWeb.Balance.BeforeUnlimitedFactionTriggers");

    internal static readonly Func<CardDatabase, CardDatabase>[] EraSteps =
    [
        ShieldPpStep, CostUpStep, ShieldCounterStep, ReasonBuffStep, DualWillStep, SilenceSpellStep, AbilityPpStep, NewCardsStep, DeflationStep, RulesSimplificationStep,
        EnergyKeywordsStep, HeroGuardStep, StrongBuildAdjustmentStep, OctoberBalanceStep, EnergyShieldStep, DamageCountersStep, PlayerTargetsStep,
        DiversityStep, CardTextStep, ArrowCardsStep, UnlimitedFactionTriggersStep
    ];

    internal static CardDatabase BeforeShieldPp(CardDatabase current) => ShieldPpStep(current);
    internal static CardDatabase BeforeCostUp(CardDatabase current) => CostUpStep(BeforeShieldPp(current));
    internal static CardDatabase BeforeShieldCounter(CardDatabase current) => ShieldCounterStep(BeforeCostUp(current));
    internal static CardDatabase BeforeReasonBuff(CardDatabase current) => ReasonBuffStep(BeforeShieldCounter(current));
    internal static CardDatabase BeforeDualWill(CardDatabase current) => DualWillStep(BeforeReasonBuff(current));
    internal static CardDatabase BeforeSilenceSpell(CardDatabase current) => SilenceSpellStep(BeforeDualWill(current));
    internal static CardDatabase BeforeAbilityPp(CardDatabase current) => AbilityPpStep(BeforeSilenceSpell(current));
    internal static CardDatabase BeforeNewCards(CardDatabase current) => NewCardsStep(BeforeAbilityPp(current));
    internal static CardDatabase BeforeDeflation(CardDatabase current) => DeflationStep(BeforeNewCards(current));
    internal static CardDatabase BeforeRulesSimplification(CardDatabase current) => RulesSimplificationStep(BeforeDeflation(current));
    internal static CardDatabase BeforeEnergyKeywords(CardDatabase current) => EnergyKeywordsStep(BeforeRulesSimplification(current));
    internal static CardDatabase BeforeHeroGuard(CardDatabase current) => HeroGuardStep(BeforeEnergyKeywords(current));
    internal static CardDatabase BeforeStrongBuildAdjustment(CardDatabase current) => StrongBuildAdjustmentStep(BeforeHeroGuard(current));
    internal static CardDatabase BeforeOctoberBalance(CardDatabase current) => OctoberBalanceStep(BeforeStrongBuildAdjustment(current));
    internal static CardDatabase BeforeEnergyShield(CardDatabase current) => EnergyShieldStep(BeforeOctoberBalance(current));
    internal static CardDatabase BeforeDamageCounters(CardDatabase current) => DamageCountersStep(BeforeEnergyShield(current));
    internal static CardDatabase BeforePlayerTargets(CardDatabase current) => PlayerTargetsStep(BeforeDamageCounters(current));
    internal static CardDatabase BeforeDiversity(CardDatabase current) => DiversityStep(BeforePlayerTargets(current));
    internal static CardDatabase BeforeCardText(CardDatabase current) => CardTextStep(BeforeDiversity(current));
    internal static CardDatabase BeforeArrowCards(CardDatabase current) => ArrowCardsStep(BeforeCardText(current));
    internal static CardDatabase BeforeUnlimitedFactionTriggers(CardDatabase current) => UnlimitedFactionTriggersStep(BeforeArrowCards(current));

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
