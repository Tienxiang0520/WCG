using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;
namespace WcgTests;

// 新流派「翠語夾縫」（WCG-273–281）與夾縫狙擊（WCG-282），2026-10-23：印刷 PP 1300–1700 的中堅，在場上靠手牌、隊友或交戰長大。
public class ThresholdGapTests
{
    private readonly CardDatabase cards;
    public ThresholdGapTests(){var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));cards=new(env.Object);}
    private GameEngine Game(){var g=new GameEngine(cards,new Random(17));g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1],true);foreach(var p in new[]{g.Player,g.Computer}){p.Hand.Clear();p.Field.Clear();p.Structures.Clear();p.EnergyZone.Clear();for(int i=0;i<12;i++)p.EnergyZone.Add(new(cards.GetCard("WCG-101")!));}Assert.Equal("player",g.CurrentTurnPlayerId);return g;}
    private CardInstance Hand(GameEngine g,int n){var c=new CardInstance(cards.GetCard($"WCG-{n:000}")!);g.Player.Hand.Add(c);return c;}
    private MonsterInstance Unit(PlayerState p,int n,int slot,bool tapped=false){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=tapped,HasSummoningSickness=false};p.Field.Add(m);p.Field.Sort((a,b)=>a.Slot.CompareTo(b.Slot));return m;}
    private static void Refresh(GameEngine g)=>typeof(GameEngine).GetMethod("RefreshBoard",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);
    private static void Choose(GameEngine g,Func<ChoiceOption,bool> pick)=>Assert.True(g.SelectChoice(g.CurrentPendingChoice!.Options.First(pick)));
    private static void Settle(GameEngine g){while(g.CurrentPendingChoice is {} c)Assert.True(g.SelectChoice(c.Options.Last()));}
    private MonsterInstance Summon(GameEngine g,int n,int slot=0){var c=Hand(g,n);Assert.True(g.SummonMonster(g.Player,c));Choose(g,o=>o.Id==$"SLOT:{slot}");return g.Player.Field.Single(m=>m.InstanceId==c.InstanceId);}
    private void Fill(GameEngine g,int n){g.Player.Hand.Clear();for(int i=0;i<n;i++)Hand(g,101);}

    [Fact] public void OldEraHasNoGapCardsAndEverythingElseIsUnchanged()
    {
        var old=CardBalanceHistory.BeforeThresholdGapCards(cards);
        for(int n=273;n<=282;n++){Assert.NotNull(cards.GetCard($"WCG-{n}"));Assert.Null(old.GetCard($"WCG-{n}"));}
        Assert.Equal(cards.AllCards.Where(c=>!CardBalanceHistory.IsThresholdGapCard(c.Id)).Select(c=>JsonSerializer.Serialize(c)),old.AllCards.Select(c=>JsonSerializer.Serialize(c)));
        Assert.Equal(JsonSerializer.Serialize(CounterHistory.BeforeThresholdGapCards(cards).AllCards),JsonSerializer.Serialize(old.AllCards));
    }
    [Fact] public void GapMonstersPrintBetweenTheRemovalThresholds()
    {
        foreach(var n in new[]{273,274,275,276,278,279}){var c=cards.GetCard($"WCG-{n}")!;Assert.InRange(c.PP!.Value,1300,1700);}
        Assert.Equal("中立",cards.GetCard("WCG-282")!.Will);
        Assert.Equal(4,cards.GetCard("WCG-282")!.TotalCost);
        Assert.Contains("印刷 PP",cards.GetCard("WCG-282")!.Text);
    }
    [Fact] public void ScribeAndStagAndKeeperGrowWithHandSize()
    {
        var g=Game();var scribe=Unit(g.Player,273,0);var stag=Unit(g.Player,276,1);var keeper=Unit(g.Player,279,2);
        Fill(g,3);Refresh(g);Assert.Equal(1600,scribe.CurrentPP);Assert.Equal(1600,stag.CurrentPP);Assert.Equal(1500,keeper.CurrentPP);
        Fill(g,7);Refresh(g);Assert.Equal(1800,scribe.CurrentPP);Assert.Equal(2000,stag.CurrentPP);Assert.Equal(2000,keeper.CurrentPP);
    }
    [Fact] public void ColossusAndElderTreeCountAllies()
    {
        var g=Game();var col=Unit(g.Player,275,0);var tree=Unit(g.Player,281,1);Unit(g.Player,101,2);Unit(g.Player,155,3);Refresh(g);
        Assert.Equal(1700+600,col.CurrentPP);Assert.Equal(2200+400,tree.CurrentPP); // tree: 275 and 155 print 1300–1700; 101 does not
        col.IsSilenced=true;Refresh(g);Assert.Equal(1700,col.CurrentPP);
    }
    [Fact] public void RingWardenGetsPlus300OnlyInCombat()
    {
        var g=Game();var warden=Unit(g.Computer,274,0);var a=Unit(g.Player,155,1);Refresh(g);Assert.Equal(1300,warden.CurrentPP);
        Assert.True(g.Attack(g.Player,a,warden));Settle(g); // 1300+300 = 1600 ties 155's 1600
        Assert.DoesNotContain(a,g.Player.Field);Assert.DoesNotContain(warden,g.Computer.Field);
        var g2=Game();var w2=Unit(g2.Computer,274,0);var b=Unit(g2.Player,154,1);Refresh(g2);
        Assert.True(g2.Attack(g2.Player,b,w2));Settle(g2);Assert.DoesNotContain(b,g2.Player.Field);Assert.Contains(w2,g2.Computer.Field);
    }
    [Fact] public void FertileInfusionAddsFiveHundredForOneCombat()
    {
        var g=Game();var mine=Unit(g.Player,154,0);var foe=Unit(g.Computer,155,1);Refresh(g);var spell=Hand(g,277);
        Assert.True(g.CastSpellAt(g.Player,spell,mine));Settle(g);Refresh(g);Assert.Equal(1800,mine.CurrentPP);
        Assert.True(g.Attack(g.Player,mine,foe));Settle(g);Refresh(g);
        Assert.DoesNotContain(foe,g.Computer.Field);Assert.Empty(mine.Attachments);Assert.Equal(1300,mine.CurrentPP);
    }
    [Fact] public void ArchivistDrawsAndThoughtGrowthDrawsTwoWithABigBody()
    {
        var g=Game();var hand=g.Player.Hand.Count;var c=Hand(g,278);Assert.True(g.SummonMonster(g.Player,c));Choose(g,o=>o.Id=="SLOT:0");Settle(g);
        Assert.Equal(hand+1,g.Player.Hand.Count);
        var g2=Game();var s=Hand(g2,280);Assert.True(g2.CastSpell(g2.Player,s));Settle(g2);Assert.Single(g2.Player.Hand);
        var g3=Game();Unit(g3.Player,155,0);Refresh(g3);var s3=Hand(g3,280);Assert.True(g3.CastSpell(g3.Player,s3));Settle(g3);Assert.Equal(2,g3.Player.Hand.Count);
    }
    [Fact] public void GapSnipeChecksPrintedNotCurrentPP()
    {
        var g=Game();var big=Unit(g.Computer,273,0);var small=Unit(g.Computer,101,1);var wall=Unit(g.Computer,113,2);
        g.Computer.Hand.Clear();for(int i=0;i<7;i++)g.Computer.Hand.Add(new(cards.GetCard("WCG-101")!));Refresh(g);Assert.Equal(1800,big.CurrentPP);
        var spell=Hand(g,282);var targets=g.GetDirectPlayTargets(g.Player,spell);
        Assert.Contains(big,targets);Assert.DoesNotContain(small,targets);Assert.DoesNotContain(wall,targets);
        Assert.True(g.CastSpellAt(g.Player,spell,big));Settle(g);Assert.DoesNotContain(big,g.Computer.Field);
    }
}
