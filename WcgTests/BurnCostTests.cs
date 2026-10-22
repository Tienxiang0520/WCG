using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Services;
namespace WcgTests;

// 2026-10-18 (scheme B): direct damage that Taunt can't stop costs more in proportion to the damage. Monsters keep their
// offset below vanilla; text and DP are unchanged; older eras keep the old numbers. 霧翼斥候 (261) and 薩弗拉斯 (178) stay.
public sealed class BurnCostTests
{
    private static readonly Dictionary<int,int> Vanilla=new(){[1]=700,[2]=1300,[3]=1600,[4]=2000,[5]=2300,[6]=2600,[7]=2900,[8]=3200,[9]=3500};
    private readonly CardDatabase cards;
    public BurnCostTests()
    {
        var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));
        cards=new(env.Object);
    }
    [Theory]
    [InlineData("WCG-006",2,4,null,null)][InlineData("WCG-014",2,3,null,null)][InlineData("WCG-013",4,5,1700,2000)]
    [InlineData("WCG-123",5,6,null,null)][InlineData("WCG-170",7,8,null,null)][InlineData("WCG-059",6,7,2400,2700)][InlineData("WCG-144",3,5,1200,1900)]
    public void DirectDamageCostsMoreAndOldErasKeepOldNumbers(string id,int oldCost,int newCost,int? oldPp,int? newPp)
    {
        var now=CardBalanceHistory.BeforeCardFixes(cards).GetCard(id)!;var old=CardBalanceHistory.BeforeBurnCost(cards).GetCard(id)!;
        Assert.Equal(newCost,now.TotalCost);Assert.Equal(oldCost,old.TotalCost);Assert.Equal(newPp,now.PP);Assert.Equal(oldPp,old.PP);
        Assert.Equal(old.Text,now.Text);Assert.Equal(old.DP,now.DP);
        if(now.PP is int pp)Assert.Equal(Vanilla[oldCost]-old.PP!.Value,Vanilla[newCost]-pp);
    }
    [Fact]
    public void OnlyTheSchemeBCardsChangedAndTheCatalogIsDetected()
    {
        var now=CardBalanceHistory.BeforeCardFixes(cards);
        var old=CardBalanceHistory.BeforeBurnCost(cards);
        var changed=now.AllCards.Where(c=>old.GetCard(c.Id)!.TotalCost!=c.TotalCost||old.GetCard(c.Id)!.PP!=c.PP).Select(c=>c.Id).OrderBy(x=>x,StringComparer.Ordinal);
        Assert.Equal(["WCG-006","WCG-013","WCG-014","WCG-059","WCG-123","WCG-144","WCG-170"],changed);
        Assert.Equal(2,now.GetCard("WCG-261")!.TotalCost);Assert.Equal(9,now.GetCard("WCG-178")!.TotalCost);
        Assert.True(CardBalanceHistory.BurnCostCatalog(now));
        Assert.False(CardBalanceHistory.BurnCostCatalog(old));
        Assert.Equal(now.AllCards.Count,old.AllCards.Count);
    }
}
