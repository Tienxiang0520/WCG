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
    internal static CardDatabase BeforeShieldAnyCombat(CardDatabase current)
    {
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
