using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WcgWeb.Models;
using WcgWeb.Models.Battle;
using WcgWeb.Services;

namespace WcgTests;
public class TrainingTests
{
    readonly CardDatabase cards;
    readonly DeckService decks;
    public TrainingTests(){var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));cards=new(env.Object);decks=new(cards,env.Object);}
    (GameEngine, BattleBridge) Game(){var g=GameEngine.CreateTraining(cards);g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1]);return(g,new(g,decks));}
    static BattleResponse Edit(BattleBridge b,string type,TrainingEdit? edit=null,Guid? id=null){var s=b.Snapshot();return b.Submit(new(Guid.NewGuid(),s.MatchId,s.Revision,type,InstanceId:id,Training:edit??new()));}
    [Fact]public void NormalAndRankedEnginesRejectTrainingEdits()
    {
        var g=new GameEngine(cards);g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1]);var b=new BattleBridge(g,decks);var revision=g.Revision;
        Assert.False(Edit(b,"training-resources",new(Hp:1,TotalEnergy:20,AvailableEnergy:20)).Success);Assert.Equal(7,g.Player.Hp);Assert.Empty(g.Player.EnergyZone);Assert.Equal(revision,g.Revision);
        Assert.Throws<InvalidOperationException>(()=>g.ExportTrainingScene());
    }
    [Fact]public void DirectPlacementSkipsEntryButEntryModeUsesRealRules()
    {
        var(g,b)=Game();Assert.True(Edit(b,"training-place",new(CardId:"WCG-013",Slot:0)).Success);Assert.Equal(7,g.Player.Hp);Assert.Equal(7,g.Computer.Hp);
        Assert.True(Edit(b,"training-enter",new(CardId:"WCG-013",Slot:1)).Success);Assert.Equal(6,g.Player.Hp);Assert.Equal(6,g.Computer.Hp);Assert.Empty(g.Player.EnergyZone);
        var revision=g.Revision;Assert.False(Edit(b,"training-place",new(CardId:"WCG-101",Slot:0)).Success);Assert.Equal(revision,g.Revision);Assert.Equal(2,g.Player.Occupied);
        Assert.False(Edit(b,"training-place",new(CardId:"WCG-014",Slot:2)).Success);
        Assert.True(Edit(b,"training-set",new(Side:"computer",CardId:"WCG-195",Slot:2)).Success);Assert.True(g.Computer.Structures.Single().IsSet);Assert.Equal("SET",b.Snapshot().Computer.Field.Single().Card.CardId);
    }
    [Fact]public void SceneRestoresAttachmentsBasePowerResourcesAndCanonicalCardsRepeatedly()
    {
        var(g,b)=Game();Assert.True(Edit(b,"training-place",new(CardId:"WCG-061",Slot:0)).Success);Assert.True(Edit(b,"training-place",new(CardId:"WCG-101",Slot:1)).Success);
        Assert.True(Edit(b,"training-hand",new(CardId:"WCG-135")).Success);Assert.True(Edit(b,"training-resources",new(Hp:3,TotalEnergy:8,AvailableEnergy:5)).Success);
        var target=g.Player.Field.Single(m=>m.Card.Id=="WCG-101");var buff=g.Player.Hand.Last();Assert.True(g.CastSpellAt(g.Player,buff,target));Assert.Equal(1200,target.CurrentPP);Assert.True(target.HasShield);
        var saved=g.ExportTrainingScene();var oldMatch=g.MatchId;
        for(var i=0;i<2;i++)
        {
            Assert.True(Edit(b,"training-grave",id:g.Player.Field.Single(m=>m.Card.Id=="WCG-101").InstanceId).Success);
            Assert.True(Edit(b,"training-restore",new(Scene:saved)).Success);Assert.NotEqual(oldMatch,g.MatchId);oldMatch=g.MatchId;
            target=g.Player.Field.Single(m=>m.Card.Id=="WCG-101");Assert.Equal(700,target.BasePP);Assert.Equal(1200,target.CurrentPP);Assert.True(target.HasShield);Assert.Single(target.Attachments);
            Assert.Equal(3,g.Player.Hp);Assert.Equal(8,g.Player.TotalEnergy);Assert.Same(cards.GetCard("WCG-101"),target.Card);
            Assert.All(g.Player.Hand,c=>Assert.Same(cards.GetCard(c.Card.Id),c.Card));
        }
        var fresh=GameEngine.CreateTraining(cards);var freshBridge=new BattleBridge(fresh,decks);Assert.True(Edit(freshBridge,"training-restore",new(Scene:saved)).Success);Assert.Equal(1200,fresh.Player.Field.Single(m=>m.Card.Id=="WCG-101").CurrentPP);
    }
    [Fact]public void InvalidEditsAndCorruptScenesKeepCurrentMatch()
    {
        var(g,b)=Game();var match=g.MatchId;var revision=g.Revision;
        Assert.False(Edit(b,"training-resources",new(Hp:8)).Success);Assert.False(Edit(b,"training-resources",new(TotalEnergy:3,AvailableEnergy:4)).Success);
        Assert.False(Edit(b,"training-restore",new(Scene:"{broken")).Success);Assert.False(Edit(b,"training-restore",new(Scene:g.ExportTrainingScene().Replace("WCG-","MISSING-"))).Success);
        Assert.Equal(match,g.MatchId);Assert.Equal(revision,g.Revision);Assert.Equal(7,g.Player.Hp);
    }
    [Fact]public void PendingEffectsBlockEditsAndSavingButRestoreCanResetThem()
    {
        var(g,b)=Game();var scene=g.ExportTrainingScene();Assert.True(Edit(b,"training-enter",new(CardId:"WCG-001",Slot:0)).Success);Assert.True(g.IsWaiting);
        Assert.False(Edit(b,"training-hand",new(CardId:"WCG-125")).Success);Assert.Throws<InvalidOperationException>(()=>g.ExportTrainingScene());
        Assert.True(Edit(b,"training-restore",new(Scene:scene)).Success);Assert.False(g.IsWaiting);Assert.Empty(g.Player.Field);
    }
    [Fact]public async Task PausedComputerOnlyActsWhenExplicitlySteppedAndAcknowledged()
    {
        var(g,b)=Game();Assert.True(g.EndTurn());using var c=new BattleCoordinator(b,g,NullLogger<BattleCoordinator>.Instance){AiPaused=true,Fast=true};var token=c.Attach();var revision=g.Revision;
        using var cancel=new CancellationTokenSource(220);await c.DriveAsync(token,_=>Task.CompletedTask,cancel.Token);Assert.Equal(revision,g.Revision);
        var step=c.StepAi(token);Assert.True(step.Success);Assert.True(g.Revision>revision);Assert.False(c.StepAi(token).Success);
        Assert.True(c.Acknowledge(token,step.State.MatchId,step.State.Revision,step.BatchId));
    }
}
