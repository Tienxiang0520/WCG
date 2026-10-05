using WcgWeb.Models;
using WcgWeb.Services;

namespace WcgTests;

public class DeckPrintTests
{
    private readonly CardDatabase database = new(
        File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../WcgWeb/Data/cards.json"))),
        File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../WcgWeb/Data/preset_decks.json"))));

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void EveryPresetPrintsAllFiftyCopiesAcrossSixPages(int preset)
    {
        var deck = database.PresetDecks[preset];
        var plan = DeckPrintPlan.Create(deck, database);
        Assert.Equal(6, plan.Pages.Count);
        Assert.All(plan.Pages.Take(5), page => Assert.Equal(9, page.Length));
        Assert.Equal(5, plan.Pages[^1].Length);
        Assert.Equal(deck.CardIds.Order(), plan.Cards.Select(c => c.Id).Order());
        foreach (var card in plan.Cards)
        {
            var original = database.GetCard(card.Id)!;
            Assert.Equal(original.Text, card.Text);
            Assert.Equal(original.TotalCost, card.Cost);
            Assert.Equal(original.PP, card.PP);
            Assert.Equal(original.DP, card.DP);
        }
    }

    [Fact]
    public void PrintingSnapshotsTheEditedDraftWithoutMutatingIt()
    {
        var preset = database.PresetDecks[0];
        var draft = new Deck { Name = "尚未儲存的牌組", MainWill = preset.MainWill, CardIds = [..preset.CardIds], Version = 8 };
        var ids = draft.CardIds.ToArray();
        var plan = DeckPrintPlan.Create(draft, database);
        Assert.Equal(ids, draft.CardIds); Assert.Equal(8, draft.Version);
        draft.Name = "之後的名稱"; draft.CardIds.Clear();
        database.GetCard(plan.Cards[0].Id)!.Name = "測試另一份資料變更";
        Assert.Equal("尚未儲存的牌組", plan.Name);
        Assert.Equal(50, plan.Cards.Count);
        Assert.DoesNotContain(plan.Cards, c => c.Name == "測試另一份資料變更");
    }

    [Fact]
    public void IncompleteOrUnknownCardDecksCannotProducePlayablePrints()
    {
        var original = database.PresetDecks[0];
        var draft = new Deck { MainWill = original.MainWill, CardIds = [..original.CardIds] };
        draft.CardIds.RemoveAt(0); Assert.Throws<ArgumentException>(() => DeckPrintPlan.Create(draft, database));
        draft.CardIds.Add("UNKNOWN"); Assert.Throws<ArgumentException>(() => DeckPrintPlan.Create(draft, database));
    }
}
