using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;
namespace WcgTests;

// 新流派「腐沼蔓生」（WCG-382–390，2026-10-25）：2026-11-12 起腐蝕是箭頭或附著在敵方怪物底下的卡（各依卡文降 PP，可疊加）；
// 沉默、驅散時附著卡進入墓地。
public class RotMireTests
{
    private readonly CardDatabase cards;
    public RotMireTests(){var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));cards=new(env.Object);}
    private GameEngine Game(int energy=12){var g=new GameEngine(cards,new Random(17));g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1],true);foreach(var p in new[]{g.Player,g.Computer}){p.Hand.Clear();p.Field.Clear();p.Structures.Clear();p.EnergyZone.Clear();p.Graveyard.Clear();for(int i=0;i<energy;i++)p.EnergyZone.Add(new(cards.GetCard("WCG-101")!));}return g;}
    private CardInstance Hand(GameEngine g,int n){var c=new CardInstance(cards.GetCard($"WCG-{n:000}")!);g.Player.Hand.Add(c);return c;}
    private MonsterInstance Unit(PlayerState p,int n,int slot,bool tapped=false){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=tapped,HasSummoningSickness=false};p.Field.Add(m);return m;}
    private MonsterInstance Structure(PlayerState p,int n,int slot){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=false,HasSummoningSickness=false};p.Structures.Add(m);return m;}
    private static void Refresh(GameEngine g)=>typeof(GameEngine).GetMethod("RefreshBoard",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);
    private static void Settle(GameEngine g,string? pick=null){for(int i=0;i<80&&g.IsWaiting;i++){if(g.CurrentPendingChoice is {} c)Assert.True(g.SelectChoice(c.Options.FirstOrDefault(o=>pick!=null&&o.Id==pick)??c.Options.FirstOrDefault(o=>o.Id.StartsWith("SLOT:"))??c.Options.FirstOrDefault(o=>o.Id!="SKIP")??c.Options[0]));else{var t=g.CurrentPendingTarget!;var m=g.Player.Board.Concat(g.Computer.Board).First(x=>t.Validator!(x));Assert.True(g.SelectTarget(m));}}}
    private static void Drain(GameEngine g)=>typeof(GameEngine).GetMethod("Drain",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);

    [Fact] public void OldEraHasNoMireCardsAndEverythingElseIsUnchanged()
    {
        var old=CardBalanceHistory.BeforeRotMireCards(cards);
        for(int n=382;n<=390;n++){Assert.NotNull(cards.GetCard($"WCG-{n}"));Assert.Null(old.GetCard($"WCG-{n}"));}
        Assert.Equal(JsonSerializer.Serialize(CounterHistory.BeforeRotMireCards(cards).AllCards),JsonSerializer.Serialize(old.AllCards));
        Assert.Equal(CardBalanceHistory.BeforeBloodPuppeteerText(cards).AllCards.Where(c=>!CardBalanceHistory.IsRotMireCard(c.Id)&&!CardBalanceHistory.IsSurgeStrikeCard(c.Id)&&!CardBalanceHistory.IsHolySpringCard(c.Id)&&!CardBalanceHistory.IsStarCountdownCard(c.Id)&&!CardBalanceHistory.IsFateFlipCard(c.Id)).Select(c=>JsonSerializer.Serialize(c)),old.AllCards.Select(c=>JsonSerializer.Serialize(c)));
    }
    private MonsterInstance Attach(GameEngine g,MonsterInstance m,int id,int n=1){for(int i=0;i<n;i++)m.Attachments.Add(new(new(cards.GetCard($"WCG-{id:000}")!),g.Player.Id));return m;}
    private static void EndTurn(GameEngine g){Assert.True(g.EndTurn());while(g.CurrentPendingChoice is {} c)Assert.True(g.SelectChoice(c.Options.Last()));}

    [Fact] public void SporeCloudLowersEveryEnemyForThisTurnOnly()
    {
        var g=Game();var foe=Unit(g.Computer,101,0);Refresh(g);var pp=foe.CurrentPP;
        Assert.True(g.CastSpell(g.Player,Hand(g,384)));Settle(g);Assert.Equal(Math.Max(0,pp-300),foe.CurrentPP);Assert.Empty(foe.Attachments);
        EndTurn(g);Refresh(g);Assert.Equal(pp,foe.CurrentPP);
    }
    [Fact] public void AttachedCardsStackAndZeroPpDoesNotKill()
    {
        var g=Game();var foe=Attach(g,Unit(g.Computer,101,0),382,2);Refresh(g);Assert.Equal((foe.Card.PP??0)-600,foe.CurrentPP);
        Attach(g,foe,388,2);Refresh(g);Assert.Equal(0,foe.CurrentPP);Assert.Contains(foe,g.Computer.Field);
        Assert.Contains(g.PowerModifiers(foe),s=>s.Label=="孢沼爬蟲"&&s.Amount==-600);Assert.Contains(g.PowerModifiers(foe),s=>s.Label=="腐朽收割"&&s.Amount==-1200);
    }
    [Fact] public void SilenceAndDispelSendCorrosionCardsToTheGraveyard()
    {
        var g=Game();var foe=Attach(g,Unit(g.Computer,113,0),382,2);Refresh(g);
        Assert.True(g.CastSpellAt(g.Player,Hand(g,208),foe));Settle(g);Assert.Empty(foe.Attachments);Assert.Equal(foe.Card.PP,foe.CurrentPP);
        Assert.Equal(2,g.Player.Graveyard.Count(c=>c.Card.Id=="WCG-382"));
        var g2=Game();var f2=Attach(g2,Unit(g2.Computer,113,0),387,3);Refresh(g2);
        Assert.True(g2.SummonMonster(g2.Player,Hand(g2,134)));Settle(g2,"DISPEL_OPT");Assert.Empty(f2.Attachments);Assert.Equal(f2.Card.PP,f2.CurrentPP);
    }
    [Fact] public void HarvestDestroysARipeMonsterOrAttachesItself()
    {
        var g=Game();var a=Unit(g.Computer,113,0);var b=Attach(g,Unit(g.Computer,113,1),382,2);Refresh(g);
        Assert.True(g.CastSpell(g.Player,Hand(g,388)));Settle(g);Assert.DoesNotContain(b,g.Computer.Field);Assert.Contains(a,g.Computer.Field);
        var harvest=Hand(g,388);Assert.True(g.CastSpell(g.Player,harvest));Settle(g);
        Assert.Contains(a.Attachments,x=>x.Card==harvest&&x.OwnerId==g.Player.Id);Assert.Equal(Math.Max(0,(a.Card.PP??0)-600),a.CurrentPP);Assert.DoesNotContain(harvest,g.Player.Graveyard);
    }
    [Fact] public void HarvestCountsArrowsAndAttachedCardsSeparately()
    {
        // One arrow plus one attached card is neither 2 attached cards nor 2 arrows: 腐朽收割 attaches itself instead.
        var g=Game();var foe=Attach(g,Unit(g.Computer,113,3),382);Unit(g.Player,383,1);Refresh(g); // ↑ from slot 1 points at slot 3
        Assert.True(g.CastSpell(g.Player,Hand(g,388)));Settle(g);Assert.Contains(foe,g.Computer.Field);Assert.Equal(2,foe.Attachments.Count);
        Assert.True(g.CastSpell(g.Player,Hand(g,388)));Settle(g);Assert.DoesNotContain(foe,g.Computer.Field);
    }
    [Fact] public void MotherShrinksEverythingThisTurnThenDestroysPpZero()
    {
        var g=Game();var small=Unit(g.Computer,101,0);var big=Unit(g.Computer,113,1);Refresh(g);
        Assert.True(g.SummonMonster(g.Player,Hand(g,390)));Settle(g);
        Assert.Equal(small.Card.PP<=600,!g.Computer.Field.Contains(small));Assert.Contains(big,g.Computer.Field);Assert.Equal(-600,big.TurnBonus);Assert.Empty(big.Attachments);
    }
    [Fact] public void MawCountsArrowsAndAttachedCardsUpToThree()
    {
        var g=Game();Attach(g,Unit(g.Computer,113,0),382,4);Refresh(g);var hp=g.Computer.Hp;
        Assert.True(g.SummonMonster(g.Player,Hand(g,389)));Settle(g);Assert.Equal(hp-3,g.Computer.Hp);
        var g2=Game();Unit(g2.Computer,113,3);Unit(g2.Player,383,1);Refresh(g2);var hp2=g2.Computer.Hp;
        Assert.True(g2.SummonMonster(g2.Player,Hand(g2,389)));Settle(g2);Assert.Equal(hp2-1,g2.Computer.Hp);
    }
    [Fact] public void BroodDestroysThePointedMonsterOnlyAtZeroPp()
    {
        var g=Game();Structure(g.Player,386,1);var foe=Unit(g.Computer,101,3);Refresh(g); // ↑ from slot 1 points at slot 3
        Assert.Equal((foe.Card.PP??0)-300,foe.CurrentPP);
        typeof(GameEngine).GetMethod("MireStartTurn",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,[g.Player]);Drain(g);Settle(g);
        Assert.Contains(foe,g.Computer.Field);
        Attach(g,foe,382,3);Refresh(g);Assert.Equal(0,foe.CurrentPP);
        typeof(GameEngine).GetMethod("MireStartTurn",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,[g.Player]);Drain(g);Settle(g);
        Assert.DoesNotContain(foe,g.Computer.Field);
    }
    [Fact] public void CrawlerDeathAttachesItselfAndHunterHitsHarder()
    {
        var g=Game();var crawler=Unit(g.Player,382,0);var foe=Unit(g.Computer,113,1);var hunter=Unit(g.Player,385,2);Refresh(g);
        var power=typeof(GameEngine).GetMethod("MireCombatPower",BindingFlags.NonPublic|BindingFlags.Instance)!;
        Assert.Equal(0,(int)power.Invoke(g,[hunter,foe,true])!);
        typeof(GameEngine).GetMethod("KillBatch",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,[new[]{crawler},"test",true]);Drain(g);Settle(g);
        Assert.Contains(foe.Attachments,a=>a.Card.Card.Id=="WCG-382"&&a.OwnerId==g.Player.Id);Assert.DoesNotContain(g.Player.Graveyard,c=>c.Card.Id=="WCG-382");
        Assert.Equal(500,(int)power.Invoke(g,[hunter,foe,true])!);
    }
    [Fact] public void ShroomSpreaderAttachesItselfAndShrinksAnotherForTheTurn()
    {
        var g=Game();var spreader=Unit(g.Player,387,0);var a=Unit(g.Computer,113,1);var b=Unit(g.Computer,113,2);Refresh(g);
        typeof(GameEngine).GetMethod("KillBatch",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,[new[]{spreader},"test",true]);Drain(g);Settle(g);
        Assert.Contains(a.Attachments,x=>x.Card.Card.Id=="WCG-387");Assert.Equal(-300,b.TurnBonus);Assert.Empty(b.Attachments);
    }
    [Fact] public void SpreaderArrowLowersPpWhileItPoints()
    {
        var g=Game();var s=Unit(g.Player,383,1);var foe=Unit(g.Computer,113,3);Refresh(g); // ↑ from slot 1 points at slot 3
        Assert.Equal((foe.Card.PP??0)-300,foe.CurrentPP);EndTurn(g);Refresh(g);Assert.Equal((foe.Card.PP??0)-300,foe.CurrentPP);Assert.Empty(foe.Attachments);
        g.Player.Field.Remove(s);Refresh(g);Assert.Equal(foe.Card.PP,foe.CurrentPP);
    }
}
