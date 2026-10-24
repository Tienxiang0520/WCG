using System.Reflection;
using System.Text.Json;
using WcgWeb.Models;
using WcgWeb.Services;

namespace WcgTests;
internal static class CounterHistory
{
    // Catalog before the rules simplification (energy only pays costs, per-turn Holy Shield, 3 PP thresholds).
    // Catalog before the PP/cost deflation (PR B).
    // Catalog before the new cards 205–257 (PR D).
    // Catalog before ability monsters were lowered below the vanilla PP curve.
    // Catalog before 寂靜學者 (WCG-208) became the silence spell 寂靜禁咒.
    // Catalog before seven single-will cards became dual-will.
    // Catalog before the 理智 buff (ten cards changed, 258–262 added).
    // Catalog before 聖盾 became the one-time 聖盾指示物 (11 cards had the per-turn wording).
    // Catalog before 013, 018, 100 and 182 cost 1 more (cost and PP only).
    // Catalog before 幸運幣 (WCG-263) existed.
    // Catalog before the proportional direct-damage cost update (cost and PP only).
    // Catalog before 聖盾交戰必扣 (design notes of the nine native holy-shield cards only).
    // Catalog before every printed-PP check said 「印刷 PP」 (text of 39 cards only).
    // Catalog before the 誓劍決鬥 cards WCG-264–272 existed.
    // Catalog before the 翠語夾縫 cards WCG-273–282 existed.
    // Catalog before the 腐萎侵蝕 cards WCG-283–291 existed.
    // Catalog before 決鬥競技場 (WCG-268) could duel any enemy monster (its text and design note only).
    // Catalog before the 烽火守望 cards WCG-292–300 existed.
    // Catalog before the 伏能喚醒 cards WCG-301–309 existed.
    // Catalog before the 焚能爆破 cards WCG-310–318 existed.
    // Catalog before the 重蹄碾壓 cards WCG-337–345 existed.
    // Catalog before the 斷路突襲 cards WCG-319–327 existed.
    // Catalog before the 奪魂傀儡 cards WCG-328–336 existed.
    // Catalog before the 2026-10-24 arrow wording (「箭頭指向的怪物」).
    // Catalog before the 2026-10-24 silence/dispel counter rules (335's text).
    // Catalog before the 墓誓軍團 cards WCG-346–354 existed.
    // Catalog before the 根脈連營 cards WCG-355–363 existed.
    internal static CardDatabase BeforeRootWebCards(CardDatabase current)
    {
        return current.GetCard("WCG-355")==null ? current
            : new(JsonSerializer.Serialize(current.AllCards.Where(c=>int.Parse(c.Id[4..]) is < 355 or > 363)),JsonSerializer.Serialize(current.PresetDecks));
    }
    internal static CardDatabase BeforeGraveOathCards(CardDatabase current)
    {
        current=BeforeRootWebCards(current);
        return current.GetCard("WCG-346")==null ? current
            : new(JsonSerializer.Serialize(current.AllCards.Where(c=>int.Parse(c.Id[4..]) is < 346 or > 354)),JsonSerializer.Serialize(current.PresetDecks));
    }
    internal static CardDatabase BeforeSilenceCounters(CardDatabase current)
    {
        current=BeforeGraveOathCards(current);
        if(current.GetCard("WCG-335")?.Text.Contains("被沉默或驅散時")!=true)return current;
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb/Data/balance-before-silence-counters.json"));
        var overrides=JsonSerializer.Deserialize<List<CardDefinition>>(File.ReadAllText(path))!.ToDictionary(c=>c.Id);
        return new(JsonSerializer.Serialize(current.AllCards.Select(c=>overrides.GetValueOrDefault(c.Id,c))),JsonSerializer.Serialize(current.PresetDecks));
    }
    internal static CardDatabase BeforeArrowText(CardDatabase current)
    {
        current=BeforeSilenceCounters(current);
        if(current.GetCard("WCG-005")?.Text.Contains("箭頭指向的怪物")!=true)return current;
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb/Data/balance-before-arrow-text.json"));
        var overrides=JsonSerializer.Deserialize<List<CardDefinition>>(File.ReadAllText(path))!.ToDictionary(c=>c.Id);
        return new(JsonSerializer.Serialize(current.AllCards.Select(c=>overrides.GetValueOrDefault(c.Id,c))),JsonSerializer.Serialize(current.PresetDecks));
    }
    internal static CardDatabase BeforeSoulPuppetCards(CardDatabase current) { current=BeforeArrowText(current); return current.GetCard("WCG-328")==null ? current
        : new(JsonSerializer.Serialize(current.AllCards.Where(c=>int.Parse(c.Id[4..]) is < 328 or > 336)),JsonSerializer.Serialize(current.PresetDecks)); }
    internal static CardDatabase BeforeLaneRaidCards(CardDatabase current) { current=BeforeSoulPuppetCards(current); return current.GetCard("WCG-319")==null ? current
        : new(JsonSerializer.Serialize(current.AllCards.Where(c=>int.Parse(c.Id[4..]) is < 319 or > 327)),JsonSerializer.Serialize(current.PresetDecks)); }
    internal static CardDatabase BeforeHoofCrushCards(CardDatabase current) { current=BeforeLaneRaidCards(current); return current.GetCard("WCG-337")==null ? current
        : new(JsonSerializer.Serialize(current.AllCards.Where(c=>int.Parse(c.Id[4..]) is < 337 or > 345)),JsonSerializer.Serialize(current.PresetDecks)); }
    internal static CardDatabase BeforeEnergyBurnCards(CardDatabase current) { current=BeforeHoofCrushCards(current); return current.GetCard("WCG-310")==null ? current
        : new(JsonSerializer.Serialize(current.AllCards.Where(c=>int.Parse(c.Id[4..]) is < 310 or > 318)),JsonSerializer.Serialize(current.PresetDecks)); }
    internal static CardDatabase BeforeEnergyVaultCards(CardDatabase current) { current=BeforeEnergyBurnCards(current); return current.GetCard("WCG-301")==null ? current
        : new(JsonSerializer.Serialize(current.AllCards.Where(c=>int.Parse(c.Id[4..]) is < 301 or > 309)),JsonSerializer.Serialize(current.PresetDecks)); }
    internal static CardDatabase BeforeBeaconWatchCards(CardDatabase current) { current=BeforeEnergyVaultCards(current); return current.GetCard("WCG-292")==null ? current
        : new(JsonSerializer.Serialize(current.AllCards.Where(c=>int.Parse(c.Id[4..]) is < 292 or > 300)),JsonSerializer.Serialize(current.PresetDecks)); }
    internal static CardDatabase BeforeArenaAnyEnemy(CardDatabase current)
    {
        current=BeforeBeaconWatchCards(current);
        if(current.GetCard("WCG-268")?.Text.Contains("敵方怪物交戰（嘲諷擋不住）")!=true)return current;
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb/Data/balance-before-arena-any-enemy.json"));
        var overrides=JsonSerializer.Deserialize<List<CardDefinition>>(File.ReadAllText(path))!.ToDictionary(c=>c.Id);
        return new(JsonSerializer.Serialize(current.AllCards.Select(c=>overrides.GetValueOrDefault(c.Id,c))),JsonSerializer.Serialize(current.PresetDecks));
    }
    internal static CardDatabase BeforeRotErosionCards(CardDatabase current) { current=BeforeArenaAnyEnemy(current); return current.GetCard("WCG-283")==null ? current
        : new(JsonSerializer.Serialize(current.AllCards.Where(c=>int.Parse(c.Id[4..]) is < 283 or > 291)),JsonSerializer.Serialize(current.PresetDecks)); }
    internal static CardDatabase BeforeThresholdGapCards(CardDatabase current) { current=BeforeRotErosionCards(current); return current.GetCard("WCG-273")==null ? current
        : new(JsonSerializer.Serialize(current.AllCards.Where(c=>int.Parse(c.Id[4..]) is < 273 or > 282)),JsonSerializer.Serialize(current.PresetDecks)); }
    internal static CardDatabase BeforeOathDuelCards(CardDatabase current) { current=BeforeThresholdGapCards(current); return current.GetCard("WCG-264")==null ? current
        : new(JsonSerializer.Serialize(current.AllCards.Where(c=>int.Parse(c.Id[4..]) is < 264 or > 272)),JsonSerializer.Serialize(current.PresetDecks)); }
    internal static CardDatabase BeforePrintedPpText(CardDatabase current)
    {
        current=BeforeOathDuelCards(current);
        if(current.GetCard("WCG-082")?.Text.Contains("印刷 PP")!=true)return current;
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb/Data/balance-before-printed-pp-text.json"));
        var overrides=JsonSerializer.Deserialize<List<CardDefinition>>(File.ReadAllText(path))!.ToDictionary(c=>c.Id);
        return new(JsonSerializer.Serialize(current.AllCards.Select(c=>overrides.GetValueOrDefault(c.Id,c))),JsonSerializer.Serialize(current.PresetDecks));
    }
    internal static CardDatabase BeforeShieldAnyCombat(CardDatabase current)
    {
        current=BeforePrintedPpText(current);
        if(current.GetCard("WCG-063")?.DesignNote.Contains("聖盾交戰必扣")!=true)return current;
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb/Data/balance-before-shield-any-combat.json"));
        var overrides=JsonSerializer.Deserialize<List<CardDefinition>>(File.ReadAllText(path))!.ToDictionary(c=>c.Id);
        return new(JsonSerializer.Serialize(current.AllCards.Select(c=>overrides.GetValueOrDefault(c.Id,c))),JsonSerializer.Serialize(current.PresetDecks));
    }
    // Catalog before 巨熊咆哮 (WCG-048) was reworded (text only).
    internal static CardDatabase BeforeBearRoarText(CardDatabase current)
    {
        current=BeforeShieldAnyCombat(current);
        if(current.GetCard("WCG-048")?.Text.Contains("己方場上怪物數 × 500")!=true)return current;
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb/Data/balance-before-bear-roar-text.json"));
        var overrides=JsonSerializer.Deserialize<List<CardDefinition>>(File.ReadAllText(path))!.ToDictionary(c=>c.Id);
        return new(JsonSerializer.Serialize(current.AllCards.Select(c=>overrides.GetValueOrDefault(c.Id,c))),JsonSerializer.Serialize(current.PresetDecks));
    }
    internal static CardDatabase BeforeCardFixes(CardDatabase current)
    {
        current=BeforeBearRoarText(current);
        if(current.GetCard("WCG-003")?.DP!=0)return current;
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb/Data/balance-before-card-fixes.json"));
        var overrides=JsonSerializer.Deserialize<List<CardDefinition>>(File.ReadAllText(path))!.ToDictionary(c=>c.Id);
        return new(JsonSerializer.Serialize(current.AllCards.Select(c=>overrides.GetValueOrDefault(c.Id,c))),JsonSerializer.Serialize(current.PresetDecks));
    }
    internal static CardDatabase BeforeBurnCost(CardDatabase current)
    {
        current=BeforeCardFixes(current);
        if(current.GetCard("WCG-006")?.TotalCost!=4)return current;
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb/Data/balance-before-burn-cost.json"));
        var overrides=JsonSerializer.Deserialize<List<CardDefinition>>(File.ReadAllText(path))!.ToDictionary(c=>c.Id);
        return new(JsonSerializer.Serialize(current.AllCards.Select(c=>overrides.GetValueOrDefault(c.Id,c))),JsonSerializer.Serialize(current.PresetDecks));
    }
    // Catalog before the taunt+shield / 狂怒 face-damage / 衝鋒 3-cost update (cost and PP only).
    internal static CardDatabase BeforeTauntFaceCharge(CardDatabase current)
    {
        current=BeforeBurnCost(current);
        if(current.GetCard("WCG-005")?.TotalCost!=3)return current;
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb/Data/balance-before-taunt-face-charge.json"));
        var overrides=JsonSerializer.Deserialize<List<CardDefinition>>(File.ReadAllText(path))!.ToDictionary(c=>c.Id);
        return new(JsonSerializer.Serialize(current.AllCards.Select(c=>overrides.GetValueOrDefault(c.Id,c))),JsonSerializer.Serialize(current.PresetDecks));
    }
    internal static CardDatabase BeforeLuckyCoin(CardDatabase current)
    {
        current=BeforeTauntFaceCharge(current);
        if(current.GetCard("WCG-263")==null)return current;
        return new(JsonSerializer.Serialize(current.AllCards.Where(c=>c.Id!="WCG-263")),JsonSerializer.Serialize(current.PresetDecks));
    }
    // Catalog before 冰霜魔導師 (029) and 時間靜止者 (035) worked directly on entry (2026-10-17); only their text changed.
    internal static CardDatabase BeforeMageDirect(CardDatabase current)
    {
        current=BeforeLuckyCoin(current);
        if(current.GetCard("WCG-029")?.Text.StartsWith("進場")!=true)return current;
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb/Data/balance-before-mage-direct.json"));
        var overrides=JsonSerializer.Deserialize<List<CardDefinition>>(File.ReadAllText(path))!.ToDictionary(c=>c.Id);
        return new(JsonSerializer.Serialize(current.AllCards.Select(c=>overrides.GetValueOrDefault(c.Id,c))),JsonSerializer.Serialize(current.PresetDecks));
    }
    // Catalog before the 聖盾 PP restore (eleven holy-shield cards, PP only).
    internal static CardDatabase BeforeShieldPp(CardDatabase current)
    {
        current=BeforeMageDirect(current);
        if(current.GetCard("WCG-063")?.PP!=500||current.GetCard("WCG-079")?.PP!=2400)return current;
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb/Data/balance-before-shield-pp.json"));
        var overrides=JsonSerializer.Deserialize<List<CardDefinition>>(File.ReadAllText(path))!.ToDictionary(c=>c.Id);
        return new(JsonSerializer.Serialize(current.AllCards.Select(c=>overrides.GetValueOrDefault(c.Id,c))),JsonSerializer.Serialize(current.PresetDecks));
    }
    internal static CardDatabase BeforeCostUp(CardDatabase current)
    {
        current=BeforeShieldPp(current);
        if(current.GetCard("WCG-100")?.TotalCost!=7)return current;
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb/Data/balance-before-cost-up.json"));
        var overrides=JsonSerializer.Deserialize<List<CardDefinition>>(File.ReadAllText(path))!.ToDictionary(c=>c.Id);
        return new(JsonSerializer.Serialize(current.AllCards.Select(c=>overrides.GetValueOrDefault(c.Id,c))),JsonSerializer.Serialize(current.PresetDecks));
    }
    internal static CardDatabase BeforeShieldCounter(CardDatabase current)
    {
        current=BeforeCostUp(current);
        if(current.GetCard("WCG-205")?.Text.Contains("聖盾指示物")!=true)return current;
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb/Data/balance-before-shield-counter.json"));
        var overrides=JsonSerializer.Deserialize<List<CardDefinition>>(File.ReadAllText(path))!.ToDictionary(c=>c.Id);
        return new(JsonSerializer.Serialize(current.AllCards.Select(c=>overrides.GetValueOrDefault(c.Id,c))),JsonSerializer.Serialize(current.PresetDecks));
    }
    internal static CardDatabase BeforeReasonBuff(CardDatabase current)
    {
        current=BeforeShieldCounter(current);
        if(current.GetCard("WCG-251")?.HasTaunt!=true)return current;
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb/Data/balance-before-reason-buff.json"));
        var overrides=JsonSerializer.Deserialize<List<CardDefinition>>(File.ReadAllText(path))!.ToDictionary(c=>c.Id);
        return new(JsonSerializer.Serialize(current.AllCards.Where(c=>int.Parse(c.Id[4..])<258).Select(c=>overrides.GetValueOrDefault(c.Id,c))),JsonSerializer.Serialize(current.PresetDecks));
    }
    internal static CardDatabase BeforeDualWill(CardDatabase current)
    {
        current=BeforeReasonBuff(current);
        if(current.GetCard("WCG-031")?.IsDual!=true)return current;
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb/Data/balance-before-dual-will.json"));
        var overrides=JsonSerializer.Deserialize<List<CardDefinition>>(File.ReadAllText(path))!.ToDictionary(c=>c.Id);
        return new(JsonSerializer.Serialize(current.AllCards.Select(c=>overrides.GetValueOrDefault(c.Id,c))),JsonSerializer.Serialize(current.PresetDecks));
    }
    internal static CardDatabase BeforeSilenceSpell(CardDatabase current)
    {
        current=BeforeDualWill(current);
        if(current.GetCard("WCG-208")?.IsSpell!=true)return current;
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb/Data/balance-before-silence-spell.json"));
        var overrides=JsonSerializer.Deserialize<List<CardDefinition>>(File.ReadAllText(path))!.ToDictionary(c=>c.Id);
        return new(JsonSerializer.Serialize(current.AllCards.Select(c=>overrides.GetValueOrDefault(c.Id,c))),JsonSerializer.Serialize(current.PresetDecks));
    }
    internal static CardDatabase BeforeAbilityPp(CardDatabase current)
    {
        current=BeforeSilenceSpell(current);
        if(!(current.GetCard("WCG-113")?.PP<2000))return current;
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb/Data/balance-before-ability-pp.json"));
        var overrides=JsonSerializer.Deserialize<List<CardDefinition>>(File.ReadAllText(path))!.ToDictionary(c=>c.Id);
        return new(JsonSerializer.Serialize(current.AllCards.Select(c=>overrides.GetValueOrDefault(c.Id,c))),JsonSerializer.Serialize(current.PresetDecks));
    }
    internal static CardDatabase BeforeNewCards(CardDatabase current) => NewCardsRemoved(BeforeAbilityPp(current));
    private static CardDatabase NewCardsRemoved(CardDatabase current) => !current.AllCards.Any(c => int.Parse(c.Id[4..]) >= 205) ? current
        : new(JsonSerializer.Serialize(current.AllCards.Where(c => int.Parse(c.Id[4..]) < 205)), JsonSerializer.Serialize(current.PresetDecks).Replace("\"WCG-249\"","\"WCG-128\"").Replace("\"WCG-250\"","\"WCG-169\"").Replace("\"WCG-212\"","\"WCG-169\""));
    internal static CardDatabase BeforeDeflation(CardDatabase current)
    {
        current=BeforeNewCards(current);
        if(!(current.GetCard("WCG-161")?.PP<4000))return current;
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb/Data/balance-before-deflation.json"));
        var overrides=JsonSerializer.Deserialize<List<CardDefinition>>(File.ReadAllText(path))!.ToDictionary(c=>c.Id);
        return new(JsonSerializer.Serialize(current.AllCards.Select(c=>overrides.GetValueOrDefault(c.Id,c))),JsonSerializer.Serialize(current.PresetDecks));
    }
    internal static CardDatabase BeforeRulesSimplification(CardDatabase current)
    {
        current=BeforeDeflation(current);
        if(current.GetCard("WCG-200")?.Text.StartsWith("你受到傷害時") != true)return current;
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb/Data/balance-before-rules-simplification.json"));
        var overrides=JsonSerializer.Deserialize<List<CardDefinition>>(File.ReadAllText(path))!.ToDictionary(c=>c.Id);
        return new(JsonSerializer.Serialize(current.AllCards.Select(c=>overrides.GetValueOrDefault(c.Id,c))),JsonSerializer.Serialize(current.PresetDecks));
    }
    internal static CardDatabase BeforeEnergyKeywords(CardDatabase current)
    {
        current=BeforeRulesSimplification(current);
        return current.GetCard("WCG-201") == null ? current
            : new(JsonSerializer.Serialize(current.AllCards.Where(c => c.Id is not ("WCG-201" or "WCG-202" or "WCG-203" or "WCG-204"))), JsonSerializer.Serialize(current.PresetDecks));
    }
    internal static CardDatabase BeforeHeroGuard(CardDatabase current)
    {
        current=BeforeEnergyKeywords(current);
        return current.GetCard("WCG-200") == null ? current
            : new(JsonSerializer.Serialize(current.AllCards.Where(c => c.Id != "WCG-200")), JsonSerializer.Serialize(current.PresetDecks));
    }
    internal static CardDatabase BeforeStrongBuildAdjustment(CardDatabase current)
    {
        current=BeforeHeroGuard(current);
        if(current.GetCard("WCG-194")?.TotalCost != 5 || current.GetCard("WCG-079")?.PP != 3600)return current;
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb/Data/balance-before-strong-build-adjustment.json"));
        var overrides=JsonSerializer.Deserialize<List<CardDefinition>>(File.ReadAllText(path))!.ToDictionary(c=>c.Id);
        return new(JsonSerializer.Serialize(current.AllCards.Select(c=>overrides.GetValueOrDefault(c.Id,c))),JsonSerializer.Serialize(current.PresetDecks));
    }
    internal static CardDatabase BeforeOctoberBalance(CardDatabase current)
    {
        current=BeforeStrongBuildAdjustment(current);
        if(current.GetCard("WCG-160")?.TotalCost != 7)return current;
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb/Data/balance-before-october-13.json"));
        var overrides=JsonSerializer.Deserialize<List<CardDefinition>>(File.ReadAllText(path))!.ToDictionary(c=>c.Id);
        return new(JsonSerializer.Serialize(current.AllCards.Select(c=>overrides.GetValueOrDefault(c.Id,c))),JsonSerializer.Serialize(current.PresetDecks));
    }
    internal static CardDatabase BeforeEnergyShield(CardDatabase current)
    {
        current=BeforeOctoberBalance(current);
        if(!current.AllCards.Any(c=>c.Text.Contains("能量護盾")||c.Keywords.Contains("能量護盾")))return current;
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb/Data/balance-before-energy-shield.json"));
        var overrides=JsonSerializer.Deserialize<List<CardDefinition>>(File.ReadAllText(path))!.ToDictionary(c=>c.Id);
        return new(JsonSerializer.Serialize(current.AllCards.Select(c=>overrides.GetValueOrDefault(c.Id,c))),JsonSerializer.Serialize(current.PresetDecks));
    }
    internal static CardDatabase Before(CardDatabase current)
    {
        current=BeforeEnergyShield(current);
        if(current.GetCard("WCG-195")?.Text.Contains("受到") != true)return current;
        var path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb/Data/balance-before-damage-counters.json"));
        var overrides=JsonSerializer.Deserialize<List<CardDefinition>>(File.ReadAllText(path))!.ToDictionary(c=>c.Id);
        return new(JsonSerializer.Serialize(current.AllCards.Select(c=>overrides.GetValueOrDefault(c.Id,c))),JsonSerializer.Serialize(current.PresetDecks));
    }
    internal static void RestartOld(GameEngine engine, CardDatabase current)
    {
        var old=Before(current); engine.ResetToNotStarted();
        typeof(GameEngine).GetMethod("UseMatchCatalog",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(engine,[old,false,false]);
        engine.EqualStartingLife=true; // old-era matches start both players at 7
        engine.StartGame(old.PresetDecks[0],old.PresetDecks[1]);
    }
}
