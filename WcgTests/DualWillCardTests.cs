using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;

namespace WcgTests;

// 2026-10-16: seven single-will cards became dual-will; old catalogs keep the single wills.
public class DualWillCardTests
{
    private readonly CardDatabase cards;
    public DualWillCardTests(){var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));cards=new(env.Object);}
    public static TheoryData<string,string,string,string> Changed=>new(){
        {"WCG-234","生機","生機","深淵"},{"WCG-031","理智","理智","秩序"},{"WCG-027","理智","理智","生機"},{"WCG-058","生機","生機","深淵"},
        {"WCG-067","秩序","秩序","理智"},{"WCG-211","理智","理智","深淵"},{"WCG-213","理智","理智","深淵"}};
    [Theory, MemberData(nameof(Changed))]
    public void CardIsDualNowAndSingleInOldCatalog(string id,string oldWill,string a,string b)
    {
        var now=CardBalanceHistory.BeforeCardFixes(cards).GetCard(id)!;Assert.True(now.IsDual);Assert.Equal(new[]{a,b}.Order(),now.Wills.Order());
        var old=CounterHistory.BeforeDualWill(cards).GetCard(id)!;Assert.False(old.IsDual);Assert.Equal(oldWill,old.Will);
        Assert.Equal(CardBalanceHistory.BeforeShieldPp(cards).GetCard(id)!.PP,old.PP);Assert.Equal(CounterHistory.BeforeShieldCounter(cards).GetCard(id)!.Text,old.Text);Assert.Equal(now.TotalCost,old.TotalCost);
        var runtime=CardBalanceHistory.BeforeDualWill(cards).GetCard(id)!;Assert.Equal(oldWill,runtime.Will);Assert.False(runtime.IsDual);
    }
    [Fact]
    public void DualCardDoesNotFitAnotherWillPair()
    {
        // The 理智＋狂怒 preset with 鏡像護衛 back in: legal before, now three wills.
        var deck=DeckService.Copy(cards.PresetDecks.Single(d=>d.Name=="v0.6 理智＋狂怒試玩"));deck.CardIds=deck.CardIds.Select(i=>i=="WCG-025"?"WCG-031":i).ToList();
        Assert.False(deck.IsValid(cards.GetCard,out _));
        Assert.True(deck.IsValid(CardBalanceHistory.BeforeDualWill(cards).GetCard,out _));
    }
    [Fact]
    public void EveryWillKeepsAVanillaMonster()
    {
        foreach(var w in Deck.Wills)Assert.Contains(cards.AllCards,c=>c.IsMonster&&!c.IsDual&&c.Will==w&&c.Text=="無。");
    }
}
