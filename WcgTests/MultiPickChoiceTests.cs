using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;
namespace WcgTests;

// Multi-pick choice dialog (UI only): loops that used to reopen a single-pick dialog now carry MultiKey/Min/Max/Ordered/StopId.
// The board submits the picks as the same single "choice" steps (then StopId), so outcomes, AI, logs and replays are unchanged.
public class MultiPickChoiceTests
{
    private readonly CardDatabase cards;
    public MultiPickChoiceTests(){var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));cards=CardBalanceHistory.BeforeV2(new(env.Object));}
    private GameEngine Game(int energy=12){var g=new GameEngine(cards,new Random(17));g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1],true);foreach(var p in new[]{g.Player,g.Computer}){p.Hand.Clear();p.Field.Clear();p.Structures.Clear();p.EnergyZone.Clear();p.Graveyard.Clear();for(int i=0;i<energy;i++)p.EnergyZone.Add(new(cards.GetCard("WCG-101")!));}return g;}
    private CardInstance Hand(GameEngine g,int n){var c=new CardInstance(cards.GetCard($"WCG-{n:000}")!);g.Player.Hand.Add(c);return c;}
    private void Grave(PlayerState p,int n,int id=101){for(int i=0;i<n;i++)p.Graveyard.Add(new(cards.GetCard($"WCG-{id:000}")!));}
    private static void Refresh(GameEngine g)=>typeof(GameEngine).GetMethod("RefreshBoard",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,null);
    private void StackDeck(GameEngine g,params int[] top){foreach(var n in top.Reverse())g.Player.Deck.Insert(0,new(cards.GetCard($"WCG-{n:000}")!));}
    private static string Print(GameEngine g)=>string.Join("|",new[]{
        "H:"+string.Join(",",g.Player.Hand.Select(c=>c.Card.Id)),"D:"+string.Join(",",g.Player.Deck.Select(c=>c.Card.Id)),
        "G:"+string.Join(",",g.Player.Graveyard.Select(c=>c.Card.Id).OrderBy(x=>x)),"E:"+string.Join(",",g.Player.EnergyZone.Select(c=>c.Card.Id+(c.IsTapped?"T":""))),
        "F:"+string.Join(",",g.Player.Field.Select(m=>m.Card.Id)),$"HP:{g.Player.Hp}/{g.Computer.Hp}",$"W:{g.IsWaiting}"});

    // Old flow: one dialog per pick, choosing option[index] each time, then an explicit stop option.
    private static void OldFlow(GameEngine g,int[] indexes,string? stop){foreach(var i in indexes)Assert.True(g.SelectChoice(g.CurrentPendingChoice!.Options[i]));if(stop!=null)Assert.True(g.SelectChoice(g.CurrentPendingChoice!.Options.First(o=>o.Id==stop)));}
    // New flow: exactly what BattleBoard.SendPicks does, with picks chosen up front from the first dialog.
    private static List<string> NewFlow(GameEngine g,int[] firstDialogIndexes)
    {
        var c=g.CurrentPendingChoice!;Assert.NotNull(c.MultiKey);var key=c.MultiKey!;
        var ids=firstDialogIndexes.Select(i=>c.Options[i].Id).ToList();Assert.InRange(ids.Count,Math.Min(c.MultiMin,c.MultiMax),c.MultiMax);
        var sent=new List<string>();
        bool Same(string id)=>g.CurrentPendingChoice is {} p&&p.MultiKey==key&&p.Options.Any(o=>o.Id==id);
        foreach(var id in ids){if(!Same(id))break;Assert.True(g.SelectChoice(g.CurrentPendingChoice!.Options.First(o=>o.Id==id)));sent.Add(id);}
        if(c.MultiStopId is {} stop&&Same(stop)){Assert.True(g.SelectChoice(g.CurrentPendingChoice!.Options.First(o=>o.Id==stop)));sent.Add(stop);}
        return sent;
    }
    private static void Finish(GameEngine g){for(int i=0;i<20&&g.CurrentPendingChoice is {} c;i++)Assert.True(g.SelectChoice(c.Options.FirstOrDefault(o=>o.Id=="KEEP")??c.Options[0]));}

    [Fact] public void Search054TakesTwoMonstersAtOnceLikeTwoSinglePicks()
    {
        GameEngine Setup(){var g=Game();StackDeck(g,5,101,7,9,40);Assert.True(g.CastSpell(g.Player,Hand(g,54)));return g;}
        var a=Setup();var c=a.CurrentPendingChoice!;Assert.Equal(0,c.MultiMin);Assert.Equal(2,c.MultiMax);Assert.Equal("SKIP",c.MultiStopId);Assert.False(c.MultiOrdered);
        // Old: pick monster #2, then (remaining list shifts) monster #1 of the new list.
        var firstIds=c.Options.Where(o=>o.Id!="SKIP").Select(o=>o.Id).ToList();
        Assert.True(a.SelectChoice(c.Options[1]));var second=a.CurrentPendingChoice!;Assert.Equal(c.MultiKey,second.MultiKey);Assert.Equal(1,second.MultiMax);
        Assert.Equal(firstIds.Where((_,i)=>i!=1),second.Options.Where(o=>o.Id!="SKIP").Select(o=>o.Id)); // remaining order is stable
        Assert.True(a.SelectChoice(second.Options[0]));Finish(a);
        var b=Setup();NewFlow(b,[1,0]);Finish(b);Assert.Equal(Print(a),Print(b));
        // Taking fewer than the maximum = the old SKIP after one pick.
        var a1=Setup();OldFlow(a1,[2],"SKIP");Finish(a1);var b1=Setup();NewFlow(b1,[2]);Finish(b1);Assert.Equal(Print(a1),Print(b1));
        var a0=Setup();OldFlow(a0,[],"SKIP");Finish(a0);var b0=Setup();NewFlow(b0,[]);Finish(b0);Assert.Equal(Print(a0),Print(b0));
    }
    [Fact] public void BottomOrderingFollowsClickOrderAndUnpickedKeepCurrentOrder()
    {
        GameEngine Setup(){var g=Game();StackDeck(g,101,5,7);Assert.True(g.CastSpell(g.Player,Hand(g,30)));Assert.True(g.SelectChoice(g.CurrentPendingChoice!.Options[0]));return g;}
        var probe=Setup();var c=probe.CurrentPendingChoice!;Assert.True(c.MultiOrdered);Assert.Equal("KEEP",c.MultiStopId);Assert.Equal(0,c.MultiMin);Assert.Equal(2,c.MultiMax);
        // Options[0] is KEEP; click the second card first.
        var a=Setup();OldFlow(a,[2],null);Finish(a);var b=Setup();var sent=NewFlow(b,[2,1]);Finish(b);Assert.Equal(Print(a),Print(b));Assert.Single(sent); // last card goes automatically
        var a0=Setup();OldFlow(a0,[],"KEEP");var b0=Setup();NewFlow(b0,[]);Assert.Equal(Print(a0),Print(b0));
        var b1=Setup();var bottom=b1.CurrentPendingChoice!.Options[2].PreviewCard!.Id;NewFlow(b1,[2]);Assert.Equal(bottom,b1.Player.Deck[^2].Card.Id);
    }
    [Fact] public void Burn311NeedsExactlyTwoAnd315AllowsUpToThree()
    {
        GameEngine S311(){var g=Game(4);Assert.True(g.CastSpell(g.Player,Hand(g,311)));return g;}
        var p=S311().CurrentPendingChoice!;Assert.Equal(2,p.MultiMin);Assert.Equal(2,p.MultiMax);Assert.Null(p.MultiStopId);
        var a=S311();OldFlow(a,[3,0],null);var b=S311();NewFlow(b,[3,0]);Assert.Equal(Print(a),Print(b));Assert.False(b.IsWaiting);
        GameEngine S315(){var g=Game(3);for(int i=0;i<4;i++)g.Player.EnergyZone.Add(new(cards.GetCard("WCG-101")!){IsTapped=true});Assert.True(g.CastSpell(g.Player,Hand(g,315)));return g;}
        var q=S315().CurrentPendingChoice!;Assert.Equal(0,q.MultiMin);Assert.Equal(3,q.MultiMax);Assert.Equal("SKIP",q.MultiStopId);
        var a2=S315();OldFlow(a2,[0,1],"SKIP");var b2=S315();NewFlow(b2,[0,1]);Assert.Equal(Print(a2),Print(b2));
        var a3=S315();OldFlow(a3,[2,0,0],null);var b3=S315();NewFlow(b3,[2,0,1]);Assert.Equal(Print(a3),Print(b3));Assert.False(b3.IsWaiting);
    }
    [Fact] public void BoneStandard349PicksExactlyThreeGraveCards()
    {
        GameEngine Setup(){var g=Game();var flag=new MonsterInstance(cards.GetCard("WCG-349")!){Slot=4,IsTapped=false,HasSummoningSickness=false};g.Player.Structures.Add(flag);Grave(g.Player,1,5);Grave(g.Player,1,113);Grave(g.Player,1,81);Grave(g.Player,2);Refresh(g);Assert.True(g.Activate(g.Player,flag));
            var raise=g.CurrentPendingChoice!;Assert.Null(raise.MultiKey);Assert.True(g.SelectChoice(raise.Options[0]));return g;}
        var p=Setup().CurrentPendingChoice!;Assert.Equal(3,p.MultiMin);Assert.Equal(3,p.MultiMax);Assert.Null(p.MultiStopId);
        var a=Setup();OldFlow(a,[1,0,0],null);Finish(a);var b=Setup();NewFlow(b,[1,0,2]);Finish(b);
        // Bottom order is shuffled by the engine RNG; both runs share the seed, so compare everything.
        Assert.Equal(Print(a),Print(b));
    }
    [Fact] public void BridgeSnapshotCarriesMultiMetadataOnlyForLoops()
    {
        var g=Game(4);var bridge=new BattleBridge(g,null!);Assert.True(g.CastSpell(g.Player,Hand(g,311)));
        var m=bridge.Snapshot().Pending!.Multi!;Assert.Equal((2,2,false,(string?)null),(m.Min,m.Max,m.Ordered,m.StopId));
        Assert.True(g.SelectChoice(g.CurrentPendingChoice!.Options[0]));var next=bridge.Snapshot().Pending!.Multi!;Assert.Equal(m.Key,next.Key);Assert.Equal(1,next.Max);
        Assert.True(g.SelectChoice(g.CurrentPendingChoice!.Options[0]));Assert.Null(bridge.Snapshot().Pending);
        // A plain single choice (slot pick) has no multi metadata.
        var g3=Game();var b3=new BattleBridge(g3,null!);Assert.True(g3.SummonMonster(g3.Player,Hand(g3,101)));Assert.Null(b3.Snapshot().Pending!.Multi);
    }
}
