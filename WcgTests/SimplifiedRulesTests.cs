using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;
namespace WcgTests;

// 規則簡化（PR A）：能量只付費用、聖盾每回合重置、貫穿無 PP 差、PP 門檻 3 級、200–204 固定效果、143/165/168 附著、反擊每次傷害最多 1 張。
public class SimplifiedRulesTests
{
    private readonly CardDatabase cards;
    public SimplifiedRulesTests(){var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));cards=new(env.Object);}
    private GameEngine Game(CardDatabase? db=null){db??=cards;var g=new GameEngine(db,new Random(17));g.StartGame(db.PresetDecks[0],db.PresetDecks[1]);foreach(var p in new[]{g.Player,g.Computer}){p.Hand.Clear();p.Field.Clear();p.EnergyZone.Clear();}for(int i=0;i<12;i++)g.Player.EnergyZone.Add(new(cards.GetCard("WCG-101")!));return g;}
    private CardInstance Hand(GameEngine g,int n){var c=new CardInstance(cards.GetCard($"WCG-{n:000}")!);g.Player.Hand.Add(c);return c;}
    private MonsterInstance Unit(PlayerState p,int n,int slot,int? pp=null){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=false,HasSummoningSickness=false};if(pp!=null)m.CurrentPP=pp.Value;p.Field.Add(m);p.Field.Sort((a,b)=>a.Slot.CompareTo(b.Slot));return m;}
    private static void Refresh(GameEngine g)=>typeof(GameEngine).GetMethod("RefreshBoard",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);
    private static void Choose(GameEngine g,Func<ChoiceOption,bool> pick)=>Assert.True(g.SelectChoice(g.CurrentPendingChoice!.Options.First(pick)));
    // Computer's turn without running its AI: tests drive the computer's attacks directly.
    private static void ComputerTurn(GameEngine g){Assert.True(g.EndTurn());while(g.CurrentPendingChoice is {} c)Assert.True(g.SelectChoice(c.Options.Last()));Assert.Equal("computer",g.CurrentTurnPlayerId);}

    [Fact] public void CurrentCatalogUsesSimplifiedRulesAndOldCatalogDoesNot()
    {
        Assert.True(Game().SimplifiedRules);
        Assert.False(Game(CounterHistory.BeforeRulesSimplification(cards)).SimplifiedRules);
        Assert.All(cards.AllCards,c=>Assert.DoesNotContain(c.Keywords,k=>k is "能量護盾" or "護主" or "怒擊" or "觀測" or "野性" or "回魂"));
    }

    [Fact] public void HolyShieldSavesOncePerTurnWithoutEnergyAndRefreshesEachTurn()
    {
        var g=Game();var knight=Unit(g.Player,69,1);var wall=Unit(g.Computer,155,3);var energy=g.Player.AvailableEnergy;Refresh(g);
        Assert.True(g.PreviewAttack(knight,wall)!.AttackerShieldBreaks);
        Assert.True(g.Attack(g.Player,knight,wall));Assert.False(g.IsWaiting);
        Assert.Contains(knight,g.Player.Field);Assert.True(knight.ShieldSpent);Assert.Equal(energy,g.Player.AvailableEnergy);
        knight.IsTapped=false;Assert.True(g.PreviewAttack(knight,wall)!.AttackerDies);
        var second=Unit(g.Player,63,2);Assert.True(g.Attack(g.Player,second,wall));Assert.Contains(second,g.Player.Field);
        ComputerTurn(g);Assert.False(knight.ShieldSpent);Assert.False(second.ShieldSpent);
        Assert.True(g.Attack(g.Computer,wall,knight));Assert.Contains(knight,g.Player.Field);Assert.True(knight.ShieldSpent);
    }

    [Fact] public void TrampleNoLongerNeedsA700Margin()
    {
        var g=Game();var cat=Unit(g.Player,111,1);var foe=Unit(g.Computer,129,3);Refresh(g);var hp=g.Computer.Hp;
        Assert.Equal(1,g.PreviewAttack(cat,foe)!.PlayerDamage);
        Assert.True(g.Attack(g.Player,cat,foe));Assert.DoesNotContain(foe,g.Computer.Field);Assert.Equal(hp-1,g.Computer.Hp);
    }

    [Theory]
    [InlineData(4,600,true)] [InlineData(4,700,false)]
    [InlineData(2,1100,true)] [InlineData(2,1200,false)]
    [InlineData(32,1600,true)] [InlineData(32,1700,false)]
    [InlineData(76,1800,true)] [InlineData(76,1700,false)]
    public void PrintedThresholdsUseThreeLevels(int spell,int pp,bool legal)
    {
        var g=Game();var foe=Unit(g.Computer,155,2,pp);
        Assert.Equal(legal,g.CastSpellAt(g.Player,Hand(g,spell),foe));
        if(legal){while(g.CurrentPendingChoice is {} c)Assert.True(g.SelectChoice(c.Options[0]));Assert.DoesNotContain(foe,g.Computer.Field);}
    }

    [Fact] public void AreaThresholdsUseThreeLevels()
    {
        var g=Game();var low=Unit(g.Computer,101,0,600);var mid=Unit(g.Computer,101,1,650);
        Assert.True(g.CastSpell(g.Player,Hand(g,68)));Assert.DoesNotContain(low,g.Computer.Field);Assert.Contains(mid,g.Computer.Field);
    }

    [Fact] public void OathguardTapsItselfToReduceDamageByOne()
    {
        var g=Game();var guard=Unit(g.Player,200,1);var ogre=Unit(g.Computer,156,2);var hp=g.Player.Hp;
        ComputerTurn(g);Assert.True(g.Attack(g.Computer,ogre));
        Assert.Equal(GameEngine.OathGuardTitle,g.CurrentPendingChoice!.Title);
        Choose(g,o=>o.Id=="GUARD:1");Assert.Equal(hp-1,g.Player.Hp);Assert.True(guard.IsTapped);
        ogre.IsTapped=false;Assert.True(g.Attack(g.Computer,ogre));Assert.False(g.IsWaiting);Assert.Equal(hp-3,g.Player.Hp); // A tapped guard can't reduce again.
    }

    [Fact] public void FixedAttackAndCombatBonusesReplaceEnergyKeywords()
    {
        var g=Game();var breaker=Unit(g.Player,201,0);Refresh(g);var hp=g.Computer.Hp;var energy=g.Player.AvailableEnergy;
        Assert.Equal(2,g.PreviewAttack(breaker)!.PlayerDamage);
        Assert.True(g.Attack(g.Player,breaker));Assert.False(g.IsWaiting);Assert.Equal(hp-2,g.Computer.Hp);Assert.Equal(1,breaker.CurrentDP);
        var beast=Unit(g.Player,203,1);var yeti=Unit(g.Computer,114,2);Refresh(g);
        Assert.True(g.PreviewAttack(beast,yeti)!.DefenderDies);
        Assert.True(g.Attack(g.Player,beast,yeti));Assert.False(g.IsWaiting);Assert.Contains(beast,g.Player.Field);Assert.DoesNotContain(yeti,g.Computer.Field);
        Assert.Equal(1600,beast.CurrentPP);Assert.Equal(energy,g.Player.AvailableEnergy);
    }

    [Fact] public void ObserverLooksAtTopTwoOnDeploy()
    {
        var g=Game();var card=Hand(g,202);var top=g.Player.Deck[0];
        Assert.True(g.SummonMonster(g.Player,card));Choose(g,o=>o.Id=="SLOT:0");
        Choose(g,o=>o.Id==top.InstanceId.ToString());Assert.Contains(top,g.Player.Hand);
    }

    [Fact] public void WandererReturnsAnotherCheapMonsterOnDeath()
    {
        var g=Game();var wanderer=Unit(g.Player,204,0);var cheap=new CardInstance(cards.GetCard("WCG-101")!);g.Player.Graveyard.Add(cheap);
        var yeti=Unit(g.Computer,114,2);Assert.True(g.Attack(g.Player,wanderer,yeti));
        if(g.CurrentPendingChoice!=null)Choose(g,o=>o.Id==cheap.InstanceId.ToString());
        Assert.Contains(cheap,g.Player.Hand);Assert.Contains(g.Player.Graveyard,c=>c.Card.Id=="WCG-204");
    }

    [Fact] public void CombatBuffsAreAttachmentsThatLeaveAfterTheMonsterBattles()
    {
        var g=Game();var croc=Unit(g.Player,154,0);croc.IsTapped=true;var buff=Hand(g,143);
        Assert.True(g.CastSpellAt(g.Player,buff,croc));Assert.False(croc.IsTapped);Assert.Equal(1800,croc.CurrentPP);Assert.Contains(croc.Attachments,a=>a.Card.Card.Id=="WCG-143");
        Assert.True(g.Attack(g.Player,croc));Assert.Equal(1800,croc.CurrentPP); // Attacking the player is not a battle.
        croc.IsTapped=false;var foe=Unit(g.Computer,155,2);
        Assert.True(g.Attack(g.Player,croc,foe));Assert.Contains(croc,g.Player.Field);Assert.Empty(croc.Attachments);Assert.Equal(1300,croc.CurrentPP);
        Assert.Contains(g.Player.Graveyard,c=>c.InstanceId==buff.InstanceId);
        var weak=Unit(g.Computer,114,3);Assert.True(g.CastSpellAt(g.Player,Hand(g,168),weak));Assert.Equal(1500,weak.CurrentPP);
    }

    [Fact] public void OnlyOneCounterPerDamageAndDeadSourceStillCancels()
    {
        var g=Game();var barrier=Hand(g,196);var oath=Hand(g,198);var ogre=Unit(g.Computer,156,2);var hp=g.Player.Hp;
        ComputerTurn(g);Assert.True(g.Attack(g.Computer,ogre));
        Assert.Equal(2,g.CurrentPendingChoice!.Options.Count(o=>o.Id!="SKIP"));
        Choose(g,o=>o.Id==barrier.InstanceId.ToString());
        Assert.False(g.IsWaiting);Assert.Equal(hp,g.Player.Hp);Assert.Contains(oath,g.Player.Hand);

        var h=Game();var goblin=Unit(h.Computer,3,1);var trap=new CardInstance(cards.GetCard("WCG-195")!);h.Player.Hand.Add(trap);var life=h.Player.Hp;var enemyLife=h.Computer.Hp;
        Assert.True(h.CastSpellAt(h.Player,new CardInstance(cards.GetCard("WCG-004")!).Also(h.Player.Hand.Add),goblin));
        Assert.Contains(h.CurrentPendingChoice!.Options,o=>o.Id==trap.InstanceId.ToString()); // Source already in the graveyard: the counter only cancels.
        Choose(h,o=>o.Id==trap.InstanceId.ToString());
        Assert.Equal(life,h.Player.Hp);Assert.Equal(enemyLife,h.Computer.Hp);
    }

    [Fact] public void CounterDamageCannotBeCountered()
    {
        var g=Game();var trap=Hand(g,195);var yeti=Unit(g.Computer,114,2);
        for(int i=0;i<6;i++)g.Computer.EnergyZone.Add(new(cards.GetCard("WCG-101")!));g.Computer.Hand.Add(new(cards.GetCard("WCG-196")!));
        ComputerTurn(g);Assert.True(g.Attack(g.Computer,yeti));
        Choose(g,o=>o.Id==trap.InstanceId.ToString());
        Assert.DoesNotContain(yeti,g.Computer.Field);Assert.False(g.IsWaiting); // 2200 PP ≥ 1800: 1 damage, and the enemy can't counter it.
        Assert.Contains(g.Computer.Hand,c=>c.Card.Id=="WCG-196");
    }
}
static class TestExtensions { public static T Also<T>(this T value,Action<T> action){action(value);return value;} }
