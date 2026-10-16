using WcgWeb.Models;
using WcgWeb.Services;
using Microsoft.AspNetCore.Hosting;
using Moq;
namespace WcgTests;

// AI v7 position evaluation (GameEngine.AiV7.Eval.cs).
public class AiEvaluationTests
{
    private readonly CardDatabase cards;
    public AiEvaluationTests(){var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb"));var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(root);cards=new(env.Object);}
    private GameEngine Game(){var g=new GameEngine(cards,new Random(17));g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1]);g.Player.Hand.Clear();g.Computer.Hand.Clear();g.Player.Field.Clear();g.Computer.Field.Clear();return g;}
    private MonsterInstance Unit(GameEngine g,PlayerState p,string id,int slot){var m=new MonsterInstance(cards.GetCard(id)!){Slot=slot,IsTapped=false,HasSummoningSickness=false};p.Field.Add(m);return m;}

    [Fact]
    public void WinsAndLossesDominate()
    {
        var g=Game();g.Computer.HasLost=true;Assert.Equal(GameEngine.Win,g.Evaluate("player"));Assert.Equal(-GameEngine.Win,g.Evaluate("computer"));
    }

    [Fact]
    public void MoreHpAndMoreBoardScoreHigher()
    {
        var g=Game();var baseline=g.Evaluate("player");
        g.Computer.Hp--;Assert.True(g.Evaluate("player")>baseline);
        var a=g.Evaluate("player");Unit(g,g.Player,"WCG-001",0);Assert.True(g.Evaluate("player")>a);
        var b=g.Evaluate("player");Unit(g,g.Computer,"WCG-001",0);Assert.True(g.Evaluate("player")<b);
    }

    [Fact]
    public void LowHpIsWorthMoreThanHighHp()
    {
        var g=Game();g.Computer.Hp=7;var high=g.Evaluate("player");g.Computer.Hp=6;var dropHigh=g.Evaluate("player")-high;
        g.Computer.Hp=2;var low=g.Evaluate("player");g.Computer.Hp=1;var dropLow=g.Evaluate("player")-low;
        Assert.True(dropLow>dropHigh);
    }

    [Fact]
    public void LethalExposureIsPenalisedAndTauntsHelp()
    {
        var g=Game();g.Player.Hp=2;var safe=g.Evaluate("player");
        var big=Unit(g,g.Computer,"WCG-001",0);big.CurrentDP=3;
        var exposed=g.Evaluate("player");Assert.True(safe-exposed>25);
        var taunt=cards.AllCards.First(c=>c.IsMonster&&c.HasTaunt);
        var t=new MonsterInstance(taunt){Slot=1};g.Player.Field.Add(t);
        Assert.True(g.Evaluate("player")>exposed+20);
    }

    [Fact]
    public void EvaluationIgnoresTheOpponentsHiddenCards()
    {
        foreach(var g in AiSimulationTests.Positions(cards,3,9))
        {
            var me=g.ActivePlayer;var e=g.GetOpponent(me);var before=g.Evaluate(me.Id);
            var hand=e.Hand.ToList();var deck=e.Deck.ToList();var filler=cards.GetCard("WCG-101")!;
            e.Hand.Clear();e.Hand.AddRange(hand.Select(_=>new CardInstance(filler)));e.Deck.Clear();e.Deck.AddRange(deck.Select(_=>new CardInstance(filler)));
            Assert.Equal(before,g.Evaluate(me.Id));
            e.Hand.Clear();e.Hand.AddRange(hand);e.Deck.Clear();e.Deck.AddRange(deck);
        }
    }
}
