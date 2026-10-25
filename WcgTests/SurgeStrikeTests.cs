using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;
namespace WcgTests;

// 新流派「怒濤連擊」（WCG-391–399，2026-10-25）：攻擊後轉為直立再攻擊；一切只持續本回合。
public class SurgeStrikeTests
{
    private readonly CardDatabase cards;
    public SurgeStrikeTests(){var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));cards=new(env.Object);}
    private GameEngine Game(int energy=12){var g=new GameEngine(cards,new Random(17));g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1],true);foreach(var p in new[]{g.Player,g.Computer}){p.Hand.Clear();p.Field.Clear();p.Structures.Clear();p.EnergyZone.Clear();p.Graveyard.Clear();for(int i=0;i<energy;i++)p.EnergyZone.Add(new(cards.GetCard("WCG-101")!));}return g;}
    private CardInstance Hand(GameEngine g,int n){var c=new CardInstance(cards.GetCard($"WCG-{n:000}")!);g.Player.Hand.Add(c);return c;}
    private MonsterInstance Unit(PlayerState p,int n,int slot,bool tapped=false){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=tapped,HasSummoningSickness=false};p.Field.Add(m);return m;}
    private MonsterInstance Structure(PlayerState p,int n,int slot){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=false,HasSummoningSickness=false};p.Structures.Add(m);return m;}
    private static void Refresh(GameEngine g)=>typeof(GameEngine).GetMethod("RefreshBoard",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);
    private static void Settle(GameEngine g,string? pick=null){for(int i=0;i<80&&g.IsWaiting;i++){if(g.CurrentPendingChoice is {} c)Assert.True(g.SelectChoice(c.Options.FirstOrDefault(o=>pick!=null&&o.Id==pick)??c.Options.FirstOrDefault(o=>o.Id.StartsWith("SLOT:"))??c.Options.FirstOrDefault(o=>o.Id!="SKIP")??c.Options[0]));else{var t=g.CurrentPendingTarget!;var m=g.Player.Board.Concat(g.Computer.Board).First(x=>t.Validator!(x));Assert.True(g.SelectTarget(m));}}}
    private static void Drain(GameEngine g)=>typeof(GameEngine).GetMethod("Drain",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);

    [Fact] public void OldEraHasNoSurgeCardsAndEverythingElseIsUnchanged()
    {
        var old=CardBalanceHistory.BeforeSurgeStrikeCards(cards);
        for(int n=391;n<=399;n++){Assert.NotNull(cards.GetCard($"WCG-{n}"));Assert.Null(old.GetCard($"WCG-{n}"));}
        Assert.Equal(JsonSerializer.Serialize(CounterHistory.BeforeSurgeStrikeCards(cards).AllCards),JsonSerializer.Serialize(old.AllCards));
        Assert.Equal(cards.AllCards.Where(c=>!CardBalanceHistory.IsSurgeStrikeCard(c.Id)&&!CardBalanceHistory.IsHolySpringCard(c.Id)&&!CardBalanceHistory.IsStarCountdownCard(c.Id)&&!CardBalanceHistory.IsFateFlipCard(c.Id)).Select(c=>JsonSerializer.Serialize(c)),old.AllCards.Select(c=>JsonSerializer.Serialize(c)));
    }
    [Fact] public void RecruitAttacksTwicePerTurn()
    {
        var g=Game();var a=Unit(g.Player,391,0);Refresh(g);
        Assert.True(g.Attack(g.Player,a));Settle(g);Assert.False(a.IsTapped);
        Assert.True(g.Attack(g.Player,a));Settle(g);Assert.True(a.IsTapped);Assert.False(g.CanAttack(a));Assert.Equal(2,a.SurgeAttacks);
    }
    [Fact] public void ChainBrawlerReadiesAfterKillsAtMostTwice()
    {
        var g=Game();var a=Unit(g.Player,394,0);var f=new[]{Unit(g.Computer,101,0),Unit(g.Computer,101,1),Unit(g.Computer,101,2)};Refresh(g);
        foreach(var x in f){Assert.True(g.CanAttack(a));Assert.True(g.Attack(g.Player,a,x));Settle(g);Assert.DoesNotContain(x,g.Computer.Field);}
        Assert.True(a.IsTapped);Assert.Equal(2,a.SurgeReadies);
    }
    [Fact] public void SwordmasterGrowsEachAttackAndResetsAtTurnEnd()
    {
        var g=Game();var a=Unit(g.Player,397,0);var b=Unit(g.Player,391,1);Refresh(g);var pp=a.CurrentPP;
        Assert.True(g.Attack(g.Player,a));Settle(g);Assert.Equal(pp+400,a.CurrentPP);
        Assert.True(g.CastSpellAt(g.Player,Hand(g,392),a));Settle(g);Assert.False(a.IsTapped);Assert.Equal(pp+700,a.CurrentPP);
        Assert.True(g.Attack(g.Player,a));Settle(g);Assert.Equal(pp+1100,a.CurrentPP);
        g.EndTurn();Drain(g);Refresh(g);Assert.Equal(0,a.SurgeAttacks);Assert.Equal(pp,a.CurrentPP);
    }
    [Fact] public void FrenziedStrideReadiesAllButNoFace()
    {
        var g=Game();var a=Unit(g.Player,101,0,true);var b=Unit(g.Player,101,1,true);Unit(g.Computer,101,0);Refresh(g);
        Assert.True(g.CastSpell(g.Player,Hand(g,396)));Settle(g);
        Assert.False(a.IsTapped);Assert.False(b.IsTapped);Assert.True(g.CanAttack(a));Assert.False(g.CanAttackPlayer(a));
    }
    [Fact] public void ForgeReadiesATappedMonster()
    {
        var g=Game();var a=Unit(g.Player,101,0,true);var s=Structure(g.Player,395,0);Refresh(g);
        Assert.True(g.Activate(g.Player,s));Settle(g);Assert.False(a.IsTapped);Assert.True(s.IsTapped);
    }
    [Fact] public void HornReadiesOnlyTheFirstAttackerEachTurn()
    {
        var g=Game();var h=Unit(g.Player,398,1);var a=Unit(g.Player,101,0);var b=Unit(g.Player,101,2);Refresh(g);
        Assert.True(g.Attack(g.Player,a));Settle(g);Assert.False(a.IsTapped);
        Assert.True(g.Attack(g.Player,b));Settle(g);Assert.True(b.IsTapped);
    }
    [Fact] public void ThousandBladesAttacksFourTimes()
    {
        var g=Game();var a=Unit(g.Player,399,0);Refresh(g);int n=0;
        while(g.CanAttack(a)&&n<10){Assert.True(g.Attack(g.Player,a));Settle(g);n++;}
        Assert.Equal(4,n);
    }
}
