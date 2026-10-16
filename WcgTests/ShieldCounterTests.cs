using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;
namespace WcgTests;

// 聖盾指示物 (2026-10-17): gaining 聖盾 puts one counter on the monster; the first combat destruction removes it instead.
// No refresh. Losing 聖盾 removes the counter; gaining it again adds a new one. Older catalogs keep the per-turn shield.
public class ShieldCounterTests
{
    private readonly CardDatabase cards;
    public ShieldCounterTests(){var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));cards=new(env.Object);}
    private GameEngine Game(CardDatabase? db=null){db??=cards;var g=new GameEngine(db,new Random(17));g.StartGame(db.PresetDecks[0],db.PresetDecks[1]);foreach(var p in new[]{g.Player,g.Computer}){p.Hand.Clear();p.Field.Clear();p.EnergyZone.Clear();}for(int i=0;i<12;i++)g.Player.EnergyZone.Add(new(cards.GetCard("WCG-101")!));return g;}
    private MonsterInstance Unit(PlayerState p,int n,int slot){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=false,HasSummoningSickness=false};p.Field.Add(m);p.Field.Sort((a,b)=>a.Slot.CompareTo(b.Slot));return m;}
    private static void Refresh(GameEngine g)=>typeof(GameEngine).GetMethod("RefreshBoard",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);
    private static void ComputerTurn(GameEngine g){Assert.True(g.EndTurn());while(g.CurrentPendingChoice is {} c)Assert.True(g.SelectChoice(c.Options.Last()));Assert.Equal("computer",g.CurrentTurnPlayerId);}
    private static void PlayerTurn(GameEngine g){Assert.True(g.EndTurn());while(g.CurrentPendingChoice is {} c)Assert.True(g.SelectChoice(c.Options.Last()));Assert.Equal("player",g.CurrentTurnPlayerId);}

    [Fact] public void CurrentCatalogUsesCountersAndOlderCatalogsDoNot()
    {
        Assert.True(Game().ShieldCounterRules);
        var old=CounterHistory.BeforeShieldCounter(cards);
        Assert.False(Game(old).ShieldCounterRules);Assert.True(Game(old).SimplifiedRules);
        Assert.Equal("嘲諷；聖盾。",old.GetCard("WCG-031")!.Text);Assert.Contains("聖盾指示物",cards.GetCard("WCG-209")!.Text);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(CounterHistory.BeforeShieldCounter(cards).AllCards),System.Text.Json.JsonSerializer.Serialize(CardBalanceHistory.BeforeShieldCounter(cards).AllCards));
        Assert.All(cards.AllCards.Where(c=>c.Keywords.Contains("聖盾")),c=>Assert.Contains("聖盾（進場放 1 個指示物）",c.Text));
    }

    [Fact] public void CounterSavesOnceAndNeverComesBack()
    {
        var g=Game();var knight=Unit(g.Player,69,1);var wall=Unit(g.Computer,155,3);Refresh(g);
        Assert.False(knight.ShieldSpent);Assert.True(g.PreviewAttack(knight,wall)!.AttackerShieldBreaks);
        Assert.True(g.Attack(g.Player,knight,wall));Assert.Contains(knight,g.Player.Field);Assert.True(knight.ShieldSpent);
        ComputerTurn(g);Assert.True(knight.ShieldSpent);
        Assert.True(g.Attack(g.Computer,wall,knight));Assert.DoesNotContain(knight,g.Player.Field);
    }

    [Fact] public void SilenceRemovesTheCounterAndDispelGivesANewOne()
    {
        var g=Game();var knight=Unit(g.Player,69,1);Refresh(g);Assert.False(knight.ShieldSpent);
        knight.IsSilenced=true;Refresh(g);Assert.False(knight.HasShield);Assert.True(knight.ShieldSpent);
        knight.IsSilenced=false;Refresh(g);Assert.True(knight.HasShield);Assert.False(knight.ShieldSpent);
    }

    [Fact] public void ArrowGivesOneCounterWhenItStartsPointingAndKeepsItSpent()
    {
        var g=Game();var target=Unit(g.Player,154,2);Refresh(g);Assert.False(target.HasShield);
        var guard=Unit(g.Player,61,1);Refresh(g);Assert.True(target.HasShield);Assert.False(target.ShieldSpent);
        target.ShieldSpent=true;Refresh(g);Assert.True(target.ShieldSpent);
        ComputerTurn(g);PlayerTurn(g);Assert.True(target.ShieldSpent);
        g.Player.Field.Remove(guard);Refresh(g);Assert.False(target.HasShield);
    }

    [Fact] public void AttachedShieldSpellRefillsASpentCounter()
    {
        var g=Game();var knight=Unit(g.Player,69,1);Refresh(g);knight.ShieldSpent=true;
        var spell=new CardInstance(cards.GetCard("WCG-074")!);g.Player.Hand.Add(spell);
        Assert.True(g.CastSpellAt(g.Player,spell,knight));while(g.CurrentPendingChoice is {} c)Assert.True(g.SelectChoice(c.Options.First()));
        Assert.False(knight.ShieldSpent);
    }

    [Fact] public void OldEraStillRefreshesEachTurn()
    {
        var g=Game(CounterHistory.BeforeShieldCounter(cards));var knight=Unit(g.Player,69,1);Refresh(g);knight.ShieldSpent=true;
        ComputerTurn(g);Assert.False(knight.ShieldSpent);
    }
}
