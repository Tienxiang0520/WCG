using WcgWeb.Models;
using WcgWeb.Services;
using Microsoft.AspNetCore.Hosting;
using Moq;
namespace WcgTests;

// AI v7 turn search (GameEngine.AiV7.cs, ai-design.md §2.2).
public class AiTurnSearchTests
{
    private readonly CardDatabase cards;
    public AiTurnSearchTests(){var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb"));var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(root);cards=new(env.Object);}
    private GameEngine Game(int level=5)
    {
        var g=new GameEngine(cards,new Random(17)){AiLevel=level,AiRevision=7};g.SeedRankedAi(17);g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1]);
        g.Player.IsAi=true;g.Player.Hand.Clear();g.Computer.Hand.Clear();g.Player.Field.Clear();g.Computer.Field.Clear();g.Player.Structures.Clear();g.Computer.Structures.Clear();
        g.Player.EnergyZone.Clear();for(int i=0;i<8;i++)g.Player.EnergyZone.Add(new(cards.GetCard("WCG-101")!));g.Player.HasFilledEnergyThisTurn=true;
        return g;
    }
    private MonsterInstance Unit(PlayerState p,string id,int slot,int pp,int dp){var m=new MonsterInstance(cards.GetCard(id)!){Slot=slot,IsTapped=false,HasSummoningSickness=false};m.CurrentPP=pp;m.CurrentDP=dp;p.Field.Add(m);return m;}
    private static List<string> PlayTurn(GameEngine g){var log=new List<string>();for(int i=0;i<60&&!g.IsOver&&g.DecisionPlayerId=="player";i++){if(!g.ExecuteAiStep())break;log.Add(AiSimulationTests.Board(g));}return log;}

    [Fact]
    public void KillsTheAttackerThatWouldBeLethalInsteadOfHittingFace()
    {
        var g=Game();g.Player.Hp=3;g.Computer.Hp=6;
        Unit(g.Player,"WCG-101",0,2000,1);var threat=Unit(g.Computer,"WCG-101",0,1000,3);
        PlayTurn(g);
        Assert.DoesNotContain(threat,g.Computer.Field);Assert.Equal(6,g.Computer.Hp);
    }

    [Fact]
    public void DoesNotSuicideIntoABiggerMonster()
    {
        var g=Game();var mine=Unit(g.Player,"WCG-101",0,800,1);Unit(g.Computer,"WCG-101",0,2500,1);
        PlayTurn(g);Assert.Contains(mine,g.Player.Field);
    }

    [Fact]
    public void WholeTurnsAreDeterministic()
    {
        foreach(var level in new[]{0,3,5})
        {
            var a=Game(level);var b=Game(level);
            foreach(var g in new[]{a,b}){g.Player.Hand.AddRange(new[]{"WCG-001","WCG-014","WCG-094"}.Select(id=>new CardInstance(cards.GetCard(id)!)));Unit(g.Player,"WCG-101",0,1200,1);Unit(g.Computer,"WCG-101",0,900,1);}
            Assert.Equal(PlayTurn(a),PlayTurn(b));
        }
    }

    [Fact]
    public void DecisionsIgnoreTheOpponentsHiddenHandAndDeck()
    {
        List<string> Run(string filler)
        {
            var g=Game();g.Player.Hand.AddRange(new[]{"WCG-001","WCG-014","WCG-094"}.Select(id=>new CardInstance(cards.GetCard(id)!)));Unit(g.Player,"WCG-101",0,1200,1);Unit(g.Computer,"WCG-101",0,900,2);
            var n=g.Computer.Hand.Count;g.Computer.Hand.AddRange(Enumerable.Range(0,4).Select(_=>new CardInstance(cards.GetCard(filler)!)));
            var deck=g.Computer.Deck.Count;g.Computer.Deck.Clear();g.Computer.Deck.AddRange(Enumerable.Range(0,deck).Select(_=>new CardInstance(cards.GetCard(filler)!)));
            // Compare only our own side: the opponent's hidden cards are not part of the decision.
            var log=new List<string>();for(int i=0;i<60&&!g.IsOver&&g.DecisionPlayerId=="player";i++){if(!g.ExecuteAiStep())break;log.Add(AiSimulationTests.Board(g).Split('|')[0]+g.Computer.Hp+string.Join(",",g.Computer.Field.Select(m=>m.CurrentPP)));}
            return log;
        }
        var a=Run("WCG-101");Assert.NotEmpty(a);Assert.Equal(a,Run("WCG-195"));Assert.Equal(a,Run("WCG-170"));
    }

    [Fact, Trait("Category", "Slow")]
    public void FullV7MatchesFinishAtEveryTier()
    {
        for(int level=0;level<6;level++)
        {
            var g=new GameEngine(cards,new Random(80+level)){AiLevel=level,AiRevision=7};g.SeedRankedAi(80+level);g.StartGame(cards.PresetDecks[level%5],cards.PresetDecks[(level+3)%5],level%2==0);g.Player.IsAi=true;
            for(int s=0;s<3000&&!g.IsOver;s++)Assert.True(g.ExecuteAiStep(),g.LastError);
            Assert.True(g.IsOver);
        }
    }
}
