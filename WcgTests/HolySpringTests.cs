using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;
namespace WcgTests;

// 新流派「聖泉滿溢」（WCG-400–408，2026-10-25）：每回復 1 點生命在聖泉之井放 1 個聖泉指示物（超過上限也算）。
public class HolySpringTests
{
    private readonly CardDatabase cards;
    public HolySpringTests(){var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));cards=new(env.Object);}
    private GameEngine Game(int energy=12){var g=new GameEngine(cards,new Random(17));g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1],true);foreach(var p in new[]{g.Player,g.Computer}){p.Hand.Clear();p.Field.Clear();p.Structures.Clear();p.EnergyZone.Clear();p.Graveyard.Clear();for(int i=0;i<energy;i++)p.EnergyZone.Add(new(cards.GetCard("WCG-101")!));}return g;}
    private CardInstance Hand(GameEngine g,int n){var c=new CardInstance(cards.GetCard($"WCG-{n:000}")!);g.Player.Hand.Add(c);return c;}
    private MonsterInstance Unit(PlayerState p,int n,int slot){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=false,HasSummoningSickness=false};p.Field.Add(m);return m;}
    private MonsterInstance Structure(PlayerState p,int n,int slot){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=false,HasSummoningSickness=false};p.Structures.Add(m);return m;}
    private static void Refresh(GameEngine g)=>typeof(GameEngine).GetMethod("RefreshBoard",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);
    private static void Settle(GameEngine g){for(int i=0;i<80&&g.IsWaiting;i++){if(g.CurrentPendingChoice is {} c)Assert.True(g.SelectChoice(c.Options.FirstOrDefault(o=>o.Id.StartsWith("SLOT:"))??c.Options.FirstOrDefault(o=>o.Id!="SKIP")??c.Options[0]));else{var t=g.CurrentPendingTarget!;var m=g.Player.Board.Concat(g.Computer.Board).First(x=>t.Validator!(x));Assert.True(g.SelectTarget(m));}}}
    private static void Drain(GameEngine g)=>typeof(GameEngine).GetMethod("Drain",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);
    private static void Heal(GameEngine g,PlayerState p,int n)=>typeof(GameEngine).GetMethod("Heal",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,[p,n]);

    [Fact] public void OldEraHasNoSpringCardsAndEverythingElseIsUnchanged()
    {
        var old=CardBalanceHistory.BeforeHolySpringCards(cards);
        for(int n=400;n<=408;n++){Assert.NotNull(cards.GetCard($"WCG-{n}"));Assert.Null(old.GetCard($"WCG-{n}"));}
        Assert.Equal(JsonSerializer.Serialize(CounterHistory.BeforeHolySpringCards(cards).AllCards),JsonSerializer.Serialize(old.AllCards));
        Assert.Equal(cards.AllCards.Where(c=>!CardBalanceHistory.IsHolySpringCard(c.Id)&&!CardBalanceHistory.IsStarCountdownCard(c.Id)).Select(c=>JsonSerializer.Serialize(c)),old.AllCards.Select(c=>JsonSerializer.Serialize(c)));
    }
    [Fact] public void WellStoresEveryPointHealedIncludingOverflow()
    {
        var g=Game();var well=Structure(g.Player,403,4);g.Player.Hp=6;
        Heal(g,g.Player,3);Assert.Equal(7,g.Player.Hp);Assert.Equal(3,well.SpringCounters);
        Assert.True(g.CanActivate(g.Player,well));var hand=g.Player.Hand.Count;Assert.True(g.Activate(g.Player,well));Settle(g);
        Assert.Equal(1,well.SpringCounters);Assert.Equal(hand+1,g.Player.Hand.Count);Assert.True(well.IsTapped);
    }
    [Fact] public void DelugeHealsFirstThenDealsOneDamagePerTwoCounters()
    {
        var g=Game();var well=Structure(g.Player,403,4);well.SpringCounters=7;var hp=g.Computer.Hp;
        Assert.True(g.CastSpell(g.Player,Hand(g,408)));Settle(g);
        Assert.Equal(hp-4,g.Computer.Hp);Assert.Equal(0,well.SpringCounters);
    }
    [Fact] public void PrayerDrawsOnlyAtFullLife()
    {
        var g=Game();g.Player.Hp=7;var hand=g.Player.Hand.Count;Assert.True(g.CastSpell(g.Player,Hand(g,402)));Settle(g);Assert.Equal(hand+1,g.Player.Hand.Count);
        var g2=Game();g2.Player.Hp=4;var h2=g2.Player.Hand.Count;Assert.True(g2.CastSpell(g2.Player,Hand(g2,402)));Settle(g2);Assert.Equal(h2,g2.Player.Hand.Count);Assert.Equal(6,g2.Player.Hp);
    }
    [Fact] public void BaptismShrinksByHealedThisTurnAndDestroysAtZero()
    {
        var g=Game();var big=Unit(g.Computer,113,0);Refresh(g);g.Player.Hp=3;
        Heal(g,g.Player,2); // 2 healed already; the spell heals 2 more → -1200 PP
        Assert.True(g.CastSpell(g.Player,Hand(g,406)));Settle(g);Assert.Equal(600,big.CurrentPP);Assert.Contains(big,g.Computer.Field);
        var g2=Game();var small=Unit(g2.Computer,101,0);Refresh(g2);Heal(g2,g2.Player,2);
        Assert.True(g2.CastSpell(g2.Player,Hand(g2,406)));Settle(g2);Assert.DoesNotContain(small,g2.Computer.Field);
    }
    [Fact] public void KnightGainsAtMostTwoDpCountersPerTurn()
    {
        var g=Game();var k=Unit(g.Player,405,0);Refresh(g);
        for(int i=0;i<4;i++)Heal(g,g.Player,1);Assert.Equal(2,k.DpCounters);
    }
    [Fact] public void AcolyteBuffsWhatItsArrowsPointAtOnHeal()
    {
        var g=Game();Unit(g.Player,401,1);var a=Unit(g.Player,101,0);var b=Unit(g.Player,101,2);Refresh(g);var pa=a.CurrentPP;
        Heal(g,g.Player,1);Refresh(g);Assert.Equal(pa+300,a.CurrentPP);Assert.Equal(pa+300,b.CurrentPP);
    }
    [Fact] public void ColossusHitsFaceAtFullLifeOnTurnStart()
    {
        var g=Game();Unit(g.Player,407,0);g.Player.Hp=7;var hp=g.Computer.Hp;
        typeof(GameEngine).GetMethod("SpringStartTurn",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,[g.Player]);Drain(g);Settle(g);
        Assert.Equal(hp-1,g.Computer.Hp);
    }
    [Fact] public void HealerHealsAtEndOfTurn()
    {
        var g=Game();Unit(g.Player,404,0);g.Player.Hp=4;Assert.True(g.EndTurn());Settle(g);Assert.Equal(5,g.Player.Hp);
    }
    [Fact] public void PrintToolsAddASpringSheetForSpringDecks()
    {
        var plan=new DeckPrintPlan("t","秩序",[new PrintedCard("WCG-403","聖泉之井","結界","秩序",2,null,null,"")]);
        Assert.Equal(0,DeckPrintTools.Defaults(plan with {Cards=[]})[PrintTool.SpringTokens]);Assert.Equal(1,DeckPrintTools.Defaults(plan)[PrintTool.SpringTokens]);
    }
}
