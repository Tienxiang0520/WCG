using WcgWeb.Models;
using WcgWeb.Models.Battle;

namespace WcgWeb.Services;

public sealed record PrintedCard(string Id, string Name, string Type, string Will, int Cost, int? PP, int? DP, string Text, string[]? Arrows = null)
{
    public bool IsMonster => Type == "怪物";
    public string[] ArrowList => Arrows ?? [];
    // The print sheet renders the same CardFace as the game (per-will frame + arrow triangles) from this snapshot.
    public BattleCard Face => new(Guid.Empty, Id, Name, Type, Will, Cost,
        IsMonster ? PP ?? 0 : null, IsMonster ? DP ?? 1 : null, Text, $"print-art/{Id}.jpg", ArrowList);
    public string PrintArt => $"print-art/{Id}.jpg";
    public string WillColor => Will switch
    {
        "狂怒" => "#b42828", "理智" => "#1761a7", "生機" => "#27743d",
        "秩序" => "#8d6500", "深淵" => "#754292", _ => "#555555"
    };
}

// A snapshot of the edited deck: printing never saves or changes the player's deck.
public sealed record DeckPrintPlan(string Name, string MainWill, IReadOnlyList<PrintedCard> Cards)
{
    public const int CardsPerPage = 9;
    public IReadOnlyList<PrintedCard[]> Pages => Cards.Chunk(CardsPerPage).ToArray();
    public static DeckPrintPlan Create(Deck deck, CardDatabase database)
    {
        if (!deck.IsValid(database.GetCard, out var error)) throw new ArgumentException(error);
        var cards = deck.CardIds.Select(id => database.GetCard(id)!)
            .OrderBy(c => c.TotalCost).ThenBy(c => c.Id, StringComparer.Ordinal)
            .Select(c => new PrintedCard(c.Id, c.Name, c.Type, c.Will, c.TotalCost, c.PP, c.DP, c.Text, c.Arrows.ToArray())).ToArray();
        return new(string.IsNullOrWhiteSpace(deck.Name) ? "未命名牌組" : deck.Name, deck.MainWill, cards);
    }
}
