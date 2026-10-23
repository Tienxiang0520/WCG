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
    // Catalog before 聖盾 was spent in every combat (2026-10-23): an attacker only lost a counter when it would be destroyed.
    // Only design notes of the nine native holy-shield cards changed; the note marks the catalog.
    internal static bool ShieldAnyCombatCatalog(CardDatabase cards) => cards.GetCard("WCG-063")?.DesignNote?.Contains("聖盾交戰必扣", StringComparison.Ordinal) == true;
    private static CardDatabase ShieldAnyCombatStep(CardDatabase c) => ShieldAnyCombatCatalog(c) ? Override(c, "WcgWeb.Balance.BeforeShieldAnyCombat") : c;
    internal static bool CardFixesCatalog(CardDatabase cards) => cards.GetCard("WCG-003")?.DP == 0;
    private static CardDatabase CardFixesStep(CardDatabase c) => CardFixesCatalog(c) ? Override(c, "WcgWeb.Balance.BeforeCardFixes") : c;
    // Catalog before the proportional direct-damage cost update (2026-10-18, scheme B): 006, 014, 013, 123, 170, 059 and 144
    // cost less (monsters keep their offset below vanilla); text and DP did not change.
    // 4-cost 怒意沸騰 identifies it (006 cost 1 or 2 in every older catalog).
    internal static bool BurnCostCatalog(CardDatabase cards) => cards.GetCard("WCG-006")?.TotalCost == 4;
    private static CardDatabase BurnCostStep(CardDatabase c) => BurnCostCatalog(c) ? Override(c, "WcgWeb.Balance.BeforeBurnCost") : c;
    // Catalog before the taunt+shield / 狂怒 face-damage / 衝鋒 3-cost update (2026-10-17): six taunt+shield cards had higher PP,
    // twelve 狂怒 cards that damage the enemy player directly cost 1 less, and 突擊狼騎兵 (005) cost 2; text and DP did not change.
    // 3-cost 突擊狼騎兵 identifies it (005 has cost 2 in every older catalog).
    internal static bool TauntFaceChargeCatalog(CardDatabase cards) => cards.GetCard("WCG-005")?.TotalCost == 3;
    private static CardDatabase TauntFaceChargeStep(CardDatabase c) => TauntFaceChargeCatalog(c) ? Override(c, "WcgWeb.Balance.BeforeTauntFaceCharge") : c;
    // Catalog before 幸運幣 (WCG-263) existed (2026-10-17); nothing else changed.
    internal static bool LuckyCoinCatalog(CardDatabase cards) => cards.GetCard("WCG-263") != null;
    private static CardDatabase LuckyCoinStep(CardDatabase c) => !LuckyCoinCatalog(c) ? c
        : new(JsonSerializer.Serialize(c.AllCards.Where(x => x.Id != "WCG-263")), JsonSerializer.Serialize(c.PresetDecks));
    // Catalog before 冰霜魔導師 (029) and 時間靜止者 (035) worked directly on entry (2026-10-17); only their text changed.
    internal static bool MageDirectCatalog(CardDatabase cards) => cards.GetCard("WCG-029")?.Text.StartsWith("進場", StringComparison.Ordinal) == true;
    private static CardDatabase MageDirectStep(CardDatabase c) => MageDirectCatalog(c) ? Override(c, "WcgWeb.Balance.BeforeMageDirect") : c;
    internal static bool ShieldPpCatalog(CardDatabase cards) => cards.GetCard("WCG-063")?.PP >= 500 && cards.GetCard("WCG-079")?.PP is 2000 or 2400;
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

    // Catalog before 巨熊咆哮 (WCG-048) was reworded (2026-10-23, PR #63): only its text changed; the new wording marks the catalog.
    internal static bool BearRoarTextCatalog(CardDatabase cards) => cards.GetCard("WCG-048")?.Text?.Contains("己方場上怪物數 × 500", StringComparison.Ordinal) == true;
    private static CardDatabase BearRoarTextStep(CardDatabase c) => BearRoarTextCatalog(c) ? Override(c, "WcgWeb.Balance.BeforeBearRoarText") : c;

    // Catalog before every printed-PP check said 「印刷 PP」 (2026-10-23): only the text of 39 cards changed; 暗影裂箭's new wording marks the catalog.
    internal static bool PrintedPpTextCatalog(CardDatabase cards) => cards.GetCard("WCG-082")?.Text?.Contains("印刷 PP", StringComparison.Ordinal) == true;
    private static CardDatabase PrintedPpTextStep(CardDatabase c) => PrintedPpTextCatalog(c) ? Override(c, "WcgWeb.Balance.BeforePrintedPpText") : c;

    // Catalog before the 誓劍決鬥 archetype (WCG-264–272, 2026-10-23) existed; nothing else changed.
    internal static bool IsOathDuelCard(string id) => id.Length == 7 && id.StartsWith("WCG-", StringComparison.Ordinal) && int.TryParse(id.AsSpan(4), out var n) && n is >= 264 and <= 272;
    private static CardDatabase OathDuelCardsStep(CardDatabase c) => c.GetCard("WCG-264") == null ? c
        : new(JsonSerializer.Serialize(c.AllCards.Where(x => !IsOathDuelCard(x.Id))), JsonSerializer.Serialize(c.PresetDecks));

    // Catalog before the 腐萎侵蝕 archetype (WCG-283–291, 2026-10-23) existed; nothing else changed.
    internal static bool IsRotErosionCard(string id) => id.Length == 7 && id.StartsWith("WCG-", StringComparison.Ordinal) && int.TryParse(id.AsSpan(4), out var n) && n is >= 283 and <= 291;
    private static CardDatabase RotErosionCardsStep(CardDatabase c) => c.GetCard("WCG-283") == null ? c
        : new(JsonSerializer.Serialize(c.AllCards.Where(x => !IsRotErosionCard(x.Id))), JsonSerializer.Serialize(c.PresetDecks));

    // Catalog before the 翠語夾縫 archetype and its answer card (WCG-273–282, 2026-10-23) existed; nothing else changed.
    internal static bool IsThresholdGapCard(string id) => id.Length == 7 && id.StartsWith("WCG-", StringComparison.Ordinal) && int.TryParse(id.AsSpan(4), out var n) && n is >= 273 and <= 282;
    private static CardDatabase ThresholdGapCardsStep(CardDatabase c) => c.GetCard("WCG-273") == null ? c
        : new(JsonSerializer.Serialize(c.AllCards.Where(x => !IsThresholdGapCard(x.Id))), JsonSerializer.Serialize(c.PresetDecks));

    // Catalog before the 烽火守望 archetype (WCG-292–300, 2026-10-23) existed; nothing else changed.
    internal static bool IsBeaconWatchCard(string id) => id.Length == 7 && id.StartsWith("WCG-", StringComparison.Ordinal) && int.TryParse(id.AsSpan(4), out var n) && n is >= 292 and <= 300;
    private static CardDatabase BeaconWatchCardsStep(CardDatabase c) => c.GetCard("WCG-292") == null ? c
        : new(JsonSerializer.Serialize(c.AllCards.Where(x => !IsBeaconWatchCard(x.Id))), JsonSerializer.Serialize(c.PresetDecks));

    // Catalog before the 伏能喚醒 archetype (WCG-301–309, 2026-10-23) existed; nothing else changed.
    internal static bool IsEnergyVaultCard(string id) => id.Length == 7 && id.StartsWith("WCG-", StringComparison.Ordinal) && int.TryParse(id.AsSpan(4), out var n) && n is >= 301 and <= 309;
    private static CardDatabase EnergyVaultCardsStep(CardDatabase c) => c.GetCard("WCG-301") == null ? c
        : new(JsonSerializer.Serialize(c.AllCards.Where(x => !IsEnergyVaultCard(x.Id))), JsonSerializer.Serialize(c.PresetDecks));

    // Catalog before the 焚能爆破 archetype (WCG-310–318, 2026-10-23) existed; nothing else changed.
    internal static bool IsEnergyBurnCard(string id) => id.Length == 7 && id.StartsWith("WCG-", StringComparison.Ordinal) && int.TryParse(id.AsSpan(4), out var n) && n is >= 310 and <= 318;
    private static CardDatabase EnergyBurnCardsStep(CardDatabase c) => c.GetCard("WCG-310") == null ? c
        : new(JsonSerializer.Serialize(c.AllCards.Where(x => !IsEnergyBurnCard(x.Id))), JsonSerializer.Serialize(c.PresetDecks));

    internal static readonly Func<CardDatabase, CardDatabase>[] EraSteps =
    [
        EnergyBurnCardsStep, EnergyVaultCardsStep, BeaconWatchCardsStep, RotErosionCardsStep, ThresholdGapCardsStep, OathDuelCardsStep, PrintedPpTextStep, ShieldAnyCombatStep, BearRoarTextStep, CardFixesStep, BurnCostStep, TauntFaceChargeStep, LuckyCoinStep, MageDirectStep, ShieldPpStep, CostUpStep, ShieldCounterStep, ReasonBuffStep, DualWillStep, SilenceSpellStep, AbilityPpStep, NewCardsStep, DeflationStep, RulesSimplificationStep,
        EnergyKeywordsStep, HeroGuardStep, StrongBuildAdjustmentStep, OctoberBalanceStep, EnergyShieldStep, DamageCountersStep, PlayerTargetsStep,
        DiversityStep, CardTextStep, ArrowCardsStep, UnlimitedFactionTriggersStep
    ];

    internal static CardDatabase BeforeEnergyBurnCards(CardDatabase current) => EnergyBurnCardsStep(current);
    internal static CardDatabase BeforeEnergyVaultCards(CardDatabase current) => EnergyVaultCardsStep(BeforeEnergyBurnCards(current));
    internal static CardDatabase BeforeBeaconWatchCards(CardDatabase current) => BeaconWatchCardsStep(BeforeEnergyVaultCards(current));
    internal static CardDatabase BeforeRotErosionCards(CardDatabase current) => RotErosionCardsStep(BeforeBeaconWatchCards(current));
    internal static CardDatabase BeforeThresholdGapCards(CardDatabase current) => ThresholdGapCardsStep(BeforeRotErosionCards(current));
    internal static CardDatabase BeforeOathDuelCards(CardDatabase current) => OathDuelCardsStep(BeforeThresholdGapCards(current));
    internal static CardDatabase BeforePrintedPpText(CardDatabase current) => PrintedPpTextStep(BeforeOathDuelCards(current));
    internal static CardDatabase BeforeShieldAnyCombat(CardDatabase current) => ShieldAnyCombatStep(BeforePrintedPpText(current));
    internal static CardDatabase BeforeBearRoarText(CardDatabase current) => BearRoarTextStep(BeforeShieldAnyCombat(current));
    internal static CardDatabase BeforeCardFixes(CardDatabase current) => CardFixesStep(BeforeBearRoarText(current));
    internal static CardDatabase BeforeBurnCost(CardDatabase current) => BurnCostStep(BeforeCardFixes(current));
    internal static CardDatabase BeforeTauntFaceCharge(CardDatabase current) => TauntFaceChargeStep(BeforeBurnCost(current));
    internal static CardDatabase BeforeLuckyCoin(CardDatabase current) => LuckyCoinStep(BeforeTauntFaceCharge(current));
    internal static CardDatabase BeforeMageDirect(CardDatabase current) => MageDirectStep(BeforeLuckyCoin(current));
    internal static CardDatabase BeforeShieldPp(CardDatabase current) => ShieldPpStep(BeforeMageDirect(current));
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
