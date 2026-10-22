using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;
namespace WcgTests;

// 理智加強（2026-10-17）：十張理智卡加強、五張新卡（258–262），反擊多了「敵方施放法術」與「敵方付費召喚」兩個時機。
public class ReasonBuffTests
{
    private readonly CardDatabase cards;
    public ReasonBuffTests(){var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));cards=new(env.Object);}
    private GameEngine Game(CardDatabase? db=null){db??=cards;var g=new GameEngine(db,new Random(17));g.StartGame(db.PresetDecks[0],db.PresetDecks[1]);foreach(var p in new[]{g.Player,g.Computer}){p.Hand.Clear();p.Field.Clear();p.EnergyZone.Clear();for(int i=0;i<12;i++)p.EnergyZone.Add(new(db.GetCard("WCG-101")!));}return g;}
    private CardInstance Hand(GameEngine g,int n,PlayerState? p=null){var c=new CardInstance(cards.GetCard($"WCG-{n:000}")!);(p??g.Player).Hand.Add(c);return c;}
    private MonsterInstance Unit(PlayerState p,int n,int slot,bool tapped=false){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=tapped,HasSummoningSickness=false};p.Field.Add(m);p.Field.Sort((a,b)=>a.Slot.CompareTo(b.Slot));return m;}
    private static void Refresh(GameEngine g)=>typeof(GameEngine).GetMethod("RefreshBoard",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);
    private static void Choose(GameEngine g,Func<ChoiceOption,bool> pick)=>Assert.True(g.SelectChoice(g.CurrentPendingChoice!.Options.First(pick)));

    [Fact] public void AbilityMonstersStayBelowTheVanillaCurve()
    {
        int[] vanilla=[0,1000,1300,1600,1900,2300,2600,2900,3200,3500];
        foreach(var id in new[]{"WCG-023","WCG-033","WCG-163","WCG-202","WCG-229","WCG-251","WCG-258","WCG-261","WCG-262"})
        {var c=cards.GetCard(id)!;Assert.True(c.PP<vanilla[c.TotalCost],id);}
        Assert.Equal(1300,cards.GetCard("WCG-251")!.PP);Assert.True(cards.GetCard("WCG-251")!.HasTaunt);
        Assert.Equal(1300,cards.GetCard("WCG-258")!.PP);Assert.True(cards.GetCard("WCG-258")!.HasTaunt);
    }

    [Fact] public void OldEraKeepsTheOldCardsAndHasNoNewOnes()
    {
        var old=CardBalanceHistory.BeforeReasonBuff(cards);
        Assert.False(old.GetCard("WCG-251")!.HasTaunt);Assert.Equal(1100,old.GetCard("WCG-033")!.PP);Assert.Equal(2200,old.GetCard("WCG-163")!.PP);
        Assert.Null(old.GetCard("WCG-258"));Assert.Null(old.GetCard("WCG-262"));Assert.Equal(257,old.AllCards.Count);
        Assert.Equal(JsonText(CounterHistory.BeforeReasonBuff(cards)),JsonText(old));
        // Old behaviour: 質能瓦解 still stops at PP 1100, 虛空隱士 only hides while alone.
        var g=Game(old);var foe=new MonsterInstance(old.GetCard("WCG-155")!){Slot=0};g.Computer.Field.Add(foe);
        var spell=new CardInstance(old.GetCard("WCG-032")!);g.Player.Hand.Add(spell);Assert.Empty(g.GetDirectPlayTargets(g.Player,spell));
    }
    private static string JsonText(CardDatabase db)=>System.Text.Json.JsonSerializer.Serialize(db.AllCards);

    [Fact] public void MassDisruptionKillsUpTo1600OrUpTo2300WhenTapped()
    {
        var g=Game();var three=Unit(g.Computer,155,0);var four=Unit(g.Computer,55,1);var five=Unit(g.Computer,159,2,tapped:true);var six=Unit(g.Computer,119,3,tapped:true);
        var spell=Hand(g,32);Refresh(g);var targets=g.GetDirectPlayTargets(g.Player,spell);
        Assert.Contains(three,targets);Assert.DoesNotContain(four,targets);Assert.Contains(five,targets);Assert.DoesNotContain(six,targets);
        Assert.True(g.CastSpellAt(g.Player,spell,five));Assert.DoesNotContain(five,g.Computer.Field);
    }

    [Fact] public void ArcaneGuardIsATauntThatDraws()
    {
        var g=Game();Unit(g.Player,154,0);var hand=g.Player.Hand.Count;var card=Hand(g,251);
        Assert.True(g.SummonMonster(g.Player,card));Choose(g,o=>o.Id=="SLOT:1");
        Assert.Equal(hand+1,g.Player.Hand.Count);Assert.True(g.Player.Field.Single(m=>m.Card.Id=="WCG-251").IsTaunt);
    }

    [Fact] public void LumenApprenticeLooksAtTheTopCardAndMayBottomIt()
    {
        var g=Game();Unit(g.Player,23,0);var hand=g.Player.Hand.Count;
        Assert.True(g.CastSpell(g.Player,Hand(g,28)));
        Assert.Equal(GameEngine.ScryTitle,g.CurrentPendingChoice!.Title);var top=g.Player.Deck[0];
        Assert.Equal(top.Card,g.CurrentPendingChoice.Options[0].PreviewCard);
        Choose(g,o=>o.Id=="BOTTOM");Assert.Same(top,g.Player.Deck[^1]);Assert.Equal(hand+2,g.Player.Hand.Count); // drew 2, no discard
    }

    [Fact] public void RunicColossusDrawsWhenYourHandIsSmall()
    {
        var g=Game();Unit(g.Player,37,0);for(int i=0;i<3;i++)Hand(g,101);Hand(g,101,g.Computer);
        var hand=g.Player.Hand.Count;Assert.True(g.CastSpell(g.Player,Hand(g,14)));Assert.Equal(hand+1,g.Player.Hand.Count);
        for(int i=0;i<3;i++)Hand(g,101);hand=g.Player.Hand.Count;Assert.True(g.CastSpell(g.Player,Hand(g,14)));Assert.Equal(hand,g.Player.Hand.Count);
    }

    [Fact] public void MirrorLakeWardenReturnsTheAttackerThatBeatIt()
    {
        var g=Game();var warden=Unit(g.Computer,258,0);var attacker=Unit(g.Player,155,0);Refresh(g);
        Assert.True(g.Attack(g.Player,attacker,warden));
        Assert.DoesNotContain(warden,g.Computer.Field);Assert.DoesNotContain(attacker,g.Player.Field);Assert.Contains(g.Player.Hand,c=>c.InstanceId==attacker.InstanceId);
        g=Game();warden=Unit(g.Computer,258,0);var small=Unit(g.Player,101,0);Refresh(g);
        Assert.True(g.Attack(g.Player,small,warden));Assert.Contains(warden,g.Computer.Field);Assert.DoesNotContain(g.Player.Hand,c=>c.InstanceId==small.InstanceId);
    }

    [Fact] public void MistwingScoutCanIgnoreTaunt()
    {
        var g=Game();var scout=Unit(g.Player,261,0);var taunt=Unit(g.Computer,65,0);var other=Unit(g.Computer,101,1);Refresh(g);
        Assert.Contains(other,g.GetAttackTargets(scout));Assert.True(g.CanAttackPlayer(scout));
    }

    [Fact] public void LoreColossusGrowsWithHandSizeUpTo700()
    {
        var g=Game();var giant=Unit(g.Player,262,0);Refresh(g);Assert.Equal(2100,giant.CurrentPP);
        for(int i=0;i<3;i++)Hand(g,101);Refresh(g);Assert.Equal(2400,giant.CurrentPP);
        for(int i=0;i<6;i++)Hand(g,101);Refresh(g);Assert.Equal(2800,giant.CurrentPP);
    }

    [Fact] public void WordOfDenialNegatesAnEnemySpellFromHand()
    {
        var g=Game();var deny=Hand(g,259,g.Computer);var foe=Unit(g.Computer,101,0);var spell=Hand(g,32);var compHand=g.Computer.Hand.Count;
        Assert.True(g.CastSpellAt(g.Player,spell,foe));
        Assert.Equal(GameEngine.NegateTitle,g.CurrentPendingChoice!.Title);Assert.Equal("computer",g.DecisionPlayerId);
        Choose(g,o=>o.Id==deny.InstanceId.ToString());
        Assert.Contains(foe,g.Computer.Field);Assert.Contains(spell,g.Player.Graveyard);Assert.Contains(deny,g.Computer.Graveyard);
        Assert.Equal(compHand,g.Computer.Hand.Count); // spent the counter, drew 1
        Assert.Equal(10,g.Computer.AvailableEnergy);Assert.Equal(9,g.Player.AvailableEnergy);
    }

    [Fact] public void SkippingWordOfDenialLetsTheSpellResolve()
    {
        var g=Game();Hand(g,259,g.Computer);var foe=Unit(g.Computer,101,0);
        Assert.True(g.CastSpellAt(g.Player,Hand(g,32),foe));Choose(g,o=>o.Id=="SKIP");Assert.DoesNotContain(foe,g.Computer.Field);
    }

    [Fact] public void NoCounterWindowWithoutEnergyOrCard()
    {
        var g=Game();var foe=Unit(g.Computer,101,0);Hand(g,259,g.Computer);foreach(var e in g.Computer.EnergyZone)e.IsTapped=true;
        Assert.True(g.CastSpellAt(g.Player,Hand(g,32),foe));Assert.False(g.IsWaiting);Assert.DoesNotContain(foe,g.Computer.Field);
    }

    [Fact] public void EbbTideReturnsAPaidSummonBeforeItsDeploy()
    {
        var g=Game();var tide=Hand(g,260,g.Computer);var hand=g.Player.Hand.Count;var card=Hand(g,39); // 萬象觀測者：進場抽 2
        Assert.True(g.SummonMonster(g.Player,card));Choose(g,o=>o.Id=="SLOT:0");
        Assert.Equal(GameEngine.EbbTideTitle,g.CurrentPendingChoice!.Title);Choose(g,o=>o.Id==tide.InstanceId.ToString());
        Assert.Empty(g.Player.Field);Assert.Contains(g.Player.Hand,c=>c.InstanceId==card.InstanceId);Assert.Equal(hand+1,g.Player.Hand.Count); // no Deploy draw
        Assert.Equal(7,g.Player.AvailableEnergy);
    }

    [Fact] public void RankedAiCountersRemovalButNotSmallSpells()
    {
        var g=Game();g.AiLevel=5;Hand(g,259,g.Computer);var foe=Unit(g.Computer,154,0);
        Assert.True(g.CastSpellAt(g.Player,Hand(g,32),foe));Assert.True(g.ExecuteAiStep());Assert.Contains(foe,g.Computer.Field);
        g=Game();g.AiLevel=5;Hand(g,259,g.Computer);Unit(g.Computer,154,0);g.Computer.Hp=7;
        Assert.True(g.CastSpell(g.Player,Hand(g,211)));Assert.True(g.ExecuteAiStep());Assert.Equal(7,g.Computer.Hp);Assert.Single(g.Computer.Hand); // 2-cost 思緒潰散 (熾紅戰斧 costs 3 since 2026-10-18)
    }

    [Fact] public void HumanGetsTheCounterPromptOnTheComputersTurn()
    {
        var g=Game();var deny=Hand(g,259);var foe=Unit(g.Player,101,0);var spell=Hand(g,32,g.Computer);
        Assert.True(g.EndTurn());while(g.IsWaiting&&g.DecisionPlayerId=="player")Choose(g,o=>true);
        Assert.Equal("computer",g.CurrentTurnPlayerId);
        Assert.True(g.CastSpellAt(g.Computer,spell,foe));Assert.Equal(GameEngine.NegateTitle,g.CurrentPendingChoice!.Title);Assert.Equal("player",g.DecisionPlayerId);
        Choose(g,o=>o.Id==deny.InstanceId.ToString());Assert.Contains(foe,g.Player.Field);
    }
}
