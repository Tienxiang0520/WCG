using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;

namespace WcgTests;

public class ThemeSystemTests
{
    private static readonly CardDatabase Cards = MakeDb();

    private static CardDatabase MakeDb()
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(x => x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../WcgWeb")));
        return new(env.Object);
    }

    [Fact]
    public void EachThemeHasGovernedSingleThemeQuotaAndAllCardsUseKnownThemes()
    {
        Assert.True(ThemeCatalog.All.Count >= 5);
        foreach (var theme in ThemeCatalog.All)
        {
            int count = Cards.AllCards.Count(c => !c.IsDual && c.Will == theme.Id);
            Assert.InRange(count, theme.MinCards, theme.MaxCards);
        }

        foreach (var card in Cards.AllCards)
            Assert.All(card.Wills, w => Assert.True(w == ThemeCatalog.Neutral || ThemeCatalog.IsTheme(w), $"{card.Id} uses unknown theme {w}"));
    }

    [Fact]
    public void LegacyWillNamesNormalizeAndValidateCleanly()
    {
        var blazer = Cards.AllCards.First(c => !c.IsDual && c.Will == "熾焰");
        Assert.True(blazer.HasWill("狂怒"));
        Assert.True(blazer.HasTheme("熾焰"));
        Assert.Equal("wrath", ThemeCatalog.FrameKey("熾焰"));
        Assert.Equal("wrath", ThemeCatalog.FrameKey("狂怒"));

        var legacyDeck = DeckService.Copy(Cards.PresetDecks[0]);
        legacyDeck.MainWill = "狂怒";
        Assert.True(legacyDeck.IsValid(Cards.GetCard, out var error), error);

        var decoded = DeckCode.Decode(DeckCode.Encode(legacyDeck), out var codeErr);
        Assert.Equal("", codeErr);
        Assert.Equal("熾焰", decoded!.MainWill);
    }
}
