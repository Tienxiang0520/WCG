using LcgWeb.Models;
using LcgWeb.Services;
using Microsoft.AspNetCore.Hosting;
using Moq;

namespace LcgTests;

public class PowerScaleTests
{
    readonly CardDatabase cards;
    readonly GameEngine engine;
    public PowerScaleTests()
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(x => x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../LcgWeb")));
        cards = new(env.Object); engine = new(cards, new Random(4));
        engine.StartGame(cards.PresetDecks[0], cards.PresetDecks[1]);
        engine.Player.Hand.Clear(); engine.Computer.Hand.Clear(); engine.Computer.IsAi = false;
        for (int i = 0; i < 15; i++) engine.Player.EnergyZone.Add(new(cards.GetCard("LCG-101")!));
    }
    MonsterInstance Monster(PlayerState side, string id, int pp)
    {
        var m = new MonsterInstance(cards.GetCard(id)!) { CurrentPP = pp, HasSummoningSickness = false };
        side.Field.Add(m); return m;
    }
    CardInstance Hand(string id)
    {
        var c = new CardInstance(cards.GetCard(id)!); engine.Player.Hand.Add(c); return c;
    }

    [Fact] public void CatalogUses25PointStepsAndExpectedRange()
    {
        var monsters = cards.AllCards.Where(c => c.IsMonster).ToArray();
        Assert.Equal(75, monsters.Length);
        Assert.Equal(500, monsters.Min(c => c.PP)); Assert.Equal(4000, monsters.Max(c => c.PP));
        Assert.All(monsters, c => Assert.Equal(0, c.PP % 25));
        Assert.Equal(500, cards.GetCard("LCG-003")!.PP);
        Assert.Equal(675, cards.GetCard("LCG-101")!.PP);
        Assert.Equal(825, cards.GetCard("LCG-005")!.PP);
    }

    [Theory]
    [InlineData("LCG-004", 500, false)]
    [InlineData("LCG-002", 1000, false)]
    [InlineData("LCG-032", 1675, false)]
    [InlineData("LCG-082", 1325, false)]
    [InlineData("LCG-076", 1675, true)]
    public void DirectSpellBoundaryUsesNewScale(string id, int limit, bool minimum)
    {
        var allowed = Monster(engine.Computer, "LCG-101", limit);
        var outside = Monster(engine.Computer, "LCG-101", limit + (minimum ? -25 : 25));
        var spell = Hand(id); var energy = engine.Player.AvailableEnergy;
        Assert.False(engine.CastSpellAt(engine.Player, spell, outside));
        Assert.Contains(spell, engine.Player.Hand); Assert.Equal(energy, engine.Player.AvailableEnergy);
        Assert.True(engine.CastSpellAt(engine.Player, spell, allowed));
        Assert.DoesNotContain(allowed, engine.Computer.Field); Assert.Contains(outside, engine.Computer.Field);
    }

    [Theory]
    [InlineData("LCG-068", 675)]
    [InlineData("LCG-078", 675)]
    [InlineData("LCG-092", 825)]
    public void AreaRemovalStopsAtScaledBoundary(string id, int limit)
    {
        var allowed = Monster(engine.Computer, "LCG-101", limit);
        var outside = Monster(engine.Computer, "LCG-101", limit + 25);
        Assert.True(engine.CastSpell(engine.Player, Hand(id)));
        Assert.DoesNotContain(allowed, engine.Computer.Field); Assert.Contains(outside, engine.Computer.Field);
    }

    [Theory]
    [InlineData(1, 500)] [InlineData(2, 1000)] [InlineData(3, 1500)]
    [InlineData(4, 2000)] [InlineData(5, 2500)]
    public void BearRoarUsesPrinted500PerFriendlyMonster(int count, int limit)
    {
        for (int i = 0; i < count; i++) Monster(engine.Player, "LCG-101", 675);
        var allowed = Monster(engine.Computer, "LCG-101", limit);
        var outside = Monster(engine.Computer, "LCG-101", limit + 25);
        var spell = Hand("LCG-048");
        Assert.Contains("× 500", spell.Card.Text);
        Assert.Equal(3, spell.Card.TotalCost);
        var energy = engine.Player.AvailableEnergy; var deck = engine.Player.Deck.Count;
        Assert.False(engine.CastSpellAt(engine.Player, spell, outside));
        Assert.True(engine.CastSpellAt(engine.Player, spell, allowed));
        Assert.Contains(outside, engine.Computer.Field); Assert.DoesNotContain(allowed, engine.Computer.Field);
        Assert.Equal(energy - 3, engine.Player.AvailableEnergy);
        Assert.Equal(deck - 1, engine.Player.Deck.Count);
        Assert.Single(engine.Player.Hand);
    }

    [Theory] [InlineData(1500, 1)] [InlineData(1525, 0)]
    public void TrampleRequires675Advantage(int defenderPp, int damage)
    {
        var a = Monster(engine.Player, "LCG-053", 2175);
        var d = Monster(engine.Computer, "LCG-101", defenderPp);
        Assert.Contains("675", a.Card.Text);
        Assert.Equal(damage, engine.PreviewAttack(a, d)!.PlayerDamage);
        Assert.True(engine.Attack(engine.Player, a, d)); Assert.Equal(7 - damage, engine.Computer.Hp);
    }

    [Theory] [InlineData(1325, true)] [InlineData(1350, false)]
    public void TyrantIgnoresTauntOnlyThrough1325(int pp, bool canHitPlayer)
    {
        var a = Monster(engine.Player, "LCG-059", 3675);
        Monster(engine.Computer, "LCG-081", pp);
        Assert.Equal(canHitPlayer, engine.CanAttackPlayer(a));
    }
}
