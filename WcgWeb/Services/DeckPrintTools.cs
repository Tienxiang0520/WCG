namespace WcgWeb.Services;

// 輔助卡 printed with a deck (never part of the 50 cards). The player picks which ones and how many; they fill the free
// cells of the last card page first, then continue on extra pages of the same grid. Since 2026-11-12 (指示物改用牌組裡的卡)
// every lasting state is shown with cards from the decks, so there are no counter sheets any more; only the life card and
// 幸運幣 remain.
public enum PrintTool { Life, Coin }
// Number counts copies of the same tool from 1 (life cards alternate 玩家 1 / 玩家 2).
public sealed record PrintToolItem(PrintTool Tool, int Number);

public static class DeckPrintTools
{
    public static readonly PrintTool[] Order = [PrintTool.Life, PrintTool.Coin];
    public static int Max(PrintTool tool) => tool == PrintTool.Life ? 4 : 3;
    public static Dictionary<PrintTool, int> Defaults(DeckPrintPlan plan) => new() { [PrintTool.Life] = 2, [PrintTool.Coin] = 1 };
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
