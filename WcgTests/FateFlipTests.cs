using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;
namespace WcgTests;

// 新流派「命運翻牌」（WCG-418–426，2026-10-25）：翻開牌庫頂、依類型結算、放到牌庫底；牌庫空時略過、不敗北。
public class FateFlipTests
{
    private readonly CardDatabase cards;
    public FateFlipTests(){var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));cards=new(env.Object);}
    private GameEngine Game(int energy=12){var g=new GameEngine(cards,new Random(17));g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1],true);foreach(var p in new[]{g.Player,g.Computer}){p.Hand.Clear();p.Field.Clear();p.Structures.Clear();p.EnergyZone.Clear();p.Graveyard.Clear();for(int i=0;i<energy;i++)p.EnergyZone.Add(new(cards.GetCard("WCG-101")!));}return g;}
    private CardInstance Hand(GameEngine g,int n){var c=new CardInstance(cards.GetCard($"WCG-{n:000}")!);g.Player.Hand.Add(c);return c;}
    private MonsterInstance Unit(PlayerState p,int n,int slot){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=false,HasSummoningSickness=false};p.Field.Add(m);return m;}
    private MonsterInstance Structure(PlayerState p,int n,int slot){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=false,HasSummoningSickness=false};p.Structures.Add(m);return m;}
    private static void Refresh(GameEngine g)=>typeof(GameEngine).GetMethod("RefreshBoard",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);
    private static void Settle(GameEngine g){for(int i=0;i<80&&g.IsWaiting;i++){if(g.CurrentPendingChoice is {} c)Assert.True(g.SelectChoice(c.Options.FirstOrDefault(o=>o.Id.StartsWith("SLOT:"))??c.Options.FirstOrDefault(o=>o.Id!="SKIP")??c.Options[0]));else{var t=g.CurrentPendingTarget!;var m=g.Player.Board.Concat(g.Computer.Board).First(x=>t.Validator!(x));Assert.True(g.SelectTarget(m));}}}
    private static void Drain(GameEngine g)=>typeof(GameEngine).GetMethod("Drain",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);
    private static void Heal(GameEngine g,PlayerState p,int n)=>typeof(GameEngine).GetMethod("Heal",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,[p,n]);

    private CardInstance Top(GameEngine g,int n){var c=new CardInstance(cards.GetCard($"WCG-{n:000}")!);g.Player.Deck.Insert(0,c);return c;}

    [Fact] public void OldEraHasNoFateCardsAndEverythingElseIsUnchanged()
    {
        var old=CardBalanceHistory.BeforeFateFlipCards(cards);
        for(int n=418;n<=426;n++){Assert.NotNull(cards.GetCard($"WCG-{n}"));Assert.Null(old.GetCard($"WCG-{n}"));}
        Assert.Equal(JsonSerializer.Serialize(CounterHistory.BeforeFateFlipCards(cards).AllCards),JsonSerializer.Serialize(old.AllCards));
        Assert.Equal(cards.AllCards.Where(c=>!CardBalanceHistory.IsFateFlipCard(c.Id)).Select(c=>JsonSerializer.Serialize(c)),old.AllCards.Select(c=>JsonSerializer.Serialize(c)));
    }
    [Fact] public void RevealPutsTheCardOnTheBottomAndDrawsTheNext()
    {
        var g=Game();var top=Top(g,101);var deck=g.Player.Deck.Count;var hand=g.Player.Hand.Count;
        Assert.True(g.SummonMonster(g.Player,Hand(g,418)));Settle(g);
        Assert.Same(top,g.Player.Deck[^1]);Assert.Equal(deck-1,g.Player.Deck.Count);Assert.Equal(hand+1,g.Player.Hand.Count);Assert.DoesNotContain(top,g.Player.Hand);
        var g2=Game();var spell=Top(g2,24);var h2=g2.Player.Hand.Count;Assert.True(g2.SummonMonster(g2.Player,Hand(g2,418)));Settle(g2);Assert.Equal(h2,g2.Player.Hand.Count);Assert.Same(spell,g2.Player.Deck[^1]);
    }
    [Fact] public void EmptyDeckSkipsWithoutLosing()
    {
        var g=Game();g.Player.Deck.Clear();Assert.True(g.SummonMonster(g.Player,Hand(g,418)));Settle(g);Assert.False(g.IsOver);Assert.Single(g.Player.Field);
    }
    [Fact] public void SummonerSummonsTheRevealedMonsterWithoutDeploy()
    {
        var g=Game();var top=Top(g,418);var deck=g.Player.Deck.Count;Assert.True(g.SummonMonster(g.Player,Hand(g,424)));Settle(g);
        Assert.Contains(g.Player.Field,x=>x.InstanceId==top.InstanceId);Assert.Equal(deck-1,g.Player.Deck.Count); // 418's deploy did not reveal again
        var g2=Game();Top(g2,24);var h2=g2.Player.Hand.Count;Assert.True(g2.SummonMonster(g2.Player,Hand(g2,424)));Settle(g2);Assert.Equal(h2+1,g2.Player.Hand.Count);
    }
    [Fact] public void WagerShrinksByPrintedCostAndDestroysAtZero()
    {
        var g=Game();var foe=Unit(g.Computer,101,0);Refresh(g);Top(g,124);var cost=cards.GetCard("WCG-124")!.TotalCost;
        Assert.True(g.CastSpell(g.Player,Hand(g,420)));Settle(g);
        if(cost*300>=1000)Assert.DoesNotContain(foe,g.Computer.Field);else Assert.Equal(1000-cost*300,foe.CurrentPP);
    }
    [Fact] public void RouletteRevealsFourAndResolvesEveryOne()
    {
        var g=Game();var hp=g.Computer.Hp;var hand=g.Player.Hand.Count;
        var m=Top(g,101);var s2=Top(g,24);var s1=Top(g,24);var ench=Top(g,124); // order from top: 124, 24, 24, 101
        var deck=g.Player.Deck.Count;
        Assert.True(g.CastSpell(g.Player,Hand(g,426)));Settle(g);
        Assert.Equal(hp-2,g.Computer.Hp);Assert.Contains(g.Player.Field,x=>x.InstanceId==m.InstanceId);
        Assert.Equal(hand+1,g.Player.Hand.Count); // the structure drew 1
        Assert.Equal(deck-2,g.Player.Deck.Count); // 1 summoned, 1 drawn
    }
    [Fact] public void DivinerAltarAndScavengerReactToReveals()
    {
        var g=Game();var d=Unit(g.Player,419,0);var sc=Unit(g.Player,423,1);var altar=Structure(g.Player,425,4);Refresh(g);var hp=g.Computer.Hp;
        Top(g,161); // cost 9 spell? no: a monster with printed cost 9
        typeof(GameEngine).GetMethod("FateStartTurn",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,[g.Player]);Drain(g);Settle(g);
        Assert.Equal(hp-1,g.Computer.Hp);Refresh(g);Assert.Equal(d.Card.PP+500,d.CurrentPP);Assert.Equal(sc.Card.PP+300,sc.CurrentPP);
        Top(g,24);typeof(GameEngine).GetMethod("FateStartTurn",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,[g.Player]);Drain(g);Settle(g);
        Assert.Equal(hp-2,g.Computer.Hp);Assert.Equal(1,sc.DpCounters);
    }
    [Fact] public void WellOfOpenEyesTakesTheCardForOneLife()
    {
        var g=Game();var well=Structure(g.Player,421,4);var top=Top(g,101);var hp=g.Player.Hp;
        Assert.True(g.Activate(g.Player,well));Settle(g);Assert.Contains(top,g.Player.Hand);Assert.Equal(hp-1,g.Player.Hp);Assert.DoesNotContain(top,g.Player.Deck);
    }
    [Fact] public void GolemDestroysWhatItsArrowPointsAtOnABigReveal()
    {
        var g=Game();var foe=Unit(g.Computer,113,4);Refresh(g);Top(g,161); // slot 0 ↑ points at enemy slot 4
        var golem=Hand(g,422);Assert.True(g.SummonMonster(g.Player,golem));Settle(g);
        var m=g.Player.Field.Single();Assert.Equal(m.Slot==0,!g.Computer.Field.Contains(foe));
    }
}
