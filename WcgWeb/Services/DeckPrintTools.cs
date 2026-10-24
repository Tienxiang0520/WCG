namespace WcgWeb.Services;

// 輔助卡 printed with a deck (never part of the 50 cards). The player picks which ones and how many; they fill the free
// cells of the last card page first, then continue on extra pages of the same grid.
public enum PrintTool { Life, Coin, ShieldTokens, DpTokens, SoulTokens, BloodTokens }
// Number counts copies of the same tool from 1 (life cards alternate 玩家 1 / 玩家 2).
public sealed record PrintToolItem(PrintTool Tool, int Number);

public static class DeckPrintTools
{
    public static readonly PrintTool[] Order = [PrintTool.Life, PrintTool.Coin, PrintTool.ShieldTokens, PrintTool.DpTokens, PrintTool.SoulTokens, PrintTool.BloodTokens];
    // Cards whose text places DP 指示物 (重蹄碾壓, WCG-337–345); decks with one get a DP token sheet by default.
    private static readonly HashSet<string> DpCounterCards = ["WCG-337", "WCG-339", "WCG-340", "WCG-342", "WCG-344"];
    public static int Max(PrintTool tool) => tool == PrintTool.Life ? 4 : 3;
    public static Dictionary<PrintTool, int> Defaults(DeckPrintPlan plan) => new()
    {
        [PrintTool.Life] = 2, [PrintTool.Coin] = 1, [PrintTool.ShieldTokens] = 1,
        [PrintTool.DpTokens] = plan.Cards.Any(c => DpCounterCards.Contains(c.Id)) ? 1 : 0,
        // 奪魂指示物 (王座篡奪 WCG-335, 奪魂傀儡).
        [PrintTool.SoulTokens] = plan.Cards.Any(c => c.Id == "WCG-335") ? 1 : 0,
        // 血誓指示物 (血誓祭壇 WCG-364, 血誓反擊).
        [PrintTool.BloodTokens] = plan.Cards.Any(c => c.Id == "WCG-364") ? 1 : 0,
    };
    public static int Clamp(PrintTool tool, int count) => Math.Clamp(count, 0, Max(tool));
    public static List<PrintToolItem> Items(IReadOnlyDictionary<PrintTool, int> counts) =>
        Order.SelectMany(t => Enumerable.Range(1, Clamp(t, counts.GetValueOrDefault(t))).Select(n => new PrintToolItem(t, n))).ToList();
    // First the free cells of the last card page, then full extra pages.
    public static (PrintToolItem[] LastPage, PrintToolItem[][] ExtraPages) Place(IReadOnlyList<PrintToolItem> items, int freeCells, int perPage)
    {
        var first = items.Take(Math.Max(0, freeCells)).ToArray();
        return (first, items.Skip(first.Length).Chunk(perPage).ToArray());
    }
}
