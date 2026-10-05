using WcgWeb.Models;
using WcgWeb.Services;
using Microsoft.AspNetCore.Hosting;
using Moq;

namespace WcgTests;

public class EnergyEngineTests
{
    private readonly CardDatabase cards;
    private readonly GameEngine engine;
    public EnergyEngineTests()
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../WcgWeb")));
        cards = new CardDatabase(env.Object);
        engine = new GameEngine(cards);
        engine.StartGame(cards.PresetDecks[0], cards.PresetDecks[1]);
        engine.Computer.IsAi = false;
    }
    private CardInstance Card(string id) => new(cards.GetCard(id)!);

    [Theory]
    [InlineData("WCG-101")]
    [InlineData("WCG-028")]
    [InlineData("WCG-062")]
    public void AllThreeCardTypesCanFillEvenWithEmptyDeck(string id)
    {
        var resource = Card(id);
        engine.Player.Hand.Add(resource);
        engine.Player.Deck.Clear();
        Assert.True(engine.PlayEnergy(engine.Player, resource));
        Assert.False(engine.Player.HasLost);
        Assert.Empty(engine.Player.Field);
        Assert.Empty(engine.Player.Graveyard);
        Assert.Null(engine.CurrentPendingChoice);
    }

    [Fact]
    public void FillRejectsWrongPlayerPhasePendingAndForeignCard()
    {
        Assert.False(engine.PlayEnergy(engine.Computer, engine.Computer.Hand[0]));
        Assert.False(engine.PlayEnergy(engine.Player, Card("WCG-101")));
        engine.CurrentPendingChoice = new PendingChoice();
        Assert.False(engine.PlayEnergy(engine.Player, engine.Player.Hand[0]));
        engine.CurrentPendingChoice = null;
        engine.CurrentPendingTarget = new PendingTarget();
        Assert.False(engine.PlayEnergy(engine.Player, engine.Player.Hand[0]));
        engine.CurrentPendingTarget = null;
        engine.CurrentPhase = TurnPhase.EndingTurn;
        Assert.False(engine.PlayEnergy(engine.Player, engine.Player.Hand[0]));
        engine.CurrentPhase = TurnPhase.MainPhase;
        Assert.True(engine.PlayEnergy(engine.Player, engine.Player.Hand[0]));
        engine.CurrentPhase = TurnPhase.EndingTurn;
        engine.CurrentPhase = TurnPhase.MainPhase;
        Assert.False(engine.PlayEnergy(engine.Player, engine.Player.Hand[0]));
    }

    [Fact]
    public void OnlyIncomingPlayerUntapsAndRegainsFillAllowance()
    {
        var playerEnergy = Card("WCG-101");
        var computerEnergy = Card("WCG-021");
        Assert.True(engine.PlayEnergy(engine.Player, engine.Player.Hand[0]));
        playerEnergy.IsTapped = computerEnergy.IsTapped = true;
        engine.Player.EnergyZone.Add(playerEnergy);
        engine.Computer.EnergyZone.Add(computerEnergy);
        engine.Computer.HasFilledEnergyThisTurn = true;
        engine.EndTurn();
        Assert.True(playerEnergy.IsTapped);
        Assert.True(engine.Player.HasFilledEnergyThisTurn);
        Assert.False(computerEnergy.IsTapped);
        Assert.False(engine.Computer.HasFilledEnergyThisTurn);
        engine.EndTurn();
        Assert.False(playerEnergy.IsTapped);
        Assert.False(engine.Player.HasFilledEnergyThisTurn);
        Assert.True(engine.PlayEnergy(engine.Player, engine.Player.Hand[0]));
    }

    [Fact]
    public void PaymentUsesOnlyUntappedResourcesAndTotalCostWithoutMutation()
    {
        var a = Card("WCG-021"); var b = Card("WCG-041"); var c = Card("WCG-081");
        c.IsTapped = true;
        engine.Player.EnergyZone.AddRange([a, b, c]);
        var cost = new CardDefinition { TotalCost = 3, CostSpec = "狂怒:3", CostGen = 0 };
        Assert.False(engine.CanPayCost(engine.Player, cost, out var failed));
        Assert.Empty(failed);
        cost.TotalCost = 2;
        Assert.True(engine.CanPayCost(engine.Player, cost, out var selected));
        Assert.Equal(new[] { a, b }, selected);
        Assert.False(a.IsTapped);
        cost.TotalCost = 0;
        Assert.True(engine.CanPayCost(engine.Player, cost, out selected));
        Assert.Empty(selected);
    }

    [Fact]
    public void NourishFillsTopCardTappedWithoutConsumingNormalAllowance()
    {
        var energy = Card("WCG-101");
        engine.Player.EnergyZone.Add(energy);
        var spell = Card("WCG-044");
        engine.Player.Hand.Add(spell);
        // Provision exactly the spell's printed cost.
        for (int i = 1; i < spell.Card.TotalCost; i++) engine.Player.EnergyZone.Add(Card("WCG-101"));
        var top = engine.Player.Deck[0];
        var deckSize = engine.Player.Deck.Count;
        var handSize = engine.Player.Hand.Count;
        Assert.True(engine.CastSpell(engine.Player, spell));
        Assert.Equal(deckSize - 1, engine.Player.Deck.Count);
        Assert.Equal(handSize - 1, engine.Player.Hand.Count);
        Assert.Contains(top, engine.Player.EnergyZone);
        Assert.True(top.IsTapped);
        Assert.False(engine.Player.HasFilledEnergyThisTurn);
        Assert.True(engine.PlayEnergy(engine.Player, engine.Player.Hand[0]));
        Assert.False(engine.SummonMonster(engine.Player, top));
        Assert.False(engine.CastSpell(engine.Player, top));
    }

    [Fact]
    public void EmptyDeckNourishDoesNotLoseOrFill()
    {
        var spell = Card("WCG-044"); engine.Player.Hand.Add(spell);
        for (int i = 0; i < spell.Card.TotalCost; i++) engine.Player.EnergyZone.Add(Card("WCG-101"));
        engine.Player.Deck.Clear();
        Assert.True(engine.CastSpell(engine.Player, spell));
        Assert.False(engine.Player.HasLost);
        Assert.Equal(spell.Card.TotalCost, engine.Player.EnergyZone.Count);
    }

    [Fact]
    public void FailedPlayCannotSpendOrMoveCards()
    {
        var monster = Card("WCG-005"); engine.Player.Hand.Add(monster);
        var resource = Card("WCG-101"); engine.Player.EnergyZone.Add(resource);
        Assert.False(engine.SummonMonster(engine.Player, monster));
        Assert.Contains(monster, engine.Player.Hand);
        Assert.False(resource.IsTapped);
        Assert.Empty(engine.Player.Field);
    }
    [Fact]
    public void ObserverDrawsTwoOnlyWhenSummonedNotWhenFilled()
    {
        var observer = Card("WCG-039");
        engine.Player.Hand.Add(observer);
        var deckBefore = engine.Player.Deck.Count;
        Assert.True(engine.PlayEnergy(engine.Player, observer));
        Assert.Equal(deckBefore, engine.Player.Deck.Count);
        Assert.Empty(engine.Player.Field);
        var summonedObserver = Card("WCG-039");
        engine.Player.Hand.Add(summonedObserver);
        for (int i = 1; i < observer.Card.TotalCost; i++) engine.Player.EnergyZone.Add(Card("WCG-101"));
        Assert.True(engine.SummonMonster(engine.Player, summonedObserver));
        Assert.Equal(deckBefore - 2, engine.Player.Deck.Count);
        Assert.Single(engine.Player.Field);
    }

    [Fact]
    public void InvalidDeckFailsBeforeMutatingExistingGame()
    {
        var previousPlayer = engine.Player;
        Assert.Throws<ArgumentException>(() => engine.StartGame(new Deck(), cards.PresetDecks[0]));
        Assert.Same(previousPlayer, engine.Player);
        Assert.Equal(TurnPhase.MainPhase, engine.CurrentPhase);
    }

    [Fact]
    public void NewGameResetsEnergyAndRejectsPreviousPlayerReference()
    {
        var previousPlayer = engine.Player;
        Assert.True(engine.PlayEnergy(previousPlayer, previousPlayer.Hand[0]));
        engine.StartGame(cards.PresetDecks[0], cards.PresetDecks[1]);
        Assert.Empty(engine.Player.EnergyZone);
        Assert.False(engine.Player.HasFilledEnergyThisTurn);
        Assert.False(engine.PlayEnergy(previousPlayer, previousPlayer.Hand[0]));
        Assert.True(engine.PlayEnergy(engine.Player, engine.Player.Hand[0]));
    }

}
