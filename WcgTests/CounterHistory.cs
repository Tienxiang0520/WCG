using System.Reflection;
using System.Text.Json;
using WcgWeb.Models;
using WcgWeb.Services;

namespace WcgTests;
internal static class CounterHistory
{
    internal static CardDatabase BeforeEnergyKeywords(CardDatabase current) => current.GetCard("WCG-201") == null ? current
        : new(JsonSerializer.Serialize(current.AllCards.Where(c => c.Id is not ("WCG-201" or "WCG-202" or "WCG-203" or "WCG-204"))), JsonSerializer.Serialize(current.PresetDecks));
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
        engine.StartGame(old.PresetDecks[0],old.PresetDecks[1]);
    }
}
