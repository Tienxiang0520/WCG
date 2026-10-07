using Xunit;
using WcgWeb.Models;
using WcgWeb.Services;
using Microsoft.AspNetCore.Hosting;
using Moq;

namespace WcgTests;

public class GameEngineTests
{
    private readonly CardDatabase _cardDb;

    public GameEngineTests()
    {
        // Mock IWebHostEnvironment to point to WcgWeb root
        var mockEnv = new Mock<IWebHostEnvironment>();
        var wcgWebPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../WcgWeb"));
        mockEnv.Setup(m => m.ContentRootPath).Returns(wcgWebPath);

        _cardDb = new CardDatabase(mockEnv.Object);
    }

    [Fact]
    public void TestCardDatabaseLoadsCards()
    {
        Assert.Equal(199, _cardDb.AllCards.Count);
        Assert.Null(_cardDb.GetCard("ENG-01"));
        Assert.NotNull(_cardDb.GetCard("WCG-001"));
        Assert.NotNull(_cardDb.GetCard("WCG-123"));

        // Verify vanilla monster text is empty
        var vanilla = _cardDb.GetCard("WCG-101"); // 綠洲迅猛龍
        Assert.NotNull(vanilla);
        Assert.Equal("無。",vanilla.Text.Trim());
    }

    [Fact]
    public void TestPresetDecksValidation()
    {
        Assert.Equal(5, _cardDb.PresetDecks.Count);
        foreach (var deck in _cardDb.PresetDecks)
        {
            Assert.Equal(50, deck.CardIds.Count);
            var groups = deck.CardIds.GroupBy(id => id);
            foreach (var g in groups)
            {
                var card = _cardDb.GetCard(g.Key);
                Assert.NotNull(card);
                if (!card.IsEnergy)
                {
                    Assert.True(g.Count() <= 4, $"Card {card.Name} in {deck.Name} exceeds 4 copies");
                }
            }
        }
    }

    [Fact]
    public void TestGameStartInitialState()
    {
        var engine = new GameEngine(_cardDb);
        var pDeck = _cardDb.PresetDecks[0];
        var cDeck = _cardDb.PresetDecks[1];

        engine.StartGame(pDeck, cDeck, playerFirst: true);

        Assert.Equal(7, engine.Player.Hp);
        Assert.Equal(7, engine.Computer.Hp);
        Assert.Equal(7, engine.Player.Hand.Count);
        Assert.Equal(7, engine.Computer.Hand.Count);
        Assert.Equal(43, engine.Player.Deck.Count); // 50 - 7
        Assert.Equal(43, engine.Computer.Deck.Count);
        Assert.Equal(TurnPhase.MainPhase, engine.CurrentPhase);
    }

    [Fact]
    public void TestFillEnergyDoesNotDraw()
    {
        var engine = new GameEngine(_cardDb);
        engine.StartGame(_cardDb.PresetDecks[0], _cardDb.PresetDecks[1]);
        var resource = engine.Player.Hand[0];
        var handBefore = engine.Player.Hand.Count;
        var deckBefore = engine.Player.Deck.ToArray();
        engine.Player.Field.Add(new MonsterInstance(_cardDb.GetCard("WCG-039")!));
        Assert.True(engine.PlayEnergy(engine.Player, resource));
        Assert.Equal(handBefore - 1, engine.Player.Hand.Count);
        Assert.Equal(deckBefore, engine.Player.Deck);
        Assert.Contains(resource, engine.Player.EnergyZone);
        Assert.False(resource.IsTapped);
        Assert.False(engine.PlayEnergy(engine.Player, engine.Player.Hand[0]));
        Assert.DoesNotContain(engine.Logs, l => l.Message.Contains(resource.Card.Name));
    }

    [Fact]
    public void TestPaymentTapsResourcesWithoutRecycling()
    {
        var engine = new GameEngine(_cardDb);
        engine.StartGame(_cardDb.PresetDecks[0], _cardDb.PresetDecks[1]);
        var resources = Enumerable.Range(0, 2).Select(_ => new CardInstance(_cardDb.GetCard("WCG-101")!)).ToArray();
        engine.Player.EnergyZone.AddRange(resources);
        var monster = new CardInstance(_cardDb.GetCard("WCG-005")!);
        engine.Player.Hand.Add(monster);
        var deckBefore = engine.Player.Deck.ToArray();
        Assert.True(engine.SummonMonster(engine.Player, monster)); TestV06.FinishPlacement(engine);
        Assert.Equal(resources, engine.Player.EnergyZone);
        Assert.All(resources, e => Assert.True(e.IsTapped));
        Assert.Equal(deckBefore, engine.Player.Deck);
        Assert.False(engine.CanPayCost(engine.Player, monster.Card, out var payment));
        Assert.Empty(payment);
    }











    [Fact]
    public void TestAiCanPlayWithCustomDeck()
    {
        var customDeck = new Deck { MainWill = "狂怒", Name = "自訂測試牌組", CardIds = new(_cardDb.PresetDecks[0].CardIds) };
        var engine = new GameEngine(_cardDb);
        engine.StartGame(_cardDb.PresetDecks[0], customDeck);
        Assert.Equal(43, engine.Computer.Deck.Count);
        engine.Computer.IsAi = false;
        engine.EndTurn();
        engine.Computer.IsAi = true;
        engine.Computer.Hand.Clear();
        engine.Computer.Hand.Add(new CardInstance(_cardDb.GetCard("WCG-003")!));
        engine.Computer.Hand.Add(new CardInstance(_cardDb.GetCard("WCG-018")!));
        engine.ExecuteAiTurn();
        Assert.Single(engine.Computer.EnergyZone);
        Assert.Single(engine.Computer.Field);
        Assert.True(engine.Computer.EnergyZone[0].IsTapped);
    }

    [Fact]
    public void TestCustomDeckValidationRules()
    {
        var deck = new Deck { MainWill = "狂怒", CardIds = new(_cardDb.PresetDecks[0].CardIds) };
        Assert.True(deck.IsValid(_cardDb.GetCard, out _));
        deck.CardIds.RemoveAt(0);
        Assert.False(deck.IsValid(_cardDb.GetCard, out var sizeError));
        Assert.Contains("50", sizeError);
        deck.CardIds.Add(deck.CardIds.First(id => deck.CardIds.Count(x => x == id) == 4));
        Assert.False(deck.IsValid(_cardDb.GetCard, out var copyError));
        Assert.Contains("超過 4", copyError);
    }

    [Fact]
    public void TestCruelTaskmasterChoiceAndTargeting()
    {
        var engine = new GameEngine(_cardDb);
        engine.StartGame(_cardDb.PresetDecks[0], _cardDb.PresetDecks[1]);
        var enemy = new MonsterInstance(new CardDefinition { Id = "WEAK", Type = "怪物", PP = 325, DP = 1 });
        engine.Computer.Field.Add(enemy);
        for (int i = 0; i < 2; i++) engine.Player.EnergyZone.Add(new CardInstance(_cardDb.GetCard("WCG-101")!));
        var source = new CardInstance(_cardDb.GetCard("WCG-001")!); engine.Player.Hand.Add(source);
        Assert.True(engine.SummonMonster(engine.Player, source)); TestV06.FinishPlacement(engine);
        Assert.Equal(2, engine.CurrentPendingChoice!.Options.Count);
        Assert.True(engine.SelectChoice(engine.CurrentPendingChoice.Options.Single(o => o.Id == "KILL")));
        Assert.NotNull(engine.CurrentPendingTarget);
        Assert.True(engine.SelectTarget(enemy));
        Assert.DoesNotContain(enemy, engine.Computer.Field);
        Assert.Null(engine.CurrentPendingTarget);
    }

    [Fact]
    public void TestRagnarosLethalStopsTurnTransition()
    {
        var engine = new GameEngine(_cardDb);
        var pDeck = _cardDb.PresetDecks[0];
        var cDeck = _cardDb.PresetDecks[1];
        engine.StartGame(pDeck, cDeck, playerFirst: true);

        // Put Ragnaros on Player's field
        var ragCard = _cardDb.GetCard("WCG-120");
        Assert.NotNull(ragCard);
        Assert.Equal(8, ragCard.TotalCost);
        var ragMonster = new MonsterInstance(ragCard);
        engine.Player.Field.Add(ragMonster);

        // Ensure Computer has no monsters and 1 HP
        engine.Computer.Field.Clear();
        engine.Computer.Hp = 1;

        // Player ends turn -> Ragnaros fires 1 damage at Computer leader -> Lethal
        engine.EndTurn();

        // Game should immediately be GameOver, Computer HasLost = true, and turn must not advance
        Assert.Equal(TurnPhase.GameOver, engine.CurrentPhase);
        Assert.True(engine.Computer.HasLost);
        Assert.False(engine.Player.HasLost);
        Assert.Equal(0, engine.Computer.Hp);
        Assert.Equal(1, engine.TurnNumber);

        // Now test ResetToNotStarted
        engine.ResetToNotStarted();
        Assert.Equal(TurnPhase.NotStarted, engine.CurrentPhase);
        Assert.False(engine.Computer.HasLost);
        Assert.False(engine.Player.HasLost);
        Assert.Equal(7, engine.Player.Hp);
        Assert.Equal(7, engine.Computer.Hp);
    }

    [Fact]
    public void TestRagnarosDeployDoesNotMillOrShuffle()
    {
        var engine = new GameEngine(_cardDb);
        engine.StartGame(_cardDb.PresetDecks[0], _cardDb.PresetDecks[1]);
        var rag = new CardInstance(_cardDb.GetCard("WCG-120")!);
        engine.Player.Hand.Add(rag);
        for (int i = 0; i < 8; i++) engine.Player.EnergyZone.Add(new CardInstance(_cardDb.GetCard("WCG-101")!));
        var before = engine.Player.Deck.ToArray();
        Assert.True(engine.SummonMonster(engine.Player, rag));
        Assert.Equal(before, engine.Player.Deck);
        Assert.Empty(engine.Player.Graveyard);
        Assert.Equal(8, engine.Player.EnergyZone.Count);
    }

    [Fact]
    public void TestAiCanSummonRagnarosWithEightResources()
    {
        var engine = new GameEngine(_cardDb);
        engine.StartGame(_cardDb.PresetDecks[0], _cardDb.PresetDecks[1]);
        engine.Computer.IsAi = false;
        engine.EndTurn();
        engine.Computer.IsAi = true;
        engine.Computer.Hand.Clear();
        engine.Computer.Hand.Add(new CardInstance(_cardDb.GetCard("WCG-120")!));
        for (int i = 0; i < 8; i++) engine.Computer.EnergyZone.Add(new CardInstance(_cardDb.GetCard("WCG-101")!));
        engine.ExecuteAiTurn();
        var rag = Assert.Single(engine.Computer.Field);
        Assert.Equal("WCG-120", rag.Card.Id);
        Assert.False(rag.IsTaunt);
        Assert.True(rag.Card.CannotAttack);
        Assert.False(engine.Computer.HasLost);
        Assert.Equal(8, engine.Computer.EnergyZone.Count);
    }


}
