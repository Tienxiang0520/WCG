using LcgWeb.Models;
using LcgWeb.Services;
using Microsoft.AspNetCore.Hosting;
using Moq;
namespace LcgTests;

public class TargetCombatTests
{
    readonly CardDatabase db; readonly GameEngine e;
    public TargetCombatTests()
    {
        var env = new Mock<IWebHostEnvironment>(); env.Setup(x => x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../LcgWeb")));
        db = new(env.Object); e = new(db,new Random(9)); e.StartGame(db.PresetDecks[0],db.PresetDecks[1]);
        e.Player.Hand.Clear(); e.Computer.Hand.Clear(); e.Computer.IsAi=false;
        for(int i=0;i<15;i++) { e.Player.EnergyZone.Add(C("LCG-101"));e.Computer.EnergyZone.Add(C("LCG-101")); }
    }
    CardInstance C(string id)=>new(db.GetCard(id)!);
    CardInstance Hand(PlayerState p,string id) {var c=C(id);p.Hand.Add(c);return c;}
    MonsterInstance M(PlayerState p,string id,int? pp=null) {var m=new MonsterInstance(db.GetCard(id)!){HasSummoningSickness=false};if(pp.HasValue)m.CurrentPP=pp.Value;p.Field.Add(m);return m;}
    void ResolveChoices()
    {
        for(int i=0;e.IsWaiting&&i<100;i++)
            if(e.CurrentPendingChoice is {} c) Assert.True(e.SelectChoice(c.Options.FirstOrDefault(x=>x.Id=="KEEP")??c.Options[0]));
            else Assert.True(e.SelectTarget(e.Player.Field.Concat(e.Computer.Field).First(m=>e.CurrentPendingTarget!.Validator!(m))));
        Assert.False(e.IsWaiting);
    }
    void Shield(MonsterInstance m,PlayerState p)
    {var c=Hand(p,"LCG-062");Assert.True(e.CastSpell(p,c));Assert.True(e.SelectTarget(m));ResolveChoices();Assert.True(m.HasShield);}
    [Fact] public void StackedShieldsBlockOneCombatEachAndReturnOneEnergy()
    {
        var m=M(e.Player,"LCG-101",325);Shield(m,e.Player);Shield(m,e.Player);
        Assert.Equal(2,m.ShieldCount);Assert.Equal(2,e.Player.AttachedEnergy);var pool=e.Player.EnergyZone.Count;
        Assert.True(e.EndTurn());
        for(int left=1;left>=0;left--){var a=M(e.Computer,"LCG-051",1675);Assert.True(e.Attack(e.Computer,a,m));Assert.Contains(m,e.Player.Field);Assert.Equal(left,m.ShieldCount);Assert.Equal(++pool,e.Player.EnergyZone.Count);}
        Assert.True(e.Attack(e.Computer,M(e.Computer,"LCG-051",1675),m));Assert.DoesNotContain(m,e.Player.Field);
    }
    [Theory][InlineData("LCG-072")][InlineData("LCG-026")][InlineData("LCG-082")]
    public void SilenceBounceAndDestroyReturnAllLayers(string spell)
    {
        var m=M(e.Player,"LCG-101",325);Shield(m,e.Player);Shield(m,e.Player);Shield(m,e.Player);
        var layers=m.ShieldEnergies.ToArray();Assert.Equal(3,m.ShieldCount);Assert.True(e.EndTurn());
        Assert.True(e.CastSpell(e.Computer,Hand(e.Computer,spell)));Assert.True(e.SelectTarget(m));
        Assert.Equal(0,m.ShieldCount);Assert.All(layers,x=>{Assert.Contains(x,e.Player.EnergyZone);Assert.False(x.IsTapped);});
    }
    [Theory][InlineData(6,0)][InlineData(7,1)]
    public void NativeShieldNeedsExtraUprightEnergyAndWarnsBeforePlay(int energy,int shields)
    {
        e.Player.EnergyZone=e.Player.EnergyZone.Take(energy).ToList();var c=Hand(e.Player,"LCG-079");
        var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../LcgWeb")));
        var bridge=new BattleBridge(e,new DeckService(db,env.Object));var h=Assert.Single(bridge.Snapshot().Hand);
        Assert.Equal(energy==6,!string.IsNullOrEmpty(h.Warning));Assert.True(h.CanPlay);
        Assert.True(e.SummonMonster(e.Player,c));ResolveChoices();Assert.Equal(shields,Assert.Single(e.Player.Field).ShieldCount);
    }
    [Fact] public void TwoTargetShieldGrantCannotChooseSameMonsterTwice()
    {
        var m=M(e.Player,"LCG-101");Shield(m,e.Player);
        Assert.True(e.SummonMonster(e.Player,Hand(e.Player,"LCG-073")));ResolveChoices();
        Assert.Equal(2,m.ShieldCount); // one existing layer plus one from this two-target effect
    }
    [Theory][InlineData(1325,675,false)][InlineData(675,1325,true)][InlineData(1000,1000,true)]
    public void FixedPPComparison(int a,int b,bool attackerDies)
    {var x=M(e.Player,"LCG-101",a);var y=M(e.Computer,"LCG-101",b);Assert.True(e.Attack(e.Player,x,y));Assert.Equal(attackerDies,!e.Player.Field.Contains(x));Assert.Equal(b<=a,!e.Computer.Field.Contains(y));Assert.Equal(7,e.Computer.Hp);}
    [Fact] public void TauntAttackAndFaceRestrictions()
    {var a=M(e.Player,"LCG-065");var t=M(e.Computer,"LCG-081");var other=M(e.Computer,"LCG-101");Assert.True(e.CanAttack(a));Assert.False(e.Attack(e.Player,a));Assert.False(e.Attack(e.Player,a,other));Assert.False(a.HasAttacked);Assert.True(e.Attack(e.Player,a,t));Assert.False(e.Attack(e.Player,a));}
    [Fact] public void AttackFaceThenSpellThenAnotherAttack()
    {var a=M(e.Player,"LCG-101");var b=M(e.Player,"LCG-101");Assert.True(e.Attack(e.Player,a));Assert.True(e.CastSpell(e.Player,Hand(e.Player,"LCG-014")));Assert.True(e.Attack(e.Player,b));Assert.Equal(4,e.Computer.Hp);}
    [Fact] public void ForeignSickAndFrozenAttackersCannotSpendAttack()
    {var a=M(e.Player,"LCG-101");a.HasSummoningSickness=true;Assert.False(e.Attack(e.Player,a));a.HasSummoningSickness=false;a.FrozenUntilTurn=3;Assert.False(e.Attack(e.Player,a));Assert.False(e.Attack(e.Player,M(e.Computer,"LCG-101")));Assert.False(a.HasAttacked);}
    [Fact] public void ChargeAndRagnarosEligibility()
    {var a=M(e.Player,"LCG-005");a.HasSummoningSickness=true;Assert.True(e.CanAttack(a));var r=M(e.Player,"LCG-120");Assert.False(e.CanAttack(r));Assert.False(r.IsTaunt);}
    [Fact] public void DefenderCanFightRepeatedlyWithoutConsumingItsAttack()
    {var a=M(e.Player,"LCG-101",325);var b=M(e.Player,"LCG-101",325);var d=M(e.Computer,"LCG-101",1325);Assert.True(e.Attack(e.Player,a,d));Assert.True(e.Attack(e.Player,b,d));Assert.False(d.HasAttacked);Assert.Contains(d,e.Computer.Field);}
    [Fact] public void StealthIsDynamicAndAoEStillKills()
    {var s=M(e.Computer,"LCG-111");var a=M(e.Player,"LCG-101");Assert.DoesNotContain(s,e.GetAttackTargets(a));Assert.False(e.CastSpell(e.Player,Hand(e.Player,"LCG-026")));Assert.True(e.CastSpell(e.Player,Hand(e.Player,"LCG-098")));Assert.Empty(e.Computer.Field);}
    [Fact] public void StealthPermanentlyBreaksOnDeclaredAttack()
    {var s=M(e.Player,"LCG-111");Assert.True(e.Attack(e.Player,s));Assert.False(s.IsStealthed);Assert.True(e.EndTurn());var bounce=Hand(e.Computer,"LCG-026");Assert.True(e.CastSpell(e.Computer,bounce));Assert.True(e.SelectTarget(s));Assert.DoesNotContain(s,e.Player.Field);}
    [Fact] public void FrozenTauntRemainsRequiredAndThawsAfterItsNextTurn()
    {var t=M(e.Computer,"LCG-081");var a=M(e.Player,"LCG-101");Assert.True(e.CastSpell(e.Player,Hand(e.Player,"LCG-024")));Assert.True(e.SelectTarget(t));Assert.True(t.IsFrozen);Assert.False(e.CanAttackPlayer(a));Assert.True(e.EndTurn());Assert.True(t.IsFrozen);Assert.False(e.CanAttack(t));Assert.True(e.EndTurn());Assert.False(t.IsFrozen);}
    [Fact] public void NativeShieldOptionalAndReservedEnergyStaysUnavailable()
    {var c=Hand(e.Player,"LCG-063");Assert.True(e.SummonMonster(e.Player,c));Assert.All(e.CurrentPendingChoice!.Options,o=>Assert.Null(o.PreviewCard));ResolveChoices();var m=Assert.Single(e.Player.Field);Assert.True(m.HasShield);Assert.Equal(13,e.Player.AvailableEnergy);Assert.Equal(15,e.Player.TotalEnergy);Assert.True(e.EndTurn());Assert.True(e.EndTurn());Assert.Equal(14,e.Player.AvailableEnergy);Assert.True(m.HasShield);}
    [Fact] public void ShieldBreaksOnceForPPAndPoisonCombinedAndReturnsUpright()
    {var a=M(e.Player,"LCG-101",325);Shield(a,e.Player);var energy=a.ShieldEnergies[0];var p=M(e.Computer,"LCG-051",1675);Assert.True(e.Attack(e.Player,a,p));Assert.Contains(a,e.Player.Field);Assert.False(a.HasShield);Assert.Contains(energy!,e.Player.EnergyZone);Assert.False(energy!.IsTapped);}
    [Fact] public void EqualPPWithBothShieldsLeavesBothAlive()
    {var a=M(e.Player,"LCG-101",1000);Shield(a,e.Player);Assert.True(e.EndTurn());var b=M(e.Computer,"LCG-101",1000);Shield(b,e.Computer);Assert.True(e.Attack(e.Computer,b,a));Assert.Contains(a,e.Player.Field);Assert.Contains(b,e.Computer.Field);Assert.False(a.HasShield);Assert.False(b.HasShield);}
    [Fact] public void ShieldDoesNotPreventSpellDestroyOrBounce()
    {var a=M(e.Player,"LCG-101");Shield(a,e.Player);Assert.True(e.EndTurn());Assert.True(e.CastSpell(e.Computer,Hand(e.Computer,"LCG-082")));Assert.True(e.SelectTarget(a));Assert.Empty(e.Player.Field);Assert.Equal(13,e.Player.AvailableEnergy);}
    [Fact] public void ShieldSpellRejectsInsufficientExtraEnergyBeforePaying()
    {M(e.Player,"LCG-101");e.Player.EnergyZone.RemoveRange(2,13);var c=Hand(e.Player,"LCG-062");Assert.False(e.CastSpell(e.Player,c));Assert.Contains(c,e.Player.Hand);Assert.Equal(2,e.Player.AvailableEnergy);}
    [Fact] public void SilenceAttachesSpellRemovesShieldAndTauntButKeepsFreeze()
    {var m=M(e.Player,"LCG-069");Shield(m,e.Player);m.FrozenUntilTurn=3;Assert.True(e.EndTurn());var spell=Hand(e.Computer,"LCG-072");Assert.True(e.CastSpell(e.Computer,spell));Assert.True(e.SelectTarget(m));Assert.True(m.IsSilenced);Assert.False(m.IsTaunt);Assert.False(m.HasShield);Assert.True(m.IsFrozen);Assert.Equal(1825,m.CurrentPP);Assert.Same(spell,m.SilenceSpell);Assert.DoesNotContain(spell,e.Computer.Graveyard);Assert.True(e.CastSpell(e.Computer,Hand(e.Computer,"LCG-026")));Assert.True(e.SelectTarget(m));Assert.Contains(spell,e.Computer.Graveyard);Assert.DoesNotContain(spell,e.Player.Hand);}
    [Fact] public void SilencedDeathAndContinuousAbilitiesDoNotTrigger()
    {var m=M(e.Computer,"LCG-003");Assert.True(e.CastSpell(e.Player,Hand(e.Player,"LCG-072")));Assert.True(e.SelectTarget(m));Assert.True(e.CastSpell(e.Player,Hand(e.Player,"LCG-098")));Assert.Equal(7,e.Player.Hp);Assert.Empty(e.Computer.Field);}
    [Fact] public void TrampleOnlyOnAttackingAndSurvivingActualKill()
    {var a=M(e.Player,"LCG-053");var b=M(e.Computer,"LCG-101");Assert.True(e.Attack(e.Player,a,b));Assert.Equal(6,e.Computer.Hp);Assert.True(e.EndTurn());var c=M(e.Computer,"LCG-101",325);Assert.True(e.Attack(e.Computer,c,a));Assert.Equal(6,e.Computer.Hp);}
    [Fact] public void SurvivingIceMageFreezesOnlySurvivingOpponent()
    {var a=M(e.Player,"LCG-029");Shield(a,e.Player);var b=M(e.Computer,"LCG-101",2000);Assert.True(e.Attack(e.Player,a,b));Assert.True(b.IsFrozen);Assert.Contains(a,e.Player.Field);}
    [Fact] public void TyrantIgnoresOnlyLowPPTaunt()
    {var a=M(e.Player,"LCG-059");M(e.Computer,"LCG-081");Assert.True(e.CanAttackPlayer(a));var high=M(e.Computer,"LCG-065");Assert.False(e.CanAttackPlayer(a));Assert.Equal(high,Assert.Single(e.GetAttackTargets(a)));}
    [Fact] public void RequiredChoicesBlockAttackAndEndTurnAndStaleCommands()
    {var a=M(e.Player,"LCG-101");M(e.Computer,"LCG-101");var c=Hand(e.Player,"LCG-026");var revision=e.Revision;Assert.True(e.CastSpell(e.Player,c));Assert.False(e.Attack(e.Player,a));Assert.False(e.EndTurn());Assert.Equal("stale",e.ExecuteCommand("player",e.MatchId,revision,()=>e.EndTurn()).Code);Assert.True(e.CancelTargetOrChoice());Assert.Equal(15,e.Player.AvailableEnergy);}
    [Fact] public void AllPresetPairsCompleteAndConserveActualCards()
    {
        foreach(var a in db.PresetDecks) foreach(var b in db.PresetDecks) for(int seed=0;seed<3;seed++)
        {
            var engine=new GameEngine(db,new Random(seed));engine.StartGame(a,b,seed%2==0);engine.Player.IsAi=true;
            for(int step=0;step<4000&&!engine.IsOver;step++) Assert.True(engine.ExecuteAiStep(),a.Name+" vs "+b.Name+": "+engine.LastError);
            Assert.True(engine.IsOver,a.Name+" vs "+b.Name);
            Assert.Equal(100,new[]{engine.Player,engine.Computer}.Sum(p=>p.Deck.Count+p.Hand.Count+p.EnergyZone.Count+p.Graveyard.Count+p.Field.Count+p.Field.Sum(m=>m.ShieldCount)+p.Field.Count(m=>m.SilenceSpell!=null)));
        }
    }
    [Theory]
    [InlineData(1675, 1000, false, false, false, false)]
    [InlineData(1000, 1675, false, false, false, false)]
    [InlineData(1000, 1000, false, false, false, false)]
    [InlineData(1000, 1675, true, false, false, false)]
    [InlineData(1000, 1000, true, true, true, true)]
    [InlineData(1000, 1675, false, false, true, false)]
    [InlineData(1675, 1000, true, true, false, true)]
    public void PreviewMatchesCombatWithoutMutating(int aPp, int dPp, bool aShield, bool dShield, bool aPoison, bool dPoison)
    {
        var a = M(e.Player, aPoison ? "LCG-089" : "LCG-101", aPp);
        var d = M(e.Computer, dPoison ? "LCG-089" : "LCG-101", dPp);
        if (aShield) Shield(a, e.Player);
        if (dShield) { var reserved = e.Computer.EnergyZone[0]; e.Computer.EnergyZone.Remove(reserved); d.ShieldEnergies.Add(reserved); d.ShieldOwnerId = e.Computer.Id; }
        var revision = e.Revision; var energy = e.Player.AvailableEnergy;
        var preview = e.PreviewAttack(a, d)!;
        Assert.NotNull(preview); Assert.Equal(revision, e.Revision); Assert.Equal(energy, e.Player.AvailableEnergy); Assert.False(a.HasAttacked);
        Assert.True(e.Attack(e.Player, a, d));
        Assert.Equal(preview.AttackerDies, !e.Player.Field.Contains(a));
        Assert.Equal(preview.DefenderDies, !e.Computer.Field.Contains(d));
        Assert.Equal(aShield && !preview.AttackerShieldBreaks, a.HasShield);
        Assert.Equal(dShield && !preview.DefenderShieldBreaks, d.HasShield);
    }
    [Fact] public void PreviewRejectsForbiddenTargetsAndPredictsTrampleAndFace()
    {
        var a = M(e.Player, "LCG-053"); var d = M(e.Computer, "LCG-101", 325);
        Assert.Equal(1, e.PreviewAttack(a, d)!.PlayerDamage);
        Assert.Equal(a.CurrentDP, e.PreviewAttack(a)!.PlayerDamage);
        var taunt = M(e.Computer, "LCG-065");
        Assert.Null(e.PreviewAttack(a)); Assert.Null(e.PreviewAttack(a, d)); Assert.NotNull(e.PreviewAttack(a, taunt));
        taunt.IsStealthed = true; Assert.Null(e.PreviewAttack(a, taunt));
        a.IsSilenced = true; Assert.Equal(0, e.PreviewAttack(a, d)!.PlayerDamage);
        a.HasAttacked = true; Assert.Null(e.PreviewAttack(a, d));
    }
}
