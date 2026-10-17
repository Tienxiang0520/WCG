using WcgWeb.Services;

namespace WcgTests;

// Physical print only: every paper/bleed combination must fit its printable area and hold a whole deck plus the tool cards.
public class DeckPrintLayoutTests
{
    public static TheoryData<PrintPaper, bool, int, int> Layouts => new()
    {
        { PrintPaper.A4, false, 9, 6 }, { PrintPaper.Letter, false, 9, 6 },
        { PrintPaper.A4, true, 8, 7 }, { PrintPaper.Letter, true, 6, 9 },
    };

    [Theory, MemberData(nameof(Layouts))]
    public void EveryLayoutFitsItsPaperAndLeavesRoomForTheToolCards(PrintPaper paper, bool bleed, int perPage, int pages)
    {
        var layout = new DeckPrintLayout(paper, bleed);
        Assert.True(layout.Fits, $"{paper} bleed={bleed}: grid {layout.GridWidth}×{layout.UsedHeight} mm in {layout.PrintableWidth}×{layout.PrintableHeight} mm");
        Assert.Equal(perPage, layout.CardsPerPage);
        Assert.Equal(pages, layout.PageCount(50));
        // 2 life cards + 1 聖盾指示物 card share the last card page.
        Assert.True(50 - (pages - 1) * perPage + 3 <= perPage);
    }

    [Fact]
    public void BleedAddsThreeMillimetresOnEverySideAndDefaultA4IsUnchanged()
    {
        var bleed = new DeckPrintLayout(PrintPaper.A4, true);
        Assert.Equal((94d, 69d), (bleed.CellWidth, bleed.CellHeight)); // 88 + 2×3, 63 + 2×3, turned sideways
        Assert.Equal(0, bleed.Gap);
        var standard = new DeckPrintLayout(PrintPaper.A4, false);
        Assert.Null(standard.PageRule); // deck-print.css keeps its A4 rule
        Assert.Equal((193d, 268d), (standard.GridWidth, standard.GridHeight));
        Assert.Equal("@page { size: letter portrait; margin: 5mm 6mm; }", new DeckPrintLayout(PrintPaper.Letter, false).PageRule);
        Assert.Equal("@page { size: A4 portrait; margin: 5mm 6mm; }", bleed.PageRule);
    }

    [Fact]
    public void PagesOfSplitsTheDeckByTheLayout()
    {
        var cards = Enumerable.Range(0, 50).Select(i => new PrintedCard($"X{i}", "", "法術", "中立", 1, null, null, "")).ToArray();
        var plan = new DeckPrintPlan("d", "中立", cards);
        Assert.Equal(plan.Pages.Count, plan.PagesOf(DeckPrintPlan.CardsPerPage).Count);
        Assert.Equal([8, 8, 8, 8, 8, 8, 2], plan.PagesOf(8).Select(p => p.Length));
    }
}
