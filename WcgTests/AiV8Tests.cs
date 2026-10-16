using WcgWeb.Models;
using WcgWeb.Models.Ranked;
using WcgWeb.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
namespace WcgTests;

// Ranked AI v8 (GameEngine.AiV7.cs): no random blunders at Diamond+, no pointless attacks into taunts,
// draw moves priced over sampled draws, shortest lethal first. Positions come from a real Diamond match code.
public sealed class AiV8Tests : IDisposable
{
    // Diamond, 生機・巨獸奔襲 (player) vs 理智＋秩序・冰封堡壘 (computer, v7), 2026-10-16.
    const string DiamondMatch="WCG1-eNrVl9tO3DAQhl-l8iVKJHs8PqVXOVaoFVRL1d7sTZJNWgSCaksqIcS7d2ygCyQqgVbLVlr9a_3xYT7bcTxX7DNLRMQWLGHr-uykW8U_uU5yVLrKXGWLssxsnqXClRlQIROpLsFqU7oyt0bkCEIZUMrl0moo0KVOp4pF7BNLMGJH1Lu1QiCAUxGrWNLXpz-6iH1kyRU7oFGXg1GiWg7UilTyKlsOqigt-dKQKqdwOVhngDr98qgBWftk9Va7znCMFW_aGBvXxo21EPdGQl3Lvl41vnVOVTmKPYw4YlDnVcmguAekKpRNUOcdzUkFaK-oyRfhqdDgHWt92ZHDriN2eA8KuaYYtUmXQ1VxgjKudIRT2g2myDhpzgWphaCFDfVJsYRiOTjuWclxG_zfXd_iL9KD92URY7wo06PDg_jdhzTfLxd3xBBYAwEHEzRwyzAT0nNwKYMGennDHWZFB0f7-oJ7X8hAbzFwU5-eO6Vh6uMoFpufefsCozvr1l8vI3zorf6-49lDq1cYelYs30_ry-hBrfbb-XHbbRx4xYhnM5kRk95VpqnNGCJ-sC71xUXdnkSc1ma7u-bxVPFnMcB_s5NCuPLpcAOo-Oev723HMAqJPxXStnbGeE9ORfP0NN6jlbtwCr50KnBi-cTzXoAdx77ZWD5MOTqE5FxGPuujwrc4EzcQuHlfbh05cmDkiK2fv3-KH0bxwyh-OWMxd4NoPNs4p5mc3LNTn84dOGzGJ8fk93UKf0xBF-90Ha7q0zd8f5M_P2PJxXqgTChjibbUoqd_R5lYF1pqA7lviTwkQkBlKAqfCYj0LgNSaHyGoJHUikq-4T7vGlgiOWVevh-ga38svL2_8nd028AKV01srOxiNLqPbd3zuHHKYNt1rWkUu_4Foq463g";
    readonly string dir=Path.Combine(Path.GetTempPath(),"wcg-v8-test-"+Guid.NewGuid());
    readonly IWebHostEnvironment env;readonly CardDatabase cards;
    public AiV8Tests(){var mock=new Mock<IWebHostEnvironment>();mock.Setup(x=>x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb")));env=mock.Object;cards=new(env);}
    public void Dispose(){if(Directory.Exists(dir))Directory.Delete(dir,true);}
    GameEngine MatchAt(int actions)
    {
        var store=new RankedStore(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Ranked:SavePath"]=Path.Combine(dir,"ranked.json")}).Build());
        var s=new RankedSession(cards,env,store,new RankedDecks(cards,env),NullLogger<BattleCoordinator>.Instance,TimeProvider.System);
        var record=MatchCode.Decode(DiamondMatch,out var error,out _)!;Assert.Equal("",error);
        return s.ReplayEngine(record,actions)!;
    }
    static List<string> Turn(GameEngine g,int revision)
    {
        var trace=new List<string>();g.AiTrace=trace.Add;g.AiRevision=revision;
        for(int i=0;i<60&&!g.IsOver&&g.DecisionPlayerId=="computer";i++)Assert.True(g.ExecuteAiStep(),g.LastError);
        return trace;
    }
    static double Chosen(string searchLine)=>double.Parse(searchLine.Split("chosen ")[1].Split(':')[0],System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void RankedAiVersionIsEight()=>Assert.Equal(8,GameEngine.RankedAiVersion);

    [Fact]
    public void TheMatchCodeStillReplaysWithItsRecordedV7Steps()
    {
        var g=MatchAt(int.MaxValue);Assert.True(g.IsOver);Assert.True(g.Computer.HasLost);
    }

    // Turn 15: v7 planned a "win" that needed one lucky simulated draw (奧術共鳴波 drawing 空間回折) and spent its
    // removal on it. v8 prices the draw over several samples and does not plan past it.
    [Fact]
    public void TurnFifteenNoLongerPlansOnALuckyDraw()
    {
        var v7=Turn(MatchAt(67),7).First(l=>l.StartsWith("search:"));Assert.True(Chosen(v7)>=1e6,v7);
        var v8=Turn(MatchAt(67),8).Where(l=>l.StartsWith("search:")).ToList();Assert.NotEmpty(v8);
        Assert.All(v8,l=>Assert.True(Chosen(l)<1e5,l));
    }

    // Turn 19: the Diamond AI rolled a random "blunder" and threw 萬象觀測者 (1800) into a 3100 taunt.
    [Fact]
    public void DiamondNeverThrowsAMonsterAwayAtTurnNineteen()
    {
        int Observers(GameEngine x)=>x.Computer.Graveyard.Count(c=>c.Card.Name=="萬象觀測者");
        var v7=MatchAt(92);var before=Observers(v7);var t7=Turn(v7,7);Assert.Contains(t7,l=>l.StartsWith("blunder:"));Assert.Equal(before+1,Observers(v7));
        var v8=MatchAt(92);var t8=Turn(v8,8);Assert.DoesNotContain(t8,l=>l.StartsWith("blunder:"));Assert.Equal(before,Observers(v8));
    }

    [Fact]
    public void DiamondAndMasterHaveNoRandomBlundersInV8()
    {
        var g=new GameEngine(cards,new Random(1)){AiRevision=8};
        foreach(var level in new[]{4,5}){g.AiLevel=level;Assert.Equal(0,g.ProfileForDecision().Blunder);}
        g.AiLevel=3;Assert.True(g.ProfileForDecision().Blunder>0);g.AiRevision=7;g.AiLevel=4;Assert.True(g.ProfileForDecision().Blunder>0);
    }

    GameEngine Board(int computerHp,int level=4)
    {
        var g=new GameEngine(cards,new Random(17)){AiLevel=level,AiRevision=8};g.SeedRankedAi(17);g.StartGame(cards.PresetDecks[0],cards.PresetDecks[1]);
        g.Player.IsAi=true;g.Player.Hand.Clear();g.Computer.Hand.Clear();g.Player.Field.Clear();g.Computer.Field.Clear();g.Player.Structures.Clear();g.Computer.Structures.Clear();
        g.Player.EnergyZone.Clear();for(int i=0;i<8;i++)g.Player.EnergyZone.Add(new(cards.GetCard("WCG-101")!));g.Player.HasFilledEnergyThisTurn=true;g.Computer.Hp=computerHp;
        int slot=0;foreach(var id in new[]{"WCG-101","WCG-154","WCG-114"})g.Player.Field.Add(new MonsterInstance(cards.GetCard(id)!){Slot=slot++,IsTapped=false,HasSummoningSickness=false});
        g.Computer.Field.Add(new MonsterInstance(cards.GetCard("WCG-146")!){Slot=0}); // 荊棘巨龜 1500, taunt
        return g;
    }
    static void PlayerTurn(GameEngine g){for(int i=0;i<60&&!g.IsOver&&g.DecisionPlayerId=="player";i++)if(!g.ExecuteAiStep())break;}

    // 700 and 1300 cannot beat a 1500 taunt; the 2000 can. The lethal must use the big monster on the taunt,
    // not first feed the small ones to it.
    [Theory] [InlineData(4)] [InlineData(5)]
    public void LethalUsesTheBigMonsterOnTheTauntInsteadOfSuicidingSmallOnes(int level)
    {
        var old=Board(1,level);old.AiRevision=7;var v7=old.FindLethal(old.Player,old.ProfileForDecision())!;
        Assert.NotEqual("雪人巨獸",old.Player.Field.Single(m=>m.InstanceId==v7.Source).Card.Name); // v7 fed a small monster first
        var g=Board(1,level);
        var first=g.FindLethal(g.Player,g.ProfileForDecision())!;Assert.NotNull(first);
        Assert.Equal("雪人巨獸",g.Player.Field.Single(m=>m.InstanceId==first.Source).Card.Name);
        PlayerTurn(g);Assert.True(g.Computer.HasLost);Assert.Equal(3,g.Player.Field.Count);
    }

    [Theory] [InlineData(4)] [InlineData(5)]
    public void BigMonsterClearsTheTauntAndSmallOnesHitFace(int level)
    {
        var g=Board(7,level);PlayerTurn(g);
        Assert.Empty(g.Computer.Field);Assert.Equal(3,g.Player.Field.Count);Assert.Equal(5,g.Computer.Hp);
    }
}
