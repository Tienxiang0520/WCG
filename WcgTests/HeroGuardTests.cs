using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;

namespace WcgTests;

public sealed class HeroGuardTests
{
    readonly CardDatabase cards;
    public HeroGuardTests()
    {
        var env=new Mock<IWebHostEnvironment>();
        env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));
        cards=CounterHistory.BeforeRulesSimplification(new(env.Object)); /* Pre-simplification rules, still used to replay saved ranked matches. */
    }
    GameEngine Scene(int energy=2)
    {
        var g=new GameEngine(cards,new Random(17));g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1]);
        foreach(var p in new[]{g.Player,g.Computer}){p.Hand.Clear();p.EnergyZone.Clear();Energy(p,energy);}
        g.Player.Hp=PlayerState.MaxHp; // fixed scene: both players at full life
        g.Computer.IsAi=false;return g;
    }
    void Energy(PlayerState p,int n){for(var i=0;i<n;i++)p.EnergyZone.Add(new(cards.GetCard("WCG-101")!));}
    MonsterInstance Unit(PlayerState p,string id="WCG-200",int dp=1)
    {
        var m=new MonsterInstance(cards.GetCard(id)!){Slot=p.Field.Count,IsTapped=false,HasSummoningSickness=false,CurrentDP=dp};p.Field.Add(m);return m;
    }
    CardInstance Hand(PlayerState p,string id){var c=new CardInstance(cards.GetCard(id)!);p.Hand.Add(c);return c;}
    static void Choose(GameEngine g,string id)=>Assert.True(g.SelectChoice(g.CurrentPendingChoice!.Options.Single(o=>o.Id==id)));
    [Theory][InlineData(0,4,2)][InlineData(1,5,1)][InlineData(2,6,0)]
    public void PlayerChoosesExactPreventionBeforeLifeIsReduced(int prevent,int hp,int energy)
    {
        var g=Scene();Unit(g.Computer);var a=Unit(g.Player,"WCG-101",3);
        Assert.True(g.Attack(g.Player,a));Assert.Equal(7,g.Computer.Hp);Assert.Equal("computer",g.DecisionPlayerId);
        Assert.Equal(new[]{"GUARD:1","GUARD:2","GUARD:0"},g.CurrentPendingChoice!.Options.Select(o=>o.Id));
        Choose(g,"GUARD:"+prevent);Assert.Equal(hp,g.Computer.Hp);Assert.Equal(energy,g.Computer.AvailableEnergy);
        Assert.False(g.IsWaiting);Assert.True(a.IsTapped);
    }
    [Fact] public void MultipleTappedGuardsProvideOneChoiceAndDoNotMultiplyReduction()
    {
        var g=Scene();Unit(g.Computer).IsTapped=true;Unit(g.Computer).IsTapped=true;
        Assert.True(g.Attack(g.Player,Unit(g.Player,"WCG-101",3)));Choose(g,"GUARD:1");
        Assert.False(g.IsWaiting);Assert.Equal(5,g.Computer.Hp);Assert.Equal(1,g.Computer.AvailableEnergy);
    }
    [Theory][InlineData("silenced")][InlineData("left")][InlineData("hand")][InlineData("no-energy")]
    public void GuardMustBePresentUnsilencedAndHaveReadyEnergy(string mode)
    {
        var g=Scene();var guard=Unit(g.Computer);
        if(mode=="silenced")guard.IsSilenced=true;
        if(mode is "left" or "hand")g.Computer.Field.Remove(guard);
        if(mode=="hand")Hand(g.Computer,"WCG-200");
        if(mode=="no-energy")g.Computer.EnergyZone.ForEach(c=>c.IsTapped=true);
        Assert.True(g.Attack(g.Player,Unit(g.Player,"WCG-101",2)));Assert.False(g.IsWaiting);Assert.Equal(5,g.Computer.Hp);
    }
    [Fact] public void FullyPreventedLethalDamageDoesNotTriggerHandCounters()
    {
        var g=Scene(5);g.Computer.Hp=1;Unit(g.Computer);var counter=Hand(g.Computer,"WCG-198");
        Assert.True(g.Attack(g.Player,Unit(g.Player,"WCG-101",2)));Choose(g,"GUARD:2");
        Assert.Equal(1,g.Computer.Hp);Assert.False(g.IsOver);Assert.False(g.IsWaiting);
        Assert.Contains(counter,g.Computer.Hand);Assert.Equal(3,g.Computer.AvailableEnergy);
    }
    [Fact] public void RemainingDamageChecksCountersUsingOnlyRemainingEnergy()
    {
        var g=Scene(4);var guard=Unit(g.Computer);var counter=Hand(g.Computer,"WCG-198");
        Assert.True(g.Attack(g.Player,Unit(g.Player,"WCG-101",3)));Choose(g,"GUARD:1");
        Assert.Equal(5,g.Computer.Hp);Assert.Equal("受到傷害，可從手牌發動反擊",g.CurrentPendingChoice!.Title);
        Choose(g,counter.InstanceId.ToString());Choose(g,guard.InstanceId.ToString());
        Assert.Equal(7,g.Computer.Hp);Assert.Equal(1,g.Computer.AvailableEnergy);
    }
    [Fact] public void SpendingAllEnergyOnPartialPreventionLeavesNoAffordableCounter()
    {
        var g=Scene();Unit(g.Computer);Hand(g.Computer,"WCG-198");
        Assert.True(g.Attack(g.Player,Unit(g.Player,"WCG-101",3)));Choose(g,"GUARD:2");
        Assert.Equal(6,g.Computer.Hp);Assert.False(g.IsWaiting);
    }
    [Fact] public void IllegalPreventionIsRejectedWithoutChangingLifeOrEnergy()
    {
        var g=Scene();Unit(g.Computer);Assert.True(g.Attack(g.Player,Unit(g.Player,"WCG-101",3)));
        Assert.False(g.SelectChoice(new(){Id="GUARD:3"}));Assert.Equal(7,g.Computer.Hp);Assert.Equal(2,g.Computer.AvailableEnergy);
    }
    [Fact] public void GuardCanPreventSelfDamageWithoutCancellingFollowingDraws()
    {
        var g=Scene(6);Unit(g.Player);var spell=Hand(g.Player,"WCG-019");var deck=g.Player.Deck.Count;
        Assert.True(g.CastSpell(g.Player,spell));Assert.Equal("player",g.DecisionPlayerId);Choose(g,"GUARD:2");
        Assert.Equal(7,g.Player.Hp);Assert.Equal(deck-2,g.Player.Deck.Count);Assert.False(g.IsWaiting);
    }
    [Fact] public void DirectSpellDamageIsPreventedBeforeLifeAndSourceStillGoesToGraveyard()
    {
        var g=Scene(5);Unit(g.Computer);var spell=Hand(g.Player,"WCG-014");
        Assert.True(g.CastSpell(g.Player,spell));Choose(g,"GUARD:1");
        Assert.Equal(7,g.Computer.Hp);Assert.Contains(spell,g.Player.Graveyard);Assert.False(g.IsWaiting);
    }
    [Theory][InlineData(-1)][InlineData(0)][InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)][InlineData(5)]
    public void EveryAiCanResolveGuardOnOpponentsTurn(int level)
    {
        var g=Scene();g.AiLevel=level;g.Computer.IsAi=true;Unit(g.Computer);
        Assert.True(g.Attack(g.Player,Unit(g.Player,"WCG-101",3)));Assert.True(g.ExecuteAiStep());
        Assert.Equal(6,g.Computer.Hp);Assert.Equal(0,g.Computer.AvailableEnergy);Assert.Equal("player",g.DecisionPlayerId);
    }
    [Fact] public void PreviewShowsOptionalPreventionWithoutSpendingEnergy()
    {
        var g=Scene();Unit(g.Computer);var preview=g.PreviewAttack(Unit(g.Player,"WCG-101",3))!;
        Assert.Equal(3,preview.PlayerDamage);Assert.Equal(2,preview.PlayerDamagePreventable);Assert.Equal(2,g.Computer.AvailableEnergy);
    }
    [Fact] public void LifeCostsCannotBePreventedEvenWhenTheyAreLethal()
    {
        var g=Scene();var guard=Unit(g.Player);g.Player.Hp=1;var spell=Hand(g.Player,"WCG-165");
        Assert.True(g.CastSpellAt(g.Player,spell,guard));Assert.True(g.IsOver);
        Assert.Equal(0,g.Player.Hp);Assert.Equal(2,g.Player.AvailableEnergy);Assert.False(g.IsWaiting);
    }
    [Fact] public void TrampleChecksGuardOnlyIfItsProviderSurvivesCombat()
    {
        foreach(var separate in new[]{false,true})
        {
            var g=Scene();var defender=Unit(g.Computer);if(separate)Unit(g.Computer);
            var a=Unit(g.Player,"WCG-111");a.CurrentPP=4000;
            Assert.True(a.HasTrample);Assert.True(g.Attack(g.Player,a,defender));
            if(separate){Assert.Equal("護主：選擇抵銷傷害",g.CurrentPendingChoice!.Title);Choose(g,"GUARD:1");Assert.Equal(7,g.Computer.Hp);}
            else {Assert.False(g.IsWaiting);Assert.Equal(6,g.Computer.Hp);}
        }
    }
    [Fact] public void EachSeparateDamageEventGetsItsOwnChoice()
    {
        var g=Scene();Unit(g.Computer);var a=Unit(g.Player,"WCG-101",1);var b=Unit(g.Player,"WCG-103",1);
        Assert.True(g.Attack(g.Player,a));Choose(g,"GUARD:1");
        Assert.True(g.Attack(g.Player,b));Choose(g,"GUARD:0");
        Assert.Equal(6,g.Computer.Hp);Assert.Equal(1,g.Computer.AvailableEnergy);
    }
    [Theory][InlineData("passive")][InlineData("death")]
    public void PassiveAndDeathDamageAlsoOffersGuard(string source)
    {
        var g=Scene();Unit(g.Computer);
        if(source=="death")
        {
            var doomed=Unit(g.Player,"WCG-003");doomed.CurrentPP=500;
            var target=Unit(g.Computer,"WCG-101");Assert.True(g.Attack(g.Player,doomed,target));
        }
        else
        {
            g.Player.Structures.Add(new(cards.GetCard("WCG-124")!){Slot=4,IsTapped=false});
            var target=Unit(g.Computer,"WCG-101");var a=Unit(g.Player,"WCG-101");a.CurrentPP=2000;
            Assert.True(g.Attack(g.Player,a,target));
        }
        Assert.Equal("護主：選擇抵銷傷害",g.CurrentPendingChoice!.Title);Choose(g,"GUARD:1");
        Assert.Equal(7,g.Computer.Hp);Assert.Equal(1,g.Computer.AvailableEnergy);
    }
    [Fact] public void CounterDamageCanOpenAnOppositeGuardChoiceWithoutLosingTheOriginalReaction()
    {
        var g=Scene(4);Unit(g.Computer);Unit(g.Player);var counter=Hand(g.Computer,"WCG-195");
        var attacker=Unit(g.Player,"WCG-101",3);attacker.CurrentPP=2000;
        Assert.True(g.Attack(g.Player,attacker));Choose(g,"GUARD:1");Choose(g,counter.InstanceId.ToString());
        Assert.Equal("player",g.DecisionPlayerId);Assert.Equal("護主：選擇抵銷傷害",g.CurrentPendingChoice!.Title);
        Choose(g,"GUARD:1");Assert.False(g.IsWaiting);Assert.False(g.IsOver);
        Assert.Equal(7,g.Player.Hp);Assert.Equal(7,g.Computer.Hp);Assert.DoesNotContain(attacker,g.Player.Field);
    }
    [Fact] public void PreventingDamageDoesNotPreventDeckExhaustion()
    {
        var g=Scene();Unit(g.Player);g.Player.Deck.Clear();Assert.False(g.DrawCard(g.Player));
        Assert.True(g.IsOver);Assert.Equal(2,g.Player.AvailableEnergy);Assert.False(g.IsWaiting);
    }
}
