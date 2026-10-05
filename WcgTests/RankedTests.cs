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
            field=p.Field.Select(m=>new {m.Card.Id,m.CurrentPP,m.CurrentDP,m.HasAttacked,m.HasSummoningSickness,m.IsFrozen,m.IsSilenced,m.IsStealthed,m.ShieldCount}),
            grave=p.Graveyard.Select(c=>c.Card.Id)}),
        choice=e.CurrentPendingChoice?.Options.Select(o=>new{o.Title,Card=o.PreviewCard?.Id}),target=e.CurrentPendingTarget?.Title };
    [Theory]
    [InlineData(0,false,0)] [InlineData(14,true,15)] [InlineData(15,false,15)]
    [InlineData(20,false,19)] [InlineData(29,true,30)] [InlineData(74,true,75)]
    [InlineData(75,false,75)] [InlineData(76,false,75)] [InlineData(75,true,76)]
    public void StarProgressAndTierFloors(int before,bool win,int after) => Assert.Equal(after,RankedRules.Apply(before,win));
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
    [Fact] public void SurrenderIsSettledOnceAndSurvivesRestart()
    {
        store.Save(new(){Season="2026-10",Stars=22,SeasonBest=22,BestStars=22});var s=Session();Assert.True(s.Start(cards.PresetDecks[0]).Success);
        var state=s.Bridge.Snapshot();var cmd=new BattleCommand(Guid.NewGuid(),state.MatchId,state.Revision,"surrender");
        Assert.True(s.Bridge.Submit(cmd).Success);Assert.Equal(21,s.Read().Stars);Assert.False(s.Bridge.Submit(cmd).Success);
        var restored=Session();Assert.Equal(21,restored.Read().Stars);Assert.Equal(1,restored.Read().Losses);Assert.True(restored.Engine.IsOver);
        Assert.False(Send(restored,"surrender").Success);Assert.Equal(1,restored.Read().Losses);
    }
    [Fact] public void RankedCannotReplaceAnActiveMatchOrUsePracticeStart()
    {
        var s=Session();Assert.True(s.Start(cards.PresetDecks[0]).Success);var id=s.Read().Match!.Id;
        Assert.False(s.Start(cards.PresetDecks[1]).Success);Assert.False(Send(s,"start").Success);Assert.False(Send(s,"reset").Success);
        Assert.Equal(id,s.Read().Match!.Id);
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
        Assert.True(decks.All.Select(e=>string.Join(',',e.Deck.CardIds.Order())).Distinct().Count()>=30);
    }
    [Fact] public void EveryRankedDeckFinishesAndConservesCards()
    {
        foreach(var entry in decks.All)
        {
            var e=new GameEngine(cards,new Random(103)){AiLevel=entry.Tier};e.StartGame(entry.Deck,cards.PresetDecks[0],entry.Tier%2==0);e.Player.IsAi=true;
            for(int n=0;n<2500&&!e.IsOver;n++)Assert.True(e.ExecuteAiStep(),entry.Deck.Name+": "+e.LastError);
            Assert.True(e.IsOver,entry.Deck.Name);
            Assert.Equal(100,new[]{e.Player,e.Computer}.Sum(p=>p.Hand.Count+p.Deck.Count+p.Graveyard.Count+p.EnergyZone.Count+p.Field.Count+p.Field.Sum(m=>m.ShieldCount)+p.Field.Count(m=>m.SilenceSpell!=null)));
        }
    }
    [Fact] public void HigherAiTakesCombinedLethalBeforeTrading()
    {
        var e=new GameEngine(cards,new Random(1)){AiLevel=3};e.StartGame(cards.PresetDecks[0],cards.PresetDecks[1],false);
        e.Computer.Hand.Clear();e.Player.Hp=2;
        for(int i=0;i<2;i++) e.Computer.Field.Add(new(cards.GetCard("WCG-101")!){HasSummoningSickness=false});
        e.Player.Field.Add(new(cards.GetCard("WCG-003")!){HasSummoningSickness=false});
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
    [Fact] public async Task FullMatchJournalRestoresEveryPendingDecisionAndSettlesVictory()
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
        Assert.True(saved.Result!.Won);Assert.Equal(1,saved.Stars);
    }
    public void Dispose(){if(Directory.Exists(dir))Directory.Delete(dir,true);}
    private sealed class TestClock:TimeProvider {public DateTimeOffset Now = new(2026,10,4,0,0,0,TimeSpan.Zero); public override DateTimeOffset GetUtcNow()=>Now;}
}
