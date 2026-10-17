using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Services;
namespace WcgTests;

// 2026-10-17: 焦土縱火犯, 地獄戰狂, 血怒百夫長 and 深淵之主·瑪爾加尼斯 cost 1 more; PP moved up the vanilla curve by the same offset.
public sealed class CostUpCardTests
{
    private static readonly Dictionary<int,int> Vanilla=new(){[1]=700,[2]=1300,[3]=1600,[4]=2000,[5]=2300,[6]=2600,[7]=2900,[8]=3200,[9]=3500};
    private readonly CardDatabase cards;
    public CostUpCardTests()
    {
        var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));
        cards=new(env.Object);
    }
    [Theory]
    [InlineData("WCG-013",3,1300,4,1700)]
    [InlineData("WCG-018",4,1800,5,2100)]
    [InlineData("WCG-100",6,2400,7,2700)]
    [InlineData("WCG-182",4,1600,5,1900)]
    public void CostUpKeepsTheOffsetBelowVanillaAndOldErasKeepOldNumbers(string id,int oldCost,int oldPp,int newCost,int newPp)
    {
        var now=cards.GetCard(id)!;var old=CardBalanceHistory.BeforeCostUp(cards).GetCard(id)!;
        Assert.Equal(newCost,now.TotalCost);Assert.Equal(newPp,now.PP);
        Assert.Equal(oldCost,old.TotalCost);Assert.Equal(oldPp,old.PP);
        Assert.Equal(oldPp-Vanilla[oldCost],newPp-Vanilla[newCost]);Assert.True(now.PP<Vanilla[newCost]);
        Assert.Equal(old.Text,now.Text);Assert.Equal(old.DP,now.DP);
        Assert.True(CardBalanceHistory.CostUpCatalog(cards));Assert.False(CardBalanceHistory.CostUpCatalog(CardBalanceHistory.BeforeCostUp(cards)));
    }
}
