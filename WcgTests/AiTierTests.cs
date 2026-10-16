using WcgWeb.Models;
using WcgWeb.Services;
using Microsoft.AspNetCore.Hosting;
using Moq;
namespace WcgTests;

// AI v7 tiers and opponent reply (GameEngine.AiV7.cs, ai-design.md §2.5, §3).
public class AiTierTests
{
    private readonly CardDatabase cards;
    public AiTierTests(){var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb"));var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(root);cards=new(env.Object);}

    [Fact]
    public void HigherTiersThinkMoreAndErrLess()
    {
        for(int l=1;l<6;l++)
        {
            var a=GameEngine.V7ProfileFor(l-1);var b=GameEngine.V7ProfileFor(l);
            Assert.True(b.Budget>a.Budget&&b.LethalBudget>a.LethalBudget&&b.Width>=a.Width&&b.Depth>=a.Depth&&b.Replies>=a.Replies,$"tier {l} budgets");
            Assert.True(b.Sigma<a.Sigma&&b.Blunder<=a.Blunder&&b.LethalSight>=a.LethalSight,$"tier {l} noise");
        }
        Assert.Equal(0,GameEngine.V7ProfileFor(5).Sigma);Assert.Equal(0,GameEngine.V7ProfileFor(5).Blunder);
    }

    // The board-only reply sees the counter-swing: trading the attacker that threatens lethal next turn beats a
    // static score that ignores which of our monsters survive the opponent's attack.
    [Fact]
    public void MasterTradesOffTheAttackerThatWouldWinNextTurn()
    {
        var g=new GameEngine(cards,new Random(17)){AiLevel=5,AiRevision=7};g.SeedRankedAi(17);g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1]);
        g.Player.IsAi=true;g.Player.Hand.Clear();g.Computer.Hand.Clear();g.Player.Field.Clear();g.Computer.Field.Clear();g.Player.Structures.Clear();g.Computer.Structures.Clear();
        g.Player.HasFilledEnergyThisTurn=true;g.Player.Hp=2;g.Computer.Hp=5;
        MonsterInstance Unit(PlayerState p,int slot,int pp,int dp){var m=new MonsterInstance(cards.GetCard("WCG-101")!){Slot=slot,IsTapped=false,HasSummoningSickness=false};m.CurrentPP=pp;m.CurrentDP=dp;p.Field.Add(m);return m;}
        Unit(g.Player,0,1800,2);Unit(g.Player,1,1800,2);var threat=Unit(g.Computer,0,1200,2);Unit(g.Computer,1,600,0);
        for(int i=0;i<40&&!g.IsOver&&g.DecisionPlayerId=="player";i++)if(!g.ExecuteAiStep())break;
        Assert.DoesNotContain(threat,g.Computer.Field);
    }

    [Fact]
    public void ReplySearchKeepsTheRealMatchAndHiddenCardsUntouched()
    {
        foreach(var g in AiSimulationTests.Positions(cards,3,9))
        {
            var me=g.ActivePlayer;var opp=g.GetOpponent(me);var hand=opp.Hand.Select(c=>c.InstanceId).ToList();var before=AiSimulationTests.Board(g);
            g.AiLevel=5;g.AiRevision=7;_=g.SearchTurn(me,GameEngine.V7ProfileFor(5));
            Assert.Equal(before,AiSimulationTests.Board(g));Assert.Equal(hand,opp.Hand.Select(c=>c.InstanceId).ToList());
        }
    }
}
