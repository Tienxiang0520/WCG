using WcgWeb.Models;
using WcgWeb.Services;
using Microsoft.AspNetCore.Hosting;
using Moq;

namespace WcgTests;

public class CardDataRevisionTests
{
    private readonly CardDatabase _cards;

    public CardDataRevisionTests()
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../WcgWeb")));
        _cards = new CardDatabase(env.Object);
    }

    [Fact]
    public void LiveCatalogHasOnly199UniversalCostCards()
    {
        Assert.Equal(199, _cards.AllCards.Count);
        Assert.Equal(199, _cards.AllCards.Select(c => c.Id).Distinct().Count());
        Assert.All(_cards.AllCards, c =>
        {
            Assert.False(c.IsEnergy);
            Assert.True(c.IsMonster || c.IsSpell || c.IsEnchantment);
            Assert.Empty(c.CostSpec);
            Assert.Equal(c.TotalCost, c.CostGen);
        });
        Assert.Null(_cards.GetCard("ENERGY-001"));
        Assert.Null(_cards.GetCard("ENG-01"));
    }

    [Fact]
    public void EveryPresetIsLegalAndDrawsSevenLeaving43()
    {
        Assert.Equal(5, _cards.PresetDecks.Count);
        foreach (var deck in _cards.PresetDecks)
        {
            Assert.True(deck.IsValid(_cards.GetCard, out var error), error);
            var engine = new GameEngine(_cards);
            engine.StartGame(deck, deck, playerFirst: true);
            Assert.Equal(7, engine.Player.Hand.Count);
            Assert.Equal(43, engine.Player.Deck.Count);
            Assert.Equal(43, engine.Computer.Deck.Count);
        }
    }

    [Theory]
    [InlineData(49)]
    [InlineData(51)]
    public void DeckRejectsWrongSize(int count)
    {
        var ids = Enumerable.Range(1, 13).SelectMany(n => Enumerable.Repeat($"WCG-{n:000}", 4)).Take(count).ToList();
        var deck = new Deck { MainWill = "狂怒", CardIds = ids };
        Assert.False(deck.IsValid(_cards.GetCard, out var error));
        Assert.Contains("50", error);
    }

    [Fact]
    public void DeckRejectsFifthCopyAndUnknownLegacyEnergyEvenAt50()
    {
        var deck = new Deck { MainWill = "狂怒", CardIds = new List<string>(_cards.PresetDecks[0].CardIds) };
        deck.CardIds[^1] = deck.CardIds[0];
        Assert.False(deck.IsValid(_cards.GetCard, out var copiesError));
        Assert.Contains("超過 4", copiesError);
        deck.CardIds[^1] = "ENERGY-001";
        Assert.False(deck.IsValid(_cards.GetCard, out var unknownError));
        Assert.Contains("找不到卡牌", unknownError);
    }
}
