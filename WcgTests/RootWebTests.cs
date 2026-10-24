using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;
namespace WcgTests;

// 新流派「根脈連營」（WCG-355–363，2026-10-24）：用 ← → 箭頭串滿一整排。
public class RootWebTests
{
    private readonly CardDatabase cards;
    public RootWebTests(){var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));cards=new(env.Object);}
    private GameEngine Game(int energy=12){var g=new GameEngine(cards,new Random(17));g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1],true);foreach(var p in new[]{g.Player,g.Computer}){p.Hand.Clear();p.Field.Clear();p.Structures.Clear();p.EnergyZone.Clear();p.Graveyard.Clear();for(int i=0;i<energy;i++)p.EnergyZone.Add(new(cards.GetCard("WCG-101")!));}return g;}
    private CardInstance Hand(GameEngine g,int n){var c=new CardInstance(cards.GetCard($"WCG-{n:000}")!);g.Player.Hand.Add(c);return c;}
    private void Grave(PlayerState p,int n,int id=101){for(int i=0;i<n;i++)p.Graveyard.Add(new(cards.GetCard($"WCG-{id:000}")!));}
    private MonsterInstance Unit(PlayerState p,int n,int slot,bool tapped=false){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=tapped,HasSummoningSickness=false};p.Field.Add(m);return m;}
    private MonsterInstance Structure(PlayerState p,int n,int slot){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=false,HasSummoningSickness=false};p.Structures.Add(m);return m;}
    private static void Refresh(GameEngine g)=>typeof(GameEngine).GetMethod("RefreshBoard",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);
    private static void Settle(GameEngine g){for(int i=0;i<80&&g.IsWaiting;i++){if(g.CurrentPendingChoice is {} c)Assert.True(g.SelectChoice(c.Options.FirstOrDefault(o=>o.Id.StartsWith("SLOT:"))??c.Options.FirstOrDefault(o=>o.Id!="SKIP")??c.Options[0]));else{var t=g.CurrentPendingTarget!;var m=g.Player.Board.Concat(g.Computer.Board).First(x=>t.Validator!(x));Assert.True(g.SelectTarget(m));}}}

    [Fact] public void OldEraHasNoRootCardsAndEverythingElseIsUnchanged()
    {
        var old=CardBalanceHistory.BeforeRootWebCards(cards);
        for(int n=355;n<=363;n++){Assert.NotNull(cards.GetCard($"WCG-{n}"));Assert.Null(old.GetCard($"WCG-{n}"));}
        Assert.Equal(JsonSerializer.Serialize(CounterHistory.BeforeRootWebCards(cards).AllCards),JsonSerializer.Serialize(old.AllCards));
        Assert.Equal(cards.AllCards.Where(c=>!CardBalanceHistory.IsRootWebCard(c.Id)).Select(c=>JsonSerializer.Serialize(c)),old.AllCards.Select(c=>JsonSerializer.Serialize(c)));
    }
    [Fact] public void HeartwoodGrowsWithEveryArrowPointingAtIt()
    {
        var g=Game();var heart=Unit(g.Player,357,2);Refresh(g);var alone=heart.CurrentPP;
        Unit(g.Player,215,1);Unit(g.Player,215,3);Refresh(g); // 215 arrows ← → have no PP aura
        Assert.Equal(alone+800,heart.CurrentPP);
    }
    [Fact] public void SqueezeNeedsAGapBetweenTwoOfYourMonsters()
    {
        var g=Game();var c=Hand(g,362);Assert.False(g.SummonMonster(g.Player,c));
        Unit(g.Player,101,1);Unit(g.Player,101,3);Refresh(g);Assert.True(g.SummonMonster(g.Player,c));Settle(g);
        Assert.Equal(2,g.Player.Field.Single(m=>m.Card.Id=="WCG-362").Slot);
    }
    [Fact] public void FreeSummonedSqueezeStillEntersAnyEmptySlot()
    {
        // League regression: 繁衍蜂群 free-summoned 362 with no gap and the card vanished.
        var g=Game();Hand(g,362);Assert.True(g.SummonMonster(g.Player,Hand(g,47)));Settle(g);
        Assert.Contains(g.Player.Field,m=>m.Card.Id=="WCG-362");
    }
}
