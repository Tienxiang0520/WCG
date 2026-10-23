using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;
namespace WcgTests;

// 新流派「烽火守望」（WCG-292–300，2026-10-23）：直立待命到你的回合結束就造成傷害或回復。
public class BeaconWatchTests
{
    private readonly CardDatabase cards;
    public BeaconWatchTests(){var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));cards=new(env.Object);}
    private GameEngine Game(){var g=new GameEngine(cards,new Random(17));g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1],true);foreach(var p in new[]{g.Player,g.Computer}){p.Hand.Clear();p.Field.Clear();p.Structures.Clear();p.EnergyZone.Clear();for(int i=0;i<12;i++)p.EnergyZone.Add(new(cards.GetCard("WCG-101")!));}Assert.Equal("player",g.CurrentTurnPlayerId);return g;}
    private CardInstance Hand(GameEngine g,int n){var c=new CardInstance(cards.GetCard($"WCG-{n:000}")!);g.Player.Hand.Add(c);return c;}
    private MonsterInstance Unit(PlayerState p,int n,int slot,bool tapped=false){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=tapped,HasSummoningSickness=false};p.Field.Add(m);p.Field.Sort((a,b)=>a.Slot.CompareTo(b.Slot));return m;}
    private static void Refresh(GameEngine g)=>typeof(GameEngine).GetMethod("RefreshBoard",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);
    private static void Settle(GameEngine g){for(int i=0;i<50&&g.IsWaiting;i++){if(g.CurrentPendingChoice is {} c)Assert.True(g.SelectChoice(c.Options.FirstOrDefault(o=>o.Id!="SKIP")??c.Options[0]));else{var t=g.CurrentPendingTarget!;var m=g.Player.Board.Concat(g.Computer.Board).First(x=>t.Validator!(x));Assert.True(g.SelectTarget(m));}}}
    private static void End(GameEngine g){Assert.True(g.EndTurn());Settle(g);}

    [Fact] public void OldEraHasNoBeaconCardsAndEverythingElseIsUnchanged()
    {
        var old=CardBalanceHistory.BeforeBeaconWatchCards(cards);
        for(int n=292;n<=300;n++){Assert.NotNull(cards.GetCard($"WCG-{n}"));Assert.Null(old.GetCard($"WCG-{n}"));}
        Assert.Equal(cards.AllCards.Count(c=>int.Parse(c.Id[4..])<=300)-9,old.AllCards.Count);
        Assert.Equal(JsonSerializer.Serialize(CounterHistory.BeforeBeaconWatchCards(cards).AllCards),JsonSerializer.Serialize(old.AllCards));
        Assert.Equal(cards.AllCards.Where(c=>int.Parse(c.Id[4..])<=300&&!CardBalanceHistory.IsBeaconWatchCard(c.Id)).Select(c=>JsonSerializer.Serialize(c)),old.AllCards.Select(c=>JsonSerializer.Serialize(c)));
        Assert.All(cards.AllCards.Where(c=>CardBalanceHistory.IsBeaconWatchCard(c.Id)),c=>{Assert.All(c.Keywords,k=>Assert.Contains(k,new[]{"嘲諷"}));Assert.Contains(c.Will,new[]{"秩序","生機"});});
    }
    [Fact] public void SentryBurnsOnlyWhileUpright()
    {
        var g=Game();Unit(g.Player,292,0);var tapped=Unit(g.Player,292,1,tapped:true);Refresh(g);var hp=g.Computer.Hp;
        End(g);Assert.Equal(hp-1,g.Computer.Hp);
    }
    [Fact] public void WardenHealsWhileUpright()
    {
        var g=Game();g.Player.Hp=3;var w=Unit(g.Player,293,0);Refresh(g);Assert.True(w.IsTaunt);End(g);Assert.Equal(4,g.Player.Hp);
        var g2=Game();g2.Player.Hp=3;Unit(g2.Player,293,0,tapped:true);Refresh(g2);End(g2);Assert.Equal(3,g2.Player.Hp);
    }
    [Fact] public void HornUntapsEverythingButNothingCanAttackThisTurn()
    {
        var g=Game();var a=Unit(g.Player,155,0,tapped:true);var b=Unit(g.Player,292,1,tapped:true);Unit(g.Computer,101,2);Refresh(g);
        Assert.True(g.CastSpell(g.Player,Hand(g,294)));Settle(g);
        Assert.False(a.IsTapped);Assert.False(b.IsTapped);Assert.False(g.CanAttack(a));Assert.False(g.CanAttack(b));
        var hp=g.Computer.Hp;End(g);Assert.Equal(hp-1,g.Computer.Hp); // the untapped sentry still burns
        End(g);Assert.True(g.CanAttack(a)); // banned for that turn only
    }
    [Fact] public void TowerNeedsThreeUprightMonsters()
    {
        var g=Game();g.Player.Structures.Add(new(cards.GetCard("WCG-295")!){Slot=4});Unit(g.Player,101,0);Unit(g.Player,101,1);var t=Unit(g.Player,101,2,tapped:true);Refresh(g);
        var hp=g.Computer.Hp;End(g);Assert.Equal(hp,g.Computer.Hp);
        End(g);hp=g.Computer.Hp;End(g);Assert.Equal(hp-1,g.Computer.Hp);
    }
    [Fact] public void ElderTreeCannotAttackAndBoostsOtherUprightMonsters()
    {
        var g=Game();var tree=Unit(g.Player,296,0);var up=Unit(g.Player,101,1);var down=Unit(g.Player,101,2,tapped:true);Unit(g.Computer,101,0);Refresh(g);
        Assert.False(g.CanAttack(tree));Assert.True(tree.IsTaunt);Assert.Equal(1200,up.CurrentPP);Assert.Equal(1000,down.CurrentPP);Assert.Equal(2100,tree.CurrentPP);
        Assert.Equal(1200,(up.Card.PP??0)+g.PowerModifiers(up).Sum(x=>x.Amount));Assert.Contains(g.PowerModifiers(up),x=>x.Source?.Id=="WCG-296"&&x.Amount==200);
        tree.IsTapped=true;Assert.Equal(1000,up.CurrentPP);
    }
    [Fact] public void VigilPrayerAddsFourHundredOnlyWhileUpright()
    {
        var g=Game();var m=Unit(g.Player,101,0);var foe=Unit(g.Computer,101,0);Refresh(g);var spell=Hand(g,297);
        Assert.Contains(m,g.GetDirectPlayTargets(g.Player,spell));Assert.DoesNotContain(foe,g.GetDirectPlayTargets(g.Player,spell));
        Assert.True(g.CastSpellAt(g.Player,spell,m));Settle(g);Assert.Equal(1400,m.CurrentPP);Assert.Equal(1400,1000+g.PowerModifiers(m).Sum(x=>x.Amount));
        m.IsTapped=true;Assert.Equal(1000,m.CurrentPP);
    }
    [Fact] public void HeraldLightsUprightNeighbours()
    {
        var g=Game();Unit(g.Player,101,0);var herald=Unit(g.Player,298,1);Unit(g.Player,101,2);Unit(g.Player,101,4);Refresh(g);
        Assert.Equal(["left","right"],herald.Card.Arrows);var hp=g.Computer.Hp;End(g);Assert.Equal(hp-2,g.Computer.Hp);
        var g2=Game();Unit(g2.Player,101,0,tapped:true);Unit(g2.Player,298,1);Unit(g2.Player,101,2);Refresh(g2);hp=g2.Computer.Hp;End(g2);Assert.Equal(hp-1,g2.Computer.Hp);
    }
    [Fact] public void MorningDewHealsPerUprightMonsterUpToThree()
    {
        var g=Game();g.Player.Hp=1;for(int i=0;i<4;i++)Unit(g.Player,101,i);Unit(g.Player,101,4,tapped:true);Refresh(g);
        Assert.True(g.CastSpell(g.Player,Hand(g,299)));Settle(g);Assert.Equal(4,g.Player.Hp);
    }
    [Fact] public void CommanderBurnsForOtherUprightMonsters()
    {
        var g=Game();Unit(g.Player,101,0);Unit(g.Player,101,1);Unit(g.Player,101,2,tapped:true);Refresh(g);var hp=g.Computer.Hp;
        var c=Hand(g,300);Assert.True(g.SummonMonster(g.Player,c));Settle(g);Assert.Equal(hp-2,g.Computer.Hp);
    }
    [Fact] public void MasterAiKeepsSentriesHomeAndFinishesAGame()
    {
        var g=Game();g.AiLevel=5;g.Player.IsAi=true;var s=Unit(g.Player,292,0);Unit(g.Computer,65,0);Refresh(g);
        for(int i=0;i<20&&g.CurrentTurnPlayerId=="player"&&!g.IsOver;i++)if(!g.ExecuteAiStep())break;
        Assert.Contains(s,g.Player.Field);
    }
}
