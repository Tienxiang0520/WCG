using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;
namespace WcgTests;

// 新流派「腐沼蔓生」（WCG-382–390，2026-10-25）：腐蝕指示物每個 PP -300，可疊加；沉默、驅散、離場時移除。
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
        Assert.Equal(CardBalanceHistory.BeforeBloodPuppeteerText(cards).AllCards.Where(c=>!CardBalanceHistory.IsRotMireCard(c.Id)&&!CardBalanceHistory.IsSurgeStrikeCard(c.Id)&&!CardBalanceHistory.IsHolySpringCard(c.Id)).Select(c=>JsonSerializer.Serialize(c)),old.AllCards.Select(c=>JsonSerializer.Serialize(c)));
    }
    [Fact] public void EachCounterIsMinusThreeHundredAndZeroPpDoesNotKill()
    {
        var g=Game();var foe=Unit(g.Computer,101,0);Refresh(g);var pp=foe.CurrentPP;
        Assert.True(g.CastSpell(g.Player,Hand(g,384)));Settle(g);
        Assert.Equal(1,foe.CorrosionCounters);Assert.Equal(Math.Max(0,pp-300),foe.CurrentPP);
        foe.CorrosionCounters=10;Refresh(g);Assert.Equal(0,foe.CurrentPP);Assert.Contains(foe,g.Computer.Field);
        Assert.Contains(g.PowerModifiers(foe),s=>s.Label=="腐蝕指示物"&&s.Amount==-3000);
    }
    [Fact] public void SilenceAndDispelRemoveCorrosion()
    {
        var g=Game();var foe=Unit(g.Computer,113,0);foe.CorrosionCounters=2;Refresh(g);
        Assert.True(g.CastSpellAt(g.Player,Hand(g,208),foe));Settle(g);Assert.Equal(0,foe.CorrosionCounters);Assert.Equal(foe.Card.PP,foe.CurrentPP);
        var g2=Game();var f2=Unit(g2.Computer,113,0);f2.CorrosionCounters=3;Refresh(g2);
        Assert.True(g2.SummonMonster(g2.Player,Hand(g2,134)));Settle(g2,"DISPEL_OPT");Assert.Equal(0,f2.CorrosionCounters);
    }
    [Fact] public void HarvestDestroysARipeMonsterOrCorrodesTwo()
    {
        var g=Game();var a=Unit(g.Computer,113,0);var b=Unit(g.Computer,113,1);b.CorrosionCounters=2;Refresh(g);
        Assert.True(g.CastSpell(g.Player,Hand(g,388)));Settle(g);Assert.DoesNotContain(b,g.Computer.Field);Assert.Contains(a,g.Computer.Field);
        Assert.True(g.CastSpell(g.Player,Hand(g,388)));Settle(g);Assert.Equal(2,a.CorrosionCounters);
    }
    [Fact] public void MotherCorrodesEverythingThenDestroysPpZero()
    {
        var g=Game();var small=Unit(g.Computer,101,0);var big=Unit(g.Computer,113,1);Refresh(g);
        Assert.True(g.SummonMonster(g.Player,Hand(g,390)));Settle(g);
        Assert.Equal(small.Card.PP<=600,!g.Computer.Field.Contains(small));Assert.Contains(big,g.Computer.Field);Assert.Equal(2,big.CorrosionCounters);
    }
    [Fact] public void MawHitsFaceForCorrosionUpToThree()
    {
        var g=Game();var a=Unit(g.Computer,113,0);a.CorrosionCounters=4;Refresh(g);var hp=g.Computer.Hp;
        Assert.True(g.SummonMonster(g.Player,Hand(g,389)));Settle(g);Assert.Equal(hp-3,g.Computer.Hp);
    }
    [Fact] public void BroodCorrodesAtTurnStartAndDestroysAtThree()
    {
        var g=Game();Structure(g.Player,386,4);var foe=Unit(g.Computer,113,0);foe.CorrosionCounters=3;Refresh(g);
        typeof(GameEngine).GetMethod("MireStartTurn",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,[g.Player]);Drain(g);Settle(g);
        Assert.DoesNotContain(foe,g.Computer.Field);
    }
    [Fact] public void CrawlerDeathCorrodesAndHunterHitsHarder()
    {
        var g=Game();var crawler=Unit(g.Player,382,0);var foe=Unit(g.Computer,113,1);Refresh(g);
        typeof(GameEngine).GetMethod("KillBatch",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,[new[]{crawler},"test",true]);Drain(g);Settle(g);
        Assert.Equal(1,foe.CorrosionCounters);
        Assert.Equal(500,(int)typeof(GameEngine).GetMethod("MireCombatPower",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[new MonsterInstance(cards.GetCard("WCG-385")!),foe,true])!);
    }
    [Fact] public void SpreaderCorrodesWhatItsArrowPointsAtEachTurnEnd()
    {
        var g=Game();var s=Unit(g.Player,383,1);var foe=Unit(g.Computer,113,3);Refresh(g); // ↑ from slot 1 points at slot 3
        Assert.True(g.EndTurn());Settle(g);Assert.Equal(1,foe.CorrosionCounters);
    }
    [Fact] public void PrintToolsAddACorrosionSheetForMireDecks()
    {
        var plan=new DeckPrintPlan("t","生機",[new PrintedCard("WCG-384","蔓延孢雲","法術","生機",2,null,null,"")]);
        Assert.Equal(0,DeckPrintTools.Defaults(plan with {Cards=[]})[PrintTool.CorrosionTokens]);Assert.Equal(1,DeckPrintTools.Defaults(plan)[PrintTool.CorrosionTokens]);
    }
}
