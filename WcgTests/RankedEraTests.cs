using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WcgWeb.Models;
using WcgWeb.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
namespace WcgTests;

// Old catalogs are built on demand, one history step at a time; they must equal the full Before* chains exactly,
// in the same order and with the same engine flags, so every saved match and 對局代碼 replays as before.
public sealed class RankedEraTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "wcg-era-test-" + Guid.NewGuid());
    readonly IWebHostEnvironment env;
    readonly CardDatabase cards;
    public RankedEraTests()
    {
        var mock = new Mock<IWebHostEnvironment>(); mock.Setup(x => x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../WcgWeb")));
        env = mock.Object; cards = new(env);
    }
    public void Dispose() { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    RankedSession Session() => new(cards, env, new RankedStore(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Ranked:SavePath"] = Path.Combine(dir, "ranked.json") }).Build()),
        new RankedDecks(cards, env), NullLogger<BattleCoordinator>.Instance);
    static string Fingerprint(CardDatabase db, bool legacyIds = false)
    {
        var json = JsonSerializer.Serialize(db.AllCards);
        if (legacyIds) json = json.Replace(CardIdentifier.CurrentPrefix, CardIdentifier.LegacyPrefix, StringComparison.Ordinal);
        return "ranked-v06:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
    [Fact]
    public void LazyErasMatchTheFullHistoryChains()
    {
        Func<CardDatabase, CardDatabase>[] chains =
        [
            CardBalanceHistory.BeforeDeckCards, CardBalanceHistory.BeforeIronhideDp, CardBalanceHistory.BeforeFateFlipCards, CardBalanceHistory.BeforeStarCountdownCards, CardBalanceHistory.BeforeHolySpringCards, CardBalanceHistory.BeforeBloodPuppeteerText, CardBalanceHistory.BeforeSurgeStrikeCards, CardBalanceHistory.BeforeRotMireCards, CardBalanceHistory.BeforeTideCycleCards, CardBalanceHistory.BeforeRevivalLordText, CardBalanceHistory.BeforeBloodOathCards, CardBalanceHistory.BeforeRootWebCards, CardBalanceHistory.BeforeGraveOathCards, CardBalanceHistory.BeforeSilenceCounters, CardBalanceHistory.BeforeArrowText, CardBalanceHistory.BeforeSoulPuppetCards, CardBalanceHistory.BeforeLaneRaidCards, CardBalanceHistory.BeforeHoofCrushCards, CardBalanceHistory.BeforeEnergyBurnCards, CardBalanceHistory.BeforeEnergyVaultCards, CardBalanceHistory.BeforeBeaconWatchCards, CardBalanceHistory.BeforeArenaAnyEnemy, CardBalanceHistory.BeforeRotErosionCards, CardBalanceHistory.BeforeThresholdGapCards, CardBalanceHistory.BeforeOathDuelCards, CardBalanceHistory.BeforePrintedPpText, CardBalanceHistory.BeforeShieldAnyCombat, CardBalanceHistory.BeforeBearRoarText, CardBalanceHistory.BeforeCardFixes, CardBalanceHistory.BeforeBurnCost, CardBalanceHistory.BeforeTauntFaceCharge, CardBalanceHistory.BeforeLuckyCoin, CardBalanceHistory.BeforeMageDirect, CardBalanceHistory.BeforeShieldPp, CardBalanceHistory.BeforeCostUp, CardBalanceHistory.BeforeShieldCounter, CardBalanceHistory.BeforeReasonBuff, CardBalanceHistory.BeforeDualWill, CardBalanceHistory.BeforeSilenceSpell,
            CardBalanceHistory.BeforeAbilityPp, CardBalanceHistory.BeforeNewCards, CardBalanceHistory.BeforeDeflation, CardBalanceHistory.BeforeRulesSimplification,
            CardBalanceHistory.BeforeEnergyKeywords, CardBalanceHistory.BeforeHeroGuard, CardBalanceHistory.BeforeStrongBuildAdjustment, CardBalanceHistory.BeforeOctoberBalance,
            CardBalanceHistory.BeforeEnergyShield, CardBalanceHistory.BeforeDamageCounters, CardBalanceHistory.BeforePlayerTargets, CardBalanceHistory.BeforeDiversity,
            CardBalanceHistory.BeforeCardText, CardBalanceHistory.BeforeArrowCards, CardBalanceHistory.BeforeUnlimitedFactionTriggers
        ];
        var eras = Session().AllPriorEras();
        Assert.Equal(chains.Length, eras.Count);
        Assert.Equal(CardBalanceHistory.EraSteps.Length, eras.Count);
        for (int i = 0; i < chains.Length; i++)
        {
            var expected = chains[i](cards);
            Assert.Equal(Fingerprint(expected), eras[i].Rules);
            Assert.Equal(Fingerprint(expected, true), eras[i].IdentifierRules);
            Assert.Equal(i == 53, eras[i].LimitFactionTriggers);
            Assert.Equal(i >= 51, eras[i].LegacyCardRules);
            // Retired: the 35 integer-counter catalogs (聖盾指示物 up to the 2026-11-12 deck-card update) and the two catalogs that
            // still carried the removed 怒擊/野性/觀測/回魂/護主 keywords.
            Assert.Equal(i <= 34 || i is 42 or 43, eras[i].Retired);
            if (i == 35) Assert.False(CardBalanceHistory.ShieldCounterCatalog(expected)); // the newest era still playable predates 聖盾指示物
            if (i == 34) Assert.True(CardBalanceHistory.ShieldCounterCatalog(expected));
            Assert.Equal(i is 42 or 43, expected.AllCards.Any(c => c.Keywords.Intersect(["怒擊", "野性", "觀測", "回魂", "護主"]).Any()));
        }
    }
}
