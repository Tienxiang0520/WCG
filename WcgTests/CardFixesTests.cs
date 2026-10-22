using Microsoft.AspNetCore.Hosting;
using Moq;
using WcgWeb.Models;
using WcgWeb.Services;
namespace WcgTests;

// 2026-10-22: 8 cards adjusted (WCG-003 DP 0, WCG-013 4/1700, WCG-018 6/2400, WCG-019 draw 4, WCG-059 taunt <= 1800,
// WCG-067 single-will 理智, WCG-170 wipe <= 2300, WCG-201 ignore taunt). Older eras keep the old numbers and effects.
public sealed class CardFixesTests
{
    private readonly CardDatabase cards;
    public CardFixesTests()
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(x => x.ContentRootPath).Returns(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../WcgWeb")));
        cards = new(env.Object);
    }

    private GameEngine Game(CardDatabase? db = null)
    {
        db ??= cards;
        var g = new GameEngine(db, new Random(17));
        g.StartGame(db.PresetDecks[0], db.PresetDecks[1]);
        foreach (var p in new[] { g.Player, g.Computer }) { p.Hand.Clear(); p.Field.Clear(); p.EnergyZone.Clear(); }
        for (int i = 0; i < 12; i++) g.Player.EnergyZone.Add(new(db.GetCard("WCG-101")!));
        return g;
    }

    private MonsterInstance Unit(PlayerState p, string id, int slot, int? pp = null, CardDatabase? db = null)
    {
        db ??= cards;
        var card = db.GetCard(id)!;
        // PP thresholds read the printed PP, so a test monster with another PP gets its own printed copy of the card.
        if (pp != null) { card = System.Text.Json.JsonSerializer.Deserialize<CardDefinition>(System.Text.Json.JsonSerializer.Serialize(card))!; card.PP = pp.Value; }
        var m = new MonsterInstance(card) { Slot = slot, IsTapped = false, HasSummoningSickness = false };
        p.Field.Add(m);
        p.Field.Sort((a, b) => a.Slot.CompareTo(b.Slot));
        return m;
    }

    [Fact]
    public void EightCardsHaveUpdatedDefinitionsAndOldEraPreservesPriorState()
    {
        var old = CardBalanceHistory.BeforeCardFixes(cards);
        Assert.True(CardBalanceHistory.CardFixesCatalog(cards));
        Assert.False(CardBalanceHistory.CardFixesCatalog(old));

        Assert.Equal(0, cards.GetCard("WCG-003")!.DP);
        Assert.Equal(1, old.GetCard("WCG-003")!.DP);

        Assert.Equal(4, cards.GetCard("WCG-013")!.TotalCost);
        Assert.Equal(1700, cards.GetCard("WCG-013")!.PP);
        Assert.Equal(5, old.GetCard("WCG-013")!.TotalCost);
        Assert.Equal(2000, old.GetCard("WCG-013")!.PP);

        Assert.Equal(6, cards.GetCard("WCG-018")!.TotalCost);
        Assert.Equal(2400, cards.GetCard("WCG-018")!.PP);
        Assert.Equal(5, old.GetCard("WCG-018")!.TotalCost);
        Assert.Equal(2100, old.GetCard("WCG-018")!.PP);

        Assert.Equal("你失去 2 點生命，抽 4 張牌。", cards.GetCard("WCG-019")!.Text);
        Assert.Equal("你失去 2 點生命，抽 2 張牌。", old.GetCard("WCG-019")!.Text);

        Assert.Contains("PP 1800 以下", cards.GetCard("WCG-059")!.Text);
        Assert.Contains("PP 1100 以下", old.GetCard("WCG-059")!.Text);

        Assert.False(cards.GetCard("WCG-067")!.IsDual);
        Assert.Equal("理智", cards.GetCard("WCG-067")!.Will);
        Assert.True(old.GetCard("WCG-067")!.IsDual);

        Assert.Contains("PP 2300 以下", cards.GetCard("WCG-170")!.Text);
        Assert.Contains("PP 1500 以下", old.GetCard("WCG-170")!.Text);

        Assert.Equal("此怪物攻擊時可以無視嘲諷。", cards.GetCard("WCG-201")!.Text);
        Assert.Equal("此怪物攻擊玩家時 DP +1。", old.GetCard("WCG-201")!.Text);

        // 1-4 cost curve and threshold updates
        Assert.Equal(1000, cards.GetCard("WCG-101")!.PP);
        Assert.Equal(700, old.GetCard("WCG-101")!.PP);
        Assert.Equal(900, cards.GetCard("WCG-081")!.PP);
        Assert.Equal(1900, cards.GetCard("WCG-114")!.PP);
        Assert.Equal(2000, old.GetCard("WCG-114")!.PP);
        Assert.Equal(3, cards.GetCard("WCG-144")!.TotalCost);
        Assert.Equal(1300, cards.GetCard("WCG-144")!.PP);
        Assert.Equal(3, cards.GetCard("WCG-190")!.TotalCost);
        Assert.Equal(1300, cards.GetCard("WCG-190")!.PP);
        Assert.Equal(2, cards.GetCard("WCG-068")!.TotalCost);
        Assert.Equal(2, cards.GetCard("WCG-082")!.TotalCost);
        Assert.Equal(1900, cards.GetCard("WCG-117")!.PP);
        Assert.Equal(2100, cards.GetCard("WCG-262")!.PP);
    }

    [Fact]
    public void BloodcallDrawsFourCardsNowAndTwoInOldEra()
    {
        var g = Game();
        var spell = new CardInstance(cards.GetCard("WCG-019")!);
        g.Player.Hand.Add(spell);
        Assert.True(g.CastSpell(g.Player, spell));
        Assert.Equal(4, g.Player.Hand.Count);

        var old = CardBalanceHistory.BeforeCardFixes(cards);
        var gOld = Game(old);
        var oldSpell = new CardInstance(old.GetCard("WCG-019")!);
        gOld.Player.Hand.Add(oldSpell);
        Assert.True(gOld.CastSpell(gOld.Player, oldSpell));
        Assert.Equal(2, gOld.Player.Hand.Count);
    }

    [Fact]
    public void FlamebreakerIgnoresTauntAndDealsOneDp()
    {
        var g = Game();
        var breaker = Unit(g.Player, "WCG-201", 0);
        var taunt = Unit(g.Computer, "WCG-113", 0, 1900); // 1900 PP Taunt
        var hp = g.Computer.Hp;
        Assert.True(g.CanAttackPlayer(breaker));
        Assert.Equal(1, g.PreviewAttack(breaker)!.PlayerDamage);
        Assert.True(g.Attack(g.Player, breaker));
        Assert.Equal(hp - 1, g.Computer.Hp);
        Assert.Contains(taunt, g.Computer.Field);
    }

    [Fact]
    public void GaiaTyrantBypassesTauntUpTo1800AndSkyfireClearsUpTo2300()
    {
        var g = Game();
        var gaia = Unit(g.Player, "WCG-059", 0);
        var taunt1800 = Unit(g.Computer, "WCG-113", 0, 1800);
        Assert.True(g.CanAttackPlayer(gaia));
        var taunt1900 = Unit(g.Computer, "WCG-113", 1, 1900);
        Assert.False(g.CanAttackPlayer(gaia));

        var skyfire = new CardInstance(cards.GetCard("WCG-170")!);
        g.Player.Hand.Add(skyfire);
        var big2300 = Unit(g.Computer, "WCG-159", 2, 2300);
        var big2400 = Unit(g.Computer, "WCG-119", 3, 2400);
        Assert.True(g.CastSpell(g.Player, skyfire));
        Assert.DoesNotContain(taunt1800, g.Computer.Field);
        Assert.DoesNotContain(taunt1900, g.Computer.Field);
        Assert.DoesNotContain(big2300, g.Computer.Field);
        Assert.Contains(big2400, g.Computer.Field);
    }

    // From the 2026-10-22 catalog, PP thresholds check the printed PP only; bonuses and penalties don't move a monster across them.
    [Fact]
    public void PpThresholdsIgnoreBonusesAndOldEraStillCountsThem()
    {
        var g = Game();
        Assert.True(g.PrintedPpThresholds);
        var buffed = Unit(g.Computer, "WCG-101", 0); buffed.CurrentPP = 1500;   // printed 1000, +500
        var weakened = Unit(g.Computer, "WCG-055", 1); weakened.CurrentPP = 1100; // printed 1900, -800
        var strangle = new CardInstance(cards.GetCard("WCG-246")!); g.Player.Hand.Add(strangle);
        Assert.False(g.CastSpellAt(g.Player, strangle, weakened));
        Assert.True(g.CastSpellAt(g.Player, strangle, buffed));
        Assert.DoesNotContain(buffed, g.Computer.Field);

        var gaia = Unit(g.Player, "WCG-059", 0);
        var taunt = Unit(g.Computer, "WCG-113", 2, 1800); taunt.CurrentPP = 2500;
        Assert.True(g.CanAttackPlayer(gaia));

        var old = CardBalanceHistory.BeforeCardFixes(cards);
        var o = Game(old);
        Assert.False(o.PrintedPpThresholds);
        var oldBuffed = Unit(o.Computer, "WCG-101", 0, db: old); oldBuffed.CurrentPP = 1500;
        var oldStrangle = new CardInstance(old.GetCard("WCG-246")!); o.Player.Hand.Add(oldStrangle);
        Assert.False(o.CastSpellAt(o.Player, oldStrangle, oldBuffed));
    }

    // From the 2026-10-22 catalog, 248's "PP 比它低" and the "PP 最低" picks compare printed PP; older catalogs keep current PP.
    [Fact]
    public void StampedeAndLowestPpPicksUsePrintedPpAndOldEraUsesCurrent()
    {
        var nature = cards.AllCards.First(c => c.IsMonster && c.HasWill("生機")).Id;
        var g = Game();
        var mine = Unit(g.Player, nature, 0, 1000); mine.CurrentPP = 3000;      // printed 1000, buffed to 3000
        var big = Unit(g.Computer, "WCG-101", 0, 1500); big.CurrentPP = 900;    // printed 1500, weakened to 900
        var small = Unit(g.Computer, "WCG-101", 1, 800); small.CurrentPP = 2000; // printed 800, buffed to 2000
        var stomp = new CardInstance(cards.GetCard("WCG-248")!); g.Player.Hand.Add(stomp);
        Assert.True(g.CastSpellAt(g.Player, stomp, mine));
        var target = g.CurrentPendingTarget!;
        Assert.False(target.Validator!(big));
        Assert.True(g.SelectTarget(small));
        Assert.DoesNotContain(small, g.Computer.Field);
        Assert.Contains(big, g.Computer.Field);

        var pick = typeof(GameEngine).GetMethod("PickLowest", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var g2 = Game();
        var lowPrinted = Unit(g2.Computer, "WCG-101", 0, 800); lowPrinted.CurrentPP = 2500;
        var lowCurrent = Unit(g2.Computer, "WCG-101", 1, 1500); lowCurrent.CurrentPP = 500;
        pick.Invoke(g2, [g2.Player, g2.Computer.Field.ToList(), new Action<MonsterInstance>(_ => { })]);
        Assert.True(g2.CurrentPendingTarget!.Validator!(lowPrinted));
        Assert.False(g2.CurrentPendingTarget!.Validator!(lowCurrent));

        var old = CardBalanceHistory.BeforeCardFixes(cards);
        var o = Game(old);
        var oPrinted = Unit(o.Computer, "WCG-101", 0, 800, old); oPrinted.CurrentPP = 2500;
        var oCurrent = Unit(o.Computer, "WCG-101", 1, 1500, old); oCurrent.CurrentPP = 500;
        pick.Invoke(o, [o.Player, o.Computer.Field.ToList(), new Action<MonsterInstance>(_ => { })]);
        Assert.True(o.CurrentPendingTarget!.Validator!(oCurrent));
        Assert.False(o.CurrentPendingTarget!.Validator!(oPrinted));
    }
}
