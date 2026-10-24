using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;
namespace WcgTests;

// 新流派「墓誓軍團」（WCG-346–354，2026-10-24）：墓地越厚越強，最後整排從墓地站起來。
public class GraveOathTests
{
    private readonly CardDatabase cards;
    public GraveOathTests(){var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));cards=new(env.Object);}
    private GameEngine Game(int energy=12){var g=new GameEngine(cards,new Random(17));g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1],true);foreach(var p in new[]{g.Player,g.Computer}){p.Hand.Clear();p.Field.Clear();p.Structures.Clear();p.EnergyZone.Clear();p.Graveyard.Clear();for(int i=0;i<energy;i++)p.EnergyZone.Add(new(cards.GetCard("WCG-101")!));}return g;}
    private CardInstance Hand(GameEngine g,int n){var c=new CardInstance(cards.GetCard($"WCG-{n:000}")!);g.Player.Hand.Add(c);return c;}
    private void Grave(PlayerState p,int n,int id=101){for(int i=0;i<n;i++)p.Graveyard.Add(new(cards.GetCard($"WCG-{id:000}")!));}
    private MonsterInstance Unit(PlayerState p,int n,int slot,bool tapped=false){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=tapped,HasSummoningSickness=false};p.Field.Add(m);return m;}
    private MonsterInstance Structure(PlayerState p,int n,int slot){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=false,HasSummoningSickness=false};p.Structures.Add(m);return m;}
    private static void Refresh(GameEngine g)=>typeof(GameEngine).GetMethod("RefreshBoard",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);
    private static void Settle(GameEngine g){for(int i=0;i<80&&g.IsWaiting;i++){if(g.CurrentPendingChoice is {} c)Assert.True(g.SelectChoice(c.Options.FirstOrDefault(o=>o.Id.StartsWith("SLOT:"))??c.Options.FirstOrDefault(o=>o.Id!="SKIP")??c.Options[0]));else{var t=g.CurrentPendingTarget!;var m=g.Player.Board.Concat(g.Computer.Board).First(x=>t.Validator!(x));Assert.True(g.SelectTarget(m));}}}

    [Fact] public void OldEraHasNoGraveCardsAndEverythingElseIsUnchanged()
    {
        var old=CardBalanceHistory.BeforeGraveOathCards(cards);
        for(int n=346;n<=354;n++){Assert.NotNull(cards.GetCard($"WCG-{n}"));Assert.Null(old.GetCard($"WCG-{n}"));}
        Assert.Equal(JsonSerializer.Serialize(CounterHistory.BeforeGraveOathCards(cards).AllCards),JsonSerializer.Serialize(old.AllCards));
        Assert.Equal(cards.AllCards.Where(c=>!CardBalanceHistory.IsGraveOathCard(c.Id)&&int.Parse(c.Id[4..])<355).Select(c=>JsonSerializer.Serialize(c)),old.AllCards.Select(c=>JsonSerializer.Serialize(c)));
        Assert.All(cards.AllCards,c=>{Assert.DoesNotContain("放逐",c.Text);Assert.DoesNotContain("移出遊戲",c.Text);});
    }
    [Fact] public void ScribeMillsThreeAndRankedDeadGrowsWithTheGraveyard()
    {
        var g=Game();var deck=g.Player.Deck.Count;Assert.True(g.SummonMonster(g.Player,Hand(g,346)));Settle(g);
        Assert.Equal(deck-3,g.Player.Deck.Count);Assert.Equal(3,g.Player.Graveyard.Count);
        var dead=Unit(g.Player,347,2);var left=Unit(g.Player,101,1);Grave(g.Player,7);Refresh(g); // 10 cards
        Assert.Equal(900+600,dead.CurrentPP);Assert.Equal(1200,left.CurrentPP);
    }
    [Fact] public void FuneralCommandSacrificesDrawsTwoAndMillsTwo()
    {
        var g=Game();var fodder=Unit(g.Player,101,0);Refresh(g);var deck=g.Player.Deck.Count;var hand=g.Player.Hand.Count;
        Assert.True(g.CastSpell(g.Player,Hand(g,348)));Settle(g);
        Assert.DoesNotContain(fodder,g.Player.Field);Assert.Equal(hand+2,g.Player.Hand.Count);Assert.Equal(deck-4,g.Player.Deck.Count);
    }
    [Fact] public void BoneStandardShufflesThreeToTheBottomAndRaisesASmallMonster()
    {
        var g=Game();var flag=Structure(g.Player,349,4);Grave(g.Player,2);Grave(g.Player,1,113);Grave(g.Player,1,81);Refresh(g); // 113 is printed PP 1800
        var deck=g.Player.Deck.Count;Assert.True(g.CanActivate(g.Player,flag));Assert.True(g.Activate(g.Player,flag));Settle(g);
        Assert.True(flag.IsTapped);Assert.Single(g.Player.Field);Assert.True((g.Player.Field[0].Card.PP??0)<=1200);Assert.Equal(deck+3,g.Player.Deck.Count);Assert.Empty(g.Player.Graveyard);
        var g2=Game();var f2=Structure(g2.Player,349,4);Grave(g2.Player,3);Refresh(g2);Assert.False(g2.CanActivate(g2.Player,f2)); // needs 3 other cards
    }
    [Fact] public void JudgeKillsWithTenAndItsArrowShrinksWithTheGraveyard()
    {
        var g=Game();var foe=Unit(g.Computer,113,2);Grave(g.Player,10);Refresh(g);
        Assert.True(g.SummonMonster(g.Player,Hand(g,350)));Settle(g);Assert.DoesNotContain(foe,g.Computer.Field);
        var g2=Game();var judge=Unit(g2.Player,350,2);var across=Unit(g2.Computer,113,2);Grave(g2.Player,9);Refresh(g2);
        Assert.Equal(1800-200,across.CurrentPP);Assert.Contains(g2.PowerModifiers(across),x=>x.Amount==-200);
    }
    [Fact] public void PhalanxComesBackOncePerTurnWithEightInTheGraveyard()
    {
        var g=Game();var a=Unit(g.Computer,351,1);var b=Unit(g.Computer,351,3);Grave(g.Computer,8);Refresh(g);
        Assert.True(g.CastSpellAt(g.Player,Hand(g,94),a));Settle(g);Assert.Equal(2,g.Computer.Field.Count(x=>x.Card.Id=="WCG-351"));
        Assert.True(g.CastSpellAt(g.Player,Hand(g,94),b));Settle(g);Assert.Single(g.Computer.Field,x=>x.Card.Id=="WCG-351");
        var g2=Game();var c=Unit(g2.Computer,351,1);Grave(g2.Computer,6);Refresh(g2); // 6 + itself = 7: stays dead
        Assert.True(g2.CastSpellAt(g2.Player,Hand(g2,94),c));Settle(g2);Assert.Empty(g2.Computer.Field);
    }
    [Fact] public void RollCallAndColossusCountTheGraveyard()
    {
        var g=Game();Grave(g.Player,13);var hp=g.Computer.Hp;Assert.True(g.CastSpell(g.Player,Hand(g,352)));Settle(g);Assert.Equal(hp-3,g.Computer.Hp);
        var g2=Game();Grave(g2.Player,4);Grave(g2.Player,2,82);Assert.Equal(2,g2.ActualCost(g2.Player,cards.GetCard("WCG-353")!));
    }
    [Fact] public void TenThousandBonesFillsEverySlotAndTheyCanAttack()
    {
        var g=Game();Unit(g.Player,101,0);Grave(g.Player,3,154);Grave(g.Player,2,113);Grave(g.Player,2,119);Refresh(g); // 119 printed PP 2600 stays
        Assert.True(g.CastSpell(g.Player,Hand(g,354)));Settle(g);
        Assert.Equal(5,g.Player.Field.Count);Assert.All(g.Player.Field.Skip(1),m=>Assert.True(g.CanAttack(m)));Assert.Equal(2,g.Player.Graveyard.Count(c=>c.Card.Id=="WCG-119"));
    }
    [Fact] public void MasterAiPlaysTheGraveDeck()
    {
        var ranked=JsonSerializer.Deserialize<List<RankedDecks.Entry>>(File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb/Data/ranked_decks.json"))))!;
        var deck=ranked.Single(e=>e.Deck.Id=="RANKED-5-ORDER-GRAVEOATH").Deck;var foe=ranked.First(e=>e.Tier==5&&e.Deck.Id!=deck.Id).Deck;
        for(int s=0;s<4;s++){var g=new GameEngine(cards,new Random(s));g.SeedRankedAi(s);g.AiLevel=5;g.StartGame(deck,foe,s%2==0);g.Player.IsAi=true;
            for(int i=0;i<4000&&!g.IsOver;i++)Assert.True(g.ExecuteAiStep(),g.LastError);Assert.True(g.IsOver);}
    }
}
