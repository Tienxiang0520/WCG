using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;
namespace WcgTests;

// 新流派「血誓反擊」（WCG-364–372，2026-10-24）：每受一次傷害就疊血誓指示物。
public class BloodOathTests
{
    private readonly CardDatabase cards;
    public BloodOathTests(){var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));cards=new(env.Object);}
    private GameEngine Game(int energy=12){var g=new GameEngine(cards,new Random(17));g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1],true);foreach(var p in new[]{g.Player,g.Computer}){p.Hand.Clear();p.Field.Clear();p.Structures.Clear();p.EnergyZone.Clear();p.Graveyard.Clear();for(int i=0;i<energy;i++)p.EnergyZone.Add(new(cards.GetCard("WCG-101")!));}return g;}
    private CardInstance Hand(GameEngine g,int n){var c=new CardInstance(cards.GetCard($"WCG-{n:000}")!);g.Player.Hand.Add(c);return c;}
    private void Grave(PlayerState p,int n,int id=101){for(int i=0;i<n;i++)p.Graveyard.Add(new(cards.GetCard($"WCG-{id:000}")!));}
    private MonsterInstance Unit(PlayerState p,int n,int slot,bool tapped=false){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=tapped,HasSummoningSickness=false};p.Field.Add(m);return m;}
    private MonsterInstance Structure(PlayerState p,int n,int slot){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=false,HasSummoningSickness=false};p.Structures.Add(m);return m;}
    private static void Refresh(GameEngine g)=>typeof(GameEngine).GetMethod("RefreshBoard",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);
    private static void Settle(GameEngine g){for(int i=0;i<80&&g.IsWaiting;i++){if(g.CurrentPendingChoice is {} c)Assert.True(g.SelectChoice(c.Options.FirstOrDefault(o=>o.Id.StartsWith("SLOT:"))??c.Options.FirstOrDefault(o=>o.Id!="SKIP")??c.Options[0]));else{var t=g.CurrentPendingTarget!;var m=g.Player.Board.Concat(g.Computer.Board).First(x=>t.Validator!(x));Assert.True(g.SelectTarget(m));}}}
    private static void Hurt(GameEngine g,PlayerState p,int n)=>typeof(GameEngine).GetMethod("Damage",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,[p,n,null]);
    private static void Drain(GameEngine g)=>typeof(GameEngine).GetMethod("Drain",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);

    [Fact] public void OldEraHasNoBloodCardsAndEverythingElseIsUnchanged()
    {
        var old=CardBalanceHistory.BeforeBloodOathCards(cards);
        for(int n=364;n<=372;n++){Assert.NotNull(cards.GetCard($"WCG-{n}"));Assert.Null(old.GetCard($"WCG-{n}"));}
        Assert.Equal(JsonSerializer.Serialize(CounterHistory.BeforeBloodOathCards(cards).AllCards),JsonSerializer.Serialize(old.AllCards));
        Assert.Equal(CardBalanceHistory.BeforeRevivalLordText(cards).AllCards.Where(c=>!CardBalanceHistory.IsBloodOathCard(c.Id)).Select(c=>JsonSerializer.Serialize(c)),old.AllCards.Select(c=>JsonSerializer.Serialize(c)));
        Assert.Equal(["狂怒","秩序"],cards.GetCard("WCG-368")!.Wills.Order(StringComparer.Ordinal));
    }
    [Fact] public void RevivalLordTextEraOnlyChangesItsWording()
    {
        var old=CardBalanceHistory.BeforeRevivalLordText(cards);
        Assert.Contains("那隻怪本回合可以攻擊",cards.GetCard("WCG-194")!.Text);Assert.Contains("它本回合可以攻擊",old.GetCard("WCG-194")!.Text);
        Assert.Equal(1,CardBalanceHistory.BeforeTideCycleCards(cards).AllCards.Count(c=>JsonSerializer.Serialize(c)!=JsonSerializer.Serialize(old.GetCard(c.Id))));
        Assert.Equal(JsonSerializer.Serialize(CounterHistory.BeforeRevivalLordText(cards).AllCards),JsonSerializer.Serialize(old.AllCards));
    }
    [Fact] public void EveryDamageStacksTheAltarAndTheOathguardAndSilenceLeavesTheAltarAlone()
    {
        var g=Game();var altar=Structure(g.Player,364,4);var guard=Unit(g.Player,365,0);Refresh(g);
        Hurt(g,g.Player,1);Settle(g);Hurt(g,g.Player,2);Settle(g);
        Assert.Equal(2,altar.BloodCounters);Assert.Equal(2,guard.DpCounters);
    }
    [Fact] public void ZealotHitsItselfAndThatCountsAsDamage()
    {
        var g=Game();var altar=Structure(g.Player,364,4);Refresh(g);var hp=g.Player.Hp;var hand=g.Player.Hand.Count;
        Assert.True(g.SummonMonster(g.Player,Hand(g,369)));Settle(g);
        Assert.Equal(hp-1,g.Player.Hp);Assert.Equal(1,altar.BloodCounters);Assert.Equal(hand+1,g.Player.Hand.Count);
    }
    [Fact] public void WrathGrowsAtThreeLifeOrLess()
    {
        var g=Game();var w=Unit(g.Player,367,0);g.Player.Hp=4;Refresh(g);Assert.Equal(1500,w.CurrentPP);Assert.Equal(1,w.CurrentDP);
        g.Player.Hp=3;Refresh(g);Assert.Equal(2100,w.CurrentPP);Assert.Equal(2,w.CurrentDP);
    }
    [Fact] public void StandardBearerAddsTwoHundredPerLifeBehind()
    {
        var g=Game();Unit(g.Player,371,1);var ally=Unit(g.Player,101,2);g.Player.Hp=3;g.Computer.Hp=6;Refresh(g);
        Assert.Equal((ally.Card.PP??0)+200+600,ally.CurrentPP);
    }
    [Fact] public void ThousandfoldReturnTurnsEveryCounterIntoDamage()
    {
        var g=Game();var a=Structure(g.Player,364,3);var b=Structure(g.Player,364,4);a.BloodCounters=2;b.BloodCounters=1;g.Computer.Hp=7;Refresh(g);
        Assert.True(g.CastSpell(g.Player,Hand(g,372)));Settle(g);
        Assert.Equal(4,g.Computer.Hp);Assert.Equal(0,a.BloodCounters+b.BloodCounters);
    }
    [Fact] public void SettlingKillsOneEnemyPerTwoCounters()
    {
        var g=Game();var a=Structure(g.Player,364,4);a.BloodCounters=5;var x=Unit(g.Computer,113,0);var y=Unit(g.Computer,113,1);var z=Unit(g.Computer,113,2);Refresh(g);
        Assert.True(g.CastSpell(g.Player,Hand(g,370)));Settle(g);
        Assert.Equal(1,g.Computer.Field.Count);Assert.Equal(1,a.BloodCounters);
        var g2=Game();var a2=Structure(g2.Player,364,4);a2.BloodCounters=1;Unit(g2.Computer,113,0);Refresh(g2);Assert.False(g2.CastSpell(g2.Player,Hand(g2,370)));
    }
    [Fact] public void BloodForBloodAndShieldOfJudgmentAnswerDamage()
    {
        var g=Game();Hand(g,366);g.Player.Hp=3;var hp=g.Computer.Hp;Hurt(g,g.Player,1);Drain(g);
        Assert.NotNull(g.CurrentPendingChoice);Assert.True(g.SelectChoice(g.CurrentPendingChoice!.Options.First(o=>o.Id!="SKIP")));Settle(g);
        Assert.Equal(hp-2,g.Computer.Hp);
        var g2=Game();var altar=Structure(g2.Player,364,4);Hand(g2,368);var foe=Unit(g2.Computer,113,0);Refresh(g2);
        var src=Activator.CreateInstance(typeof(GameEngine).GetNestedType("DamageSource",BindingFlags.NonPublic)!,[g2.Computer,foe.Card,foe,"attack"]);
        typeof(GameEngine).GetMethod("Damage",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g2,[g2.Player,1,src]);Drain(g2);
        Assert.Contains(g2.CurrentPendingChoice!.Options,o=>o.Title=="審判之盾");
        Assert.True(g2.SelectChoice(g2.CurrentPendingChoice!.Options.First(o=>o.Title=="審判之盾")));Settle(g2);
        Assert.DoesNotContain(foe,g2.Computer.Field);Assert.Equal(2,altar.BloodCounters);
    }
    [Fact] public void PrintToolsAddABloodSheetForAltarDecks()
    {
        var plan=new DeckPrintPlan("t","秩序",[new PrintedCard("WCG-364","血誓祭壇","結界","秩序",2,null,null,"")]);
        Assert.Equal(0,DeckPrintTools.Defaults(plan with {Cards=[]})[PrintTool.BloodTokens]);Assert.Equal(1,DeckPrintTools.Defaults(plan)[PrintTool.BloodTokens]);
        Assert.Contains(PrintTool.BloodTokens,DeckPrintTools.Order);
    }
}
