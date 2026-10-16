namespace WcgWeb.Models;

public class Deck
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "新牌組";
    public string Description { get; set; } = "";
    public static IReadOnlyList<string> Wills { get; } = Array.AsReadOnly(new[] { "狂怒", "理智", "生機", "秩序", "深淵" });
    // There is no limit on secondary-will (副色) card count; only "at most two wills plus neutral" applies.
    public string MainWill { get; set; } = "";
    public bool IsOffColor(CardDefinition card) => !card.IsNeutral && !card.HasWill(MainWill);
    // A dual-will card fits only a deck whose main + secondary wills are exactly its two wills (either order).
    public static bool DualFits(CardDefinition card, string mainWill, string? secondWill) =>
        !card.IsDual || (card.HasWill(mainWill) && (string.IsNullOrEmpty(secondWill) || card.HasWill(secondWill)));
    public (int Main, int OffColor, int Neutral) GetColorCounts(Func<string, CardDefinition?> getCard)
    {
        int main = 0, off = 0, neutral = 0;
        foreach (var id in CardIds)
        {
            var card = getCard(id);
            if (card == null) continue;
            if (card.IsNeutral) neutral++;
            else if (card.HasWill(MainWill)) main++;
            else off++;
        }
        return (main, off, neutral);
    }
    public long Version { get; set; }
    private List<string> cardIds = new();
    public List<string> CardIds
    {
        get => cardIds;
        set => cardIds = value == null ? null! : value.Select(CardIdentifier.Canonical).ToList();
    }

    public (bool IsValid, List<string> Errors) Validate(Func<string, CardDefinition?> getCard)
    {
        var errors = new List<string>();

        if (CardIds.Count != 50)
        {
            errors.Add($"牌組張數必須恰好為 50 張（目前：{CardIds.Count} 張）");
        }

        if (!Wills.Contains(MainWill)) errors.Add("請選擇五種意志之一作為主色（中立不能作為主色）。");
        else
        {
            var cards = CardIds.Select(getCard).Where(c => c != null).Select(c => c!).ToArray();
            var factions = cards.SelectMany(c => c.FactionWills).Distinct().ToArray();
            if (factions.Length > 2) errors.Add("牌組最多包含兩個派系，中立不計。");
            foreach (var dual in cards.Where(c => c.IsDual).DistinctBy(c => c.Id))
            {
                var other = factions.FirstOrDefault(w => w != MainWill);
                if (!DualFits(dual, MainWill, other))
                    errors.Add($"雙色卡【{dual.Name}】只能放進主色與副色剛好是{string.Join("＋", dual.Wills)}的牌組。");
            }
        }

        var counts = CardIds.GroupBy(CardIdentifier.Canonical).ToDictionary(g => g.Key, g => g.Count());
        foreach (var (cardId, count) in counts)
        {
            var card = getCard(cardId);
            if (card == null)
            {
                errors.Add($"找不到卡牌 ID：{cardId}");
                continue;
            }

            if (count > 4)
            {
                errors.Add($"卡牌【{card.Name}】超過 4 張上限（目前：{count} 張）");
            }
        }

        return (errors.Count == 0, errors);
    }

    public bool IsValid(Func<string, CardDefinition?> getCard, out string? errorMessage)
    {
        var (isValid, errors) = Validate(getCard);
        errorMessage = errors.Count > 0 ? string.Join("；", errors) : null;
        return isValid;
    }
}
