using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;
namespace WcgTests;

// 新流派「伏能喚醒」（WCG-301–309，2026-10-23）：把怪物蓋進能量區，再不付費從能量區召喚（不觸發進場）。
public class EnergyVaultTests
{
    private readonly CardDatabase cards;
    public EnergyVaultTests(){var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));cards=new(env.Object);}
    private GameEngine Game(int energy=12){var g=new GameEngine(cards,new Random(17));g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1],true);foreach(var p in new[]{g.Player,g.Computer}){p.Hand.Clear();p.Field.Clear();p.Structures.Clear();p.EnergyZone.Clear();for(int i=0;i<energy;i++)p.EnergyZone.Add(new(cards.GetCard("WCG-101")!));}return g;}
    private CardInstance Hand(GameEngine g,int n){var c=new CardInstance(cards.GetCard($"WCG-{n:000}")!);g.Player.Hand.Add(c);return c;}
    private CardInstance Energy(PlayerState p,int n,bool tapped=false){var c=new CardInstance(cards.GetCard($"WCG-{n:000}")!){IsTapped=tapped};p.EnergyZone.Add(c);return c;}
    private MonsterInstance Unit(PlayerState p,int n,int slot,bool tapped=false){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=tapped,HasSummoningSickness=false};p.Field.Add(m);return m;}
    private static void Refresh(GameEngine g)=>typeof(GameEngine).GetMethod("RefreshBoard",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);
    private static void Pick(GameEngine g,string id)=>Assert.True(g.SelectChoice(g.CurrentPendingChoice!.Options.First(o=>o.Id==id)));
    private static void Settle(GameEngine g){for(int i=0;i<50&&g.IsWaiting;i++){if(g.CurrentPendingChoice is {} c)Assert.True(g.SelectChoice(c.Options.FirstOrDefault(o=>o.Id.StartsWith("SLOT:"))??c.Options.FirstOrDefault(o=>o.Id!="SKIP")??c.Options[0]));else{var t=g.CurrentPendingTarget!;var m=g.Player.Board.Concat(g.Computer.Board).First(x=>t.Validator!(x));Assert.True(g.SelectTarget(m));}}}
    private MonsterInstance Summon(GameEngine g,int n){var c=Hand(g,n);Assert.True(g.SummonMonster(g.Player,c));Pick(g,"SLOT:0");return g.Player.Field.Single(m=>m.InstanceId==c.InstanceId);}

    [Fact] public void OldEraHasNoVaultCardsAndEverythingElseIsUnchanged()
    {
        var old=CardBalanceHistory.BeforeEnergyVaultCards(cards);
        for(int n=301;n<=309;n++){Assert.NotNull(cards.GetCard($"WCG-{n}"));Assert.Null(old.GetCard($"WCG-{n}"));}
        Assert.Equal(cards.AllCards.Count(c=>int.Parse(c.Id[4..])<=309)-9,old.AllCards.Count);
        Assert.Equal(JsonSerializer.Serialize(CounterHistory.BeforeEnergyVaultCards(cards).AllCards),JsonSerializer.Serialize(old.AllCards));
        Assert.Equal(CardBalanceHistory.BeforeArrowText(cards).AllCards.Where(c=>int.Parse(c.Id[4..])<=309&&!CardBalanceHistory.IsEnergyVaultCard(c.Id)).Select(c=>JsonSerializer.Serialize(c)),old.AllCards.Select(c=>JsonSerializer.Serialize(c)));
        Assert.DoesNotContain(cards.AllCards.Where(c=>CardBalanceHistory.IsEnergyVaultCard(c.Id)),c=>c.Text.Contains("檢視"));
    }
    [Fact] public void ApprenticeHidesAHandMonsterTappedWithoutUsingTheEnergyFill()
    {
        var g=Game();var big=Hand(g,306);Hand(g,24);Summon(g,301);
        Assert.Equal(GameEngine.HideMonsterTitle,g.CurrentPendingChoice!.Title);Assert.DoesNotContain(g.CurrentPendingChoice.Options,o=>o.PreviewCard?.Id=="WCG-024");
        Pick(g,big.InstanceId.ToString());Assert.Contains(big,g.Player.EnergyZone);Assert.True(big.IsTapped);Assert.DoesNotContain(big,g.Player.Hand);Assert.False(g.Player.HasFilledEnergyThisTurn);
        var g2=Game();var keep=Hand(g2,306);Summon(g2,301);Pick(g2,"SKIP");Assert.Contains(keep,g2.Player.Hand);
    }
    [Fact] public void VaultSealHidesAnyCardAndDraws()
    {
        var g=Game();var spell=Hand(g,24);var seal=Hand(g,302);var hand=g.Player.Hand.Count;Assert.True(g.CastSpell(g.Player,seal));
        Assert.Equal(GameEngine.HideCardTitle,g.CurrentPendingChoice!.Title);Pick(g,spell.InstanceId.ToString());Settle(g);
        Assert.Contains(spell,g.Player.EnergyZone);Assert.True(spell.IsTapped);Assert.Equal(hand-1,g.Player.Hand.Count);
        var lone=Game();var only=Hand(lone,302);Assert.False(lone.CanPlayCard(lone.Player,only));
    }
    [Fact] public void SigilSummonsAFourCostMonsterUprightWithoutDeploy()
    {
        var g=Game();var small=Energy(g.Player,29);var big=Energy(g.Player,306);Unit(g.Computer,101,0);var hand=g.Player.Hand.Count;
        var sigil=Hand(g,303);Assert.True(g.CastSpell(g.Player,sigil));
        Assert.Equal(GameEngine.EnergySummonTitle,g.CurrentPendingChoice!.Title);Assert.DoesNotContain(g.CurrentPendingChoice.Options,o=>o.Id==big.InstanceId.ToString());
        Pick(g,small.InstanceId.ToString());Settle(g);
        var m=g.Player.Field.Single(x=>x.Card.Id=="WCG-029");Assert.False(m.IsTapped);Assert.True(m.FromEnergy);Assert.DoesNotContain(small,g.Player.EnergyZone);
        Assert.All(g.Computer.Field,x=>Assert.False(x.IsTapped)); // 冰霜魔導師's 進場 did not trigger
        Assert.Equal(hand,g.Player.Hand.Count);Assert.True(g.CanAttack(m)); // 直立進場 = 可以攻擊
        var none=Game(0);var s2=Hand(none,303);Energy(none.Player,306);Assert.False(none.CanPlayCard(none.Player,s2));
    }
    [Fact] public void SentinelGetsAShieldCounterWithFiveEnergy()
    {
        var g=Game(5);var s=Summon(g,304);Settle(g);Assert.Equal(1,s.ShieldCounters);
        var g2=Game(4);var s2=Summon(g2,304);Settle(g2);Assert.Equal(0,s2.ShieldCounters);
    }
    [Fact] public void KeeperGoesToTheEnergyZoneWhenItDies()
    {
        var g=Game();var k=Unit(g.Player,305,0);var foe=Unit(g.Computer,40,0);Refresh(g);var energy=g.Player.EnergyZone.Count;
        Assert.True(g.Attack(g.Player,k,foe));Settle(g);
        Assert.DoesNotContain(k,g.Player.Field);Assert.Equal(energy+1,g.Player.EnergyZone.Count);Assert.Contains(g.Player.EnergyZone,c=>c.Card.Id=="WCG-305"&&c.IsTapped);Assert.DoesNotContain(g.Player.Graveyard,c=>c.Card.Id=="WCG-305");
    }
    [Fact] public void ColossusIsBiggerOnlyWhenSummonedFromEnergy()
    {
        var g=Game();var c=Energy(g.Player,306);Assert.True(g.CastSpell(g.Player,Hand(g,307)));Pick(g,c.InstanceId.ToString());Settle(g);
        var m=g.Player.Field.Single(x=>x.Card.Id=="WCG-306");Assert.Equal(2800,m.CurrentPP);Assert.True(m.IsTapped);Assert.Equal(2800,2500+g.PowerModifiers(m).Sum(x=>x.Amount));
        var g2=Game();var plain=Summon(g2,306);Settle(g2);Assert.Equal(2500,plain.CurrentPP);
    }
    [Fact] public void StarsRealignSummonsUpToTwoTapped()
    {
        var g=Game();var a=Energy(g.Player,40);var b=Energy(g.Player,80);Assert.True(g.CastSpell(g.Player,Hand(g,307)));
        Pick(g,a.InstanceId.ToString());Pick(g,"SLOT:0");Assert.Equal(GameEngine.EnergySummonMoreTitle,g.CurrentPendingChoice!.Title);Pick(g,b.InstanceId.ToString());Pick(g,"SLOT:1");
        Assert.Equal(2,g.Player.Field.Count);Assert.All(g.Player.Field,m=>Assert.True(m.IsTapped));Assert.Equal(1,g.Player.Field.Single(m=>m.Card.Id=="WCG-080").ShieldCounters);
    }
    [Fact] public void DivinerSwapsAndKeepsTheTappedState()
    {
        var g=Game();var used=Energy(g.Player,24,tapped:true);var big=Hand(g,306);Summon(g,308);
        Assert.Equal(GameEngine.SwapHandTitle,g.CurrentPendingChoice!.Title);Pick(g,big.InstanceId.ToString());
        Assert.Equal(GameEngine.SwapEnergyTitle,g.CurrentPendingChoice!.Title);Pick(g,used.InstanceId.ToString());
        Assert.Contains(used,g.Player.Hand);Assert.Contains(big,g.Player.EnergyZone);Assert.True(big.IsTapped);
    }
    [Fact] public void AmbushOrderReadiesEnergySummonsThisTurnOnly()
    {
        var g=Game();var c=Energy(g.Player,40);Assert.True(g.CastSpell(g.Player,Hand(g,309)));Settle(g);
        Assert.True(g.CastSpell(g.Player,Hand(g,307)));Pick(g,c.InstanceId.ToString());Settle(g);
        var m=g.Player.Field.First(x=>x.Card.Id=="WCG-040");Assert.False(m.IsTapped);Assert.True(g.CanAttack(m));Assert.Equal(2700,m.CurrentPP);
    }
    [Fact] public void OpponentNeverSeesFaceDownEnergyInSimulations()
    {
        var g=Game();var secret=Energy(g.Computer,306);
        var sim=(GameEngine)typeof(GameEngine).GetMethod("CloneQuiescent",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,[1,"player"])!;
        Assert.DoesNotContain(sim.Computer.EnergyZone,x=>x.Card.Id=="WCG-306");Assert.Contains(sim.Player.EnergyZone,x=>x.Card.Id=="WCG-101");
        var before=CardBalanceHistory.BeforeEnergyVaultCards(cards);var old=new GameEngine(before,new Random(3));old.StartGame(before.PresetDecks[0],before.PresetDecks[1],true);
        var oldSim=(GameEngine)typeof(GameEngine).GetMethod("CloneQuiescent",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(old,[1,"player"])!;
        Assert.Equal(old.Computer.EnergyZone.Select(c=>c.Card.Id),oldSim.Computer.EnergyZone.Select(c=>c.Card.Id)); // older eras keep their simulations
    }
    [Fact] public void MasterAiHidesBigMonstersAndSummonsThem()
    {
        var g=Game();g.AiLevel=5;g.Player.IsAi=true;var big=Hand(g,40);Summon(g,301);Assert.True(g.ExecuteAiStep());Assert.Contains(big,g.Player.EnergyZone);
        var g2=Game();g2.AiLevel=5;g2.Player.IsAi=true;Energy(g2.Player,306);Energy(g2.Player,29);Assert.True(g2.CastSpell(g2.Player,Hand(g2,307)));
        for(int i=0;i<6&&g2.IsWaiting;i++)Assert.True(g2.ExecuteAiStep());Assert.Contains(g2.Player.Field,m=>m.Card.Id=="WCG-306");
    }
}
