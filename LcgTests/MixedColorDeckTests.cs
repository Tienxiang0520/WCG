using LcgWeb.Models;
using LcgWeb.Services;
using Microsoft.AspNetCore.Hosting;
using Moq;
namespace LcgTests;

public class MixedColorDeckTests
{
    readonly CardDatabase db;
    public MixedColorDeckTests()
    {
        var env=new Mock<IWebHostEnvironment>();
        env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../LcgWeb")));
        db=new(env.Object);
    }
    List<string> Cards(string will,int count)=>db.AllCards.Where(c=>c.Will==will).SelectMany(c=>Enumerable.Repeat(c.Id,4)).Take(count).ToList();
    Deck Make(int main,int neutral,params (string Will,int Count)[] other)
    {
        var d=new Deck{MainWill="狂怒",CardIds=Cards("狂怒",main)};
        d.CardIds.AddRange(Cards("中立",neutral));foreach(var (will,count) in other)d.CardIds.AddRange(Cards(will,count));return d;
    }
    [Fact] public void FourOtherWillsShareTwelveSlotsAndCountCopies()
    {
        var d=Make(18,20,("理智",4),("生機",3),("秩序",3),("深淵",2));
        Assert.Equal((18,12,20),d.GetColorCounts(db.GetCard));Assert.True(d.IsValid(db.GetCard,out var error),error);
    }
    [Fact] public void ThirteenOffColorCardsFailEvenAtFifty()
    {
        var d=Make(17,20,("理智",4),("生機",4),("秩序",3),("深淵",2));
        Assert.False(d.IsValid(db.GetCard,out var error));Assert.Contains("混色卡合計最多 12",error);Assert.Contains("13",error);
    }
    [Theory][InlineData(50,0)][InlineData(0,50)][InlineData(10,40)]
    public void MainAndNeutralHaveNoMinimumOrAdditionalMaximum(int main,int neutral)
    {
        var d=Make(main,neutral);Assert.True(d.IsValid(db.GetCard,out var error),error);Assert.Equal(0,d.GetColorCounts(db.GetCard).OffColor);
    }
    [Theory][InlineData("")][InlineData("中立")][InlineData("不存在")]
    public void MainWillMustBeOneOfFiveWills(string will)
    {
        var d=Make(0,50);d.MainWill=will;Assert.False(d.IsValid(db.GetCard,out var error));Assert.Contains("主色",error);
    }
    [Fact] public void ChangingMainWillRecalculatesQuotaWithoutChangingCards()
    {
        var d=Make(38,0,("理智",12));var ids=d.CardIds.ToList();Assert.True(d.IsValid(db.GetCard,out _));
        d.MainWill="理智";Assert.Equal(38,d.GetColorCounts(db.GetCard).OffColor);Assert.False(d.IsValid(db.GetCard,out _));Assert.Equal(ids,d.CardIds);
    }
    [Fact] public void StartRejectsExcessMixedCardsBeforeReplacingMatch()
    {
        var engine=new GameEngine(db);engine.StartGame(db.PresetDecks[0],db.PresetDecks[1]);var match=engine.MatchId;var player=engine.Player;
        var d=Make(37,0,("理智",13));Assert.Throws<ArgumentException>(()=>engine.StartGame(d,db.PresetDecks[1]));Assert.Equal(match,engine.MatchId);Assert.Same(player,engine.Player);
    }
}
