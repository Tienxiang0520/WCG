using System.Reflection;
using System.Text.Json;
using WcgWeb.Models;
using WcgWeb.Services;

namespace WcgTests;
internal static class CounterHistory
{
    internal static CardDatabase Before(CardDatabase current)
    {
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
