using System.Text.Json;
using WcgWeb.Models;
using WcgWeb.Models.Battle;
using WcgWeb.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace WcgTests;

public class BattleBridgeTests
{
    readonly CardDatabase db;
    readonly GameEngine engine;
    readonly BattleBridge bridge;
    public BattleBridgeTests()
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));
        db=new(env.Object);engine=new(db,new Random(11));bridge=new(engine,new DeckService(db,env.Object));
        engine.StartGame(db.PresetDecks[0],db.PresetDecks[1]);
    }
    BattleCommand Cmd(string type, Guid? id=null,Guid? target=null,string? option=null)=>
        new(Guid.NewGuid(),engine.MatchId,engine.Revision,type,id,target,option);
    CardInstance Hand(string id){var c=new CardInstance(db.GetCard(id)!);engine.Player.Hand.Add(c);return c;}
    void Energy(){for(int i=0;i<12;i++)engine.Player.EnergyZone.Add(new(db.GetCard("WCG-101")!));}
    [Theory][InlineData(0)][InlineData(1)][InlineData(2)]
    public void CounterRevealReportsInsufficientEnergyWithoutChangingResolution(int available)
    {
        Assert.True(engine.EndTurn());
        var attacker=Field(engine.Computer,"WCG-101");
        var counter=new MonsterInstance(db.GetCard("WCG-199")!){IsSet=true,Slot=0};
        engine.Player.Structures.Add(counter);
        for(int i=0;i<3;i++)engine.Player.EnergyZone.Add(new(db.GetCard("WCG-101")!){IsTapped=i>=available});
        Assert.True(engine.Attack(engine.Computer,attacker));
        var result=bridge.Submit(Cmd("choice",option:counter.InstanceId.ToString()));
        Assert.True(result.Success);
        Assert.Empty(engine.Player.Structures);
        Assert.Single(engine.Player.Graveyard,c=>c.InstanceId==counter.InstanceId);
        var reveal=Assert.Single(result.Events,e=>e.Type=="reveal");
        if(available<2)
        {
            var message=$"費用不足，反擊未發動（需要 2，可用 {available}）；蓋牌已送墓地。";
            Assert.Equal(message,reveal.Label);
            Assert.Equal("WCG-199",reveal.Card!.CardId);
            Assert.Contains(engine.Logs,l=>l.Message==message);
            Assert.False(LocalizationCatalog.HasHan(LocalizationCatalog.Translate(message)));
            Assert.Contains(attacker,engine.Computer.Field);
            Assert.Equal(7-attacker.CurrentDP,engine.Player.Hp);
            Assert.Equal(available,engine.Player.AvailableEnergy);
        }
        else
        {
            Assert.Equal("翻開蓋牌",reveal.Label);
            Assert.DoesNotContain(attacker,engine.Computer.Field);
            Assert.Equal(7,engine.Player.Hp);
            Assert.Equal(0,engine.Player.AvailableEnergy);
        }
    }
    [Fact] public void DirectSpellTargetsRejectFriendlyAndStealthWithoutSpendingThenResolveEnemy()
    {
        Energy();var card=Hand("WCG-026");var own=Field(engine.Player,"WCG-101");
        var hidden=Field(engine.Computer,"WCG-111"); engine.Computer.Field.Remove(hidden); engine.Computer.Structures.Add(new(hidden.Card){InstanceId=hidden.InstanceId,IsSet=true});var enemy=Field(engine.Computer,"WCG-101");
        var hand=bridge.Snapshot().Hand.Single(h=>h.Card.InstanceId==card.InstanceId);
        Assert.Equal(new[]{enemy.InstanceId.ToString()},hand.PlayTargets);
        foreach(var id in new[]{own.InstanceId,hidden.InstanceId,Guid.NewGuid()})
        {Assert.False(bridge.Submit(Cmd("play",card.InstanceId,id)).Success);Assert.Contains(card,engine.Player.Hand);Assert.Equal(12,engine.Player.AvailableEnergy);Assert.False(engine.IsWaiting);}
        var command=Cmd("play",card.InstanceId,enemy.InstanceId);var result=bridge.Submit(command);
        Assert.True(result.Success);Assert.Null(result.State.Pending);Assert.DoesNotContain(enemy,engine.Computer.Field);
        Assert.DoesNotContain(card,engine.Player.Hand);Assert.Equal(12-card.Card.TotalCost,engine.Player.AvailableEnergy);
        Assert.Equal(result,bridge.Submit(command));
    }
    [Fact] public void DirectSpellWithSacrificeStillWaitsForCostAndCanCancel()
    {
        Energy();var card=Hand("WCG-088");Field(engine.Player,"WCG-101");var enemy=Field(engine.Computer,"WCG-101");
        var result=bridge.Submit(Cmd("play",card.InstanceId,enemy.InstanceId));
        Assert.True(result.Success);Assert.True(result.State.Pending!.CanCancel);Assert.Equal("target",result.State.Pending.Kind);
        Assert.Equal(12,engine.Player.AvailableEnergy);Assert.Contains(card,engine.Player.Hand);
        Assert.True(bridge.Submit(Cmd("cancel")).Success);Assert.Contains(enemy,engine.Computer.Field);Assert.Equal(12,engine.Player.AvailableEnergy);
    }
    MonsterInstance Field(PlayerState p,string id){var m=new MonsterInstance(db.GetCard(id)!){IsTapped=false,HasSummoningSickness=false};p.Field.Add(m);return m;}
    [Fact] public void OpponentDeathChoiceDoesNotChangeTurnOwnership()
    {
        var attacker=Field(engine.Player,"WCG-009");attacker.CurrentPP=10000;
        var target=Field(engine.Computer,"WCG-093");
        engine.Computer.Graveyard.Add(new(db.GetCard("WCG-101")!));
        var response=bridge.Submit(Cmd("attack",attacker.InstanceId,target.InstanceId));
        Assert.True(response.Success);
        Assert.NotNull(engine.CurrentPendingChoice);
        Assert.Equal("computer",response.State.DecisionPlayerId);
        Assert.Equal("player",response.State.ActivePlayerId);
        Assert.Equal(1,response.State.Turn);
    }
    [Theory]
    [InlineData("WCG-003", "none", "choice")]
    [InlineData("WCG-026", "target", "target")]
    [InlineData("WCG-046", "choice", "choice")]
    [InlineData("WCG-096", "sacrifice", "target")]
    public void PlayGuidanceMatchesActualPreparationAndCancellationPreservesResources(string id,string preparation,string? pendingKind)
    {
        Energy();Field(engine.Player,"WCG-101");Field(engine.Computer,"WCG-101");var c=Hand(id);
        var hand=Assert.Single(bridge.Snapshot().Hand,h=>h.Card.InstanceId==c.InstanceId);
        Assert.True(hand.CanPlay);Assert.Equal(preparation,hand.Preparation);
        var energy=engine.Player.AvailableEnergy;var count=engine.Player.Hand.Count;
        var response=bridge.Submit(Cmd("play",c.InstanceId));Assert.True(response.Success);
        Assert.Equal(pendingKind,response.State.Pending?.Kind);
        if(pendingKind==null)return;
        Assert.True(response.State.Pending!.CanCancel);
        Assert.Equal(c.InstanceId,response.State.Pending.SourceInstanceId);
        Assert.Equal(energy,engine.Player.AvailableEnergy);Assert.Equal(count,engine.Player.Hand.Count);
        Assert.True(bridge.Submit(Cmd("cancel")).Success);
        Assert.Contains(c,engine.Player.Hand);Assert.Equal(energy,engine.Player.AvailableEnergy);
    }
    [Fact] public void SpellPreparationKeepsSourceAcrossChoiceAndTargetAndCanCancel()
    {
        Energy();Field(engine.Computer,"WCG-003");var card=Hand("WCG-046");
        var choice=bridge.Submit(Cmd("play",card.InstanceId));Assert.True(choice.Success);
        Assert.Equal(card.InstanceId,choice.State.Pending!.SourceInstanceId);
        var target=bridge.Submit(Cmd("choice",option:"KILL"));Assert.True(target.Success);
        Assert.Equal("target",target.State.Pending!.Kind);Assert.Equal(card.InstanceId,target.State.Pending.SourceInstanceId);
        Assert.Equal(card.InstanceId,bridge.Snapshot().Pending!.SourceInstanceId);
        Assert.Equal(12,engine.Player.AvailableEnergy);
        Assert.True(bridge.Submit(Cmd("cancel")).Success);Assert.Contains(card,engine.Player.Hand);
        Assert.Null(bridge.Snapshot().Pending);Assert.Equal(12,engine.Player.AvailableEnergy);
    }
    [Fact] public void PaidSpellChoiceCannotBeConfusedWithUnpaidPreparation()
    {
        Energy();var card=Hand("WCG-046");
        Assert.True(bridge.Submit(Cmd("play",card.InstanceId)).Success);
        var paid=bridge.Submit(Cmd("choice",option:"LOOT"));Assert.True(paid.Success);
        Assert.NotNull(paid.State.Pending);Assert.False(paid.State.Pending.CanCancel);
        Assert.Null(paid.State.Pending.SourceInstanceId);Assert.DoesNotContain(card,engine.Player.Hand);
        Assert.Contains(card,engine.Player.Graveyard);Assert.Equal(11,engine.Player.AvailableEnergy);
        Assert.False(bridge.Submit(Cmd("cancel")).Success);
        var finish=bridge.Submit(Cmd("choice",option:paid.State.Pending.Options[0].Id));
        Assert.True(finish.Success);Assert.Null(finish.State.Pending);
    }
    [Fact] public void HandGuidanceExplainsInsufficientEnergyFullFieldAndEnergyUsed()
    {
        var c=Hand("WCG-101");var initial=Assert.Single(bridge.Snapshot().Hand,h=>h.Card.InstanceId==c.InstanceId);
        Assert.False(initial.CanPlay);Assert.Contains("能量不足",initial.Problem);Assert.True(initial.CanEnergy);Assert.Empty(initial.EnergyProblem);
        Assert.True(bridge.Submit(Cmd("energy",Hand("WCG-003").InstanceId)).Success);
        var afterEnergy=Assert.Single(bridge.Snapshot().Hand,h=>h.Card.InstanceId==c.InstanceId);
        Assert.True(afterEnergy.CanPlay);Assert.False(afterEnergy.CanEnergy);Assert.Contains("已填過",afterEnergy.EnergyProblem);
        for(int i=0;i<5;i++)Field(engine.Player,"WCG-101");
        var full=Assert.Single(bridge.Snapshot().Hand,h=>h.Card.InstanceId==c.InstanceId);
        Assert.False(full.CanPlay);Assert.Contains("場上已滿",full.Problem);
        Assert.True(bridge.Submit(Cmd("end")).Success);
        var opponentTurn=Assert.Single(bridge.Snapshot().Hand,h=>h.Card.InstanceId==c.InstanceId);
        Assert.False(opponentTurn.CanPlay);Assert.False(opponentTurn.CanEnergy);
        Assert.NotEmpty(opponentTurn.Problem);Assert.NotEmpty(opponentTurn.EnergyProblem);
    }
    [Fact] public void DuplicateCommandReturnsOriginalResultWithoutSecondMutation()
    {
        var c=Hand("WCG-101");var command=Cmd("energy",c.InstanceId);
        var a=bridge.Submit(command);var b=bridge.Submit(command);
        Assert.True(a.Success);Assert.Same(a,b);Assert.Equal(a.State.Revision,engine.Revision);Assert.Single(engine.Player.EnergyZone);
    }
    [Fact] public void RejectsStaleForeignAndUnknownWithoutConsumingResources()
    {
        var c=Hand("WCG-101");var stale=Cmd("energy",c.InstanceId) with{ExpectedRevision=0};
        Assert.Equal("stale",bridge.Submit(stale).Code);
        Assert.False(bridge.Submit(Cmd("energy",Guid.NewGuid())).Success);
        Assert.False(bridge.Submit(Cmd("attack",Guid.NewGuid(),Guid.NewGuid())).Success);
        Assert.False(bridge.Submit(Cmd("execute-arbitrary-function")).Success);
        Assert.Contains(c,engine.Player.Hand);Assert.Empty(engine.Player.EnergyZone);
    }
    [Fact] public void VisibleStateNeverSerializesHiddenHandDeckOrEnergyFaces()
    {
        engine.Computer.Hand.Clear();engine.Computer.Hand.Add(new(db.GetCard("WCG-040")!));
        engine.Computer.Deck.Clear();engine.Computer.Deck.Add(new(db.GetCard("WCG-098")!));
        engine.Player.EnergyZone.Add(new(db.GetCard("WCG-096")!));engine.Computer.EnergyZone.Add(new(db.GetCard("WCG-095")!));
        var json=JsonSerializer.Serialize(bridge.Snapshot());
        Assert.DoesNotContain("WCG-040",json);Assert.DoesNotContain("WCG-098",json);
        Assert.DoesNotContain("WCG-096",json);Assert.DoesNotContain("WCG-095",json);
    }
    [Fact] public void TargetCancellationPreservesHandAndEnergyThenCanonicalTargetResolves()
    {
        Energy();var c=Hand("WCG-026");var m=Field(engine.Computer,"WCG-101");
        var a=bridge.Submit(Cmd("play",c.InstanceId));Assert.True(a.Success);Assert.Contains(m.InstanceId.ToString(),a.State.Pending!.Targets);
        Assert.True(bridge.Submit(Cmd("cancel")).Success);Assert.Contains(c,engine.Player.Hand);Assert.Equal(12,engine.Player.AvailableEnergy);
        Assert.True(bridge.Submit(Cmd("play",c.InstanceId)).Success);
        Assert.False(bridge.Submit(Cmd("target",target:Guid.NewGuid())).Success);
        Assert.True(bridge.Submit(Cmd("target",target:m.InstanceId)).Success);Assert.DoesNotContain(m,engine.Computer.Field);
    }
    [Fact] public void PresentationOrderCapturesCostAttackDeathAndTrigger()
    {
        Energy();var c=Hand("WCG-101");bridge.Submit(Cmd("play",c.InstanceId));var summon=bridge.Submit(Cmd("choice",option:engine.CurrentPendingChoice!.Options[0].Id));
        Assert.True(summon.Success);Assert.Equal(new[]{"pay","play","summon"},summon.Events.Select(e=>e.Type));
        var attacker=engine.Player.Field.Single();attacker.IsTapped=false;attacker.HasSummoningSickness=false;
        var victim=Field(engine.Computer,"WCG-003");attacker.CurrentPP=2000;
        var response=bridge.Submit(Cmd("attack",attacker.InstanceId,victim.InstanceId));
        var types=response.Events.Select(e=>e.Type).ToList();
        Assert.True(types.IndexOf("attack")<types.IndexOf("death"));Assert.True(types.IndexOf("death")<types.IndexOf("trigger"));
        Assert.True(types.IndexOf("trigger")<types.IndexOf("damage"));
        Assert.All(response.Events,e=>Assert.Equal(response.State.Revision,e.Revision));
        Assert.Equal(response.Events.Length,response.Events.Select(e=>e.Id).Distinct().Count());
    }
    // Presentation-only events for the client effect queue: a deploy ability names its source before its result,
    // and an attachment names the spell and the monster it lands on. Neither changes the resolved state.
    [Fact] public void DeployAbilityAndAttachmentEmitSourceEventsForTheEffectQueue()
    {
        Energy();var c=Hand("WCG-021");bridge.Submit(Cmd("play",c.InstanceId));
        var deck=engine.Player.Deck.Count;var hand=engine.Player.Hand.Count;
        var summon=bridge.Submit(Cmd("choice",option:engine.CurrentPendingChoice!.Options[0].Id));
        Assert.Equal(new[]{"pay","play","summon","effect","draw"},summon.Events.Select(e=>e.Type));
        var effect=summon.Events.Single(e=>e.Type=="effect");Assert.Equal(c.InstanceId,effect.InstanceId);Assert.Equal("進場能力",effect.Label);
        Assert.Equal(deck-1,engine.Player.Deck.Count);Assert.Equal(hand,engine.Player.Hand.Count); // the summoned card left, one card was drawn
        var own=engine.Player.Field.Single();var shield=Hand("WCG-062");
        var cast=bridge.Submit(Cmd("play",shield.InstanceId,own.InstanceId));
        if(cast.State.Pending!=null)cast=bridge.Submit(Cmd("target",target:own.InstanceId));
        Assert.True(cast.Success);
        var attach=cast.Events.Single(e=>e.Type=="attach");
        Assert.Equal(shield.InstanceId,attach.InstanceId);Assert.Equal(own.InstanceId,attach.TargetId);
        var types=cast.Events.Select(e=>e.Type).ToList();Assert.True(types.IndexOf("play")<types.IndexOf("attach"));
        Assert.Contains(bridge.Snapshot().Player.Field.Single().Status,s=>s.StartsWith("聖盾"));
    }
    [Fact] public void CoordinatorRejectsOldPresenterAndMismatchedAcknowledgement()
    {
        using var coordinator=new BattleCoordinator(bridge,engine,NullLogger<BattleCoordinator>.Instance);
        var old=coordinator.Attach();var current=coordinator.Attach();
        Assert.False(coordinator.Submit(old,Cmd("end")).Success);
        var response=coordinator.Submit(current,Cmd("end"));Assert.True(response.Success);Assert.True(coordinator.IsBusy);
        Assert.False(coordinator.Acknowledge(old,response.State.MatchId,response.State.Revision,response.BatchId));
        Assert.False(coordinator.Acknowledge(current,response.State.MatchId,response.State.Revision,Guid.NewGuid()));
        Assert.True(coordinator.Acknowledge(current,response.State.MatchId,response.State.Revision,response.BatchId));Assert.False(coordinator.IsBusy);
        coordinator.Detach(current);Assert.False(coordinator.Submit(current,Cmd("end")).Success);
    }
    [Fact] public void ChoiceOptionsUseCanonicalEngineInstances()
    {
        Energy();var c=Hand("WCG-030");var a=bridge.Submit(Cmd("play",c.InstanceId));Assert.True(a.Success);
        Assert.Equal("choice",a.State.Pending!.Kind);
        Assert.False(bridge.Submit(Cmd("choice",option:"made-up")).Success);
        Assert.True(bridge.Submit(Cmd("choice",option:a.State.Pending.Options[0].Id)).Success);
    }
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    [Fact] public async Task MissingAnimationAcknowledgementResyncsWithoutPermanentWait()
    {
        var clock = new ManualClock();
        using var coordinator = new BattleCoordinator(bridge, engine, NullLogger<BattleCoordinator>.Instance, clock) { Fast = true };
        var token = coordinator.Attach(); var command = coordinator.Submit(token, Cmd("end"));
        Assert.True(command.Success); Assert.True(coordinator.IsBusy);
        clock.Now += TimeSpan.FromSeconds(9);
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        BattleResponse? result = null;
        await coordinator.DriveAsync(token, response => { result = response; stop.Cancel(); return Task.CompletedTask; }, stop.Token);
        Assert.Equal("resync", result?.Code); Assert.Empty(result!.Events); Assert.False(coordinator.IsBusy);
        Assert.Equal(command.State.Revision, result.State.Revision);
    }
    [Fact] public async Task AiExceptionPublishesActionableErrorAndResetRecovers()
    {
        engine.EndTurn();
        engine.CurrentPendingChoice = new() { OwnerId = "computer", Options = [new() { Id = "FAIL" }],
            OnChoiceSelected = _ => throw new InvalidOperationException("Injected AI failure") };
        using var coordinator = new BattleCoordinator(bridge, engine, NullLogger<BattleCoordinator>.Instance) { Fast = true };
        var token = coordinator.Attach();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3)); BattleResponse? result = null;
        await coordinator.DriveAsync(token, response => { result = response; stop.Cancel(); return Task.CompletedTask; }, stop.Token);
        Assert.Equal("ai-error", result?.Code); Assert.False(result!.Success); Assert.NotEmpty(result.Message);
        var reset = coordinator.Submit(token, Cmd("reset")); Assert.True(reset.Success); Assert.Equal("", coordinator.Error);
    }
    [Fact] public void SnapshotRemainsDetachedAndResyncClearsOnlyActivePresentersWait()
    {
        var snapshot = bridge.Snapshot(); int count = snapshot.Hand.Length;
        engine.Player.Hand.Clear(); Assert.Equal(count, snapshot.Hand.Length);
        using var coordinator = new BattleCoordinator(bridge, engine, NullLogger<BattleCoordinator>.Instance);
        var token = coordinator.Attach(); var response = coordinator.Submit(token, Cmd("end"));
        Assert.True(response.Success); coordinator.Resync(Guid.NewGuid()); Assert.True(coordinator.IsBusy);
        var sync = coordinator.Resync(token); Assert.False(coordinator.IsBusy); Assert.Empty(sync.Events);
    }

    [Fact] public async Task EnemyDrawAndEnergyEventsNeverContainHiddenCardFaces()
    {
        engine.Player.Hand.Clear(); engine.Computer.Hand.Clear(); engine.Computer.Deck.Clear();
        engine.Computer.Deck.Add(new(db.GetCard("WCG-095")!));
        var end = bridge.Submit(Cmd("end"));
        var draw = Assert.Single(end.Events, e => e.Type == "draw");
        Assert.Equal("computer", draw.Side); Assert.Null(draw.Card); Assert.Null(draw.InstanceId);
        using var coordinator = new BattleCoordinator(bridge, engine, NullLogger<BattleCoordinator>.Instance) { Fast = true };
        var token = coordinator.Attach(); using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        BattleResponse? response = null;
        await coordinator.DriveAsync(token, r => { response = r; stop.Cancel(); return Task.CompletedTask; }, stop.Token);
        Assert.Equal("energy", Assert.Single(response!.Events).Type);
        Assert.DoesNotContain("WCG-095", JsonSerializer.Serialize(response));
    }
    [Fact] public void HandInspectionRevealsOnlyRuleAuthorizedCards()
    {
        Energy(); engine.Computer.Hand.Clear();
        engine.Computer.Hand.Add(new(db.GetCard("WCG-101")!)); engine.Computer.Hand.Add(new(db.GetCard("WCG-121")!));
        var card = Hand("WCG-040"); bridge.Submit(Cmd("play",card.InstanceId)); var response=bridge.Submit(Cmd("choice",option:engine.CurrentPendingChoice!.Options[0].Id));
        Assert.True(response.Success); Assert.Equal(new[] { "WCG-101", "WCG-121" }, response.State.RevealedCards.Select(c => c.CardId));
        Assert.Single(response.State.Pending!.Options);
        Assert.Equal("WCG-121", response.State.Pending.Options[0].Card!.CardId);
    }

}
