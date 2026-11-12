using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;
namespace WcgTests;

// 聖盾來源 (2026-11-12 指示物改用牌組裡的卡): a monster has 聖盾 while it has a source — an attached 聖盾 spell, a hand card
// put face up under it, or a creature giving it 聖盾 with an arrow. When it is attacked one source goes to the graveyard
// (the controller picks when there are several) and it isn't destroyed that combat. The attacker never spends one.
public class DeckCardShieldTests
{
    private readonly CardDatabase cards;
    public DeckCardShieldTests(){var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));cards=new(env.Object);}
    private GameEngine Game(CardDatabase? db=null){db??=cards;var g=new GameEngine(db,new Random(17));g.StartGame(db.PresetDecks[0],db.PresetDecks[1]);foreach(var p in new[]{g.Player,g.Computer}){p.Hand.Clear();p.Field.Clear();p.EnergyZone.Clear();}for(int i=0;i<12;i++)g.Player.EnergyZone.Add(new(cards.GetCard("WCG-101")!));return g;}
    private MonsterInstance Unit(PlayerState p,int n,int slot){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=false,HasSummoningSickness=false};p.Field.Add(m);p.Field.Sort((a,b)=>a.Slot.CompareTo(b.Slot));return m;}
    private CardInstance Card(int n)=>new(cards.GetCard($"WCG-{n:000}")!);
    private static void Refresh(GameEngine g)=>typeof(GameEngine).GetMethod("RefreshBoard",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);
    private static void ComputerTurn(GameEngine g){Assert.True(g.EndTurn());while(g.CurrentPendingChoice is {} c)Assert.True(g.SelectChoice(c.Options.Last()));Assert.Equal("computer",g.CurrentTurnPlayerId);}
    private static void Drain(GameEngine g){while(g.CurrentPendingChoice is {} c)Assert.True(g.SelectChoice(c.Options.First()));}

    [Fact] public void CurrentCatalogUsesSourcesAndNoCardHasNativeShield()
    {
        var g=Game();Assert.True(g.ShieldSourceRules);Assert.True(g.DeckCardRules);
        Assert.DoesNotContain(cards.AllCards,c=>c.Keywords.Contains("聖盾"));
        Assert.DoesNotContain(cards.AllCards,c=>c.Text.Contains("指示物"));
        Assert.False(Game(CounterHistory.BeforeShieldCounter(CardBalanceHistory.BeforeDeckCards(cards))).ShieldSourceRules);
    }

    [Fact] public void AttackedMonsterSpendsTheGiverEvenWhenItWouldSurvive()
    {
        var g=Game();var giver=Unit(g.Player,61,1);var target=Unit(g.Player,154,2);var small=Unit(g.Computer,101,3);Refresh(g);
        Assert.True(target.HasShield);Assert.Equal(1,g.ShieldSourceCount(target));ComputerTurn(g);
        var preview=g.PreviewAttack(small,target);if(preview!=null){Assert.True(preview.DefenderShieldBreaks);Assert.False(preview.DefenderDies);}
        Assert.True(g.Attack(g.Computer,small,target));Drain(g);
        Assert.Contains(target,g.Player.Field);Assert.DoesNotContain(giver,g.Player.Field);Assert.Contains(g.Player.Graveyard,c=>c.Card.Id=="WCG-061");
        Assert.False(target.HasShield);
    }

    [Fact] public void ShieldSavesTheAttackedMonsterFromALosingCombat()
    {
        var g=Game();Unit(g.Player,61,1);var target=Unit(g.Player,154,2);var wall=Unit(g.Computer,155,2);Refresh(g);ComputerTurn(g);
        Assert.True(g.Attack(g.Computer,wall,target));Drain(g);
        Assert.Contains(target,g.Player.Field);Assert.False(target.HasShield);
    }

    [Fact] public void AttackerNeverSpendsASource()
    {
        var g=Game();var giver=Unit(g.Player,61,1);var target=Unit(g.Player,154,2);var wall=Unit(g.Computer,155,3);Refresh(g);
        var preview=g.PreviewAttack(target,wall)!;Assert.False(preview.AttackerShieldBreaks);Assert.True(preview.AttackerDies);
        Assert.True(g.Attack(g.Player,target,wall));Drain(g);
        Assert.DoesNotContain(target,g.Player.Field);Assert.Contains(giver,g.Player.Field);
    }

    [Fact] public void AttachedShieldSpellIsTheSourceAndGoesToTheGraveyard()
    {
        var g=Game();var target=Unit(g.Player,154,2);var spell=Card(74);g.Player.Hand.Add(spell);Refresh(g);
        Assert.True(g.CastSpellAt(g.Player,spell,target));Drain(g);
        Assert.Contains(target.Attachments,a=>a.Card.Card.Id=="WCG-074");Assert.True(target.HasShield);
        var wall=Unit(g.Computer,155,2);Refresh(g);ComputerTurn(g);
        Assert.True(g.Attack(g.Computer,wall,target));Drain(g);
        Assert.Contains(target,g.Player.Field);Assert.Empty(target.Attachments);Assert.Contains(g.Player.Graveyard,c=>c.Card.Id=="WCG-074");
    }

    [Fact] public void SeveralSourcesLetTheControllerPick()
    {
        var g=Game();var giver=Unit(g.Player,61,1);var target=Unit(g.Player,154,2);var spell=Card(74);g.Player.Hand.Add(spell);Refresh(g);
        Assert.True(g.CastSpellAt(g.Player,spell,target));Drain(g);Assert.Equal(2,g.ShieldSourceCount(target));
        var wall=Unit(g.Computer,155,2);Refresh(g);ComputerTurn(g);
        Assert.True(g.Attack(g.Computer,wall,target));
        var choice=g.CurrentPendingChoice!;Assert.Equal(GameEngine.ShieldSourceTitle,choice.Title);Assert.Equal("player",choice.OwnerId);Assert.Equal(2,choice.Options.Count);
        Assert.True(g.SelectChoice(choice.Options.First(o=>o.Title==giver.Card.Name)));Drain(g);
        Assert.Contains(target,g.Player.Field);Assert.DoesNotContain(giver,g.Player.Field);Assert.Contains(target.Attachments,a=>a.Card.Card.Id=="WCG-074");Assert.True(target.HasShield);
    }

    [Fact] public void SilenceSendsCardsUnderItToTheGraveyardButArrowsStillGive()
    {
        var g=Game();Unit(g.Player,61,1);var target=Unit(g.Player,154,2);var spell=Card(74);g.Player.Hand.Add(spell);Refresh(g);
        Assert.True(g.CastSpellAt(g.Player,spell,target));Drain(g);
        target.ShieldCards.Add(new(Card(101),g.Player.Id));Refresh(g);Assert.Equal(3,g.ShieldSourceCount(target));
        typeof(GameEngine).GetMethod("Silence",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,[g.Computer,Card(72),target]);
        Assert.Empty(target.ShieldCards);Assert.DoesNotContain(target.Attachments,a=>a.Card.Card.Id=="WCG-074");
        Assert.True(target.HasShield);Assert.Equal(1,g.ShieldSourceCount(target));
    }

    [Fact] public void SilencedGiverStopsGivingAndIsNotDestroyed()
    {
        var g=Game();var giver=Unit(g.Player,61,1);var target=Unit(g.Player,154,2);Refresh(g);Assert.True(target.HasShield);
        giver.IsSilenced=true;Refresh(g);Assert.False(target.HasShield);Assert.Contains(giver,g.Player.Field);
    }

    [Fact] public void ArbiterUpArrowPurgesShieldCardsAndBlocksGivers()
    {
        var g=Game();var target=Unit(g.Computer,154,2);target.ShieldCards.Add(new(Card(101),g.Computer.Id));Refresh(g);Assert.True(target.HasShield);
        Unit(g.Player,147,2);Refresh(g);
        Assert.Empty(target.ShieldCards);Assert.False(target.HasShield);Assert.Contains(g.Computer.Graveyard,c=>c.Card.Id=="WCG-101");
    }

    [Fact] public void ShieldBreakersSendShieldCardsToTheGraveyardButNeverTheGiver()
    {
        var g=Game();var giver=Unit(g.Computer,61,1);var a=Unit(g.Computer,154,2);var b=Unit(g.Computer,155,3);
        a.ShieldCards.Add(new(Card(101),g.Computer.Id));a.ShieldCards.Add(new(Card(101),g.Computer.Id));b.ShieldCards.Add(new(Card(101),g.Computer.Id));b.ShieldCards.Add(new(Card(101),g.Computer.Id));Refresh(g);
        Assert.Contains("所有聖盾法術",cards.GetCard("WCG-205")!.Text);Assert.Contains("1 張聖盾法術",cards.GetCard("WCG-209")!.Text);
        var m=typeof(GameEngine).GetMethod("BreakShield",BindingFlags.NonPublic|BindingFlags.Instance)!;
        m.Invoke(g,[g.Player,true]);Assert.True(g.SelectTarget(a));Assert.Empty(a.ShieldCards);Assert.True(a.HasShield);Assert.Contains(giver,g.Computer.Field);
        m.Invoke(g,[g.Player,false]);Assert.True(g.SelectTarget(b));Assert.Single(b.ShieldCards);
    }

    [Fact] public void ShieldDoesNotRefreshAtTurnChange()
    {
        var g=Game();var target=Unit(g.Player,154,2);target.ShieldCards.Add(new(Card(101),g.Player.Id));Refresh(g);
        var wall=Unit(g.Computer,155,2);Refresh(g);ComputerTurn(g);
        Assert.True(g.Attack(g.Computer,wall,target));Drain(g);Assert.Contains(target,g.Player.Field);Assert.False(target.HasShield);
        Assert.True(g.EndTurn());Drain(g);Refresh(g);Assert.False(target.HasShield);
    }

    [Fact] public void LeavingPlaySendsShieldCardsToTheirOwnersGraveyard()
    {
        var g=Game();var target=Unit(g.Player,154,2);var hand=Card(101);target.ShieldCards.Add(new(hand,g.Player.Id));Refresh(g);
        var kill=typeof(GameEngine).GetMethod("KillBatch",BindingFlags.NonPublic|BindingFlags.Instance)!;
        kill.Invoke(g,[new[]{target},"測試",true]);Drain(g);
        Assert.Contains(hand,g.Player.Graveyard);Assert.Empty(target.ShieldCards);
    }

    [Fact] public void OldEraStillRefreshesEachTurn()
    {
        var db=CounterHistory.BeforeShieldCounter(CardBalanceHistory.BeforeDeckCards(cards));var g=Game(db);var knight=Unit(g.Player,69,1);knight.Card=db.GetCard("WCG-069")!;Refresh(g);Assert.True(knight.HasShield);knight.ShieldSpent=true;
        ComputerTurn(g);Assert.False(knight.ShieldSpent);
    }
}
