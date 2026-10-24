using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;
namespace WcgTests;

// 2026-10-24 沉默／驅散規則：沉默移除 DP 指示物；有奪魂指示物的怪物被沉默或驅散時，移除指示物並歸還擁有者。
public class SilenceCounterTests
{
    private readonly CardDatabase cards;
    public SilenceCounterTests(){var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));cards=new(env.Object);}
    private GameEngine Game(CardDatabase db){var g=new GameEngine(db,new Random(17));g.StartGame(db.PresetDecks[0],db.PresetDecks[1],true);foreach(var p in new[]{g.Player,g.Computer}){p.Hand.Clear();p.Field.Clear();p.Structures.Clear();p.EnergyZone.Clear();for(int i=0;i<12;i++)p.EnergyZone.Add(new(db.GetCard("WCG-101")!));}return g;}
    private static CardInstance Hand(GameEngine g,CardDatabase db,int n){var c=new CardInstance(db.GetCard($"WCG-{n:000}")!);g.Player.Hand.Add(c);return c;}
    private static MonsterInstance Unit(PlayerState p,CardDatabase db,int n,int slot){var m=new MonsterInstance(db.GetCard($"WCG-{n:000}")!){Slot=slot,HasSummoningSickness=false};p.Field.Add(m);return m;}
    private static void Refresh(GameEngine g)=>typeof(GameEngine).GetMethod("RefreshBoard",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);
    private static void Settle(GameEngine g,MonsterInstance? pick=null){for(int i=0;i<50&&g.IsWaiting;i++){if(g.CurrentPendingChoice is {} c)Assert.True(g.SelectChoice(c.Options.FirstOrDefault(o=>o.Id.StartsWith("SLOT:"))??c.Options.FirstOrDefault(o=>o.Id!="SKIP")??c.Options[0]));else{var t=g.CurrentPendingTarget!;var m=pick!=null&&t.Validator!(pick)?pick:g.Player.Board.Concat(g.Computer.Board).First(x=>t.Validator!(x));Assert.True(g.SelectTarget(m));}}}

    [Fact] public void SilenceRemovesDpCountersOnlyInTheNewEra()
    {
        foreach(var (db,expected) in new[]{(cards,0),(CardBalanceHistory.BeforeSilenceCounters(cards),2)})
        {
            var g=Game(db);var foe=Unit(g.Computer,db,101,1);foe.DpCounters=2;Refresh(g);
            Assert.True(g.CastSpellAt(g.Player,Hand(g,db,72),foe));Settle(g,foe);
            Assert.True(foe.IsSilenced);Assert.Equal(expected,foe.DpCounters);
        }
    }
    [Fact] public void SilencedUsurpedMonsterGoesBackToItsOwner()
    {
        var g=Game(cards);var foe=Unit(g.Computer,cards,113,2);Refresh(g);
        Assert.True(g.CastSpellAt(g.Player,Hand(g,cards,335),foe));Settle(g);
        Assert.Contains(foe,g.Player.Field);Assert.True(foe.SoulCounter);
        Unit(g.Computer,cards,101,2);Refresh(g); // its old slot is taken now: it goes to the nearest empty one
        Assert.True(g.CastSpellAt(g.Player,Hand(g,cards,72),foe));Settle(g,foe);
        Assert.Contains(foe,g.Computer.Field);Assert.False(foe.SoulCounter);Assert.Null(foe.OwnerId);Assert.NotEqual(2,foe.Slot);Assert.True(foe.IsSilenced);
    }
    [Fact] public void DispelledUsurpedMonsterGoesBackAndOldEraKeepsIt()
    {
        foreach(var (db,back) in new[]{(cards,true),(CardBalanceHistory.BeforeSilenceCounters(cards),false)})
        {
            var g=Game(db);var foe=Unit(g.Computer,db,113,2);Unit(g.Computer,db,101,0);Refresh(g);
            Assert.True(g.CastSpellAt(g.Player,Hand(g,db,335),foe));Settle(g);Assert.True(foe.SoulCounter);
            var targets=(IEnumerable<MonsterInstance>)typeof(GameEngine).GetMethod("DispelTargets",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,[g.Computer])!;
            Assert.Equal(back,targets.Contains(foe));
            if(!back)continue;
            typeof(GameEngine).GetMethod("Dispel",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,[g.Computer,true]);
            Assert.True(g.CurrentPendingTarget!.Validator!(foe));Assert.True(g.SelectTarget(foe));Settle(g);
            Assert.Contains(foe,g.Computer.Field);Assert.False(foe.SoulCounter);Assert.Equal(1,foe.Slot); // nearest empty slot of its owner to where it stood (slot 0)
        }
    }
    [Fact] public void OldEraOnlyChangesUsurpText()
    {
        var old=CardBalanceHistory.BeforeSilenceCounters(cards);
        Assert.Equal(JsonSerializer.Serialize(CounterHistory.BeforeSilenceCounters(cards).AllCards),JsonSerializer.Serialize(old.AllCards));
        Assert.Equal(["WCG-335"],cards.AllCards.Where(c=>JsonSerializer.Serialize(c)!=JsonSerializer.Serialize(old.GetCard(c.Id))).Select(c=>c.Id));
        Assert.True(CardBalanceHistory.SilenceCountersCatalog(cards));Assert.False(CardBalanceHistory.SilenceCountersCatalog(old));
    }
}
