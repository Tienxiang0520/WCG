using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;
namespace WcgTests;

// 新流派「斷路突襲」（WCG-319–327，2026-10-24）：清空正對面那一格，從空路越過嘲諷打臉。
public class LaneRaidTests
{
    private readonly CardDatabase cards;
    public LaneRaidTests(){var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));cards=new(env.Object);}
    private GameEngine Game(int energy=12){var g=new GameEngine(cards,new Random(17));g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1],true);foreach(var p in new[]{g.Player,g.Computer}){p.Hand.Clear();p.Field.Clear();p.Structures.Clear();p.EnergyZone.Clear();for(int i=0;i<energy;i++)p.EnergyZone.Add(new(cards.GetCard("WCG-101")!));}return g;}
    private CardInstance Hand(GameEngine g,int n){var c=new CardInstance(cards.GetCard($"WCG-{n:000}")!);g.Player.Hand.Add(c);return c;}
    private MonsterInstance Unit(PlayerState p,int n,int slot,bool tapped=false){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=tapped,HasSummoningSickness=false};p.Field.Add(m);return m;}
    private MonsterInstance Structure(PlayerState p,int n,int slot){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=false,HasSummoningSickness=false};p.Structures.Add(m);return m;}
    private static void Refresh(GameEngine g)=>typeof(GameEngine).GetMethod("RefreshBoard",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);
    private static void Settle(GameEngine g){for(int i=0;i<50&&g.IsWaiting;i++){if(g.CurrentPendingChoice is {} c)Assert.True(g.SelectChoice(c.Options.FirstOrDefault(o=>o.Id.StartsWith("SLOT:"))??c.Options.FirstOrDefault(o=>o.Id!="SKIP")??c.Options[0]));else{var t=g.CurrentPendingTarget!;var m=g.Player.Board.Concat(g.Computer.Board).First(x=>t.Validator!(x));Assert.True(g.SelectTarget(m));}}}

    [Fact] public void OldEraHasNoLaneCardsAndEverythingElseIsUnchanged()
    {
        var old=CardBalanceHistory.BeforeLaneRaidCards(cards);
        for(int n=319;n<=327;n++){Assert.NotNull(cards.GetCard($"WCG-{n}"));Assert.Null(old.GetCard($"WCG-{n}"));}
        Assert.Equal(JsonSerializer.Serialize(CounterHistory.BeforeLaneRaidCards(cards).AllCards),JsonSerializer.Serialize(old.AllCards));
        Assert.Equal(cards.AllCards.Where(c=>!CardBalanceHistory.IsLaneRaidCard(c.Id)).Select(c=>JsonSerializer.Serialize(c)),old.AllCards.Select(c=>JsonSerializer.Serialize(c)));
        Assert.Contains("印刷 PP",cards.GetCard("WCG-326")!.Text);
    }
    [Fact] public void ScoutPassesTauntOnlyWhileItsLaneIsOpen()
    {
        var g=Game();var s=Unit(g.Player,319,0);var taunt=Unit(g.Computer,113,1);Refresh(g);
        Assert.True(g.CanAttackPlayer(s));
        // An enchantment in the opposite slot doesn't close the lane.
        Structure(g.Computer,342,4);Refresh(g);Assert.True(g.CanAttackPlayer(s));
        var blocker=Unit(g.Computer,101,4);Refresh(g);Assert.False(g.CanAttackPlayer(s));
        g.Computer.Field.Remove(blocker);s.IsSilenced=true;Refresh(g);Assert.False(g.CanAttackPlayer(s));
    }
    [Fact] public void AlleyHunterHitsForTwoThroughAnOpenLane()
    {
        var g=Game();var h=Unit(g.Player,320,0);Unit(g.Computer,113,1);Refresh(g);var hp=g.Computer.Hp;
        Assert.Equal(2,g.PreviewAttack(h)!.PlayerDamage);Assert.True(g.Attack(g.Player,h));Settle(g);Assert.Equal(hp-2,g.Computer.Hp);Assert.Equal(1,h.CurrentDP);
    }
    [Fact] public void ObserverBouncesACheapMonsterOppositeOrDraws()
    {
        var g=Game();var foe=Unit(g.Computer,101,4);var hand=g.Computer.Hand.Count;Assert.True(g.SummonMonster(g.Player,Hand(g,321)));Settle(g);
        Assert.DoesNotContain(foe,g.Computer.Field);Assert.Equal(hand+1,g.Computer.Hand.Count);
        var g2=Game();var mine=g2.Player.Hand.Count;Assert.True(g2.SummonMonster(g2.Player,Hand(g2,321)));Settle(g2);Assert.Equal(mine+1,g2.Player.Hand.Count);
        var g3=Game();var big=Unit(g3.Computer,119,4);Assert.True(g3.SummonMonster(g3.Player,Hand(g3,321)));Settle(g3);Assert.Contains(big,g3.Computer.Field);
    }
    [Fact] public void GaleReadiesOnlyAnOpenLaneMonster()
    {
        var g=Game();var m=Unit(g.Player,101,0,tapped:true);m.HasAttacked=true;Refresh(g);
        Assert.True(g.CastSpellAt(g.Player,Hand(g,322),m));Settle(g);Assert.False(m.IsTapped);Assert.True(g.CanAttack(m));Assert.Equal(1300,m.CurrentPP);
        var g2=Game();var n=Unit(g2.Player,101,0,tapped:true);Unit(g2.Computer,101,4);Refresh(g2);
        Assert.True(g2.CastSpellAt(g2.Player,Hand(g2,322),n));Settle(g2);Assert.True(n.IsTapped);
    }
    [Fact] public void EdictNeedsAnArrowFacingTargetAndDrawsForTaunt()
    {
        var g=Game();var c=Hand(g,323);Unit(g.Computer,113,4);Refresh(g);Assert.False(g.CastSpell(g.Player,c));
        var arrow=Unit(g.Player,319,0);Refresh(g);var hand=g.Player.Hand.Count;
        Assert.True(g.CastSpell(g.Player,c));Settle(g);Assert.Empty(g.Computer.Field);Assert.Equal(hand,g.Player.Hand.Count); // −1 edict +1 draw
    }
    [Fact] public void FlankerGainsPpOnAnOpenLane()
    {
        var g=Game();var f=Unit(g.Player,324,0);Refresh(g);Assert.Equal(1600,f.CurrentPP);
        Unit(g.Computer,101,4);Refresh(g);Assert.Equal(1300,f.CurrentPP);
    }
    [Fact] public void BridgebreakerTapsAndKeepsTheFacingMonsterDown()
    {
        var g=Game();var foe=Unit(g.Computer,101,4);Assert.True(g.SummonMonster(g.Player,Hand(g,325)));Settle(g);Assert.True(foe.IsTapped);
        var other=Unit(g.Computer,101,3,tapped:true);
        Assert.True(g.EndTurn());Settle(g);Assert.True(foe.IsTapped);Assert.False(other.IsTapped);
        Assert.Equal("斷橋鎖住：回合開始不轉為直立",g.LaneStatus(foe));
    }
    [Fact] public void VanguardDestroysPrintedPpUpTo1800Opposite()
    {
        var g=Game();var foe=Unit(g.Computer,113,4);Assert.True(g.SummonMonster(g.Player,Hand(g,326)));Settle(g);
        Assert.DoesNotContain(foe,g.Computer.Field); // printed 1800
        var g2=Game();var big=Unit(g2.Computer,119,4);Assert.True(g2.SummonMonster(g2.Player,Hand(g2,326)));Settle(g2);Assert.Contains(big,g2.Computer.Field);
    }
    [Fact] public void ThousandRoadsAddsOneForEachOpenLaneThisTurn()
    {
        var g=Game();var a=Unit(g.Player,101,0);var b=Unit(g.Player,101,1);Unit(g.Computer,101,3);Refresh(g);
        Assert.True(g.CastSpell(g.Player,Hand(g,327)));Settle(g);
        Assert.Equal(2,g.PreviewAttack(a)!.PlayerDamage);Assert.Equal(1,g.PreviewAttack(b)!.PlayerDamage);
        Assert.True(g.EndTurn());Settle(g);Assert.True(g.EndTurn());Settle(g);Assert.Equal(1,g.PreviewAttack(a)!.PlayerDamage);
    }
    [Fact] public void MasterAiFindsTheOpenLane()
    {
        var g=Game();g.AiLevel=5;g.Player.IsAi=true;g.Computer.Hp=1;Unit(g.Computer,113,4);Unit(g.Computer,113,3);Hand(g,319);Refresh(g);
        for(int i=0;i<10&&!g.IsOver&&g.DecisionPlayerId==g.Player.Id;i++)if(!g.ExecuteAiStep())break;
        var scout=g.Player.Field.First(x=>x.Card.Id=="WCG-319");Assert.Null(g.Computer.Field.FirstOrDefault(x=>x.Slot==4-scout.Slot));
    }
}
