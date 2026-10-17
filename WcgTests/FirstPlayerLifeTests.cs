using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;
namespace WcgTests;

// 先攻 6 點生命: the first player starts at 6, the second at 7, and healing still caps at 7.
public sealed class FirstPlayerLifeTests
{
    private readonly CardDatabase cards;
    public FirstPlayerLifeTests()
    {
        var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));
        cards=new(env.Object);
    }
    [Theory][InlineData(true)][InlineData(false)]
    public void FirstPlayerStartsOneLifeShort(bool playerFirst)
    {
        var g=new GameEngine(cards,new Random(3));g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1],playerFirst);
        var (first,second)=playerFirst?(g.Player,g.Computer):(g.Computer,g.Player);
        Assert.Equal(6,first.Hp);Assert.Equal(7,second.Hp);Assert.Equal(first.Id,g.CurrentTurnPlayerId);
        Assert.Contains(g.Logs,l=>l.Message.Contains("先攻 6 點生命、後攻 7 點生命"));
    }
    [Fact]
    public void HealingStillCapsAtSeven()
    {
        var g=new GameEngine(cards,new Random(3));g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1]);
        typeof(GameEngine).GetMethod("Heal",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(g,[g.Player,3]);
        Assert.Equal(PlayerState.MaxHp,g.Player.Hp);
    }
    [Fact]
    public void LegacyMatchesStartBothPlayersAtSeven()
    {
        var g=new GameEngine(cards,new Random(3)){EqualStartingLife=true};g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1]);
        Assert.Equal(7,g.Player.Hp);Assert.Equal(7,g.Computer.Hp);
        // Restarting the same engine for a new game after clearing the flag applies the current rule again.
        g.EqualStartingLife=false;g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1],false);
        Assert.Equal(7,g.Player.Hp);Assert.Equal(6,g.Computer.Hp);
    }
    [Fact]
    public void PracticeBridgeStartUsesTheRule()
    {
        var g=new GameEngine(cards,new Random(5));var storage=new Mock<IPlayerStorage>();storage.Setup(x=>x.Gate).Returns(new object());var b=new BattleBridge(g,new DeckService(cards,storage.Object));
        Assert.True(b.Submit(new(Guid.NewGuid(),g.MatchId,g.Revision,"start",PlayerDeckId:cards.PresetDecks[0].Id,ComputerDeckId:cards.PresetDecks[1].Id,PlayerFirst:false)).Success);
        Assert.Equal(7,g.Player.Hp);Assert.Equal(6,g.Computer.Hp);
        Assert.Equal(6,b.Snapshot().Computer.Hp);
    }
}
