using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;
namespace WcgTests;

// 新流派「潮汐輪迴」（WCG-373–381，2026-10-25）：收回自己的怪再打，進場效果重複使用。
public class TideCycleTests
{
    private readonly CardDatabase cards;
    public TideCycleTests(){var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));cards=new(env.Object);}
    private GameEngine Game(int energy=12){var g=new GameEngine(cards,new Random(17));g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1],true);foreach(var p in new[]{g.Player,g.Computer}){p.Hand.Clear();p.Field.Clear();p.Structures.Clear();p.EnergyZone.Clear();p.Graveyard.Clear();for(int i=0;i<energy;i++)p.EnergyZone.Add(new(cards.GetCard("WCG-101")!));}return g;}
    private CardInstance Hand(GameEngine g,int n){var c=new CardInstance(cards.GetCard($"WCG-{n:000}")!);g.Player.Hand.Add(c);return c;}
    private MonsterInstance Unit(PlayerState p,int n,int slot,bool tapped=false){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=tapped,HasSummoningSickness=false};p.Field.Add(m);return m;}
    private MonsterInstance Structure(PlayerState p,int n,int slot){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=false,HasSummoningSickness=false};p.Structures.Add(m);return m;}
    private static void Refresh(GameEngine g)=>typeof(GameEngine).GetMethod("RefreshBoard",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);
    private static void Settle(GameEngine g,string? pick=null){for(int i=0;i<80&&g.IsWaiting;i++){if(g.CurrentPendingChoice is {} c)Assert.True(g.SelectChoice(c.Options.FirstOrDefault(o=>pick!=null&&o.Id==pick)??c.Options.FirstOrDefault(o=>o.Id.StartsWith("SLOT:"))??c.Options.FirstOrDefault(o=>o.Id!="SKIP")??c.Options[0]));else{var t=g.CurrentPendingTarget!;var m=g.Player.Board.Concat(g.Computer.Board).First(x=>t.Validator!(x));Assert.True(g.SelectTarget(m));}}}

    [Fact] public void OldEraHasNoTideCardsAndEverythingElseIsUnchanged()
    {
        var old=CardBalanceHistory.BeforeTideCycleCards(cards);
        for(int n=373;n<=381;n++){Assert.NotNull(cards.GetCard($"WCG-{n}"));Assert.Null(old.GetCard($"WCG-{n}"));}
        Assert.Equal(JsonSerializer.Serialize(CounterHistory.BeforeTideCycleCards(cards).AllCards),JsonSerializer.Serialize(old.AllCards));
        Assert.Equal(cards.AllCards.Where(c=>!CardBalanceHistory.IsTideCycleCard(c.Id)&&!CardBalanceHistory.IsRotMireCard(c.Id)&&!CardBalanceHistory.IsSurgeStrikeCard(c.Id)&&!CardBalanceHistory.IsHolySpringCard(c.Id)&&!CardBalanceHistory.IsStarCountdownCard(c.Id)&&!CardBalanceHistory.IsFateFlipCard(c.Id)).Select(c=>JsonSerializer.Serialize(c)),old.AllCards.Select(c=>JsonSerializer.Serialize(c)));
        Assert.Equal(["理智","生機"],cards.GetCard("WCG-381")!.Wills.Order(StringComparer.Ordinal));
    }
    [Fact] public void GathererHealsAndDrawsAgainEveryTimeItIsPaidFor()
    {
        var g=Game();g.Player.Hp=4;var hand=g.Player.Hand.Count;
        Assert.True(g.SummonMonster(g.Player,Hand(g,373)));Settle(g);
        Assert.Equal(5,g.Player.Hp);Assert.Equal(hand+1,g.Player.Hand.Count);
    }
    [Fact] public void EbbcallReturnsAnAllyAndTheNextPaidMonsterCostsOneLess()
    {
        var g=Game();var ally=Unit(g.Player,373,0,true);Refresh(g);
        Assert.True(g.SummonMonster(g.Player,Hand(g,374)));Settle(g,ally.InstanceId.ToString());
        Assert.DoesNotContain(ally,g.Player.Field);var back=g.Player.Hand.Single(c=>c.Card.Id=="WCG-373");
        Assert.Equal(0,g.ActualCost(g.Player,back.Card));Assert.Equal(1,g.ActualCost(g.Player,cards.GetCard("WCG-374")!));
        Assert.True(g.SummonMonster(g.Player,back));Settle(g);
        Assert.Equal(2,g.ActualCost(g.Player,cards.GetCard("WCG-374")!));
    }
    [Fact] public void RecedingWaveDrawsOnlyForPrintedCostThreeOrMore()
    {
        var g=Game();var small=Unit(g.Player,373,0);var big=Unit(g.Player,377,1);Refresh(g);var hand=g.Player.Hand.Count;
        Assert.True(g.CastSpellAt(g.Player,Hand(g,375),small));Settle(g);Assert.Equal(hand+1,g.Player.Hand.Count);
        Assert.True(g.CastSpellAt(g.Player,Hand(g,375),big));Settle(g);Assert.Equal(hand+3,g.Player.Hand.Count);
    }
    [Fact] public void RingDrawsTwicePerTurnAndTortoiseGetsCheaper()
    {
        var g=Game();Structure(g.Player,378,4);var a=Unit(g.Player,373,0);var b=Unit(g.Player,373,1);var c=Unit(g.Player,373,2);Refresh(g);
        var turtle=Hand(g,379);Assert.Equal(5,g.ActualCost(g.Player,turtle.Card));var hand=g.Player.Hand.Count;
        foreach(var x in new[]{a,b,c}){Assert.True(g.CastSpellAt(g.Player,Hand(g,375),x));Settle(g);}
        // three returns, 1 card back each, ring draws only twice.
        Assert.Equal(hand+3+2,g.Player.Hand.Count);Assert.Equal(3,g.ActualCost(g.Player,turtle.Card));
    }
    [Fact] public void ReturnOfAllTidesReturnsEverythingDrawsAndDiscountsByTwo()
    {
        var g=Game();Unit(g.Player,373,0);Unit(g.Player,377,1);Unit(g.Player,380,2);Refresh(g);var hand=g.Player.Hand.Count;
        Assert.True(g.CastSpell(g.Player,Hand(g,381)));Settle(g);
        Assert.Empty(g.Player.Field);Assert.Equal(hand+3+3,g.Player.Hand.Count);
        Assert.Equal(0,g.ActualCost(g.Player,cards.GetCard("WCG-373")!));Assert.Equal(2,g.ActualCost(g.Player,cards.GetCard("WCG-380")!));
        Assert.Equal(6,g.ActualCost(g.Player,cards.GetCard("WCG-381")!));
    }
    [Fact] public void CatcherBouncesAnEnemyAndEnvoyFeedsTheMonstersItsArrowsPointAt()
    {
        var g=Game();var foe=Unit(g.Computer,113,0);Refresh(g);
        Assert.True(g.SummonMonster(g.Player,Hand(g,380)));Settle(g);Assert.DoesNotContain(foe,g.Computer.Field);Assert.Contains(g.Computer.Hand,c=>c.InstanceId==foe.InstanceId);
        var g2=Game();var l=Unit(g2.Player,101,0);var r=Unit(g2.Player,101,2);Refresh(g2);
        Assert.True(g2.SummonMonster(g2.Player,Hand(g2,376)));Settle(g2,"SLOT:1");
        Assert.Equal(1,l.DpCounters);Assert.Equal(1,r.DpCounters);Assert.Equal((l.Card.PP??0)+200,l.CurrentPP);
    }
}
