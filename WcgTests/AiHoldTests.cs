using WcgWeb.Models;
using WcgWeb.Services;
using Microsoft.AspNetCore.Hosting;
using Moq;
namespace WcgTests;

// AI v7 hold values and opponent model (GameEngine.AiV7.Model.cs, ai-design.md §2.4–2.6).
public class AiHoldTests
{
    private readonly CardDatabase cards;
    public AiHoldTests(){var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb"));var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(root);cards=new(env.Object);}
    private GameEngine Game()
    {
        var g=new GameEngine(cards,new Random(17)){AiLevel=5,AiRevision=7};g.SeedRankedAi(17);g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1]);
        g.Player.IsAi=true;g.Player.Hand.Clear();g.Computer.Hand.Clear();g.Player.Field.Clear();g.Computer.Field.Clear();g.Player.Structures.Clear();g.Computer.Structures.Clear();g.Computer.Graveyard.Clear();
        g.Player.EnergyZone.Clear();for(int i=0;i<8;i++)g.Player.EnergyZone.Add(new(cards.GetCard("WCG-101")!));g.Player.HasFilledEnergyThisTurn=true;
        for(int i=0;i<4;i++)g.Computer.Hand.Add(new(cards.GetCard("WCG-101")!));
        return g;
    }
    private CardInstance Hand(GameEngine g,string id){var c=new CardInstance(cards.GetCard(id)!);g.Player.Hand.Add(c);return c;}
    private MonsterInstance Unit(PlayerState p,string id,int slot,int pp,int dp){var m=new MonsterInstance(cards.GetCard(id)!){Slot=slot,IsTapped=false,HasSummoningSickness=false};m.CurrentPP=pp;m.CurrentDP=dp;p.Field.Add(m);return m;}
    private static void PlayTurn(GameEngine g){for(int i=0;i<60&&!g.IsOver&&g.DecisionPlayerId=="player";i++)if(!g.ExecuteAiStep())break;}

    [Fact]
    public void RolesComeFromCardText()
    {
        Assert.True(GameEngine.RoleOf(cards.GetCard("WCG-094")!).HasFlag(GameEngine.AiRole.Removal));
        Assert.True(GameEngine.RoleOf(cards.GetCard("WCG-098")!).HasFlag(GameEngine.AiRole.Wipe));
        Assert.True(GameEngine.RoleOf(cards.GetCard("WCG-020")!).HasFlag(GameEngine.AiRole.Wipe));
        Assert.True(GameEngine.RoleOf(cards.GetCard("WCG-014")!).HasFlag(GameEngine.AiRole.Burn));
        Assert.True(GameEngine.RoleOf(cards.GetCard("WCG-195")!).HasFlag(GameEngine.AiRole.Counter));
        Assert.Equal(GameEngine.AiRole.None,GameEngine.RoleOf(cards.GetCard("WCG-101")!));
        // Every card in the catalog gets a role or is plain; the classification never throws.
        Assert.All(cards.AllCards,c=>GameEngine.RoleOf(c));
    }

    [Fact]
    public void KeepsUnconditionalRemovalForARealThreat()
    {
        var g=Game();var removal=Hand(g,"WCG-094");var small=Unit(g.Computer,"WCG-101",0,700,1);
        PlayTurn(g);
        Assert.Contains(removal,g.Player.Hand);Assert.Contains(small,g.Computer.Field);
        var big=cards.AllCards.Where(c=>c.IsMonster&&(c.PP??0)>=2300).OrderByDescending(c=>c.PP).First();
        var h=Game();var r2=new CardInstance(cards.GetCard("WCG-094")!);h.Player.Hand.Add(r2);
        var threat=new MonsterInstance(big){Slot=0,IsTapped=false,HasSummoningSickness=false};h.Computer.Field.Add(threat);
        PlayTurn(h);
        Assert.DoesNotContain(r2,h.Player.Hand);Assert.DoesNotContain(threat,h.Computer.Field);
    }

    [Fact]
    public void HoldsBackMonstersWhenTheOpponentHasShownBoardWipes()
    {
        int Played(bool wipeWill)
        {
            var g=Game();
            foreach(var id in new[]{"WCG-101","WCG-101","WCG-101","WCG-101"})Hand(g,id);
            Unit(g.Player,"WCG-101",0,700,1);Unit(g.Player,"WCG-101",1,700,1);
            // A wipe in the graveyard reveals a will whose pool holds board wipes.
            var wipe=cards.GetCard("WCG-098")!;if(wipeWill)g.Computer.Graveyard.Add(new(wipe));
            var hand=g.Player.Hand.Count;PlayTurn(g);return hand-g.Player.Hand.Count;
        }
        Assert.True(Played(true)<=Played(false));
    }

    [Fact]
    public void OpponentModelUsesOnlyPublicCards()
    {
        var g=Game();var before=g.OpponentModel(g.Computer);
        g.Computer.Hand.Clear();for(int i=0;i<4;i++)g.Computer.Hand.Add(new(cards.GetCard("WCG-098")!));
        g.Computer.Deck.Clear();for(int i=0;i<20;i++)g.Computer.Deck.Add(new(cards.GetCard("WCG-014")!));
        Assert.Equal(before,g.OpponentModel(g.Computer));
        g.Computer.Graveyard.Add(new(cards.GetCard("WCG-098")!));
        Assert.NotEqual(before,g.OpponentModel(g.Computer));
        g.Computer.Hand.Clear();Assert.Equal(new GameEngine.OpponentOdds(0,0,0,0),g.OpponentModel(g.Computer));
    }
}
