using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;
namespace WcgTests;

// 幸運幣 (WCG-263, 2026-10-17): the second player starts with one extra coin in hand. Casting it puts it face up in the
// energy zone as 1 temporary energy; at the end of that turn it goes to the graveyard whether used or not.
// Older start rules and catalogs before the coin have no coin.
public sealed class LuckyCoinTests
{
    private readonly CardDatabase cards;
    public LuckyCoinTests(){var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));cards=new(env.Object);}
    private GameEngine Game(int energy,params string[] hand)
    {
        var g=new GameEngine(cards,new Random(11));g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1]);
        foreach(var p in new[]{g.Player,g.Computer}){p.Hand.Clear();p.Field.Clear();p.EnergyZone.Clear();}
        for(int i=0;i<energy;i++)g.Player.EnergyZone.Add(new(cards.GetCard("WCG-101")!));
        foreach(var id in hand)g.Player.Hand.Add(new(cards.GetCard(id)!));
        g.Player.HasFilledEnergyThisTurn=true;return g;
    }
    private static CardInstance Hand(GameEngine g,string id)=>g.Player.Hand.First(c=>c.Card.Id==id);

    [Fact] public void CoinIsANeutralZeroCostSpell()
    {
        var c=cards.GetCard("WCG-263")!;Assert.Equal("幸運幣",c.Name);Assert.True(c.IsSpell);Assert.False(c.IsCounter);Assert.Equal(0,c.TotalCost);
    }
    [Theory][InlineData(true)][InlineData(false)]
    public void SecondPlayerStartsWithOneExtraCoin(bool playerFirst)
    {
        var g=new GameEngine(cards,new Random(3));g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1],playerFirst);
        var (first,second)=playerFirst?(g.Player,g.Computer):(g.Computer,g.Player);
        Assert.Equal(7,first.Hand.Count);Assert.Equal(8,second.Hand.Count);Assert.Equal(1,g.OpeningCoins);
        Assert.Single(second.Hand,c=>c.Card.Id=="WCG-263");Assert.Equal(50,second.Deck.Count+second.Hand.Count-1);
        Assert.Contains(g.Logs,l=>l.Message.Contains("後攻，開局手牌多 1 張【幸運幣】"));
    }
    [Fact] public void OlderStartRulesAndCatalogsHaveNoCoin()
    {
        var g=new GameEngine(cards,new Random(3)){SecondPlayerCoin=false};g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1]);
        Assert.Equal(7,g.Computer.Hand.Count);Assert.Equal(0,g.OpeningCoins);
        g=new GameEngine(cards,new Random(3)){EqualStartingLife=true};g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1]);
        Assert.Equal(7,g.Computer.Hand.Count);
        var old=CardBalanceHistory.BeforeLuckyCoin(cards);Assert.Null(old.GetCard("WCG-263"));Assert.Equal(cards.AllCards.Count-1,old.AllCards.Count);
        g=new GameEngine(old,new Random(3));g.StartGame(old.PresetDecks[0],old.PresetDecks[1]);Assert.Equal(7,g.Computer.Hand.Count);
        Assert.Same(old,CardBalanceHistory.BeforeLuckyCoin(old));
    }
    [Fact] public void CoinGivesOneEnergyForThisTurnOnly()
    {
        var g=Game(2,"WCG-263","WCG-155");
        Assert.False(g.CanPlayCard(g.Player,Hand(g,"WCG-155")));
        Assert.True(g.CastSpell(g.Player,Hand(g,"WCG-263")));
        Assert.Equal(3,g.Player.AvailableEnergy);Assert.Equal(1,g.Player.TemporaryEnergy);
        Assert.DoesNotContain(g.Player.Graveyard,c=>c.Card.Id=="WCG-263");
        var coin=g.Player.EnergyZone.Single(e=>e.TemporaryEnergy);Assert.False(coin.IsTapped);Assert.Equal("WCG-263",coin.Card.Id);
        Assert.True(g.SummonMonster(g.Player,Hand(g,"WCG-155")));while(g.CurrentPendingChoice is {} c)Assert.True(g.SelectChoice(c.Options.First()));Assert.Single(g.Player.Field);Assert.Equal(0,g.Player.AvailableEnergy);
        Assert.True(g.EndTurn());
        Assert.Equal(2,g.Player.EnergyZone.Count);Assert.Equal(0,g.Player.TemporaryEnergy);
        Assert.Contains(g.Player.Graveyard,c=>c.Card.Id=="WCG-263");Assert.False(coin.TemporaryEnergy);
    }
    [Fact] public void UnusedCoinStillLeavesAtEndOfTurn()
    {
        var g=Game(1,"WCG-263");Assert.True(g.CastSpell(g.Player,Hand(g,"WCG-263")));Assert.Equal(2,g.Player.AvailableEnergy);
        Assert.True(g.EndTurn());Assert.Single(g.Player.EnergyZone);Assert.Contains(g.Player.Graveyard,c=>c.Card.Id=="WCG-263");
    }
    // 2026-10-17: the coin may never be filled face down as permanent energy (rule check, UI and every AI tier).
    [Fact] public void CoinCannotBeFilledAsPermanentEnergy()
    {
        var g=Game(1,"WCG-263","WCG-101");g.Player.HasFilledEnergyThisTurn=false;var coin=Hand(g,"WCG-263");
        Assert.Equal("幸運幣不能放入能量區當永久能量。",g.GetEnergyProblem(g.Player,coin));Assert.False(g.CanPlayEnergy(g.Player,coin));
        Assert.False(g.PlayEnergy(g.Player,coin));Assert.Single(g.Player.EnergyZone);Assert.Contains(coin,g.Player.Hand);Assert.False(g.Player.HasFilledEnergyThisTurn);
        Assert.True(g.CanPlayEnergy(g.Player,Hand(g,"WCG-101")));
    }
    [Fact] public void SnapshotDoesNotOfferCoinAsEnergy()
    {
        var g=Game(1,"WCG-263","WCG-101");g.Player.HasFilledEnergyThisTurn=false;var storage=new Mock<IPlayerStorage>();storage.Setup(x=>x.Gate).Returns(new object());var b=new BattleBridge(g,new DeckService(cards,storage.Object));
        var hand=b.Snapshot().Hand;var coin=hand.Single(h=>h.Card.CardId=="WCG-263");
        Assert.False(coin.CanEnergy);Assert.Equal("幸運幣不能放入能量區當永久能量。",coin.EnergyProblem);
        Assert.True(hand.Single(h=>h.Card.CardId=="WCG-101").CanEnergy);
    }
    [Theory][InlineData(-1)][InlineData(0)][InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)][InlineData(5)][InlineData(6)]
    public void AiNeverFillsTheCoin(int level)
    {
            // Coin plus an expensive card: the coin can't enable it, so the only legal fill is the other card.
            var g=Game(0,"WCG-263","WCG-080");g.Player.HasFilledEnergyThisTurn=false;g.Player.IsAi=true;g.AiLevel=level;RunPlayerTurn(g);
            Assert.DoesNotContain(g.Player.EnergyZone,e=>e.Card.Id=="WCG-263");
            g=Game(0,"WCG-263");g.Player.HasFilledEnergyThisTurn=false;g.Player.IsAi=true;g.AiLevel=level;RunPlayerTurn(g);
            Assert.DoesNotContain(g.Player.EnergyZone,e=>e.Card.Id=="WCG-263");
    }
    [Fact] public void SnapshotMarksTemporaryEnergy()
    {
        var g=Game(1,"WCG-263");var storage=new Mock<IPlayerStorage>();storage.Setup(x=>x.Gate).Returns(new object());var b=new BattleBridge(g,new DeckService(cards,storage.Object));
        Assert.DoesNotContain(b.Snapshot().Player.Energy,e=>e.Temporary);
        Assert.True(g.CastSpell(g.Player,Hand(g,"WCG-263")));
        Assert.Single(b.Snapshot().Player.Energy,e=>e.Temporary);
    }
    [Theory][InlineData(-1)][InlineData(3)][InlineData(5)] // low tiers blunder on purpose
    public void AiPlaysTheCoinOnlyWhenItEnablesACard(int level)
    {
        var g=Game(2,"WCG-263","WCG-155");g.Player.IsAi=true;g.AiLevel=level;RunPlayerTurn(g);
        Assert.Contains(g.Player.Field,m=>m.Card.Id=="WCG-155");Assert.Contains(g.Player.Graveyard,c=>c.Card.Id=="WCG-263");
        g=Game(2,"WCG-263","WCG-101");g.Player.IsAi=true;g.AiLevel=level;RunPlayerTurn(g);
        Assert.Contains(g.Player.Field,m=>m.Card.Id=="WCG-101");Assert.Contains(g.Player.Hand,c=>c.Card.Id=="WCG-263");
    }
    private static void RunPlayerTurn(GameEngine g)
    {
        for(int i=0;i<40&&g.CurrentTurnPlayerId=="player"&&!g.IsOver;i++)Assert.True(g.ExecuteAiStep(),g.LastError);
        Assert.NotEqual("player",g.CurrentTurnPlayerId);
    }
}
