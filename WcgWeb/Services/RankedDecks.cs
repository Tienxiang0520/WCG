using System.Text.Json;
using WcgWeb.Models;
namespace WcgWeb.Services;

public sealed partial class RankedDecks
{
    public record Entry(int Tier, Deck Deck);
    public IReadOnlyList<Entry> All { get; }
    public RankedDecks(CardDatabase cards, string json)
    {
        All = JsonSerializer.Deserialize<List<Entry>>(json)!;
        foreach (var entry in All)
            if (entry.Tier is < 0 or > 5 || !entry.Deck.IsValid(cards.GetCard, out _)) throw new InvalidDataException("天梯牌組不合法：" + entry.Deck.Name);
        if (Enumerable.Range(0, 6).Any(t => All.Count(e => e.Tier == t) < 5)) throw new InvalidDataException("各牌位須至少五套對手牌組。");
    }
    public Deck Pick(int tier, Random random)
    { var pool = All.Where(e => e.Tier == tier).ToArray(); return DeckService.Copy(pool[random.Next(pool.Length)].Deck); }
    public static string Strategy(int tier) => "v0.6 試玩 AI：固定站位、結界、反擊與能量防守，強度尚未校準";
}
