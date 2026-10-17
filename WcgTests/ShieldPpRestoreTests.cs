using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Services;
namespace WcgTests;

// 2026-10-17: 聖盾 is a one-time counter, so the extra PP cut made for the per-turn shield is removed (only that part).
public sealed class ShieldPpRestoreTests
{
    private static readonly Dictionary<int,int> Vanilla=new(){[1]=700,[2]=1300,[3]=1600,[4]=2000,[5]=2300,[6]=2600,[7]=2900,[8]=3200,[9]=3500};
    private readonly CardDatabase cards;
    public ShieldPpRestoreTests()
    {
        var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));
        cards=new(env.Object);
    }
    [Theory]
    [InlineData("WCG-031",800,1100)][InlineData("WCG-061",700,900)][InlineData("WCG-063",300,500)][InlineData("WCG-069",1100,1400)]
    [InlineData("WCG-073",1600,1800)][InlineData("WCG-075",1800,2100)][InlineData("WCG-079",2100,2400)][InlineData("WCG-080",2100,2400)]
    [InlineData("WCG-147",800,900)][InlineData("WCG-171",2300,2600)][InlineData("WCG-179",2500,2800)]
    public void ShieldCardsLoseOnlyTheShieldCutAndOldErasKeepOldPp(string id,int oldPp,int newPp)
    {
        var now=cards.GetCard(id)!;var old=CardBalanceHistory.BeforeShieldPp(cards).GetCard(id)!;
        Assert.Equal(newPp,now.PP);Assert.Equal(oldPp,old.PP);
        Assert.Equal(old.Text,now.Text);Assert.Equal(old.DP,now.DP);Assert.Equal(old.TotalCost,now.TotalCost);
        // Ability monsters stay at least 200 below vanilla, which keeps taunt combinations under the 100-below pure-taunt cards.
        Assert.True(now.PP<=Vanilla[now.TotalCost]-200);
    }
    [Fact]
    public void EraIsDetectedOnlyOnTheCurrentCatalog()
    {
        Assert.True(CardBalanceHistory.ShieldPpCatalog(cards));
        Assert.False(CardBalanceHistory.ShieldPpCatalog(CardBalanceHistory.BeforeShieldPp(cards)));
        Assert.False(CardBalanceHistory.ShieldPpCatalog(CardBalanceHistory.BeforeDeflation(cards)));
    }
}
