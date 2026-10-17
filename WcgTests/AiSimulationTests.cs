using WcgWeb.Models;
using WcgWeb.Services;
using Microsoft.AspNetCore.Hosting;
using Moq;
namespace WcgTests;

// AI v7 simulation clones (GameEngine.Simulation.cs).
public class AiSimulationTests
{
    private readonly CardDatabase cards;
    public AiSimulationTests(){var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb"));var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(root);cards=new(env.Object);}

    internal static string Board(GameEngine g)=>string.Join("|",new[]{g.Player,g.Computer}.Select(p=>
        $"{p.Id}:{p.Hp}:{p.EnergyZone.Count}/{p.AvailableEnergy}:{p.Hand.Count}:{p.Deck.Count}:{p.Graveyard.Count}:"+
        string.Join(",",p.Field.OrderBy(m=>m.Slot).Select(m=>$"{m.Card.Id}@{m.Slot}/{m.CurrentPP}/{m.CurrentDP}/{(m.IsTapped?1:0)}/{(m.HasShield?1:0)}"))+":"+
        string.Join(",",p.Structures.OrderBy(m=>m.Slot).Select(m=>$"{(m.IsSet?"set":m.Card.Id)}@{m.Slot}"))))+$"|{g.CurrentTurnPlayerId}:{g.TurnNumber}:{g.CurrentPhase}";
    private static int Count(GameEngine g)=>new[]{g.Player,g.Computer}.Sum(p=>p.Hand.Count+p.Deck.Count+p.Graveyard.Count+p.EnergyZone.Count+p.Occupied+p.Field.Sum(m=>m.Attachments.Count)+p.Field.Count(m=>m.SilenceSpell!=null));

    // Self-play positions at quiescent points across several matches.
    internal static IEnumerable<GameEngine> Positions(CardDatabase cards,int matches=6,int every=7)
    {
        for(int seed=0;seed<matches;seed++)
        {
            var g=new GameEngine(cards,new Random(40+seed)){AiLevel=4};g.SeedRankedAi(40+seed);
            g.StartGame(cards.PresetDecks[seed%5],cards.PresetDecks[(seed+2)%5],seed%2==0);g.Player.IsAi=true;
            for(int step=0;step<600&&!g.IsOver;step++)
            {
                if(step%every==0&&g.IsQuiescent&&g.CurrentPhase==TurnPhase.MainPhase)yield return g;
                if(g.IsOver||!g.ExecuteAiStep())break;
            }
        }
    }

    [Fact]
    public void CloneConservesCardsAndHidesOpponentCards()
    {
        int checkedPositions=0;
        foreach(var g in Positions(cards))
        {
            var viewer=g.ActivePlayer;var c=g.CloneQuiescent(11,viewer.Id);
            Assert.True(c.IsSimulation);Assert.Equal(Count(g),Count(c));Assert.Equal(Board(g),Board(c));
            var me=c.Player.Id==viewer.Id?c.Player:c.Computer;var them=c.GetOpponent(me);var realThem=g.GetOpponent(viewer);
            Assert.Equal(viewer.Hand.Select(x=>x.InstanceId),me.Hand.Select(x=>x.InstanceId));
            Assert.All(them.Hand.Concat(them.Deck),x=>Assert.Equal("SIM-HIDDEN",x.Card.Id));
            Assert.All(them.Structures.Where(m=>m.IsSet),m=>Assert.Equal("SIM-HIDDEN",m.Card.Id));
            Assert.DoesNotContain(them.Hand.Concat(them.Deck),x=>realThem.Hand.Concat(realThem.Deck).Any(r=>r.InstanceId==x.InstanceId));
            Assert.Equal(viewer.Deck.Select(x=>x.Card.Id).Order(StringComparer.Ordinal),me.Deck.Select(x=>x.Card.Id).Order(StringComparer.Ordinal));
            checkedPositions++;
        }
        Assert.True(checkedPositions>20);
    }

    [Fact, Trait("Category", "Slow")]
    public void SimulationNeverChangesTheRealMatch()
    {
        foreach(var g in Positions(cards,4,11))
        {
            var before=Board(g);var revision=g.Revision;var logs=g.Logs.Count;var events=g.PresentationEvents.Length;
            var c=g.CloneQuiescent(3,g.ActivePlayer.Id);
            for(int i=0;i<60&&!c.IsOver;i++)if(!c.ExecuteAiStep())break;
            Assert.Equal(before,Board(g));Assert.Equal(revision,g.Revision);Assert.Equal(logs,g.Logs.Count);Assert.Equal(events,g.PresentationEvents.Length);
            Assert.Empty(c.Logs);Assert.Empty(c.PresentationEvents);
        }
    }

    [Fact]
    public void ClonesAreDeterministicForTheSameSeed()
    {
        foreach(var g in Positions(cards,3,13))
        {
            var a=g.CloneQuiescent(5,g.ActivePlayer.Id);var b=g.CloneQuiescent(5,g.ActivePlayer.Id);
            for(int i=0;i<40&&!a.IsOver;i++){var x=a.ExecuteAiStep();var y=b.ExecuteAiStep();Assert.Equal(x,y);if(!x)break;Assert.Equal(Board(a),Board(b));}
        }
    }

    [Fact]
    public void SimulatedAttacksMatchTheRealEngine()
    {
        int compared=0;
        foreach(var g in Positions(cards,6,5))
        {
            var p=g.ActivePlayer;var a=p.Field.FirstOrDefault(g.CanAttack);if(a==null)continue;
            var d=g.GetAttackTargets(a).FirstOrDefault();
            var c=g.CloneQuiescent(9,p.Id);var cp=c.Player.Id==p.Id?c.Player:c.Computer;
            var ca=cp.Field.First(m=>m.InstanceId==a.InstanceId);var cd=d==null?null:c.GetOpponent(cp).Field.First(m=>m.InstanceId==d.InstanceId);
            var hpBefore=g.GetOpponent(p).Hp;
            // Compare on a full-information clone of the real match so the real match stays untouched.
            var full=g.CloneQuiescent(9,p.Id);
            Assert.True(c.SimApply(s=>s.Attack(cp,ca,cd))==full.SimApply(s=>{var fp=s.Player.Id==p.Id?s.Player:s.Computer;return s.Attack(fp,fp.Field.First(m=>m.InstanceId==a.InstanceId),d==null?null:s.GetOpponent(fp).Field.First(m=>m.InstanceId==d.InstanceId));}));
            string Fields(GameEngine x)=>string.Join("|",new[]{x.Player,x.Computer}.Select(s=>s.Hp+":"+string.Join(",",s.Field.OrderBy(m=>m.Slot).Select(m=>$"{m.Card.Id}/{m.CurrentPP}/{m.CurrentDP}/{m.IsTapped}"))));
            Assert.Equal(Fields(full),Fields(c));Assert.Equal(hpBefore,g.GetOpponent(p).Hp);compared++;
        }
        Assert.True(compared>10);
    }

    [Fact]
    public void CloneRequiresAQuiescentStateAndSimApplyRequiresASimulation()
    {
        var g=new GameEngine(cards,new Random(2));g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1]);
        Assert.Throws<InvalidOperationException>(()=>g.SimApply(_=>true));
        g.CurrentPendingChoice=new PendingChoice{OwnerId="player",Title="x",Options=[new(){Id="A"}]};
        Assert.Throws<InvalidOperationException>(()=>g.CloneQuiescent(1,"player"));
    }
}
