using System.Text.Json;
using WcgWeb.Models;
using WcgWeb.Models.Battle;
using WcgWeb.Models.Ranked;
using WcgWeb.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
namespace WcgTests;

public sealed class RankedTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "wcg-ranked-test-" + Guid.NewGuid());
    readonly IWebHostEnvironment env;
    readonly CardDatabase cards;
    readonly RankedDecks decks;
    readonly TestClock clock = new();
    readonly RankedStore store;
    public RankedTests()
    {
        var mock = new Mock<IWebHostEnvironment>(); mock.Setup(x => x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../WcgWeb")));
        env = mock.Object; cards = new(env); decks = new(cards, env);
        store = new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Ranked:SavePath"] = Path.Combine(dir, "ranked.json") }).Build());
    }
    RankedSession Session() => new(cards, env, store, decks, NullLogger<BattleCoordinator>.Instance, clock);
    BattleResponse Send(RankedSession s, string type, Guid? id = null, Guid? target = null, string? option = null)
    { var state = s.Bridge.Snapshot(); return s.Bridge.Submit(new(Guid.NewGuid(), state.MatchId, state.Revision, type, id, target, option)); }
    static object StateShape(GameEngine e) => new {
        e.TurnNumber, e.CurrentTurnPlayerId, e.CurrentPhase, e.DecisionPlayerId,
        sides = new[] {e.Player,e.Computer}.Select(p=>new {p.Hp,p.HasLost,p.HasFilledEnergyThisTurn,
            hand=p.Hand.Select(c=>c.Card.Id),deck=p.Deck.Select(c=>c.Card.Id),energy=p.EnergyZone.Select(c=>new {c.Card.Id,c.IsTapped}),
            field=p.Field.Select(m=>new {m.Card.Id,m.CurrentPP,m.CurrentDP,m.HasAttacked,m.HasSummoningSickness,m.IsSilenced,m.IsStealthed,m.ShieldCount}),
            grave=p.Graveyard.Select(c=>c.Card.Id)}),
        choice=e.CurrentPendingChoice?.Options.Select(o=>new{o.Title,Card=o.PreviewCard?.Id}),target=e.CurrentPendingTarget?.Title };
    [Theory]
    [InlineData(0,false,0)] [InlineData(5,false,5)] [InlineData(14,false,14)] [InlineData(14,true,15)] [InlineData(15,false,14)]
    [InlineData(20,false,19)] [InlineData(29,true,30)] [InlineData(74,true,75)]
    [InlineData(30,false,29)] [InlineData(45,false,44)] [InlineData(60,false,59)]
    [InlineData(75,false,74)] [InlineData(76,false,75)] [InlineData(75,true,76)]
    public void StarProgressProtectsOnlyBronze(int before,bool win,int after) => Assert.Equal(after,RankedRules.Apply(before,win));
    [Theory] [InlineData(0,0)] [InlineData(15,0)] [InlineData(30,0)] [InlineData(45,15)] [InlineData(60,30)] [InlineData(90,45)]
    public void MonthlyDropIsTwoTiers(int before,int after)=>Assert.Equal(after,RankedRules.Reset(before));
    [Fact] public void TaiwanMidnightAndSeveralMissedMonths()
    {
        var p=new RankedProfile{Season="2026-10",Stars=80,BestStars=90,SeasonBest=85,Wins=3};
        RankedRules.Advance(p,new DateTimeOffset(2026,10,31,15,59,59,TimeSpan.Zero));Assert.Equal(80,p.Stars);
        RankedRules.Advance(p,new DateTimeOffset(2026,10,31,16,0,0,TimeSpan.Zero));Assert.Equal("2026-11",p.Season);Assert.Equal(45,p.Stars);
        Assert.Single(p.History);Assert.Equal(80,p.History[0].EndingStars);Assert.Equal(90,p.BestStars);
        RankedRules.Advance(p,new DateTimeOffset(2027,1,1,0,0,0,TimeSpan.Zero));Assert.Equal(0,p.Stars);Assert.Equal(3,p.History.Count);
        RankedRules.Advance(p,new DateTimeOffset(2027,1,5,0,0,0,TimeSpan.Zero));Assert.Equal(3,p.History.Count);
    }
    [Fact] public void ActiveMatchDefersSeasonUntilSettlement()
    {
        var p=new RankedProfile{Season="2026-09",Stars=60,Match=new(){Season="2026-09"}};
        RankedRules.Advance(p,clock.GetUtcNow());Assert.Equal("2026-09",p.Season);
        p.Match.Settled=true;RankedRules.Advance(p,clock.GetUtcNow());Assert.Equal("2026-10",p.Season);Assert.Equal(30,p.Stars);
    }
    [Fact] public async Task ReloadReplaysPlayerAndAiActionsExactly()
    {
        var s=Session();Assert.True(s.Start(cards.PresetDecks[0]).Success);var lease=s.Coordinator.Attach();s.Coordinator.Fast=true;
        // A surrender is legal on either turn; another test below resumes pending choices.
        var before=JsonSerializer.Serialize(StateShape(s.Engine));var reloaded=Session();Assert.Empty(reloaded.Error);
        Assert.Equal(before,JsonSerializer.Serialize(StateShape(reloaded.Engine)));
        if(s.Engine.DecisionPlayerId=="player")
        {
            Assert.True(Send(s,"energy",s.Engine.Player.Hand[0].InstanceId).Success);
            Assert.True(Send(s,"end").Success);
        }
        // Drive one real AI step through the coordinator, the same path used by the browser.
        using var cancel=new CancellationTokenSource();
        await s.Coordinator.DriveAsync(lease,r=>{cancel.Cancel();return Task.CompletedTask;},cancel.Token);
        reloaded=Session();Assert.Empty(reloaded.Error);
        Assert.Equal(JsonSerializer.Serialize(StateShape(s.Engine)),JsonSerializer.Serialize(StateShape(reloaded.Engine)));
        Assert.NotEmpty(reloaded.Read().Match!.Actions);
    }
    [Theory]
    [InlineData(0,0)] [InlineData(14,14)] [InlineData(15,14)] [InlineData(22,21)]
    [InlineData(30,29)] [InlineData(45,44)] [InlineData(60,59)] [InlineData(75,74)]
    public void SurrenderIsSettledOnceAndSurvivesRestart(int before,int after)
    {
        store.Save(new(){Season="2026-10",Stars=before,SeasonBest=before,BestStars=before});var s=Session();Assert.True(s.Start(cards.PresetDecks[0]).Success);
        var state=s.Bridge.Snapshot();var cmd=new BattleCommand(Guid.NewGuid(),state.MatchId,state.Revision,"surrender");
        Assert.True(s.Bridge.Submit(cmd).Success);Assert.Equal(after,s.Read().Stars);Assert.False(s.Bridge.Submit(cmd).Success);
        var restored=Session();Assert.Equal(after,restored.Read().Stars);Assert.Equal(1,restored.Read().Losses);Assert.True(restored.Engine.IsOver);
        Assert.Equal(before,restored.Read().BestStars);Assert.Equal(before,restored.Read().SeasonBest);
        Assert.Equal(before,restored.Read().Result!.Before);Assert.Equal(after,restored.Read().Result!.After);
        Assert.False(Send(restored,"surrender").Success);Assert.Equal(1,restored.Read().Losses);
        Assert.True(restored.Start(cards.PresetDecks[0]).Success);Assert.Equal(RankedRules.Tier(after),restored.Read().Match!.Tier);
    }
    [Fact] public void RankedCannotReplaceAnActiveMatchOrUsePracticeStart()
    {
        var s=Session();Assert.True(s.Start(cards.PresetDecks[0]).Success);var id=s.Read().Match!.Id;
        Assert.False(s.Start(cards.PresetDecks[1]).Success);Assert.False(Send(s,"start").Success);Assert.False(Send(s,"reset").Success);
        Assert.Equal(id,s.Read().Match!.Id);
    }
    [Fact] public void DeckListUpdateKeepsTheSavedOpponentAndItsReplay()
    {
        var current=decks.Pool(5).Single(e=>e.Deck.Id=="RANKED-5-WRATH-SCORCH").Deck;
        var previous=DeckService.Copy(current);
        var replaced=previous.CardIds.GroupBy(id=>id).First(g=>g.Count()>=2).Key;
        var previousCard=cards.AllCards.First(c=>c.Will==current.MainWill&&!current.CardIds.Contains(c.Id)).Id;
        for(var i=0;i<2;i++)
        {
            Assert.True(previous.CardIds.Remove(replaced));previous.CardIds.Add(previousCard);
        }
        Assert.True(decks.IsLegal(previous,out var error),error);
        store.Save(new(){Season="2026-10",Stars=75,BestStars=75,SeasonBest=75});
        var original=Session();Assert.True(original.Start(cards.PresetDecks[0]).Success);
        var profile=original.Read();profile.Match!.ComputerDeck=previous;profile.Match.PlayerFirst=true;store.Save(profile);
        var old=Session();Assert.True(Send(old,"energy",old.Engine.Player.Hand[0].InstanceId).Success);
        var shape=JsonSerializer.Serialize(StateShape(old.Engine));var rules=old.Read().Match!.Rules;
        var restored=Session();Assert.Empty(restored.Error);
        Assert.Equal(previous.CardIds,restored.Read().Match!.ComputerDeck.CardIds);
        Assert.Equal(rules,restored.Read().Match!.Rules);Assert.Equal(75,restored.Read().Stars);
        Assert.Equal(shape,JsonSerializer.Serialize(StateShape(restored.Engine)));
        Assert.Contains(previousCard,restored.Engine.Computer.Hand.Concat(restored.Engine.Computer.Deck).Select(c=>c.Card.Id));
        Assert.DoesNotContain(previousCard,current.CardIds);
        Assert.True(Send(restored,"surrender").Success);
        Assert.Empty(restored.BuildReplay(restored.Read().Records!.Last().Id).Error);
        Assert.Equal(current.CardIds,decks.Pool(5).Single(e=>e.Deck.Id==current.Id).Deck.CardIds);
    }
    [Fact] public void BalanceUpdatePreservesActiveOldMatchAndUsesNewRulesOnNextMatch()
    {
        var oldCards=Overlay(Overlay(Overlay(Overlay(Overlay(cards,"balance-before-player-targets.json"),"balance-before-diversity.json"),"balance-before-card-text.json"),"balance-before-arrow-cards.json"),"balance-before-unlimited-faction-triggers.json");
        var deck=DeckService.Copy(oldCards.PresetDecks[1]);deck.CardIds.RemoveRange(0,4);deck.CardIds.AddRange(Enumerable.Repeat("WCG-185",4));
        var oldSession=new RankedSession(oldCards,env,store,new RankedDecks(oldCards,env),NullLogger<BattleCoordinator>.Instance,clock);
        Assert.True(oldSession.Start(deck).Success);
        var profile=oldSession.Read();profile.Match!.PlayerFirst=true;store.Save(profile);
        oldSession=new(oldCards,env,store,new RankedDecks(oldCards,env),NullLogger<BattleCoordinator>.Instance,clock);
        Assert.True(Send(oldSession,"energy",oldSession.Engine.Player.Hand[0].InstanceId).Success);
        var before=JsonSerializer.Serialize(StateShape(oldSession.Engine));
        var restored=Session();Assert.Empty(restored.Error);Assert.NotEmpty(restored.BalanceNotice);
        Assert.Equal(before,JsonSerializer.Serialize(StateShape(restored.Engine)));
        Assert.Equal(5,restored.Engine.Player.Hand.Concat(restored.Engine.Player.Deck).Concat(restored.Engine.Player.EnergyZone).First(c=>c.Card.Id=="WCG-185").Card.TotalCost);
        Assert.Equal(4,cards.GetCard("WCG-185")!.TotalCost);Assert.Equal(4,cards.GetCard("WCG-190")!.TotalCost);
        Assert.True(Send(restored,"surrender").Success);Assert.True(restored.Start(deck).Success);
        Assert.Empty(restored.BalanceNotice);
        Assert.Equal(4,restored.Engine.Player.Hand.Concat(restored.Engine.Player.Deck).First(c=>c.Card.Id=="WCG-185").Card.TotalCost);
        Assert.Empty(Session().Error);
    }
    [Fact] public void DamageCounterUpgradePreservesOldMatchAndNextMatchUsesHandCounters()
    {
        var oldCards=CounterHistory.Before(cards);
        var old=new RankedSession(oldCards,env,store,new RankedDecks(oldCards,env),NullLogger<BattleCoordinator>.Instance,clock);
        Assert.True(old.Start(oldCards.PresetDecks[0]).Success);Assert.False(old.Engine.DamageCounters);
        var before=JsonSerializer.Serialize(StateShape(old.Engine));
        var restored=Session();Assert.Empty(restored.Error);Assert.False(restored.Engine.DamageCounters);
        Assert.NotEmpty(restored.BalanceNotice);Assert.Equal(before,JsonSerializer.Serialize(StateShape(restored.Engine)));
        Assert.True(Send(restored,"surrender").Success);Assert.Empty(restored.BuildReplay(restored.Read().Records!.Last().Id).Error);
        Assert.True(restored.Start(cards.PresetDecks[0]).Success);Assert.True(restored.Engine.DamageCounters);
        Assert.True(Session().Engine.DamageCounters);Assert.Empty(Session().Error);
    }
    [Fact] public void CurrentPendingHandCounterRestoresAndItsChoiceIsJournaled()
    {
        var deck=DeckService.Copy(cards.PresetDecks[0]);
        // Keep the legal Fury starter, replace a playset with barriers for a two-faction reaction fixture.
        deck.CardIds.RemoveRange(0,4);deck.CardIds.AddRange(Enumerable.Repeat("WCG-196",4));
        var session=Session();Assert.True(session.Start(deck).Success);
        var saved=session.Read();saved.Match!.PlayerFirst=true;
        // Select a deterministic opening containing a counter; no live engine state is edited.
        var seed=Enumerable.Range(1,200).First(seed=>
        {
            var preview=new GameEngine(cards,new Random(seed));preview.StartGame(deck,saved.Match.ComputerDeck,true);
            return preview.Player.Hand.Any(c=>c.Card.IsCounter);
        });
        saved.Match.Seed=seed;store.Save(saved);session=Session();
        var lease=session.Coordinator.Attach();session.Coordinator.AiPaused=true;
        for(var n=0;n<300&&!session.Engine.IsOver;n++)
        {
            var engine=session.Engine;
            if(engine.CurrentPendingChoice is {OwnerId:"player"} choice && choice.Title=="受到傷害，可從手牌發動反擊")
            {
                var before=JsonSerializer.Serialize(StateShape(engine));var restored=Session();Assert.Empty(restored.Error);
                Assert.Equal(before,JsonSerializer.Serialize(StateShape(restored.Engine)));Assert.True(restored.Engine.IsWaiting);
                Assert.True(restored.Engine.DamageCounters);Assert.True(Send(restored,"choice",option:"SKIP").Success);
                var after=JsonSerializer.Serialize(StateShape(restored.Engine));Assert.Equal(after,JsonSerializer.Serialize(StateShape(Session().Engine)));
                Assert.Equal(GameEngine.RankedAiVersion,restored.Read().Match!.Actions.Last().RulesVersion);return;
            }
            if(engine.DecisionPlayerId=="computer"){session.Coordinator.Resync(lease);Assert.True(session.Coordinator.StepAi(lease).Success);continue;}
            if(engine.CurrentPendingChoice is {} other){Assert.True(Send(session,"choice",option:other.Options.First().Id).Success);continue;}
            if(!engine.Player.HasFilledEnergyThisTurn)
            {
                var fill=engine.Player.Hand.FirstOrDefault(c=>!c.Card.IsCounter);
                if(fill!=null){Assert.True(Send(session,"energy",fill.InstanceId).Success);continue;}
            }
            Assert.True(Send(session,"end").Success);
        }
        Assert.Fail("Fixture never reached a legal hand counter window.");
    }
    CardDatabase Overlay(CardDatabase current,string file)
    {
        current=CounterHistory.Before(current);
        var prior=JsonSerializer.Deserialize<List<CardDefinition>>(File.ReadAllText(Path.Combine(env.ContentRootPath,"Data",file)))!.ToDictionary(c=>c.Id);
        return new(JsonSerializer.Serialize(current.AllCards.Select(c=>prior.GetValueOrDefault(c.Id,c))),JsonSerializer.Serialize(current.PresetDecks));
    }
    static string Hash(CardDatabase c)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(c.AllCards))));
    private static Deck HealingDeck()
    {
        var deck = DeckService.CreateStarterDeck();
        deck.CardIds = deck.CardIds.Select(id => id == "WCG-003" ? "WCG-050" : id).ToList();
        return deck;
    }
    [Fact, Trait("Category", "Slow")] public void PlayerHealingJournalResumesAndReplaysBothHeroTargets()
    {
        foreach (var target in new[] { GameEngine.PlayerTargetId, GameEngine.ComputerTargetId })
        {
            store.Save(new() { Season = "2026-10", Stars = 22, BestStars = 22, SeasonBest = 22 });
            var session = Session(); Assert.True(session.Start(HealingDeck()).Success);
            var profile = session.Read(); profile.Match!.PlayerFirst = true;
            // Find a deterministic opening containing the existing four-copy healing spell.
            for (var seed = 0; seed < 50; seed++)
            {
                profile.Match.Seed = seed; store.Save(profile); session = Session();
                if (session.Engine.Player.Hand.Any(c => c.Card.Id == "WCG-050")) break;
            }
            var healing = session.Engine.Player.Hand.First(c => c.Card.Id == "WCG-050");
            var lease = session.Coordinator.Attach(); session.Coordinator.AiPaused = true;
            Assert.True(Send(session, "energy", session.Engine.Player.Hand.First(c => c != healing).InstanceId).Success);
            Assert.True(Send(session, "end").Success);
            for (var i = 0; session.Engine.DecisionPlayerId == "computer" && i < 100; i++)
            {
                var step = session.Coordinator.StepAi(lease); Assert.True(step.Success);
                session.Coordinator.Acknowledge(lease, step.State.MatchId, step.State.Revision, step.BatchId);
            }
            Assert.Equal("player", session.Engine.DecisionPlayerId);
            Assert.True(Send(session, "energy", session.Engine.Player.Hand.First(c => c.Card.Id != "WCG-050").InstanceId).Success);
            healing = session.Engine.Player.Hand.First(c => c.Card.Id == "WCG-050");
            Assert.True(Send(session, "play", healing.InstanceId).Success);
            var pending = Session(); Assert.Empty(pending.Error); Assert.NotNull(pending.Engine.CurrentPendingTarget?.PlayerValidator);
            Assert.True(Send(pending, "target", target: target).Success);
            Assert.Equal(target == GameEngine.PlayerTargetId ? 2 : 3, pending.Read().Match!.Actions.Last().TargetSide);
            var shape = JsonSerializer.Serialize(StateShape(pending.Engine));
            var restored = Session(); Assert.Empty(restored.Error); Assert.Equal(22, restored.Read().Stars);
            Assert.Equal(shape, JsonSerializer.Serialize(StateShape(restored.Engine)));
            Assert.True(Send(restored, "surrender").Success);
            Assert.Empty(restored.BuildReplay(restored.Read().Records!.Last().Id).Error);
        }
    }
    [Fact] public void PrePlayerTargetMatchKeepsOldHealingAndRankAfterUpgrade()
    {
        var prior = Overlay(cards, "balance-before-player-targets.json");
        Assert.Equal("88C0C870C9CC1E0CEA81399269174E23D1A1E7649EA41F6BB91DCAE57EABA187", Hash(prior));
        store.Save(new() { Season = "2026-10", Stars = 22, BestStars = 22, SeasonBest = 22 });
        var old = new RankedSession(prior, env, store, new RankedDecks(prior, env), NullLogger<BattleCoordinator>.Instance, clock);
        Assert.True(old.Start(HealingDeck()).Success);
        var before = JsonSerializer.Serialize(StateShape(old.Engine));
        var current = Session(); Assert.Empty(current.Error); Assert.NotEmpty(current.BalanceNotice);
        Assert.Equal(22, current.Read().Stars); Assert.Equal(before, JsonSerializer.Serialize(StateShape(current.Engine)));
        Assert.Equal("回復 2 點生命。", current.Engine.Player.Hand.Concat(current.Engine.Player.Deck).First(c => c.Card.Id == "WCG-050").Card.Text);
        Assert.True(Send(current, "surrender").Success);
        Assert.Empty(current.BuildReplay(current.Read().Records!.Last().Id).Error);
    }
    [Fact] public void DiversityUpdateResumesPostClarityMatchWithoutEnablingLegacyEffects()
    {
        var prior=Overlay(Overlay(cards,"balance-before-player-targets.json"),"balance-before-diversity.json");
        store.Save(new(){Season="2026-10",Stars=22,BestStars=22,SeasonBest=22});
        var old=new RankedSession(prior,env,store,new RankedDecks(prior,env),NullLogger<BattleCoordinator>.Instance,clock);
        var deck=DeckService.Copy(prior.PresetDecks[0]);Assert.True(old.Start(deck).Success);
        var profile=old.Read();profile.Match!.PlayerFirst=true;store.Save(profile);
        old=new(prior,env,store,new RankedDecks(prior,env),NullLogger<BattleCoordinator>.Instance,clock);
        Assert.True(Send(old,"energy",old.Engine.Player.Hand[0].InstanceId).Success);
        var before=JsonSerializer.Serialize(StateShape(old.Engine));var restored=Session();
        Assert.Empty(restored.Error);Assert.NotEmpty(restored.BalanceNotice);Assert.Equal(22,restored.Read().Stars);
        Assert.Equal(before,JsonSerializer.Serialize(StateShape(restored.Engine)));Assert.False(CardTextClarityTests.IsLegacy(restored.Engine));
        Assert.True(Send(restored,"surrender").Success);
        var replay=restored.BuildReplay(restored.Read().Records!.Last().Id);Assert.Empty(replay.Error);Assert.True(replay.Steps.Count>=2);
        Assert.True(restored.Start(deck).Success);Assert.Empty(restored.BalanceNotice);Assert.False(CardTextClarityTests.IsLegacy(restored.Engine));
        Assert.Equal(3,restored.Engine.Player.Hand.Concat(restored.Engine.Player.Deck).First(c=>c.Card.Id=="WCG-124").Card.TotalCost);
    }
    [Fact] public void AddingHeroGuardKeepsReleasedMatchAndReplayAndAllowsNewCardNextMatch()
    {
        var prior=CounterHistory.BeforeHeroGuard(cards);Assert.Equal(199,prior.AllCards.Count);Assert.Null(prior.GetCard("WCG-200"));
        store.Save(new(){Season="2026-10",Stars=22,BestStars=22,SeasonBest=22});
        var old=new RankedSession(prior,env,store,new RankedDecks(prior,env),NullLogger<BattleCoordinator>.Instance,clock);
        var deck=DeckService.Copy(prior.PresetDecks[3]);Assert.True(old.Start(deck).Success);
        var profile=old.Read();profile.Match!.PlayerFirst=true;store.Save(profile);old.Coordinator.Dispose();
        old=new(prior,env,store,new RankedDecks(prior,env),NullLogger<BattleCoordinator>.Instance,clock);
        Assert.True(Send(old,"energy",old.Engine.Player.Hand[0].InstanceId).Success);
        var shape=JsonSerializer.Serialize(StateShape(old.Engine));var rules=old.Read().Match!.Rules;old.Coordinator.Dispose();
        var current=Session();Assert.Empty(current.Error);Assert.NotEmpty(current.BalanceNotice);Assert.Equal(22,current.Read().Stars);
        Assert.Equal(rules,current.Read().Match!.Rules);Assert.Equal(shape,JsonSerializer.Serialize(StateShape(current.Engine)));
        Assert.True(Send(current,"surrender").Success);Assert.Empty(current.BuildReplay(current.Read().Records!.Last().Id).Error);
        deck.CardIds=Enumerable.Repeat("WCG-200",4).Concat(deck.CardIds.Skip(4)).ToList();
        Assert.True(deck.IsValid(cards.GetCard,out _));Assert.True(current.Start(deck).Success);Assert.NotEqual(rules,current.Read().Match!.Rules);
        Assert.Equal(4,current.Engine.Player.Hand.Concat(current.Engine.Player.Deck).Count(c=>c.Card.Id=="WCG-200"));
        current.Coordinator.Dispose();
    }

    [Fact] public void NewCardsPreserveActiveMatchAndReplay()
    {
        var prior=CounterHistory.BeforeNewCards(cards);
        Assert.Equal(204,prior.AllCards.Count);Assert.Null(prior.GetCard("WCG-205"));Assert.NotNull(cards.GetCard("WCG-257"));
        store.Save(new(){Season="2026-10",Stars=22,BestStars=22,SeasonBest=22});
        var old=new RankedSession(prior,env,store,new RankedDecks(prior,env),NullLogger<BattleCoordinator>.Instance,clock);
        var deck=DeckService.Copy(prior.PresetDecks[4]);Assert.True(old.Start(deck).Success);
        var profile=old.Read();profile.Match!.PlayerFirst=true;store.Save(profile);old.Coordinator.Dispose();
        old=new(prior,env,store,new RankedDecks(prior,env),NullLogger<BattleCoordinator>.Instance,clock);
        Assert.True(Send(old,"energy",old.Engine.Player.Hand[0].InstanceId).Success);
        var shape=JsonSerializer.Serialize(StateShape(old.Engine));var rules=old.Read().Match!.Rules;old.Coordinator.Dispose();
        var current=Session();Assert.Empty(current.Error);Assert.NotEmpty(current.BalanceNotice);Assert.Equal(22,current.Read().Stars);
        Assert.Equal(rules,current.Read().Match!.Rules);Assert.Equal(shape,JsonSerializer.Serialize(StateShape(current.Engine)));
        Assert.True(Send(current,"surrender").Success);Assert.Empty(current.BuildReplay(current.Read().Records!.Last().Id).Error);
        Assert.True(current.Start(deck).Success);Assert.Empty(current.BalanceNotice);Assert.NotEqual(rules,current.Read().Match!.Rules);
        current.Coordinator.Dispose();
    }

    [Fact] public void RulesSimplificationPreservesActiveMatchAndReplay()
    {
        var prior=CounterHistory.BeforeRulesSimplification(cards);
        Assert.False(prior.GetCard("WCG-200")!.Text.StartsWith("你受到傷害時"));Assert.StartsWith("你受到傷害時",cards.GetCard("WCG-200")!.Text);
        Assert.NotEmpty(prior.GetCard("WCG-200")!.Keywords);Assert.Empty(cards.GetCard("WCG-200")!.Keywords);
        store.Save(new(){Season="2026-10",Stars=22,BestStars=22,SeasonBest=22});
        var old=new RankedSession(prior,env,store,new RankedDecks(prior,env),NullLogger<BattleCoordinator>.Instance,clock);
        var deck=DeckService.Copy(prior.PresetDecks[4]);Assert.True(old.Start(deck).Success);
        var profile=old.Read();profile.Match!.PlayerFirst=true;store.Save(profile);old.Coordinator.Dispose();
        old=new(prior,env,store,new RankedDecks(prior,env),NullLogger<BattleCoordinator>.Instance,clock);
        Assert.True(Send(old,"energy",old.Engine.Player.Hand[0].InstanceId).Success);
        var shape=JsonSerializer.Serialize(StateShape(old.Engine));var rules=old.Read().Match!.Rules;old.Coordinator.Dispose();
        var current=Session();Assert.Empty(current.Error);Assert.NotEmpty(current.BalanceNotice);Assert.Equal(22,current.Read().Stars);
        Assert.Equal(rules,current.Read().Match!.Rules);Assert.Equal(shape,JsonSerializer.Serialize(StateShape(current.Engine)));
        Assert.True(Send(current,"surrender").Success);Assert.Empty(current.BuildReplay(current.Read().Records!.Last().Id).Error);
        Assert.True(current.Start(deck).Success);Assert.Empty(current.BalanceNotice);Assert.NotEqual(rules,current.Read().Match!.Rules);
        current.Coordinator.Dispose();
    }

    [Fact] public void AddingEnergyKeywordsPreserves200CardMatchAndReplay()
    {
        var prior=CounterHistory.BeforeEnergyKeywords(cards);Assert.Equal(200,prior.AllCards.Count);Assert.Null(prior.GetCard("WCG-201"));
        store.Save(new(){Season="2026-10",Stars=22,BestStars=22,SeasonBest=22});
        var old=new RankedSession(prior,env,store,new RankedDecks(prior,env),NullLogger<BattleCoordinator>.Instance,clock);
        var deck=DeckService.Copy(prior.PresetDecks[0]);Assert.True(old.Start(deck).Success);
        var profile=old.Read();profile.Match!.PlayerFirst=true;store.Save(profile);old.Coordinator.Dispose();
        old=new(prior,env,store,new RankedDecks(prior,env),NullLogger<BattleCoordinator>.Instance,clock);
        Assert.True(Send(old,"energy",old.Engine.Player.Hand[0].InstanceId).Success);
        var shape=JsonSerializer.Serialize(StateShape(old.Engine));var rules=old.Read().Match!.Rules;old.Coordinator.Dispose();
        var current=Session();Assert.Empty(current.Error);Assert.NotEmpty(current.BalanceNotice);Assert.Equal(22,current.Read().Stars);
        Assert.Equal(rules,current.Read().Match!.Rules);Assert.Equal(shape,JsonSerializer.Serialize(StateShape(current.Engine)));
        Assert.True(Send(current,"surrender").Success);Assert.Empty(current.BuildReplay(current.Read().Records!.Last().Id).Error);
        deck.CardIds=Enumerable.Repeat("WCG-201",4).Concat(deck.CardIds.Skip(4)).ToList();
        Assert.True(deck.IsValid(cards.GetCard,out _));Assert.True(current.Start(deck).Success);Assert.NotEqual(rules,current.Read().Match!.Rules);
        Assert.Equal(4,current.Engine.Player.Hand.Concat(current.Engine.Player.Deck).Count(c=>c.Card.Id=="WCG-201"));
        current.Coordinator.Dispose();
    }

    [Fact] public void DeflationPreservesPreDeflationMatchAndReplay()
    {
        var prior=CounterHistory.BeforeDeflation(cards);Assert.Equal(4000,prior.GetCard("WCG-161")!.PP);Assert.Equal(7,prior.GetCard("WCG-160")!.TotalCost);
        Assert.Equal(3300,cards.GetCard("WCG-161")!.PP);Assert.Equal(8,cards.GetCard("WCG-160")!.TotalCost);
        store.Save(new(){Season="2026-10",Stars=22,BestStars=22,SeasonBest=22});
        var old=new RankedSession(prior,env,store,new RankedDecks(prior,env),NullLogger<BattleCoordinator>.Instance,clock);
        var deck=DeckService.Copy(prior.PresetDecks[0]);Assert.True(old.Start(deck).Success);
        var profile=old.Read();profile.Match!.PlayerFirst=true;store.Save(profile);old.Coordinator.Dispose();
        old=new(prior,env,store,new RankedDecks(prior,env),NullLogger<BattleCoordinator>.Instance,clock);
        Assert.True(Send(old,"energy",old.Engine.Player.Hand[0].InstanceId).Success);
        var shape=JsonSerializer.Serialize(StateShape(old.Engine));var rules=old.Read().Match!.Rules;old.Coordinator.Dispose();
        var current=Session();Assert.Empty(current.Error);Assert.Equal(22,current.Read().Stars);
        Assert.Equal(rules,current.Read().Match!.Rules);Assert.Equal(shape,JsonSerializer.Serialize(StateShape(current.Engine)));
        Assert.True(Send(current,"surrender").Success);Assert.Empty(current.BuildReplay(current.Read().Records!.Last().Id).Error);
        Assert.True(current.Start(DeckService.Copy(cards.PresetDecks[0])).Success);Assert.NotEqual(rules,current.Read().Match!.Rules);
        current.Coordinator.Dispose();
    }

    [Fact] public void StrongBuildAdjustmentPreservesActiveMatchReplayAndNextMatchUsesNewValues()
    {
        var prior=CounterHistory.BeforeStrongBuildAdjustment(cards);
        Assert.Equal(1800,prior.GetCard("WCG-146")!.PP);
        Assert.Equal(4000,prior.GetCard("WCG-079")!.PP);
        Assert.Equal(1100,prior.GetCard("WCG-148")!.PP);
        Assert.Equal(6,prior.GetCard("WCG-194")!.TotalCost);
        Assert.Equal(2,prior.GetCard("WCG-186")!.TotalCost);
        Assert.Equal(4,prior.GetCard("WCG-017")!.TotalCost);
        store.Save(new(){Season="2026-10",Stars=22,BestStars=22,SeasonBest=22});
        var old=new RankedSession(prior,env,store,new RankedDecks(prior,env),NullLogger<BattleCoordinator>.Instance,clock);
        var deck=DeckService.Copy(prior.PresetDecks[4]);
        Assert.True(old.Start(deck).Success);
        var profile=old.Read();profile.Match!.PlayerFirst=true;store.Save(profile);
        old.Coordinator.Dispose();
        old=new(prior,env,store,new RankedDecks(prior,env),NullLogger<BattleCoordinator>.Instance,clock);
        Assert.True(Send(old,"energy",old.Engine.Player.Hand[0].InstanceId).Success);
        var shape=JsonSerializer.Serialize(StateShape(old.Engine));var oldRules=old.Read().Match!.Rules;
        old.Coordinator.Dispose();
        var restored=Session();Assert.Empty(restored.Error);Assert.NotEmpty(restored.BalanceNotice);
        Assert.Equal(22,restored.Read().Stars);Assert.Equal(oldRules,restored.Read().Match!.Rules);
        Assert.Equal(shape,JsonSerializer.Serialize(StateShape(restored.Engine)));
        Assert.Equal(6,restored.Engine.Player.Hand.Concat(restored.Engine.Player.Deck).First(c=>c.Card.Id=="WCG-194").Card.TotalCost);
        Assert.True(Send(restored,"surrender").Success);
        Assert.Empty(restored.BuildReplay(restored.Read().Records!.Last().Id).Error);
        Assert.True(restored.Start(deck).Success);Assert.Empty(restored.BalanceNotice);
        Assert.NotEqual(oldRules,restored.Read().Match!.Rules);
        Assert.Equal(5,restored.Engine.Player.Hand.Concat(restored.Engine.Player.Deck).First(c=>c.Card.Id=="WCG-194").Card.TotalCost);
        Assert.Equal(1100,restored.Engine.Player.Hand.Concat(restored.Engine.Player.Deck).First(c=>c.Card.Id=="WCG-148").Card.PP);
        restored.Coordinator.Dispose();
    }

    [Fact] public void OctoberBalancePreservesActiveMatchReplayAndNextMatchUsesNewValues()
    {
        var prior=CounterHistory.BeforeOctoberBalance(cards);
        Assert.Equal(2,prior.GetCard("WCG-082")!.TotalCost);
        Assert.Equal(9,prior.GetCard("WCG-160")!.TotalCost);
        store.Save(new(){Season="2026-10",Stars=22,BestStars=22,SeasonBest=22});
        var old=new RankedSession(prior,env,store,new RankedDecks(prior,env),NullLogger<BattleCoordinator>.Instance,clock);
        var deck=DeckService.Copy(prior.PresetDecks[4]);
        deck.CardIds=deck.CardIds.Select(id=>id=="WCG-095"?"WCG-082":id=="WCG-138"?"WCG-160":id).ToList();
        Assert.True(old.Start(deck).Success);
        var profile=old.Read();profile.Match!.PlayerFirst=true;store.Save(profile);
        old=new(prior,env,store,new RankedDecks(prior,env),NullLogger<BattleCoordinator>.Instance,clock);
        Assert.True(Send(old,"energy",old.Engine.Player.Hand[0].InstanceId).Success);
        var shape=JsonSerializer.Serialize(StateShape(old.Engine));var oldRules=old.Read().Match!.Rules;
        var restored=Session();Assert.Empty(restored.Error);Assert.NotEmpty(restored.BalanceNotice);
        Assert.Equal(22,restored.Read().Stars);Assert.Equal(oldRules,restored.Read().Match!.Rules);
        Assert.Equal(shape,JsonSerializer.Serialize(StateShape(restored.Engine)));
        Assert.True(restored.Engine.DamageCounters);Assert.False(CardTextClarityTests.IsLegacy(restored.Engine));
        Assert.True(Send(restored,"surrender").Success);
        Assert.Empty(restored.BuildReplay(restored.Read().Records!.Last().Id).Error);
        Assert.True(restored.Start(deck).Success);Assert.Empty(restored.BalanceNotice);
        Assert.NotEqual(oldRules,restored.Read().Match!.Rules);
        var arrow=restored.Engine.Player.Hand.Concat(restored.Engine.Player.Deck).First(c=>c.Card.Id=="WCG-082");
        Assert.Equal(3,arrow.Card.TotalCost);
        Assert.Equal(3,cards.GetCard("WCG-082")!.TotalCost);
        Assert.Equal(8,cards.GetCard("WCG-160")!.TotalCost);
    }

    [Fact] public void EnergyShieldRenamePreservesActiveMatchAndItsReplay()
    {
        var prior=CounterHistory.BeforeEnergyShield(cards);
        // Fingerprint read from the deployed pre-rename version; design notes also participate in the hash.
        Assert.Equal("B480EB9937F9EF5760C83B3AB13A6AC39310ABF96F99A29D44154EF2A91FE887",Hash(prior));
        store.Save(new(){Season="2026-10",Stars=22,BestStars=22,SeasonBest=22});
        var old=new RankedSession(prior,env,store,new RankedDecks(prior,env),NullLogger<BattleCoordinator>.Instance,clock);
        var deck=DeckService.Copy(prior.PresetDecks[3]);Assert.True(old.Start(deck).Success);
        var profile=old.Read();profile.Match!.PlayerFirst=true;store.Save(profile);
        old=new(prior,env,store,new RankedDecks(prior,env),NullLogger<BattleCoordinator>.Instance,clock);
        Assert.True(Send(old,"energy",old.Engine.Player.Hand[0].InstanceId).Success);
        var before=JsonSerializer.Serialize(StateShape(old.Engine));var oldRules=old.Read().Match!.Rules;
        var restored=Session();Assert.Empty(restored.Error);Assert.Equal(22,restored.Read().Stars);
        Assert.True(restored.Engine.DamageCounters);Assert.False(CardTextClarityTests.IsLegacy(restored.Engine));
        Assert.Equal(before,JsonSerializer.Serialize(StateShape(restored.Engine)));
        Assert.Equal(oldRules,restored.Read().Match!.Rules);
        Assert.True(Send(restored,"surrender").Success);
        Assert.Empty(restored.BuildReplay(restored.Read().Records!.Last().Id).Error);
        Assert.True(restored.Start(deck).Success);Assert.NotEqual(oldRules,restored.Read().Match!.Rules);
    }

    [Fact] public void HistoricCatalogsReproduceTheReleasedFingerprints()
    {
        // Ranked saves store a hash of the catalog; the overlays must rebuild the exact released catalogs or old matches stop resuming.
        var beforeText=Overlay(Overlay(Overlay(cards,"balance-before-player-targets.json"),"balance-before-diversity.json"),"balance-before-card-text.json");
        // origin/main 43615fc (before 卡牌文字白話化).
        Assert.Equal("E5FF04DB722205E91C3D52DD688EE435F60B40E07B0E445C1B3F05DEA02D6557",Hash(beforeText));
        Assert.NotEqual(Hash(beforeText),Hash(cards));
        var beforeArrows=Overlay(beforeText,"balance-before-arrow-cards.json");
        Assert.Equal("3664E96255F2DBC34CDF76D9BB5760AAD8F97A0E153FFF94F9CC95BCF97EA2CE",Hash(beforeArrows));
        Assert.Equal("4ED3BFBD9FD5B5D2EFB919883D80442392F1B27B160F17119C1EFB0FF235A194",Hash(Overlay(beforeArrows,"balance-before-unlimited-faction-triggers.json")));
        Assert.NotEqual(Hash(beforeArrows),Hash(cards));
    }
    [Fact] public void ArrowExpansionPreservesActivePreArrowMatchAndReplays()
    {
        var oldCards=Overlay(Overlay(Overlay(Overlay(cards,"balance-before-player-targets.json"),"balance-before-diversity.json"),"balance-before-card-text.json"),"balance-before-arrow-cards.json");
        Assert.Empty(oldCards.GetCard("WCG-005")!.Arrows);Assert.Equal("衝鋒。",oldCards.GetCard("WCG-005")!.Text);
        var deck=DeckService.Copy(oldCards.PresetDecks[0]);
        var oldSession=new RankedSession(oldCards,env,store,new RankedDecks(oldCards,env),NullLogger<BattleCoordinator>.Instance,clock);
        Assert.True(oldSession.Start(deck).Success);
        var profile=oldSession.Read();profile.Match!.PlayerFirst=true;store.Save(profile);
        oldSession=new(oldCards,env,store,new RankedDecks(oldCards,env),NullLogger<BattleCoordinator>.Instance,clock);
        Assert.True(Send(oldSession,"energy",oldSession.Engine.Player.Hand[0].InstanceId).Success);
        Assert.Empty(oldSession.Engine.Player.Hand.Concat(oldSession.Engine.Player.Deck).First(c=>c.Card.Id=="WCG-005").Card.Arrows);
        var before=JsonSerializer.Serialize(StateShape(oldSession.Engine));var stars=oldSession.Read().Stars;
        var restored=Session();Assert.Empty(restored.Error);Assert.NotEmpty(restored.BalanceNotice);
        Assert.Equal(before,JsonSerializer.Serialize(StateShape(restored.Engine)));
        Assert.Equal(stars,restored.Read().Stars);
        Assert.True(Send(restored,"surrender").Success);
        var record=restored.Read().Records!.Last();
        var replay=restored.BuildReplay(record.Id);Assert.Empty(replay.Error);Assert.True(replay.Steps.Count>=2);
        Assert.True(restored.Start(deck).Success);Assert.Empty(restored.BalanceNotice);
        var rider=restored.Engine.Player.Hand.Concat(restored.Engine.Player.Deck).First(c=>c.Card.Id=="WCG-005").Card;
        Assert.Equal(["left","right"],rider.Arrows);
    }
    [Fact] public void CardTextUpdatePreservesActiveOldMatchWithLegacyRulesAndReplays()
    {
        var oldCards=Overlay(Overlay(Overlay(cards,"balance-before-player-targets.json"),"balance-before-diversity.json"),"balance-before-card-text.json");
        Assert.Equal(1200,oldCards.GetCard("WCG-147")!.PP);Assert.StartsWith("離場：",oldCards.GetCard("WCG-003")!.Text);
        var deck=DeckService.Copy(oldCards.PresetDecks[0]);
        var oldSession=new RankedSession(oldCards,env,store,new RankedDecks(oldCards,env),NullLogger<BattleCoordinator>.Instance,clock);
        Assert.True(oldSession.Start(deck).Success);
        var profile=oldSession.Read();profile.Match!.PlayerFirst=true;store.Save(profile);
        oldSession=new(oldCards,env,store,new RankedDecks(oldCards,env),NullLogger<BattleCoordinator>.Instance,clock);
        Assert.True(Send(oldSession,"energy",oldSession.Engine.Player.Hand[0].InstanceId).Success);
        var before=JsonSerializer.Serialize(StateShape(oldSession.Engine));var stars=oldSession.Read().Stars;
        var restored=Session();Assert.Empty(restored.Error);Assert.NotEmpty(restored.BalanceNotice);
        Assert.Equal(before,JsonSerializer.Serialize(StateShape(restored.Engine)));Assert.Equal(stars,restored.Read().Stars);
        Assert.True(CardTextClarityTests.IsLegacy(restored.Engine));
        Assert.True(Send(restored,"surrender").Success);
        var replay=restored.BuildReplay(restored.Read().Records!.Last().Id);Assert.Empty(replay.Error);Assert.True(replay.Steps.Count>=2);
        Assert.True(restored.Start(deck).Success);Assert.Empty(restored.BalanceNotice);Assert.False(CardTextClarityTests.IsLegacy(restored.Engine));
    }
    [Fact] public void CorruptSaveIsPreservedAndBlocksRankedOnly()
    {
        Directory.CreateDirectory(dir);File.WriteAllText(store.Path,"broken");var s=Session();Assert.NotEmpty(s.Error);Assert.False(s.Start(cards.PresetDecks[0]).Success);Assert.Equal("broken",File.ReadAllText(store.Path));
    }
    [Fact] public void DeckPoolsAreLegalDistinctAndCoverEveryWill()
    {
        for(int tier=0;tier<6;tier++)
        {
            var pool=decks.All.Where(d=>d.Tier==tier).ToArray();Assert.True(pool.Length>=5);
            Assert.Equal(5,pool.Select(x=>x.Deck.MainWill).Distinct().Count());
            Assert.All(pool,d=>Assert.True(d.Deck.IsValid(cards.GetCard,out _)));
        }
        Assert.True(decks.All.Select(e=>string.Join(',',e.Deck.CardIds.Order())).Distinct().Count()>=5);
    }
    [Fact] public void VersionOneSaveIsArchivedAndTheSeasonRestarts()
    {
        store.Save(new(){Version=1,Season="2026-10",Stars=61,BestStars=80,SeasonBest=70,Wins=9,Losses=4,
            History=[new("2026-09",30,30,1,1)],Match=new(){Season="2026-10",Tier=4},Result=new(Guid.NewGuid(),"2026-10",true,60,61,"")});
        var s=Session();Assert.Empty(s.Error);Assert.NotEmpty(s.ResetNotice);
        var p=s.Read();Assert.Equal(RankedRules.ProfileVersion,p.Version);Assert.Equal(0,p.Stars);Assert.Equal(80,p.BestStars);
        Assert.Equal(0,p.Wins+p.Losses);Assert.Null(p.Match);Assert.Null(p.Result);
        var archived=Assert.Single(p.History,h=>h.Season=="2026-10 舊版電腦");Assert.Equal(61,archived.EndingStars);Assert.Equal(9,archived.Wins);
        Assert.Empty(Session().ResetNotice);Assert.Equal(0,Session().Read().Stars);
        Assert.True(s.Start(cards.PresetDecks[0]).Success);Assert.True(s.Read().Match!.Actions.Count==0);
    }
    [Fact] public void CorruptSaveIsStillPreservedWhenTheVersionCannotBeRead()
    {
        Directory.CreateDirectory(dir);File.WriteAllText(store.Path,"{\"Version\":9}");var s=Session();Assert.NotEmpty(s.Error);Assert.False(s.Start(cards.PresetDecks[0]).Success);
        Assert.Equal("{\"Version\":9}",File.ReadAllText(store.Path));
    }
    [Fact, Trait("Category", "Slow")] public void HigherTiersWinMoreOftenAgainstTheTrialAiAndStayDeterministic()
    {
        int Wins(int level,int games)
        {
            int wins=0;
            for(int seed=0;seed<games;seed++)
            {
                var e=new GameEngine(cards,new Random(seed+800)){AiLevel=level};e.SeedRankedAi(seed);
                e.StartGame(cards.PresetDecks[seed%5],cards.PresetDecks[(seed+2)%5],seed%2==0);e.Player.IsAi=true;
                for(int n=0;n<3000&&!e.IsOver;n++){e.AiLevel=e.DecisionPlayerId=="player"?level:-1;Assert.True(e.ExecuteAiStep(),e.LastError);}
                Assert.True(e.IsOver);if(e.Computer.HasLost)wins++;
            }
            return wins;
        }
        var bronze=Wins(0,40);var silver=Wins(1,40);var master=Wins(5,40);
        Assert.True(bronze<master&&silver<master,$"bronze {bronze}, silver {silver}, master {master}");
        var a=new GameEngine(cards,new Random(5)){AiLevel=3};a.SeedRankedAi(5);a.StartGame(cards.PresetDecks[0],cards.PresetDecks[1],true);a.Player.IsAi=true;
        var b=new GameEngine(cards,new Random(5)){AiLevel=3};b.SeedRankedAi(5);b.StartGame(cards.PresetDecks[0],cards.PresetDecks[1],true);b.Player.IsAi=true;
        for(int n=0;n<12;n++){Assert.True(a.ExecuteAiStep());Assert.True(b.ExecuteAiStep());
            Assert.Equal(JsonSerializer.Serialize(StateShape(a)),JsonSerializer.Serialize(StateShape(b)));}
    }
    [Fact, Trait("Category", "Slow")] public void TieredAiReplaysWholeGamesIdenticallyDespiteFreshInstanceIds()
    {
        // Card instance ids are new GUIDs in every engine, as in a replay; decisions must not depend on them.
        string Play(int level,int seed,Deck mine,Deck theirs)
        {
            var e=new GameEngine(cards,new Random(seed)){AiLevel=level};e.SeedRankedAi(seed);e.StartGame(mine,theirs,seed%2==0);e.Player.IsAi=true;
            var trace=new System.Text.StringBuilder();
            for(int n=0;n<3000&&!e.IsOver;n++){e.AiLevel=e.DecisionPlayerId=="player"?level:(level+3)%6;Assert.True(e.ExecuteAiStep(),e.LastError);
                trace.Append(e.Revision).Append(':').Append(e.Player.Hp).Append('/').Append(e.Computer.Hp).Append('/').Append(e.Player.Hand.Count).Append('/').Append(e.Computer.Field.Count).Append(';');}
            Assert.True(e.IsOver);return trace.ToString();
        }
        var pool=decks.All;
        for(int k=0;k<24;k++)
        {
            int level=k%6;var mine=pool[(k*7)%pool.Count].Deck;var theirs=pool[(k*11+3)%pool.Count].Deck;
            Assert.Equal(Play(level,1000+k,mine,theirs),Play(level,1000+k,mine,theirs));
        }
    }
    // Pinned to v6 (kept for replays of version-6 journals); v7 lethal is covered by AiLethalTests.
    [Fact] public void MasterAiFindsCombinedLethalThatBronzeMisses()
    {
        GameEngine Setup(int level)
        {
            var e=new GameEngine(cards,new Random(1)){AiLevel=level,AiRevision=6};e.SeedRankedAi(1);e.StartGame(cards.PresetDecks[0],cards.PresetDecks[1],false);
            e.Computer.Hand.Clear();e.Player.Hp=2;
            for(int i=0;i<2;i++)e.Computer.Field.Add(new(cards.GetCard("WCG-101")!){IsTapped=false,HasSummoningSickness=false});
            e.Player.Field.Add(new(cards.GetCard("WCG-003")!){IsTapped=false,HasSummoningSickness=false});
            return e;
        }
        var master=Setup(5);Assert.True(master.ExecuteAiStep());Assert.Equal(1,master.Player.Hp);Assert.True(master.ExecuteAiStep());Assert.True(master.Player.HasLost);
        var bronze=Setup(0);var swings=0;
        for(int n=0;n<3&&!bronze.IsOver;n++){Assert.True(bronze.ExecuteAiStep());if(bronze.Player.Field.Count<1)swings++;}
        Assert.False(bronze.Player.HasLost);
    }
    [Fact] public void RankedActionsRecordTheTieredAiVersion()
    {
        var s=Session();Assert.True(s.Start(cards.PresetDecks[0]).Success);
        var state=s.Bridge.Snapshot();Assert.True(s.Bridge.Submit(new(Guid.NewGuid(),state.MatchId,state.Revision,"surrender")).Success);
        Assert.Equal(GameEngine.RankedAiVersion,s.Read().Match!.Actions.Single().RulesVersion);
    }
    static string ArchetypeKey(string id) => id.Split('-',3)[2];
    [Fact] public void ArchetypesAreGatedToSpecificTiers()
    {
        var keys=Enumerable.Range(0,6).Select(t=>decks.Pool(t).Select(e=>ArchetypeKey(e.Deck.Id)).ToHashSet()).ToArray();
        for(int tier=0;tier<6;tier++)
        {
            var pool=decks.Pool(tier);
            Assert.True(pool.Count>=8);Assert.Equal(pool.Count,keys[tier].Count);
            Assert.Equal(5,pool.Select(e=>e.Deck.MainWill).Distinct().Count());
            Assert.All(pool,e=>{Assert.True(decks.IsLegal(e.Deck,out var error),error);Assert.NotEmpty(e.Archetype);Assert.NotEmpty(e.Deck.Description);
                if(tier==5)Assert.Empty(e.Variants!);else Assert.True((e.Variants?.Count??0)>=3);});
            var summary=decks.Summary(tier);Assert.StartsWith($"{pool.Count} 種流派",summary);
            Assert.All(pool,e=>Assert.Contains(RankedDecks.ArchetypeName(e.Deck),summary));
        }
        // 嘲諷回血最強的流派只在大師；冰封堡壘只在鑽石與大師。
        foreach(var key in new[]{"ORDER-RAMPART","ORDER-VERDICT","VITAL-THORN"})
            Assert.Equal([5],Enumerable.Range(0,6).Where(t=>keys[t].Contains(key)));
        Assert.Equal([4,5],Enumerable.Range(0,6).Where(t=>keys[t].Contains("REASON-GLACIER")));
        Assert.Equal([4,5],Enumerable.Range(0,6).Where(t=>keys[t].Contains("ABYSS-ASSASSIN")));
        Assert.Equal([2,3,4,5],Enumerable.Range(0,6).Where(t=>keys[t].Contains("WRATH-SCORCH")));
        Assert.False(keys[2].SetEquals(keys[1]));
        // Rebuilt draw, sacrifice and swarm themes remain available at high tiers; strong control stays gated above.
        foreach(var key in new[]{"REASON-ARCANE","ABYSS-SOULFEAST","REASON-ORACLE","WRATH-WARBAND"}) Assert.Contains(key,keys[5]);
        Assert.True(keys[5].IsSupersetOf(keys[0]));
        Assert.Equal(new[]{12,22,29,30,34,37}, keys.Select(k=>k.Count));
        // PR E 新流派：機制越複雜越晚出現。
        Assert.Equal([0,1,2,3,4,5],Enumerable.Range(0,6).Where(t=>keys[t].Contains("ORDER-MERCENARY")));
        Assert.Equal([1,2,3,4,5],Enumerable.Range(0,6).Where(t=>keys[t].Contains("WRATH-LONEWOLF")));
        foreach(var key in new[]{"VITAL-PHALANX","REASON-PROPHECY","VITAL-ROTCYCLE"}) Assert.Equal([2,3,4,5],Enumerable.Range(0,6).Where(t=>keys[t].Contains(key)));
        Assert.Equal([3,4,5],Enumerable.Range(0,6).Where(t=>keys[t].Contains("REASON-TIDE")));
        Assert.Equal([4,5],Enumerable.Range(0,6).Where(t=>keys[t].Contains("WRATH-WARRUNE")));
        Assert.Contains(decks.Pool(5),e=>e.Deck.CardIds.Any(id=>cards.GetCard(id)!.Wills.Length==2));
        Assert.Contains(decks.Pool(5).Single(e=>ArchetypeKey(e.Deck.Id)=="REASON-ARCANE").Deck.CardIds,id=>id=="WCG-252");
        Assert.DoesNotContain("白銀長城",decks.Summary(4));Assert.Contains("白銀長城",decks.Summary(5));
        Assert.Equal(37,keys.SelectMany(k=>k).Distinct().Count());
        // 大師使用完整牌表；同一流派在較低牌位的牌表是簡化版。
        var thorn=decks.Pool(5).Single(e=>ArchetypeKey(e.Deck.Id)=="VITAL-THORN").Deck;
        Assert.Contains(thorn.CardIds,id=>id=="WCG-082");
        Assert.Equal(decks.All.Count,decks.All.Select(e=>string.Join(',',e.Deck.CardIds.Order())).Distinct().Count());
        Assert.DoesNotContain(decks.All,e=>cards.PresetDecks.Any(p=>p.CardIds.Order().SequenceEqual(e.Deck.CardIds.Order())));
    }
    [Fact] public void VariantsAreLegalAndPicksAvoidRepeatingTheLastArchetype()
    {
        foreach(var entry in decks.All) foreach(var variant in entry.Variants!)
        {
            var changed=RankedDecks.Apply(entry.Deck,variant);Assert.NotNull(changed);Assert.True(decks.IsLegal(changed!,out var error),entry.Deck.Name+" "+variant.Name+" "+error);
            Assert.Equal(50,changed!.CardIds.Count);
        }
        for(int tier=0;tier<6;tier++)
        {
            var rng=new Random(tier);string? last=null;var lists=new HashSet<string>();
            for(int i=0;i<120;i++)
            {
                var deck=decks.Pick(tier,rng,last);Assert.True(decks.IsLegal(deck,out _));
                Assert.Contains(decks.Pool(tier),e=>e.Deck.Id==deck.Id);
                if(last!=null)Assert.NotEqual(ArchetypeKey(last),ArchetypeKey(deck.Id));
                last=deck.Id;lists.Add(string.Join(',',deck.CardIds.Order()));
            }
            if(tier<5)Assert.True(lists.Count>decks.Pool(tier).Count,"variants should add decklists beyond the base pool");
            else Assert.All(lists,list=>Assert.Contains(decks.Pool(5),e=>string.Join(',',e.Deck.CardIds.Order())==list));
        }
        Assert.Equal(decks.Pick(3,new Random(42),null).CardIds,decks.Pick(3,new Random(42),null).CardIds);
    }
    [Fact] public void NextRankedMatchFacesADifferentArchetypeAndKeepsItsSnapshot()
    {
        var s=Session();
        for(int i=0;i<6;i++)
        {
            var before=s.Read().Match?.ComputerDeck.Id;
            Assert.True(s.Start(cards.PresetDecks[0]).Success);var match=s.Read().Match!;
            if(before!=null)Assert.NotEqual(ArchetypeKey(before),ArchetypeKey(match.ComputerDeck.Id));
            Assert.True(decks.IsLegal(match.ComputerDeck,out _));
            var restored=Session();Assert.Equal(match.ComputerDeck.CardIds,restored.Read().Match!.ComputerDeck.CardIds);
            Assert.True(Send(s,"surrender").Success);
        }
    }
    [Fact, Trait("Category", "Slow")] public void EveryRankedDeckFinishesAndConservesCards()
    {
        foreach(var entry in decks.All)
        {
            var e=new GameEngine(cards,new Random(103)){AiLevel=entry.Tier};e.StartGame(entry.Deck,cards.PresetDecks[0],entry.Tier%2==0);e.Player.IsAi=true;
            for(int n=0;n<2500&&!e.IsOver;n++)Assert.True(e.ExecuteAiStep(),entry.Deck.Name+": "+e.LastError);
            Assert.True(e.IsOver,entry.Deck.Name);
            Assert.Equal(100,new[]{e.Player,e.Computer}.Sum(p=>p.Hand.Count+p.Deck.Count+p.Graveyard.Count+p.EnergyZone.Count+p.Occupied+p.Field.Sum(m=>m.Attachments.Count)+p.Field.Count(m=>m.SilenceSpell!=null)));
        }
    }
    [Fact] public void HigherAiTakesCombinedLethalBeforeTrading()
    {
        var e=new GameEngine(cards,new Random(1)){AiLevel=3};e.StartGame(cards.PresetDecks[0],cards.PresetDecks[1],false);
        e.Computer.Hand.Clear();e.Player.Hp=2;
        for(int i=0;i<2;i++) e.Computer.Field.Add(new(cards.GetCard("WCG-101")!){IsTapped=false,HasSummoningSickness=false});
        e.Player.Field.Add(new(cards.GetCard("WCG-003")!){IsTapped=false,HasSummoningSickness=false});
        Assert.True(e.ExecuteAiStep());Assert.Equal(1,e.Player.Hp);Assert.Single(e.Player.Field);
        Assert.True(e.ExecuteAiStep());Assert.True(e.Player.HasLost);
    }
    [Fact] public void PendingChoiceAndItsResolutionReplayAfterRestart()
    {
        var deck=DeckService.Copy(cards.PresetDecks[0]);
        deck.CardIds.RemoveRange(0,4);deck.CardIds.AddRange(Enumerable.Repeat("WCG-102",4));
        // Freeze a legal opening containing the one-cost choice card, then use real commands only.
        var s=Session();Assert.True(s.Start(deck).Success);var p=s.Read();p.Match!.PlayerFirst=true;
        for(int seed=0;;seed++)
        {
            var probe=new GameEngine(cards,new Random(seed));probe.StartGame(deck,p.Match.ComputerDeck,true);
            if(probe.Player.Hand.Any(c=>c.Card.Id=="WCG-102")){p.Match.Seed=seed;break;}
        }
        store.Save(p);s=Session();Assert.Empty(s.Error);
        Assert.True(Send(s,"energy",s.Engine.Player.Hand.First(c=>c.Card.Id!="WCG-102").InstanceId).Success);
        Assert.True(Send(s,"play",s.Engine.Player.Hand.First(c=>c.Card.Id=="WCG-102").InstanceId).Success);
        Assert.NotNull(s.Engine.CurrentPendingChoice);
        var restored=Session();Assert.Empty(restored.Error);
        Assert.Equal(JsonSerializer.Serialize(StateShape(s.Engine)),JsonSerializer.Serialize(StateShape(restored.Engine)));
        for(int n=0;n<4&&restored.Engine.CurrentPendingChoice is {} choice;n++)
            Assert.True(Send(restored,"choice",option:choice.Options[0].Id).Success);
        Assert.Equal(JsonSerializer.Serialize(StateShape(restored.Engine)),JsonSerializer.Serialize(StateShape(Session().Engine)));
    }
    [Fact] public void FailedSaveRollsBackTheAction()
    {
        var s=Session();Assert.True(s.Start(cards.PresetDecks[0]).Success);
        var before=JsonSerializer.Serialize(StateShape(s.Engine));var journal=s.Read().Match!.Actions.Count;
        File.Move(store.Path,store.Path+".backup");Directory.CreateDirectory(store.Path);
        Assert.False(Send(s,"surrender").Success);Assert.NotEmpty(s.Error);
        Assert.Equal(before,JsonSerializer.Serialize(StateShape(s.Engine)));Assert.Equal(journal,s.Read().Match!.Actions.Count);
        Directory.Delete(store.Path);File.Move(store.Path+".backup",store.Path);
        Assert.False(Session().Engine.IsOver);
    }
    [Fact] public void CrossMonthSettlementIsAppliedExactlyOnce()
    {
        store.Save(new(){Season="2026-10",Stars=61,BestStars=61,SeasonBest=61});var s=Session();Assert.True(s.Start(cards.PresetDecks[0]).Success);
        clock.Now=new(2026,11,1,0,0,0,TimeSpan.Zero);Assert.Equal("2026-10",s.Read().Season);
        Assert.True(Send(s,"surrender").Success);var p=s.Read();Assert.Equal(30,p.Stars);Assert.Equal("2026-11",p.Season);
        Assert.Single(p.History);Assert.Equal(60,p.History[0].EndingStars);Assert.Equal(1,p.History[0].Losses);
        Assert.Single(Session().Read().History);
    }
    [Theory] [InlineData(0)] [InlineData(2)] [InlineData(5)]
    public void AiDoesNotReadHiddenOpponentCards(int level)
    {
        GameEngine Make(bool changeHidden)
        {
            var e=new GameEngine(cards,new Random(55)){AiLevel=level};e.StartGame(cards.PresetDecks[0],cards.PresetDecks[1],false);
            if(changeHidden)
            {
                e.Player.Hand.Clear();e.Player.Hand.AddRange(Enumerable.Range(0,7).Select(_=>new CardInstance(cards.GetCard("WCG-020")!)));
                e.Player.Deck.Reverse();
            }
            Assert.True(e.ExecuteAiStep());return e;
        }
        var a=Make(false);var b=Make(true);
        Assert.Equal(a.Computer.Hand.Select(c=>c.Card.Id),b.Computer.Hand.Select(c=>c.Card.Id));
        Assert.Equal(a.Computer.EnergyZone.Select(c=>c.Card.Id),b.Computer.EnergyZone.Select(c=>c.Card.Id));
        Assert.Equal(a.Computer.Field.Select(c=>c.Card.Id),b.Computer.Field.Select(c=>c.Card.Id));
    }
    [Fact, Trait("Category", "Slow")] public async Task FullMatchJournalRestoresEveryPendingDecisionAndSettlesResult()
    {
        var s=Session();Assert.True(s.Start(cards.PresetDecks[0]).Success);
        var profile=s.Read();profile.Match!.Seed=700;profile.Match.PlayerFirst=true;
        profile.Match.ComputerDeck=DeckService.Copy(decks.All.First(x=>x.Tier==0).Deck);store.Save(profile);s=Session();
        int pendingCount=0;
        for(int n=0;n<1000&&!s.Engine.IsOver;n++)
        {
            var state=s.Bridge.Snapshot();
            if(state.DecisionPlayerId=="computer")
            {
                var token=s.Coordinator.Attach();s.Coordinator.Fast=true;using var cancel=new CancellationTokenSource();
                await s.Coordinator.DriveAsync(token,r=>{Assert.True(r.Success,r.Message);cancel.Cancel();return Task.CompletedTask;},cancel.Token);
                s.Coordinator.Detach(token);
            }
            else
            {
                BattleResponse response;
                if(state.Pending is {} pending)
                {
                    pendingCount++;var restored=Session();Assert.Empty(restored.Error);
                    Assert.Equal(JsonSerializer.Serialize(StateShape(s.Engine)),JsonSerializer.Serialize(StateShape(restored.Engine)));
                    response=pending.Kind=="choice" ? Send(s,"choice",option:pending.Options[0].Id) : Send(s,"target",target:Guid.Parse(pending.Targets[0]));
                }
                else if(state.Hand.Where(h=>h.CanEnergy && state.Player.TotalEnergy < 5).OrderByDescending(h=>h.Card.Cost).FirstOrDefault() is {} energy) response=Send(s,"energy",energy.Card.InstanceId);
                else if(state.Hand.FirstOrDefault(h=>h.CanPlay) is {} card) response=Send(s,"play",card.Card.InstanceId);
                else if(state.Player.Field.FirstOrDefault(m=>m.CanAttack&&m.Targets.Length>0) is {} monster)
                {
                    var target=monster.Targets.FirstOrDefault(t=>t.Id=="face")??monster.Targets[0];
                    response=Send(s,"attack",monster.Card.InstanceId,target.Id=="face"?null:Guid.Parse(target.Id));
                }
                else response=Send(s,"end");
                Assert.True(response.Success,response.Message);
            }
        }
        Assert.True(s.Engine.IsOver);Assert.True(pendingCount>0);var saved=s.Read();var after=Session();Assert.Empty(after.Error);
        Assert.Equal(JsonSerializer.Serialize(StateShape(s.Engine)),JsonSerializer.Serialize(StateShape(after.Engine)));
        Assert.Equal(1,saved.Wins+saved.Losses);Assert.Equal(saved.Stars,after.Read().Stars);
        Assert.Equal(!s.Engine.Player.HasLost,saved.Result!.Won);Assert.Equal(RankedRules.Apply(0,saved.Result.Won),saved.Stars);
    }
    public void Dispose(){if(Directory.Exists(dir))Directory.Delete(dir,true);}
    private sealed class TestClock:TimeProvider {public DateTimeOffset Now = new(2026,10,4,0,0,0,TimeSpan.Zero); public override DateTimeOffset GetUtcNow()=>Now;}

    static object PublicShape(BattleSnapshot s) => new { s.Turn, s.IsOver, s.Outcome,
        sides = new[] { s.Player, s.Computer }.Select(p => new { p.Hp, p.DeckCount, p.HandCount, p.TotalEnergy,
            field = p.Field.OrderBy(m => m.Slot).Select(m => new { m.Card.CardId, m.PP, m.DP, m.Slot, m.IsSet }), grave = p.Graveyard.Select(c => c.CardId) }),
        hand = s.Hand.Select(h => h.Card.CardId) };
    [Fact] public async Task FinishedMatchesAreRecordedAndReplayToTheSameFinalState()
    {
        var s=Session();Assert.True(s.Start(cards.PresetDecks[0]).Success);var lease=s.Coordinator.Attach();s.Coordinator.Fast=true;
        for(int round=0;round<3&&!s.Engine.IsOver;round++)
        {
            if(s.Engine.DecisionPlayerId=="player")
            {
                if(s.Bridge.Snapshot().Pending!=null||s.Engine.Player.Hand.Count==0)break;
                Assert.True(Send(s,"energy",s.Engine.Player.Hand[0].InstanceId).Success);Assert.True(Send(s,"end").Success);
            }
            for(int n=0;n<200&&!s.Engine.IsOver&&s.Engine.DecisionPlayerId=="computer";n++)
            {
                s.Coordinator.Resync(lease);using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await s.Coordinator.DriveAsync(lease,r=>{Assert.True(r.Success,r.Message);cancel.Cancel();return Task.CompletedTask;},cancel.Token);
            }
        }
        if(!s.Engine.IsOver)Assert.True(Send(s,"surrender").Success);
        var live=JsonSerializer.Serialize(PublicShape(s.Bridge.Snapshot()));
        var profile=s.Read();var record=Assert.Single(profile.Records!);
        Assert.Equal(profile.Result!.MatchId,record.Id);Assert.Equal(s.Engine.TurnNumber,record.Turns);Assert.NotNull(record.StartedAt);Assert.NotNull(record.EndedAt);
        Assert.Equal(profile.Match!.Actions,RankedRecord.Decode(record.Actions));Assert.Equal(profile.Match.ComputerDeck.CardIds,record.ComputerDeck.CardIds);Assert.Equal(profile.Match.ComputerDeck.Name,record.ComputerDeck.Name);Assert.NotEmpty(record.Archetype);
        var replay=Session().BuildReplay(record.Id);Assert.Empty(replay.Error);
        Assert.Equal(profile.Match.Actions.Count+1,replay.Steps.Count);
        Assert.Equal(live,JsonSerializer.Serialize(PublicShape(replay.Steps[^1].State)));
        // Replays are deterministic and never touch the live engine or save.
        var again=s.BuildReplay(record.Id);Assert.Equal(live,JsonSerializer.Serialize(PublicShape(again.Steps[^1].State)));
        Assert.Equal(RankedRules.ProfileVersion,s.Read().Version);
        Assert.Contains("\"Records\"",File.ReadAllText(store.Path));
    }
    [Fact] public void HistoryIsCappedAndStaysSmall()
    {
        var s=Session();
        for(int i=0;i<RankedRules.MaxRecords+3;i++){Assert.True(s.Start(cards.PresetDecks[i%2]).Success);Assert.True(Send(s,"surrender").Success);}
        var records=s.Read().Records!;Assert.Equal(RankedRules.MaxRecords,records.Count);Assert.Equal(s.Read().Match!.Id,records[^1].Id);
        Assert.True(new FileInfo(store.Path).Length<400_000,$"save is {new FileInfo(store.Path).Length} bytes");
    }
    [Fact] public void OlderSavesWithoutRecordsLoadAndALastSettledMatchIsListed()
    {
        var s=Session();Assert.True(s.Start(cards.PresetDecks[0]).Success);Assert.True(Send(s,"surrender").Success);
        var p=s.Read();p.Records=null;store.Save(p);
        var json=File.ReadAllText(store.Path);Assert.DoesNotContain("\"StartedAt\": null",json.Replace("\"Records\": null",""),StringComparison.Ordinal);
        var restored=Session();Assert.Empty(restored.Error);var record=Assert.Single(restored.Read().Records!);Assert.Equal(p.Match!.Id,record.Id);Assert.Null(record.EndedAt);
        Assert.Empty(restored.BuildReplay(record.Id).Error);
    }
    [Fact] public void CorruptHistoryEntriesAreDroppedWithoutBlockingRanked()
    {
        var s=Session();Assert.True(s.Start(cards.PresetDecks[0]).Success);Assert.True(Send(s,"surrender").Success);
        var p=s.Read();p.Records!.Add(new(){Id=Guid.NewGuid(),Season=p.Season,Tier=9,PlayerDeck=new(),ComputerDeck=new(),Actions="x"});
        p.Records.Add(new(){Id=Guid.NewGuid(),Season=p.Season,Tier=1,PlayerDeck=new(),ComputerDeck=new(),Actions="bad"});store.Save(p);
        var restored=Session();Assert.Empty(restored.Error);Assert.Single(restored.Read().Records!);
        Assert.NotEmpty(restored.BuildReplay(Guid.NewGuid()).Error);
    }

    // ---- 對局代碼 (match code) ----
    async Task<RankedSession> PlayFullMatch(int tier,int seed)
    {
        var s=Session();Assert.True(s.Start(cards.PresetDecks[0]).Success);
        var profile=s.Read();profile.Match!.Seed=seed;profile.Match.PlayerFirst=true;profile.Match.Tier=tier;
        profile.Match.ComputerDeck=DeckService.Copy(decks.All.First(x=>x.Tier==tier).Deck);store.Save(profile);s=Session();
        for(int n=0;n<1500&&!s.Engine.IsOver;n++)
        {
            var state=s.Bridge.Snapshot();BattleResponse response;
            if(state.DecisionPlayerId=="computer")
            {
                var token=s.Coordinator.Attach();s.Coordinator.Fast=true;using var cancel=new CancellationTokenSource();
                await s.Coordinator.DriveAsync(token,r=>{Assert.True(r.Success,r.Message);cancel.Cancel();return Task.CompletedTask;},cancel.Token);
                s.Coordinator.Detach(token);continue;
            }
            if(state.Pending is {} pending) response=pending.Kind=="choice"?Send(s,"choice",option:pending.Options[0].Id):Send(s,"target",target:Guid.Parse(pending.Targets[0]));
            else if(state.Hand.Where(h=>h.CanEnergy&&state.Player.TotalEnergy<5).OrderByDescending(h=>h.Card.Cost).FirstOrDefault() is {} energy) response=Send(s,"energy",energy.Card.InstanceId);
            else if(state.Hand.FirstOrDefault(h=>h.CanPlay) is {} card) response=Send(s,"play",card.Card.InstanceId);
            else if(state.Player.Field.FirstOrDefault(m=>m.CanAttack&&m.Targets.Length>0) is {} monster)
            { var target=monster.Targets.FirstOrDefault(t=>t.Id=="face")??monster.Targets[0]; response=Send(s,"attack",monster.Card.InstanceId,target.Id=="face"?null:Guid.Parse(target.Id)); }
            else response=Send(s,"end");
            Assert.True(response.Success,response.Message);
        }
        Assert.True(s.Engine.IsOver);return s;
    }
    static string FinalState(RankedReplay r)=>JsonSerializer.Serialize(PublicShape(r.Steps[^1].State));
    [Fact, Trait("Category", "Slow")] public async Task MatchCodeRoundTripsAndReplaysToTheSameFinalState()
    {
        var s=await PlayFullMatch(4,9123);var record=s.Read().Records!.Last();
        var actions=RankedRecord.Decode(record.Actions);Assert.Contains(actions,a=>a.Type=="ai"&&a.RulesVersion==GameEngine.RankedAiVersion);
        var code=s.MatchCodeFor(record.Id)!;Assert.StartsWith(MatchCode.Prefix,code);Assert.True(code.Length<record.Actions.Length+4000,code.Length.ToString());
        var decoded=MatchCode.Decode(code,out var error,out var live)!;Assert.Equal("",error);Assert.False(live);
        Assert.Equal(record.Seed,decoded.Seed);Assert.Equal(record.Tier,decoded.Tier);Assert.Equal(record.PlayerFirst,decoded.PlayerFirst);Assert.Equal(record.Rules,decoded.Rules);
        Assert.Equal(record.Actions,decoded.Actions);Assert.Equal(record.PlayerDeck.CardIds,decoded.PlayerDeck.CardIds);Assert.Equal(record.ComputerDeck.CardIds,decoded.ComputerDeck.CardIds);
        Assert.Equal(record.ComputerDeck.Id,decoded.ComputerDeck.Id);Assert.Equal(record.Won,decoded.Won);Assert.Equal(record.Turns,decoded.Turns);
        var original=s.BuildReplay(record.Id);Assert.Empty(original.Error);
        // A fresh session with no history (another player's device) replays the pasted code to the same end.
        Directory.Delete(dir,true);var other=Session();var fromCode=other.BuildReplay(decoded);Assert.Empty(fromCode.Error);
        Assert.Equal(original.Steps.Count,fromCode.Steps.Count);Assert.Equal(FinalState(original),FinalState(fromCode));Assert.True(fromCode.Steps[^1].State.IsOver);
        // Whitespace and line breaks from chat apps are ignored.
        var spaced=" \n"+string.Join("\n  ",code.Chunk(40).Select(c=>new string(c)))+"\t";
        Assert.Equal(FinalState(original),FinalState(other.BuildReplay(MatchCode.Decode(spaced,out _,out _)!)));
    }
    [Fact] public void InProgressMatchCodeReplaysTheJournalSoFar()
    {
        var s=Session();Assert.True(s.Start(cards.PresetDecks[0]).Success);
        if(s.Engine.DecisionPlayerId=="player")Assert.True(Send(s,"energy",s.Engine.Player.Hand[0].InstanceId).Success);
        var code=s.CurrentMatchCode()!;var record=MatchCode.Decode(code,out var error,out var live)!;Assert.Equal("",error);Assert.True(live);
        var replay=s.BuildReplay(record);Assert.Empty(replay.Error);Assert.Equal(s.Read().Match!.Actions.Count+1,replay.Steps.Count);
        Assert.True(Send(s,"surrender").Success);Assert.Null(s.CurrentMatchCode());
    }
    [Theory]
    [InlineData("","請貼上對局代碼。")]
    [InlineData("hello","這不是魂誓對局代碼（應以 WCG1- 開頭）。")]
    [InlineData("WCG2-abc","這個對局代碼來自較新的版本，請更新遊戲後再試。")]
    [InlineData("WCG1-***","對局代碼含有無效字元，可能複製不完整。")]
    [InlineData("WCG1-AAAAAAAA","對局代碼已損壞或複製不完整。")]
    public void InvalidMatchCodesExplainWhatIsWrong(string code,string message)
    {
        Assert.Null(MatchCode.Decode(code,out var error,out _));Assert.Equal(message,error);
        var en=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(File.ReadAllText(Path.Combine(env.ContentRootPath,"i18n","en.json")))!;
        Assert.True(en.ContainsKey(message),"missing English text: "+message);
    }
    [Fact] public async Task TruncatedMatchCodeIsRejected()
    {
        var s=await PlayFullMatch(1,777);var code=s.MatchCodeFor(s.Read().Records!.Last().Id)!;
        Assert.Null(MatchCode.Decode(code[..(code.Length*2/3)],out var error,out _));Assert.Equal("對局代碼已損壞或複製不完整。",error);
        Assert.Null(MatchCode.Decode(code[..^12],out error,out _));Assert.Equal("對局代碼已損壞或複製不完整。",error);
    }
    [Fact] public void OldEraMatchCodeReplaysWithItsOwnCatalog()
    {
        var prior=Overlay(cards,"balance-before-player-targets.json");
        store.Save(new(){Season="2026-10",Stars=22,BestStars=22,SeasonBest=22});
        var old=new RankedSession(prior,env,store,new RankedDecks(prior,env),NullLogger<BattleCoordinator>.Instance,clock);
        Assert.True(old.Start(HealingDeck()).Success);Assert.True(Send(old,"surrender").Success);
        var record=old.Read().Records!.Last();var code=old.MatchCodeFor(record.Id)!;
        Directory.Delete(dir,true);var current=Session();Assert.Null(current.MatchCodeFor(record.Id));
        var decoded=MatchCode.Decode(code,out var error,out _)!;Assert.Equal("",error);Assert.Equal(record.Rules,decoded.Rules);
        var replay=current.BuildReplay(decoded);Assert.Empty(replay.Error);Assert.True(replay.Steps.Count>=2);
        // A code whose rules fingerprint matches no known era explains that it cannot be re-simulated.
        var unknown=new RankedRecord{Rules="ranked-v06:"+new string('0',64),Tier=0,PlayerDeck=decoded.PlayerDeck,ComputerDeck=decoded.ComputerDeck,Actions=""};
        Assert.NotEmpty(current.BuildReplay(MatchCode.Decode(MatchCode.Encode(unknown),out _,out _)!).Error);
    }
}
