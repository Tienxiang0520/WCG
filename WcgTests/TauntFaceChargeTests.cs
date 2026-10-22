using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Services;
namespace WcgTests;

// 2026-10-17: taunt+shield cards sit about 100 below the taunt standard, 狂怒 cards that damage the enemy player directly
// cost 1 more, and 衝鋒 monsters start at cost 3. Monsters keep their offset below vanilla; older eras keep the old numbers.
public sealed class TauntFaceChargeTests
{
    private static readonly Dictionary<int,int> Vanilla=new(){[1]=700,[2]=1300,[3]=1600,[4]=2000,[5]=2300,[6]=2600,[7]=2900,[8]=3200,[9]=3500};
    private readonly CardDatabase cards;
    public TauntFaceChargeTests()
    {
        var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));
        cards=new(env.Object);
    }
    [Theory]
    [InlineData("WCG-031",1100,700)][InlineData("WCG-069",1400,1000)][InlineData("WCG-073",1800,1600)]
    [InlineData("WCG-079",2400,2000)][InlineData("WCG-171",2600,2300)][InlineData("WCG-179",2800,2700)]
    public void TauntShieldCardsDropPpOnly(string id,int oldPp,int newPp)
    {
        var now=CardBalanceHistory.BeforeCardFixes(cards).GetCard(id)!;var old=CardBalanceHistory.BeforeTauntFaceCharge(cards).GetCard(id)!;
        Assert.True(now.HasTaunt);Assert.Equal(newPp,now.PP);Assert.Equal(oldPp,old.PP);
        Assert.Equal(old.TotalCost,now.TotalCost);Assert.Equal(old.Text,now.Text);Assert.Equal(old.DP,now.DP);
        Assert.True(now.PP<=Vanilla[now.TotalCost]-400);
    }
    [Theory]
    [InlineData("WCG-002",2,3)][InlineData("WCG-003",1,2)][InlineData("WCG-005",2,3)][InlineData("WCG-006",1,2)][InlineData("WCG-014",1,2)]
    [InlineData("WCG-017",3,4)][InlineData("WCG-124",3,4)][InlineData("WCG-144",2,3)][InlineData("WCG-170",6,7)][InlineData("WCG-195",2,3)]
    [InlineData("WCG-239",2,3)][InlineData("WCG-243",3,4)][InlineData("WCG-244",3,4)]
    public void CostUpKeepsTheOffsetBelowVanillaAndOldErasKeepOldNumbers(string id,int oldCost,int newCost)
    {
        var now=CardBalanceHistory.BeforeBurnCost(cards).GetCard(id)!;var old=CardBalanceHistory.BeforeTauntFaceCharge(cards).GetCard(id)!;
        Assert.Equal(newCost,now.TotalCost);Assert.Equal(oldCost,old.TotalCost);
        Assert.Equal(old.Text,now.Text);Assert.Equal(old.DP,now.DP);
        if(now.PP is int pp){Assert.Equal(Vanilla[oldCost]-old.PP!.Value,Vanilla[newCost]-pp);Assert.True(pp<Vanilla[newCost]);}
    }
    [Fact]
    public void ChargeMonstersCostAtLeastThree()
    {
        Assert.All(cards.AllCards.Where(c=>c.Keywords.Contains("衝鋒")),c=>Assert.True(c.TotalCost>=3,c.Id));
        Assert.True(CardBalanceHistory.TauntFaceChargeCatalog(cards));
        Assert.False(CardBalanceHistory.TauntFaceChargeCatalog(CardBalanceHistory.BeforeTauntFaceCharge(cards)));
        Assert.Equal(cards.AllCards.Count,CardBalanceHistory.BeforeTauntFaceCharge(cards).AllCards.Count);
    }
}
