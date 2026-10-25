using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;
namespace WcgTests;

// 新流派「星律倒數」（WCG-409–417，2026-10-25）：回合開始時移除 1 個倒數指示物，移除最後 1 個時觸發；沉默移除且不觸發。
public class StarCountdownTests
{
    private readonly CardDatabase cards;
    public StarCountdownTests(){var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));cards=new(env.Object);}
    private GameEngine Game(int energy=12){var g=new GameEngine(cards,new Random(17));g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1],true);foreach(var p in new[]{g.Player,g.Computer}){p.Hand.Clear();p.Field.Clear();p.Structures.Clear();p.EnergyZone.Clear();p.Graveyard.Clear();for(int i=0;i<energy;i++)p.EnergyZone.Add(new(cards.GetCard("WCG-101")!));}return g;}
    private CardInstance Hand(GameEngine g,int n){var c=new CardInstance(cards.GetCard($"WCG-{n:000}")!);g.Player.Hand.Add(c);return c;}
    private MonsterInstance Unit(PlayerState p,int n,int slot){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=false,HasSummoningSickness=false};p.Field.Add(m);return m;}
    private MonsterInstance Structure(PlayerState p,int n,int slot){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=false,HasSummoningSickness=false};p.Structures.Add(m);return m;}
    private static void Refresh(GameEngine g)=>typeof(GameEngine).GetMethod("RefreshBoard",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);
    private static void Settle(GameEngine g){for(int i=0;i<80&&g.IsWaiting;i++){if(g.CurrentPendingChoice is {} c)Assert.True(g.SelectChoice(c.Options.FirstOrDefault(o=>o.Id.StartsWith("SLOT:"))??c.Options.FirstOrDefault(o=>o.Id!="SKIP")??c.Options[0]));else{var t=g.CurrentPendingTarget!;var m=g.Player.Board.Concat(g.Computer.Board).First(x=>t.Validator!(x));Assert.True(g.SelectTarget(m));}}}
    private static void Drain(GameEngine g)=>typeof(GameEngine).GetMethod("Drain",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);
    private static void Heal(GameEngine g,PlayerState p,int n)=>typeof(GameEngine).GetMethod("Heal",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,[p,n]);

    private List<MonsterInstance> Targets(GameEngine g)=>(List<MonsterInstance>)typeof(GameEngine).GetMethod("SpellTargets",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,[g.Player,cards.GetCard("WCG-024")!])!;
    private static void StartTurn(GameEngine g,PlayerState p){typeof(GameEngine).GetMethod("StarStartTurn",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,[p]);Drain(g);Settle(g);}

    [Fact] public void OldEraHasNoStarCardsAndEverythingElseIsUnchanged()
    {
        var old=CardBalanceHistory.BeforeStarCountdownCards(cards);
        for(int n=409;n<=417;n++){Assert.NotNull(cards.GetCard($"WCG-{n}"));Assert.Null(old.GetCard($"WCG-{n}"));}
        Assert.Equal(JsonSerializer.Serialize(CounterHistory.BeforeStarCountdownCards(cards).AllCards),JsonSerializer.Serialize(old.AllCards));
        Assert.Equal(cards.AllCards.Where(c=>!CardBalanceHistory.IsStarCountdownCard(c.Id)).Select(c=>JsonSerializer.Serialize(c)),old.AllCards.Select(c=>JsonSerializer.Serialize(c)));
    }
    [Fact] public void ApprenticeDrawsTwoWhenItsLastCounterGoes()
    {
        var g=Game();Assert.True(g.SummonMonster(g.Player,Hand(g,409)));Settle(g);var m=g.Player.Field.Single();Assert.Equal(1,m.Countdown);
        var hand=g.Player.Hand.Count;StartTurn(g,g.Player);Assert.Equal(0,m.Countdown);Assert.Equal(hand+2,g.Player.Hand.Count);
        StartTurn(g,g.Player);Assert.Equal(hand+2,g.Player.Hand.Count); // only once
    }
    [Fact] public void SilenceRemovesCountersWithoutTriggering()
    {
        var g=Game();var m=Unit(g.Player,414,0);m.Countdown=1;var foe=Unit(g.Computer,101,0);Refresh(g);var hp=g.Computer.Hp;
        m.IsSilenced=true;typeof(GameEngine).GetMethod("SilenceStrip",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,[m]);
        Assert.Equal(0,m.Countdown);StartTurn(g,g.Player);Assert.Equal(hp,g.Computer.Hp);Assert.Contains(foe,g.Computer.Field);
    }
    [Fact] public void ClockEntersWithThreeAndLoops()
    {
        var g=Game();Assert.True(g.PlayEnchantment(g.Player,Hand(g,410)));Settle(g);var clock=g.Player.Structures.Single();Assert.Equal(3,clock.Countdown);
        var foe=Unit(g.Computer,113,0);Refresh(g);
        StartTurn(g,g.Player);StartTurn(g,g.Player);Assert.Contains(foe,g.Computer.Field);StartTurn(g,g.Player);
        Assert.DoesNotContain(foe,g.Computer.Field);Assert.Equal(3,clock.Countdown);
    }
    [Fact] public void SeerHitsFaceAndClearsPrintedSmallMonsters()
    {
        var g=Game();var seer=Unit(g.Player,414,0);seer.Countdown=1;var small=Unit(g.Computer,101,0);var big=Unit(g.Computer,113,1);Refresh(g);var hp=g.Computer.Hp;
        StartTurn(g,g.Player);Assert.Equal(hp-2,g.Computer.Hp);Assert.Equal(small.Card.PP<=900,!g.Computer.Field.Contains(small));Assert.Contains(big,g.Computer.Field);
    }
    [Fact] public void WardenIsSpellProofWhileTickingThenHardens()
    {
        var g=Game();var w=Unit(g.Computer,413,0);w.Countdown=1;Refresh(g);
        Assert.DoesNotContain(w,Targets(g));
        StartTurn(g,g.Computer);Assert.Equal(2,w.DpCounters);Assert.Equal(1,w.ShieldCounters);
        Assert.Contains(w,Targets(g));
    }
    [Fact] public void AdeptRemovesAnExtraCounterFromWhatItsArrowsPointAt()
    {
        var g=Game();Unit(g.Player,411,1);var a=Unit(g.Player,414,0);a.Countdown=2;var b=Unit(g.Player,413,3);b.Countdown=2;Refresh(g);
        StartTurn(g,g.Player);Assert.Equal(0,a.Countdown);Assert.Equal(1,b.Countdown);
    }
    [Fact] public void CatalystAcceleratesOrDraws()
    {
        var g=Game();var a=Unit(g.Player,409,0);a.Countdown=2;Refresh(g);var hand=g.Player.Hand.Count;
        Assert.True(g.CastSpell(g.Player,Hand(g,412)));Settle(g);Assert.Equal(0,a.Countdown);Assert.Equal(hand+2,g.Player.Hand.Count);
        var g2=Game();var h2=g2.Player.Hand.Count;Assert.True(g2.CastSpell(g2.Player,Hand(g2,412)));Settle(g2);Assert.Equal(h2+1,g2.Player.Hand.Count);
    }
    [Fact] public void RealignRearmsASpentStarCardOrDraws()
    {
        var g=Game();var a=Unit(g.Player,414,0);Refresh(g);
        Assert.True(g.CastSpell(g.Player,Hand(g,415)));Settle(g);Assert.Equal(2,a.Countdown);
        var g2=Game();var h2=g2.Player.Hand.Count;Assert.True(g2.CastSpell(g2.Player,Hand(g2,415)));Settle(g2);Assert.Equal(h2+1,g2.Player.Hand.Count);
        Assert.All(g2.Computer.Board,x=>Assert.Equal(0,x.Countdown));
    }
    [Fact] public void EndFiresEverythingAndHitsFacePerTrigger()
    {
        var g=Game();var a=Unit(g.Player,409,0);a.Countdown=1;var b=Unit(g.Player,413,1);b.Countdown=2;Structure(g.Player,416,4);Refresh(g);var hp=g.Computer.Hp;var hand=g.Player.Hand.Count;
        Assert.True(g.CastSpell(g.Player,Hand(g,417)));Settle(g);
        Assert.Equal(hp-2,g.Computer.Hp);Assert.Equal(0,a.Countdown);Assert.Equal(0,b.Countdown);Assert.Equal(2,b.DpCounters);
        Assert.Equal(hand+2+2,g.Player.Hand.Count); // 409 draws 2, the tower draws 1 per trigger
        var g2=Game();var h2=g2.Player.Hand.Count;Assert.True(g2.CastSpell(g2.Player,Hand(g2,417)));Settle(g2);Assert.Equal(h2+3,g2.Player.Hand.Count);
    }
    [Fact] public void PrintToolsAddACountdownSheetForStarDecks()
    {
        var plan=new DeckPrintPlan("t","理智",[new PrintedCard("WCG-409","星沙學徒","怪物","理智",1,500,0,"")]);
        Assert.Equal(0,DeckPrintTools.Defaults(plan with {Cards=[]})[PrintTool.CountdownTokens]);Assert.Equal(1,DeckPrintTools.Defaults(plan)[PrintTool.CountdownTokens]);
    }
}
