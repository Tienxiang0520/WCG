using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;

namespace WcgTests;

public sealed class DamageCounterTests
{
    readonly CardDatabase cards;
    public DamageCounterTests()
    {
        var env=new Mock<IWebHostEnvironment>();
        env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));
        cards=new(env.Object);
    }
    CardDefinition Card(int id)=>cards.GetCard($"WCG-{id:000}")!;
    GameEngine Scene()
    {
        var g=new GameEngine(cards,new Random(17));g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1]);
        foreach(var p in new[]{g.Player,g.Computer}){p.Hand.Clear();p.EnergyZone.Clear();Energy(p,12);}
        return g;
    }
    void Energy(PlayerState p,int count){for(int i=0;i<count;i++)p.EnergyZone.Add(new(Card(101)));}
    CardInstance Hand(PlayerState p,int id){var c=new CardInstance(Card(id));p.Hand.Add(c);return c;}
    MonsterInstance Unit(PlayerState p,int id,int slot=0){var m=new MonsterInstance(Card(id)){Slot=slot,IsTapped=false,HasSummoningSickness=false};p.Field.Add(m);return m;}
    void Choose(GameEngine g,string id)=>Assert.True(g.SelectChoice(g.CurrentPendingChoice!.Options.Single(o=>o.Id==id)));
    void React(GameEngine g,CardInstance c)=>Choose(g,c.InstanceId.ToString());
    [Fact]public void CountersStayInHandAndCannotBeSetOrCastNormally()
    {
        var g=Scene();var c=Hand(g.Player,195);var energy=g.Player.AvailableEnergy;
        Assert.False(g.SetCard(g.Player,c));Assert.False(g.CastSpell(g.Player,c));
        Assert.Contains(c,g.Player.Hand);Assert.Equal(energy,g.Player.AvailableEnergy);Assert.Empty(g.Player.Structures);
        Assert.Equal(43,g.Player.Deck.Count); // No life-card deal and no damage draw.
    }
    [Theory][InlineData(0,false)][InlineData(1,false)][InlineData(2,true)]
    public void PopupRequiresAffordableCardAndDoesNotDiscardInvalidCards(int energy,bool popup)
    {
        var g=Scene();g.Computer.EnergyZone.Clear();Energy(g.Computer,energy);var c=Hand(g.Computer,195);var a=Unit(g.Player,101);
        Assert.True(g.Attack(g.Player,a));Assert.Equal(6,g.Computer.Hp);Assert.Equal(popup,g.IsWaiting);
        if(popup)React(g,c);
        Assert.Equal(popup?7:6,g.Computer.Hp);Assert.Equal(popup?0:energy,g.Computer.AvailableEnergy);
        Assert.Equal(!popup,g.Computer.Hand.Contains(c));Assert.True(!popup?g.Player.Field.Contains(a):!g.Player.Field.Contains(a));
    }
    [Fact]public void SkipKeepsCardAndEnergyAndAttackFinishesTapped()
    {
        var g=Scene();var c=Hand(g.Computer,196);var a=Unit(g.Player,101);Assert.True(g.Attack(g.Player,a));Choose(g,"SKIP");
        Assert.Equal(6,g.Computer.Hp);Assert.Contains(c,g.Computer.Hand);Assert.Equal(12,g.Computer.AvailableEnergy);Assert.True(a.IsTapped);
    }
    [Theory][InlineData(195)][InlineData(199)]public void MonsterOnlyCounterNeverRespondsToSpellEvenWithEnergy(int id)
    {
        var g=Scene();var counter=Hand(g.Computer,id);var spell=Hand(g.Player,14);
        Assert.True(g.CastSpell(g.Player,spell));Assert.False(g.IsWaiting);Assert.Equal(6,g.Computer.Hp);
        Assert.Contains(counter,g.Computer.Hand);Assert.Equal(12,g.Computer.AvailableEnergy);
    }
    [Fact]public void SpellWindowListsOnlyLegalCardsAndRejectsForgedChoice()
    {
        var g=Scene();var illegal=Hand(g.Computer,195);var valid=Hand(g.Computer,196);
        Assert.True(g.CastSpell(g.Player,Hand(g.Player,14)));Assert.Equal(6,g.Computer.Hp);
        Assert.Equal(new[]{valid.InstanceId.ToString(),"SKIP"},g.CurrentPendingChoice!.Options.Select(o=>o.Id));
        Assert.False(g.SelectChoice(new(){Id=illegal.InstanceId.ToString()}));Assert.Equal(12,g.Computer.AvailableEnergy);
        React(g,valid);Assert.Equal(7,g.Computer.Hp);Assert.Equal(10,g.Computer.AvailableEnergy);Assert.Contains(illegal,g.Computer.Hand);
    }
    [Fact]public void UntargetableDamageSourceCannotBeDestroyedByCounter()
    {
        var g=Scene();var c=Hand(g.Computer,195);var a=Unit(g.Player,152);a.CurrentDP=1;
        Assert.True(g.Attack(g.Player,a));Assert.False(g.IsWaiting);Assert.Contains(c,g.Computer.Hand);
        a.IsTapped=false;a.IsSilenced=true;Assert.True(g.Attack(g.Player,a));Assert.True(g.IsWaiting);React(g,c);
        Assert.DoesNotContain(a,g.Player.Field);Assert.Equal(6,g.Computer.Hp);
    }
    [Fact]public void LethalDamageAllowsCounterBeforeDefeat()
    {
        var g=Scene();g.Computer.Hp=1;var c=Hand(g.Computer,198);var a=Unit(g.Player,119);a.CurrentDP=3;
        Assert.True(g.Attack(g.Player,a));Assert.Equal(0,g.Computer.Hp);Assert.False(g.IsOver);React(g,c);
        Assert.Equal(3,g.Computer.Hp);Assert.False(g.IsOver);Assert.True(a.IsTapped);Assert.Contains(c,g.Computer.Graveyard);
    }
    [Fact]public void LethalSkippedCounterLosesImmediatelyWithoutConsumption()
    {
        var g=Scene();g.Computer.Hp=1;var c=Hand(g.Computer,198);Assert.True(g.Attack(g.Player,Unit(g.Player,101)));
        Choose(g,"SKIP");Assert.True(g.Computer.HasLost);Assert.Contains(c,g.Computer.Hand);Assert.Equal(12,g.Computer.AvailableEnergy);
    }
    [Fact]public void OriginalSpellContinuesDrawingAfterSelfDamageCounter()
    {
        var g=Scene();g.Player.Hp=1;var counter=Hand(g.Player,198);var spell=Hand(g.Player,19);var count=g.Player.Deck.Count;
        Assert.True(g.CastSpell(g.Player,spell));Assert.Equal(0,g.Player.Hp);Assert.False(g.IsOver);Assert.Equal(count,g.Player.Deck.Count);
        React(g,counter);Assert.Equal(3,g.Player.Hp);Assert.Equal(count-2,g.Player.Deck.Count);Assert.Equal(2,g.Player.Hand.Count);
    }
    [Fact]public void LethalWithoutReactionDoesNotWaitForLaterOriginalHealingOrDraw()
    {
        var g=Scene();g.Player.Hp=1;var count=g.Player.Deck.Count;Assert.True(g.CastSpell(g.Player,Hand(g.Player,19)));
        Assert.True(g.Player.HasLost);Assert.Equal(count,g.Player.Deck.Count);
    }
    [Fact]public void LifePaymentNeverOffersCounter()
    {
        var g=Scene();g.Player.Hp=1;var c=Hand(g.Player,198);var victim=Unit(g.Player,101);
        Assert.True(g.CastSpell(g.Player,Hand(g.Player,96)));Assert.True(g.SelectTarget(victim));
        Assert.True(g.Player.HasLost);Assert.Contains(c,g.Player.Hand);Assert.False(g.IsWaiting);
    }
    [Fact]public void CounterDamageCanBeCounteredButNotByMonsterOnlyCard()
    {
        var g=Scene();var monsterOnly=Hand(g.Player,195);var answer=Hand(g.Player,196);var c=Hand(g.Computer,195);var a=Unit(g.Player,119);
        Assert.True(g.Attack(g.Player,a));React(g,c);
        Assert.Equal(g.Player.Id,g.DecisionPlayerId);Assert.Equal(6,g.Player.Hp);Assert.Equal(7,g.Computer.Hp);
        Assert.Equal(new[]{answer.InstanceId.ToString(),"SKIP"},g.CurrentPendingChoice!.Options.Select(o=>o.Id));
        React(g,answer);Assert.Equal(7,g.Player.Hp);Assert.False(g.IsWaiting);Assert.False(g.IsOver);Assert.Contains(monsterOnly,g.Player.Hand);
    }
    [Fact]public void DeathSourceInGraveStillAllowsBarrierButNotDestroyOnlyCounter()
    {
        var g=Scene();var destroy=Hand(g.Computer,195);var barrier=Hand(g.Computer,196);var source=Unit(g.Player,3);var foe=Unit(g.Computer,119);
        Assert.True(g.Attack(g.Player,source,foe));Assert.DoesNotContain(source,g.Player.Field);
        Assert.Equal(new[]{barrier.InstanceId.ToString(),"SKIP"},g.CurrentPendingChoice!.Options.Select(o=>o.Id));
        React(g,barrier);Assert.Equal(7,g.Computer.Hp);Assert.Contains(destroy,g.Computer.Hand);
    }
    [Fact]public void DeathSourceAllowsRecoveryCounterWhenItsRecoveryConditionsHold()
    {
        var g=Scene();var c=Hand(g.Computer,199);var recover=new CardInstance(Card(83));g.Computer.Graveyard.Add(recover);
        var a=Unit(g.Player,3);Assert.True(g.Attack(g.Player,a,Unit(g.Computer,119)));React(g,c);
        Choose(g,recover.InstanceId.ToString());Assert.Contains(recover,g.Computer.Hand);Assert.Equal(6,g.Computer.Hp);
        Assert.False(g.IsWaiting);Assert.Contains(c,g.Computer.Graveyard);
    }
    [Fact]public void DeadSourceAndNoRecoverableMonsterCreatesNoCounterPopup()
    {
        var g=Scene();var c=Hand(g.Computer,199);g.Computer.Graveyard.Add(new(Card(119)));
        Assert.True(g.Attack(g.Player,Unit(g.Player,3),Unit(g.Computer,119)));
        Assert.False(g.IsWaiting);Assert.Equal(6,g.Computer.Hp);Assert.Contains(c,g.Computer.Hand);Assert.Equal(12,g.Computer.AvailableEnergy);
    }
    [Fact]public void DamageFromEnemyMonsterPassiveCanTriggerMonsterCounter()
    {
        var g=Scene();var c=Hand(g.Player,195);var attacker=Unit(g.Player,101);var passive=Unit(g.Computer,144);
        Assert.True(g.Attack(g.Player,attacker,passive));React(g,c);
        Assert.DoesNotContain(passive,g.Computer.Field);Assert.Equal(7,g.Player.Hp);Assert.False(g.IsWaiting);
    }
    [Fact]public void PassiveWardDamageOffersBarrierAndNeverMonsterOnlyCounter()
    {
        var g=Scene();g.Player.Structures.Add(new(Card(124)){Slot=1});var wrong=Hand(g.Computer,195);var right=Hand(g.Computer,196);
        Assert.True(g.Attack(g.Player,Unit(g.Player,119),Unit(g.Computer,101)));
        Assert.Equal(new[]{right.InstanceId.ToString(),"SKIP"},g.CurrentPendingChoice!.Options.Select(o=>o.Id));React(g,right);
        Assert.Equal(7,g.Computer.Hp);Assert.Contains(wrong,g.Computer.Hand);
    }
    [Fact]public void FullyReducedSpellDamageDoesNotCreateAWindow()
    {
        var g=Scene();Unit(g.Computer,101);g.Computer.Structures.Add(new(Card(127)){Slot=1});var c=Hand(g.Computer,196);
        Assert.True(g.CastSpell(g.Player,Hand(g.Player,14)));Assert.Equal(7,g.Computer.Hp);Assert.False(g.IsWaiting);Assert.Contains(c,g.Computer.Hand);
    }
    [Fact]public void ReversingDamageOnceClosesWindowAndDoesNotConsumeSecondCounter()
    {
        var g=Scene();var first=Hand(g.Computer,196);var second=Hand(g.Computer,196);
        Assert.True(g.Attack(g.Player,Unit(g.Player,101)));React(g,first);
        Assert.Equal(7,g.Computer.Hp);Assert.False(g.IsWaiting);Assert.Contains(second,g.Computer.Hand);Assert.Equal(10,g.Computer.AvailableEnergy);
    }
    [Fact]public void RedemptionCanAttachAndCanDeclineAttachment()
    {
        foreach(var attach in new[]{false,true})
        {
            var g=Scene();var target=Unit(g.Computer,101);var c=Hand(g.Computer,198);
            Assert.True(g.Attack(g.Player,Unit(g.Player,119)));React(g,c);
            Choose(g,attach?target.InstanceId.ToString():"SKIP");Assert.False(g.IsWaiting);
            Assert.Equal(attach,target.Attachments.Any(a=>a.Card==c));Assert.Equal(!attach,g.Computer.Graveyard.Contains(c));
        }
    }
    [Fact]public void BothPlayersCanSurviveNestedLethalReaction()
    {
        var g=Scene();g.Player.Hp=g.Computer.Hp=1;var c=Hand(g.Computer,195);var answer=Hand(g.Player,198);
        Assert.True(g.Attack(g.Player,Unit(g.Player,119)));React(g,c);
        Assert.Equal(0,g.Player.Hp);Assert.False(g.IsOver);React(g,answer);
        Assert.Equal(3,g.Player.Hp);Assert.Equal(1,g.Computer.Hp);Assert.False(g.IsOver);Assert.False(g.IsWaiting);
    }
}
