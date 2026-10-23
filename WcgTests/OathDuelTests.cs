using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;
namespace WcgTests;

// 新流派「誓劍決鬥」（WCG-264–272，2026-10-23）：決鬥 = 指定 1 隻敵方怪物交戰，嘲諷擋不住；決鬥怪進場當回合就能決鬥。
public class OathDuelTests
{
    private readonly CardDatabase cards;
    public OathDuelTests(){var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));cards=new(env.Object);}
    private GameEngine Game()=>Game(cards);
    private static GameEngine Game(CardDatabase cards){var g=new GameEngine(cards,new Random(17));g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1],true);foreach(var p in new[]{g.Player,g.Computer}){p.Hand.Clear();p.Field.Clear();p.Structures.Clear();p.EnergyZone.Clear();for(int i=0;i<12;i++)p.EnergyZone.Add(new(cards.GetCard("WCG-101")!));}Assert.Equal("player",g.CurrentTurnPlayerId);return g;}
    private CardInstance Hand(GameEngine g,int n){var c=new CardInstance(cards.GetCard($"WCG-{n:000}")!);g.Player.Hand.Add(c);return c;}
    private MonsterInstance Unit(PlayerState p,int n,int slot,bool tapped=false){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=tapped,HasSummoningSickness=false};p.Field.Add(m);p.Field.Sort((a,b)=>a.Slot.CompareTo(b.Slot));return m;}
    private static void Refresh(GameEngine g)=>typeof(GameEngine).GetMethod("RefreshBoard",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);
    private static void Choose(GameEngine g,Func<ChoiceOption,bool> pick)=>Assert.True(g.SelectChoice(g.CurrentPendingChoice!.Options.First(pick)));
    private static void Settle(GameEngine g){while(g.CurrentPendingChoice is {} c)Assert.True(g.SelectChoice(c.Options.Last()));}
    private MonsterInstance Summon(GameEngine g,int n,int slot=0){var c=Hand(g,n);Assert.True(g.SummonMonster(g.Player,c));Choose(g,o=>o.Id==$"SLOT:{slot}");return g.Player.Field.Single(m=>m.InstanceId==c.InstanceId);}
    private static void Duel(GameEngine g,MonsterInstance target){Assert.Equal(GameEngine.DuelTitle,g.CurrentPendingChoice!.Title);Choose(g,o=>o.Id==target.InstanceId.ToString());}

    [Fact] public void OldEraHasNoDuelCardsAndEverythingElseIsUnchanged()
    {
        var old=CardBalanceHistory.BeforeOathDuelCards(cards);
        for(int n=264;n<=272;n++){Assert.NotNull(cards.GetCard($"WCG-{n}"));Assert.Null(old.GetCard($"WCG-{n}"));}
        Assert.Equal(cards.AllCards.Count(c=>int.Parse(c.Id[4..])<=272)-9,old.AllCards.Count);
        Assert.Equal(JsonSerializer.Serialize(CounterHistory.BeforeOathDuelCards(cards).AllCards),JsonSerializer.Serialize(old.AllCards));
        Assert.Equal(cards.AllCards.Where(c=>int.Parse(c.Id[4..])<=272&&!CardBalanceHistory.IsOathDuelCard(c.Id)).Select(c=>JsonSerializer.Serialize(c)),old.AllCards.Select(c=>JsonSerializer.Serialize(c)));
    }
    [Fact] public void DuelMonstersStayBelowTheVanillaCurve()
    {
        int[] vanilla=[0,1000,1300,1600,1900,2300,2600,2900,3200,3500];
        foreach(var n in new[]{264,265,266,269,270,272}){var c=cards.GetCard($"WCG-{n}")!;Assert.True(c.PP<vanilla[c.TotalCost],c.Id);Assert.Equal("秩序",c.Will);}
        Assert.DoesNotContain(cards.AllCards.Where(c=>CardBalanceHistory.IsOathDuelCard(c.Id)).SelectMany(c=>c.Keywords),k=>k is not ("聖盾"));
    }
    [Fact] public void WitnessGivesAFriendlyMonsterACounter()
    {
        var g=Game();var ally=Unit(g.Player,101,1);Refresh(g);var w=Summon(g,264,0);
        Assert.Equal(GameEngine.ShieldGiftTitle,g.CurrentPendingTarget!.Title);Assert.True(g.SelectTarget(ally));
        Assert.Equal(1,ally.ShieldCounters);Assert.True(ally.HasShield);Assert.Equal(0,w.ShieldCounters);
        Refresh(g);Assert.Equal(1,ally.ShieldCounters); // a manual counter is not recounted by RefreshBoard
    }
    [Fact] public void SquireDrawsWhenItSurvivesACombat()
    {
        var g=Game();var squire=Unit(g.Player,265,0);var foe=Unit(g.Computer,101,2);Refresh(g);Assert.Equal(1,squire.ShieldCounters);
        var hand=g.Player.Hand.Count;Assert.True(g.Attack(g.Player,squire,foe));Settle(g);
        Assert.Contains(squire,g.Player.Field);Assert.Equal(0,squire.ShieldCounters);Assert.Equal(hand+1,g.Player.Hand.Count);
    }
    [Fact] public void ChallengerDuelsOnEntryPastTauntAndMaySkip()
    {
        var g=Game();var wall=Unit(g.Computer,113,0);var small=Unit(g.Computer,101,1);Refresh(g);
        var knight=Summon(g,266,0);Duel(g,small);Settle(g);
        Assert.DoesNotContain(small,g.Computer.Field);Assert.Contains(knight,g.Player.Field);Assert.Contains(wall,g.Computer.Field);
        Assert.False(knight.IsTapped&&knight.HasAttacked);
        var g2=Game();var foe=Unit(g2.Computer,101,1);Refresh(g2);var k2=Summon(g2,266,0);Choose(g2,o=>o.Id=="SKIP");Settle(g2);
        Assert.Contains(foe,g2.Computer.Field);Assert.Contains(k2,g2.Player.Field);
    }
    // Effect-started combats present as a normal attack (source = the fighter, target = the chosen enemy) so the client plays the lunge.
    private static void AssertLunge(GameEngine g,MonsterInstance a,MonsterInstance d,int from)
    {
        var evs=g.PresentationEvents.Skip(from).ToList();var hit=evs.FindIndex(e=>e.Type=="attack"&&e.InstanceId==a.InstanceId&&e.TargetId==d.InstanceId);
        Assert.True(hit>=0);Assert.Equal("player",evs[hit].Side);
        var death=evs.FindIndex(e=>e.Type=="death"&&e.InstanceId==d.InstanceId);if(death>=0)Assert.True(hit<death);
        Assert.Null(g.LastAttack);
    }
    [Fact] public void EntryDuelPresentsAsAnAttackLunge()
    {
        var g=Game();var small=Unit(g.Computer,101,1);Refresh(g);var knight=Summon(g,266,0);Duel(g,small);AssertLunge(g,knight,small,0);Settle(g);Assert.DoesNotContain(small,g.Computer.Field);
    }
    [Fact] public void SpellDuelsPresentAsAnAttackLunge()
    {
        var g=Game();var mine=Unit(g.Player,101,0);var big=Unit(g.Computer,155,1);Refresh(g);var spell=Hand(g,267);
        Assert.True(g.CastSpellAt(g.Player,spell,mine));Assert.True(g.SelectTarget(big));AssertLunge(g,mine,big,0);Settle(g);
        var g2=Game();var m2=Unit(g2.Player,155,0);var foe=Unit(g2.Computer,101,1);Refresh(g2);var decl=Hand(g2,230);
        Assert.True(g2.CastSpellAt(g2.Player,decl,m2));Assert.True(g2.SelectTarget(foe));AssertLunge(g2,m2,foe,0);Settle(g2);Assert.DoesNotContain(foe,g2.Computer.Field);
    }
    [Fact] public void ChampionDuelsABigTauntAndSurvivesWithItsCounter()
    {
        var g=Game();var wall=Unit(g.Computer,113,0);Refresh(g);var champ=Summon(g,269,0);Assert.Equal(1,champ.ShieldCounters);
        Duel(g,wall);Settle(g);Assert.DoesNotContain(wall,g.Computer.Field);Assert.Contains(champ,g.Player.Field);Assert.Equal(0,champ.ShieldCounters);
    }
    [Fact] public void DuelOfHonorShieldsTapsAndFights()
    {
        var g=Game();var mine=Unit(g.Player,101,0);var big=Unit(g.Computer,155,1);Refresh(g);var spell=Hand(g,267);
        Assert.Contains(mine,g.GetDirectPlayTargets(g.Player,spell));Assert.True(g.CastSpellAt(g.Player,spell,mine));
        Assert.True(g.SelectTarget(big));Settle(g);
        Assert.Contains(mine,g.Player.Field);Assert.True(mine.IsTapped);Assert.Equal(0,mine.ShieldCounters);Assert.Contains(big,g.Computer.Field);
        var tapped=Unit(g.Player,155,2,tapped:true);var spell2=Hand(g,267);Assert.DoesNotContain(tapped,g.GetDirectPlayTargets(g.Player,spell2));
    }
    [Fact] public void ArenaDuelsAnyEnemyMonsterAndTapsBoth()
    {
        var g=Game();var arena=new MonsterInstance(cards.GetCard("WCG-268")!){Slot=4,IsTapped=false,HasSummoningSickness=false};g.Player.Structures.Add(arena);
        var mine=Unit(g.Player,155,0);Refresh(g);Assert.False(g.CanActivate(g.Player,arena));
        var foe=Unit(g.Computer,101,1);var taunt=Unit(g.Computer,65,2);Refresh(g);Assert.True(g.CanActivate(g.Player,arena));
        Assert.True(g.Activate(g.Player,arena));Assert.True(g.SelectTarget(mine));Assert.Equal(GameEngine.ArenaFoeTitle,g.CurrentPendingTarget!.Title);
        Assert.True(g.SelectTarget(foe));Settle(g);
        Assert.DoesNotContain(foe,g.Computer.Field);Assert.Contains(taunt,g.Computer.Field);Assert.True(mine.IsTapped);Assert.True(arena.IsTapped);
    }
    [Fact] public void PreArenaAnyEnemyCatalogStillDuelsOnlyTaunts()
    {
        var old=CardBalanceHistory.BeforeArenaAnyEnemy(cards);Assert.Contains("敵方嘲諷怪物交戰",old.GetCard("WCG-268")!.Text);
        Assert.Equal(cards.AllCards.Where(c=>c.Id!="WCG-268").Select(c=>System.Text.Json.JsonSerializer.Serialize(c)),old.AllCards.Where(c=>c.Id!="WCG-268").Select(c=>System.Text.Json.JsonSerializer.Serialize(c)));
        var g=Game(old);var arena=new MonsterInstance(old.GetCard("WCG-268")!){Slot=4,IsTapped=false,HasSummoningSickness=false};g.Player.Structures.Add(arena);
        var mine=Unit(g.Player,155,0);var foe=Unit(g.Computer,101,1);Refresh(g);Assert.False(g.CanActivate(g.Player,arena));
        var taunt=Unit(g.Computer,65,2);Refresh(g);Assert.True(g.CanActivate(g.Player,arena));
        Assert.True(g.Activate(g.Player,arena));Assert.True(g.SelectTarget(mine));Assert.Equal(GameEngine.ArenaTauntTitle,g.CurrentPendingTarget!.Title);Assert.True(g.SelectTarget(taunt));Settle(g);
        Assert.DoesNotContain(taunt,g.Computer.Field);Assert.True(mine.IsTapped);Assert.True(arena.IsTapped);Assert.Contains(foe,g.Computer.Field);
    }
    [Fact] public void StandardBearerShieldsAVictorOncePerTurn()
    {
        var g=Game();var flag=Unit(g.Player,270,0);var a=Unit(g.Player,155,1);var b=Unit(g.Player,155,2);var f1=Unit(g.Computer,101,0);var f2=Unit(g.Computer,101,1);Refresh(g);
        Assert.True(g.Attack(g.Player,a,f1));Settle(g);Assert.Equal(1,a.ShieldCounters);
        Assert.True(g.Attack(g.Player,b,f2));Settle(g);Assert.Equal(0,b.ShieldCounters);Assert.Equal(1,a.ShieldCounters);
    }
    [Fact] public void DuelingBladeAddsFourHundredUntilItsMonsterFights()
    {
        var g=Game();var mine=Unit(g.Player,101,0);var foe=Unit(g.Computer,154,1);Refresh(g);var blade=Hand(g,271);
        Assert.True(g.CastSpellAt(g.Player,blade,mine));Assert.Equal(1400,mine.CurrentPP);
        Assert.True(g.Attack(g.Player,mine,foe));Settle(g);
        Assert.Contains(mine,g.Player.Field);Assert.DoesNotContain(foe,g.Computer.Field);Assert.Equal(1000,mine.CurrentPP);Assert.Contains(g.Player.Graveyard,c=>c.Card.Id=="WCG-271");
    }
    [Fact] public void MarshalFightsUpToTwoTaunts()
    {
        var g=Game();var t1=Unit(g.Computer,113,0);var t2=Unit(g.Computer,65,1);var plain=Unit(g.Computer,101,2);Refresh(g);var m=Summon(g,272,0);
        Assert.DoesNotContain(g.CurrentPendingChoice!.Options,o=>o.Id==plain.InstanceId.ToString());
        Duel(g,t1);Duel(g,t2);Settle(g);
        Assert.DoesNotContain(t1,g.Computer.Field);Assert.DoesNotContain(t2,g.Computer.Field);Assert.Contains(plain,g.Computer.Field);Assert.Contains(m,g.Player.Field);
    }
    [Fact] public void MasterAiDuelsWhatItBeatsAndDeclinesALosingDuel()
    {
        var g=Game();g.AiLevel=5;g.Player.IsAi=true;var wall=Unit(g.Computer,113,0);var small=Unit(g.Computer,101,1);Refresh(g);
        var knight=Summon(g,266,0);Assert.Equal(GameEngine.DuelTitle,g.CurrentPendingChoice!.Title);Assert.True(g.ExecuteAiStep());Settle(g);
        Assert.DoesNotContain(small,g.Computer.Field);Assert.Contains(wall,g.Computer.Field);Assert.Contains(knight,g.Player.Field);
        var g2=Game();g2.AiLevel=5;g2.Player.IsAi=true;var wall2=Unit(g2.Computer,113,0);Refresh(g2);var k2=Summon(g2,266,0);Assert.True(g2.ExecuteAiStep());Settle(g2);
        Assert.Contains(k2,g2.Player.Field);Assert.Contains(wall2,g2.Computer.Field);
    }
}
