using WcgWeb.Models;
using WcgWeb.Services;
using Microsoft.AspNetCore.Hosting;
using Moq;
namespace WcgTests;
public class V06RulesTests
{
    private readonly CardDatabase cards;
    public V06RulesTests(){var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));cards=new(env.Object);}
    private GameEngine Game(){var g=new GameEngine(cards,new Random(17));g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1]);g.Player.Hand.Clear();g.Computer.Hand.Clear();for(int i=0;i<12;i++)g.Player.EnergyZone.Add(new(cards.GetCard("WCG-101")!));return g;}
    private CardInstance Hand(GameEngine g,int n){var c=new CardInstance(cards.GetCard($"WCG-{n:000}")!);g.Player.Hand.Add(c);return c;}
    private MonsterInstance Unit(PlayerState p,int n,int slot,bool tapped=false){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=tapped,HasSummoningSickness=false};p.Field.Add(m);return m;}
    private void Finish(GameEngine g, int slot=2){for(int i=0;i<100&&g.IsWaiting&&!g.IsOver;i++){if(g.CurrentPendingChoice is {} c){var o=c.Options.FirstOrDefault(o=>o.Id==$"SLOT:{slot}")??c.Options.FirstOrDefault(o=>o.Id=="SHIELD")??c.Options.FirstOrDefault(o=>o.Id=="KEEP")??c.Options.FirstOrDefault(o=>o.Id!="SKIP")??c.Options[0];Assert.True(g.SelectChoice(o));}else{var t=g.CurrentPendingTarget!;var m=g.Player.Board.Concat(g.Computer.Board).First(x=>t.Validator!(x));Assert.True(g.SelectTarget(m));}}Assert.False(g.IsWaiting);}
    [Fact]public void CatalogAndPresetsAreValid(){Assert.Equal(199,cards.AllCards.Count);Assert.Equal(8,cards.AllCards.Count(c=>c.IsEnchantment));Assert.Equal(5,cards.AllCards.Count(c=>c.IsCounter));Assert.All(cards.PresetDecks,d=>Assert.True(d.IsValid(cards.GetCard,out _)));}
    [Fact]public void PlacementIsChosenBeforePaymentAndPersistsAcrossDeath(){var g=Game();var c=Hand(g,61);Assert.True(g.SummonMonster(g.Player,c));Assert.Equal(12,g.Player.AvailableEnergy);Finish(g,3);Assert.Equal(3,g.Player.Field.Single().Slot);Assert.Equal(10,g.Player.AvailableEnergy);Assert.True(g.Player.Field.Single().IsTapped);}
    [Fact]public void ReadyRestoresAttackIncludingSummoningSickness(){var g=Game();var c=Hand(g,167);var m=Unit(g.Player,156,2,true);m.HasSummoningSickness=true;Assert.True(g.CastSpellAt(g.Player,c,m));Assert.True(g.CanAttack(m));Assert.True(g.Attack(g.Player,m));Assert.True(m.IsTapped);c=Hand(g,167);Assert.True(g.CastSpellAt(g.Player,c,m));Assert.True(g.CanAttack(m));}
    [Fact]public void ArrowQualificationCostsEnergyEveryTimeAndDoesNotRotate(){var g=Game();var src=Unit(g.Player,61,1,true);var target=Unit(g.Player,101,2);var foe=Unit(g.Computer,119,0);for(int i=0;i<2;i++)g.Computer.EnergyZone.Add(new(cards.GetCard("WCG-101")!));Assert.True(g.Attack(g.Player,target,foe));Finish(g);Assert.Contains(target,g.Player.Field);Assert.Equal(11,g.Player.AvailableEnergy);src.IsTapped=false;Assert.True(g.CastSpellAt(g.Player,Hand(g,167),target));Assert.True(target.HasShield);Assert.True(g.Attack(g.Player,target,foe));Finish(g);Assert.Equal(10,g.Player.AvailableEnergy);}
    [Fact]public void LegacyEnemyFacingArrowUsesEnemyEnergy(){var g=Game();CardTextClarityTests.Legacy(g);Unit(g.Player,147,1);var foe=Unit(g.Computer,101,3);g.Computer.EnergyZone.Add(new(cards.GetCard("WCG-101")!));var a=Unit(g.Player,119,4);Assert.True(g.Attack(g.Player,a,foe));Finish(g);Assert.Contains(foe,g.Computer.Field);Assert.Equal(0,g.Computer.AvailableEnergy);Assert.Equal(12,g.Player.AvailableEnergy);}
    [Fact]public void SilenceAndDispelRestoreArrowButKeepExternalBuff(){var g=Game();var a=Unit(g.Player,61,1);var t=Unit(g.Player,101,2);Assert.True(g.CastSpellAt(g.Player,Hand(g,135),t));Assert.Equal(1200,t.CurrentPP);Assert.True(g.CastSpellAt(g.Player,Hand(g,72),a));Assert.False(t.HasShield);var dispel=Hand(g,134);Assert.True(g.SummonMonster(g.Player,dispel));Finish(g,0);Assert.False(a.IsSilenced);Assert.True(t.HasShield);}
    [Fact]public void DynamicShieldTracksCurrentHp(){var g=Game();g.Player.Hp=3;var m=Unit(g.Player,133,2);Assert.True(g.CastSpell(g.Player,Hand(g,121)));Assert.True(m.HasShield);Assert.True(g.CastSpell(g.Player,Hand(g,70)));Assert.False(m.HasShield);}
    [Fact]public void AttachmentsCanStackAndReturnToTheirOwnerOnBounce(){var g=Game();CardTextClarityTests.Legacy(g);var m=Unit(g.Computer,101,2);var c=Hand(g,141);Assert.True(g.CastSpellAt(g.Player,c,m));Assert.Equal(1400,m.CurrentPP);Assert.True(g.CastSpellAt(g.Player,Hand(g,139),m));Assert.Contains(c,g.Player.Graveyard);Assert.DoesNotContain(c,g.Computer.Hand);}
    [Fact]public void NoFreezeTapOrBounceBranchesUseOriginalState(){var g=Game();var m=Unit(g.Computer,119,1);Assert.True(g.CastSpellAt(g.Player,Hand(g,137),m));Assert.True(m.IsTapped);Assert.Contains(m,g.Computer.Field);Assert.True(g.CastSpellAt(g.Player,Hand(g,137),m));Assert.DoesNotContain(m,g.Computer.Field);Assert.Contains(g.Computer.Hand,c=>c.InstanceId==m.InstanceId);}
    [Fact]public void EnchantmentAndSetShareSlotsAndHideIdentity(){var g=Game();Assert.True(g.PlayEnchantment(g.Player,Hand(g,126)));Finish(g,1);Assert.True(g.Activate(g.Player,g.Player.Structures.Single()));Assert.Equal(1,g.Player.NextCreatureDiscount);Assert.True(g.SummonMonster(g.Player,Hand(g,156)));Finish(g,2);Assert.Equal(0,g.Player.NextCreatureDiscount);Assert.True(g.SetCard(g.Player,Hand(g,195)));Finish(g,3);Assert.Equal(3,g.Player.Occupied);}
    [Fact]public void CounterStopsAttackAndLeavesAttackSpent(){var g=Game();var attacker=Unit(g.Player,156,1);var counter=new MonsterInstance(cards.GetCard("WCG-196")!){IsSet=true,Slot=2,IsTapped=false};g.Computer.Structures.Add(counter);for(int i=0;i<2;i++)g.Computer.EnergyZone.Add(new(cards.GetCard("WCG-101")!));Assert.True(g.Attack(g.Player,attacker));Finish(g);Assert.Equal(7,g.Computer.Hp);Assert.True(attacker.IsTapped);Assert.Empty(g.Computer.Structures);Assert.Contains(g.Computer.Graveyard,c=>c.Card.Id=="WCG-196");}
    [Fact]public void TwoFactionsHaveNoTwelveCardLimitButThirdIsRejected(){var d=new Deck{MainWill="狂怒",CardIds=Enumerable.Range(1,13).SelectMany(i=>Enumerable.Repeat($"WCG-{i:000}",4)).Take(50).ToList()};for(int i=0;i<20;i++)d.CardIds[i]=$"WCG-{21+i/4:000}";Assert.True(d.IsValid(cards.GetCard,out _));d.CardIds[20]="WCG-041";Assert.False(d.IsValid(cards.GetCard,out _));}
    [Fact]public void CurrentMissingHpSetsActualCost(){var g=Game();g.Player.Hp=3;Assert.Equal(5,g.ActualCost(g.Player,cards.GetCard("WCG-160")!));g.Player.Hp=6;Assert.Equal(8,g.ActualCost(g.Player,cards.GetCard("WCG-160")!));}
    [Fact]public void DeployedBuffOnlyIncludesPresentRecipientsAndStacks(){var g=Game();var first=Unit(g.Player,3,0);Assert.True(g.SummonMonster(g.Player,Hand(g,181)));Finish(g,1);Assert.Equal(800,first.CurrentPP);Assert.True(g.SummonMonster(g.Player,Hand(g,181)));Finish(g,2);Assert.Equal(1100,first.CurrentPP);var later=Hand(g,3);Assert.True(g.SummonMonster(g.Player,later));Finish(g,3);Assert.Equal(500,g.Player.Field.Single(x=>x.InstanceId==later.InstanceId).CurrentPP);}
    [Theory][InlineData(195)][InlineData(196)][InlineData(197)][InlineData(198)][InlineData(199)]public void AllCountersResolveThroughAttack(int id){var g=Game();var a=Unit(g.Player,156,0);g.Computer.Structures.Add(new(cards.GetCard($"WCG-{id}")!){Slot=1,IsSet=true});Unit(g.Computer,101,2);for(int i=0;i<3;i++)g.Computer.EnergyZone.Add(new(cards.GetCard("WCG-101")!));Assert.True(g.Attack(g.Player,a));Finish(g);Assert.False(g.IsWaiting);Assert.Equal(7,g.Computer.Hp);}
    [Fact]public void EveryCardCanResolveWithoutUnimplementedEffects()
    {
        foreach(var card in cards.AllCards.Where(c=>!c.IsCounter))
        {
            var g=Game();g.Player.Hp=3;Unit(g.Player,81,0);Unit(g.Player,43,1,true);Unit(g.Computer,101,0,true);Unit(g.Computer,119,1);g.Player.Graveyard.Add(new(cards.GetCard("WCG-083")!));g.Player.Graveyard.Add(new(cards.GetCard("WCG-121")!));
            var c=new CardInstance(card);g.Player.Hand.Add(c);g.Player.Hand.Add(new(cards.GetCard("WCG-101")!));g.Player.Hand.Add(new(cards.GetCard("WCG-101")!));
            var result=card.IsMonster?g.SummonMonster(g.Player,c):card.IsEnchantment?g.PlayEnchantment(g.Player,c):g.CastSpell(g.Player,c);
            if(result)Finish(g,2);else Assert.NotEmpty(g.GetPlayProblem(g.Player,c));
        }
    }
    [Fact]public void AllPresetMatchupsFinishWithLegalSlotsAndNoStalledChoices()
    {
        foreach(var p in cards.PresetDecks)foreach(var e in cards.PresetDecks)
        {var g=new GameEngine(cards,new Random(39));g.StartGame(p,e);g.Player.IsAi=true;g.Computer.IsAi=true;
            for(int i=0;i<3000&&!g.IsOver;i++){Assert.True(g.ExecuteAiStep(),$"{p.Name}/{e.Name} stalled at {g.TurnNumber} {g.CurrentPendingChoice?.Title}");foreach(var side in new[]{g.Player,g.Computer}){Assert.InRange(side.Occupied,0,5);Assert.Equal(side.Occupied,side.Board.Select(m=>m.Slot).Distinct().Count());}}
            Assert.True(g.IsOver,$"{p.Name}/{e.Name} did not finish");}
    }
    [Fact]public void PassiveAltarTriggersForEveryCombatWithoutTapping()
    {
        var g=Game();var altar=new MonsterInstance(cards.GetCard("WCG-124")!){Slot=2,IsTapped=false};g.Player.Structures.Add(altar);
        var a=Unit(g.Player,119,0);var b=Unit(g.Player,119,1);var x=Unit(g.Computer,101,0);var y=Unit(g.Computer,101,1);
        Assert.True(g.Attack(g.Player,a,x));Finish(g);Assert.True(g.Attack(g.Player,b,y));Finish(g);
        Assert.Equal(5,g.Computer.Hp);Assert.False(altar.IsTapped);Assert.Equal(0,altar.TriggersThisTurn);Assert.Equal(3,altar.Card.TotalCost);
    }
    [Fact]public void PassiveEchoRecoversOnEveryQualifyingSpellWithoutTapping()
    {
        var g=Game();var tower=new MonsterInstance(cards.GetCard("WCG-153")!){Slot=2,IsTapped=false};g.Player.Structures.Add(tower);var m=Unit(g.Player,101,0);
        g.Player.Graveyard.Add(new(cards.GetCard("WCG-014")!));g.Player.Graveyard.Add(new(cards.GetCard("WCG-121")!));
        for(int i=0;i<2;i++){Assert.True(g.CastSpellAt(g.Player,Hand(g,62),m));Finish(g);Assert.Equal(i+1,g.Player.Hand.Count);}
        Assert.Empty(g.Player.Graveyard);Assert.False(tower.IsTapped);Assert.Equal(0,tower.TriggersThisTurn);Assert.Equal(4,tower.Card.TotalCost);
    }
    [Fact]public void MultipleEchoTowersRecheckGraveyardBeforeEachRecovery()
    {
        var g=Game();for(int i=1;i<=2;i++)g.Player.Structures.Add(new(cards.GetCard("WCG-153")!){Slot=i});var m=Unit(g.Player,101,0);
        var recovered=new CardInstance(cards.GetCard("WCG-014")!);g.Player.Graveyard.Add(recovered);
        Assert.True(g.CastSpellAt(g.Player,Hand(g,62),m));Finish(g);Assert.Single(g.Player.Hand);Assert.Same(recovered,g.Player.Hand[0]);Assert.Empty(g.Player.Graveyard);
        g.Player.Graveyard.Add(new(cards.GetCard("WCG-121")!));Assert.True(g.CastSpellAt(g.Player,Hand(g,62),m));Finish(g);Assert.Equal(2,g.Player.Hand.Count);
    }
    [Fact]public void ArcaneGuardTriggersOnThirdSpellWithoutCountingOrTapping()
    {
        var g=Game();var guard=Unit(g.Player,185,4);Hand(g,101);
        for(int i=0;i<3;i++)Unit(g.Computer,101,i);
        for(int i=0;i<3;i++){Assert.True(g.CastSpell(g.Player,Hand(g,166)));Finish(g);Assert.Equal(2-i,g.Computer.Field.Count);}
        Assert.False(guard.IsTapped);Assert.Equal(0,guard.TriggersThisTurn);Assert.Equal(5,guard.Card.TotalCost);
    }
    [Fact]public void SilverFlagHealsOnItsEntryAndEveryLaterOrderSummon()
    {
        var g=Game();g.Player.Hp=1;
        Assert.True(g.SummonMonster(g.Player,Hand(g,190)));Finish(g,0);var flag=g.Player.Field.Single();Assert.Equal(2,g.Player.Hp);
        for(int i=0;i<3;i++){Assert.True(g.SummonMonster(g.Player,Hand(g,129)));Finish(g,i+1);Assert.Equal(3+i,g.Player.Hp);}
        Assert.Equal(0,flag.TriggersThisTurn);Assert.Equal(4,flag.Card.TotalCost);
        g.Player.Hp=7;Assert.True(g.SummonMonster(g.Player,Hand(g,129)));Finish(g,4);Assert.Equal(7,g.Player.Hp);
    }
    [Fact]public void SilencedFactionSourcesDoNotTrigger()
    {
        var g=Game();g.Player.Hp=1;var guard=Unit(g.Player,185,3);var flag=Unit(g.Player,190,4);guard.IsSilenced=flag.IsSilenced=true;
        Unit(g.Computer,101,0);Hand(g,101);Assert.True(g.CastSpell(g.Player,Hand(g,166)));Finish(g);Assert.Single(g.Computer.Field);
        Assert.True(g.SummonMonster(g.Player,Hand(g,129)));Finish(g,0);Assert.Equal(1,g.Player.Hp);
    }
}
