using WcgWeb.Models;
using WcgWeb.Services;
using Microsoft.AspNetCore.Hosting;
using Moq;
namespace WcgTests;
public class TargetCombatTests
{
    readonly CardDatabase db;readonly GameEngine e;
    public TargetCombatTests(){var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));db=new(env.Object);e=new(db);e.StartGame(db.PresetDecks[0],db.PresetDecks[1]);e.Player.Hand.Clear();e.Computer.Hand.Clear();}
    MonsterInstance Unit(PlayerState p,string id,int pp){var m=new MonsterInstance(db.GetCard(id)!){IsTapped=false,HasSummoningSickness=false,CurrentPP=pp};p.Field.Add(m);return m;}
    [Theory][InlineData(1000,1000,true,true)][InlineData(1500,1000,false,true)][InlineData(1000,1500,true,false)]
    public void FixedPPCombatRemovesCorrectUnits(int ap,int dp,bool adead,bool ddead){var a=Unit(e.Player,"WCG-101",ap);var d=Unit(e.Computer,"WCG-101",dp);Assert.True(e.Attack(e.Player,a,d));Assert.Equal(adead,!e.Player.Field.Contains(a));Assert.Equal(ddead,!e.Computer.Field.Contains(d));}
    [Fact]public void TauntRemainsWhileTappedButIsRemovedBySilence(){var a=Unit(e.Player,"WCG-101",1000);var d=Unit(e.Computer,"WCG-081",1000);d.IsTapped=true;Assert.False(e.CanAttackPlayer(a));d.IsSilenced=true;Assert.True(e.CanAttackPlayer(a));}
    [Fact]public void DefenderDoesNotTapFromBeingAttacked(){var a=Unit(e.Player,"WCG-101",500);var d=Unit(e.Computer,"WCG-101",1500);Assert.True(e.Attack(e.Player,a,d));Assert.False(d.IsTapped);}
    [Fact]public void ShieldIsOptionalAndDecliningSpendsNoEnergy(){var a=Unit(e.Player,"WCG-101",2000);var d=Unit(e.Computer,"WCG-063",800);e.Computer.EnergyZone.Add(new(db.GetCard("WCG-101")!));Assert.True(e.Attack(e.Player,a,d));Assert.Equal("computer",e.DecisionPlayerId);Assert.True(e.SelectChoice(e.CurrentPendingChoice!.Options.Single(o=>o.Id=="SKIP")));Assert.DoesNotContain(d,e.Computer.Field);Assert.Equal(1,e.Computer.AvailableEnergy);}
    [Fact]public void PoisonAndPPTogetherRequireOnlyOneShieldPayment(){var a=Unit(e.Player,"WCG-051",3000);var d=Unit(e.Computer,"WCG-063",800);e.Computer.EnergyZone.Add(new(db.GetCard("WCG-101")!));Assert.True(e.Attack(e.Player,a,d));Assert.True(e.SelectChoice(e.CurrentPendingChoice!.Options.Single(o=>o.Id=="SHIELD")));Assert.Contains(d,e.Computer.Field);Assert.Equal(0,e.Computer.AvailableEnergy);Assert.Empty(d.ShieldEnergies);}
    [Fact]public void NoEnergyMeansQualifiedShieldDoesNotSave(){var a=Unit(e.Player,"WCG-101",2000);var d=Unit(e.Computer,"WCG-063",800);Assert.True(e.Attack(e.Player,a,d));Assert.DoesNotContain(d,e.Computer.Field);Assert.False(e.IsWaiting);}
    [Theory][InlineData(1500,1)][InlineData(1600,0)]public void TrampleUses700Margin(int defender,int hit){var a=Unit(e.Player,"WCG-053",2200);var d=Unit(e.Computer,"WCG-101",defender);Assert.True(e.Attack(e.Player,a,d));Assert.Equal(7-hit,e.Computer.Hp);}
    [Fact]public void AttackerTapsAfterFaceAttack(){var a=Unit(e.Player,"WCG-101",1000);Assert.True(e.Attack(e.Player,a));Assert.True(a.IsTapped);Assert.False(e.CanAttack(a));}
}
