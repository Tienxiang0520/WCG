using WcgWeb.Models;
using WcgWeb.Services;
using Microsoft.AspNetCore.Hosting;
using Moq;
namespace WcgTests;

// AI v7 lethal search (GameEngine.AiV7.cs, ai-design.md §2.8).
public class AiLethalTests
{
    private readonly CardDatabase cards;
    public AiLethalTests(){var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb"));var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(root);cards=new(env.Object);}
    // The player side is driven by the ranked AI so the tests can use the player's turn.
    private GameEngine Game(int revision=7)
    {
        var g=new GameEngine(cards,new Random(17)){AiLevel=5,AiRevision=revision};g.SeedRankedAi(17);g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1]);
        g.Player.IsAi=true;g.Player.Hand.Clear();g.Computer.Hand.Clear();g.Player.Field.Clear();g.Computer.Field.Clear();g.Player.Structures.Clear();g.Computer.Structures.Clear();
        g.Player.EnergyZone.Clear();for(int i=0;i<8;i++)g.Player.EnergyZone.Add(new(cards.GetCard("WCG-101")!));g.Player.HasFilledEnergyThisTurn=true;
        return g;
    }
    private CardInstance Hand(GameEngine g,string id){var c=new CardInstance(cards.GetCard(id)!);g.Player.Hand.Add(c);return c;}
    private MonsterInstance Unit(PlayerState p,string id,int slot,int dp=1){var m=new MonsterInstance(cards.GetCard(id)!){Slot=slot,IsTapped=false,HasSummoningSickness=false};m.CurrentDP=dp;p.Field.Add(m);return m;}
    private static void PlayTurn(GameEngine g){for(int i=0;i<60&&!g.IsOver&&g.DecisionPlayerId=="player";i++)if(!g.ExecuteAiStep())break;}

    [Fact]
    public void CombinesTwoBurnSpellsWithAnAttack()
    {
        foreach(var revision in new[]{6,7})
        {
            var g=Game(revision);g.Computer.Hp=3;Hand(g,"WCG-014");Hand(g,"WCG-014");Unit(g.Player,"WCG-101",0);
            // A big enemy body makes v6 prefer developing over burning face.
            Unit(g.Computer,"WCG-101",0,dp:0).CurrentPP=3000;
            PlayTurn(g);
            if(revision==7)Assert.True(g.Computer.HasLost,"v7 must find burn + burn + attack");
        }
    }

    [Fact]
    public void RemovesTheOnlyTauntThenAttacksForLethal()
    {
        var g=Game();g.Computer.Hp=2;Hand(g,"WCG-094");Unit(g.Player,"WCG-101",0,dp:2);
        var taunt=cards.AllCards.First(c=>c.IsMonster&&c.HasTaunt&&!c.HasEnergyShield&&c.PP>=1500);
        g.Computer.Field.Add(new MonsterInstance(taunt){Slot=0});g.Computer.Field.Add(new MonsterInstance(taunt){Slot=1});
        g.Player.Field[0].CurrentDP=2;
        // Two taunts: removal clears one, the attacker still cannot reach the player, so no false lethal.
        Assert.Null(g.FindLethal(g.Player,GameEngine.V7ProfileFor(5)));
        g.Computer.Field.RemoveAt(1);
        Assert.NotNull(g.FindLethal(g.Player,GameEngine.V7ProfileFor(5)));
        PlayTurn(g);Assert.True(g.Computer.HasLost);
    }

    [Fact]
    public void DamageBoundReadsBurnAndChargeFromCardText()
    {
        Assert.Equal(1,GameEngine.CardReach(cards.GetCard("WCG-014")!));Assert.Equal(2,GameEngine.CardReach(cards.GetCard("WCG-006")!));
        Assert.Equal(0,GameEngine.CardReach(cards.GetCard("WCG-094")!));
        var g=Game();g.Computer.Hp=7;Hand(g,"WCG-014");Unit(g.Player,"WCG-101",0);
        Assert.True(g.DamageBound(g.Player,g.Computer)<g.Computer.Hp);Assert.Null(g.FindLethal(g.Player,GameEngine.V7ProfileFor(5)));
    }

    [Fact]
    public void LethalSearchIsDeterministicAndIgnoresHiddenCards()
    {
        SimMoveKey Run(string filler){var g=Game();g.Computer.Hp=3;Hand(g,"WCG-014");Hand(g,"WCG-014");Unit(g.Player,"WCG-101",0);
            g.Computer.Hand.AddRange(Enumerable.Range(0,5).Select(_=>new CardInstance(cards.GetCard(filler)!)));
            var m=g.FindLethal(g.Player,GameEngine.V7ProfileFor(5))!;return new(m.Kind,g.Player.Hand.Concat(g.Player.EnergyZone).Select(c=>c.InstanceId).ToList().IndexOf(m.Source));}
        var a=Run("WCG-101");Assert.Equal(a,Run("WCG-101"));Assert.Equal(a,Run("WCG-195"));Assert.Equal(a,Run("WCG-014"));
    }
    private record SimMoveKey(string Kind,int Index);

    [Fact]
    public void RankedAiVersionIsSevenAndOldStepsKeepV6()
    {
        Assert.Equal(7,GameEngine.RankedAiVersion);
        var g=Game(6);g.Computer.Hp=3;Hand(g,"WCG-014");Hand(g,"WCG-014");Unit(g.Player,"WCG-101",0);
        var v6=Game(6);v6.Computer.Hp=3;Hand(v6,"WCG-014");Hand(v6,"WCG-014");Unit(v6.Player,"WCG-101",0);
        PlayTurn(g);PlayTurn(v6);Assert.Equal(AiSimulationTests.Board(v6),AiSimulationTests.Board(g));
    }
}
