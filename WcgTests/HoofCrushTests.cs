using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;
namespace WcgTests;

// 新流派「重蹄碾壓」（WCG-337–345，2026-10-24）：DP 指示物讓怪物 DP 越長越大，越過嘲諷一下打 3～4 點。
public class HoofCrushTests
{
    private readonly CardDatabase cards;
    public HoofCrushTests(){var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));cards=new(env.Object);}
    private GameEngine Game(int energy=12){var g=new GameEngine(cards,new Random(17));g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1],true);foreach(var p in new[]{g.Player,g.Computer}){p.Hand.Clear();p.Field.Clear();p.Structures.Clear();p.EnergyZone.Clear();for(int i=0;i<energy;i++)p.EnergyZone.Add(new(cards.GetCard("WCG-101")!));}return g;}
    private CardInstance Hand(GameEngine g,int n){var c=new CardInstance(cards.GetCard($"WCG-{n:000}")!);g.Player.Hand.Add(c);return c;}
    private MonsterInstance Unit(PlayerState p,int n,int slot,bool tapped=false){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=tapped,HasSummoningSickness=false};p.Field.Add(m);return m;}
    private MonsterInstance Structure(PlayerState p,int n,int slot){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=false,HasSummoningSickness=false};p.Structures.Add(m);return m;}
    private static void Refresh(GameEngine g)=>typeof(GameEngine).GetMethod("RefreshBoard",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);
    private static void Settle(GameEngine g){for(int i=0;i<50&&g.IsWaiting;i++){if(g.CurrentPendingChoice is {} c)Assert.True(g.SelectChoice(c.Options.FirstOrDefault(o=>o.Id.StartsWith("SLOT:"))??c.Options.FirstOrDefault(o=>o.Id!="SKIP")??c.Options[0]));else{var t=g.CurrentPendingTarget!;var m=g.Player.Board.Concat(g.Computer.Board).First(x=>t.Validator!(x));Assert.True(g.SelectTarget(m));}}}

    [Fact] public void OldEraHasNoHoofCardsAndEverythingElseIsUnchanged()
    {
        var old=CardBalanceHistory.BeforeHoofCrushCards(cards);
        for(int n=337;n<=345;n++){Assert.NotNull(cards.GetCard($"WCG-{n}"));Assert.Null(old.GetCard($"WCG-{n}"));}
        Assert.Equal(JsonSerializer.Serialize(CounterHistory.BeforeHoofCrushCards(cards).AllCards),JsonSerializer.Serialize(old.AllCards));
        Assert.Equal(CardBalanceHistory.BeforeArrowText(cards).AllCards.Where(c=>!CardBalanceHistory.IsHoofCrushCard(c.Id)&&!CardBalanceHistory.IsLaneRaidCard(c.Id)&&!CardBalanceHistory.IsSoulPuppetCard(c.Id)).Select(c=>JsonSerializer.Serialize(c)),old.AllCards.Select(c=>JsonSerializer.Serialize(c)));
        Assert.All(Enumerable.Range(337,9).Where(n=>n is 337 or 339 or 340 or 342 or 344),n=>Assert.Contains("DP 指示物",cards.GetCard($"WCG-{n}")!.Text));
    }
    [Fact] public void CalfGainsACounterEachCombatItSurvivesAndCountersLeaveWithIt()
    {
        var g=Game();var calf=Unit(g.Player,337,0);var small=Unit(g.Computer,101,0);Refresh(g);
        Assert.True(g.Attack(g.Player,calf,small));Settle(g);
        Assert.Equal(1,calf.DpCounters);Assert.Equal(2,calf.CurrentDP);
        // Leaving play removes the counters: bounced and summoned again it is back to printed DP.
        g.Player.Field.Remove(calf);var again=Unit(g.Player,337,1);Assert.Equal(0,again.DpCounters);Assert.Equal(1,again.CurrentDP);
    }
    [Fact] public void SilenceKeepsDpCounters()
    {
        var g=Game();var calf=Unit(g.Player,337,0);calf.DpCounters=2;calf.IsSilenced=true;Refresh(g);Assert.Equal(3,calf.CurrentDP);
    }
    [Fact] public void RhinoKingGrowsAtEndOfTurnOnlyWhenUpright()
    {
        var g=Game();var up=Unit(g.Player,339,0);var down=Unit(g.Player,339,1,tapped:true);Refresh(g);
        Assert.True(g.EndTurn());Settle(g);Assert.Equal(1,up.DpCounters);Assert.Equal(0,down.DpCounters);Assert.Equal(3,up.CurrentDP);
    }
    [Fact] public void HorseshoeAddsDpAndPpWhileAttached()
    {
        var g=Game();var m=Unit(g.Player,101,0);Refresh(g);Assert.True(g.CastSpellAt(g.Player,Hand(g,338),m));Settle(g);
        Assert.Equal(2,m.CurrentDP);Assert.Equal(1200,m.CurrentPP);Assert.Equal(0,m.DpCounters);
    }
    [Fact] public void FenceBreakerLetsABigHitterPastTauntOrElseGrows()
    {
        var g=Game();var m=Unit(g.Player,101,0);var taunt=Unit(g.Computer,113,0);Refresh(g);
        Assert.False(g.CanAttackPlayer(m));
        Assert.True(g.CastSpell(g.Player,Hand(g,340)));Settle(g);Assert.Equal(1,m.DpCounters);Assert.False(g.CanAttackPlayer(m));
        m.DpCounters=2;Assert.True(g.CastSpell(g.Player,Hand(g,340)));Settle(g);Assert.True(m.TurnUnblockable);Assert.True(g.CanAttackPlayer(m));
        var hp=g.Computer.Hp;Assert.True(g.Attack(g.Player,m));Settle(g);Assert.Equal(hp-3,g.Computer.Hp);
        Assert.True(g.EndTurn());Settle(g);Assert.False(m.TurnUnblockable);
    }
    [Fact] public void RagingHoofAddsOneForEachOtherBigMonster()
    {
        var g=Game();var big=Unit(g.Player,339,0);var calf=Unit(g.Player,337,1);calf.DpCounters=1;var small=Unit(g.Player,101,2);var w=Unit(g.Player,341,3);Refresh(g);
        var hp=g.Computer.Hp;Assert.Equal(3,g.PreviewAttack(w)!.PlayerDamage);Assert.True(g.Attack(g.Player,w));Settle(g);Assert.Equal(hp-3,g.Computer.Hp);Assert.Equal(1,w.CurrentDP);
    }
    [Fact] public void ArenaGrowsMonstersThatWinCombat()
    {
        var g=Game();Structure(g.Player,342,4);var m=Unit(g.Player,114,0);var foe=Unit(g.Computer,101,0);Refresh(g);
        Assert.True(g.Attack(g.Player,m,foe));Settle(g);Assert.Equal(1,m.DpCounters);
        var g2=Game();Structure(g2.Player,342,4);var weak=Unit(g2.Player,101,0);var big=Unit(g2.Computer,114,0);Refresh(g2);Assert.True(g2.Attack(g2.Player,weak,big));Settle(g2);Assert.Equal(0,big.DpCounters);
    }
    [Fact] public void IronhideShrinksWhatItAttacksOnlyWithThreeDp()
    {
        var g=Game();var hide=Unit(g.Player,343,0);var foe=Unit(g.Computer,119,0);Refresh(g); // 2400 vs 2600 − 500
        Assert.True(g.PreviewAttack(hide,foe)!.DefenderDies);Assert.True(g.Attack(g.Player,hide,foe));Settle(g);Assert.DoesNotContain(foe,g.Computer.Field);
        var g2=Game();var h2=Unit(g2.Player,343,0);h2.CurrentDP=2;var f2=Unit(g2.Computer,119,0);Refresh(g2);
        Assert.False(g2.PreviewAttack(h2,f2)!.DefenderDies);
    }
    [Fact] public void StampedeCountersEveryMonsterAndDraws()
    {
        var g=Game();var a=Unit(g.Player,101,0);var b=Unit(g.Player,101,1);Refresh(g);var hand=g.Player.Hand.Count;
        Assert.True(g.CastSpell(g.Player,Hand(g,344)));Settle(g);Assert.Equal(1,a.DpCounters);Assert.Equal(1,b.DpCounters);Assert.Equal(hand+1,g.Player.Hand.Count);
    }
    [Fact] public void LethalRamDoublesDpForTheTurnAndIgnoresTauntAtFour()
    {
        var g=Game();var m=Unit(g.Player,101,0);m.DpCounters=1;Unit(g.Computer,113,0);Refresh(g);
        Assert.True(g.CastSpellAt(g.Player,Hand(g,345),m));Settle(g);Assert.Equal(4,m.CurrentDP);Assert.True(g.CanAttackPlayer(m));
        Assert.True(g.EndTurn());Settle(g);Assert.Equal(2,m.CurrentDP);
        var g2=Game();var s=Unit(g2.Player,101,0);Unit(g2.Computer,113,0);Refresh(g2);Assert.True(g2.CastSpellAt(g2.Player,Hand(g2,345),s));Settle(g2);Assert.Equal(2,s.CurrentDP);Assert.False(g2.CanAttackPlayer(s));
    }
    [Fact] public void DpBreakdownListsCountersAndAttachments()
    {
        var g=Game();var m=Unit(g.Player,101,0);m.DpCounters=2;Refresh(g);Assert.True(g.CastSpellAt(g.Player,Hand(g,338),m));Settle(g);
        var parts=g.DamageModifiers(m);Assert.Equal(m.CurrentDP-(m.Card.DP??1),parts.Sum(x=>x.Amount));Assert.Contains(parts,x=>x.Label=="DP 指示物"&&x.Amount==2);
    }
    [Fact] public void MasterAiPlaysTheArchetype()
    {
        var g=Game();g.AiLevel=5;g.Player.IsAi=true;g.Computer.Hp=3;var m=Unit(g.Player,101,0);m.DpCounters=2;Unit(g.Computer,113,0);Hand(g,340);Refresh(g);
        for(int i=0;i<8&&!g.IsOver&&g.DecisionPlayerId==g.Player.Id;i++)if(!g.ExecuteAiStep())break;Assert.True(g.Computer.Hp<=0||g.IsOver);
    }
}
