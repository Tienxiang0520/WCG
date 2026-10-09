using System.Text.Json;
using WcgWeb.Models;
using WcgWeb.Services;
using Microsoft.AspNetCore.Hosting;
using Moq;
namespace WcgTests;

// Positional arrow effects added by the arrow-card expansion (docs/history/箭頭卡牌擴充.md).
public class ArrowCardTests
{
    private readonly CardDatabase cards;
    private readonly string root;
    public ArrowCardTests(){root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../WcgWeb"));var env=new Mock<IWebHostEnvironment>();env.Setup(x=>x.ContentRootPath).Returns(root);cards=new(env.Object);}
    private GameEngine Game(CardDatabase? db=null){db??=cards;var g=new GameEngine(db,new Random(17));g.StartGame(db.PresetDecks[0],db.PresetDecks[1]);g.Player.Hand.Clear();g.Computer.Hand.Clear();g.Player.Field.Clear();g.Computer.Field.Clear();for(int i=0;i<12;i++)g.Player.EnergyZone.Add(new(db.GetCard("WCG-101")!));return g;}
    private CardInstance Hand(GameEngine g,int n,CardDatabase? db=null){var c=new CardInstance((db??cards).GetCard($"WCG-{n:000}")!);g.Player.Hand.Add(c);return c;}
    private MonsterInstance Unit(PlayerState p,int n,int slot,CardDatabase? db=null){var m=new MonsterInstance((db??cards).GetCard($"WCG-{n:000}")!){Slot=slot,IsTapped=false,HasSummoningSickness=false};p.Field.Add(m);p.Field.Sort((a,b)=>a.Slot.CompareTo(b.Slot));return m;}
    // Units placed by hand skip the engine wiring; refresh like every engine action does.
    private static void Refresh(GameEngine g)=>typeof(GameEngine).GetMethod("RefreshBoard",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(g,null);
    private static void Finish(GameEngine g,int slot){for(int i=0;i<100&&g.IsWaiting&&!g.IsOver;i++){if(g.CurrentPendingChoice is {} c){var o=c.Options.FirstOrDefault(o=>o.Id==$"SLOT:{slot}")??c.Options.FirstOrDefault(o=>o.Id=="SHIELD")??c.Options.FirstOrDefault(o=>o.Id=="KEEP")??c.Options.FirstOrDefault(o=>o.Id!="SKIP")??c.Options[0];Assert.True(g.SelectChoice(o));}else{var t=g.CurrentPendingTarget!;var m=g.Player.Board.Concat(g.Computer.Board).First(x=>t.Validator!(x));Assert.True(g.SelectTarget(m));}}Assert.False(g.IsWaiting);}
    private MonsterInstance Summon(GameEngine g,int n,int slot){var c=Hand(g,n);Assert.True(g.SummonMonster(g.Player,c));Finish(g,slot);return g.Player.Field.Single(m=>m.InstanceId==c.InstanceId);}

    [Fact] public void CatalogHasTwelveArrowCardsAndOnlyThreeShieldArrows()
    {
        var arrows=cards.AllCards.Where(c=>c.Arrows.Length>0).Select(c=>c.Id).ToArray();
        Assert.Equal(12,arrows.Length);
        Assert.Equal(["WCG-061","WCG-073","WCG-147"],arrows.Where(GameEngine.ShieldArrows));
        Assert.All(arrows,id=>Assert.Contains("【箭頭：",cards.GetCard(id)!.Text));
    }
    [Theory] [InlineData(5,200)] [InlineData(55,300)]
    public void SideArrowsBoostBothAdjacentAllies(int id,int bonus)
    {
        var g=Game();var left=Unit(g.Player,101,1);var src=Unit(g.Player,id,2);var right=Unit(g.Player,154,3);var far=Unit(g.Player,101,4);var foe=Unit(g.Computer,101,2);Refresh(g);
        Assert.Equal(700+bonus,left.CurrentPP);Assert.Equal(1500+bonus,right.CurrentPP);Assert.Equal(700,far.CurrentPP);Assert.Equal(700,foe.CurrentPP);
        Assert.Equal(src.Card.PP,src.CurrentPP);
    }
    [Fact] public void RightArrowOnlyBoostsTheRightNeighbour()
    {
        var g=Game();var left=Unit(g.Player,101,1);Unit(g.Player,157,2);var right=Unit(g.Player,101,3);Refresh(g);
        Assert.Equal(700,left.CurrentPP);Assert.Equal(1200,right.CurrentPP);
    }
    [Theory] [InlineData(85)] [InlineData(114)]
    public void UpArrowWeakensOnlyTheEnemyStraightAhead(int id)
    {
        var g=Game();Unit(g.Player,id,1);var ahead=Unit(g.Computer,154,3);var other=Unit(g.Computer,154,1);var ally=Unit(g.Player,154,2);Refresh(g);
        Assert.Equal(1200,ahead.CurrentPP);Assert.Equal(1500,other.CurrentPP);Assert.Equal(1500,ally.CurrentPP);
    }
    [Fact] public void SilenceTurnsAurasOffAndArrowCardsDoNotGrantShields()
    {
        var g=Game();var src=Unit(g.Player,5,1);var t=Unit(g.Player,101,2);Refresh(g);
        Assert.Equal(900,t.CurrentPP);Assert.False(t.HasShield);
        Assert.True(g.CastSpellAt(g.Player,Hand(g,72),src));Assert.True(src.IsSilenced);Assert.Equal(700,t.CurrentPP);
    }
    [Fact] public void AuraEndsWhenTheSourceLeaves()
    {
        var g=Game();var src=Unit(g.Player,55,1);var t=Unit(g.Player,101,0);Refresh(g);Assert.Equal(1000,t.CurrentPP);g.Player.Field.Remove(src);Refresh(g);Assert.Equal(700,t.CurrentPP);
    }
    [Theory] [InlineData(true,2)] [InlineData(false,1)]
    public void ApprenticeDrawsTwoBetweenTwoAllies(bool both,int draws)
    {
        var g=Game();Unit(g.Player,101,1);if(both)Unit(g.Player,101,3);
        Summon(g,21,2);Assert.Equal(draws,g.Player.Hand.Count);
    }
    [Fact] public void ThiefTapsTheEnemyAheadOtherwiseDraws()
    {
        var g=Game();var foe=Unit(g.Computer,119,2);Summon(g,33,2);Assert.True(foe.IsTapped);Assert.Empty(g.Player.Hand);
        g=Game();foe=Unit(g.Computer,119,1);Summon(g,33,2);Assert.Equal(2,g.Player.Field.Single().Slot);Assert.Equal(1,foe.Slot);Assert.False(foe.IsTapped,string.Join(",",g.Player.Field.Select(x=>x.Slot)));Assert.Single(g.Player.Hand);
    }
    [Fact] public void OgreSmashesASmallEnemyStraightAheadOnly()
    {
        var g=Game();var small=Unit(g.Computer,101,0);var side=Unit(g.Computer,101,1);Summon(g,156,4);
        Assert.DoesNotContain(small,g.Computer.Field);Assert.Contains(side,g.Computer.Field);
        g=Game();var big=Unit(g.Computer,154,0);Summon(g,156,4);Assert.Contains(big,g.Computer.Field);
    }
    [Fact] public void BoneBehemothDestroysTheEnemyAheadWhenItLeaves()
    {
        var g=Game();var behemoth=Unit(g.Player,159,1);var ahead=Unit(g.Computer,154,3);var brawler=Unit(g.Computer,119,0);
        Assert.True(g.Attack(g.Player,behemoth,brawler),g.LastError);Finish(g,0);
        Assert.DoesNotContain(behemoth,g.Player.Field);Assert.DoesNotContain(ahead,g.Computer.Field);Assert.Contains(brawler,g.Computer.Field);
    }
    [Fact] public void PreArrowCatalogKeepsTheOldVanillaCards()
    {
        var prior=JsonSerializer.Deserialize<List<CardDefinition>>(File.ReadAllText(Path.Combine(root,"Data/balance-before-arrow-cards.json")))!.ToDictionary(c=>c.Id);
        Assert.Equal(9,prior.Count);
        var old=new CardDatabase(JsonSerializer.Serialize(cards.AllCards.Select(c=>prior.GetValueOrDefault(c.Id,c))),JsonSerializer.Serialize(cards.PresetDecks));
        var g=Game(old);var small=Unit(g.Computer,101,2,old);var left=Unit(g.Player,101,3,old);Unit(g.Player,5,4,old);Refresh(g);
        var c=Hand(g,156,old);Assert.True(g.SummonMonster(g.Player,c));Finish(g,2);
        Assert.Contains(small,g.Computer.Field);Assert.Equal(700,left.CurrentPP);
    }
}
