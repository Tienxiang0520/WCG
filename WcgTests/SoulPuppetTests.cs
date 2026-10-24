using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;
namespace WcgTests;

// 新流派「奪魂傀儡」（WCG-328–336，2026-10-24）：搶對手的怪物來打、用完犧牲；暫時控制轉 180 度留在原格。
public class SoulPuppetTests
{
    private readonly CardDatabase cards;
    public SoulPuppetTests(){var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));cards=new(env.Object);}
    private GameEngine Game(int energy=12){var g=new GameEngine(cards,new Random(17));g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1],true);foreach(var p in new[]{g.Player,g.Computer}){p.Hand.Clear();p.Field.Clear();p.Structures.Clear();p.EnergyZone.Clear();for(int i=0;i<energy;i++)p.EnergyZone.Add(new(cards.GetCard("WCG-101")!));}return g;}
    private CardInstance Hand(GameEngine g,int n){var c=new CardInstance(cards.GetCard($"WCG-{n:000}")!);g.Player.Hand.Add(c);return c;}
    private MonsterInstance Unit(PlayerState p,int n,int slot,bool tapped=false){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=tapped,HasSummoningSickness=false};p.Field.Add(m);return m;}
    private MonsterInstance Structure(PlayerState p,int n,int slot){var m=new MonsterInstance(cards.GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=false,HasSummoningSickness=false};p.Structures.Add(m);return m;}
    private static void Refresh(GameEngine g)=>typeof(GameEngine).GetMethod("RefreshBoard",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);
    private static void Settle(GameEngine g){for(int i=0;i<50&&g.IsWaiting;i++){if(g.CurrentPendingChoice is {} c)Assert.True(g.SelectChoice(c.Options.FirstOrDefault(o=>o.Id.StartsWith("SLOT:"))??c.Options.FirstOrDefault(o=>o.Id!="SKIP")??c.Options[0]));else{var t=g.CurrentPendingTarget!;var m=g.Player.Board.Concat(g.Computer.Board).First(x=>t.Validator!(x));Assert.True(g.SelectTarget(m));}}}

    private static int Cards(PlayerState p)=>p.Hand.Count+p.Deck.Count+p.Graveyard.Count+p.EnergyZone.Count+p.Occupied;
    [Fact] public void OldEraHasNoSoulCardsAndEverythingElseIsUnchanged()
    {
        var old=CardBalanceHistory.BeforeSoulPuppetCards(cards);
        for(int n=328;n<=336;n++){Assert.NotNull(cards.GetCard($"WCG-{n}"));Assert.Null(old.GetCard($"WCG-{n}"));}
        Assert.Equal(JsonSerializer.Serialize(CounterHistory.BeforeSoulPuppetCards(cards).AllCards),JsonSerializer.Serialize(old.AllCards));
        Assert.Equal(CardBalanceHistory.BeforeArrowText(cards).AllCards.Where(c=>!CardBalanceHistory.IsSoulPuppetCard(c.Id)).Select(c=>JsonSerializer.Serialize(c)),old.AllCards.Select(c=>JsonSerializer.Serialize(c)));
    }
    [Fact] public void BorrowedBladeStealsUntilEndOfTurnInPlaceAndGivesItBack()
    {
        var g=Game();var foe=Unit(g.Computer,113,2,tapped:true);Unit(g.Player,101,0);Refresh(g);
        var before=(Cards(g.Player),Cards(g.Computer));
        Assert.True(g.CastSpellAt(g.Player,Hand(g,331),foe));Settle(g);
        Assert.Contains(foe,g.Player.Field);Assert.True(foe.Turned);Assert.Equal(g.Computer.Id,foe.OwnerId);Assert.Equal(2,foe.Slot);
        Assert.True(g.CanAttack(foe));Assert.Equal(1,g.Computer.Lent);
        // It still occupies its owner's slot 3: the stealer's own slot 3 stays free, the owner's doesn't.
        Assert.Equal(before.Item2,Cards(g.Computer));
        // Its taunt now protects the stealer, not the owner.
        var hp=g.Computer.Hp;Assert.True(g.Attack(g.Player,foe));Settle(g);Assert.Equal(hp-1,g.Computer.Hp);
        Assert.True(g.EndTurn());Settle(g);
        Assert.Contains(foe,g.Computer.Field);Assert.False(foe.Turned);Assert.Null(foe.OwnerId);Assert.Equal(0,g.Computer.Lent);
    }
    [Fact] public void StolenMonsterLeavesToItsOwnersGraveyardAndPactDealsTwo()
    {
        var g=Game();var foe=Unit(g.Computer,101,1);var puppeteer=Unit(g.Player,328,0);Refresh(g);
        Assert.True(g.CastSpellAt(g.Player,Hand(g,331),foe));Settle(g);
        var hp=g.Computer.Hp;var hand=g.Player.Hand.Count;var grave=g.Computer.Graveyard.Count;
        var pact=Hand(g,329);Assert.True(g.CastSpell(g.Player,pact));
        for(int i=0;i<10&&g.IsWaiting;i++){if(g.CurrentPendingTarget is {} t)Assert.True(g.SelectTarget(foe));else Settle(g);}
        Assert.Equal(hp-2,g.Computer.Hp);Assert.Equal(grave+1,g.Computer.Graveyard.Count);Assert.Equal(hand+1,g.Player.Hand.Count); // −pact +1 draw from 328
        Assert.DoesNotContain(g.Player.Graveyard,c=>c.InstanceId==foe.InstanceId);
    }
    [Fact] public void PactOnYourOwnMonsterDraws()
    {
        var g=Game();var mine=Unit(g.Player,101,0);Refresh(g);var hp=g.Computer.Hp;var hand=g.Player.Hand.Count;
        Assert.True(g.CastSpell(g.Player,Hand(g,329)));Settle(g);Assert.Equal(hp,g.Computer.Hp);Assert.Equal(hand+1,g.Player.Hand.Count);Assert.Contains(g.Player.Graveyard,c=>c.InstanceId==mine.InstanceId);
    }
    [Fact] public void WhisperTapsFirstAndStealsATappedOneWithoutAttacking()
    {
        var g=Game();var foe=Unit(g.Computer,101,1);Refresh(g);
        Assert.True(g.CastSpellAt(g.Player,Hand(g,330),foe));Settle(g);Assert.True(foe.IsTapped);Assert.Contains(foe,g.Computer.Field);
        Assert.True(g.CastSpellAt(g.Player,Hand(g,330),foe));Settle(g);Assert.Contains(foe,g.Player.Field);Assert.False(g.CanAttack(foe));
    }
    [Fact] public void WardenStealsTheLowestUntilTheEndOfYourNextTurn()
    {
        var g=Game();var warden=Unit(g.Player,332,0);var small=Unit(g.Computer,132,1);var big=Unit(g.Computer,114,2);Refresh(g);
        Assert.True(g.EndTurn());Settle(g); // computer's turn
        Assert.True(g.Attack(g.Computer,big,warden));Settle(g);
        Assert.Contains(small,g.Player.Field);Assert.True(small.Turned);
        Assert.True(g.EndTurn());Settle(g);Assert.Contains(small,g.Player.Field); // still yours during your turn
        Assert.True(g.EndTurn());Settle(g);Assert.Contains(small,g.Computer.Field);
    }
    [Fact] public void MindthiefStealsASmallOneOrDrawsWhenTheEnemyHasNone()
    {
        var g=Game();var foe=Unit(g.Computer,101,3);Assert.True(g.SummonMonster(g.Player,Hand(g,333)));Settle(g);
        Assert.Contains(foe,g.Player.Field);Assert.True(g.CanAttack(foe));
        var g2=Game();var hand=g2.Player.Hand.Count;Assert.True(g2.SummonMonster(g2.Player,Hand(g2,333)));Settle(g2);Assert.Equal(hand+1,g2.Player.Hand.Count);
    }
    [Fact] public void PuppetmasterTradesABorrowedMonsterForAKill()
    {
        var g=Game();Unit(g.Player,334,0);var foe=Unit(g.Computer,113,1);var small=Unit(g.Computer,101,3);Refresh(g);
        Assert.True(g.CastSpellAt(g.Player,Hand(g,331),foe));Settle(g);
        Assert.True(g.EndTurn());Settle(g);
        Assert.Contains(g.Computer.Graveyard,c=>c.InstanceId==foe.InstanceId);Assert.Contains(g.Computer.Graveyard,c=>c.InstanceId==small.InstanceId);
    }
    [Fact] public void UsurpMovesItToYourEmptySlotWithASoulCounterForGood()
    {
        var g=Game();var giant=Unit(g.Computer,119,2);Refresh(g);
        Assert.True(g.CastSpellAt(g.Player,Hand(g,335),giant));Settle(g);
        Assert.Contains(giant,g.Player.Field);Assert.True(giant.SoulCounter);Assert.False(giant.Turned);Assert.False(g.CanAttack(giant));
        Assert.True(g.EndTurn());Settle(g);Assert.True(g.EndTurn());Settle(g);Assert.Contains(giant,g.Player.Field);Assert.True(g.CanAttack(giant));
        // Full row: can't be cast.
        var g2=Game();for(int i=0;i<5;i++)Unit(g2.Player,101,i);Unit(g2.Computer,101,0);Refresh(g2);Assert.False(g2.CastSpellAt(g2.Player,Hand(g2,335),g2.Computer.Field[0]));
    }
    [Fact] public void PuppetsReturnSendsAStolenMonsterHomeAndDrawsTwo()
    {
        var g=Game();var giant=Unit(g.Computer,119,2);Refresh(g);Assert.True(g.CastSpellAt(g.Player,Hand(g,335),giant));Settle(g);
        var hand=g.Player.Hand.Count;var theirs=g.Computer.Hand.Count;Assert.True(g.CastSpell(g.Player,Hand(g,336)));Settle(g);
        Assert.Equal(hand+2,g.Player.Hand.Count);Assert.Equal(theirs+1,g.Computer.Hand.Count);Assert.Empty(g.Player.Field);
    }
    [Fact] public void BoardShowsTurnedMonsterInItsOwnersRow()
    {
        var g=Game();var foe=Unit(g.Computer,113,2);Refresh(g);Assert.True(g.CastSpellAt(g.Player,Hand(g,331),foe));Settle(g);
        var storage=new Mock<IPlayerStorage>();storage.Setup(x=>x.Gate).Returns(new object());var bridge=new BattleBridge(g,new DeckService(cards,storage.Object));
        var snap=bridge.Snapshot();var shown=snap.Computer.Field.Single(m=>m.Card.InstanceId==foe.InstanceId);
        Assert.True(shown.Turned);Assert.True(shown.CanAttack);Assert.DoesNotContain(snap.Player.Field,m=>m.Card.InstanceId==foe.InstanceId);
    }
    [Fact] public void PrintToolsAddASoulSheetForUsurpDecks()
    {
        var plan=new DeckPrintPlan("t","深淵",[new PrintedCard("WCG-335","王座篡奪","法術","深淵",7,null,null,"")]);Assert.Equal(0,DeckPrintTools.Defaults(plan with {Cards=[]})[PrintTool.SoulTokens]);
        Assert.Equal(1,DeckPrintTools.Defaults(plan)[PrintTool.SoulTokens]);
    }
    [Fact] public void TurnedMonstersArrowsHitWhateverTheyPointAt()
    {
        // 突擊狼騎兵 turned 180° still buffs the monsters next to it in the row it stands in, whoever controls them.
        var g=Game();var wolf=Unit(g.Computer,5,2);var left=Unit(g.Computer,101,1);var right=Unit(g.Computer,101,3);Refresh(g);
        Assert.True(g.CastSpellAt(g.Player,Hand(g,331),wolf));Settle(g);Refresh(g);
        Assert.Equal(1200,left.CurrentPP);Assert.Equal(1200,right.CurrentPP);
        // ↑ still points straight across: a stolen 枯骨低語者 now shrinks the stealer's own monster opposite it.
        var g2=Game();var whisper=Unit(g2.Computer,283,2);var mine=Unit(g2.Player,101,2);Refresh(g2);Assert.Equal(700,mine.CurrentPP);
        Assert.True(g2.CastSpellAt(g2.Player,Hand(g2,331),whisper));Settle(g2);Refresh(g2);Assert.Equal(700,mine.CurrentPP);
        // One-way arrows swap with the rotation: 銀色誓言衛 (→) now points at its owner's left neighbour.
        var g3=Game();var oath=Unit(g3.Computer,61,2);var l=Unit(g3.Computer,101,1);var r=Unit(g3.Computer,101,3);Refresh(g3);
        Assert.True(g3.CastSpellAt(g3.Player,Hand(g3,331),oath));Settle(g3);Refresh(g3);
        var pa=typeof(GameEngine).GetMethod("PointsAt",BindingFlags.NonPublic|BindingFlags.Instance)!;
        bool At(MonsterInstance t)=>(bool)pa.Invoke(g3,[g3.Player,oath,g3.Computer,t])!;
        Assert.True(At(l));Assert.False(At(r));
    }
    [Fact] public void ArrowCardsNoLongerSayOwnOrEnemyAndOlderErasKeepTheirText()
    {
        var arrows=cards.AllCards.Where(c=>c.Arrows.Length>0).ToList();
        Assert.All(arrows,c=>{var arrowPart=c.Text[c.Text.IndexOf("【箭頭：",StringComparison.Ordinal)..];Assert.DoesNotContain("己方怪物",arrowPart.Replace("己方怪物時","").Split("攻擊敵方玩家")[0]);});
        var old=CardBalanceHistory.BeforeArrowText(cards);
        Assert.Equal(JsonSerializer.Serialize(CounterHistory.BeforeArrowText(cards).AllCards),JsonSerializer.Serialize(old.AllCards));
        Assert.Contains("左右相鄰的己方怪物",old.GetCard("WCG-005")!.Text);Assert.Contains("箭頭指向的怪物",cards.GetCard("WCG-005")!.Text);
        Assert.Equal(20,cards.AllCards.Count(c=>c.Text!=old.GetCard(c.Id)!.Text));
    }
    [Fact] public void MasterAiStealsAndSwingsForLethal()
    {
        var g=Game();g.AiLevel=5;g.Player.IsAi=true;g.Computer.Hp=2;Unit(g.Computer,113,2);Hand(g,331);Refresh(g);
        for(int i=0;i<12&&!g.IsOver&&g.DecisionPlayerId==g.Player.Id;i++)if(!g.ExecuteAiStep())break;Assert.True(g.Computer.Hp<2||g.IsOver);
    }
}
