using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Models.Battle;
using WcgWeb.Services;

namespace WcgTests;
public class TrainingToolsTests
{
    readonly CardDatabase cards;
    readonly DeckService decks;
    public TrainingToolsTests(){var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));cards=new(env.Object);decks=new(cards,env.Object);}
    (GameEngine, BattleBridge) Game(){var g=GameEngine.CreateTraining(cards);g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1]);return(g,new(g,decks));}
    static BattleResponse Edit(BattleBridge b,string type,TrainingEdit? edit=null,Guid? id=null){var s=b.Snapshot();return b.Submit(new(Guid.NewGuid(),s.MatchId,s.Revision,type,InstanceId:id,Training:edit??new()));}
    (GameEngine, BattleBridge) Preset(string name){var g=GameEngine.CreateTraining(cards);var b=new BattleBridge(g,decks);Assert.True(Edit(b,"training-preset",new(Preset:name)).Success,name);return(g,b);}

    [Fact]public void EveryPresetLoadsFromTheLobbyWithRealCards()
    {
        foreach(var name in GameEngine.TrainingPresets){var(g,_)=Preset(name);Assert.Equal(TurnPhase.MainPhase,g.CurrentPhase);Assert.All(g.Player.Board.Concat(g.Computer.Board),m=>Assert.Same(cards.GetCard(m.Card.Id),m.Card));Assert.NotEmpty(g.ExportTrainingScene());}
        var normal=new GameEngine(cards);Assert.False(Edit(new BattleBridge(normal,decks),"training-preset",new(Preset:"taunt")).Success);
    }
    [Fact]public void CountersAreSetOnEitherSideAndCountdownTicksAtTurnStart()
    {
        var(g,b)=Game();Assert.True(Edit(b,"training-place",new(Side:"computer",CardId:"WCG-413",Slot:0)).Success);
        var guard=g.Computer.Field.Single();
        Assert.True(Edit(b,"training-counters",new(Side:"computer",Countdown:2,Dp:1,Corrosion:1),guard.InstanceId).Success);
        Assert.Equal(2,guard.Countdown);Assert.Equal(1,guard.DpCounters);Assert.Equal(1,guard.CorrosionCounters);
        Assert.False(Edit(b,"training-counters",new(Side:"computer",Countdown:21),guard.InstanceId).Success);
        Assert.Contains("倒數指示物",b.Snapshot().Computer.Field.Single().Status);
        Assert.True(Edit(b,"training-endturn").Success);Assert.Equal("computer",g.CurrentTurnPlayerId);Assert.Equal(1,guard.Countdown);
    }
    [Fact]public void CountdownPresetFiresAtTheStartOfYourTurn()
    {
        var(g,b)=Preset("countdown");var hand=g.Player.Hand.Count;var guard=g.Player.Field.Single(m=>m.Card.Id=="WCG-413");
        Assert.True(Edit(b,"training-endturn").Success);
        Assert.Equal("player",g.CurrentTurnPlayerId);Assert.Equal(hand+3,g.Player.Hand.Count);// turn draw + 星沙學徒 draws 2Assert.Equal(1,guard.Countdown);
        Assert.True(g.IsWaiting);// 倒懸時鐘 asks which enemy monster to destroy
    }
    [Fact]public void UntapPresetUntapsAtTurnStartAndTauntPresetBlocksFace()
    {
        var(g,b)=Preset("untap");Assert.All(g.Player.Field,m=>Assert.True(m.IsTapped));Assert.True(Edit(b,"training-endturn").Success);Assert.All(g.Player.Field,m=>Assert.False(m.IsTapped));
        var(t,_)=Preset("taunt");var attacker=t.Player.Field.First();Assert.False(t.Attack(t.Player,attacker,null));Assert.Equal(7,t.Computer.Hp);
        var(v,_)=Preset("victory");Assert.True(v.Attack(v.Player,v.Player.Field.Single(),null));Assert.True(v.Computer.HasLost);
    }
    [Fact]public void DeckTopDrawAndTurnNumber()
    {
        var(g,b)=Game();var deck=g.Player.Deck.Count;
        Assert.True(Edit(b,"training-top",new(CardId:"WCG-114")).Success);Assert.Equal("WCG-114",g.Player.Deck[0].Card.Id);Assert.InRange(g.Player.Deck.Count,deck,deck+1);
        var hand=g.Player.Hand.Count;Assert.True(Edit(b,"training-draw").Success);Assert.Equal(hand+1,g.Player.Hand.Count);Assert.Equal("WCG-114",g.Player.Hand.Last().Card.Id);
        Assert.True(Edit(b,"training-turn",new(Turn:9)).Success);Assert.Equal(9,g.TurnNumber);Assert.False(Edit(b,"training-turn",new(Turn:0)).Success);
    }
    [Fact]public void SwapSidesLetsTheUserControlTheOpponentAndSwapsBack()
    {
        var(g,b)=Preset("taunt");var taunt=g.Computer.Field.Single(m=>m.Card.Id=="WCG-065").InstanceId;
        Assert.True(Edit(b,"training-swap").Success);
        Assert.Contains(g.Player.Field,m=>m.InstanceId==taunt);Assert.Equal("computer",g.CurrentTurnPlayerId);Assert.Equal("player",g.Player.Id);Assert.True(g.Computer.IsAi);Assert.False(g.Player.IsAi);
        Assert.True(Edit(b,"training-swap").Success);Assert.Contains(g.Computer.Field,m=>m.InstanceId==taunt);Assert.Equal("player",g.CurrentTurnPlayerId);
    }
    [Fact]public void PendingChoiceCanBeCancelledByRestoringTheLastStableBoard()
    {
        var(g,b)=Game();var stable=g.ExportTrainingScene();Assert.True(Edit(b,"training-enter",new(CardId:"WCG-001",Slot:0)).Success);Assert.True(g.IsWaiting);
        Assert.False(Edit(b,"training-counters",new(Countdown:1)).Success);Assert.False(Edit(b,"training-endturn").Success);
        Assert.True(Edit(b,"training-restore",new(Scene:stable)).Success);Assert.False(g.IsWaiting);Assert.Empty(g.Player.Field);
    }
}
