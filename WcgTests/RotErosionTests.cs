using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;
namespace WcgTests;

// 新流派「腐萎侵蝕」（WCG-283–291），2026-10-23：減益只改目前 PP，用來把敵方嘲諷縮小；印刷 PP 門檻不受影響。
public class RotErosionTests
{
    private readonly CardDatabase cards;
    public RotErosionTests(){var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));cards=new(env.Object);}
    private GameEngine Game(){var g=new GameEngine(cards,new Random(17));g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1],true);foreach(var p in new[]{g.Player,g.Computer}){p.Hand.Clear();p.Field.Clear();p.Structures.Clear();p.EnergyZone.Clear();for(int i=0;i<12;i++)p.EnergyZone.Add(new(cards.GetCard("WCG-101")!));}Assert.Equal("player",g.CurrentTurnPlayerId);return g;}
    private CardInstance Hand(GameEngine g,int n){var c=new CardInstance(cards.GetCard($"WCG-{n:000}")!);g.Player.Hand.Add(c);return c;}
    private MonsterInstance Unit(PlayerState p,int n,int slot,bool tapped=false){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=tapped,HasSummoningSickness=false};p.Field.Add(m);p.Field.Sort((a,b)=>a.Slot.CompareTo(b.Slot));return m;}
    private static void Refresh(GameEngine g)=>typeof(GameEngine).GetMethod("RefreshBoard",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);
    private static void Choose(GameEngine g,Func<ChoiceOption,bool> pick)=>Assert.True(g.SelectChoice(g.CurrentPendingChoice!.Options.First(pick)));
    private static void Settle(GameEngine g){while(g.CurrentPendingChoice is {} c)Assert.True(g.SelectChoice(c.Options.Last()));}
    private MonsterInstance Summon(GameEngine g,int n,int slot=0){var c=Hand(g,n);Assert.True(g.SummonMonster(g.Player,c));Choose(g,o=>o.Id==$"SLOT:{slot}");return g.Player.Field.Single(m=>m.InstanceId==c.InstanceId);}
    private void Fill(GameEngine g,int n){g.Player.Hand.Clear();for(int i=0;i<n;i++)Hand(g,101);}

    [Fact] public void OldEraHasNoRotCardsAndEverythingElseIsUnchanged()
    {
        var old=CardBalanceHistory.BeforeRotErosionCards(cards);
        for(int n=283;n<=291;n++){Assert.NotNull(cards.GetCard($"WCG-{n}"));Assert.Null(old.GetCard($"WCG-{n}"));}
        Assert.Equal(CardBalanceHistory.BeforeArenaAnyEnemy(cards).AllCards.Where(c=>!CardBalanceHistory.IsRotErosionCard(c.Id)).Select(c=>JsonSerializer.Serialize(c)),old.AllCards.Select(c=>JsonSerializer.Serialize(c)));
        Assert.Equal(JsonSerializer.Serialize(CounterHistory.BeforeRotErosionCards(cards).AllCards),JsonSerializer.Serialize(old.AllCards));
    }
    [Fact] public void ArrowsShrinkTheEnemyOppositeButNotItsPrintedThreshold()
    {
        var g=Game();Unit(g.Player,283,1);var foe=Unit(g.Computer,155,3);Unit(g.Player,287,2);var foe2=Unit(g.Computer,159,2);Refresh(g);
        Assert.Equal(1300,foe.CurrentPP);Assert.Equal(1800,foe2.CurrentPP);
        Assert.Contains(g.PowerModifiers(foe),x=>x.Source?.Id=="WCG-283"&&x.Amount==-300);
        var snipe=Hand(g,82);Assert.DoesNotContain(foe,g.GetDirectPlayTargets(g.Player,snipe)); // 1300 current, 1600 printed
    }
    [Fact] public void TendrilIsAOneCombatMinusSevenHundred()
    {
        var g=Game();var mine=Unit(g.Player,154,0);var wall=Unit(g.Computer,159,1);Refresh(g);var s=Hand(g,284);
        Assert.DoesNotContain(mine,g.GetDirectPlayTargets(g.Player,s));
        Assert.True(g.CastSpellAt(g.Player,s,wall));Settle(g);Refresh(g);Assert.Equal(1600,wall.CurrentPP);
        var a=Unit(g.Player,155,2);Refresh(g);Assert.True(g.Attack(g.Player,a,wall));Settle(g);Refresh(g); // 1600 vs 1600
        Assert.DoesNotContain(wall,g.Computer.Field);Assert.Contains(g.Player.Graveyard,c=>c.Card.Id=="WCG-284");
    }
    [Fact] public void HornblowerShrinksAllEnemyTauntsThisTurnOnly()
    {
        var g=Game();var t1=Unit(g.Computer,113,0);var t2=Unit(g.Computer,81,1);var plain=Unit(g.Computer,155,2);Refresh(g);
        var c=Hand(g,285);Assert.True(g.SummonMonster(g.Player,c));Choose(g,o=>o.Id=="SLOT:0");Settle(g);Refresh(g);
        Assert.Equal(1400,t1.CurrentPP);Assert.Equal(500,t2.CurrentPP);Assert.Equal(1600,plain.CurrentPP);
        g.EndTurn();Settle(g);Refresh(g);Assert.Equal(1800,t1.CurrentPP);
    }
    [Fact] public void StalkerShrinksWhatItAttacksOnly()
    {
        var g=Game();var s=Unit(g.Player,286,0);var foe=Unit(g.Computer,155,1);Refresh(g);
        Assert.True(g.Attack(g.Player,s,foe));Settle(g); // 1300 vs 1600-400
        Assert.DoesNotContain(foe,g.Computer.Field);Assert.Contains(s,g.Player.Field);
        var g2=Game();var s2=Unit(g2.Computer,286,0);var a=Unit(g2.Player,155,1);Refresh(g2);
        Assert.True(g2.Attack(g2.Player,a,s2));Settle(g2);Assert.DoesNotContain(s2,g2.Computer.Field);Assert.Contains(a,g2.Player.Field);
    }
    [Fact] public void GnawingCurseStaysAndDrawsOnATaunt()
    {
        var g=Game();var wall=Unit(g.Computer,113,0);Refresh(g);var s=Hand(g,288);
        Assert.True(g.CastSpellAt(g.Player,s,wall));Settle(g);Refresh(g);Assert.Equal(1300,wall.CurrentPP);Assert.Single(g.Player.Hand);
        var a=Unit(g.Player,155,1);Refresh(g);Assert.True(g.Attack(g.Player,a,wall));Settle(g);
        Assert.DoesNotContain(wall,g.Computer.Field);
        var g2=Game();var foe=Unit(g2.Computer,155,0);Refresh(g2);var s2=Hand(g2,288);Assert.True(g2.CastSpellAt(g2.Player,s2,foe));Settle(g2);Refresh(g2);
        Assert.Empty(g2.Player.Hand);Assert.Equal(1100,foe.CurrentPP);Assert.Single(foe.Attachments);
    }
    [Fact] public void MiasmaBogShrinksEnemyTauntsAndShowsInTheBreakdown()
    {
        var g=Game();g.Player.Structures.Add(new MonsterInstance(cards.GetCard("WCG-289")!){Slot=4});
        var wall=Unit(g.Computer,113,0);var plain=Unit(g.Computer,155,1);var mine=Unit(g.Player,81,0);Refresh(g);
        Assert.Equal(1500,wall.CurrentPP);Assert.Equal(1600,plain.CurrentPP);Assert.Equal(900,mine.CurrentPP);
        Assert.Equal(wall.CurrentPP,(wall.Card.PP??0)+g.PowerModifiers(wall).Sum(x=>x.Amount));
    }
    [Fact] public void SkullpiercerIgnoresSmallerTaunts()
    {
        var g=Game();var p=Unit(g.Player,290,0);var small=Unit(g.Computer,113,0);var other=Unit(g.Computer,101,1);Refresh(g);
        Assert.True(g.CanAttackPlayer(p));Assert.Contains(other,g.GetAttackTargets(p));
        var big=Unit(g.Computer,100,2);Refresh(g);Assert.False(g.CanAttackPlayer(p));
        var s=Hand(g,284);Assert.True(g.CastSpellAt(g.Player,s,big));Settle(g);Refresh(g);Assert.False(g.CanAttackPlayer(p)); // 2700-700 = 2000 is not lower
        var s2=Hand(g,284);Assert.True(g.CastSpellAt(g.Player,s2,big));Settle(g);Refresh(g);Assert.True(g.CanAttackPlayer(p));
    }
    [Fact] public void WandererShrinksOneEnemyThisTurnAndAiPicksATaunt()
    {
        var g=Game();var wall=Unit(g.Computer,113,0);var plain=Unit(g.Computer,155,1);Refresh(g);
        var c=Hand(g,291);Assert.True(g.SummonMonster(g.Player,c));Choose(g,o=>o.Id=="SLOT:0");
        Assert.Equal(GameEngine.DecayTitle,g.CurrentPendingTarget!.Title);Assert.True(g.SelectTarget(plain));Settle(g);Refresh(g);Assert.Equal(1300,plain.CurrentPP);
    }
}
