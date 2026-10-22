using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;
namespace WcgTests;

// 2026-10-17: 冰霜魔導師 (029) and 時間靜止者 (035) no longer need a tapped card; both work directly on entry.
public class MageDirectTests
{
    private readonly CardDatabase cards;
    public MageDirectTests(){var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));cards=new(env.Object);}
    private static GameEngine Game(CardDatabase db){var g=new GameEngine(db,new Random(17));g.StartGame(db.PresetDecks[0],db.PresetDecks[1]);foreach(var p in new[]{g.Player,g.Computer}){p.Hand.Clear();p.Field.Clear();p.EnergyZone.Clear();for(int i=0;i<12;i++)p.EnergyZone.Add(new(db.GetCard("WCG-101")!));}return g;}
    private static CardInstance Hand(CardDatabase db,GameEngine g,int n){var c=new CardInstance(db.GetCard($"WCG-{n:000}")!);g.Player.Hand.Add(c);return c;}
    private static MonsterInstance Unit(CardDatabase db,PlayerState p,int n,int slot,bool tapped=false){var m=new MonsterInstance(db.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=tapped,HasSummoningSickness=false};p.Field.Add(m);p.Field.Sort((a,b)=>a.Slot.CompareTo(b.Slot));return m;}
    private static void Refresh(GameEngine g)=>typeof(GameEngine).GetMethod("RefreshBoard",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);
    private static void Slot(GameEngine g,int slot){if(g.CurrentPendingChoice?.Options.Any(o=>o.Id==$"SLOT:{slot}")==true)Assert.True(g.SelectChoice(g.CurrentPendingChoice.Options.First(o=>o.Id==$"SLOT:{slot}")));}

    [Fact] public void TextsChangedAndOldEraKeepsTheOldTexts()
    {
        var now=CardBalanceHistory.BeforeCardFixes(cards);
        Assert.Equal("進場：將 1 隻直立的敵方怪物橫置，然後抽 1 張牌。",now.GetCard("WCG-029")!.Text);
        Assert.Equal("進場：你可以將 1 隻敵方怪物移回手牌，然後將所有敵方怪物橫置。",now.GetCard("WCG-035")!.Text);
        var old=CardBalanceHistory.BeforeMageDirect(cards);
        Assert.StartsWith("每當此怪物交戰後",old.GetCard("WCG-029")!.Text);Assert.Contains("已橫置",old.GetCard("WCG-035")!.Text);
        foreach(var id in new[]{"WCG-029","WCG-035"}){Assert.Equal(old.GetCard(id)!.PP,now.GetCard(id)!.PP);Assert.Equal(old.GetCard(id)!.TotalCost,now.GetCard(id)!.TotalCost);}
        Assert.True(CardBalanceHistory.MageDirectCatalog(cards));Assert.False(CardBalanceHistory.MageDirectCatalog(old));
    }

    [Fact] public void FrostMagusTapsAnUprightEnemyAndDrawsOnEntry()
    {
        var g=Game(cards);var foe=Unit(cards,g.Computer,155,0);Refresh(g);var hand=g.Player.Hand.Count;
        Assert.True(g.SummonMonster(g.Player,Hand(cards,g,29)));Slot(g,1);
        Assert.NotNull(g.CurrentPendingTarget);Assert.True(g.SelectTarget(foe));
        Assert.True(foe.IsTapped);Assert.Equal(hand+1,g.Player.Hand.Count); // the card left the hand, one card was drawn
        // With no upright enemy it still draws.
        g=Game(cards);Unit(cards,g.Computer,155,0,tapped:true);Refresh(g);hand=g.Player.Hand.Count;
        Assert.True(g.SummonMonster(g.Player,Hand(cards,g,29)));Slot(g,1);
        Assert.Null(g.CurrentPendingTarget);Assert.Equal(hand+1,g.Player.Hand.Count);
    }

    [Fact] public void FrostMagusNoLongerActsAfterCombatButTheOldEraDoes()
    {
        var g=Game(cards);var magus=Unit(cards,g.Player,29,0);var foe=Unit(cards,g.Computer,101,0);foe.ShieldCounters=1;Refresh(g);
        Assert.True(g.Attack(g.Player,magus,foe));Assert.Contains(foe,g.Computer.Field);Assert.Contains(magus,g.Player.Field);Assert.False(foe.IsTapped);
        var old=CardBalanceHistory.BeforeMageDirect(cards);g=Game(old);magus=Unit(old,g.Player,29,0);foe=Unit(old,g.Computer,101,0);foe.ShieldCounters=1;Refresh(g);
        Assert.True(g.Attack(g.Player,magus,foe));Assert.Contains(foe,g.Computer.Field);Assert.True(foe.IsTapped);
    }

    [Fact] public void TimestopperMayReturnAnUprightEnemy()
    {
        var g=Game(cards);var foe=Unit(cards,g.Computer,155,0);var other=Unit(cards,g.Computer,101,1);Refresh(g);
        Assert.True(g.SummonMonster(g.Player,Hand(cards,g,35)));Slot(g,1);
        var choice=g.CurrentPendingChoice!;Assert.Contains(choice.Options,o=>o.Id==foe.InstanceId.ToString());
        Assert.True(g.SelectChoice(choice.Options.First(o=>o.Id==foe.InstanceId.ToString())));
        Assert.DoesNotContain(foe,g.Computer.Field);Assert.Contains(g.Computer.Hand,c=>c.InstanceId==foe.InstanceId);Assert.True(other.IsTapped);
        // The old era only offers monsters that were already tapped.
        var old=CardBalanceHistory.BeforeMageDirect(cards);g=Game(old);foe=Unit(old,g.Computer,155,0);Refresh(g);
        Assert.True(g.SummonMonster(g.Player,Hand(old,g,35)));Slot(g,1);
        Assert.True(g.CurrentPendingChoice==null||g.CurrentPendingChoice.Options.All(o=>o.Id!=foe.InstanceId.ToString()));Assert.True(foe.IsTapped);
    }
}
