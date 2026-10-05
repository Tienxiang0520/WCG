using LcgWeb.Models;
using LcgWeb.Services;
using Microsoft.AspNetCore.Hosting;
using Moq;
namespace LcgTests;

public class InteractionRegressionTests
{
    readonly CardDatabase db;
    readonly GameEngine e;
    public InteractionRegressionTests()
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(x => x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../LcgWeb")));
        db = new(env.Object); e = new(db, new Random(7));
        e.StartGame(db.PresetDecks[0], db.PresetDecks[1]); e.Computer.IsAi = false;
        e.Player.Hand.Clear(); e.Computer.Hand.Clear();
        Energy(e.Player); Energy(e.Computer);
    }
    CardInstance Card(string id) => new(db.GetCard(id)!);
    void Energy(PlayerState p) { for (int i=0;i<12;i++) p.EnergyZone.Add(Card("LCG-101")); }
    CardInstance Hand(PlayerState p,string id) { var c=Card(id);p.Hand.Add(c);return c; }
    MonsterInstance Field(PlayerState p,string id,int? pp=null)
    { var m=new MonsterInstance(db.GetCard(id)!){HasSummoningSickness=false};if(pp!=null)m.CurrentPP=pp.Value;p.Field.Add(m);return m; }
    void Choices()
    {
        for(int i=0;e.IsWaiting && i<100;i++)
        {
            if(e.CurrentPendingChoice is {} c) Assert.True(e.SelectChoice(c.Options.FirstOrDefault(x=>x.Id=="KEEP")??c.Options.First()));
            else { var t=e.CurrentPendingTarget!;Assert.True(e.SelectTarget(e.Player.Field.Concat(e.Computer.Field).First(m=>t.Validator!(m)))); }
        }
        Assert.False(e.IsWaiting);
    }
    [Fact] public void TargetCancelAndForeignSelectionNeverPay()
    {
        var c=Hand(e.Player,"LCG-026"); var m=Field(e.Computer,"LCG-101");
        Assert.True(e.CastSpell(e.Player,c));Assert.False(e.SelectTarget(new MonsterInstance(m.Card)));
        Assert.True(e.CancelTargetOrChoice());Assert.Contains(c,e.Player.Hand);Assert.Equal(12,e.Player.AvailableEnergy);Assert.Contains(m,e.Computer.Field);
        Assert.True(e.CastSpell(e.Player,c));Assert.True(e.SelectTarget(m));Assert.DoesNotContain(m,e.Computer.Field);Assert.Equal(12-c.Card.TotalCost,e.Player.AvailableEnergy);
    }

    [Fact] public void ExtraDiscardRejectsBeforePaymentAndExcludesSource()
    {
        var c=Hand(e.Player,"LCG-095");Hand(e.Player,"LCG-101");Assert.False(e.SummonMonster(e.Player,c));Assert.Equal(12,e.Player.AvailableEnergy);
        Hand(e.Player,"LCG-101");Assert.True(e.SummonMonster(e.Player,c));Assert.Empty(e.Player.Hand);Assert.Equal(2,e.Player.Graveyard.Count);Assert.Single(e.Player.Field);Assert.Equal(c.Card.Id,e.Player.Field[0].Card.Id);
    }
    [Fact] public void LifeCostAtOneLosesBeforeDrawing()
    {
        e.Player.Hp=1;Field(e.Player,"LCG-101");var c=Hand(e.Player,"LCG-096");int deck=e.Player.Deck.Count;
        Assert.True(e.CastSpell(e.Player,c));Assert.True(e.SelectTarget(e.Player.Field[0]));Assert.True(e.IsOver);Assert.True(e.Player.HasLost);Assert.Equal(deck,e.Player.Deck.Count);
    }
    [Fact] public void SacrificeDeathCanFinishMatchBeforeOriginalSpell()
    {
        e.Computer.Hp=1;Field(e.Player,"LCG-003");var victim=Field(e.Computer,"LCG-101");var c=Hand(e.Player,"LCG-088");
        Assert.True(e.CastSpell(e.Player,c));Assert.True(e.SelectTarget(victim));Assert.True(e.SelectTarget(e.Player.Field[0]));Assert.True(e.IsOver);Assert.Contains(victim,e.Computer.Field);
    }

    [Fact] public void StaleCommandAndPreviousMatchAreRejected()
    {
        var m=e.MatchId;var r=e.Revision;var c=Hand(e.Player,"LCG-101");
        Assert.True(e.ExecuteCommand("player",m,r,()=>e.PlayEnergy(e.Player,c)).Success);
        bool executed=false;Assert.Equal("stale",e.ExecuteCommand("player",m,r,()=>{executed=true;return true;}).Code);Assert.False(executed);
        e.StartGame(db.PresetDecks[0],db.PresetDecks[1]);Assert.Equal("stale",e.ExecuteCommand("player",m,e.Revision,()=>true).Code);
    }



    [Fact] public void FreeSummonConsumesHandWithoutDeployDraw()
    {
        var child=Hand(e.Player,"LCG-001");var c=Hand(e.Player,"LCG-052");int deck=e.Player.Deck.Count;
        Assert.True(e.CastSpell(e.Player,c));Choices();Assert.DoesNotContain(child,e.Player.Hand);Assert.Single(e.Player.Field);Assert.Equal(child.InstanceId,e.Player.Field[0].InstanceId);Assert.Equal(deck,e.Player.Deck.Count);
    }
    [Fact] public void SearchUsesActualTopCardsAndChosenBottomOrder()
    {
        var top=new[]{Card("LCG-101"),Card("LCG-121"),Card("LCG-122")};e.Player.Deck.Clear();e.Player.Deck.AddRange(top);var spell=Hand(e.Player,"LCG-030");
        Assert.True(e.CastSpell(e.Player,spell));Assert.True(e.SelectChoice(e.CurrentPendingChoice!.Options.Single(x=>x.Id==top[0].InstanceId.ToString())));
        Assert.True(e.SelectChoice(e.CurrentPendingChoice!.Options.Single(x=>x.Id==top[2].InstanceId.ToString())));
        Assert.Contains(top[0],e.Player.Hand);Assert.Equal(new[]{top[2],top[1]},e.Player.Deck);
    }
    [Fact] public void SimultaneousDeathRemovesBothBeforeDeathDraw()
    {
        var a=Field(e.Player,"LCG-025",3000);var b=Field(e.Computer,"LCG-103",3000);int pd=e.Player.Deck.Count,cd=e.Computer.Deck.Count;Assert.True(e.Attack(e.Player,a,b));
        Assert.Empty(e.Player.Field);Assert.Empty(e.Computer.Field);Assert.Equal(pd-1,e.Player.Deck.Count);Assert.Equal(cd-1,e.Computer.Deck.Count);
    }
    [Fact] public void EveryMonsterAndSpellResolvesWithoutCreatingCards()
    {
        foreach(var definition in db.AllCards)
        {
            e.StartGame(db.PresetDecks[0],db.PresetDecks[1]);e.Computer.IsAi=false;e.Player.Hand.Clear();e.Computer.Hand.Clear();Energy(e.Player);Energy(e.Computer);
            Field(e.Player,"LCG-101");Field(e.Player,"LCG-103");
            Field(e.Computer,"LCG-007",325);Field(e.Computer,"LCG-113");
            Hand(e.Player,"LCG-001");Hand(e.Player,"LCG-003");Hand(e.Player,"LCG-121");
            e.Player.Graveyard.Add(Card("LCG-101"));e.Player.Graveyard.Add(Card("LCG-103"));
            var card=new CardInstance(definition);e.Player.Hand.Add(card);int before=Count();
            Assert.True(definition.IsMonster?e.SummonMonster(e.Player,card):e.CastSpell(e.Player,card),definition.Id+": "+e.LastError);Choices();
            Assert.False(e.IsOver);Assert.Equal(before,Count());
            if(definition.IsMonster) Assert.Equal(definition.PP??0,e.Player.Field.Single(m=>m.InstanceId==card.InstanceId).CurrentPP);
            if(definition.IsMonster) { Energy(e.Player);var wipe=Hand(e.Player,"LCG-098");before=Count();Assert.True(e.CastSpell(e.Player,wipe),definition.Id+": "+e.LastError);Choices();Assert.Equal(before,Count()); }
        }
        int Count()=>new[]{e.Player,e.Computer}.Sum(p=>p.Deck.Count+p.Hand.Count+p.EnergyZone.Count+p.Graveyard.Count+p.Field.Count+p.Field.Sum(m=>m.ShieldCount)+p.Field.Count(m=>m.SilenceSpell!=null));
    }
    [Fact] public void SacrificeTriggersLastWordsButNotDestroyedListeners()
    {
        e.Player.Hp=5;Field(e.Player,"LCG-011");Field(e.Player,"LCG-099");var victim=Field(e.Player,"LCG-003");var c=Hand(e.Player,"LCG-096");int deck=e.Player.Deck.Count;
        Assert.True(e.CastSpell(e.Player,c));Assert.True(e.SelectTarget(victim));Assert.Equal(4,e.Player.Hp);Assert.Equal(6,e.Computer.Hp);Assert.Equal(deck-2,e.Player.Deck.Count);
    }

}
