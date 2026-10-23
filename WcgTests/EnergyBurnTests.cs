using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;
namespace WcgTests;

// 新流派「焚能爆破」（WCG-310–318，2026-10-23）：把自己能量區的牌送入墓地，換便宜的直傷與爆發。
public class EnergyBurnTests
{
    private readonly CardDatabase cards;
    public EnergyBurnTests(){var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));cards=new(env.Object);}
    private GameEngine Game(int energy=12){var g=new GameEngine(cards,new Random(17));g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1],true);foreach(var p in new[]{g.Player,g.Computer}){p.Hand.Clear();p.Field.Clear();p.Structures.Clear();p.EnergyZone.Clear();for(int i=0;i<energy;i++)p.EnergyZone.Add(new(cards.GetCard("WCG-101")!));}return g;}
    private CardInstance Hand(GameEngine g,int n){var c=new CardInstance(cards.GetCard($"WCG-{n:000}")!);g.Player.Hand.Add(c);return c;}
    private CardInstance Energy(PlayerState p,int n,bool tapped=false){var c=new CardInstance(cards.GetCard($"WCG-{n:000}")!){IsTapped=tapped};p.EnergyZone.Add(c);return c;}
    private MonsterInstance Unit(PlayerState p,int n,int slot,bool tapped=false){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=tapped,HasSummoningSickness=false};p.Field.Add(m);return m;}
    private static void Refresh(GameEngine g)=>typeof(GameEngine).GetMethod("RefreshBoard",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);
    private static void Pick(GameEngine g,string id)=>Assert.True(g.SelectChoice(g.CurrentPendingChoice!.Options.First(o=>o.Id==id)));
    private static void Settle(GameEngine g){for(int i=0;i<50&&g.IsWaiting;i++){if(g.CurrentPendingChoice is {} c)Assert.True(g.SelectChoice(c.Options.FirstOrDefault(o=>o.Id.StartsWith("SLOT:"))??c.Options.FirstOrDefault(o=>o.Id!="SKIP")??c.Options[0]));else{var t=g.CurrentPendingTarget!;var m=g.Player.Board.Concat(g.Computer.Board).First(x=>t.Validator!(x));Assert.True(g.SelectTarget(m));}}}
    private MonsterInstance Summon(GameEngine g,int n){var c=Hand(g,n);Assert.True(g.SummonMonster(g.Player,c));Pick(g,"SLOT:0");return g.Player.Field.Single(m=>m.InstanceId==c.InstanceId);}

    private static string Id(CardInstance c)=>c.InstanceId.ToString();
    [Fact] public void OldEraHasNoBurnCardsAndEverythingElseIsUnchanged()
    {
        var old=CardBalanceHistory.BeforeEnergyBurnCards(cards);
        for(int n=310;n<=318;n++){Assert.NotNull(cards.GetCard($"WCG-{n}"));Assert.Null(old.GetCard($"WCG-{n}"));}
        Assert.Equal(cards.AllCards.Count(c=>int.Parse(c.Id[4..])<=318)-9,old.AllCards.Count);
        Assert.Equal(JsonSerializer.Serialize(CounterHistory.BeforeEnergyBurnCards(cards).AllCards),JsonSerializer.Serialize(old.AllCards));
        Assert.Equal(cards.AllCards.Where(c=>int.Parse(c.Id[4..])<=318&&!CardBalanceHistory.IsEnergyBurnCard(c.Id)).Select(c=>JsonSerializer.Serialize(c)),old.AllCards.Select(c=>JsonSerializer.Serialize(c)));
        Assert.Contains("印刷 PP 900",cards.GetCard("WCG-318")!.Text);
    }
    [Fact] public void IgniterBurnsUprightEnergyForOneDamage()
    {
        var g=Game(4);var hp=g.Computer.Hp;var target=g.Player.EnergyZone.Last();Summon(g,310);
        Assert.Equal(GameEngine.BurnOptionalTitle,g.CurrentPendingChoice!.Title);Pick(g,Id(target));
        Assert.Contains(target,g.Player.Graveyard);Assert.False(target.IsTapped);Assert.Equal(hp-1,g.Computer.Hp);
        var g2=Game(4);var hp2=g2.Computer.Hp;var n=g2.Player.EnergyZone.Count;Summon(g2,310);Pick(g2,"SKIP");Assert.Equal(hp2,g2.Computer.Hp);Assert.Equal(n,g2.Player.EnergyZone.Count);
    }
    [Fact] public void BurnBoltNeedsTwoEnergyAndDealsTwo()
    {
        var g=Game(2);var bolt=Hand(g,311);var hp=g.Computer.Hp;Assert.True(g.CastSpell(g.Player,bolt));Settle(g);
        Assert.Empty(g.Player.EnergyZone);Assert.Equal(hp-2,g.Computer.Hp);
        var g1=Game(1);Assert.False(g1.CanPlayCard(g1.Player,Hand(g1,311)));
    }
    [Fact] public void PriestGrowsForEachBurnedCard()
    {
        var g=Game(6);var priest=Unit(g.Player,312,1);Refresh(g);var pp=priest.CurrentPP;
        Assert.True(g.CastSpell(g.Player,Hand(g,311)));Settle(g);Assert.Equal(pp+600,priest.CurrentPP);
    }
    [Fact] public void LurkerGetsFiveHundredWhenItBurns()
    {
        var g=Game(4);var l=Summon(g,313);Pick(g,Id(g.Player.EnergyZone[0]));Assert.Equal(1800,l.CurrentPP);Assert.True(g.CanAttack(l));
    }
    [Fact] public void AshReclamationRevivesBurnedMonstersUpright()
    {
        var g=Game(4);var ash=Energy(g.Player,5);Summon(g,310);Pick(g,Id(ash));
        Assert.True(g.CastSpell(g.Player,Hand(g,314)));Pick(g,Id(ash));Settle(g);
        var m=g.Player.Field.Single(x=>x.InstanceId==ash.InstanceId);Assert.False(m.IsTapped);
        var g2=Game(4);var old=new CardInstance(cards.GetCard("WCG-005")!);g2.Player.Graveyard.Add(old);
        Assert.True(g2.CastSpell(g2.Player,Hand(g2,314)));Pick(g2,Id(old));Settle(g2);var o=g2.Player.Field.Single(x=>x.InstanceId==old.InstanceId);Assert.Equal(!o.HasCharge,o.IsTapped);
    }
    [Fact] public void BurnOutOnlyTakesTappedEnergy()
    {
        var g=Game(0);for(int i=0;i<3;i++)Energy(g.Player,101,tapped:true);var upright=Energy(g.Player,101);for(int i=0;i<3;i++)Energy(g.Player,101);var hp=g.Computer.Hp;
        Assert.True(g.CastSpell(g.Player,Hand(g,315)));
        Assert.All(g.CurrentPendingChoice!.Options.Where(o=>o.Id!="SKIP"),o=>Assert.True(g.Player.EnergyZone.Single(c=>Id(c)==o.Id).IsTapped));
        Settle(g);Assert.Contains(upright,g.Player.EnergyZone);Assert.Equal(hp-3,g.Computer.Hp);
    }
    [Fact] public void ScorchedSoldierBurnsOnDeath()
    {
        var g=Game(3);var s=Unit(g.Player,316,0);var foe=Unit(g.Computer,40,0);Refresh(g);var hp=g.Computer.Hp;
        Assert.True(g.Attack(g.Player,s,foe));Settle(g);Assert.Equal(2,g.Player.EnergyZone.Count);Assert.Equal(hp-1,g.Computer.Hp);
        var g0=Game(0);var s0=Unit(g0.Player,316,0);var f0=Unit(g0.Computer,40,0);Refresh(g0);var hp0=g0.Computer.Hp;Assert.True(g0.Attack(g0.Player,s0,f0));Settle(g0);Assert.Equal(hp0,g0.Computer.Hp);
    }
    [Fact] public void BloodAshPutsAGraveCardIntoEnergyAndDraws()
    {
        var g=Game(2);var dead=new CardInstance(cards.GetCard("WCG-040")!);g.Player.Graveyard.Add(dead);var hand=g.Player.Hand.Count;var spell=Hand(g,317);
        Assert.True(g.CastSpell(g.Player,spell));Pick(g,Id(dead));Settle(g);
        Assert.Contains(dead,g.Player.EnergyZone);Assert.True(dead.IsTapped);Assert.Equal(hand+1,g.Player.Hand.Count);
    }
    [Fact] public void WorldFlameKillsSmallMonstersOrHitsFace()
    {
        var g=Game(8);var small=Unit(g.Computer,51,0);Refresh(g);var hp=g.Computer.Hp;Summon(g,318);
        Pick(g,Id(g.Player.EnergyZone[0]));Assert.Equal(GameEngine.WorldFlameTitle,g.CurrentPendingChoice!.Title);Pick(g,"KILL");if(g.CurrentPendingTarget!=null)Assert.True(g.SelectTarget(small));else if(g.CurrentPendingChoice?.Title!=GameEngine.BurnOptionalTitle)Pick(g,small.InstanceId.ToString());
        Assert.DoesNotContain(small,g.Computer.Field);
        Pick(g,Id(g.Player.EnergyZone[0]));Assert.DoesNotContain(g.CurrentPendingChoice!.Options,o=>o.Id=="KILL");Pick(g,"FACE");
        Pick(g,"SKIP");Assert.Equal(hp-1,g.Computer.Hp);Assert.Equal(6,g.Player.EnergyZone.Count);
    }
    [Fact] public void MasterAiBurnsForLethal()
    {
        var g=Game(4);g.AiLevel=5;g.Player.IsAi=true;g.Computer.Hp=1;Summon(g,310);
        for(int i=0;i<4&&g.IsWaiting;i++)Assert.True(g.ExecuteAiStep());Assert.True(g.Computer.Hp<=0||g.IsOver);
    }
}
